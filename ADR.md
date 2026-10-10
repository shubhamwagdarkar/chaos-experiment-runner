# Architecture Decision Records: chaos-experiment-runner

---

## ADR-001: YAML as experiment definition format

**Decision:** Use YAML with YamlDotNet and HyphenatedNamingConvention.

**Options considered:**
- JSON — more common in .NET, but verbose and unfriendly for multi-line strings
- YAML — human-readable, widely used in DevOps tooling (Kubernetes, Ansible, GitHub Actions), familiar to the target audience
- C# fluent API — type-safe but requires recompile for every experiment change

**Rationale:** Experiments need to be readable and editable by SREs who may not be .NET developers. YAML is the lingua franca of infrastructure-as-code. The HyphenatedNamingConvention (`timeout-seconds` → `TimeoutSeconds`) matches the conventions used in Kubernetes and Helm charts, reducing the learning curve.

---

## ADR-002: Strongly typed models over Dictionary<string, object>

**Decision:** Use abstract record hierarchies (`ProbeConfig`, `FaultConfig`, `ProbeTolerance`) instead of `Dictionary<string, object>` for probe and fault configuration.

**Options considered:**
- `Dictionary<string, object>` — flexible but stringly typed, runtime errors, poor IDE support
- Strongly typed abstract records — type-safe, pattern-matchable, compile-time correctness

**Rationale:** The number of probe/fault types is bounded and known. Type safety enables exhaustive `switch` expressions in the loader and makes test assertions clear. The intermediate DTO layer absorbs the YAML→domain type mapping without polluting core models.

**Trade-off:** Adding a new fault type requires a new record class and a new switch arm in the loader — a deliberate constraint that prevents undefined/misconfigured faults from silently passing through.

---

## ADR-003: FaultRegistry pattern for HTTP fault state

**Decision:** HTTP fault runners register active faults in a shared `FaultRegistry` (in-memory `ConcurrentDictionary`) rather than directly intercepting HTTP at the OS level.

**Options considered:**
- OS-level network fault injection (tc, iptables) — accurate but requires root access and Linux
- Toxiproxy subprocess — accurate but requires a running sidecar and network reconfiguration
- FaultRegistry in-memory — no external dependencies, fully testable, represents fault state for consumption by application-side `FaultAwareHttpClientHandler`

**Rationale:** This project targets CI/CD pipelines and local dev environments where root access and sidecars are not available. The FaultRegistry pattern is the foundation for a real implementation: in production, a `FaultAwareHttpClientHandler` registered in the application would query the FaultRegistry (or a shared service) to apply latency/errors. The architecture is correct even if the actual interception is deferred.

---

## ADR-004: Constructor injection for all dependencies

**Decision:** All runners receive dependencies via constructor injection; no static state or service locator.

**Rationale:** Constructor injection enables NSubstitute mocks in tests without any test-specific constructor overloads or `InternalsVisibleTo` attributes. The entire test suite runs with zero real processes, zero real HTTP calls, and zero file system access (except `ExperimentLoader` tests which use inline YAML strings).

---

## ADR-005: Sequential fault execution with fail-fast

**Decision:** Fault actions execute sequentially; the first failed action stops execution and triggers rollback of successfully executed actions only.

**Options considered:**
- Parallel execution — faster but harder to reason about fault interactions
- Sequential with continue-on-failure — all faults injected even if one fails
- Sequential with fail-fast (chosen)

**Rationale:** Chaos experiments are designed to test one hypothesis at a time. If a fault injection itself fails, subsequent faults may produce meaningless results. Fail-fast prevents misleading after-state readings. Rolling back only successfully executed actions is consistent with the principle of minimum blast radius.

---

## ADR-006: Steady state runs all probes regardless of individual failures

**Decision:** `SteadyStateVerifier` runs all probes even after a probe fails.

**Rationale:** Full visibility into which probes pass and which fail is more valuable than stopping early. An operator looking at an experiment report needs to know whether one probe failed or three did. The `SteadyStateResult.Passed` flag is computed from all results after all probes run.
