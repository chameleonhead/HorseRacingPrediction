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

The semantic `PageSnapshot` is already captured with one browser evaluation and all JRA parsers consume that immutable snapshot without requiring Playwright. A horse-profile snapshot also contains many historical-race rows; the existing 70-race fixture parses in about 0.08–0.09 ms after runtime warm-up, whereas a bounded live profile-to-result E2E took 44.0 seconds (45.374 seconds wall time) on the local Windows development machine. Browser navigation, not snapshot parsing, dominates this path.

Snapshot-first may materially reduce browser-held API waits, transport calls and replay cost, but current evidence does not prove that those phases dominate every job. Playwright/Snapshot/navigation improvements are therefore moved to a separate, higher-priority change and measured independently. This change follows later as a feature-flagged `race-detail` pilot before any subject expansion, and deliberately does not increase Lambda or queue concurrency.

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
- Changing collection task identity, scheduling priority, retry policy, or JRA access rate, except for merging duplicate historical-race requests while preserving the highest existing lane/priority and earliest due time.
- Replacing SQLite in this change.
- Assuming that same-age horses share a profile/history page or that cohort grouping alone reduces JRA page navigations.
- Enabling incremental history-page cutoff until live evidence proves ordering, correction, and overlap rules.
- Treating incidental Jockey/Trainer tables as race history until controlled live snapshots establish their semantics and an explicit persistence requirement is approved.
- Enabling parallel page navigation in production; any rollout requires a separate approved change regardless of diagnostic benchmark results.
- Playwright/Snapshot/navigation micro-optimizations, which are governed by [Playwright collection efficiency](../20260915_playwright-collection-efficiency/README.md).
- EventStore grouping/no-op changes, HTTP compression/retry, and Lambda/runtime tuning, which are governed by [Collection application and runtime efficiency](../20260915_collection-application-runtime-efficiency/README.md).

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

The pilot scope contains only the existing unified `race-detail` definition. Its versioned payload contains race-card data and, when collected by that same task, race-result data. Horse, Jockey, Trainer, race-odds and Owner definitions do not move to this ingestion path in the pilot. The envelope is a normalized, compact DTO rather than the complete semantic DOM. It contains a contract version, stable capture key, payload SHA-256, task/attempt correlation, provider/resource identity, requested and final URLs, capture time, captured data, citations, and deduplicated referenced-resource requests.

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

The pilot invokes the existing race/result/subject application semantics behind its durable operation key. It does not require or claim aggregate grouping, subject set-prefetch, GET-before-write removal, semantic same-state no-op, generic query reduction or projection optimization; those belong exclusively to the application/runtime record.

Referenced resource requests are normalized and deduplicated in the ingestion payload and submitted through one internal API operation. The pilot may adapt the existing application path internally; generic aggregate/query/transaction optimization belongs to the separate application/runtime change and is not required or credited here. The pilot's deterministic batch identity still prevents duplicate logical requests on replay.

Client-side `Task.WhenAll` is not the primary optimization because it does not reduce request count and merely moves contention to SQLite. Bounded parallelism may be used only for independent remaining operations after the browser has been released and only when measurements show that the target endpoint is not serialized by the database gate.

### Subject expansion

Horse history/shared Race frontier and Jockey/Trainer directory/profile batching have moved to [Subject profile/history bulk ingestion](../20260915_subject-profile-history-bulk-ingestion/README.md). They require successful pilot evidence and separate approval.

### Related optimization changes

Browser readiness, candidate discovery, Snapshot projection, retained navigation results and sequential transient pages have their own acceptance and rollout gates in [Playwright collection efficiency](../20260915_playwright-collection-efficiency/README.md). They are implemented and measured before this pilot so their gains are not incorrectly attributed to Snapshot-first.

Aggregate-grouped persistence, semantic no-op/upsert, transport compression/retry and Lambda/runtime tuning are governed independently by [Collection application and runtime efficiency](../20260915_collection-application-runtime-efficiency/README.md). Snapshot-first may consume those verified capabilities but does not authorize them.

### Snapshot retention decision

