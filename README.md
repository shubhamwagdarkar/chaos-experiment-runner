# chaos-experiment-runner

A lightweight chaos engineering experiment runner built in C# .NET 8. Define chaos experiments in YAML, inject faults into your system, and verify resilience with typed probes — all from a single CLI.

Inspired by the [Chaos Engineering Principles](https://principlesofchaos.org/) and the structure of [Chaos Toolkit](https://chaostoolkit.org/).

---

## What It Does

1. **Verifies steady state** (before) — confirms your system is healthy using HTTP, process, or command probes
2. **Injects faults** — HTTP latency, HTTP errors, process termination, or resource exhaustion
3. **Verifies steady state** (after) — checks whether your system recovered
4. **Rolls back** — removes all injected faults automatically
5. **Reports results** — pass/fail/abort with per-probe and per-action detail

---

## Architecture

```
ExperimentLoader (YAML)
       ↓
ChaosExperiment
       ↓
ExperimentRunner
  ├── SteadyStateVerifier (before)
  │     ├── HttpProbeRunner
  │     ├── ProcessProbeRunner
  │     └── CommandProbeRunner
  ├── FaultRunners (method)
  │     ├── HttpLatencyFaultRunner  → FaultRegistry
  │     ├── HttpErrorFaultRunner    → FaultRegistry
  │     ├── ProcessTerminateFaultRunner
  │     └── ResourceExhaustionFaultRunner
  ├── SteadyStateVerifier (after)
  └── Rollback
       ↓
ConsoleExperimentReporter
```

---

## Supported Probe Types

| Type | What It Checks |
|------|---------------|
| `Http` | HTTP endpoint status code, latency, optional body match |
| `Process` | Whether a named process is running or stopped |
| `Command` | Shell command exit code and optional stdout match |

## Supported Fault Types

| Type | What It Injects |
|------|----------------|
| `HttpLatency` | Registers a latency fault in the FaultRegistry (consumed by FaultAwareHttpClient) |
| `HttpError` | Registers an error fault (HTTP 5xx) in the FaultRegistry |
| `ProcessTerminate` | Kills all processes matching a name |
| `ResourceExhaustion` | Runs a background CPU or memory stress task |

---

## Usage

```bash
dotnet run --project src/ChaosExperimentRunner -- run experiments/sample-http-resilience.yaml

# Dry run: parse and display the experiment without injecting faults
dotnet run --project src/ChaosExperimentRunner -- run --dry-run experiments/sample-http-resilience.yaml
```

Exit code `0` = experiment passed. Exit code `1` = failed or aborted.

---

## Experiment YAML Format

```yaml
name: http-service-resilience
description: Verify the API handles upstream latency gracefully

steady-state:
  title: Service is healthy and responsive
  probes:
    - name: health-endpoint-responds
      type: Http
      config:
        url: http://localhost:8080/health
        method: GET
        timeout-seconds: 5
      tolerance:
        status-code: 200
        max-latency-ms: 500

    - name: worker-process-is-running
      type: Process
      config:
        process-name: myservice
      tolerance:
        running: true

    - name: db-check
      type: Command
      config:
        command: pg_isready
        arguments: -h localhost -p 5432
      tolerance:
        exit-code: 0

method:
  name: inject-upstream-latency
  actions:
    - name: add-3s-delay
      type: HttpLatency
      config:
        target-url: http://upstream:8081
        latency-ms: 3000
        duration-seconds: 30

    - name: return-503-errors
      type: HttpError
      config:
        target-url: http://payment-service:9000
        status-code: 503
        duration-seconds: 20

rollback:
  enabled: true
  timeout-seconds: 60
```

---

## Running Tests

```bash
dotnet test
```

89 tests — zero external dependencies required.

```
ModelTests             ×10
ExperimentLoaderTests  ×12
SteadyStateVerifier    ×11
ExperimentRunnerTests  ×15
FaultRunnerTests       ×18
ProbeRunnerTests       ×16
ExperimentReporter     ×7
```

---

## Project Structure

```
chaos-experiment-runner/
├── src/ChaosExperimentRunner/
│   ├── Models/          # Domain models: ChaosExperiment, probes, faults, results
│   ├── Core/            # Runner, verifier, loader interfaces and implementations
│   ├── Probes/          # HTTP, Process, Command probe runners
│   ├── Faults/          # HTTP latency/error, process termination, resource exhaustion
│   ├── Reporting/       # Console reporter
│   └── Extensions/      # DI registration
├── tests/ChaosExperimentRunner.Tests/
├── experiments/         # Sample YAML experiment definitions
├── PRD.md
├── ADR.md
├── USER_STORIES.md
└── USER_JOURNEYS.md
```

---

## What I Learned

Building this reinforced how important the **Steady State Hypothesis** is in chaos engineering — it's not enough to inject a fault and observe. You need a formal definition of "normal" both before and after. The YAML-driven design forced me to separate experiment *definition* from experiment *execution*, which made the runner significantly easier to test. Dependency injection + interfaces meant I could write 89 tests against real logic without any running services.

---

## Stack

- C# .NET 8
- YamlDotNet 15 — experiment definition parsing
- Microsoft.Extensions.DependencyInjection + Http + Logging
- xUnit + NSubstitute + FluentAssertions
