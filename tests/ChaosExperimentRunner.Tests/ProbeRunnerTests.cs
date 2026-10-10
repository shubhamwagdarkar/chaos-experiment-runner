using System.Net;
using ChaosExperimentRunner.Models;
using ChaosExperimentRunner.Probes;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ChaosExperimentRunner.Tests;

public sealed class ProbeRunnerTests
{
    // Helper: fake HttpMessageHandler
    private static HttpClient MakeHttpClient(HttpStatusCode statusCode, string body = "")
    {
        var handler = new FakeHttpHandler(statusCode, body);
        return new HttpClient(handler);
    }

    private static ExperimentProbe MakeHttpProbe(string url = "http://test/health",
        int expectedStatus = 200, int? maxLatencyMs = null, string? bodyContains = null) =>
        new("health-probe", ProbeType.Http,
            new HttpProbeConfig(url, "GET", 5),
            new HttpProbeTolerance(expectedStatus, maxLatencyMs, bodyContains));

    private static ExperimentProbe MakeProcessProbe(string name, bool expectRunning) =>
        new("process-probe", ProbeType.Process,
            new ProcessProbeConfig(name),
            new ProcessProbeTolerance(expectRunning));

    private static ExperimentProbe MakeCommandProbe(string cmd, int exitCode = 0, string? outputContains = null) =>
        new("cmd-probe", ProbeType.Command,
            new CommandProbeConfig(cmd, ""),
            new CommandProbeTolerance(exitCode, outputContains));

    // HttpProbeRunner
    [Fact]
    public void HttpProbeRunner_CanHandle_Http()
    {
        var runner = new HttpProbeRunner(MakeHttpClient(HttpStatusCode.OK));
        runner.CanHandle(ProbeType.Http).Should().BeTrue();
    }

    [Fact]
    public void HttpProbeRunner_CannotHandle_Process()
    {
        var runner = new HttpProbeRunner(MakeHttpClient(HttpStatusCode.OK));
        runner.CanHandle(ProbeType.Process).Should().BeFalse();
    }

