using ChaosExperimentRunner.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ChaosExperimentRunner.Core;

// Intermediate DTOs for YAML deserialization
internal sealed class ExperimentDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public SteadyStateDto SteadyState { get; set; } = new();
    public MethodDto Method { get; set; } = new();
    public RollbackDto Rollback { get; set; } = new();
}

internal sealed class SteadyStateDto
{
    public string Title { get; set; } = string.Empty;
    public List<ProbeDto> Probes { get; set; } = new();
}

internal sealed class ProbeDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public ProbeConfigDto Config { get; set; } = new();
    public ProbeToleranceDto Tolerance { get; set; } = new();
}

internal sealed class ProbeConfigDto
{
    public string? Url { get; set; }
    public string? Method { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? ProcessName { get; set; }
    public string? Command { get; set; }
    public string? Arguments { get; set; }
}

internal sealed class ProbeToleranceDto
{
    public int? StatusCode { get; set; }
    public int? MaxLatencyMs { get; set; }
    public string? BodyContains { get; set; }
    public bool? Running { get; set; }
    public int? ExitCode { get; set; }
    public string? OutputContains { get; set; }
}

internal sealed class MethodDto
{
    public string Name { get; set; } = string.Empty;
    public List<FaultDto> Actions { get; set; } = new();
}

internal sealed class FaultDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public FaultConfigDto Config { get; set; } = new();
}

internal sealed class FaultConfigDto
{
    public string? TargetUrl { get; set; }
    public int? LatencyMs { get; set; }
    public int? StatusCode { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ProcessName { get; set; }
    public string? ResourceType { get; set; }
    public int? IntensityPercent { get; set; }
}

internal sealed class RollbackDto
{
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class YamlExperimentLoader : IExperimentLoader
{
    private readonly IDeserializer _deserializer;

    public YamlExperimentLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public ChaosExperiment Load(string yaml)
    {
        var dto = _deserializer.Deserialize<ExperimentDto>(yaml)
            ?? throw new InvalidOperationException("Failed to deserialize experiment YAML.");

        return MapToDomain(dto);
    }

    public ChaosExperiment LoadFromFile(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        return Load(yaml);
    }

    private static ChaosExperiment MapToDomain(ExperimentDto dto)
    {
        var probes = dto.SteadyState.Probes
            .Select(MapProbe)
            .ToList()
            .AsReadOnly();

        var actions = dto.Method.Actions
            .Select(MapFaultAction)
            .ToList()
            .AsReadOnly();

        return new ChaosExperiment(
            dto.Name,
            dto.Description,
            new SteadyStateHypothesis(dto.SteadyState.Title, probes),
            new ExperimentMethod(dto.Method.Name, actions),
            new RollbackConfig(dto.Rollback.Enabled, dto.Rollback.TimeoutSeconds)
        );
    }

    private static ExperimentProbe MapProbe(ProbeDto dto)
    {
        var type = ParseEnum<ProbeType>(dto.Type, "probe");
        var (config, tolerance) = type switch
        {
            ProbeType.Http => (
                (ProbeConfig)new HttpProbeConfig(
                    dto.Config.Url ?? throw new InvalidOperationException($"Probe '{dto.Name}': url is required for Http probes."),
                    dto.Config.Method ?? "GET",
                    dto.Config.TimeoutSeconds ?? 5),
                (ProbeTolerance)new HttpProbeTolerance(
                    dto.Tolerance.StatusCode ?? 200,
                    dto.Tolerance.MaxLatencyMs,
                    dto.Tolerance.BodyContains)),
            ProbeType.Process => (
                (ProbeConfig)new ProcessProbeConfig(
                    dto.Config.ProcessName ?? throw new InvalidOperationException($"Probe '{dto.Name}': process-name is required for Process probes.")),
                (ProbeTolerance)new ProcessProbeTolerance(
                    dto.Tolerance.Running ?? true)),
            ProbeType.Command => (
                (ProbeConfig)new CommandProbeConfig(
                    dto.Config.Command ?? throw new InvalidOperationException($"Probe '{dto.Name}': command is required for Command probes."),
                    dto.Config.Arguments ?? ""),
                (ProbeTolerance)new CommandProbeTolerance(
                    dto.Tolerance.ExitCode ?? 0,
                    dto.Tolerance.OutputContains)),
            _ => throw new InvalidOperationException($"Unsupported probe type: {dto.Type}")
        };

        return new ExperimentProbe(dto.Name, type, config, tolerance);
    }

    private static FaultAction MapFaultAction(FaultDto dto)
    {
        var type = ParseEnum<FaultType>(dto.Type, "fault");
        var config = type switch
        {
            FaultType.HttpLatency => (FaultConfig)new HttpLatencyFaultConfig(
                dto.Config.TargetUrl ?? throw new InvalidOperationException($"Fault '{dto.Name}': target-url is required."),
                dto.Config.LatencyMs ?? throw new InvalidOperationException($"Fault '{dto.Name}': latency-ms is required."),
                dto.Config.DurationSeconds ?? 30),
            FaultType.HttpError => new HttpErrorFaultConfig(
                dto.Config.TargetUrl ?? throw new InvalidOperationException($"Fault '{dto.Name}': target-url is required."),
                dto.Config.StatusCode ?? 503,
                dto.Config.DurationSeconds ?? 30),
            FaultType.ProcessTerminate => new ProcessTerminateFaultConfig(
                dto.Config.ProcessName ?? throw new InvalidOperationException($"Fault '{dto.Name}': process-name is required.")),
            FaultType.ResourceExhaustion => new ResourceExhaustionFaultConfig(
                ParseEnum<ResourceType>(dto.Config.ResourceType ?? "Cpu", "resource"),
                dto.Config.DurationSeconds ?? 30,
                dto.Config.IntensityPercent ?? 50),
            _ => throw new InvalidOperationException($"Unsupported fault type: {dto.Type}")
        };

        return new FaultAction(dto.Name, type, config);
    }

    private static T ParseEnum<T>(string value, string context) where T : struct, Enum
    {
        if (!Enum.TryParse<T>(value, ignoreCase: true, out var result))
            throw new InvalidOperationException($"Unknown {context} type: '{value}'. Valid values: {string.Join(", ", Enum.GetNames<T>())}");
        return result;
    }
}