The normalized envelope is persisted until its configured retention period expires; it is sufficient to replay API/domain work. Full semantic DOM snapshots are not stored on every success in the initial release because recorded examples are roughly hundreds of kilobytes per page and may exceed raw HTML size. Tests and failure diagnostics may serialize redacted/compressed semantic snapshots under an explicit size limit. Always-on object storage and a separate non-Playwright worker are reserved for a later change if production evidence shows that failures before API acceptance cause material repeat-browser cost.

## Decisions

- Optimize work per Playwright invocation before increasing concurrency.
- Use semantic `PageSnapshot` as the in-process browser/parser boundary and a smaller normalized collected snapshot as the durable API boundary.
- Treat canonical Race identity—not horse cohort—as the cross-horse sharing and deduplication boundary.
- Use cohort/birth year only for ordering; use global state-aware race union for correctness and browser-page reduction.
- Release the browser before the API application phase.
- Prefer one durable bulk ingestion request to parallel single-item requests.
- Run post-capture processing on the existing, underutilized API host; do not add AWS services in the initial release.
- Preserve current collection task identity while adding an explicit `Applying` state and transferring fenced completion ownership from Collector to the API processor at acceptance.
- Retain full semantic snapshots only for bounded failure diagnostics initially; revisit S3-backed replay separately if measured repeat-capture cost warrants it.
- Prefer directory indexing and sequential transient pages over concurrent tabs; parallel two-page navigation remains disabled throughout this change and requires a separate approved rollout.

## Alternatives considered

### Raise Lambda and queue concurrency first

Rejected for this change. It increases simultaneous Playwright cost and JRA load while leaving approximately 110 sequential calls per race and SQLite contention intact.

### Send all existing API calls with `Task.WhenAll`

Rejected as the main design. It reduces some client wall time but not HTTP count or read-before-write races, and the collection store serializes writers through a per-database gate.

### Persist every semantic DOM snapshot to S3 and use a second Lambda

Deferred. It provides the strongest replay boundary but introduces storage, IAM, lifecycle, dispatch, deployment, and cross-service recovery work before the cheaper bulk-ingestion improvement is measured.

### Persist raw HTML only

Rejected. Current parsers depend on rendered semantic structure, resolved links, visibility, table spans, and cell fragments; reconstructing those reliably would require another browser-like runtime.

### Process each horse and its historical results end-to-end

Rejected. Horses of similar age frequently can reference the same races, and a race-result snapshot contains the full field. Per-horse traversal would repeat result navigation and application. Horse pages produce a shared Race frontier instead.

### Group horses only by age or generation

Rejected as a primary optimization. Every distinct horse profile/history page still requires navigation, and age does not prove shared participation. Cohort is an ordering hint only; canonical Race identity and current collection state decide reuse.

### Search meeting/result-list pages as a set-cover optimization

Deferred. Current evidence proves direct result URLs and one result snapshot per race, but does not prove that one meeting page provides full results for multiple races. Replacing validated direct links could add navigation and parsing risk.

### Open every target in parallel tabs

Rejected for initial production. It overlaps JRA requests rather than reducing them, and the latest observed Lambda maximum was about 1.45 GB of 2 GB before adding another renderer. It also conflicts with the current single-page browser/Navigator ownership model. A separately measured maximum of two page leases is the only permitted experiment.

### Persist only final domain commands

