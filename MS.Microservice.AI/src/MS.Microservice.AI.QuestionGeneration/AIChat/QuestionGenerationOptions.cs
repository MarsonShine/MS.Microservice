namespace MS.Microservice.AI.QuestionGeneration.AIChat;

public sealed class QuestionGenerationOptions
{
    public string DraftScenario { get; set; } = "QuestionGenerationDraft";

    public string ReviewScenario { get; set; } = "QuestionGenerationReview";

    public string RepairScenario { get; set; } = "QuestionGenerationRepair";
}
