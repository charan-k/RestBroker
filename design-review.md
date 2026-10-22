# Design Review — EPMCDMETST-66948

## Review scope

Reviewed the revised requirements and architecture, plus available T1–T3 implementation evidence. This review assesses traceability, security, failure handling, maintainability, testability, and complexity. It does not authorize implementation or revise the approved requirements.

## Summary

The revision improves source and toolchain traceability: the UI source is pinned to `d36bd3f8647a091d406e53bad463c5e3e5d2ece1`, and the specified versions are JDK 26+, Maven 3.9.14, Node 24.14.1, and npm 11.11.0. The architecture also correctly avoids assuming that the upstream fixed-port Compose setup or interactive `run_locally.cmd` is safe for concurrent CI.

The architecture records approved D1–D5 policies covering per-run Compose isolation and lifecycle, CI trust and toolchain, public API mutation safeguards, and artifact/log redaction and retention. These decisions have not been implemented or runtime-verified. Exact action SHAs, workflow/environment configuration, base-image digest enforcement, probe-image provenance/network attachment, Docker/Compose support, diagnostic artifact destination/quantitative bounds, cancellation-safe post-step guarantees, and cleanup after abrupt runner loss remain **Not Found**.

T1–T3 provide API configuration and CRUD implementation evidence, but the UI project remains an empty scaffold. T2 and T3 tests use fake handlers only; they do not demonstrate a live API integration or private UI provisioning.

The D4 public API live-mutation policy is now approved and documented. It restricts mutations to serialized, reviewer-gated trusted/manual runs with same-run record ownership checks, no mutation retries, and finally cleanup. These safeguards have not been implemented or exercised against the live service; residual shared-service risks and runtime verification remain.

## Traceability and coverage

| Requirement area | Architecture / implementation evidence | Review |
|---|---|---|
| Pinned UI source and specified toolchain | Architecture records the source SHA and toolchain versions. Upstream README confirms the stated versions. | Addressed at the source/version identification level. Reproducible provisioning commands and runner compatibility remain **Not Found**. |
| Private, isolated UI environment for each run; no public UI mutation or fallback | Architecture specifies a unique per-run Compose project, runtime-only configuration replacing upstream fixed host mappings, frontend-only dynamic loopback publication, private backend services, and project-scoped cleanup (architecture.md, lines 31–41, 74–78, 107). | Design specified; implementation and runner verification remain pending. Runtime-only configuration must actually remove the upstream mappings and preserve per-run isolation. |
| Provisioning and readiness failure behavior; teardown outcome | Architecture specifies six backend Actuator endpoints plus frontend `/`, a 20-minute combined build/start/readiness bound, bounded sanitized run-scoped diagnostics, project-scoped teardown after success/failure/partial startup/cancellation, and teardown failure reporting (architecture.md, lines 36–41, 74–76, 112–113). | Policy specified; implementation and runner verification remain pending. Private probe image/network attachment, diagnostic artifact destination/quantitative bounds, and hard-runner-loss behavior are **Not Found**. |
| Toolchain, CI isolation, and trust boundaries | Architecture specifies secret-free PR validation, trusted/manual integration, least-privilege permissions, full-SHA action pins, `ubuntu-24.04`, exact version installation/verification, reviewer-gated environment secrets, and digest-pinned base images (architecture.md, lines 17–26, 88–92, 107–116). | Policy specified; implementation and runtime verification pending. Exact actions, environment/event configuration, digest-enforcement mechanism, runner support, and D4 credential/execution details remain **Not Found**. |
| Cancellation | Architecture requires run-scoped teardown attempts on cancellation through a cancellation-safe post-step and separately reports teardown failure; cleanup after abrupt runner loss remains **Not Found**. | Policy specified; post-step guarantee and hard-runner-loss behavior require runner verification. |
| Reports and artifact redaction (D5) | Approved requirements and architecture specify separate API/UI TRX reports, masked UI failure screenshots, and separately labeled sanitized provisioning-diagnostics artifacts only for UI environment failures; all uploaded artifacts are retained for 7 days. Playwright traces/videos are disabled. | Policy approved; artifact destination/quantitative bounds, implementation, and runtime verification are **Not Found**. Diagnostics that cannot be safely sanitized must not be published. |
| Public API live mutations (AC9, AC28–AC31) | Approved trusted/manual, reviewer-gated policy documented in requirements and architecture; unique run marker, same-run created ID only, marker checks, no arbitrary listing, no mutation retries, uncertain-create abort, and finally cleanup. | Policy selected; residual shared-service risks remain. Current tests are fake-handler only and do not demonstrate live behavior; runtime verification is **Not Found**. |

