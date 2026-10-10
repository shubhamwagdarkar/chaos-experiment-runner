using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Core;

public interface ISteadyStateVerifier
{
    Task<SteadyStateResult> VerifyAsync(SteadyStateHypothesis hypothesis, CancellationToken ct = default);
}
