# User Journeys: chaos-experiment-runner

---

## Journey 1 — First Resilience Experiment (New User)

**Persona:** Alex, Platform Engineer
**Goal:** Verify that the order service handles upstream payment gateway latency without returning errors to end users

---

**Steps:**

1. Alex clones the repo and runs `dotnet build` — clean build, zero external dependencies
2. Alex copies `experiments/sample-http-resilience.yaml` to `experiments/order-service-resilience.yaml`
3. Alex edits the probe URLs to point to the order service (`localhost:8080/health`) and payment gateway (`localhost:9000/ping`)
4. Alex adds a `HttpLatency` action targeting `http://localhost:9000` with `latency-ms: 5000`
5. Alex runs `dotnet run -- run --dry-run experiments/order-service-resilience.yaml`
   - **Decision point:** YAML valid → proceed; YAML error → fix and retry
6. Alex starts the order service locally and runs `dotnet run -- run experiments/order-service-resilience.yaml`
7. Pre-experiment probes pass — steady state confirmed
8. Fault runner registers a 5s latency fault on the payment gateway URL
9. Post-experiment probes run — **decision point:**
   - Order service returns 200 within 500ms → experiment **PASSES** (timeout + circuit breaker working)
   - Order service returns 504 → experiment **FAILS** (resilience gap detected)
10. Fault is rolled back automatically

**Happy path outcome:** Alex sees `STATUS: PASSED` and has evidence that the circuit breaker triggers correctly under payment gateway latency.

**Error path outcome:** Alex sees `STATUS: FAILED — System did not recover to steady state` and opens a ticket to add a timeout + fallback to the payment gateway client.

---

## Journey 2 — CI/CD Integration (Existing User)

**Persona:** Jordan, SRE
**Goal:** Run chaos experiments automatically as a gate before every production deployment

---

**Steps:**

1. Jordan adds a `chaos-test` job to the GitHub Actions pipeline after the `integration-test` job
2. The job starts the staging environment using Docker Compose
3. Jordan adds a step: `dotnet run --project src/ChaosExperimentRunner -- run experiments/http-resilience.yaml`
4. GitHub Actions captures the exit code — `0` = proceed to deploy, `1` = block deployment
5. **Decision point:** On failure, the deployment is blocked and Jordan receives a Slack alert with the experiment report
6. Jordan reviews the console output (captured as a CI artifact) to understand which probe failed

**Happy path outcome:** All experiments pass on every deployment. Resilience regressions are caught before production.

**Error path outcome:** A new code change removes a timeout from the HTTP client. The `max-latency-ms: 500` probe fails. Deployment is blocked. Developer fixes the timeout before merge.

---

## Journey 3 — Post-Incident Resilience Validation (On-Call Engineer)

**Persona:** Sam, On-Call Engineer
**Goal:** After resolving an outage caused by a database connection pool exhaustion, verify the system now recovers gracefully under the same conditions

---

**Steps:**

1. Sam creates a new experiment file: `experiments/db-pool-exhaustion-recovery.yaml`
2. Sam adds a `ResourceExhaustion` action targeting memory at 80% intensity for 60 seconds
3. Sam adds `Process` probes to verify the app process stays running throughout
4. Sam adds `Http` probes to verify the health endpoint continues to respond
5. Sam runs the experiment against the updated system
6. **Decision point:** During fault phase — if process probe detects process restart, experiment fails immediately
7. After rollback, steady state probes confirm full recovery
8. Sam documents the experiment file in the incident postmortem as "resilience regression test"

**Happy path outcome:** System survives memory pressure and recovers within the probe timeout — incident fix confirmed.

**Error path outcome:** Process crashes under pressure — experiment aborts after-state, Sam identifies a missing OOM guard and adds it.

---

## Journey 4 — Extending with a Custom Probe Type (Advanced User)

**Persona:** Morgan, Staff Engineer
**Goal:** Add a custom `Kafka` probe type to verify consumer lag stays below 1000 messages during fault injection

---

**Steps:**

1. Morgan creates `KafkaProbeRunner : IProbeRunner` that queries the Kafka Admin API for consumer group lag
2. `CanHandle(ProbeType.Kafka)` — Morgan adds `Kafka` to the `ProbeType` enum
3. Morgan registers the runner: `services.AddSingleton<IProbeRunner, KafkaProbeRunner>()` before `AddChaosRunner()`
4. Morgan adds a Kafka probe to an experiment YAML:
   ```yaml
   - name: consumer-lag-acceptable
     type: Kafka
     config:
       bootstrap-servers: localhost:9092
       consumer-group: order-processor
     tolerance:
       max-lag-messages: 1000
   ```
5. `SteadyStateVerifier` routes the Kafka probe to `KafkaProbeRunner.CanHandle()` → dispatches correctly
6. Morgan runs the experiment — the custom probe evaluates Kafka lag as part of steady state

**Outcome:** The extensibility contract works without modifying the core runner. Morgan's custom probe is usable by the whole team.
