using System.Diagnostics;
using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Faults;

public interface IProcessKiller
{
    bool TryKill(string processName, out string message);
}

public sealed class SystemProcessKiller : IProcessKiller
{
    public bool TryKill(string processName, out string message)
    {
        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            message = $"No process found with name '{processName}'";
            return false;
        }

        int killed = 0;
        foreach (var p in processes)
        {
            try { p.Kill(); killed++; }
            catch { /* ignore individual kill failures */ }
        }

        message = $"Terminated {killed}/{processes.Length} instance(s) of '{processName}'";
        return killed > 0;
    }
}

public sealed class ProcessTerminateFaultRunner : IFaultRunner
{
    private readonly IProcessKiller _killer;

    public ProcessTerminateFaultRunner(IProcessKiller killer)
    {
        _killer = killer;
    }

    public bool CanHandle(FaultType type) => type == FaultType.ProcessTerminate;

    public Task<ActionResult> ExecuteAsync(FaultAction action, CancellationToken ct = default)
    {
        if (action.Config is not ProcessTerminateFaultConfig config)
            return Task.FromResult(new ActionResult(action.Name, false, "Invalid fault config: expected ProcessTerminateFaultConfig"));

        bool success = _killer.TryKill(config.ProcessName, out string message);
        return Task.FromResult(new ActionResult(action.Name, success, message));
    }

    public Task RollbackAsync(FaultAction action, CancellationToken ct = default)
    {
        // Process termination is not reversible — nothing to roll back
        return Task.CompletedTask;
    }
}
