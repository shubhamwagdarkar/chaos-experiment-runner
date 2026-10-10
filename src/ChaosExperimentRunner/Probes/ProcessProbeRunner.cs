using System.Diagnostics;
using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Probes;

public interface IProcessWrapper
{
    bool IsRunning(string processName);
}

public sealed class SystemProcessWrapper : IProcessWrapper
{
    public bool IsRunning(string processName) =>
        Process.GetProcessesByName(processName).Length > 0;
}

public sealed class ProcessProbeRunner : IProbeRunner
{
    private readonly IProcessWrapper _processWrapper;

    public ProcessProbeRunner(IProcessWrapper processWrapper)
    {
        _processWrapper = processWrapper;
    }

    public bool CanHandle(ProbeType type) => type == ProbeType.Process;

    public Task<ProbeResult> RunAsync(ExperimentProbe probe, CancellationToken ct = default)
    {
        if (probe.Config is not ProcessProbeConfig config)
            return Task.FromResult(new ProbeResult(probe.Name, false, Message: "Invalid probe config: expected ProcessProbeConfig"));
        if (probe.Tolerance is not ProcessProbeTolerance tolerance)
            return Task.FromResult(new ProbeResult(probe.Name, false, Message: "Invalid probe tolerance: expected ProcessProbeTolerance"));

        bool isRunning = _processWrapper.IsRunning(config.ProcessName);
        bool passed = isRunning == tolerance.Running;

        string message = passed ? "OK"
            : tolerance.Running
                ? $"Process '{config.ProcessName}' is not running"
                : $"Process '{config.ProcessName}' is still running";

        return Task.FromResult(new ProbeResult(probe.Name, passed, 0, message));
    }
}
