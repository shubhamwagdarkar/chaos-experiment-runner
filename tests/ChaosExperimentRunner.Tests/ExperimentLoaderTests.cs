using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Models;
using FluentAssertions;
using Xunit;

namespace ChaosExperimentRunner.Tests;

public sealed class ExperimentLoaderTests
{
    private readonly YamlExperimentLoader _loader = new();

    private const string FullYaml = """
        name: http-resilience-test
        description: Verify service handles upstream latency
        steady-state:
          title: Service is healthy
          probes:
            - name: health-check
              type: Http
              config:
                url: http://localhost:8080/health
                method: GET
                timeout-seconds: 5
              tolerance:
                status-code: 200
                max-latency-ms: 500
            - name: service-process
              type: Process
              config:
                process-name: myservice
              tolerance:
                running: true
            - name: ping-command
              type: Command
              config:
                command: echo
                arguments: hello
              tolerance:
                exit-code: 0
                output-contains: hello
        method:
          name: inject-faults
          actions:
            - name: add-latency
              type: HttpLatency
              config:
                target-url: http://upstream:8081
                latency-ms: 3000
                duration-seconds: 30
            - name: return-errors
              type: HttpError
              config:
                target-url: http://upstream:8082
                status-code: 503
                duration-seconds: 20
            - name: kill-worker
              type: ProcessTerminate
              config:
                process-name: worker
            - name: exhaust-cpu
              type: ResourceExhaustion
              config:
                resource-type: Cpu
                duration-seconds: 10
                intensity-percent: 75
        rollback:
          enabled: true
          timeout-seconds: 90
        """;

    [Fact]
    public void Load_ValidYaml_ReturnsExperiment()
    {
        var exp = _loader.Load(FullYaml);
        exp.Name.Should().Be("http-resilience-test");
        exp.Description.Should().Be("Verify service handles upstream latency");
    }

    [Fact]
    public void Load_HttpProbe_ParsesConfig()
    {
        var exp = _loader.Load(FullYaml);
        var probe = exp.SteadyState.Probes.First(p => p.Name == "health-check");
        probe.Type.Should().Be(ProbeType.Http);
        var config = probe.Config.Should().BeOfType<HttpProbeConfig>().Subject;
        config.Url.Should().Be("http://localhost:8080/health");
        config.Method.Should().Be("GET");
        config.TimeoutSeconds.Should().Be(5);
    }

    [Fact]
    public void Load_HttpProbe_ParsesTolerance()
    {
        var exp = _loader.Load(FullYaml);
        var probe = exp.SteadyState.Probes.First(p => p.Name == "health-check");
        var tol = probe.Tolerance.Should().BeOfType<HttpProbeTolerance>().Subject;
        tol.StatusCode.Should().Be(200);
        tol.MaxLatencyMs.Should().Be(500);
    }

    [Fact]
    public void Load_ProcessProbe_ParsesConfig()
    {
        var exp = _loader.Load(FullYaml);
        var probe = exp.SteadyState.Probes.First(p => p.Name == "service-process");
        probe.Type.Should().Be(ProbeType.Process);
        var config = probe.Config.Should().BeOfType<ProcessProbeConfig>().Subject;
        config.ProcessName.Should().Be("myservice");
        var tol = probe.Tolerance.Should().BeOfType<ProcessProbeTolerance>().Subject;
        tol.Running.Should().BeTrue();
    }

    [Fact]
    public void Load_CommandProbe_ParsesConfig()
    {
        var exp = _loader.Load(FullYaml);
        var probe = exp.SteadyState.Probes.First(p => p.Name == "ping-command");
        probe.Type.Should().Be(ProbeType.Command);
        var config = probe.Config.Should().BeOfType<CommandProbeConfig>().Subject;
        config.Command.Should().Be("echo");
        config.Arguments.Should().Be("hello");
        var tol = probe.Tolerance.Should().BeOfType<CommandProbeTolerance>().Subject;
        tol.ExitCode.Should().Be(0);
        tol.OutputContains.Should().Be("hello");
    }

