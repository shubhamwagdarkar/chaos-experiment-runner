using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Faults;

public sealed class HttpErrorFaultRunner : IFaultRunner
{
    private readonly FaultRegistry _registry;

    public HttpErrorFaultRunner(FaultRegistry registry)
    {
        _registry = registry;
    }

    public bool CanHandle(FaultType type) => type == FaultType.HttpError;

    public Task<ActionResult> ExecuteAsync(FaultAction action, CancellationToken ct = default)
    {
        if (action.Config is not HttpErrorFaultConfig config)
            return Task.FromResult(new ActionResult(action.Name, false, "Invalid fault config: expected HttpErrorFaultConfig"));

        var faultId = $"error-{Guid.NewGuid():N}";
        var fault = new ActiveFault(
            faultId,
            FaultType.HttpError,
            config.TargetUrl,
            StatusCode: config.StatusCode,
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(config.DurationSeconds));

        _registry.Register(faultId, fault);

        return Task.FromResult(new ActionResult(action.Name, true,
            $"Injected HTTP {config.StatusCode} errors on '{config.TargetUrl}' for {config.DurationSeconds}s [id={faultId}]"));
    }

    public Task RollbackAsync(FaultAction action, CancellationToken ct = default)
    {
        if (action.Config is HttpErrorFaultConfig config)
            _registry.RemoveByTarget(FaultType.HttpError, config.TargetUrl);

        return Task.CompletedTask;
    }
}
