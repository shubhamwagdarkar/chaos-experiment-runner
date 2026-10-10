using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Models;
using ChaosExperimentRunner.Reporting;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ChaosExperimentRunner.Tests;

public sealed class ExperimentRunnerTests
{
    private static SteadyStateResult PassedState(string title = "healthy") =>
        new(title, true, Array.Empty<ProbeResult>());

    private static SteadyStateResult FailedState(string title = "unhealthy") =>
        new(title, false, Array.Empty<ProbeResult>());

    private static ChaosExperiment BuildExperiment(
        FaultAction[]? actions = null,
        bool rollbackEnabled = true) =>
        new(
            "test-experiment",
            "desc",
            new SteadyStateHypothesis("baseline", Array.Empty<ExperimentProbe>()),
            new ExperimentMethod("method", actions ?? new[]
            {
                new FaultAction("inject-latency", FaultType.HttpLatency,
                    new HttpLatencyFaultConfig("http://upstream", 2000))
            }),
            new RollbackConfig(rollbackEnabled, 30)
        );

    private ExperimentRunner BuildRunner(
        ISteadyStateVerifier? verifier = null,
        IFaultRunner[]? faultRunners = null,
        IExperimentReporter? reporter = null)
    {
        verifier ??= Substitute.For<ISteadyStateVerifier>();
        reporter ??= Substitute.For<IExperimentReporter>();
        return new ExperimentRunner(
            verifier,
            faultRunners ?? Array.Empty<IFaultRunner>(),
            reporter,
            NullLogger<ExperimentRunner>.Instance);
    }

    [Fact]
    public async Task RunAsync_WhenBeforeStateNotMet_AbortsExperiment()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(FailedState());

        var runner = BuildRunner(verifier);
        var result = await runner.RunAsync(BuildExperiment());

