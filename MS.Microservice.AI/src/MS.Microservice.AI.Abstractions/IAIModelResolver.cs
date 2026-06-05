namespace MS.Microservice.AI.Abstractions;

public interface IAIModelResolver
{
    AIResolvedModel ResolveChatModel(AIChatRequest request);
}