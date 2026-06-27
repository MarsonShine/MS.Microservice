using System;
using FluentAssertions;
using MS.Microservice.Infrastructure.Utils.Diagnostics;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Utils.Diagnostics;

public sealed class PerformanceProbeTests
{
    [Fact]
    public void Start_EndPhase_Stop_ShouldCapturePhaseReport()
    {
        var probe = new PerformanceProbe().Start("load");

        probe.EndPhase()
            .BeginPhase("save")
            .EndPhase();

        var report = probe.Stop();

        report.Phases.Should().HaveCount(2);
        report.Phases[0].Name.Should().Be("load");
        report.Phases[1].Name.Should().Be("save");
        report.ToTable().Should().Contain("load").And.Contain("save");
    }

    [Fact]
    public void Start_WhenCalledTwice_ShouldThrow()
    {
        var probe = new PerformanceProbe().Start();

        Action action = () => probe.Start();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Probe already started.");
    }
}