## Findings

### DR-01 — CI provisioning design selected; runtime isolation remains unverified

**Evidence:** Requirements AC12, AC15–17, and AC26 require a private per-run UI environment, isolation, and no public UI fallback. The architecture now specifies a unique Compose project per run, runtime-only configuration replacing fixed host mappings, frontend-only dynamic loopback publication, private backend services, and project-scoped cleanup (architecture.md, lines 31–41, 74–78, 107). Docker/Compose runner availability and runtime verification are **Not Found**.

**Impact:** The selected design avoids relying on fixed published ports and process-wide cleanup, but concurrent-run isolation is not yet demonstrated. An incomplete runtime configuration could leave upstream fixed mappings active or expose backend ports.

**Recommendation:** Require Compose 2.24.0 or later for `!override`, check the installed version before provisioning, and inspect the merged model programmatically. Fail unless it contains only the frontend's dynamically assigned loopback host mapping and no backend/H2 host mappings; do not log raw merged configuration. Also verify each run has a distinct Compose project/network and cleanup targets only that project. Preserve the no-public-UI-fallback invariant.

**Status:** D1 design decision recorded and approved. Runtime verification remains pending; I4 and dependent browser/CI implementation remain blocked by D2/D3 implementation and runner gaps and the relevant approval gates.

### DR-02 — Lifecycle policy specified; probe and runner guarantees remain unverified

**Evidence:** Requirements AC12, AC16, and AC17 require provisioning failure and teardown outcomes. Architecture specifies readiness on all six backend Actuator health endpoints plus frontend `/`, a 20-minute combined build/start/readiness bound, run-scoped diagnostics, teardown attempts after success, test failure, partial provisioning/readiness failure, and cancellation, plus differentiated teardown-failure reporting (architecture.md, lines 36–41, 74–76, 112–113). No runtime validation has been performed.

**Impact:** The policy provides explicit gates and failure outcomes, but the backend endpoints must be probed without publishing private backend ports. CI runner/plugin compatibility and cancellation-step guarantees could prevent the policy from being enforced; abrupt runner loss may still leave resources running.

**Recommendation:** Reuse the digest-pinned Node image already used by the UI source for the private-network readiness probe, using Node's built-in HTTP client and no additional probe image; verify the exact command and network attachment without exposing backend ports. Validate endpoint success semantics; enforce the total 20-minute bound; bound and redact diagnostics; verify teardown only targets the current project and preserves the primary failure. Treat an `always()` teardown step as best-effort: GitHub Actions may forcibly terminate canceled jobs, and neither cancellation cleanup nor cleanup after abrupt runner loss is guaranteed.

**Status:** D2 lifecycle and D5 artifact/redaction policies are recorded and approved. Runtime verification remains pending. The Node-image probe approach is selected, but its exact command/network attachment, diagnostic artifact destination/quantitative bounds, Compose runner support, cancellation outcome, and cleanup after abrupt runner loss are **Not Found**.

### DR-03 — CI trust policy specified; implementation details remain unverified

**Evidence:** Architecture specifies secret-free pull-request validation, trusted-branch/manual integration, workflow permissions defaulting to `{}` with checkout-only `contents: read`, full-SHA action pins, fixed `ubuntu-24.04`, exact toolchain installation/verification, reviewer-gated GitHub Environment secrets, and digest-pinned base images (architecture.md, lines 17–26, 88–92, 107–116). No workflow or runtime validation is present.

