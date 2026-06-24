namespace MS.Microservice.Persistence.EFCore.Tests;

public class MsPlatformDbContextSettingsTests
{
    [Fact]
    public void Defaults_ShouldHaveDisabledAutoTimeTracker()
    {
        var settings = new MsPlatformDbContextSettings();

        settings.AutoTimeTracker.Should().Be("Disabled");
    }

    [Fact]
    public void Defaults_ShouldHaveSoftDeleteEnabled()
    {
        var settings = new MsPlatformDbContextSettings();

        settings.EnabledSoftDeleted.Should().BeTrue();
    }

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

    [Fact]
    public void SectionName_ShouldBeCorrect()
    {
        MsPlatformDbContextSettings.SectionName.Should().Be("FzPlatformDbContextSettings");
    }
}