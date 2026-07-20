using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.QuestionGeneration.Contracts;
using MS.Microservice.AI.QuestionGeneration.Serialization;

namespace MS.Microservice.AI.QuestionGeneration.AIChat;

public sealed class AIChatQuestionModelClient(
    IAIChatClient chatClient,
    IAIModelResolver modelResolver,
    IQuestionJsonContract jsonContract,
    IOptions<QuestionGenerationOptions> options,
    TimeProvider timeProvider,
    ILogger<AIChatQuestionModelClient> logger) : IQuestionModelClient
{
    private readonly ConcurrentDictionary<string, StructuredFormatCapability> capabilities =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<QuestionModelResult<QuestionCandidate>> DraftAsync(
        QuestionDraftRequest request,
        CancellationToken cancellationToken) =>
        CompleteAsync<QuestionCandidate>(
            request,
            options.Value.DraftScenario,
            new
            {
                blueprint = request.Blueprint,
                context = request.Context,
            },
            cancellationToken);

    public Task<QuestionModelResult<QuestionEvaluation>> ReviewAsync(
        QuestionReviewRequest request,
        CancellationToken cancellationToken) =>
        CompleteAsync<QuestionEvaluation>(
            request,
            options.Value.ReviewScenario,
            new
            {
                blueprint = request.Blueprint,
                context = request.Context,
                candidate = request.Candidate,
                validation = request.Validation,
                rubric = request.Rubric,
            },
            cancellationToken);

    public Task<QuestionModelResult<QuestionCandidate>> RepairAsync(
        QuestionRepairRequest request,
        CancellationToken cancellationToken) =>
        CompleteAsync<QuestionCandidate>(
            request,
            options.Value.RepairScenario,
            new
            {
                blueprint = request.Blueprint,
                context = request.Context,
                candidate = request.Candidate,
                issues = request.Issues,
                review = request.Evaluation,
                repairAttempt = request.RepairAttempt,
                allowedFields = request.AllowedFields,
            },
            cancellationToken);

    private async Task<QuestionModelResult<T>> CompleteAsync<T>(
        QuestionModelRequest request,
        string scenario,
        object envelope,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);

        var baseRequest = new AIChatRequest
        {
            Scenario = scenario,
            Messages =
            [
                new AIChatMessage("system", request.Prompt.SystemInstructions),
                new AIChatMessage("user", jsonContract.Serialize(envelope)),
            ],
            RequestId = $"{request.Blueprint.BlueprintId}:{request.Prompt.Id}",
        };
        var resolved = modelResolver.ResolveChatModel(baseRequest);
        var capabilityKey = $"{resolved.Provider}:{resolved.Model}";
        var capability = capabilities.GetOrAdd(
            capabilityKey,
            StructuredFormatCapability.JsonSchema);
        var startedAt = timeProvider.GetTimestamp();

        try
        {
            while (true)
            {
                var responseFormat = capability switch
                {
                    StructuredFormatCapability.JsonSchema => AIChatResponseFormat.JsonSchema(
                        request.Prompt.SchemaName,
                        jsonContract.GetStrictSchema(request.ResponseType)),
                    StructuredFormatCapability.JsonObject => AIChatResponseFormat.JsonObject,
                    StructuredFormatCapability.Text => AIChatResponseFormat.Text,
                    _ => throw new InvalidOperationException("Unknown structured output capability."),
                };
                try
                {
                    var response = await chatClient.GetResponseAsync(
                        new AIChatRequest
                        {
                            Provider = resolved.Provider,
                            Model = resolved.Model,
                            Scenario = scenario,
                            Messages = baseRequest.Messages,
                            RequestId = baseRequest.RequestId,
                            Temperature = resolved.Temperature,
                            TopP = resolved.TopP,
                            MaxOutputTokens = resolved.MaxOutputTokens,
                            Timeout = resolved.Timeout,
                            ResponseFormat = responseFormat,
                        },
                        cancellationToken).ConfigureAwait(false);
                    capabilities[capabilityKey] = capability;
                    return ParseResponse<T>(request.ResponseType, response, startedAt);
                }
                catch (AIProviderException exception)
                    when (exception.ErrorCode == AIErrorCodes.UnsupportedResponseFormat &&
                          capability != StructuredFormatCapability.Text)
                {
                    capability = capability == StructuredFormatCapability.JsonSchema
                        ? StructuredFormatCapability.JsonObject
                        : StructuredFormatCapability.Text;
                    capabilities[capabilityKey] = capability;
                    logger.LogInformation(
                        "AI provider {Provider} model {Model} does not support the requested response format; downgrading to {Format}.",
                        resolved.Provider,
                        resolved.Model,
                        capability);
                }
            }
        }
        catch (AIContentSafetyException exception)
        {
            return Failed<T>(
                QuestionModelFailureKind.Refusal,
                exception.ErrorCode,
                exception.Message,
                retryable: false,
                exception,
                startedAt);
        }
        catch (AIException exception)
        {
            return Failed<T>(
                exception.IsTransient
                    ? QuestionModelFailureKind.Transient
                    : QuestionModelFailureKind.Permanent,
                exception.ErrorCode,
                exception.Message,
                exception.IsTransient,
                exception,
                startedAt);
        }
    }

    private QuestionModelResult<T> ParseResponse<T>(
        Type responseType,
        AIChatResponse response,
        long startedAt)
        where T : class
    {
        var metadata = Metadata(response, startedAt);
        if (string.Equals(response.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            return QuestionModelResult<T>.Failed(
                new(
                    QuestionModelFailureKind.Truncated,
                    "model_output_truncated",
                    "The model response was truncated.",
                    Retryable: true),
                metadata);
        }

        if (response.FinishReason?.Contains("content_filter", StringComparison.OrdinalIgnoreCase) == true ||
            response.FinishReason?.Contains("safety", StringComparison.OrdinalIgnoreCase) == true)
        {
            return QuestionModelResult<T>.Failed(
                new(
                    QuestionModelFailureKind.Refusal,
                    "model_output_filtered",
                    "The model response was filtered by the provider.",
                    Retryable: false),
                metadata);
        }

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            return QuestionModelResult<T>.Failed(
                new(
                    QuestionModelFailureKind.InvalidStructuredOutput,
                    "empty_structured_output",
                    "The model returned an empty response.",
                    Retryable: true),
                metadata);
        }

        try
        {
            var value = jsonContract.Deserialize(response.Text, responseType);
            return value is T typed
                ? QuestionModelResult<T>.Success(typed, metadata)
                : QuestionModelResult<T>.Failed(
                    new(
                        QuestionModelFailureKind.InvalidStructuredOutput,
                        "structured_output_type_mismatch",
                        $"Expected '{responseType.Name}' but received '{value.GetType().Name}'.",
                        Retryable: true),
                    metadata);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "AI provider {Provider} model {Model} returned invalid structured question output.",
                response.Provider,
                response.Model);
            return QuestionModelResult<T>.Failed(
                new(
                    QuestionModelFailureKind.InvalidStructuredOutput,
                    "invalid_structured_output",
                    exception.Message,
                    Retryable: true),
                metadata);
        }
    }

    private QuestionModelResult<T> Failed<T>(
        QuestionModelFailureKind kind,
        string code,
        string message,
        bool retryable,
        AIException exception,
        long startedAt)
        where T : class =>
        QuestionModelResult<T>.Failed(
            new(kind, code, message, retryable),
            new(
                exception.ProviderRequestId,
                exception.Provider,
                exception.Model,
                null,
                0,
                0,
                timeProvider.GetElapsedTime(startedAt)));

    private QuestionModelCallMetadata Metadata(AIChatResponse response, long startedAt) =>
        new(
            response.ProviderRequestId,
            response.Provider,
            response.Model,
            response.FinishReason,
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            timeProvider.GetElapsedTime(startedAt));

    private enum StructuredFormatCapability
    {
        Text = 0,
        JsonObject = 1,
        JsonSchema = 2,
    }
}