    [Fact]
    public async Task HttpProbeRunner_WhenStatusCodeMatches_ReturnsPassed()
    {
        var runner = new HttpProbeRunner(MakeHttpClient(HttpStatusCode.OK));
        var result = await runner.RunAsync(MakeHttpProbe(expectedStatus: 200));
        result.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task HttpProbeRunner_WhenStatusCodeMismatch_ReturnsFailed()
    {
        var runner = new HttpProbeRunner(MakeHttpClient(HttpStatusCode.ServiceUnavailable));
        var result = await runner.RunAsync(MakeHttpProbe(expectedStatus: 200));
        result.Passed.Should().BeFalse();
        result.Message.Should().Contain("503");
    }

    [Fact]
    public async Task HttpProbeRunner_WhenBodyMatches_ReturnsPassed()
    {
        var runner = new HttpProbeRunner(MakeHttpClient(HttpStatusCode.OK, "status: healthy"));
        var result = await runner.RunAsync(MakeHttpProbe(bodyContains: "healthy"));
        result.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task HttpProbeRunner_WhenBodyDoesNotMatch_ReturnsFailed()
    {
        var runner = new HttpProbeRunner(MakeHttpClient(HttpStatusCode.OK, "status: degraded"));
        var result = await runner.RunAsync(MakeHttpProbe(bodyContains: "healthy"));
        result.Passed.Should().BeFalse();
        result.Message.Should().Contain("healthy");
    }

    [Fact]
    public async Task HttpProbeRunner_WhenRequestFails_ReturnsFailed()
    {
        var client = new HttpClient(new ThrowingHttpHandler());
        var runner = new HttpProbeRunner(client);
        var result = await runner.RunAsync(MakeHttpProbe());
        result.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task HttpProbeRunner_RecordsLatency()
    {
        var runner = new HttpProbeRunner(MakeHttpClient(HttpStatusCode.OK));
        var result = await runner.RunAsync(MakeHttpProbe());
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    // ProcessProbeRunner
    [Fact]
    public void ProcessProbeRunner_CanHandle_Process()
    {
        var wrapper = Substitute.For<IProcessWrapper>();
        var runner = new ProcessProbeRunner(wrapper);
        runner.CanHandle(ProbeType.Process).Should().BeTrue();
    }

    [Fact]
    public void ProcessProbeRunner_CannotHandle_Http()
    {
        var wrapper = Substitute.For<IProcessWrapper>();
        var runner = new ProcessProbeRunner(wrapper);
        runner.CanHandle(ProbeType.Http).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessProbeRunner_WhenProcessRunning_AndExpectedRunning_ReturnsPassed()
    {
        var wrapper = Substitute.For<IProcessWrapper>();
        wrapper.IsRunning("myservice").Returns(true);
        var runner = new ProcessProbeRunner(wrapper);

        var result = await runner.RunAsync(MakeProcessProbe("myservice", expectRunning: true));
        result.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessProbeRunner_WhenProcessNotRunning_AndExpectedRunning_ReturnsFailed()
    {
        var wrapper = Substitute.For<IProcessWrapper>();
        wrapper.IsRunning("myservice").Returns(false);
        var runner = new ProcessProbeRunner(wrapper);

        var result = await runner.RunAsync(MakeProcessProbe("myservice", expectRunning: true));
        result.Passed.Should().BeFalse();
        result.Message.Should().Contain("not running");
    }

    [Fact]
    public async Task ProcessProbeRunner_WhenProcessRunning_AndExpectedStopped_ReturnsFailed()
    {
        var wrapper = Substitute.For<IProcessWrapper>();
        wrapper.IsRunning("myservice").Returns(true);
        var runner = new ProcessProbeRunner(wrapper);

        var result = await runner.RunAsync(MakeProcessProbe("myservice", expectRunning: false));
        result.Passed.Should().BeFalse();
        result.Message.Should().Contain("still running");
    }

    // CommandProbeRunner
    [Fact]
    public void CommandProbeRunner_CanHandle_Command()
    {
        var executor = Substitute.For<ICommandExecutor>();
        var runner = new CommandProbeRunner(executor);
        runner.CanHandle(ProbeType.Command).Should().BeTrue();
    }

    [Fact]
    public async Task CommandProbeRunner_WhenExitCodeMatches_ReturnsPassed()
    {
        var executor = Substitute.For<ICommandExecutor>();
        executor.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((0, "success"));

        var runner = new CommandProbeRunner(executor);
        var result = await runner.RunAsync(MakeCommandProbe("check.sh", exitCode: 0));

        result.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task CommandProbeRunner_WhenExitCodeMismatch_ReturnsFailed()
    {
        var executor = Substitute.For<ICommandExecutor>();
        executor.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((1, "error"));

        var runner = new CommandProbeRunner(executor);
        var result = await runner.RunAsync(MakeCommandProbe("check.sh", exitCode: 0));

        result.Passed.Should().BeFalse();
        result.Message.Should().Contain("exit code");
    }

    [Fact]
    public async Task CommandProbeRunner_WhenOutputContainsMatch_ReturnsPassed()
    {
        var executor = Substitute.For<ICommandExecutor>();
        executor.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((0, "status: healthy, uptime: 99.9%"));

        var runner = new CommandProbeRunner(executor);
        var result = await runner.RunAsync(MakeCommandProbe("status.sh", outputContains: "healthy"));

        result.Passed.Should().BeTrue();
    }
}

// HTTP handlers for testing
internal sealed class ThrowingHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new HttpRequestException("Connection refused");
}

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _body;

    public FakeHttpHandler(HttpStatusCode statusCode, string body = "")
    {
        _statusCode = statusCode;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_body)
        };
        return Task.FromResult(response);
    }
}
