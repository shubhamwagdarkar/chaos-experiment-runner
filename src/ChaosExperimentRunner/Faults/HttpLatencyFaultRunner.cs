using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Faults;

public sealed class HttpLatencyFaultRunner : IFaultRunner
{
    private readonly FaultRegistry _registry;

    public HttpLatencyFaultRunner(FaultRegistry registry)
    {
        _registry = registry;
    }

    public bool CanHandle(FaultType type) => type == FaultType.HttpLatency;

    public Task<ActionResult> ExecuteAsync(FaultAction action, CancellationToken ct = default)
    {
        if (action.Config is not HttpLatencyFaultConfig config)
            return Task.FromResult(new ActionResult(action.Name, false, "Invalid fault config: expected HttpLatencyFaultConfig"));

        var faultId = $"latency-{Guid.NewGuid():N}";
        var fault = new ActiveFault(
            faultId,
            FaultType.HttpLatency,
            config.TargetUrl,
            LatencyMs: config.LatencyMs,
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(config.DurationSeconds));

        _registry.Register(faultId, fault);

        return Task.FromResult(new ActionResult(action.Name, true,
            $"Injected {config.LatencyMs}ms latency on '{config.TargetUrl}' for {config.DurationSeconds}s [id={faultId}]"));
    }

    public Task RollbackAsync(FaultAction action, CancellationToken ct = default)
    {
        if (action.Config is HttpLatencyFaultConfig config)
            _registry.RemoveByTarget(FaultType.HttpLatency, config.TargetUrl);

        return Task.CompletedTask;
    }
}