    [Fact]
    public void Load_HttpLatencyFault_ParsesConfig()
    {
        var exp = _loader.Load(FullYaml);
        var action = exp.Method.Actions.First(a => a.Name == "add-latency");
        action.Type.Should().Be(FaultType.HttpLatency);
        var config = action.Config.Should().BeOfType<HttpLatencyFaultConfig>().Subject;
        config.TargetUrl.Should().Be("http://upstream:8081");
        config.LatencyMs.Should().Be(3000);
        config.DurationSeconds.Should().Be(30);
    }

    [Fact]
    public void Load_HttpErrorFault_ParsesConfig()
    {
        var exp = _loader.Load(FullYaml);
        var action = exp.Method.Actions.First(a => a.Name == "return-errors");
        action.Type.Should().Be(FaultType.HttpError);
        var config = action.Config.Should().BeOfType<HttpErrorFaultConfig>().Subject;
        config.TargetUrl.Should().Be("http://upstream:8082");
        config.StatusCode.Should().Be(503);
        config.DurationSeconds.Should().Be(20);
    }

    [Fact]
    public void Load_ProcessTerminateFault_ParsesConfig()
    {
        var exp = _loader.Load(FullYaml);
        var action = exp.Method.Actions.First(a => a.Name == "kill-worker");
        action.Type.Should().Be(FaultType.ProcessTerminate);
        var config = action.Config.Should().BeOfType<ProcessTerminateFaultConfig>().Subject;
        config.ProcessName.Should().Be("worker");
    }

    [Fact]
    public void Load_ResourceExhaustionFault_ParsesConfig()
    {
        var exp = _loader.Load(FullYaml);
        var action = exp.Method.Actions.First(a => a.Name == "exhaust-cpu");
        action.Type.Should().Be(FaultType.ResourceExhaustion);
        var config = action.Config.Should().BeOfType<ResourceExhaustionFaultConfig>().Subject;
        config.ResourceType.Should().Be(ResourceType.Cpu);
        config.DurationSeconds.Should().Be(10);
        config.IntensityPercent.Should().Be(75);
    }

    [Fact]
    public void Load_RollbackConfig_ParsesCorrectly()
    {
        var exp = _loader.Load(FullYaml);
        exp.Rollback.Enabled.Should().BeTrue();
        exp.Rollback.TimeoutSeconds.Should().Be(90);
    }

    [Fact]
    public void Load_InvalidProbeType_ThrowsException()
    {
        var yaml = """
            name: test
            description: test
            steady-state:
              title: title
              probes:
                - name: bad-probe
                  type: UnknownType
                  config:
                    url: http://example.com
                  tolerance:
                    status-code: 200
            method:
              name: method
              actions: []
            rollback:
              enabled: true
              timeout-seconds: 60
            """;

        _loader.Invoking(l => l.Load(yaml))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*UnknownType*");
    }

    [Fact]
    public void Load_InvalidFaultType_ThrowsException()
    {
        var yaml = """
            name: test
            description: test
            steady-state:
              title: title
              probes: []
            method:
              name: method
              actions:
                - name: bad-fault
                  type: UnknownFault
                  config:
                    target-url: http://example.com
            rollback:
              enabled: true
              timeout-seconds: 60
            """;

        _loader.Invoking(l => l.Load(yaml))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*UnknownFault*");
    }

    [Fact]
    public void Load_MinimalYaml_UsesDefaults()
    {
        var yaml = """
            name: minimal
            description: minimal test
            steady-state:
              title: baseline
              probes:
                - name: check
                  type: Http
                  config:
                    url: http://localhost/health
                  tolerance:
                    status-code: 200
            method:
              name: no-op
              actions: []
            rollback:
              enabled: false
              timeout-seconds: 60
            """;

        var exp = _loader.Load(yaml);
        exp.Name.Should().Be("minimal");
        var probe = exp.SteadyState.Probes.Single();
        var config = probe.Config.Should().BeOfType<HttpProbeConfig>().Subject;
        config.Method.Should().Be("GET");
        config.TimeoutSeconds.Should().Be(5);
        exp.Rollback.Enabled.Should().BeFalse();
    }
}
