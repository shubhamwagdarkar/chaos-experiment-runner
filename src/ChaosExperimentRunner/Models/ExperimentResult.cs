namespace ChaosExperimentRunner.Models;

public sealed record ProbeResult(
    string ProbeName,
    bool Passed,
    long LatencyMs = 0,
    string? Message = null
);

public sealed record SteadyStateResult(
    string HypothesisTitle,
    bool Passed,
    IReadOnlyList<ProbeResult> ProbeResults
);

public sealed record ActionResult(
    string ActionName,
    bool Succeeded,
    string? Message = null
);

public sealed record ExperimentResult(
    string ExperimentName,
    ExperimentStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    SteadyStateResult BeforeState,
    SteadyStateResult AfterState,
    IReadOnlyList<ActionResult> ActionResults,
    string? FailureReason = null
)
{
    public TimeSpan Duration => CompletedAt - StartedAt;
    public bool SteadyStateViolated => !BeforeState.Passed || !AfterState.Passed;
}
