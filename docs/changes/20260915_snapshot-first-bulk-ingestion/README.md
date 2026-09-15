# Snapshot-first collection and bulk ingestion

- Status: Proposed
- Owner: HorseRacingPrediction team
- Created: 2026-09-15
- Updated: 2026-09-15

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | User approval is required before production changes. |
| Verification | Not started | The approved implementation must pass the tests and performance comparison in this record. |
| Deployment/operation | Not started | No AWS capacity increase or new paid service is planned for the initial release. |

## Context

Production observation on 2026-09-15 showed that the collector is slow even though Lambda memory and the API host CPU have headroom. The current non-refresh race-card path interleaves Playwright work with many synchronous API writes. For an 18-runner race it performs 20–38 write-service calls and can then issue up to 72 individual subject collection requests. Internal read-before-write calls can increase the actual HTTP count further.

The semantic `PageSnapshot` is already captured with one browser evaluation and all JRA parsers consume that immutable snapshot without requiring Playwright. The cheapest first step is therefore to stop holding the browser while domain writes are performed and replace per-item HTTP traffic with one durable, normalized ingestion request per compatible execution envelope. This change deliberately does not increase Lambda or queue concurrency.

## Goals

- Complete all navigation and semantic snapshot capture before starting domain/API writes for a collection unit.
- Release the Playwright session before the normalized ingestion request is processed.
- Submit one normalized ingestion envelope containing the captured results for every compatible race task in the current execution envelope instead of per-race and per-entry write calls.
- Persist the accepted envelope and processing state before returning success to the Collector, then apply it on the existing API host without Playwright.
- Batch and deduplicate referenced Horse, Jockey, Trainer, and Owner collection requests in the same processing flow.
- Make ingestion replayable and idempotent without repeating Playwright after the API has accepted the envelope.
- Reduce HTTP requests, SQLite transactions, and time for which a costly browser session is held, without adding AWS fixed capacity.

## Non-goals

- Increasing Lambda reserved concurrency, SQS event-source batch size, or `MaxInFlightEnvelopes`.
- Adding S3, another SQS queue, another Lambda, ECS/Fargate, RDS, or another always-on service in the initial release.
- Running browser operations in parallel or using the same Playwright page concurrently.
- Persisting every full semantic DOM snapshot indefinitely.
- Making writes across all domain aggregates one distributed all-or-nothing transaction.
- Changing collection task identity, scheduling priority, retry policy, or JRA access rate.
- Replacing SQLite in this change.

## Experience and operational behavior

The collection task remains the operator-visible unit. A successful task means that its normalized snapshot was accepted durably and all required application stages completed. Acceptance alone transitions the task from `Running` to a new non-terminal `Applying` state; it does not report success. While processing is pending, the task remains active and shows the current phase: capture, ingestion accepted, domain application, or referenced-request creation.

If navigation or capture fails, the existing collection retry behavior applies and Playwright may run again. If domain application fails after ingestion acceptance, the API-owned processor resumes from the accepted envelope and must not launch Playwright again. The Lambda does not poll for processing completion and returns as soon as the handoff is durable. Permanent validation or schema errors remain visible with the failing stage and item identity.

## Technical impact

### Snapshot-first Collector boundary

For each compatible `race-detail` execution envelope the Collector will:

1. Acquire each included task immediately before its capture and receive an API-generated capture key that is already bound durably to that task attempt.
2. Reuse one JRA session to navigate and capture the terminal semantic `PageSnapshot` values for all startable tasks, preserving the current compatible-envelope microbatch behavior.
3. Parse them into provider-neutral collected-race values while no additional browser operation is started. Heartbeat acquired tasks that are waiting for the group capture to finish and stop before the Lambda deadline.
4. End the envelope's shared browser/session scope once.
5. Send one ingestion envelope containing all captured task payloads to the internal API.
6. Return after the API atomically accepts or identifies every task handoff; do not poll for domain application.

