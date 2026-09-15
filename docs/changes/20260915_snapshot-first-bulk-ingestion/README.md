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

The cheapest first step is therefore to stop holding the browser while domain writes are performed, replace per-item HTTP traffic with one durable normalized ingestion request per compatible execution envelope, and union historical races from multiple horses before creating race work. This change deliberately does not increase Lambda or queue concurrency.

## Goals

- Complete all navigation and semantic snapshot capture before starting domain/API writes for a collection unit.
- Release the Playwright session before the normalized ingestion request is processed.
- Submit one normalized ingestion envelope containing the captured results for every compatible race task in the current execution envelope instead of per-race and per-entry write calls.
- Persist the accepted envelope and processing state before returning success to the Collector, then apply it on the existing API host without Playwright.
- Batch and deduplicate referenced Horse, Jockey, Trainer, and Owner collection requests in the same processing flow.
- Capture Horse profile/history pages before writes and submit one `horse-profile-history` payload containing the profile, pedigree references, and all validated historical-race descriptors.
- Union historical races across horses by canonical race identity and schedule each missing/stale race once, regardless of how many horses referenced it.
- Capture one race-result snapshot per unique required race and use it to populate every runner in that race.
- Resolve batches of Jockey or Trainer profiles from one directory traversal per subject type instead of scanning all kana groups for every person.
- Reuse verified subject locations or navigation descriptors and capture Jockey/Trainer profiles before one bulk ingestion request.
- Evaluate a same-context sequential transient-page pattern that preserves the parent directory/search page without enabling concurrent JRA navigation by default.
- Remove duplicate readiness barriers and discarded full-page text extraction from typed JRA navigation while preserving page-specific readiness.
- Measure and, only after semantic-equivalence gates pass, block browser resources that do not contribute to collected data.
- Apply each Race aggregate in bounded groups, prefetch referenced domain subjects as sets, and avoid appending events for semantically unchanged state.
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
- Changing EventStore technology, introducing asynchronous projection infrastructure, or making a whole race/all aggregates one atomic transaction.
- Enabling browser resource blocking, request compression, cross-invocation browser pooling, or a new Lambda memory size without the measurement gates in this record.

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

The initial production scope contains the existing unified `race-detail`, Horse `horse-profile`, Jockey `jockey-profile`, and Trainer `trainer-profile` definitions. The versioned contract is a discriminated union: race detail contains race-card data and, when collected by that same task, race-result data; horse profile/history contains profile fields, source identity, ordered history-page observations, pagination completeness, pedigree references, and validated canonical historical-race descriptors; Jockey and Trainer profile payloads contain identity evidence, profile fields, source/navigation descriptor, and location outcomes. Race-odds and Owner page handling do not move to this ingestion path. Jockey/Trainer race-history extraction is not introduced. The envelope is a normalized, compact DTO rather than the complete semantic DOM. It contains a contract version, stable capture key, payload SHA-256, task/attempt correlation, provider/resource identity, requested and final URLs, capture time, captured data, citations, and deduplicated referenced-resource requests.

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

The existing `result-bulk` HTTP endpoint is not treated as proof of bulk persistence: it currently publishes Race and entry commands sequentially and performs per-entry Horse/Jockey/Trainer existence checks. The ingestion processor instead groups ordered changes by aggregate, loads each Race aggregate once per application group, emits only required events, and commits that group once while retaining structured item outcomes. This preserves restartable stage/group boundaries rather than turning an entire race and all referenced aggregates into one all-or-nothing transaction.

Horse, Jockey, and Trainer identities referenced by an envelope are prefetched as sets before missing subjects are registered. Concurrent registration is resolved through the deterministic operation key and aggregate identity; it does not fall back to GET-before-POST/PUT loops. A capture with a new capture key but the same normalized domain values is a semantic no-op for profile and race state, except for explicitly historical observations such as a new citation or observation timestamp whose retention is part of the frozen contract.

Referenced resource requests are normalized and deduplicated before persistence. The store preloads definitions, revisions, suppressions, existing resources, matching batch requests, active tasks, and states for the complete key set; it then creates the required rows in one transaction with at most two `SaveChangesAsync` calls. A database uniqueness rule for the batch/idempotency identity protects concurrent replay instead of relying only on the process-local semaphore.

