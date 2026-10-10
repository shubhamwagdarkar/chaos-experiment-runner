using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Core;

public interface IProbeRunner
{
    bool CanHandle(ProbeType type);
    Task<ProbeResult> RunAsync(ExperimentProbe probe, CancellationToken ct = default);
}
