using Xunit;
using OldDefault = MS.Microservice.Lab.AotExamples.Legacy.Defaults.DefaultComparisonExample;
using NewDefault = MS.Microservice.Lab.AotExamples.Static.Defaults.DefaultComparisonExample;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class DefaultComparisonExampleTests
{
    [Fact]
    public void CommonDefaultsMatchButConstructingAStructIsNotDefault()
    {
        Assert.Equal(OldDefault.GetDefault(typeof(int)), NewDefault.GetDefault<int>());
        Assert.Equal(OldDefault.IsDefault(0), NewDefault.IsDefault(0));
        Assert.Equal(7, ((CustomValue)OldDefault.GetDefault(typeof(CustomValue))!).Value);
        Assert.Equal(0, NewDefault.GetDefault<CustomValue>().Value);
    }

    public struct CustomValue
    {
        public int Value { get; }
        public CustomValue() => Value = 7;
    }
}
