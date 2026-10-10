using System.Collections.Concurrent;
using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Core;

public sealed record ActiveFault(
    string Id,
    FaultType Type,
    string TargetUrl,
    int LatencyMs = 0,
    int StatusCode = 0,
    DateTimeOffset ExpiresAt = default
);

public sealed class FaultRegistry
{
    private readonly ConcurrentDictionary<string, ActiveFault> _activeFaults = new();

    public void Register(string faultId, ActiveFault fault) =>
        _activeFaults[faultId] = fault;

    public bool TryRemove(string faultId, out ActiveFault? fault) =>
        _activeFaults.TryRemove(faultId, out fault);

    public IReadOnlyList<ActiveFault> GetActiveFaults() =>
        _activeFaults.Values.ToList().AsReadOnly();

    public ActiveFault? GetFaultForUrl(string url) =>
        _activeFaults.Values.FirstOrDefault(f =>
            url.Contains(f.TargetUrl, StringComparison.OrdinalIgnoreCase));

    public void RemoveByTarget(FaultType type, string targetUrl)
    {
        var toRemove = _activeFaults.Values
            .Where(f => f.Type == type && f.TargetUrl.Equals(targetUrl, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Id)
            .ToList();

        foreach (var id in toRemove)
            _activeFaults.TryRemove(id, out _);
    }

    public int Count => _activeFaults.Count;
}