Client-side `Task.WhenAll` is not the primary optimization because it does not reduce request count and merely moves contention to SQLite. Bounded parallelism may be used only for independent remaining operations after the browser has been released and only when measurements show that the target endpoint is not serialized by the database gate.

### Horse-history and shared-race frontier

Horse tasks already grouped in one compatible envelope keep a single sequential JRA session. The current maximum remains 12 tasks. Within the group, tasks with a validated persisted source URL are captured first because they avoid public name search; birth year may be used only as a stable locality/tie-break ordering hint. It is not a correctness or deduplication key.

Each captured history page produces zero or more descriptors keyed by `(Provider, Definition=race-detail, yyyyMMdd:Course:RaceNumber)`. The API unions descriptors from every horse in the envelope and from concurrent/replayed envelopes, then performs one set-based state lookup. A race already `Current` at the required revision/freshness is skipped, an active matching task is reused, and only a missing/stale race creates work. Provenance from requesting horses is retained as a bounded many-to-one relation and is not part of task identity. Conflicting descriptors for the same canonical race are recorded per source; one invalid descriptor does not poison an independently valid descriptor.

Horse capture and race capture are separate bounded waves. A Horse task succeeds after its complete captured frontier, profile, pedigree references, and historical-race requests are durably accepted/applied; it does not wait for all downstream race results. Historical `race-detail` work remains grouped by the existing race-day compatibility and uses explicit validated result URLs. One race-result snapshot applies the full race field and may discover additional Horse tasks, but the operation-level visited key `(Resource, Definition, Revision)` and existing discovery-depth rules prevent cycles. No recursive horse→race→horse traversal occurs inside one browser lease.

Full history traversal continues until no next page exists. A partial traversal is marked `Partial`, never `Complete`, and follows the same partial-envelope/fencing rules. An incremental high-watermark with a one-page overlap could reduce repeat pagination materially, but it remains disabled in this change until local/live verification proves newest-to-oldest ordering, correction behavior, and a resumable or safely repeatable next-page boundary.

### Jockey and Trainer directory batching

Jockey and Trainer tasks currently restart at the JRA top page and can scan up to ten kana groups for every individual. The new envelope-level subject resolver processes only same-definition tasks together, traverses each required active directory group once, builds a bounded name-to-action index, and visits the retired directory once for unresolved names. Duplicate names still require the existing identity validation and may not be selected by name alone.

After a successful profile capture, the API persists the verified source URL and, when a stable GET URL is unavailable, a versioned navigation descriptor containing subject type, directory kind/group and bounded action/form identity. Reuse always validates the resulting subject. A stale descriptor is marked suspect and falls back to one batched directory rebuild; it is never treated as permanent identity without validation. Jockey/Trainer normalized snapshots are applied in one ingestion envelope with per-item outcomes instead of one profile HTTP request per person.

The common parser currently inspects tables named `年月日`/`レース名` for every subject type, but production schedules history only for Horse. This change does not infer Jockey or Trainer history from that incidental parser behavior. Controlled fixtures/live snapshots must first establish meaning, pagination and desired domain ownership; any Jockey/Trainer history feature requires a later approved design.

### Sequential transient pages and parallel-tab gate

`PlaywrightWebBrowser` currently owns one `IPage`, so existing browser and Navigator instances must never be used concurrently or switched through a global active-tab flag. The browser/session boundary will add a page-scoped lease that opens an independent `IPage` in the same `BrowserContext`, constructs page-local browser/reader/navigator state, and closes only that page in `finally`; the root lease continues to own the context and Chromium process.

The initial production policy permits at most one sequential transient child page. Its parent directory or Horse search/history page remains open while a verified same-origin GET link is captured in the child, after which the child closes. This may remove back-navigation, directory reconstruction and repeated pagination while preserving cookies and browser cache. POST/session-bound actions use the existing same-page path until a focused browser test proves that their exact action can be replayed safely in a child page. Unknown or cross-origin links are rejected.

Two concurrently navigating child pages are benchmark-only in this change. A later change may consider them only if snapshots remain identical, all child pages close, peak process memory stays below 1.8 GB in the 2 GB Lambda-equivalent limit, wall time improves by at least 25%, and 403/429/timeouts do not increase. Passing that benchmark never authorizes production rollout; a separate approved change is still required. More than two concurrent pages is outside scope.

