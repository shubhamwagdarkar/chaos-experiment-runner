using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Faults;

public interface IResourceStressor
{
    Task<string> StartAsync(ResourceType resourceType, int intensityPercent, int durationSeconds, CancellationToken ct = default);
    void Stop(string stressorId);
}

public sealed class BackgroundResourceStressor : IResourceStressor
{
    private readonly Dictionary<string, CancellationTokenSource> _active = new();

    public Task<string> StartAsync(ResourceType resourceType, int intensityPercent, int durationSeconds, CancellationToken ct = default)
    {
        var stressorId = $"stress-{Guid.NewGuid():N}";
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
        _active[stressorId] = cts;

        _ = resourceType == ResourceType.Cpu
            ? Task.Run(() => BurnCpu(intensityPercent, cts.Token), cts.Token)
            : Task.Run(() => AllocateMemory(intensityPercent, cts.Token), cts.Token);

        return Task.FromResult(stressorId);
    }

    public void Stop(string stressorId)
    {
        if (_active.TryGetValue(stressorId, out var cts))
        {
            cts.Cancel();
            _active.Remove(stressorId);
        }
    }

    private static void BurnCpu(int intensityPercent, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var end = DateTime.UtcNow.AddMilliseconds(intensityPercent);
            while (DateTime.UtcNow < end && !ct.IsCancellationRequested) { /* spin */ }
            if (!ct.IsCancellationRequested)
                Thread.Sleep(100 - intensityPercent);
        }
    }

    private static void AllocateMemory(int intensityPercent, CancellationToken ct)
    {
        var mb = intensityPercent * 10; // 1% = 10MB, 100% = 1GB
        var buffer = new byte[mb * 1024 * 1024];
        new Random().NextBytes(buffer); // prevent optimization
        ct.WaitHandle.WaitOne();
        GC.KeepAlive(buffer);
    }
}

public sealed class ResourceExhaustionFaultRunner : IFaultRunner
{
    private readonly IResourceStressor _stressor;
    private readonly Dictionary<string, string> _activeStressors = new();

    public ResourceExhaustionFaultRunner(IResourceStressor stressor)
    {
        _stressor = stressor;
    }

    public bool CanHandle(FaultType type) => type == FaultType.ResourceExhaustion;

    public async Task<ActionResult> ExecuteAsync(FaultAction action, CancellationToken ct = default)
    {
        if (action.Config is not ResourceExhaustionFaultConfig config)
            return new ActionResult(action.Name, false, "Invalid fault config: expected ResourceExhaustionFaultConfig");

        var stressorId = await _stressor.StartAsync(
            config.ResourceType, config.IntensityPercent, config.DurationSeconds, ct);

        _activeStressors[action.Name] = stressorId;

        return new ActionResult(action.Name, true,
            $"Started {config.ResourceType} exhaustion at {config.IntensityPercent}% intensity for {config.DurationSeconds}s [id={stressorId}]");
    }

    public Task RollbackAsync(FaultAction action, CancellationToken ct = default)
    {
        if (_activeStressors.TryGetValue(action.Name, out var stressorId))
        {
            _stressor.Stop(stressorId);
            _activeStressors.Remove(action.Name);
        }
        return Task.CompletedTask;
    }
}