Rejected as the only artifact. It is compact but loses the normalized collected evidence and makes parser/application discrepancies harder to diagnose. The collected snapshot envelope retains source and version metadata while remaining much smaller than the full DOM snapshot.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | A compatible envelope containing 12 normal race-detail tasks uses one shared JRA session to capture/parse all startable tasks, disposes that session once, and then sends one ingestion request with no legacy per-entry write calls. | T1, T3 | Microbatch workflow test asserts one session, ordering, one request, zero legacy calls, and disposal before ingestion. | Not started |
| AC2 | The ingestion API durably saves every normalized task payload and processing work, transfers each task from `Running` to `Applying`, and returns without the Lambda polling; an API process restart resumes with zero browser-factory calls. | T1, T2 | API/store integration test with restart, state/lease assertions, and browser-factory zero-call assertion. | Not started |
| AC3 | The capture key is durably bound at acquire. Repeating the same key and hash returns the original receipt without duplicates; the same key with a different hash conflicts; a crash after acceptance commit but before response causes redelivery to acknowledge the existing handoff without Playwright. | T1, T2, T3, T4 | Concurrent submission, response-loss/redelivery/lease-expiry, and hash-conflict integration tests. | Not started |
| AC4 | Race, race-card entries, horse profile fields, citations, and race-result data produced by the current `race-detail` workflow remain equivalent to the legacy path. Odds and Owner handling remain on their existing paths; Jockey/Trainer history is not introduced. | T2, T3 | Golden legacy-versus-ingestion comparison and entry-point matrix through real API and persistence boundaries. | Not started |
| AC5 | Referenced Horse, Jockey, Trainer, and Owner requests are deduplicated and persisted in one batch transaction without one HTTP request per subject. | T2, T4 | 18-entry integration test verifies one batch and exact resources/tasks. | Not started |
| AC6 | A transient failure before domain commit, after domain commit but before the CollectionPlatform checkpoint, or at a stage boundary resumes missing operation keys only, does not rerun Playwright, and does not duplicate domain events, citations, tasks, stages, or items. | T2, T3 | Fault injection on both sides of every EventStore/CollectionPlatform commit ambiguity window plus restart/retry tests. | Not started |
| AC7 | Validation, unsupported version, oversized payload, capture heartbeat/lease expiry, processor lease expiry, partial item failure, and cancellation in `Running` or `Applying` have explicit fenced terminal or retryable outcomes visible on the collection attempt. Cancelled accepted work never resumes after restart. | T1, T2, T5 | Contract, worker, store, API error-path, expiry-race, and cancellation-restart tests. | Not started |
| AC8 | For the fixed 12-race × 18-entry pilot corpus, post-capture application/reference HTTP calls fall by at least 80%, persisted data matches baseline, failure/retry rate does not increase, and either total p95 or Lambda GB-seconds improves by at least 10%. Browser-held and API-application times are reported separately. Database transaction/query optimization is not attributed to this pilot. | T3, T4, T5 | Instrumented baseline/candidate report lists HTTP, phase, failure, p50/p95 and GB-seconds and fails unless equality plus the HTTP and total-cost gates pass. | Not started |
| AC9 | The initial deployment adds no AWS service and does not raise Lambda, SQS, or dispatcher concurrency. | T5 | Terraform/config diff and deployment checklist. | Not started |
| AC10 | Existing non-external solution tests, formatting verification, Release build, and the end-to-end capture→ingestion→persistence happy and restart paths pass. | T1–T5 | CI-equivalent commands and E2E evidence. | Not started |
| AC11 | If task N has a navigation/parse failure while its lease is current, its structured failure is accepted; if its lease is lost/expired, its stale outcome is rejected and N follows normal recovery/redelivery; if cancellation is already durable, it is acknowledged without stale mutation. In every case, valid captures 1 through N-1 are handed off after the shared session is disposed and unstarted tasks are redelivered. | T2, T3 | Mid-envelope navigation failure, cancellation, one-stale-lease partial acceptance, and deadline tests verify fencing and that no valid successful capture is discarded or repeated. | Not started |

## Delivery plan