### Browser readiness and resource policy

Typed JRA navigation uses one post-action readiness barrier and then performs a no-wait semantic capture. It does not perform the legacy normalized full-page text extraction when that return value is discarded, and semantic snapshot capture does not add another generic readiness wait. Readiness is defined per JRA page kind by the required identity marker and data root/table/form; delayed images, fonts, media, and trackers are not readiness conditions. Existing text-returning browser operations remain available for non-JRA agent/browser callers.

Link, clickable, section-action, form and visible-image discovery must not issue repository-level Playwright API calls per candidate element. One browser-side evaluation returns bounded candidate descriptors in document order plus a DOM-mutation generation; C# applies the existing URL/text/region/visibility scoring. A second evaluation requires the unchanged mutation generation and a unique match on the complete actionable fingerprint (resolved/raw href, onclick/form identity, normalized text, region and document-order discriminator), then returns the exact `ElementHandle`. Only that handle is operated through Playwright. The token/handle is session-local and never persisted; mutation, ambiguity, replacement or detach causes one batch re-resolution and then safe failure rather than clicking another element. Real click behavior is retained for JRA one-shot, JavaScript and session-bound links.

Before any resource interception is enabled, the benchmark records requests and transferred bytes by resource type and host. The first candidate may block images, media, and fonts while retaining documents, scripts, XHR/fetch, and CSS. Production enables only a policy whose normalized parser outputs and diagnostics match the control corpus and whose navigation failure/access-limit rate does not increase. Third-party hosts are blocked only from an observed allow/block inventory, never by an unreviewed wildcard.

Snapshot capture metrics separate browser evaluation, JSON transfer, deserialization, parser time, serialized bytes, and allocated bytes. Per-page snapshot projections may omit forms, images, metadata, or structured data only when fixture and bounded-live comparisons prove that the page-kind parser, citations, identity validation, and diagnostics are unchanged.

One semantic capture creates at most one `JraSnapshotView`; ordered page identification and the selected parser share that immutable projection instead of rebuilding tables, headings, links and actions for each `CanParse` and `Parse`. A typed subject parse similarly returns one immutable result containing profile, identity evidence, pedigree and historical-race descriptors for all downstream consumers. The normalized DTO—not raw browser JSON or the full semantic DOM—remains the durable API boundary.

Horse fallback search retains only each immutable parsed/normalized candidate result and bounded identity evidence while checking same-name/birth-date uniqueness, not every full semantic snapshot. At most 32 candidates and 256 KiB of retained normalized evidence are allowed per search; overflow produces a structured ambiguity result and never selects a partial set. Once one candidate is validated uniquely, that retained result is returned directly; the Navigator does not rebuild top/search/form/page position and recapture the winner. On race-card shortcut miss, the already captured current-page view is passed to the race-list fallback rather than captured again. Directory link lists may be cached only within the current page/mutation generation and are invalidated on every navigation, DOM mutation or same-page action.

### Runtime cost gates

Production evidence shows 68 cold starts among 5,672 invocations over seven days (1.20%); initialization p95 was 967.1 ms while collection duration averaged tens of seconds. The roughly 1.48 GB container image is therefore deployment and rare-scale-out hygiene, not an initial throughput target. The current 2,048 MB Lambda used at most about 1.446 GB in the observed period and has no OOM evidence.

