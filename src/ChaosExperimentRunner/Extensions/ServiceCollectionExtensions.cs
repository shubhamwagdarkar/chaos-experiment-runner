using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Faults;
using ChaosExperimentRunner.Probes;
using ChaosExperimentRunner.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace ChaosExperimentRunner.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChaosRunner(this IServiceCollection services)
    {
        // Core
        services.AddSingleton<FaultRegistry>();
        services.AddSingleton<ISteadyStateVerifier, SteadyStateVerifier>();
        services.AddSingleton<IExperimentRunner, ExperimentRunner>();
        services.AddSingleton<IExperimentLoader, YamlExperimentLoader>();
        services.AddSingleton<IExperimentReporter, ConsoleExperimentReporter>();

        // HTTP client for probes
        services.AddHttpClient<HttpProbeRunner>();

        // Probe runners
        services.AddSingleton<IProcessWrapper, SystemProcessWrapper>();
        services.AddSingleton<ICommandExecutor, SystemCommandExecutor>();
        services.AddSingleton<IProbeRunner, HttpProbeRunner>(sp =>
            new HttpProbeRunner(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpProbeRunner))));
        services.AddSingleton<IProbeRunner, ProcessProbeRunner>();
        services.AddSingleton<IProbeRunner, CommandProbeRunner>();

        // Fault runners
        services.AddSingleton<IProcessKiller, SystemProcessKiller>();
        services.AddSingleton<IResourceStressor, BackgroundResourceStressor>();
        services.AddSingleton<IFaultRunner, HttpLatencyFaultRunner>();
        services.AddSingleton<IFaultRunner, HttpErrorFaultRunner>();
        services.AddSingleton<IFaultRunner, ProcessTerminateFaultRunner>();
        services.AddSingleton<IFaultRunner, ResourceExhaustionFaultRunner>();

        return services;
    }
}
