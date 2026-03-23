using MS.Microservice.Core.FeatureManager;
using NSubstitute;

namespace MS.Microservice.Core.Tests.FeatureManager
{
    public class FeatureToggleManagerTests
    {
        [Fact]
        public void IsEnabled_ProviderReturnsTrue_ReturnsTrue()
        {
            var provider = Substitute.For<IFeatureToggleProvider>();
            provider.IsFeatureEnabled("DarkMode").Returns(true);

            var manager = new FeatureToggleManager(provider);
            Assert.True(manager.IsEnabled("DarkMode"));
        }

        [Fact]
        public void IsEnabled_ProviderReturnsFalse_ReturnsFalse()
        {
            var provider = Substitute.For<IFeatureToggleProvider>();
            provider.IsFeatureEnabled("DarkMode").Returns(false);

            var manager = new FeatureToggleManager(provider);
            Assert.False(manager.IsEnabled("DarkMode"));
        }

        [Fact]
        public void IsEnabled_DelegatesToProvider()
        {
            var provider = Substitute.For<IFeatureToggleProvider>();
            provider.IsFeatureEnabled(Arg.Any<string>()).Returns(false);

            var manager = new FeatureToggleManager(provider);
            manager.IsEnabled("Feature1");
            manager.IsEnabled("Feature2");

            provider.Received(1).IsFeatureEnabled("Feature1");
            provider.Received(1).IsFeatureEnabled("Feature2");
        }
    }
}