The ingestion envelope records a per-task capture outcome. Successfully captured tasks carry their normalized payload. A navigation or parse failure is submitted as a structured capture failure only while that task's Collector lease is current. Tasks not started before a session failure or deadline remain unresolved for SQS redelivery. After a failure that can invalidate the shared session, the Collector stops starting tasks, disposes the session, and hands off all earlier successful captures whose leases remain current. If task N's lease was lost or expired, the API rejects its stale outcome and normal lease recovery/redelivery handles N; this does not reject valid handoffs for tasks 1 through N-1. If cancellation was already durably recorded by the API, the Collector acknowledges that cancellation without mutating the task through its stale lease. A failure in task N never discards valid captured tasks 1 through N-1.

The initial production scope is only the existing unified `race-detail` definition. Its versioned payload is a discriminated race-detail contract containing race-card data and, when collected by that same task, race-result data. Race-odds tasks and Horse/Jockey/Trainer/Owner profile page parsing do not move to this ingestion path. The envelope is a normalized, compact DTO rather than the complete semantic DOM. It contains a contract version, stable capture key, payload SHA-256, task/attempt correlation, provider/resource identity, requested and final URLs, capture time, race-card/result data present in the task, citations, and the deduplicated referenced-resource requests derived from those values.

The API generates and stores the stable capture key while the task attempt is acquired, before Playwright starts, and returns it with the lease. The Collector reuses it for the accepted payload and every transport retry. The API returns the existing receipt for the same key and payload hash and returns conflict for the same key with different content.

### Durable API ingestion

The internal API accepts one envelope, validates its bounded size and version, and atomically persists:

- the immutable normalized payload;
- its idempotency key and hash;
- processing phase and per-stage outcome;
- a processing outbox/work item.

The acceptance transaction also verifies each current Collector lease, binds the receipt to its task/attempt, transitions that task from `Running` to `Applying`, fences the Collector lease, and creates an API-processing lease/work item. Only after that transaction commits may it return an accepted response. The existing API host processes accepted receipts without Playwright through a bounded, independently testable ingestion cycle invoked by a hosted loop. The loop has a maximum item count, processing lease and deadline; it is not the core business operation. The stages are race/card application, result application when present, citation application, and referenced collection-request creation.

The domain EventStore and CollectionPlatform task store are separate SQLite databases, so no stage claims a cross-database transaction. Every domain item or group receives a deterministic ingestion operation key. Its domain changes and idempotency marker commit together in the EventStore transaction. The processor then records the structured result and checkpoint in CollectionPlatform. If it dies after the domain commit but before that checkpoint, replay presents the same operation key; the domain side returns the prior result without adding events, after which the missing checkpoint is written. Referenced requests use the same pattern through a batch idempotency key. A failure before domain commit leaves no marker; a failure after commit is recovered through the marker rather than cross-database rollback.

If the acceptance commit succeeds but the HTTP response is lost, SQS redelivery does not reacquire the task for capture. Acquire returns an `Applying`/already-handed-off result with the existing receipt identity, so the Collector acknowledges that task reference without launching Playwright. If acceptance did not commit, the original attempt remains `Running` until its normal lease expiry and recovery rules apply.

The API processor, not the Lambda, owns final completion after handoff. Its fenced processing lease prevents concurrent application and is renewed only while a bounded cycle is active. On success it transitions the original task and attempt to `Succeeded`; retryable application failure schedules another API-side processing attempt without a collection/SQS retry; permanent failure records the stage/item detail and transitions the task through the existing failure policy.

The domain application remains idempotent and may report structured per-item validation failures. A stage may contain several independently keyed domain items/groups. Retry resumes missing keys only; committed keys are no-ops, and non-retryable item failures are recorded as terminal structured outcomes. The durability guarantee is atomic receipt plus processing work and replay-safe item commits, not a distributed rollback across separate databases or aggregates.

Cancellation of an `Applying` task fences or invalidates its processing lease. Already committed domain stages remain recorded and are not rolled back; unstarted stages and referenced requests are skipped, the task becomes `Cancelled`, and restart must not resume it. A completion racing with cancellation is accepted only when its processing lease token is still current.

### Bulk domain and collection-request application

The ordinary race-card path will reuse or generalize the existing race-card/result bulk contract instead of calling race, horse profile, and entry endpoints individually. A structured response identifies each rejected item and whether it is retryable.

Referenced resource requests are normalized and deduplicated before persistence. The store preloads definitions, revisions, suppressions, existing resources, matching batch requests, active tasks, and states for the complete key set; it then creates the required rows in one transaction with at most two `SaveChangesAsync` calls. A database uniqueness rule for the batch/idempotency identity protects concurrent replay instead of relying only on the process-local semaphore.

