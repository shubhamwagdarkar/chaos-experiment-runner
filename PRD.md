# Product Requirements Document: chaos-experiment-runner

**Author:** Shubham Wagdarkar
**Status:** Shipped

---

## Problem Statement

Distributed systems fail in unpredictable ways — upstream timeouts, process crashes, resource exhaustion. Teams discover these failure modes only after an outage, not before. Existing chaos engineering tools (Chaos Toolkit, Gremlin) are either Python-only, require cloud agents, or are too heavyweight for enterprise .NET environments. Teams need a lightweight, dependency-free runner they can embed in their CI/CD pipelines or run locally before each deployment.

---

## Target Users

| User | Context |
|------|---------|
| Platform Engineer | Needs to validate resilience of microservices before promoting to production |
| SRE / On-call Engineer | Needs to verify recovery behavior after an incident is closed |
| Developer | Needs to confirm their service handles upstream failures gracefully in a local environment |

---

## Goals & Success Metrics

| Goal | Metric | Target |
|------|--------|--------|
| Validate resilience before incidents | % of teams with chaos experiments in CI | > 50% within 6 months |
| Reduce MTTR for known failure modes | Time to detect steady-state recovery | < 5 minutes per experiment |
| Reduce experiment definition effort | Time to write first experiment | < 30 minutes |
| Zero false positives from flaky probes | Rate of aborted experiments due to pre-existing issues | < 5% |

---

## Feature Requirements

### F1 — Steady State Hypothesis
- Define system health using typed probes (HTTP, Process, Command)
- Each probe has configurable tolerance (status code, latency threshold, exit code, output match)
- Experiment aborts if steady state is not met before fault injection
- System failure (after state not restored) is reported as a resilience gap

### F2 — Fault Injection
- Four fault types: `HttpLatency`, `HttpError`, `ProcessTerminate`, `ResourceExhaustion`
- Faults defined in experiment YAML alongside probes
- Multiple faults executed sequentially per experiment

### F3 — Automatic Rollback
- All injected faults rolled back after experiment completes (pass or fail)
- Configurable rollback timeout to prevent indefinite blocking
- Rollback only applies to faults that were successfully executed

### F4 — YAML Experiment Definitions
- Experiments defined in human-readable YAML with kebab-case keys
- HyphenatedNamingConvention maps YAML to C# models automatically
- Loader validates required fields and enum values with clear error messages

### F5 — CLI Runner
- `run <experiment.yaml>` — execute an experiment
- `run --dry-run <experiment.yaml>` — parse and display experiment without injecting faults
- Exit code: `0` = passed, `1` = failed or aborted

### F6 — Dependency Injection / Extensibility
- All components registered via `IServiceCollection` extension
- Custom probe runners and fault runners pluggable via `IProbeRunner` / `IFaultRunner`

---

## Non-Functional Requirements

| NFR | Requirement |
|-----|-------------|
| Test coverage | > 85% line coverage; all core paths unit tested |
| Dependencies | No external services required to run tests |
| Build | `dotnet build` and `dotnet test` must pass on a clean machine with .NET 8 SDK |
| Startup time | CLI ready in < 1s |
| Rollback safety | Rollback timeout prevents indefinite blocking regardless of experiment outcome |

---

## Out of Scope

- Distributed fault injection across multiple machines (requires agent)
- Real HTTP proxy-based fault injection (requires Toxiproxy or tc)
- Parallel fault injection
- Web dashboard or API
- Kubernetes-native chaos (out of scope for v1; see Chaos Mesh)

---

## Open Questions

1. Should the FaultRegistry expose an HTTP endpoint so external services can check active faults? (Would enable real proxy-based injection.)
2. Should experiments support `parallel` action blocks in addition to sequential?
3. Should failed probes block remaining probes, or should all probes always run for full visibility?
