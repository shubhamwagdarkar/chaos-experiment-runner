using System.Diagnostics;
using ChaosExperimentRunner.Core;
using ChaosExperimentRunner.Models;

namespace ChaosExperimentRunner.Probes;

public sealed class HttpProbeRunner : IProbeRunner
{
    private readonly HttpClient _httpClient;

    public HttpProbeRunner(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool CanHandle(ProbeType type) => type == ProbeType.Http;

    public async Task<ProbeResult> RunAsync(ExperimentProbe probe, CancellationToken ct = default)
    {
        if (probe.Config is not HttpProbeConfig config)
            return new ProbeResult(probe.Name, false, Message: "Invalid probe config: expected HttpProbeConfig");
        if (probe.Tolerance is not HttpProbeTolerance tolerance)
            return new ProbeResult(probe.Name, false, Message: "Invalid probe tolerance: expected HttpProbeTolerance");

        var sw = Stopwatch.StartNew();
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

            using var request = new HttpRequestMessage(new HttpMethod(config.Method), config.Url);
            using var response = await _httpClient.SendAsync(request, linkedCts.Token);
            sw.Stop();

            bool statusMatch = (int)response.StatusCode == tolerance.StatusCode;
            bool latencyOk = tolerance.MaxLatencyMs is null || sw.ElapsedMilliseconds <= tolerance.MaxLatencyMs;
            bool bodyMatch = true;

            if (tolerance.BodyContains is not null)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                bodyMatch = body.Contains(tolerance.BodyContains, StringComparison.OrdinalIgnoreCase);
            }

            bool passed = statusMatch && latencyOk && bodyMatch;
            string message = passed ? "OK"
                : !statusMatch ? $"Expected status {tolerance.StatusCode}, got {(int)response.StatusCode}"
                : !latencyOk ? $"Latency {sw.ElapsedMilliseconds}ms exceeded limit of {tolerance.MaxLatencyMs}ms"
                : $"Body does not contain '{tolerance.BodyContains}'";

            return new ProbeResult(probe.Name, passed, sw.ElapsedMilliseconds, message);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult(probe.Name, false, sw.ElapsedMilliseconds, ex.Message);
        }
    }
}