1. Complete and measure the separately approved [Playwright efficiency change](../20260915_playwright-collection-efficiency/README.md) first.
2. Freeze the versioned normalized envelope, receipt, error, and processing-stage contracts for `race-detail` only.
3. Add the durable API inbox/outbox, `Applying` handoff, bounded processor and idempotency gates behind a default-off pilot flag.
4. Add race-card/result application and heterogeneous referenced-request batching, then connect only a bounded `race-detail` canary.
5. Compare the same 12-race × 18-entry corpus and require identical persistence, no failure-rate increase, 80% fewer post-capture HTTP calls, and at least 10% improvement in either total p95 or Lambda GB-seconds. Database transaction improvement is reported but belongs to the separate application/runtime change.
6. If the pilot misses both total-cost gates, leave it off and require a new user decision. Passing permits a separate general-rollout approval, not automatic rollout.
7. Only after the pilot passes, consider the separately proposed [subject expansion record](../20260915_subject-profile-history-bulk-ingestion/README.md); pilot approval does not authorize it.
8. Observe one full collection window for each approved rollout and remove rollback paths only in separate verified cleanup checkpoints.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Freeze `race-detail` normalized payload, acquire-issued capture key, receipt, `Applying` handoff and structured outcomes. Covers AC1–AC3, AC7, AC10. | Main | Lead tier | Playwright efficiency AC1–AC9 Verified and final baseline frozen | `Contracts`, `ApiClient`, contract tests | Serialization/version/hash/state tests | Reviewed pilot contract | Dependent |
| T2 | Implement default-off API inbox/outbox, task handoff, bounded processor, replay checkpoints and cancellation. Covers AC2–AC7, AC10, AC11. | Main | Lead tier | T1 | `CollectionOperations` schema/store/migration and API tests | Restart/fault/replay/expiry/cancellation tests | Durable pilot persistence path | Dependent |
| T3 | Connect a bounded `race-detail` Collector pilot using one session, browser release and one ingestion call. Covers AC1, AC3, AC4, AC6, AC8, AC10, AC11. | Main | Lead tier | T1,T2 | Collector race-detail pilot and tests | Canary flag, partial capture, response loss and equivalence | Default-off pilot path | Dependent |
| T4 | Add only the race-card/result/reference ingestion adapter required by the pilot; generic aggregate/query optimization is excluded. Covers AC3–AC5, AC8, AC10. | Main | Lead tier | T1,T2 | Pilot-specific API/Application adapter and tests; no generic runtime optimization | HTTP count, item outcomes, replay and equivalence | Correct one-call pilot application | Dependent |
| T5 | Measure fixed-corpus and bounded-canary outcomes and decide stop/general-rollout proposal. Covers AC7–AC10. | Worker | Worker tier | T2–T4 | Pilot metrics/reports and this record | Baseline/candidate p50/p95, GB-seconds, HTTP/failure comparison | Evidence-backed go/no-go | Dependent |

T1–T5 are the only pilot tasks and remain `Dependent` until the Playwright record is fully Verified and its final baseline is frozen. The subject and application/runtime records own their files and gates independently; none becomes runnable through approval of this pilot.

## Review gates

- **Design and task-split review** — 2026-09-15, reviewer: Main plus independent R10/R12. Earlier reviews established the technical contracts but left one record with coupled browser, runtime, pilot and subject scopes. R10 rejected the first extraction because formal subject ACs, canonical scope, DB ownership and prerequisite states still overlapped. Follow-up limits this record to default-off `race-detail` pilot AC1–AC11; Playwright, application/runtime and subject expansion now have separate Proposed records, exclusive task scopes and explicit dependencies. R12 returned `PASS` for all four records and verified this record neither implements nor credits the other scopes. No production or AWS changes occurred. Measured agent usage/cost was unavailable; revision count is the efficiency proxy.
- **Pre-implementation review** — Pending approval. Classify tasks and record exact worker contracts before code changes.
- **Checkpoint review** — Pending implementation.
- **Final review** — Pending implementation and full evidence reconciliation.

## Documentation updates

- `docs/01-lambda-collector-architecture.md`: adds the canonical rule that browser capture precedes a durable normalized ingestion phase and that concurrency remains unchanged for this optimization.
- `docs/23-jra-scraping-redesign.md`: clarifies the normalized durable handoff boundary and points browser/subject concerns to their separate records.
- `docs/changes/20260915_playwright-collection-efficiency/README.md`: owns the higher-priority Playwright/Snapshot/navigation work.
- `docs/changes/20260915_collection-application-runtime-efficiency/README.md`: owns persistence, transport and runtime tuning.
- `docs/changes/20260915_subject-profile-history-bulk-ingestion/README.md`: owns Horse/Jockey/Trainer expansion after pilot success.
- Existing snapshot API/cutover and microbatch change records were inspected and remain historical records; they are linked rather than rewritten.

## Verification record

