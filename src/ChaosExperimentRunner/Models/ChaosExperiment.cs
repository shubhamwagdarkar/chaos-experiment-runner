namespace ChaosExperimentRunner.Models;

public sealed record ChaosExperiment(
    string Name,
    string Description,
    SteadyStateHypothesis SteadyState,
    ExperimentMethod Method,
    RollbackConfig Rollback
);

public sealed record SteadyStateHypothesis(
    string Title,
    IReadOnlyList<ExperimentProbe> Probes
);

public sealed record ExperimentProbe(
    string Name,
    ProbeType Type,
    ProbeConfig Config,
    ProbeTolerance Tolerance
);

public sealed record ExperimentMethod(
    string Name,
    IReadOnlyList<FaultAction> Actions
);

public sealed record FaultAction(
    string Name,
    FaultType Type,
    FaultConfig Config
);

public sealed record RollbackConfig(bool Enabled = true, int TimeoutSeconds = 60);

// Probe configs
public abstract record ProbeConfig;
public sealed record HttpProbeConfig(
    string Url,
    string Method = "GET",
    int TimeoutSeconds = 5
) : ProbeConfig;

public sealed record ProcessProbeConfig(string ProcessName) : ProbeConfig;

public sealed record CommandProbeConfig(
    string Command,
    string Arguments = ""
) : ProbeConfig;

// Probe tolerances
public abstract record ProbeTolerance;
public sealed record HttpProbeTolerance(
    int StatusCode = 200,
    int? MaxLatencyMs = null,
    string? BodyContains = null
) : ProbeTolerance;

public sealed record ProcessProbeTolerance(bool Running = true) : ProbeTolerance;

public sealed record CommandProbeTolerance(
    int ExitCode = 0,
    string? OutputContains = null
) : ProbeTolerance;

// Fault configs
public abstract record FaultConfig;
public sealed record HttpLatencyFaultConfig(
    string TargetUrl,
    int LatencyMs,
    int DurationSeconds = 30
) : FaultConfig;

public sealed record HttpErrorFaultConfig(
    string TargetUrl,
    int StatusCode = 503,
    int DurationSeconds = 30
) : FaultConfig;

public sealed record ProcessTerminateFaultConfig(string ProcessName) : FaultConfig;

public sealed record ResourceExhaustionFaultConfig(
    ResourceType ResourceType,
    int DurationSeconds = 30,
    int IntensityPercent = 50
) : FaultConfig;
