using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class ProviderSelectionExamplesTests
{
    [Theory]
    [InlineData("DeepSeek")]
    [InlineData("deepseek")]
    [InlineData("OpenAI")]
    [InlineData(null)]
    public void StaticSelectorPreservesProviderMatching(string? provider)
    {
        var model = new Model(provider);
        Assert.Equal(
            MS.Microservice.Lab.AotExamples.Legacy.AI.ProviderSelection.Matches(model, "DeepSeek"),
            MS.Microservice.Lab.AotExamples.Static.AI.ProviderSelection.Matches(model, "DeepSeek", static value => value.Provider));
    }
    public sealed record Model(string? Provider);
}
