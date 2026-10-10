# User Stories: chaos-experiment-runner

---

## US-001 — Define an experiment in YAML

**As a** platform engineer,
**I want to** define a chaos experiment in a YAML file with probes, fault actions, and rollback config,
**so that** I can version-control my resilience tests alongside my infrastructure code.

**Acceptance Criteria:**
- Experiment file includes `name`, `description`, `steady-state`, `method`, and `rollback` sections
- Supported probe types: `Http`, `Process`, `Command`
- Supported fault types: `HttpLatency`, `HttpError`, `ProcessTerminate`, `ResourceExhaustion`
- Invalid probe/fault types produce a clear error message with valid options listed
- Missing required fields (e.g., `url` for Http probe) produce a clear error message

---

## US-002 — Run an experiment from the CLI

**As a** developer,
**I want to** run an experiment by pointing the CLI at a YAML file,
**so that** I can validate resilience without writing any code.

**Acceptance Criteria:**
- `run <file>` parses, executes, and reports the experiment
- Exit code `0` on pass, `1` on fail or abort
- Output includes steady-state results (before and after), fault actions, and failure reason if applicable

---

## US-003 — Dry-run an experiment before executing

**As a** developer,
**I want to** dry-run an experiment to verify it parses correctly,
**so that** I can catch YAML errors before scheduling it in CI.

**Acceptance Criteria:**
- `run --dry-run <file>` prints experiment details without injecting any faults
- Exits with code `0` if the YAML is valid
- Reports probe count, fault action count, and rollback settings

---

## US-004 — Abort automatically when steady state is not met pre-experiment

**As an** SRE,
**I want** the runner to abort if my system is already unhealthy before fault injection,
**so that** I don't get misleading results from an experiment that ran against a broken baseline.

**Acceptance Criteria:**
- Pre-experiment steady-state check runs before any fault injection
- If any probe fails, experiment status is `Aborted` with a clear reason
- No faults are injected when the experiment is aborted
- Aborted experiments exit with code `1`

---

## US-005 — Detect resilience gaps automatically

**As a** platform engineer,
**I want** the runner to flag when my system does not recover after fault removal,
**so that** I know whether my service has a timeout or retry gap.

**Acceptance Criteria:**
- Post-experiment steady-state check runs after all faults are rolled back
- If after-state probes fail, experiment status is `Failed` with reason "System did not recover to steady state"
- Failure reason distinguishes between fault injection failure and recovery failure

---

## US-006 — Roll back faults automatically after every experiment

**As a** developer,
**I want** injected faults to be removed automatically when the experiment completes,
**so that** a failing experiment doesn't leave my local environment in a broken state.

**Acceptance Criteria:**
- All successfully executed fault actions are rolled back after experiment completion (pass, fail, or abort)
- Rollback uses a configurable timeout to prevent indefinite blocking
- Only actions that were successfully executed are rolled back

---

## US-007 — Add custom probe or fault runners via DI

**As a** platform engineer,
**I want to** register custom probe or fault runner implementations,
**so that** I can extend the tool to support organization-specific health checks or fault types.

**Acceptance Criteria:**
- `IProbeRunner` and `IFaultRunner` interfaces are public
- Custom implementations can be registered via `IServiceCollection` before calling `AddChaosRunner()`
- `CanHandle()` routing dispatches to the first registered runner that handles the given type
