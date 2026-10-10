using System.Diagnostics;
using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Probes;

public interface ICommandExecutor
{
    Task<(int ExitCode, string Output)> ExecuteAsync(string command, string arguments, CancellationToken ct = default);
}

public sealed class SystemCommandExecutor : ICommandExecutor
{
    public async Task<(int ExitCode, string Output)> ExecuteAsync(string command, string arguments, CancellationToken ct = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(command, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, output.Trim());
    }
}

public sealed class CommandProbeRunner : IProbeRunner
{
    private readonly ICommandExecutor _executor;

    public CommandProbeRunner(ICommandExecutor executor)
    {
        _executor = executor;
    }

    public bool CanHandle(ProbeType type) => type == ProbeType.Command;

    public async Task<ProbeResult> RunAsync(ExperimentProbe probe, CancellationToken ct = default)
    {
        if (probe.Config is not CommandProbeConfig config)
            return new ProbeResult(probe.Name, false, Message: "Invalid probe config: expected CommandProbeConfig");
        if (probe.Tolerance is not CommandProbeTolerance tolerance)
            return new ProbeResult(probe.Name, false, Message: "Invalid probe tolerance: expected CommandProbeTolerance");

        var sw = Stopwatch.StartNew();
        try
        {
            var (exitCode, output) = await _executor.ExecuteAsync(config.Command, config.Arguments, ct);
            sw.Stop();

            bool exitCodeMatch = exitCode == tolerance.ExitCode;
            bool outputMatch = tolerance.OutputContains is null
                || output.Contains(tolerance.OutputContains, StringComparison.OrdinalIgnoreCase);

            bool passed = exitCodeMatch && outputMatch;
            string message = passed ? "OK"
                : !exitCodeMatch ? $"Expected exit code {tolerance.ExitCode}, got {exitCode}"
                : $"Output does not contain '{tolerance.OutputContains}'";

            return new ProbeResult(probe.Name, passed, sw.ElapsedMilliseconds, message);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult(probe.Name, false, sw.ElapsedMilliseconds, ex.Message);
        }
    }
}