Phase telemetry is added before runtime tuning. A bounded memory power test compares 1,536, 2,048, 2,560, and 3,072 MB by task type and selects the lowest GB-second configuration that meets the duration and memory gates; it never changes concurrency. Ephemeral storage remains 4,096 MB until `/tmp` high-water evidence supports a safe reduction. HTTP connection/retry tuning is deadline-aware and may retry a non-idempotent ingestion request only because its acquire-issued idempotency key makes replay safe. Request compression remains disabled unless normalized-envelope size, safety, and CPU measurements pass the explicit gates. Idempotency hashes canonical uncompressed contract bytes, so compressed and uncompressed representations of the same capture share one receipt.

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
| AC8 | For the fixed 12-race × 18-entry benchmark, all 18 Horse/Jockey/Trainer/Owner identities are distinct, so the legacy path executes the documented 38 application plus 72 referenced-request service calls per race. Post-capture application/reference HTTP calls and total EventStore plus CollectionPlatform write transactions each fall by at least 80%. The candidate uses at most one ingestion-acceptance transaction per 12-task envelope plus eight write transactions per race, including claims, domain groups, reference batch, checkpoints, and finalization. Collection-request prefetch uses at most eight set queries and two `SaveChangesAsync` calls per batch, independent of entry count. Browser-held time excludes ingestion/application and persisted data matches baseline. | T3, T4, T5 | Instrumented baseline/candidate report lists exact HTTP, query, transaction, stage and browser-lifetime counts plus p50/p95, and fails if either the 80% comparison or numeric candidate budget is exceeded. | Not started |
| AC9 | The initial deployment adds no AWS service and does not raise Lambda, SQS, or dispatcher concurrency. | T5 | Terraform/config diff and deployment checklist. | Not started |
| AC10 | Existing non-external solution tests, formatting verification, Release build, and the end-to-end capture→ingestion→persistence happy and restart paths pass. | T1–T5 | CI-equivalent commands and E2E evidence. | Not started |
| AC11 | If task N has a navigation/parse failure while its lease is current, its structured failure is accepted; if its lease is lost/expired, its stale outcome is rejected and N follows normal recovery/redelivery; if cancellation is already durable, it is acknowledged without stale mutation. In every case, valid captures 1 through N-1 are handed off after the shared session is disposed and unstarted tasks are redelivered. | T2, T3 | Mid-envelope navigation failure, cancellation, one-stale-lease partial acceptance, and deadline tests verify fencing and that no valid successful capture is discarded or repeated. | Not started |
| AC12 | Up to 12 compatible Horse tasks reuse one sequential JRA session, capture every history page before writes, dispose the session, and submit one `horse-profile-history` ingestion envelope instead of one profile plus H historical-race requests per horse. | T1, T3, T6 | Multi-horse workflow test asserts session count, pagination completeness, disposal ordering, one ingestion request, and zero legacy history-request calls. | Not started |
| AC13 | Historical-race descriptors from multiple horses are globally deduplicated by `(Provider, RaceId, race-detail, RequiredRevision)`. Eighteen horses referencing one race create or reuse exactly one active `race-detail` task and one eventual result snapshot. Provenance has a unique `(RaceResource, HorseResource)` relation, accepts at most 32 distinct requesters per race, rejects overflow rather than truncating, and merges the highest lane/priority and earliest due time. | T2, T4, T6 | Store/API integration test covers 18 distinct requesters, duplicate edges, 32/33 boundary, active, Current, stale, conflicting descriptor, replay, and simultaneous envelopes under the database uniqueness constraint. | Not started |
| AC14 | A complete race-result snapshot applies all runners and satisfies every requesting-horse edge without per-horse result navigation; horse and race work remain separate bounded waves with cycle prevention. | T2, T3, T6 | Capture→frontier→unique race-detail→full-field application E2E verifies one result navigation and all runner relationships. | Not started |
| AC15 | Local performance evidence records direct-URL and search-fallback Horse paths separately, history pages/races, browser-held time, snapshot parse time, unique-race reduction, and ingestion time. Warm parsing of the existing 70-race snapshot remains below 1 ms p95, and no performance claim treats test-host startup as parser time. | T5, T6 | Repeatable in-process parser benchmark plus bounded live/local workflow measurement with environment and limitations recorded. | Not started |
| AC16 | A batch of same-definition Jockey or Trainer tasks traverses each active kana group at most once, visits the retired directory at most once for unresolved names, validates every selected identity, and sends one normalized ingestion envelope after browser release. | T1, T2, T7 | Directory fixture tests for 1/10/100 names, duplicate names, active/retired fallback and exact navigation/snapshot/API counts. | Not started |
| AC17 | A verified Jockey/Trainer URL or versioned navigation descriptor is reused before directory discovery; stale or invalid reuse falls back to one batched discovery and cannot update the wrong subject. | T2, T7 | Direct-location, POST-descriptor, stale fallback, duplicate identity and replay tests. | Not started |
| AC18 | The browser abstraction can keep one parent page and use one same-context sequential child page with page-local Navigator/Reader state; cancellation, parse failure and navigation failure always close the child, preserve the parent, and never operate the same `IPage` concurrently. | T3, T7 | Local Chromium page-lease lifecycle, cookie/context sharing, state isolation, failure cleanup and no-active-page-switch tests. | Not started |
| AC19 | Parallel child-page navigation is unconditionally disabled in this change and requires a later approved rollout. The diagnostic benchmark runs current, sequential-child and parallel-two fixture strategies for five measured iterations after warm-up, samples the full Chromium process-tree peak RSS at least every 100 ms, checks exact snapshot equality and zero leaked pages, and reports wall time and failures without enabling production concurrency. POST/session-bound actions remain same-page unless separately proven safe. | T5, T7 | Production configuration/registration assertion proves maximum concurrent navigation is one; the five-iteration local report records environment, RSS, equality, page count, timings and limitations. | Not started |
| AC20 | Rolling upgrades preserve version-1 definition-scoped envelopes by deterministic redelivery, while current/version-2 compatible mixed `RaceDay` envelopes (`RaceCard`, `RaceResult`, `Race`, and `race-detail`) and mixed `WeekendSubjects` Horse-definition envelopes are routed item-by-item into the correct discriminated payload. Neither path loses or repeats an accepted capture. | T1, T3, T6 | Contract and workflow tests separately cover version-1 definition envelopes in both old/new deployment orders and every supported version-2 mixed-definition pair, including partial acceptance, response loss, and replay; valid captures are applied exactly once. | Not started |
| AC21 | Typed JRA navigation performs at most one post-action readiness barrier, zero discarded legacy full-page text extractions, and one semantic capture for each parsed terminal page. Page-kind readiness waits for required data but not delayed images/fonts/trackers; normalized outputs and failure classification remain identical. | T3, T7, T8 | Instrumented browser fixture tests count barriers/text reads/captures; delayed-resource and delayed-data cases plus representative local runs compare exact normalized outputs, retry/failure rate, p50 and p95. | Not started |
| AC22 | Browser request inventory records request count and transferred bytes by type/host. Resource blocking remains disabled unless a control/candidate corpus covering calendar, directory, card, result, odds, Horse, Jockey and Trainer pages, with at least 10 warm fixture iterations per page kind/candidate and three bounded-live repetitions for each enabled policy, has exact normalized output/diagnostic equality, zero added identity/parse/access-limit failures, and at least 15% lower browser-held p50 and p95. | T5, T8 | Repeatable control/interception report with request waterfall, bytes, outputs, failures and production-registration assertion; rejected policies remain off. | Not started |
| AC23 | Snapshot telemetry separately records browser evaluation, JSON transfer/deserialization, parsing, serialized bytes and allocations. A page-kind projection is adopted only when at least 10 warm fixture iterations per page kind/candidate plus three bounded-live repetitions show outputs, citations, identity evidence and diagnostics equal the full snapshot and capture-stage p95 improves by at least 10%; otherwise full safe-pruning capture remains. | T5, T8 | Fixture and bounded-live projection matrix with exact field/diagnostic comparison and stage/size/allocation measurements. | Not started |
| AC24 | For an 18-entry result, all items are validated before mutation; rejected items receive structured outcomes, while the valid ordered event set is applied by loading the Race aggregate at most once and committing once per documented stage/group. A group commit failure commits/checkpoints none of that valid set and retries it under the same operation key; a post-commit/pre-checkpoint failure replays as a no-op. Horse/Jockey/Trainer existence prefetch uses at most three set queries per envelope independent of entry count. | T2, T4, T5 | Invalid-item plus pre/during/post-commit fault tests verify structured rejection, exact valid final state, atomic stage/group event commit, checkpoint ordering and replay; instrumentation reports aggregate loads, transactions, events, subject queries and per-projection updates. | Not started |
| AC25 | Reapplying semantically identical normalized race, Horse, Jockey or Trainer values under a different capture key appends zero new state-change domain events and performs zero state projection updates; explicitly retained observation/citation history is tested separately. Idempotent subject upsert removes Collector GET-before-write and remains correct under concurrent replay. | T1, T2, T4, T6, T7 | Two-capture and concurrent-upsert integration tests assert event/projection deltas, operation results, citations and final state through real persistence. | Not started |
| AC26 | Phase telemetry attributes at least 95% of successful attempt wall time and records task kind, phase, retries, request/response bytes, cold/warm state, Chromium process-tree RSS and `/tmp` high-water without secrets. Runtime tuning uses at least 30 runs per task type/configuration; any selected memory setting minimizes GB-seconds with p95 regression no greater than 5%, zero timeouts and peak memory below 85%. Request compression is enabled only above a measured 64 KiB threshold when it reduces transferred bytes by at least 50% with less than 5% CPU/latency regression. The API accepts only `gzip` or identity, permits at most 1 MiB compressed and 8 MiB decompressed data with a 2-second decompression budget, rejects corrupt/truncated/unsupported bodies structurally, and hashes canonical uncompressed bytes; compressed/uncompressed retry of one capture returns one receipt. | T1, T2, T5, T8 | Metrics coverage test, fixed-corpus power-tuning report, payload histogram/compression benchmark, encoding/size/time/corruption contract tests, config diff and secret-field review. | Not started |
| AC27 | Transport retry is limited to connection failure and HTTP 502/503/504, at most two retries with exponential jitter, honors `Retry-After` only when the next attempt can start and finish before a 30-second Collector deadline reserve, recreates/disposes each request body, and stops immediately on cancellation. The 30-second reserve is the production default and cannot be configured below 15 seconds. Validation, unsupported encoding/version, oversized body, hash conflict and other permanent 4xx responses are never retried. Response loss after acceptance reuses the capture key and resolves to the existing receipt. | T1, T2, T3, T8 | Deterministic handler/clock boundary tests cover each status/error, attempt/backoff bounds, 15/30-second reserve configuration, deadline exhaustion, cancellation, body lifetime, non-retry cases, and response-loss replay through the real ingestion boundary. | Not started |
| AC28 | Candidate discovery is constant in repository-level Playwright API invocations: `GetLinksAsync`, `ClickLinkAsync`, generic click and section-action selection each use at most one batch candidate evaluation and one mutation-generation/unique-fingerprint revalidation independent of 10/100/500 candidate elements, followed by at most one `ElementHandle` action. URL/title/region/order/deduplication, exact-prefix-contains scoring, hidden/image-alt behavior and pseudo-action filtering equal the legacy result. Mutation/reorder/replacement, duplicate fingerprints or detach cause one re-resolution then safe failure and never click another element; Playwright action internals are timed but are not misreported as this repository-level invocation count. | T7, T8 | Five warm iterations after warm-up for 10/100/500-element fixtures record repository-level calls, payload bytes and wall time; first/middle/last, duplicate, hidden, onclick/href, mutation-generation change, reorder/replace with an indistinguishable neighbor, detach and cancellation cases prove equality and at least 80% wall-time reduction at 100 links. | Not started |
| AC29 | Horse fallback discovery opens the search flow once, captures/parses each candidate at most once, and retains only the immutable normalized result plus identity evidence—not the full semantic snapshot—without rebuilding the search. Retention is capped at 32 candidates and 256 KiB; overflow returns structured ambiguity without partial selection. A winner on zero-based page `q` avoids the second top/search/submit, `q` replay clicks and winner recapture while preserving identity and source evidence. Race-card shortcut fallback snapshots the unchanged current page once, and directory link caches are bound to and invalidated by page/mutation generation. | T6, T7, T8 | Navigation-trace fixtures vary 32/33 candidates, 256 KiB boundary, candidate page/history pages, uniqueness failure, async mutation and generation; assertions compare exact DTO/source identity and Navigate/click/back/capture/GetLinks counts with legacy. | Not started |
| AC30 | Each captured JRA page creates at most one `JraSnapshotView` shared by identification and parsing, and each subject capture creates one immutable parsed result shared by profile, pedigree and history-frontier consumers. A result page no longer performs up to six repeated view projections; parser selection/order, fields, citations and diagnostics remain identical, and raw browser JSON/full semantic DOM is not sent as the durable ingestion payload. | T1, T3, T6, T7, T8 | Instrumented parser suite and 70-history/result fixtures assert one capture, one view projection, one typed parse and exact outputs; allocation/time benchmark records default and shared-view paths. | Not started |

