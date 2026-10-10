using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Reporting;

public interface IExperimentReporter
{
    void Report(ExperimentResult result);
}
