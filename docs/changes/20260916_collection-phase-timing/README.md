# Collection phase timing

- Status: Implemented
- Owner: HorseRacingPrediction team
- Created: 2026-09-16
- Updated: 2026-09-16

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | Task and shared-session timing attribution is connected to every Collector internal HTTP client. |
| Verification | Verified | Focused 27/27, Collector 214/214, non-external 1,009 plus one intentional skip, formatting and zero-warning Release build passed. |
| Deployment/operation | Complete | Logging uses the existing Collector output; no AWS resource or concurrency change was made. |

## Context

The existing terminal event attributes task wall time to `AcquireMs`, `HandlerMs`, `CompleteMs`, and `UnattributedMs`, but `HandlerMs` combines JRA navigation/capture/parsing with internal API waits. A shared JRA browser session also remains alive across a compatible envelope, including task-control and application calls. Consequently current logs cannot determine whether Snapshot-first would improve Lambda duration or merely move already-fast API work.

The Snapshot-first proposal returned to `Proposed` on 2026-09-16. This independent measurement change supplies the missing evidence without changing collection behavior.

## Goals

- Measure internal API calls and elapsed time while a collection handler is active.
- Measure the lifetime of each shared JRA session envelope and the internal API time/call count occurring while it is held.
- Preserve the existing terminal task runtime event and make all new measurements safe for production aggregation.
- Make timing deterministic in tests and available on success, classified failure, and cancellation paths.

## Non-goals

- Implementing Snapshot-first, durable snapshots, `Applying`, inbox/outbox, or API-owned processing.
- Adding browser tabs, parallel navigation, queue concurrency, AWS resources, metrics services, or tracing backends.
- Logging URLs, task/resource IDs, lease tokens, payloads, page content, response bodies, exception messages, or arbitrary HTTP route values.
- Claiming a speedup from instrumentation alone.

## Technical impact

An async-local timing scope collects monotonic elapsed values and bounded counters for the current task and shared JRA envelope. All internal Collector HTTP clients pass through one timing handler that records elapsed time and count without retaining routes or request data. The existing task terminal event adds handler-internal API elapsed/count and derives non-API handler time without double counting. The JRA session scope emits one terminal event containing total session-scope time, API elapsed/count while the session is alive, derived non-API time, task count, result category, and whether a browser session was actually created.

Fixed categories are limited to task control, domain write, referenced-request write, prediction scheduling, and other internal API. Endpoint paths and identifiers are never emitted. Nested/retried calls are counted once at the HTTP send boundary. Timing observation must not alter exception, cancellation, lease, retry, or disposal behavior.

## Decisions

- Extend the existing structured logging path rather than introduce a paid telemetry service.
- Use monotonic time and injectable clocks in tests.
- Record milliseconds and counts only; derive percentages offline from terminal events.
- Keep task and envelope events separate because a browser session can span several tasks.
- Measure all internal API waits, including failures, while preserving the original exception.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Every acquired collection task emits exactly one terminal runtime event on success, classified failure, and cancellation. It retains existing fields and adds `HandlerApiMs`, `HandlerApiCallCount`, and `HandlerNonApiMs`; nonnegative phase values account for at least 99% of handler time within clock precision. | T1 | Deterministic clock tests for success, HTTP failure, handler failure, and cancellation. | Verified |
| AC2 | Every compatible JRA envelope emits exactly one terminal session event after disposal. It reports `SessionMs`, `SessionApiMs`, `SessionApiCallCount`, `SessionNonApiMs`, task count, fixed compatibility/result categories, and whether a browser was created; values account for at least 99% of session time. | T2 | One/12-task, no-browser, failure, cancellation, and disposal-order tests. | Verified |
| AC3 | Logs contain no URL, resource/task/request ID, lease token, payload, page content, response body, exception message, or unbounded route/category value. Existing collection behavior, HTTP requests, retries, cancellation and browser disposal remain unchanged. | T1,T2 | Captured-log allowlist test plus request/exception/disposal equivalence tests. | Verified |
| AC4 | Exact formatting, Release build, relevant Collector tests, and full non-external regression tests pass. No AWS, queue, browser-concurrency, database, API-contract, or domain-state change is present. | T1,T2 | CI-equivalent commands, configuration diff, CodeGraph sync and final diff review. | Verified |