- 2026-09-15: Used CodeGraph before targeted repository searches to trace `PageSnapshot`, `JraPageReader`, JRA parsers, race-card workflows, write services, collection requests, store serialization, and Lambda/session boundaries.
- 2026-09-15: Confirmed all registered JRA page parsers consume semantic `PageSnapshot` without Playwright, while Navigator page transitions and history pagination still require Playwright.
- 2026-09-15: Confirmed the non-refresh 18-entry race-card path has 20–38 sequential write-service calls and up to 72 sequential referenced-resource request calls; existing result/race-card refresh paths already demonstrate a bulk contract.
- 2026-09-15: Confirmed the API collection store serializes writes per SQLite database and that parallel single-item requests would not remove transaction/query cost.
- 2026-09-15: The existing 70-race Horse semantic-snapshot fixture parsed at about 0.08–0.09 ms per warm in-process invocation on Windows 11, .NET SDK 10.0.202, Intel i7-12700. Five isolated test-process runs took 1.201–1.229 seconds and were dominated by test-host startup.
- 2026-09-15: One bounded live E2E for Horse `エンジャムメント` reached the 2026-09-06 Nakayama race result in 44.0 seconds test time / 45.374 seconds wall time. That diagnostic path reopens Horse search/profile before the result; production history discovery already derives direct race-result URLs and does not use that repeated-search helper.
- 2026-09-15: Confirmed one Horse history snapshot can contain at least 70 validated race descriptors, while one race-result snapshot contains the whole field. Cross-horse canonical Race deduplication can therefore reduce result browser navigations; cohort grouping alone cannot reduce Horse profile page navigations.
- 2026-09-15: Confirmed newly discovered Jockey/Trainer tasks usually lack reusable source URLs and the current per-person path restarts at the JRA top, can scan ten kana groups, and repeats active/retired discovery. Production persists their profiles but does not schedule their incidental parsed race rows.
- 2026-09-15: Confirmed the current browser/session/Navigator stack owns one mutable `IPage`; concurrent tab use would race it. A same-context page-scoped lease with page-local state is required before even sequential transient pages are safe.
- 2026-09-15: Confirmed typed browser operations can repeat the same generic `DOMContentLoaded`/`Load` settlement barrier and then extract normalized page text that JRA callers discard before taking a semantic snapshot. No request interception exists, and default snapshots include metadata, structured data, links, forms and images without page-kind projection metrics.
- 2026-09-15: Confirmed the existing result bulk endpoint still publishes Race/entry commands sequentially and can issue up to 54 Horse/Jockey/Trainer existence queries plus missing-subject commands for an 18-entry race. Several profile/race aggregate updates emit events without same-value guards, and large JSON read models may amplify projection writes.
- 2026-09-15: Read-only AWS evidence for the preceding seven days found 5,672 Lambda reports, 68 cold starts (1.20%), initialization p95 967.1 ms, maximum observed memory about 1.446 GB of 2 GB, and no OOM evidence. The 1.48 GB image and cold start are not the primary throughput target; phase attribution and page/API work reduction precede runtime tuning.
- 2026-09-15: Confirmed link extraction and click resolution still enumerate candidate elements through per-element Playwright calls despite comments describing batch evaluation. Static upper estimates for 100 anchors are roughly 701–1,301 repository-level calls for extraction and about 304 more for click re-resolution; the approved candidate boundary is one batch description, one mutation-safe unique revalidation returning the exact handle, and one normal action.
- 2026-09-15: Confirmed Horse fallback search rebuilds the top/search/page position and recaptures the selected profile after uniqueness checks, and race-card fallback can snapshot the unchanged current page twice. Confirmed ordered page parsers may rebuild `JraSnapshotView` up to six times for one result capture; one immutable view/typed result can be shared without changing the normalized durable boundary.
- No production source, test, configuration, schema, or AWS resource was changed before approval.

## Deviations and follow-up

- A later evidence-based change may persist full compressed semantic snapshots in private object storage and run a separate non-Playwright processor. It is intentionally outside this initial no-new-service scope.
- PostgreSQL/RDS migration and collector fleet parallelism remain separate decisions after the per-browser work reduction is measured.
