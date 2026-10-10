using ChaosExperimentRunner.Models;
using ChaosExperimentRunner.Reporting;
using Microsoft.Extensions.Logging;

namespace ChaosExperimentRunner.Core;

public sealed class ExperimentRunner : IExperimentRunner
{
    private readonly ISteadyStateVerifier _verifier;
    private readonly IEnumerable<IFaultRunner> _faultRunners;
    private readonly IExperimentReporter _reporter;
    private readonly ILogger<ExperimentRunner> _logger;

    public ExperimentRunner(
        ISteadyStateVerifier verifier,
        IEnumerable<IFaultRunner> faultRunners,
        IExperimentReporter reporter,
        ILogger<ExperimentRunner> logger)
    {
        _verifier = verifier;
        _faultRunners = faultRunners;
        _reporter = reporter;
        _logger = logger;
    }

    public async Task<ExperimentResult> RunAsync(ChaosExperiment experiment, CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var actionResults = new List<ActionResult>();
        var executedActions = new List<FaultAction>();

        _logger.LogInformation("Starting experiment: {Name}", experiment.Name);

        // 1. Verify steady state before
        _logger.LogInformation("Verifying steady state (before)");
        var beforeState = await _verifier.VerifyAsync(experiment.SteadyState, ct);

        if (!beforeState.Passed)
        {
            _logger.LogWarning("Steady state not met before experiment — aborting");
            var aborted = new ExperimentResult(
                experiment.Name, ExperimentStatus.Aborted, startedAt, DateTimeOffset.UtcNow,
                beforeState, beforeState, actionResults.AsReadOnly(),
                "Steady state not met before experiment — aborting");
            _reporter.Report(aborted);
            return aborted;
        }

        // 2. Execute fault actions
        var status = ExperimentStatus.Passed;
        string? failureReason = null;

        try
        {
            foreach (var action in experiment.Method.Actions)
            {
                if (ct.IsCancellationRequested) break;

                var runner = _faultRunners.FirstOrDefault(r => r.CanHandle(action.Type));
                if (runner is null)
                {
                    actionResults.Add(new ActionResult(action.Name, false, $"No runner for fault type '{action.Type}'"));
                    status = ExperimentStatus.Failed;
                    failureReason = $"No fault runner registered for type '{action.Type}'";
                    break;
                }

                _logger.LogInformation("Executing fault action: {Action}", action.Name);
                var actionResult = await runner.ExecuteAsync(action, ct);
                actionResults.Add(actionResult);

                if (!actionResult.Succeeded)
                {
                    status = ExperimentStatus.Failed;
                    failureReason = $"Fault action '{action.Name}' failed: {actionResult.Message}";
                    break;
                }

                executedActions.Add(action);
            }
        }
        catch (Exception ex)
        {
            status = ExperimentStatus.Failed;
            failureReason = $"Experiment method threw an exception: {ex.Message}";
            _logger.LogError(ex, "Experiment method failed");
        }

        // 3. Verify steady state after
        _logger.LogInformation("Verifying steady state (after)");
        var afterState = await _verifier.VerifyAsync(experiment.SteadyState, ct);

        if (!afterState.Passed && status == ExperimentStatus.Passed)
        {
            status = ExperimentStatus.Failed;
            failureReason = "System did not recover to steady state after fault injection — resilience gap detected";
        }

        // 4. Rollback faults
        var rollbackResults = new List<RollbackResult>();
        if (experiment.Rollback.Enabled && executedActions.Count > 0)
        {
            _logger.LogInformation("Rolling back {Count} fault action(s)", executedActions.Count);
            using var rollbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            rollbackCts.CancelAfter(TimeSpan.FromSeconds(experiment.Rollback.TimeoutSeconds));

            foreach (var action in executedActions)
            {
                var runner = _faultRunners.FirstOrDefault(r => r.CanHandle(action.Type));
                if (runner is null)
                {
                    rollbackResults.Add(new RollbackResult(action.Name, false, $"No runner found for fault type '{action.Type}'"));
                    continue;
                }

                try
                {
                    await runner.RollbackAsync(action, rollbackCts.Token);
                    rollbackResults.Add(new RollbackResult(action.Name, true, "Rolled back"));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Rollback failed for action '{Action}' — fault may still be active", action.Name);
                    rollbackResults.Add(new RollbackResult(action.Name, false, ex.Message));
                }
            }
        }

        var result = new ExperimentResult(
            experiment.Name, status, startedAt, DateTimeOffset.UtcNow,
            beforeState, afterState, actionResults.AsReadOnly(), failureReason,
            rollbackResults.Count > 0 ? rollbackResults.AsReadOnly() : null);

        _logger.LogInformation("Experiment completed: {Status}", status);
        _reporter.Report(result);
        return result;
    }
}
