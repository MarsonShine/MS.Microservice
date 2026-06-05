namespace MS.Microservice.AI.Abstractions;

public interface IAIModelResolver
{
    AIResolvedModel ResolveChatModel(AIChatRequest request);

    AIResolvedModel ResolveTtsModel(AITtsRequest request);

    AIResolvedModel ResolveAsrModel(AIAsrRequest request);

    AIResolvedModel ResolveImageGenerationModel(AIImageGenerationRequest request);

    AIResolvedModel ResolveImageEditModel(AIImageEditRequest request);
}