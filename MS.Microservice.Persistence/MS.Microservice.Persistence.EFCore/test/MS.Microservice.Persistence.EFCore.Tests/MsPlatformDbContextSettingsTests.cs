namespace MS.Microservice.Persistence.EFCore.Tests;

public class MsPlatformDbContextSettingsTests
{
    [Fact]
    public void EnabledAutoTimeTracker_ShouldReturnTrue_WhenEnabled()
    {
        var settings = new MsPlatformDbContextSettings { AutoTimeTracker = "Enabled" };

        settings.EnabledAutoTimeTracker().Should().BeTrue();
    }

    [Fact]
    public void EnabledAutoTimeTracker_ShouldReturnFalse_WhenDisabled()
    {
        var settings = new MsPlatformDbContextSettings { AutoTimeTracker = "Disabled" };

        settings.EnabledAutoTimeTracker().Should().BeFalse();
    }
}
