using ChaosExperimentRunner.Models;
using FluentAssertions;
using Xunit;

namespace ChaosExperimentRunner.Tests;

public sealed class ModelTests
{
    [Fact]
    public void ExperimentResult_Duration_IsCorrect()
    {
        var start = DateTimeOffset.UtcNow;
        var end = start.AddSeconds(5);
        var result = BuildResult(start, end, ExperimentStatus.Passed);

        result.Duration.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ExperimentResult_SteadyStateViolated_WhenBeforeStateFails()
    {
        var result = BuildResult(beforePassed: false, afterPassed: true);
        result.SteadyStateViolated.Should().BeTrue();
    }

    [Fact]
    public void ExperimentResult_SteadyStateViolated_WhenAfterStateFails()
    {
        var result = BuildResult(beforePassed: true, afterPassed: false);
        result.SteadyStateViolated.Should().BeTrue();
    }

    [Fact]
    public void ExperimentResult_NotViolated_WhenBothPass()
    {
        var result = BuildResult(beforePassed: true, afterPassed: true);
        result.SteadyStateViolated.Should().BeFalse();
    }

    [Fact]
    public void ChaosExperiment_CanBeConstructed()
    {
        var experiment = BuildExperiment();
        experiment.Name.Should().Be("test-experiment");
        experiment.SteadyState.Probes.Should().HaveCount(1);
        experiment.Method.Actions.Should().HaveCount(1);
    }

    [Fact]
    public void SteadyStateResult_Passed_WhenAllProbesPassed()
    {
        var probes = new List<ProbeResult>
        {
            new("probe-1", true),
            new("probe-2", true)
        };
        var result = new SteadyStateResult("title", true, probes.AsReadOnly());
        result.Passed.Should().BeTrue();
    }

    [Fact]
    public void SteadyStateResult_Failed_WhenAnyProbeFailed()
    {
        var probes = new List<ProbeResult>
        {
            new("probe-1", true),
            new("probe-2", false)
        };
        var result = new SteadyStateResult("title", false, probes.AsReadOnly());
        result.Passed.Should().BeFalse();
    }

    [Fact]
    public void ProbeResult_HasCorrectProperties()
    {
        var r = new ProbeResult("my-probe", true, 42, "OK");
        r.ProbeName.Should().Be("my-probe");
        r.Passed.Should().BeTrue();
        r.LatencyMs.Should().Be(42);
        r.Message.Should().Be("OK");
    }

    [Fact]
    public void ActionResult_HasCorrectProperties()
    {
        var r = new ActionResult("inject-latency", true, "Injected 2000ms");
        r.ActionName.Should().Be("inject-latency");
        r.Succeeded.Should().BeTrue();
        r.Message.Should().Be("Injected 2000ms");
    }

    [Fact]
    public void RollbackConfig_DefaultValues()
    {
        var cfg = new RollbackConfig();
        cfg.Enabled.Should().BeTrue();
        cfg.TimeoutSeconds.Should().Be(60);
    }

    // Helpers
    private static ExperimentResult BuildResult(
        DateTimeOffset? start = null, DateTimeOffset? end = null,
        ExperimentStatus status = ExperimentStatus.Passed,
        bool beforePassed = true, bool afterPassed = true)
    {
        var s = start ?? DateTimeOffset.UtcNow;
        var e = end ?? s.AddSeconds(1);
        var before = new SteadyStateResult("before", beforePassed, Array.Empty<ProbeResult>());
        var after = new SteadyStateResult("after", afterPassed, Array.Empty<ProbeResult>());
        return new ExperimentResult("exp", status, s, e, before, after, Array.Empty<ActionResult>());
    }

    private static ChaosExperiment BuildExperiment() =>
        new(
            "test-experiment",
            "desc",
            new SteadyStateHypothesis("title", new[]
            {
                new ExperimentProbe("p1", ProbeType.Http,
                    new HttpProbeConfig("http://localhost/health"),
                    new HttpProbeTolerance(200))
            }),
            new ExperimentMethod("method", new[]
            {
                new FaultAction("inject", FaultType.HttpLatency,
                    new HttpLatencyFaultConfig("http://upstream", 2000))
            }),
            new RollbackConfig()
        );
}