**Impact:** The selected trust model separates untrusted PR code from privileged credentials and limits permissions by default. However, exact events/branches/environment reviewers and final action pins are not configured. Floating Dockerfile base-image references, the probe's private-network attachment, and runner/Compose availability still need implementation safeguards; D4 live-mutation execution details and credentials are also unresolved.

**Recommendation:** Implement only the approved trust tiers; verify no PR-controlled code can access reviewer-gated secrets; reverify and pin every third-party action by full SHA; install and assert the exact toolchain on `ubuntu-24.04`; after checking out the pinned source, guard a CI-only rewrite of expected floating Dockerfile `FROM` references to verified image digests and fail if references/counts differ or any floating image remains. Verify the Maven archive checksum before extraction. Scope environment secrets only to approved trusted jobs. Do not enable live API mutation until the approved D4 controls and credential authorization are implemented and verified. Enforce and verify the approved D5 sanitization policy before publishing diagnostics.

**Status:** D3 CI trust/toolchain decisions and the separate D4 public API mutation policy are recorded and approved. Candidate action SHAs, Java/Node image digests, and Maven archive checksum are recorded in the architecture for revalidation; they are not yet authorized as final workflow pins. Workflow implementation and verification remain pending. Triggers/branch/environment/reviewer configuration, exact guarded rewrite implementation, probe network attachment, Docker/Compose runner support, and D4 credential authorization/execution details are **Not Found**.

### DR-04 — Approved live-mutation policy; shared-service risks and runtime evidence remain

**Evidence:** Requirements approve the D4 policy (requirements.md, lines 36, 55–60, 129–136); the separate trusted/manual lane and safeguards are documented in architecture.md, lines 16–27, 45–55, and 79. Existing API tests use fake handlers and do not demonstrate live behavior (RestfulBookerBookingApiClientTests.cs, lines 22, 58). No live API calls were made.

**Approved policy:** Live mutations are restricted to a dedicated trusted/manual lane after reviewer-gated Environment approval, with at most one active live-mutation run. Use synthetic, non-sensitive data and a unique high-entropy run marker. Only a confirmed successful create in the same run may supply an ID for later operations. Verify that record's marker before every update or delete, including cleanup; never list or select arbitrary IDs. Do not retry POST/PUT/PATCH/DELETE. An uncertain create aborts without retrying or guessing an ID. Attempt cleanup in `finally`; cleanup failure fails an otherwise-passing job, and otherwise is reported separately while preserving the primary failure.

**Impact:** The safeguards reduce the risk of modifying unrelated records but do not eliminate public-service instability or interference. They cannot guarantee cleanup after an ambiguous create or abrupt runner loss. Fake-handler tests establish client behavior against the harness only; they do not prove live service availability, mutation safety, or enforcement of this policy.

**Required safeguards / mitigation:** Keep live public API mutation excluded and fail closed before any request. Do not claim live mutation safety based on fake-handler results. I7 should run the deterministic API fake-backed suite only and must not attach credentials or enable a live mutation lane.

**Final scope decision:** The project will not perform live public API booking mutations. This is a deliberate, permanent scope boundary, not a pending prerequisite. Keep the existing fail-closed explicit fixture and fake-backed API client coverage; do not provision live mutation credentials, add an incident-channel integration, or enable live POST/PUT/PATCH/DELETE tests in I7. The previously approved credentials, serialization, marker, and orphan-reporting policies remain documented as the safeguards that would have applied had live mutations been in scope; they do not authorize live execution.

**Impact / disposition:** The absent incident-channel integration and authorized live-mutation credentials are accepted consequences of this scope decision, not blockers. No live requests have been made or are planned. Continue API coverage with deterministic fake-handler tests only.

**Status:** I5 is complete as an intentional exclusion of live public API mutations. Its fixture remains fail-closed; no live credential or recovery-channel setup is required. D4's safeguards apply only to already authorized synthetic/fake test behavior; there is no live mutation lane to implement in I7.

### DR-05 — Current test evidence does not verify end-to-end acceptance