Client-side `Task.WhenAll` is not the primary optimization because it does not reduce request count and merely moves contention to SQLite. Bounded parallelism may be used only for independent remaining operations after the browser has been released and only when measurements show that the target endpoint is not serialized by the database gate.

### Snapshot retention decision

The normalized envelope is persisted until its configured retention period expires; it is sufficient to replay API/domain work. Full semantic DOM snapshots are not stored on every success in the initial release because recorded examples are roughly hundreds of kilobytes per page and may exceed raw HTML size. Tests and failure diagnostics may serialize redacted/compressed semantic snapshots under an explicit size limit. Always-on object storage and a separate non-Playwright worker are reserved for a later change if production evidence shows that failures before API acceptance cause material repeat-browser cost.

## Decisions

- Optimize work per Playwright invocation before increasing concurrency.
- Use semantic `PageSnapshot` as the in-process browser/parser boundary and a smaller normalized collected snapshot as the durable API boundary.
- Release the browser before the API application phase.
- Prefer one durable bulk ingestion request to parallel single-item requests.
- Run post-capture processing on the existing, underutilized API host; do not add AWS services in the initial release.
- Preserve current collection task identity while adding an explicit `Applying` state and transferring fenced completion ownership from Collector to the API processor at acceptance.
- Retain full semantic snapshots only for bounded failure diagnostics initially; revisit S3-backed replay separately if measured repeat-capture cost warrants it.

## Alternatives considered

### Raise Lambda and queue concurrency first

Rejected for this change. It increases simultaneous Playwright cost and JRA load while leaving approximately 110 sequential calls per race and SQLite contention intact.

### Send all existing API calls with `Task.WhenAll`

Rejected as the main design. It reduces some client wall time but not HTTP count or read-before-write races, and the collection store serializes writers through a per-database gate.

### Persist every semantic DOM snapshot to S3 and use a second Lambda

Deferred. It provides the strongest replay boundary but introduces storage, IAM, lifecycle, dispatch, deployment, and cross-service recovery work before the cheaper bulk-ingestion improvement is measured.

### Persist raw HTML only

Rejected. Current parsers depend on rendered semantic structure, resolved links, visibility, table spans, and cell fragments; reconstructing those reliably would require another browser-like runtime.

### Persist only final domain commands

