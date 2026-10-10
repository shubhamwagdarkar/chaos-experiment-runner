using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddChaosRunner();

var provider = services.BuildServiceProvider();

var args_list = args.ToList();
if (args_list.Count == 0 || args_list[0] is "-h" or "--help")
{
    Console.WriteLine("chaos-experiment-runner — run chaos engineering experiments");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  chaos-experiment-runner run <experiment.yaml>");
    Console.WriteLine("  chaos-experiment-runner run --dry-run <experiment.yaml>");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  chaos-experiment-runner run experiments/sample-http-resilience.yaml");
    return 0;
}

if (args_list[0] == "run")
{
    bool dryRun = args_list.Contains("--dry-run");
    string? filePath = args_list.LastOrDefault(a => !a.StartsWith('-'));

    if (filePath is null)
    {
        Console.Error.WriteLine("Error: experiment file path is required.");
        return 1;
    }

    if (!File.Exists(filePath))
    {
        Console.Error.WriteLine($"Error: file not found: {filePath}");
        return 1;
    }

    var loader = provider.GetRequiredService<IExperimentLoader>();
    var runner = provider.GetRequiredService<IExperimentRunner>();

    try
    {
        var experiment = loader.LoadFromFile(filePath);
        Console.WriteLine($"Loaded experiment: {experiment.Name}");
        Console.WriteLine($"  Description: {experiment.Description}");
        Console.WriteLine($"  Probes: {experiment.SteadyState.Probes.Count}");
        Console.WriteLine($"  Fault actions: {experiment.Method.Actions.Count}");

        if (dryRun)
        {
            Console.WriteLine();
            Console.WriteLine("[DRY RUN] Experiment would execute the above — no faults injected.");
            return 0;
        }

        var result = await runner.RunAsync(experiment);
        return result.Status == ChaosExperimentRunner.Models.ExperimentStatus.Passed ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

Console.Error.WriteLine($"Unknown command: {args_list[0]}. Use --help for usage.");
return 1;
