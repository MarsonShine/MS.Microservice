using Microsoft.Extensions.Configuration;
using MS.Microservice.Core.FeatureManager.Internals;
using System.Collections.Generic;

namespace MS.Microservice.Core.Tests.FeatureManager
{
    public class ConfigurationFeatureToggleProviderTests
    {
        private static IConfiguration BuildConfiguration(Dictionary<string, string?> data)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(data)
                .Build();
        }

        [Fact]
        public void IsFeatureEnabled_ExistingTrueToggle_ReturnsTrue()
        {
            var config = BuildConfiguration(new Dictionary<string, string?>
            {
                { "FeatureToggles:DarkMode", "true" }
            });

            var provider = new ConfigurationFeatureToggleProvider(config);
            Assert.True(provider.IsFeatureEnabled("DarkMode"));
        }

        [Fact]
        public void IsFeatureEnabled_ExistingFalseToggle_ReturnsFalse()
        {
            var config = BuildConfiguration(new Dictionary<string, string?>
            {
                { "FeatureToggles:DarkMode", "false" }
            });

            var provider = new ConfigurationFeatureToggleProvider(config);
            Assert.False(provider.IsFeatureEnabled("DarkMode"));
        }

        [Fact]
        public void IsFeatureEnabled_MissingToggle_ReturnsFalse()
        {
            var config = BuildConfiguration(new Dictionary<string, string?>());

            var provider = new ConfigurationFeatureToggleProvider(config);
            Assert.False(provider.IsFeatureEnabled("NonExistent"));
        }

        [Fact]
        public void IsFeatureEnabled_MultipleToggles_ReturnsCorrectValues()
        {
            var config = BuildConfiguration(new Dictionary<string, string?>
            {
                { "FeatureToggles:Feature1", "true" },
                { "FeatureToggles:Feature2", "false" },
                { "FeatureToggles:Feature3", "true" }
            });

            var provider = new ConfigurationFeatureToggleProvider(config);
            Assert.True(provider.IsFeatureEnabled("Feature1"));
            Assert.False(provider.IsFeatureEnabled("Feature2"));
            Assert.True(provider.IsFeatureEnabled("Feature3"));
        }
    }
}