## Delivery plan

1. Freeze the versioned normalized envelope, receipt, error, and processing-stage contracts.
2. Add the durable API inbox/outbox, `Applying` ownership handoff, and restart-safe bounded processor with idempotency gates.
3. Add bulk race-card application and heterogeneous referenced-request batch persistence.
4. Add `horse-profile-history` capture and the global state-aware Race frontier before changing the Collector entry path.
5. Add batched Jockey/Trainer directory resolution, verified navigation-descriptor reuse, and normalized profile ingestion.
6. Add the page-scoped sequential transient-page abstraction and use it only where it demonstrably avoids parent reconstruction.
7. Restructure the Collector into envelope capture/parse and post-browser ingestion phases; retain the old path behind a temporary rollback flag.
8. Remove redundant readiness/text extraction, benchmark page-specific readiness, resource interception and snapshot projections, and enable only candidates meeting AC21–AC23.
9. Add EventStore aggregate/query/event/projection counters, semantic no-op/upsert coverage, operational phase timings, and representative race, Horse and subject-directory benchmarks.
10. Run the runtime cost gates, deploy with existing concurrency limits and parallel tabs disabled, observe one full collection window, then remove the rollback path only in a separately verified cleanup checkpoint.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Define the discriminated race-detail, Horse profile/history, Jockey profile and Trainer profile snapshots, acquire-issued capture key, receipt, `Applying` handoff, navigation descriptor, structured outcome contracts, mixed-definition dispatch contract, semantic no-op/history boundary, one-parse normalized subject result, and encoding/retry errors. Covers AC1–AC3, AC7, AC10, AC12, AC16, AC20, AC25–AC27, AC30. | Main | Lead tier | - | `Contracts`, `ApiClient`, contract tests | Contract serialization/version/hash/state/navigation-descriptor, encoding/retry, normalized subject result, no-op/history, and mixed-definition matrix tests | Reviewed frozen contract and passing tests | Proposed |
| T2 | Implement API inbox/outbox schema, atomic task ownership handoff, bounded finite processor cycle, replay checkpoints, cancellation, structured outcomes, API-owned request decoding/size/time limits, and operation-keyed subject upsert. Covers AC2–AC7, AC10, AC11, AC24–AC27. | Main | Lead tier | T1 | `CollectionOperations` schema/store/migration and API ingestion services/decoding/tests | Restart, cross-database ambiguity-window fault injection, replay, expiry, cancellation, encoding limits, subject-upsert and concurrency tests | Durable/replayable real persistence path with frozen ingestion decode contract | Proposed |
| T3 | Refactor the envelope-level Collector/session workflow to capture all race-detail tasks with one session, dispose once, and submit one envelope without polling. Covers AC1, AC3, AC4, AC6, AC8, AC10, AC11, AC20, AC21, AC27, AC30. | Main | Lead tier | T1, T2 | Collector collection-platform/session execution, race-detail workflow, focused tests | Microbatch ordering, partial capture, response-loss/retry, equivalence, heartbeat, cancellation, readiness/view counts, and mixed `RaceDay` definition tests | One-ingestion-call production path retaining session reuse | Proposed |
| T4 | Implement aggregate-grouped race-card/result and referenced-request bulk application, domain-subject set prefetch, semantic no-op/upsert, and database uniqueness. Covers AC3–AC5, AC8, AC10, AC24, AC25. | Main | Lead tier | T1, T2 | API/Application/Domain/CollectionOperations bulk paths, schema/index, tests | Query/transaction/aggregate/event/projection counts, concurrent replay/upsert, domain equivalence and partial outcomes | Bounded-query idempotent bulk path without same-state event growth | Proposed |
| T5 | Add phase, browser-resource, payload, EventStore and runtime metrics; run the fixed race/Horse/subject/runtime benchmarks; then update operational documentation. Covers AC7–AC10, AC15, AC22–AC24, AC26. | Worker | Worker tier | T2–T4,T6–T8 | Collector/API metrics, benchmark tools/tests, infrastructure measurements, `docs/01-lambda-collector-architecture.md`, `docs/23-jra-scraping-redesign.md`, this record | Benchmarks, telemetry coverage, power/payload reports, CI-equivalent suite, config diff, CodeGraph sync/queries | Measured AC evidence for Main final review | Proposed |
| T6 | Implement Horse envelope capture, pagination completeness, retained-candidate reuse, one-parse profile/pedigree application, cross-horse Race frontier union, provenance and cycle guards. Covers AC12–AC15, AC20, AC29, AC30. | Main | Lead tier | T1,T2,T4 | Horse Collector/Scraping workflow, CollectionOperations frontier/provenance, API application, focused tests | Multi-horse partial/replay/state-dedupe, navigation trace, mixed `WeekendSubjects` Horse-definition matrix, and unique-race E2E | One history ingestion per envelope and one result capture per unique required race | Proposed |
| T7 | Implement page-scoped leases, batched Jockey/Trainer directory resolution, sequential transient-page policy, descriptor reuse, page-generation cache/action tokens and profile ingestion. Covers AC16–AC19, AC21, AC25, AC28–AC30. | Main | Lead tier | T1,T2,T3 | Browser/session abstraction, JRA subject navigation, Collector subject handler, API application, focused tests | Directory-once, page-isolation/generation, RPC bounds, cleanup, fallback, no-op upsert and benchmark gates | One directory traversal per kind/group and one ingestion per subject envelope | Proposed |
| T8 | Implement and gate typed no-text navigation, batch candidate resolution, single/page-kind readiness, shared view projection, browser request inventory/interception candidates, snapshot projection candidates, Collector-owned request compression, deadline-aware HTTP policy, and runtime experiment configuration against T2's frozen decode contract. Covers AC21–AC23, AC26–AC30. | Main | Lead tier | T1,T2,T3,T7 | Scraping browser/navigation/snapshot infrastructure and Collector HTTP/runtime configuration; no API decoder writes and no concurrent navigation | Barrier/RPC/resource/projection equality, navigation trace, Collector encoding, transport retry/idempotency tests and fixed-corpus runtime reports | Only measured candidates enabled; rejected candidates remain explicitly disabled | Proposed |

