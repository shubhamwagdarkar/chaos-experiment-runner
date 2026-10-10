using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Faults;
using ChaosExperimentRunner.Models;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ChaosExperimentRunner.Tests;

public sealed class FaultRunnerTests
{
    // HttpLatencyFaultRunner
    [Fact]
    public void HttpLatencyFaultRunner_CanHandle_HttpLatency()
    {
        var runner = new HttpLatencyFaultRunner(new FaultRegistry());
        runner.CanHandle(FaultType.HttpLatency).Should().BeTrue();
    }

    [Fact]
    public void HttpLatencyFaultRunner_CannotHandle_OtherTypes()
    {
        var runner = new HttpLatencyFaultRunner(new FaultRegistry());
        runner.CanHandle(FaultType.HttpError).Should().BeFalse();
        runner.CanHandle(FaultType.ProcessTerminate).Should().BeFalse();
    }

    [Fact]
    public async Task HttpLatencyFaultRunner_Execute_RegistersFaultInRegistry()
    {
        var registry = new FaultRegistry();
        var runner = new HttpLatencyFaultRunner(registry);
        var action = new FaultAction("add-latency", FaultType.HttpLatency,
            new HttpLatencyFaultConfig("http://upstream", 2000, 30));

        var result = await runner.ExecuteAsync(action);

        result.Succeeded.Should().BeTrue();
        registry.Count.Should().Be(1);
        registry.GetActiveFaults().Single().LatencyMs.Should().Be(2000);
    }

    [Fact]
    public async Task HttpLatencyFaultRunner_Rollback_RemovesFaultFromRegistry()
    {
        var registry = new FaultRegistry();
        var runner = new HttpLatencyFaultRunner(registry);
        var action = new FaultAction("add-latency", FaultType.HttpLatency,
            new HttpLatencyFaultConfig("http://upstream", 2000, 30));

        await runner.ExecuteAsync(action);
        registry.Count.Should().Be(1);

        await runner.RollbackAsync(action);
        registry.Count.Should().Be(0);
    }

    // HttpErrorFaultRunner
    [Fact]
    public void HttpErrorFaultRunner_CanHandle_HttpError()
    {
        var runner = new HttpErrorFaultRunner(new FaultRegistry());
        runner.CanHandle(FaultType.HttpError).Should().BeTrue();
    }

    [Fact]
    public async Task HttpErrorFaultRunner_Execute_RegistersFault()
    {
        var registry = new FaultRegistry();
        var runner = new HttpErrorFaultRunner(registry);
        var action = new FaultAction("return-503", FaultType.HttpError,
            new HttpErrorFaultConfig("http://upstream", 503, 20));

        var result = await runner.ExecuteAsync(action);

        result.Succeeded.Should().BeTrue();
        registry.GetActiveFaults().Single().StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task HttpErrorFaultRunner_Rollback_RemovesFault()
    {
        var registry = new FaultRegistry();
        var runner = new HttpErrorFaultRunner(registry);
        var action = new FaultAction("return-503", FaultType.HttpError,
            new HttpErrorFaultConfig("http://upstream", 503, 20));

        await runner.ExecuteAsync(action);
        await runner.RollbackAsync(action);

        registry.Count.Should().Be(0);
    }

    // ProcessTerminateFaultRunner
    [Fact]
    public void ProcessTerminateFaultRunner_CanHandle_ProcessTerminate()
    {
        var killer = Substitute.For<IProcessKiller>();
        var runner = new ProcessTerminateFaultRunner(killer);
        runner.CanHandle(FaultType.ProcessTerminate).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessTerminateFaultRunner_Execute_CallsKiller()
    {
        var killer = Substitute.For<IProcessKiller>();
        killer.TryKill("myservice", out Arg.Any<string>()).Returns(x =>
        {
            x[1] = "Terminated 1/1 instance(s) of 'myservice'";
            return true;
        });

        var runner = new ProcessTerminateFaultRunner(killer);
        var action = new FaultAction("kill-svc", FaultType.ProcessTerminate,
            new ProcessTerminateFaultConfig("myservice"));

        var result = await runner.ExecuteAsync(action);

        result.Succeeded.Should().BeTrue();
        killer.Received(1).TryKill("myservice", out Arg.Any<string>());
    }

    [Fact]
    public async Task ProcessTerminateFaultRunner_WhenProcessNotFound_ReturnsFailed()
    {
        var killer = Substitute.For<IProcessKiller>();
        killer.TryKill("notexist", out Arg.Any<string>()).Returns(x =>
        {
            x[1] = "No process found with name 'notexist'";
            return false;
        });

        var runner = new ProcessTerminateFaultRunner(killer);
        var action = new FaultAction("kill-svc", FaultType.ProcessTerminate,
            new ProcessTerminateFaultConfig("notexist"));

        var result = await runner.ExecuteAsync(action);

        result.Succeeded.Should().BeFalse();
    }

    // ResourceExhaustionFaultRunner
    [Fact]
    public void ResourceExhaustionFaultRunner_CanHandle_ResourceExhaustion()
    {
        var stressor = Substitute.For<IResourceStressor>();
        var runner = new ResourceExhaustionFaultRunner(stressor);
        runner.CanHandle(FaultType.ResourceExhaustion).Should().BeTrue();
    }

    [Fact]
    public async Task ResourceExhaustionFaultRunner_Execute_StartsStressor()
    {
        var stressor = Substitute.For<IResourceStressor>();
        stressor.StartAsync(Arg.Any<ResourceType>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns("stress-abc123");

        var runner = new ResourceExhaustionFaultRunner(stressor);
        var action = new FaultAction("burn-cpu", FaultType.ResourceExhaustion,
            new ResourceExhaustionFaultConfig(ResourceType.Cpu, 30, 75));

        var result = await runner.ExecuteAsync(action);

        result.Succeeded.Should().BeTrue();
        await stressor.Received(1).StartAsync(ResourceType.Cpu, 75, 30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResourceExhaustionFaultRunner_Rollback_StopsStressor()
    {
        var stressor = Substitute.For<IResourceStressor>();
        stressor.StartAsync(Arg.Any<ResourceType>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns("stress-abc123");

        var runner = new ResourceExhaustionFaultRunner(stressor);
        var action = new FaultAction("burn-cpu", FaultType.ResourceExhaustion,
            new ResourceExhaustionFaultConfig(ResourceType.Cpu, 30, 75));

        await runner.ExecuteAsync(action);
        await runner.RollbackAsync(action);

        stressor.Received(1).Stop("stress-abc123");
    }

    // FaultRegistry
    [Fact]
    public void FaultRegistry_RegisterAndCount()
    {
        var registry = new FaultRegistry();
        registry.Register("id-1", new ActiveFault("id-1", FaultType.HttpLatency, "http://a", LatencyMs: 500));
        registry.Count.Should().Be(1);
    }

    [Fact]
    public void FaultRegistry_GetFaultForUrl_ReturnsMatchingFault()
    {
        var registry = new FaultRegistry();
        registry.Register("id-1", new ActiveFault("id-1", FaultType.HttpLatency, "http://upstream:8081", LatencyMs: 1000));

        var fault = registry.GetFaultForUrl("http://upstream:8081/api/data");

        fault.Should().NotBeNull();
        fault!.LatencyMs.Should().Be(1000);
    }
}