Rejected as the only artifact. It is compact but loses the normalized collected evidence and makes parser/application discrepancies harder to diagnose. The collected snapshot envelope retains source and version metadata while remaining much smaller than the full DOM snapshot.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | A compatible envelope containing 12 normal race-detail tasks uses one shared JRA session to capture/parse all startable tasks, disposes that session once, and then sends one ingestion request with no legacy per-entry write calls. | T1, T3 | Microbatch workflow test asserts one session, ordering, one request, zero legacy calls, and disposal before ingestion. | Not started |
| AC2 | The ingestion API durably saves every normalized task payload and processing work, transfers each task from `Running` to `Applying`, and returns without the Lambda polling; an API process restart resumes with zero browser-factory calls. | T1, T2 | API/store integration test with restart, state/lease assertions, and browser-factory zero-call assertion. | Not started |
| AC3 | The capture key is durably bound at acquire. Repeating the same key and hash returns the original receipt without duplicates; the same key with a different hash conflicts; a crash after acceptance commit but before response causes redelivery to acknowledge the existing handoff without Playwright. | T1, T2, T3, T4 | Concurrent submission, response-loss/redelivery/lease-expiry, and hash-conflict integration tests. | Not started |
| AC4 | Race, race-card entries, horse profile fields, citations, and race-result data produced by the current `race-detail` workflow remain equivalent to the legacy path; odds and subject-profile tasks remain on their existing paths. | T2, T3 | Golden legacy-versus-ingestion comparison and entry-point matrix through real API and persistence boundaries. | Not started |
| AC5 | Referenced Horse, Jockey, Trainer, and Owner requests are deduplicated and persisted in one batch transaction without one HTTP request per subject. | T2, T4 | 18-entry integration test verifies one batch and exact resources/tasks. | Not started |
| AC6 | A transient failure before domain commit, after domain commit but before the CollectionPlatform checkpoint, or at a stage boundary resumes missing operation keys only, does not rerun Playwright, and does not duplicate domain events, citations, tasks, stages, or items. | T2, T3 | Fault injection on both sides of every EventStore/CollectionPlatform commit ambiguity window plus restart/retry tests. | Not started |
| AC7 | Validation, unsupported version, oversized payload, capture heartbeat/lease expiry, processor lease expiry, partial item failure, and cancellation in `Running` or `Applying` have explicit fenced terminal or retryable outcomes visible on the collection attempt. Cancelled accepted work never resumes after restart. | T1, T2, T5 | Contract, worker, store, API error-path, expiry-race, and cancellation-restart tests. | Not started |
| AC8 | For the fixed 12-race × 18-entry benchmark, all 18 Horse/Jockey/Trainer/Owner identities are distinct, so the legacy path executes the documented 38 application plus 72 referenced-request service calls per race. Post-capture application/reference HTTP calls and total EventStore plus CollectionPlatform write transactions each fall by at least 80%. The candidate uses at most one ingestion-acceptance transaction per 12-task envelope plus eight write transactions per race, including claims, domain groups, reference batch, checkpoints, and finalization. Collection-request prefetch uses at most eight set queries and two `SaveChangesAsync` calls per batch, independent of entry count. Browser-held time excludes ingestion/application and persisted data matches baseline. | T3, T4, T5 | Instrumented baseline/candidate report lists exact HTTP, query, transaction, stage and browser-lifetime counts plus p50/p95, and fails if either the 80% comparison or numeric candidate budget is exceeded. | Not started |
| AC9 | The initial deployment adds no AWS service and does not raise Lambda, SQS, or dispatcher concurrency. | T5 | Terraform/config diff and deployment checklist. | Not started |
| AC10 | Existing non-external solution tests, formatting verification, Release build, and the end-to-end capture→ingestion→persistence happy and restart paths pass. | T1–T5 | CI-equivalent commands and E2E evidence. | Not started |
| AC11 | If task N has a navigation/parse failure while its lease is current, its structured failure is accepted; if its lease is lost/expired, its stale outcome is rejected and N follows normal recovery/redelivery; if cancellation is already durable, it is acknowledged without stale mutation. In every case, valid captures 1 through N-1 are handed off after the shared session is disposed and unstarted tasks are redelivered. | T2, T3 | Mid-envelope navigation failure, cancellation, one-stale-lease partial acceptance, and deadline tests verify fencing and that no valid successful capture is discarded or repeated. | Not started |

## Delivery plan

1. Freeze the versioned normalized envelope, receipt, error, and processing-stage contracts.
2. Add the durable API inbox/outbox, `Applying` ownership handoff, and restart-safe bounded processor with idempotency gates.
3. Add bulk race-card application and heterogeneous referenced-request batch persistence.
4. Restructure the Collector into capture/parse and post-browser ingestion phases; retain the old path behind a temporary rollback flag.
5. Add operational phase timings, request/transaction counters, regression coverage, and a representative benchmark.
6. Deploy with existing concurrency limits, observe one full collection window, then remove the rollback path only in a separately verified cleanup checkpoint.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Define the `race-detail`-only collected snapshot, acquire-issued capture key, receipt, `Applying` handoff, and structured outcome contracts. Covers AC1–AC3, AC7, AC10. | Main | Lead tier | - | `Contracts`, `ApiClient`, contract tests | Contract serialization/version/hash/state tests | Reviewed frozen contract and passing tests | Proposed |
| T2 | Implement API inbox/outbox schema, atomic task ownership handoff, bounded finite processor cycle, replay checkpoints, cancellation and structured outcomes. Covers AC2–AC7, AC10, AC11. | Main | Lead tier | T1 | `CollectionOperations` schema/store/migration and API ingestion services/tests | Restart, cross-database ambiguity-window fault injection, replay, expiry, cancellation and concurrency tests | Durable/replayable real persistence path | Proposed |
| T3 | Refactor the envelope-level Collector/session workflow to capture all race-detail tasks with one session, dispose once, and submit one envelope without polling. Covers AC1, AC3, AC4, AC6, AC8, AC10, AC11. | Main | Lead tier | T1, T2 | Collector collection-platform/session execution, race-detail workflow, focused tests | Microbatch ordering, partial capture, response-loss, equivalence, heartbeat and cancellation tests | One-ingestion-call production path retaining session reuse | Proposed |
| T4 | Implement race-card/result and referenced-request bulk application with set-based prefetch and database uniqueness. Covers AC3–AC5, AC8, AC10. | Main | Lead tier | T1, T2 | API/Application/CollectionOperations bulk paths, schema/index, tests | Query/transaction counts, concurrent replay, domain equivalence | Bounded-query idempotent bulk path | Proposed |
| T5 | Add phase metrics and the fixed benchmark, then update operational documentation. Covers AC7–AC10. | Worker | Worker tier | T2–T4 | Collector/API metrics, benchmark tools/tests, `docs/01-lambda-collector-architecture.md`, `docs/23-jra-scraping-redesign.md`, this record | Benchmark, CI-equivalent suite, config diff, CodeGraph sync/queries | Measured AC evidence for Main final review | Proposed |

