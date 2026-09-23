using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.AspNetCore;
using MS.Microservice.Core.Functional;
using MS.Microservice.Idempotency.EFCore;
using MS.Microservice.Messaging;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;

namespace MS.Microservice.Reference.Web;

internal static class ProfileIdempotencyHandler
{
    private const string HeaderName = "Idempotency-Key";
    private const string Operation = "profiles.create";
    private const string JsonContentType = "application/json; charset=utf-8";
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    internal static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    internal static async Task<IResult> CreateAsync(CreateProfile request, AuditActor actor, ProfileService service,
        HttpContext http, IUnitOfWork unit, EfCoreIdempotencyStore<ReferenceDbContext> store,
        IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        if (!http.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return ToResult(await service.CreateAsync(request, actor, cancellationToken));
        }

        if (values.Count != 1 || values[0] is not { } key)
        {
            return InvalidKey();
        }

        IdempotencyRequest idempotency;
        try
        {
            // Hash the authenticated actor's canonical representation before passing it to the bounded store API.
            var actorBytes = JsonSerializer.SerializeToUtf8Bytes(actor, ReferenceIdempotencyJsonContext.Default.AuditActor);
            var actorHash = Convert.ToHexString(SHA256.HashData(actorBytes));
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request,
                ReferenceIdempotencyJsonContext.Default.CreateProfile);
            idempotency = IdempotencyRequest.Create(Operation, actorHash, key, requestBytes);
        }
        catch (ArgumentException)
        {
            return InvalidKey();
        }

        var existing = await store.FindAsync(idempotency, cancellationToken);
        if (existing.Kind == IdempotencyLookupKind.Replay)
            return new StoredResponseResult(existing.Response!);
        if (existing.Kind == IdempotencyLookupKind.DifferentRequest)
            return DifferentRequest();

        try
        {
            var response = await unit.ExecuteAsync(token => store.ClaimAndExecuteAsync(idempotency, Retention,
                async operationToken =>
                {
                    var result = await service.CreateAsync(request, actor, operationToken);
                    if (result.IsLeft) throw new BusinessFailure(result.Left);
                    var profile = result.Right;
                    var body = JsonSerializer.SerializeToUtf8Bytes(profile,
                        ReferenceIdempotencyJsonContext.Default.ProfileView);
                    return new IdempotencyResponse(201, JsonContentType, body,
                        $"/api/v1/profiles/{profile.Id}");
                }, token), cancellationToken);
            return new StoredResponseResult(response);
        }
        catch (BusinessFailure failure)
        {
            // The outer unit of work has rolled back the claim and any staged business changes.
            return ApplicationErrorResults.ToProblem(failure.Error.Code, failure.Error.Message, failure.Error.Details);
        }
        catch (DbUpdateException)
        {
            // The failed context and transaction cannot be reused after a concurrent claim.
            await using var scope = scopes.CreateAsyncScope();
            var freshStore = scope.ServiceProvider.GetRequiredService<EfCoreIdempotencyStore<ReferenceDbContext>>();
            var winner = await freshStore.FindAsync(idempotency, cancellationToken);
            if (winner.Kind == IdempotencyLookupKind.Replay)
                return new StoredResponseResult(winner.Response!);
            if (winner.Kind == IdempotencyLookupKind.DifferentRequest)
                return DifferentRequest();
            throw;
        }
    }

    private static IResult ToResult(Either<Error, ProfileView> result)
        => result.Match(error => ApplicationErrorResults.ToProblem(error.Code, error.Message, error.Details),
            profile => Results.Created($"/api/v1/profiles/{profile.Id}", profile));

    private static IResult InvalidKey()
        => ApplicationErrorResults.ToProblem("validation", "Idempotency-Key or request representation is invalid.");

    private static IResult DifferentRequest()
        => ApplicationErrorResults.ToProblem("conflict", "Idempotency-Key was used for a different request.");

    private sealed class BusinessFailure(Error error) : Exception
    {
        internal Error Error { get; } = error;
    }

    private sealed class StoredResponseResult(IdempotencyResponse response) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = response.StatusCode;
            httpContext.Response.ContentType = response.ContentType;
            if (response.Location is { } location) httpContext.Response.Headers.Location = location;
            await httpContext.Response.Body.WriteAsync(response.Body, httpContext.RequestAborted);
        }
    }
}
