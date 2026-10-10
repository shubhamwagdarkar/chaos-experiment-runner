using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Core;

public sealed class SteadyStateVerifier : ISteadyStateVerifier
{
    private readonly IEnumerable<IProbeRunner> _runners;

    public SteadyStateVerifier(IEnumerable<IProbeRunner> runners)
    {
        _runners = runners;
    }

    public async Task<SteadyStateResult> VerifyAsync(SteadyStateHypothesis hypothesis, CancellationToken ct = default)
    {
        var results = new List<ProbeResult>();

        foreach (var probe in hypothesis.Probes)
        {
            var runner = _runners.FirstOrDefault(r => r.CanHandle(probe.Type))
                ?? throw new InvalidOperationException($"No probe runner registered for type '{probe.Type}'.");

            var result = await runner.RunAsync(probe, ct);
            results.Add(result);
        }

        bool allPassed = results.Count == 0 || results.All(r => r.Passed);
        return new SteadyStateResult(hypothesis.Title, allPassed, results.AsReadOnly());
    }
}
