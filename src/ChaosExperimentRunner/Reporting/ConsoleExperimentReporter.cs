using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Reporting;

public sealed class ConsoleExperimentReporter : IExperimentReporter
{
    private readonly TextWriter _output;

    public ConsoleExperimentReporter() : this(Console.Out) { }

    public ConsoleExperimentReporter(TextWriter output)
    {
        _output = output;
    }

    public void Report(ExperimentResult result)
    {
        _output.WriteLine();
        _output.WriteLine(new string('=', 60));
        _output.WriteLine($"  EXPERIMENT: {result.ExperimentName}");
        _output.WriteLine($"  STATUS:     {result.Status.ToString().ToUpperInvariant()}");
        _output.WriteLine($"  DURATION:   {result.Duration.TotalSeconds:F2}s");
        _output.WriteLine($"  STARTED:    {result.StartedAt:u}");
        _output.WriteLine(new string('=', 60));

        _output.WriteLine();
        _output.WriteLine($"  STEADY STATE (BEFORE): {(result.BeforeState.Passed ? "PASS" : "FAIL")}");
        foreach (var probe in result.BeforeState.ProbeResults)
            _output.WriteLine($"    [{(probe.Passed ? "PASS" : "FAIL")}] {probe.ProbeName} ({probe.LatencyMs}ms) — {probe.Message}");

        _output.WriteLine();
        _output.WriteLine($"  METHOD ACTIONS: {result.ActionResults.Count}");
        foreach (var action in result.ActionResults)
            _output.WriteLine($"    [{(action.Succeeded ? "OK  " : "FAIL")}] {action.ActionName} — {action.Message}");

        _output.WriteLine();
        _output.WriteLine($"  STEADY STATE (AFTER): {(result.AfterState.Passed ? "PASS" : "FAIL")}");
        foreach (var probe in result.AfterState.ProbeResults)
            _output.WriteLine($"    [{(probe.Passed ? "PASS" : "FAIL")}] {probe.ProbeName} ({probe.LatencyMs}ms) — {probe.Message}");

        if (result.FailureReason is not null)
        {
            _output.WriteLine();
            _output.WriteLine($"  FAILURE REASON: {result.FailureReason}");
        }

        _output.WriteLine();
        _output.WriteLine(new string('=', 60));
        _output.WriteLine();
    }
}