Tasks that share contracts, schema, migrations, or generated snapshots remain serialized under Main ownership. T3 starts only after T1 is frozen and T2 exposes a reviewed fake/test boundary. T5 measurement work may start after the measured interfaces are frozen, but final verification depends on all implementation tasks.

## Review gates

- **Design and task-split review** — 2026-09-15, reviewer: Main. Inputs: production AWS evidence, CodeGraph paths, semantic snapshot design records, current workflow/store/API code, delegated D1/D2 research, and independent D3 review. First decision: revise; D3 identified undefined completion ownership, commit-response-loss replay, conflict with envelope-scoped session reuse, and ambiguous definition scope. Second decision: revise; D3 identified an impossible cross-database stage transaction claim, an unfrozen database performance target, and undefined partial capture behavior. Final decision: pass after separating current-lease failure, stale-lease recovery, and durable cancellation. API-owned `Applying` handoff, acquire-bound capture key, response-loss behavior, envelope-level capture/disposal, `race-detail`-only scope, EventStore operation-key idempotency across the checkpoint ambiguity window, numeric transaction/query budgets, partial-envelope outcomes, cancellation, and task ownership are explicit. D1/D2/D3 are `Verified`; they made no file changes. Rework: two design-revision cycles, no implementation rework or escalation. Measured usage/cost was unavailable; elapsed investigation and revision count are the efficiency proxy. Follow-up: request user approval before any production edit.
- **Pre-implementation review** — Pending approval. Classify tasks and record exact worker contracts before code changes.
- **Checkpoint review** — Pending implementation.
- **Final review** — Pending implementation and full evidence reconciliation.

## Documentation updates

- `docs/01-lambda-collector-architecture.md`: adds the canonical rule that browser capture precedes a durable normalized ingestion phase and that concurrency remains unchanged for this optimization.
- `docs/23-jra-scraping-redesign.md`: clarifies semantic snapshot reuse, browser lifetime, and the normalized durable handoff boundary.
- Existing snapshot API/cutover and microbatch change records were inspected and remain historical records; they are linked rather than rewritten.

## Verification record

- 2026-09-15: Used CodeGraph before targeted repository searches to trace `PageSnapshot`, `JraPageReader`, JRA parsers, race-card workflows, write services, collection requests, store serialization, and Lambda/session boundaries.
- 2026-09-15: Confirmed all registered JRA page parsers consume semantic `PageSnapshot` without Playwright, while Navigator page transitions and history pagination still require Playwright.
- 2026-09-15: Confirmed the non-refresh 18-entry race-card path has 20–38 sequential write-service calls and up to 72 sequential referenced-resource request calls; existing result/race-card refresh paths already demonstrate a bulk contract.
- 2026-09-15: Confirmed the API collection store serializes writes per SQLite database and that parallel single-item requests would not remove transaction/query cost.
- No production source, test, configuration, schema, or AWS resource was changed before approval.

## Deviations and follow-up

- A later evidence-based change may persist full compressed semantic snapshots in private object storage and run a separate non-Playwright processor. It is intentionally outside this initial no-new-service scope.
- PostgreSQL/RDS migration and collector fleet parallelism remain separate decisions after the per-browser work reduction is measured.