## Delivery plan

1. Freeze the fixed categories, nested timing semantics and log allowlist.
2. Add task-level internal-API attribution and deterministic tests.
3. Add shared-session lifetime attribution and lifecycle tests.
4. Run the full verification gates and record how to query/compare the resulting events.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Attribute handler-internal API elapsed/count and derived non-API time. Covers AC1, AC3, AC4. | Main | Lead tier | Approval | Collector HTTP/timing scope and focused tests | Deterministic terminal-event and behavior tests | One safe task event on every terminal path | Verified |
| T2 | Attribute shared JRA session lifetime and API waits. Covers AC2–AC4. | Main | Lead tier | T1 timing contract | JRA session scope/invocation and lifecycle tests | Deterministic envelope lifecycle tests | One safe post-disposal session event | Verified |

The tasks are serialized because the timing scope and logging contract are shared. No worker production write is planned; read-only inventory results from the withdrawn Snapshot-first work supplied the call-path evidence.

## Review gates

- **Design and task-split review** — 2026-09-16, reviewer: Main. Existing CodeGraph paths and runtime telemetry were reviewed. AC1–AC4 cover the task event, envelope event, safety/non-interference, and regression boundary. T1 and T2 are serialized under one owner because both use the same async-local aggregation contract. Fixed categories prevent cardinality and secret leakage. The change is measurement-only and does not authorize Snapshot-first or AWS changes. Provider usage/cost telemetry is unavailable; using the existing logging backend is the lower-cost option.
- **Pre-implementation review** — 2026-09-16, reviewer: Main. The user approved AC1–AC4. T1 is `Runnable`; T2 is `Dependent` on the shared timing contract established by T1. Both production write scopes remain serialized under Main. Inputs are the existing task terminal logger, internal HTTP registrations, session execution scope, focused telemetry/lifecycle tests, and the approved allowlist. Completion evidence is deterministic timing attribution plus unchanged request, exception, cancellation and disposal behavior. Escalate on any need to expose identifiers/content, change a public/API/persistence contract, add infrastructure, or alter browser/queue concurrency.
- **Checkpoint review** — 2026-09-16, reviewer: Main. Task scopes exclude acquire/complete, while session scope includes all internal API waits until browser disposal. Focused tests passed, but independent review requested stronger HTTP-failure, retry, parallel AsyncLocal, dispose-failure and strict-field-allowlist evidence; all requested tests were added and passed.
- **Final review** — 2026-09-16, independent reviewer: `/root/phase_timing_review`. Re-audit found no blocking issue and accepted AC1–AC4 as Verified. It confirmed terminal paths, attribution boundaries, exactly-once post-disposal logs, concurrency isolation, logical retry counting, strict field allowlists, all typed-client registration, and absence of AWS/DB/API/queue/browser-concurrency changes.

## Documentation updates

- `docs/changes/20260915_snapshot-first-bulk-ingestion/README.md`: returns the pilot to `Proposed` and records phase timing as the prerequisite for renewed approval.
- Existing architecture documents were inspected. No canonical architecture update is required because collection behavior and deployment topology do not change.

## Verification record

- 2026-09-16: CodeGraph traced `CollectionPlatformWorkerClient.ExecuteAsync`, `CollectionWorkerLeaseHandler`, `JraSessionExecutionScope.ExecuteAsync`, `CollectionLambdaInvocation`, and the race-detail application clients before design.
- 2026-09-16: Current fixed-corpus estimates are 24–72 application HTTP waits inside a 12-task shared session, but current logs cannot isolate their elapsed share. This record measures rather than assumes that share.
- 2026-09-16: Focused telemetry, lifecycle and worker-client tests passed 27/27; the full Collector suite passed 214/214.
- 2026-09-16: The full non-external solution suite passed 1,009 tests with one intentional API skip. Exact formatting and the Release solution build passed with zero warnings and errors.
- 2026-09-16: CodeGraph synchronization and `git diff --check` passed. Independent final review found no blocking issue.
- No schema, AWS resource, API contract, queue setting, browser concurrency, or external data was changed.

## Deviations and follow-up

- After sufficient production samples exist, a separate decision will compare session API share, p50/p95, failures and Lambda GB-seconds before reconsidering Snapshot-first.
