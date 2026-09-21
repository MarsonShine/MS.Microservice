using MS.Microservice.Lab.AotExamples.Static.Configuration;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class FeatureToggleExampleTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("true", true)]
    [InlineData(" FALSE ", false)]
    public void StaticScalarParserKeepsBooleanSemantics(string? value, bool expected)
        => Assert.Equal(expected, FeatureToggle.Read(value, "FeatureToggles:flag"));

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    public void InvalidValuesHaveConfigurationPath(string value)
        => Assert.Contains("FeatureToggles:flag", Assert.Throws<InvalidOperationException>(() => FeatureToggle.Read(value, "FeatureToggles:flag")).Message);
}