**Evidence:** T2 has 27 fake-handler tests and T3 has 12 fake-handler tests; both suites reportedly passed. The UI project is an empty scaffold. No live API test or private UI provisioning implementation is present in the supplied evidence.

**Impact:** The reported test results support behavior against fakes only. They do not verify real API compatibility, UI startup/readiness, per-run isolation, browser behavior, or teardown.

**Recommendation:** Retain the fast fake-handler tests. Once the approved private UI lifecycle exists, add automated browser tests for the UI acceptance criteria and focused lifecycle tests for provisioning, readiness failure, cancellation, and teardown failure. Treat live public API verification as a separately identified test category with explicit safeguards.

**Status:** Coverage gap; do not describe fake-handler results as integration or end-to-end verification.

## Strengths

- The revision pins the UI source to a specific SHA and records the required toolchain versions.
- The architecture records a per-run Compose project design with dynamic loopback frontend publication, private backend services, and project-scoped cleanup rather than treating upstream fixed-port mappings or the process-wide local launcher as CI-ready.
- The architecture defines a two-tier workflow trust model, minimal permissions, full-SHA action pinning, an exact toolchain on a fixed runner, reviewer-gated environment secrets, and mandatory base-image digest pinning.
- The approved D4 policy restricts live mutations to a reviewer-gated trusted/manual lane and specifies same-run ownership checks, no mutation retries, abort-on-uncertain-create, and cleanup failure behavior.
- The API implementation has fake-handler tests, providing a basis for deterministic request and response testing.

## Complexity and maintainability

Per-run provisioning and cleanup add orchestration complexity, but that complexity is inherent in the requirement for private isolated UI instances. Prefer the smallest supported non-interactive lifecycle that can guarantee isolation, readiness, and cleanup. Avoid reusing the upstream interactive launcher or adding independent cleanup paths that can terminate unrelated processes. Keep API fake tests, external API checks, and private-UI browser tests distinguishable in reports.

## Unresolved facts

The following remain **Not Found** in the reviewed architecture evidence:

- Docker Engine/Compose runner version, permissions, and compatibility.
- Exact runtime-only Compose configuration/commands and verification that all upstream fixed host mappings are replaced.
- Runtime evidence that overlapping runs use isolated projects/networks and project-scoped cleanup.
- Exact command and private-network attachment for the selected digest-pinned Node-image readiness probe, without publishing backend ports.
- Diagnostic artifact destination, quantitative bounds, sanitization implementation, and runtime verification.
- Revalidation of candidate third-party action SHAs and implementation of toolchain setup/checksum verification.
- Exact workflow event/branch names, GitHub Environment name and required reviewers, and runtime secret names/scope configuration.
- Guarded CI-only mechanism to rewrite expected floating Dockerfile base references to image digests and verify the resulting build inputs.
- Outcome of cancellation-triggered `always()` cleanup and cleanup behavior after abrupt runner loss or hard termination; GitHub Actions cancellation cleanup is best-effort, not guaranteed.
- D4 credential authorization, concurrency mechanism, marker handling, orphan recovery, and live runtime verification are not pursued because live public API mutation is an intentional permanent project exclusion. The existing fail-closed guard must remain in place.

## Review disposition

The source pin, toolchain versions, and approved D1–D5 provisioning, lifecycle, CI trust, API safety, and artifact/redaction policies improve traceability. I5 is closed as a deliberate exclusion of live API mutations; the fail-closed fixture is retained and no live credentials or recovery integration are required. I7 has resolved the previously pending D2/D3 runtime and D5 artifact implementation details: the workflow is implemented with pull-request and manual triggers, parallel fake-backed API and private UI jobs, pinned actions, a Docker Compose version check, scoped provisioning and cleanup, and test/artifact steps. Local evidence includes passing actionlint, 63 API tests, and 23 focused UI tests, with no secret references found. These results do not establish successful CI runtime behavior. I4 and I6 remain incomplete pending CI verification of Compose, build, readiness, teardown, and I6 browser execution. Do not describe the permanent I5 scope boundary as a blocker.