Tasks that share contracts, schema, migrations, browser/session abstractions, or generated snapshots remain serialized under Main ownership. T3 and T6 start only after T1 is frozen and T2 exposes a reviewed fake/test boundary; T6 also depends on T4's set-based request contract. T7 starts after T3 freezes the page/session ownership contract; T8 starts after T7 freezes page ownership and edits the shared browser stack serially. T5 instrumentation may start after measured interfaces are frozen, but its final reports depend on all implementation tasks.

## Review gates

- **Design and task-split review** — 2026-09-15, reviewer: Main. Inputs: production AWS evidence, CodeGraph paths, semantic snapshot design records, current workflow/store/API code, delegated D1/D2/H1/H2/JT1/TAB1, A1–A3 and B1–B3 research, and independent D3/R2–R9 reviews. The original race-only design passed after two revision cycles covering completion ownership, response loss, envelope session reuse, cross-database idempotency, numeric budgets, partial capture and fencing. User then expanded scope to Horse history, cross-horse Race deduplication, Jockey/Trainer profiles, transient tabs, cost/latency opportunities, and Snapshot/Playwright internals. R2–R8 successively found stale scope and provenance bounds, parallel-rollout ambiguity, v1/v2 compatibility, compression/deadline safety, aggregate partial-success boundaries, task ownership overlap, an unmeasurable protocol bound, and unsafe DOM-index reuse. Follow-up aligns all subject/race definitions, freezes Race-frontier uniqueness and the 32-requester policy, unconditionally excludes production parallel navigation, separately verifies v1 definition-scoped and current/v2 mixed routing, and adds AC21–AC30 for browser waits/resources/snapshot projection, aggregate-grouped persistence, semantic no-op, telemetry/runtime cost, encoding/retry safety, constant repository-level candidate discovery, retained Horse results, and one-view parsing. R9 independently returned `PASS`. D1/D2/D3/H1/H2/JT1/TAB1, A1–A3, B1–B3, R4, R7, and R9 are `Verified` and made no tracked file changes. Rework: ten design-revision cycles, no implementation rework or escalation. Measured agent usage/cost was unavailable; elapsed investigation and revision count are the efficiency proxy.
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
