using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Core;

public interface IExperimentLoader
{
    ChaosExperiment Load(string yaml);
    ChaosExperiment LoadFromFile(string filePath);
}
