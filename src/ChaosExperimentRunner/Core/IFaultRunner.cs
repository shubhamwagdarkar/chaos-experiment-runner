using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Core;

public interface IFaultRunner
{
    bool CanHandle(FaultType type);
    Task<ActionResult> ExecuteAsync(FaultAction action, CancellationToken ct = default);
    Task RollbackAsync(FaultAction action, CancellationToken ct = default);
}
