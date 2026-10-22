# Requirements — EPMCDMETST-66948 (Revised)

## Story

**As a** QA/test automation engineer,
**I want** an automated test suite combining API tests in C# using RestSharp and NUnit with browser-based UI tests using Playwright,
**so that** we have a repeatable, CI-runnable regression suite covering authentication, CRUD operations, API error handling, and core booking-flow validation through the browser UI.

## Context and scope

- **Scope Revision (Design Review DR-01/DR-02):** The Restful Booker API (`https://restful-booker.herokuapp.com`) and the UI are separate applications with separate data stores. API-created bookings are not expected to appear in the UI.
  - API tests create, read, update, patch, and delete bookings through the public REST API only.
  - CI must provision a private, ephemeral UI environment for each run from the official Restful Booker Platform source. Browser UI tests use only that run's environment.
  - UI tests must not submit bookings to the shared public UI at `https://automationintesting.online`.
  - UI booking creation and confirmation are verified within the same run's private UI environment.
- API target: Restful Booker API at `https://restful-booker.herokuapp.com`.
- UI target: private, per-run environment provisioned from the official Restful Booker Platform source; the shared public UI is not a mutating test target.
- **Verified upstream source and toolchain:** Use the Restful Booker Platform repository at `https://github.com/mwinteringham/restful-booker-platform`, pinned to immutable commit `d36bd3f8647a091d406e53bad463c5e3e5d2ece1` (GitHub's latest commit listing is dated 2026-07-01). Do not resolve a moving branch, tag, or "latest" revision. The root README for this revision specifies JDK 26 or higher (tested with JDK 26), Maven 3.9.14, Node 24.14.1, and npm 11.11.0; CI must use these versions for the UI source build and provisioning.
- **Approved per-run UI isolation:** Each CI run uses a unique Docker Compose project. A runtime-only Compose override replaces the upstream fixed host mappings and publishes only the frontend container port 80 on an OS-assigned host port bound to `127.0.0.1`. Backend services and their embedded H2 databases remain private; do not enable `dbServer` or publish H2 ports. Derive the UI base URL from the published frontend port. Clean up only that run's Compose project. Do not use the upstream interactive `run_locally.cmd` launcher or process-wide cleanup.
- **Approved readiness and lifecycle policy:** Before UI tests, readiness requires all six backend Actuator endpoints (`/booking/actuator/health`, `/room/actuator/health`, `/branding/actuator/health`, `/auth/actuator/health`, `/report/actuator/health`, and `/message/actuator/health`) and frontend `/`. A short-lived, digest-pinned probe container checks backend endpoints on the private Compose network without publishing backend ports. The combined image-build, environment-start, and readiness phase must complete within 20 minutes; otherwise it fails and enters diagnostics and teardown. On provisioning, readiness, or test failure, collect bounded, run-scoped diagnostics under the D5 artifact and log policy, including sanitized Compose status/log summaries and each readiness endpoint's last observed response summary. Upload the separately labeled provisioning-diagnostics artifact only for private UI environment provisioning/readiness failures. Attempt project-scoped teardown after success, test failure, partial provisioning/readiness failure, and cancellation using a cancellation-safe post-step. If tests passed but teardown fails, fail the job; if another failure is already primary, preserve it and report teardown failure separately. Cleanup after abrupt runner loss or hard termination is **Not Found**.
- **D1–D3 implementation details still unresolved:** Exact runtime-only Compose syntax, private probe image provenance/digest and network-attachment mechanics, Docker/Compose runner version/permissions, diagnostic destination/quantitative bounds/detailed response redaction, cancellation-safe post-step guarantee, and enforcement of base-image digests against the pinned source's floating Dockerfile references are **Not Found**.
- API automation uses the approved C#/.NET 8, RestSharp, and NUnit project context.
- Browser automation uses Playwright.
- CI platform: GitHub Actions.
- Both test types must be runnable from the CLI using `dotnet test`.
- API and UI suites must run independently and in parallel in CI, with isolated test data and independent assertions.
- UI scope is limited to public/no-admin pages in the private environment:
  - Verify room availability and that the room list renders with relevant fields.
  - Verify the booking form and submission workflow.
  - Verify the booking confirmation after form submission.
- Admin-only pages or workflows are out of scope.
- API base URL, UI base URL, credentials, booking test data, browser, and viewport must be configurable rather than hardcoded. The UI base URL is supplied by the per-run environment and must not default to the shared public UI.
- Credentials must be provided through environment variables and/or CI secrets, and must never be committed.
- **Approved CI trust model:** Pull-request validation runs without privileged credentials. Credentialed integration runs are limited to trusted-branch events or manual runs; PR-controlled code must not execute in a job with privileged secrets. Workflow-level permissions default to `{}`; only checkout jobs receive `contents: read`. Every third-party action is reviewed and pinned to a full commit SHA. Use a fixed `ubuntu-24.04` runner and reviewed, full-SHA-pinned setup actions to install and verify JDK 26, Maven 3.9.14, Node 24.14.1, and npm 11.11.0. Reviewer-gated GitHub Environment secrets are exposed only to approved trusted integration jobs. All container base images must be digest-pinned before CI use.
- **D3 details still unresolved:** Exact workflow event/branch names, GitHub Environment name/reviewer configuration, action/setup SHAs, exact permission exceptions, secret/configuration names and scope, digest-enforcement mechanism, and runner configuration are **Not Found**.
- **Approved public API mutation policy (D4):** Live mutations run only in a dedicated trusted/manual lane after reviewer-gated GitHub Environment approval, separately from secret-free pull-request validation, with no more than one live-mutation run active at a time. Use synthetic, non-sensitive booking data and a unique high-entropy run marker. Operate only on the booking ID returned by that same run's confirmed successful create; verify the marker before every update or delete, including cleanup. Never list or select arbitrary IDs. If create outcome is uncertain, abort without retrying or guessing an ID. Disable automatic retries for POST, PUT, PATCH, and DELETE. Attempt cleanup in a finally/teardown path after confirmed successful creation. Cleanup failure fails an otherwise-passing job; if another failure is already primary, preserve it and report cleanup failure separately.
- **Approved I5 mutation safeguards:** Obtain `RESTBOOKER_API_USERNAME` and `RESTBOOKER_API_PASSWORD` only from reviewer-gated GitHub Environment secrets in `restbroker-live-api-mutations`; the service owner must provision authorized values. Never use defaults or commit credentials. Serialize the dedicated manual lane with repository-wide concurrency group `restbroker-live-api-mutations` and `cancel-in-progress: false`. Generate a fresh 128-bit cryptographically random marker for each test run and store it in the existing `additionalneeds` field. Mask the marker and any known booking ID. Never retry mutations, enumerate bookings, or act on an ID other than the confirmed same-run create response.
- **Approved orphan policy:** If a create outcome is uncertain or cleanup fails, ordinary logs, reports, and artifacts may contain only a sanitized run URL/ID, attempt number, time window, and failure category. The exact marker and known booking ID may be sent only through a restricted incident channel approved and provided by the service owner; they must never appear in ordinary logs or CI artifacts. The incident-channel platform/integration and owner-provided credentials are **Not Found**. Therefore, live mutations remain disabled until the recovery integration is available and verified, authorized environment credentials are provisioned, and the serialized reviewer-gated workflow is implemented. I5 scaffolding must fail closed without these prerequisites.
- **D4/I5 details still unresolved:** Service-owner credential authorization/provisioning, incident-channel platform/integration, external service availability/rate/reset/persistence behavior, recovery after abrupt runner loss, whether read-only public API checks run on pull requests, and live runtime verification are **Not Found**.
- CI defaults to headless Chromium at a 1280 × 720 viewport. Local headed execution must be available for debugging.
- There is no fixed full-suite duration limit. Tests should remain focused and bounded.

## Functional requirements

### API authentication and booking operations

- The suite must authenticate with valid credentials and obtain an auth token.
- The token must be reused across dependent tests within the same test run, rather than fetched separately for every dependent test.
- With valid booking data, creating a booking must return HTTP 200 and a valid booking ID in the response body.
- Retrieving the created booking by ID must return details that match the booking data created by the test.
- Retrieving a non-existent booking ID must return HTTP 404; the test must explicitly assert this expected response and must not treat it as an unexpected test failure.
- With a valid token and valid updated data:
  - PUT must fully update the booking.
  - PATCH must update only the specified fields.
- With a valid token, DELETE must remove the booking; a subsequent GET for that booking ID must return HTTP 404.
- PUT, PATCH, and DELETE with an invalid or missing token must return HTTP 401. Each expected status must be explicitly asserted.
- In live public API mutation tests, only the booking ID returned by that same run's confirmed successful create may be read or mutated. Before every update or delete, including cleanup, retrieve that ID and verify the unique run marker; if the ID is unavailable, the booking is missing, or the marker is absent/mismatched, abort without performing the mutation.
- Live mutation tests use synthetic, non-sensitive data with a unique high-entropy run marker and never list or select arbitrary booking IDs.
- Live mutation tests run only in the dedicated trusted/manual lane after reviewer-gated GitHub Environment approval, not in secret-free pull-request validation; no more than one live-mutation run may be active at a time.
- Live mutation credentials come only from `RESTBOOKER_API_USERNAME` and `RESTBOOKER_API_PASSWORD` secrets in the reviewer-gated `restbroker-live-api-mutations` GitHub Environment. Serialize the manual lane with repository-wide concurrency group `restbroker-live-api-mutations`, with cancellation disabled for in-progress runs.
- Use a 128-bit cryptographically random run marker stored in the existing `additionalneeds` field. Mask the marker and known booking ID. On uncertain create or failed cleanup, ordinary logs and artifacts contain only a sanitized run URL/ID, attempt, time window, and failure category; exact marker/ID details are limited to a service-owner-approved restricted incident channel.
- Keep all live mutations disabled until authorized secrets and the owner-provided restricted incident-channel integration are available and verified and the serialized reviewer-gated workflow is implemented. Missing prerequisites must fail closed before any request.
- Disable automatic retries for live POST, PUT, PATCH, and DELETE operations. An uncertain create outcome aborts without retrying, guessing an ID, or attempting subsequent mutations.
- Teardown must delete a test booking when it exists.
- After a confirmed successful live create, attempt marker-verified cleanup in a finally/teardown path. Cleanup failure fails an otherwise-passing job; if another failure is already primary, preserve it and report cleanup failure separately.
- Setup or cleanup failures must fail the affected tests. Tests dependent on failed setup must be skipped. There must be no silent retry or success-shaped fallback.
- When the target API is unavailable or intermittent, tests must fail clearly and provide diagnostics; they must not report success through fallback behavior.

### Browser UI validation

- CI must provision a private UI environment for each run from the verified repository and immutable commit specified above, using the stated toolchain and approved per-run Compose isolation. All container base images must be digest-pinned before CI use. Exact implementation details, runtime credentials, private probe container provenance/network attachment, and runtime verification are **Not Found**.
- Playwright UI tests must target only that run's private environment:
  - Browse available rooms and verify the room list renders with relevant fields (room name, description, price, facilities, etc.).
  - Submit a booking form with valid guest and booking data to the private environment.
  - Verify the booking confirmation in that same environment.
- UI tests must not create or mutate bookings on `https://automationintesting.online`, use its data, or fall back to it if provisioning or readiness fails.
- Separate runs must not share UI instances or booking data, and external users must not share or mutate a run's UI test state.
- Locators must be resilient and not rely on styling-specific brittle CSS or XPath selectors.
- UI tests must run headless by default in CI and support headed local debugging.
- UI test failures must automatically capture a screenshot with sensitive fields masked for diagnosis. Playwright traces and videos must be disabled.
- Browser dependencies must be installed in the CI pipeline; CI must not assume they are already installed on the runner.
- UI test failures must not be reported as API test failures, and API test failures must not be reported as UI test failures.

### Reports and CI

#### D5 artifact and log policy

- Use only synthetic test values. Never log credentials, authentication tokens, or raw API response bodies.
- Redact configured credentials, authentication tokens and cookies, booking values, and run markers from reports, diagnostics, and captured screenshots.
- Exclude raw network request/response bodies and headers, environment dumps, Docker inspection/configuration output, and unredacted logs. Playwright traces and videos must be disabled.
- If diagnostics cannot be safely sanitized, do not publish them.
- Retain uploaded test reports, failure screenshots, and provisioning-diagnostics artifacts for 7 days.

- Both suites must be runnable using `dotnet test`.
- CI must publish separate, clearly labeled API and UI TRX results, retaining each result for 7 days.
- Reports and debugging artifacts must follow the approved D5 artifact and log policy below.
- API and UI tests must run in parallel in CI using isolated test data.
- CI must provision the per-run private UI environment before UI tests and pass its discovered frontend URL to the suite. Readiness requires all six backend Actuator health endpoints and frontend `/`; backend ports must remain unpublished and backend probes use a short-lived digest-pinned container on the private Compose network. The combined image-build/start/readiness phase is limited to 20 minutes. Provisioning/readiness failure fails the UI suite with a separately labeled, bounded, run-scoped provisioning-diagnostics artifact only for these UI environment failures, and no shared-public-UI fallback. The diagnostic artifact destination, quantitative bounds, implementation, and runtime verification are **Not Found**.
- CI attempts project-scoped teardown after success, test failure, partial provisioning/readiness failure, and cancellation through a cancellation-safe post-step. If tests passed but teardown fails, the job fails; if another failure is primary, preserve it and report teardown failure separately. Cleanup after abrupt runner loss or hard termination is **Not Found**.
- The API suite continues to target `https://restful-booker.herokuapp.com` independently and must not rely on bookings created by the UI suite.

## Non-functional requirements

- **Security and privacy:** Do not hardcode or commit credentials. Supply credentials through environment variables and/or CI secrets.
- **Security and privacy:** Use only synthetic test values; never log credentials, authentication tokens, or raw API response bodies. Redact configured credentials, authentication tokens/cookies, booking values, and run markers from reports, diagnostics, and captured screenshots. Exclude raw network request/response bodies and headers, environment dumps, Docker inspection/configuration output, and unredacted logs. Do not publish diagnostics that cannot be safely sanitized.
- **Reliability:** Do not silently retry failed setup or cleanup operations. Report target unavailability or intermittent failures clearly with diagnostics. Live public API mutation requests (POST, PUT, PATCH, DELETE) are not automatically retried.
- **Isolation:** Every CI run must use a private UI environment and booking state separate from other runs and external users. No UI booking mutation may be sent to the shared public UI. API and UI execution must use isolated test data; failures in one suite must remain distinct from the other suite.
- **Public API mutation:** Follow the approved D4 policy above. At most one live-mutation run may execute at a time, and it must use only a same-run created booking ID with marker verification and finally cleanup.
- **Lifecycle:** The run-specific UI environment is ephemeral and uses a unique Compose project. Readiness, 20-minute bound, diagnostics, teardown on success/failure/partial provisioning/cancellation, and teardown-failure behavior are specified above. Cleanup after abrupt runner loss or hard termination and cancellation-safe post-step guarantees remain **Not Found**.
- **Configuration:** API and UI targets remain distinct. CI supplies the private per-run UI target. UI source provenance and required build toolchain are fixed as specified above. Exact runtime-only Compose and probe-container mechanics, private ports beyond the dynamic frontend mapping, runtime credentials, Docker/Compose runner configuration, and digest-enforcement implementation are **Not Found**.
- **Performance:** No fixed full-run time limit is specified. Keep the test scope focused and execution bounded.
- **Browser:** CI default is headless Chromium, viewport 1280 × 720; browser and viewport must remain configurable.
- **Availability and compliance:** **Not Found** beyond the failure and diagnostic behavior specified above.

## Acceptance criteria

1. Given valid API credentials, when the suite runs, it obtains an auth token and reuses it across dependent tests within the same run rather than fetching it unnecessarily per test.
2. Given valid booking data, when the suite creates a booking via the API, the API returns HTTP 200 and the response contains a valid booking ID.
3. Given an existing booking ID, when the suite retrieves the booking via the API, the returned details match the data created by that test.
4. Given a non-existent booking ID, when the suite retrieves it via the API, the API returns HTTP 404 and the test explicitly asserts that expected response.
5. Given valid updated data and a valid token, when the suite sends PUT to the API, the booking is fully updated.
6. Given a valid token, when the suite sends PATCH to the API with specified fields, only those fields are updated.
7. Given a valid token, when the suite deletes a booking via the API, DELETE removes it and a subsequent GET for its ID returns HTTP 404.
8. Given an invalid or missing token, when the suite sends PUT, PATCH, or DELETE to the API, the API returns HTTP 401 and the test explicitly asserts the response.
9. A live API test may update or delete only the booking ID returned by that same test run's confirmed successful create. Before each update or delete, including cleanup, it verifies that the booking contains that run's marker; if the ID is unavailable or the marker is absent or mismatched, it aborts without performing the mutation. Tests do not list or select arbitrary booking IDs.
10. When test setup or cleanup fails, the affected tests fail and tests depending on failed setup are skipped; the suite does not silently retry or report fallback success.
11. When the target API or UI is unavailable or intermittent, the affected test fails clearly with diagnostics and does not report success through fallback behavior.
12. When CI runs the UI suite, it checks out the Restful Booker Platform source at commit `d36bd3f8647a091d406e53bad463c5e3e5d2ece1`, not a moving branch, tag, or "latest" revision, and provisions the private UI environment from that source.
13. When Playwright executes the booking workflow, it browses the run-specific private UI, submits the booking there, and verifies confirmation from that same environment.
14. UI tests do not submit bookings to `https://automationintesting.online`, use shared public UI data, or rely on API-created bookings.
15. When CI runs overlap, each run uses a separate private UI environment and booking state; no run or external user shares or mutates another run's UI test state.
16. If provisioning or readiness fails, the UI suite fails with diagnostics and does not run against the public UI. Each run uses a unique Compose project and runtime-only override that replaces the upstream fixed host mappings, publishes only the frontend container port 80 on an OS-assigned `127.0.0.1` port, and keeps backend and H2 ports unpublished with `dbServer` disabled. The UI base URL is derived from the discovered port. Readiness requires `/booking/actuator/health`, `/room/actuator/health`, `/branding/actuator/health`, `/auth/actuator/health`, `/report/actuator/health`, `/message/actuator/health`, and frontend `/`; backend checks use a short-lived digest-pinned probe container on the private Compose network. The combined image-build/start/readiness phase must complete within 20 minutes. A separately labeled, bounded, run-scoped provisioning-diagnostics artifact is uploaded only for private UI environment provisioning/readiness failures; it may include sanitized Compose status/log summaries and endpoint response summaries, but not raw network bodies/headers, environment dumps, Docker inspection/configuration output, or unredacted logs. Probe image provenance/digest and network attachment, exact Compose override, diagnostic destination/quantitative bounds, implementation, and runtime verification are **Not Found**.
17. CI attempts project-scoped teardown after success, test failure, partial provisioning/readiness failure, and cancellation through a cancellation-safe post-step. If tests passed but teardown fails, the job fails; if another failure is primary, preserve it and report teardown failure separately. Cleanup after abrupt runner loss or hard termination and the runner guarantee for the post-step are **Not Found**.
18. The API suite continues to target `https://restful-booker.herokuapp.com`, independently of the private UI environment; it does not assert that API bookings appear in the UI.
19. UI tests use resilient locators and verify the relevant room, form, and confirmation elements in the private environment.
20. UI tests run headless in CI using Chromium at 1280 × 720 by default, with configurable browser and viewport and headed local debugging available.
21. UI failures capture screenshots with sensitive fields masked; Playwright traces and videos are disabled. CI installs the required browser dependencies.
22. CI publishes separate, clearly labeled API and UI TRX results, retaining all uploaded test reports, masked failure screenshots, and provisioning-diagnostics artifacts for 7 days. A separately labeled, bounded, run-scoped provisioning-diagnostics artifact is uploaded only when private UI environment provisioning/readiness fails.
23. The UI base URL is supplied by the per-run environment and is not hardcoded to the shared public UI. Credentials remain external to source control; UI runtime credential requirements are **Not Found**.
24. Reports, diagnostics, and captured screenshots redact configured credentials, authentication tokens/cookies, booking values, and run markers. Only synthetic test values are used; credentials, tokens, and raw API response bodies are never logged. Raw network bodies/headers, environment dumps, Docker inspection/configuration output, and unredacted logs are excluded, and diagnostics that cannot be safely sanitized are not published.
25. The UI source build and provisioning use JDK 26, Maven 3.9.14, Node 24.14.1, and npm 11.11.0, as specified by the root README at the pinned source revision. CI installs and verifies these exact versions using reviewed full-SHA-pinned setup actions.
26. For overlapping CI runs, each run uses a unique Compose project and runtime-only override, publishes only the frontend on a dynamically assigned `127.0.0.1` port, keeps backend and H2 ports private, derives the UI URL from the discovered port, and cleans up only its own project. The exact Compose override and runner-specific runtime validation are **Not Found**.
27. The workflow runs secret-free pull-request validation and limits credentialed integration to trusted-branch/manual runs; PR-controlled code never executes with privileged secrets. Workflow-level permissions default to `{}`, with `contents: read` only for checkout jobs. All third-party actions are reviewed and pinned to full commit SHAs. CI uses `ubuntu-24.04`, reviewer-gated GitHub Environment secrets only for trusted integration jobs, and digest-pinned base images. Exact event/branch/environment/reviewer configuration, action/setup SHAs, secret names/scope, base-image digest enforcement, and runner-specific permission details are **Not Found**.
28. Live public API mutations run only in a dedicated trusted/manual workflow lane after reviewer-gated GitHub Environment approval, never in secret-free pull-request validation. No more than one live-mutation run is active at a time. The exact serialization mechanism is **Not Found**.
29. Each live-mutation run uses synthetic, non-sensitive booking data with a unique high-entropy run marker and operates only on the booking ID returned by that same run's confirmed successful create. Before each update or delete, including cleanup, it verifies the marker on that ID; if the ID is unavailable, missing, or mismatched, it aborts without mutation. It never lists or selects arbitrary IDs. The marker storage field and exact entropy criterion are **Not Found**.
30. Live POST, PUT, PATCH, and DELETE requests are not automatically retried. If create outcome is uncertain, the run aborts without retrying, guessing an ID, or continuing mutations.
31. After confirmed successful creation, the run attempts marker-verified cleanup in a finally/teardown path. Cleanup failure fails an otherwise-passing job; if another failure is already primary, preserve it and report cleanup failure separately.
