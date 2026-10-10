using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Models;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ChaosExperimentRunner.Tests;

public sealed class SteadyStateVerifierTests
{
    private static ExperimentProbe MakeProbe(string name, ProbeType type = ProbeType.Http) =>
        new(name, type, new HttpProbeConfig("http://test"), new HttpProbeTolerance(200));

    private static IProbeRunner MakeRunner(ProbeType type, bool passes)
    {
        var runner = Substitute.For<IProbeRunner>();
        runner.CanHandle(type).Returns(true);
        runner.RunAsync(Arg.Any<ExperimentProbe>(), Arg.Any<CancellationToken>())
              .Returns(ci => Task.FromResult(new ProbeResult(ci.Arg<ExperimentProbe>().Name, passes)));
        return runner;
    }

    [Fact]
    public async Task VerifyAsync_WhenAllProbesPassed_ReturnsPassedResult()
    {
        var runner = MakeRunner(ProbeType.Http, passes: true);
        var verifier = new SteadyStateVerifier(new[] { runner });
        var hypothesis = new SteadyStateHypothesis("title", new[] { MakeProbe("p1"), MakeProbe("p2") });

        var result = await verifier.VerifyAsync(hypothesis);

        result.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_WhenAnyProbeFails_ReturnsFailedResult()
    {
        var passRunner = MakeRunner(ProbeType.Http, passes: true);
        var failRunner = Substitute.For<IProbeRunner>();
        failRunner.CanHandle(ProbeType.Process).Returns(true);
        failRunner.RunAsync(Arg.Any<ExperimentProbe>(), Arg.Any<CancellationToken>())
                  .Returns(ci => Task.FromResult(new ProbeResult(ci.Arg<ExperimentProbe>().Name, false)));

        var verifier = new SteadyStateVerifier(new[] { passRunner, failRunner });
        var hypothesis = new SteadyStateHypothesis("title", new[]
        {
            MakeProbe("http-probe", ProbeType.Http),
            new ExperimentProbe("process-probe", ProbeType.Process,
                new ProcessProbeConfig("svc"), new ProcessProbeTolerance(true))
        });

        var result = await verifier.VerifyAsync(hypothesis);

        result.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WhenNoProbes_ReturnsPassedResult()
    {
        var verifier = new SteadyStateVerifier(Array.Empty<IProbeRunner>());
        var hypothesis = new SteadyStateHypothesis("empty", Array.Empty<ExperimentProbe>());

        var result = await verifier.VerifyAsync(hypothesis);

        result.Passed.Should().BeTrue();
        result.ProbeResults.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyAsync_RunsAllProbes_EvenAfterFirstFailure()
    {
        var runner = Substitute.For<IProbeRunner>();
        runner.CanHandle(ProbeType.Http).Returns(true);
        runner.RunAsync(Arg.Any<ExperimentProbe>(), Arg.Any<CancellationToken>())
              .Returns(ci => Task.FromResult(new ProbeResult(ci.Arg<ExperimentProbe>().Name, false)));

        var verifier = new SteadyStateVerifier(new[] { runner });
        var hypothesis = new SteadyStateHypothesis("title", new[] { MakeProbe("p1"), MakeProbe("p2"), MakeProbe("p3") });

        var result = await verifier.VerifyAsync(hypothesis);

        result.ProbeResults.Should().HaveCount(3);
    }

    [Fact]
    public async Task VerifyAsync_ProbeResultsContainAllProbeNames()
    {
        var runner = MakeRunner(ProbeType.Http, passes: true);
        var verifier = new SteadyStateVerifier(new[] { runner });
        var hypothesis = new SteadyStateHypothesis("title", new[] { MakeProbe("alpha"), MakeProbe("beta") });

        var result = await verifier.VerifyAsync(hypothesis);

        result.ProbeResults.Select(r => r.ProbeName).Should().Contain("alpha", "beta");
    }

    [Fact]
    public async Task VerifyAsync_PassesHypothesisTitleToResult()
    {
        var verifier = new SteadyStateVerifier(Array.Empty<IProbeRunner>());
        var hypothesis = new SteadyStateHypothesis("My Hypothesis Title", Array.Empty<ExperimentProbe>());

        var result = await verifier.VerifyAsync(hypothesis);

        result.HypothesisTitle.Should().Be("My Hypothesis Title");
    }

    [Fact]
    public async Task VerifyAsync_WhenNoRunnerFound_ThrowsInvalidOperation()
    {
        var verifier = new SteadyStateVerifier(Array.Empty<IProbeRunner>());
        var hypothesis = new SteadyStateHypothesis("title", new[] { MakeProbe("p1") });

        await verifier.Invoking(v => v.VerifyAsync(hypothesis))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Http*");
    }

    [Fact]
    public async Task VerifyAsync_CallsCorrectRunner_ForHttpProbe()
    {
        var httpRunner = MakeRunner(ProbeType.Http, passes: true);
        var processRunner = MakeRunner(ProbeType.Process, passes: true);
        var verifier = new SteadyStateVerifier(new[] { httpRunner, processRunner });
        var hypothesis = new SteadyStateHypothesis("title", new[] { MakeProbe("p1", ProbeType.Http) });

        await verifier.VerifyAsync(hypothesis);

        await httpRunner.Received(1).RunAsync(Arg.Any<ExperimentProbe>(), Arg.Any<CancellationToken>());
        await processRunner.DidNotReceive().RunAsync(Arg.Any<ExperimentProbe>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_CallsCorrectRunner_ForProcessProbe()
    {
        var httpRunner = MakeRunner(ProbeType.Http, passes: true);
        var processRunner = MakeRunner(ProbeType.Process, passes: true);
        var verifier = new SteadyStateVerifier(new[] { httpRunner, processRunner });
        var probe = new ExperimentProbe("p1", ProbeType.Process,
            new ProcessProbeConfig("svc"), new ProcessProbeTolerance(true));
        var hypothesis = new SteadyStateHypothesis("title", new[] { probe });

        await verifier.VerifyAsync(hypothesis);

        await processRunner.Received(1).RunAsync(Arg.Any<ExperimentProbe>(), Arg.Any<CancellationToken>());
        await httpRunner.DidNotReceive().RunAsync(Arg.Any<ExperimentProbe>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_WhenMixedResults_ReturnsFalse()
    {
        var runner = Substitute.For<IProbeRunner>();
        runner.CanHandle(ProbeType.Http).Returns(true);
        int callCount = 0;
        runner.RunAsync(Arg.Any<ExperimentProbe>(), Arg.Any<CancellationToken>())
              .Returns(ci =>
              {
                  callCount++;
                  return Task.FromResult(new ProbeResult(ci.Arg<ExperimentProbe>().Name, callCount % 2 == 1));
              });

        var verifier = new SteadyStateVerifier(new[] { runner });
        var hypothesis = new SteadyStateHypothesis("title", new[] { MakeProbe("p1"), MakeProbe("p2") });

        var result = await verifier.VerifyAsync(hypothesis);

        result.Passed.Should().BeFalse();
        result.ProbeResults.Should().HaveCount(2);
    }

    [Fact]
    public async Task VerifyAsync_SingleFailedProbe_ReturnsSingleFailure()
    {
        var runner = Substitute.For<IProbeRunner>();
        runner.CanHandle(ProbeType.Http).Returns(true);
        runner.RunAsync(Arg.Any<ExperimentProbe>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(new ProbeResult("p1", false, Message: "Timed out")));

        var verifier = new SteadyStateVerifier(new[] { runner });
        var hypothesis = new SteadyStateHypothesis("title", new[] { MakeProbe("p1") });

        var result = await verifier.VerifyAsync(hypothesis);

        result.Passed.Should().BeFalse();
        result.ProbeResults.Single().Message.Should().Be("Timed out");
    }
}