        result.Status.Should().Be(ExperimentStatus.Aborted);
    }

    [Fact]
    public async Task RunAsync_WhenAborted_FailureReasonIsSet()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(FailedState());

        var runner = BuildRunner(verifier);
        var result = await runner.RunAsync(BuildExperiment());

        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenAborted_DoesNotExecuteFaultActions()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(FailedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(Arg.Any<FaultType>()).Returns(true);

        var runner = BuildRunner(verifier, new[] { faultRunner });
        await runner.RunAsync(BuildExperiment());

        await faultRunner.DidNotReceive().ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenAllSucceed_ReturnsPassedStatus()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        var result = await runner.RunAsync(BuildExperiment());

        result.Status.Should().Be(ExperimentStatus.Passed);
    }

    [Fact]
    public async Task RunAsync_WhenFaultFails_ReturnsFailedStatus()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", false, "connection refused"));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        var result = await runner.RunAsync(BuildExperiment());

        result.Status.Should().Be(ExperimentStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_WhenAfterStateNotMet_ReturnsFailedStatus()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        int callCount = 0;
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(_ => callCount++ == 0 ? PassedState() : FailedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        var result = await runner.RunAsync(BuildExperiment());

        result.Status.Should().Be(ExperimentStatus.Failed);
        result.FailureReason.Should().Contain("steady state");
    }

    [Fact]
    public async Task RunAsync_CallsVerifier_TwiceOnNormalRun()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        await runner.RunAsync(BuildExperiment());

        await verifier.Received(2).VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenRollbackEnabled_CallsRollback()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        await runner.RunAsync(BuildExperiment(rollbackEnabled: true));

        await faultRunner.Received(1).RollbackAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenRollbackDisabled_SkipsRollback()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        await runner.RunAsync(BuildExperiment(rollbackEnabled: false));

        await faultRunner.DidNotReceive().RollbackAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenNoFaultRunner_ReturnsFailedStatus()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var runner = BuildRunner(verifier);
        var result = await runner.RunAsync(BuildExperiment());

        result.Status.Should().Be(ExperimentStatus.Failed);
        result.ActionResults.Should().HaveCount(1);
        result.ActionResults[0].Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_RecordsStartAndEndTime()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));

        var before = DateTimeOffset.UtcNow;
        var runner = BuildRunner(verifier, new[] { faultRunner });
        var result = await runner.RunAsync(BuildExperiment());
        var after = DateTimeOffset.UtcNow;

        result.StartedAt.Should().BeOnOrAfter(before);
        result.CompletedAt.Should().BeOnOrBefore(after);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_CallsReporter_WithResult()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var reporter = Substitute.For<IExperimentReporter>();
        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));

        var runner = BuildRunner(verifier, new[] { faultRunner }, reporter);
        await runner.RunAsync(BuildExperiment());

        reporter.Received(1).Report(Arg.Any<ExperimentResult>());
    }

    [Fact]
    public async Task RunAsync_WhenSecondFaultFails_StopsAtSecondAction()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        int callCount = 0;
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(_ => new ActionResult($"action-{++callCount}", callCount < 2));

        var actions = new[]
        {
            new FaultAction("action-1", FaultType.HttpLatency, new HttpLatencyFaultConfig("http://a", 1000)),
            new FaultAction("action-2", FaultType.HttpLatency, new HttpLatencyFaultConfig("http://b", 1000)),
            new FaultAction("action-3", FaultType.HttpLatency, new HttpLatencyFaultConfig("http://c", 1000))
        };

        var runner = BuildRunner(verifier, new[] { faultRunner });
        var result = await runner.RunAsync(BuildExperiment(actions));

        result.Status.Should().Be(ExperimentStatus.Failed);
        result.ActionResults.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_WhenAborted_BeforeAndAfterStateAreSame()
    {
        var failedState = FailedState("system-not-ready");
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(failedState);

        var runner = BuildRunner(verifier);
        var result = await runner.RunAsync(BuildExperiment());

        result.BeforeState.Should().Be(result.AfterState);
    }

    [Fact]
    public async Task RunAsync_WhenFaultFails_OnlyRollsBackExecutedActions()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        int callCount = 0;
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(_ => new ActionResult($"action-{++callCount}", callCount == 1));

        var actions = new[]
        {
            new FaultAction("a1", FaultType.HttpLatency, new HttpLatencyFaultConfig("http://a", 1000)),
            new FaultAction("a2", FaultType.HttpLatency, new HttpLatencyFaultConfig("http://b", 1000))
        };

        var runner = BuildRunner(verifier, new[] { faultRunner });
        await runner.RunAsync(BuildExperiment(actions));

        await faultRunner.Received(1).RollbackAsync(Arg.Is<FaultAction>(a => a.Name == "a1"), Arg.Any<CancellationToken>());
        await faultRunner.DidNotReceive().RollbackAsync(Arg.Is<FaultAction>(a => a.Name == "a2"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RollbackResults_AreRecorded_WhenRollbackEnabled()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        var result = await runner.RunAsync(BuildExperiment(rollbackEnabled: true));

        result.RollbackResults.Should().NotBeNull().And.HaveCount(1);
        result.RollbackResults![0].Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_RollbackResults_AreNull_WhenRollbackDisabled()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        var result = await runner.RunAsync(BuildExperiment(rollbackEnabled: false));

        result.RollbackResults.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WhenRollbackThrows_RecordsFailedRollbackResult()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));
        faultRunner.RollbackAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns<Task>(_ => throw new InvalidOperationException("rollback connection refused"));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        var result = await runner.RunAsync(BuildExperiment(rollbackEnabled: true));

        result.RollbackResults.Should().NotBeNull();
        result.RollbackResults![0].Succeeded.Should().BeFalse();
        result.RollbackResults[0].Message.Should().Contain("rollback connection refused");
        result.HasRollbackFailures.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenRollbackThrows_ExperimentStatusIsUnaffected()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));
        faultRunner.RollbackAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns<Task>(_ => throw new InvalidOperationException("rollback failed"));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        var result = await runner.RunAsync(BuildExperiment(rollbackEnabled: true));

        // Rollback failure does not change experiment status — it's tracked separately
        result.Status.Should().Be(ExperimentStatus.Passed);
        result.HasRollbackFailures.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ExperimentName_IsPreservedInResult()
    {
        var verifier = Substitute.For<ISteadyStateVerifier>();
        verifier.VerifyAsync(Arg.Any<SteadyStateHypothesis>(), Arg.Any<CancellationToken>())
                .Returns(PassedState());

        var faultRunner = Substitute.For<IFaultRunner>();
        faultRunner.CanHandle(FaultType.HttpLatency).Returns(true);
        faultRunner.ExecuteAsync(Arg.Any<FaultAction>(), Arg.Any<CancellationToken>())
                   .Returns(new ActionResult("inject-latency", true));

        var runner = BuildRunner(verifier, new[] { faultRunner });
        var result = await runner.RunAsync(BuildExperiment());

        result.ExperimentName.Should().Be("test-experiment");
    }
}
