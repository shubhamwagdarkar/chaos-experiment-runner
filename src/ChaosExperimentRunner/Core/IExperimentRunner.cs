using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Core;

public interface IExperimentRunner
{
    Task<ExperimentResult> RunAsync(ChaosExperiment experiment, CancellationToken ct = default);
}
