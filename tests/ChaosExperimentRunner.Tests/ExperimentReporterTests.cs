using ChaosExperimentRunner.Models;
using ChaosExperimentRunner.Reporting;
using FluentAssertions;
using Xunit;

namespace ChaosExperimentRunner.Tests;

public sealed class ExperimentReporterTests
{
    private static ExperimentResult BuildResult(
        ExperimentStatus status,
        string name = "my-experiment",
        string? failureReason = null,
        IReadOnlyList<ProbeResult>? beforeProbes = null,
        IReadOnlyList<ActionResult>? actions = null,
        IReadOnlyList<RollbackResult>? rollbackResults = null)
    {
        var before = new SteadyStateResult("before", status != ExperimentStatus.Aborted,
            beforeProbes ?? Array.Empty<ProbeResult>());
        var after = new SteadyStateResult("after", status == ExperimentStatus.Passed,
            Array.Empty<ProbeResult>());
        return new ExperimentResult(name, status,
            DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow,
            before, after, actions ?? Array.Empty<ActionResult>(), failureReason, rollbackResults);
    }

    [Fact]
    public void Report_PassedExperiment_WritesPassedStatus()
    {
        var writer = new StringWriter();
        var reporter = new ConsoleExperimentReporter(writer);

        reporter.Report(BuildResult(ExperimentStatus.Passed));

        writer.ToString().Should().Contain("PASSED");
    }

    [Fact]
    public void Report_FailedExperiment_WritesFailedStatus()
    {
        var writer = new StringWriter();
        var reporter = new ConsoleExperimentReporter(writer);

        reporter.Report(BuildResult(ExperimentStatus.Failed, failureReason: "System unstable after fault"));

        writer.ToString().Should().Contain("FAILED");
    }

    [Fact]
    public void Report_AbortedExperiment_WritesAbortedStatus()
    {
        var writer = new StringWriter();
        var reporter = new ConsoleExperimentReporter(writer);

        reporter.Report(BuildResult(ExperimentStatus.Aborted, failureReason: "Steady state not met before experiment"));

        writer.ToString().Should().Contain("ABORTED");
    }

    [Fact]
    public void Report_IncludesExperimentName()
    {
        var writer = new StringWriter();
        var reporter = new ConsoleExperimentReporter(writer);

        reporter.Report(BuildResult(ExperimentStatus.Passed, name: "http-resilience-check"));

        writer.ToString().Should().Contain("http-resilience-check");
    }

    [Fact]
    public void Report_IncludesFailureReason_WhenPresent()
    {
        var writer = new StringWriter();
        var reporter = new ConsoleExperimentReporter(writer);
        const string reason = "Service did not recover after fault injection";

        reporter.Report(BuildResult(ExperimentStatus.Failed, failureReason: reason));

        writer.ToString().Should().Contain(reason);
    }

    [Fact]
    public void Report_IncludesProbeNames()
    {
        var writer = new StringWriter();
        var reporter = new ConsoleExperimentReporter(writer);
        var probes = new List<ProbeResult>
        {
            new("health-endpoint", true, 42, "OK"),
            new("upstream-ping", false, 100, "Timed out")
        }.AsReadOnly();

        reporter.Report(BuildResult(ExperimentStatus.Failed, beforeProbes: probes));

        var output = writer.ToString();
        output.Should().Contain("health-endpoint");
        output.Should().Contain("upstream-ping");
    }

    [Fact]
    public void Report_IncludesRollbackResults_WhenPresent()
    {
        var writer = new StringWriter();
        var reporter = new ConsoleExperimentReporter(writer);
        var rollbacks = new List<RollbackResult>
        {
            new("inject-latency", true, "Rolled back"),
            new("return-errors", false, "connection refused")
        }.AsReadOnly();

        reporter.Report(BuildResult(ExperimentStatus.Passed, rollbackResults: rollbacks));

        var output = writer.ToString();
        output.Should().Contain("inject-latency");
        output.Should().Contain("return-errors");
        output.Should().Contain("PARTIAL");
    }

    [Fact]
    public void Report_ShowsWarning_WhenRollbackPartiallyFailed()
    {
        var writer = new StringWriter();
        var reporter = new ConsoleExperimentReporter(writer);
        var rollbacks = new List<RollbackResult>
        {
            new("inject-latency", false, "timed out")
        }.AsReadOnly();

        reporter.Report(BuildResult(ExperimentStatus.Passed, rollbackResults: rollbacks));

        writer.ToString().Should().Contain("WARNING");
    }

    [Fact]
    public void Report_IncludesActionResults()
    {
        var writer = new StringWriter();
        var reporter = new ConsoleExperimentReporter(writer);
        var actions = new List<ActionResult>
        {
            new("inject-latency", true, "Injected 2000ms")
        }.AsReadOnly();

        reporter.Report(BuildResult(ExperimentStatus.Passed, actions: actions));

        writer.ToString().Should().Contain("inject-latency");
    }
}
