# Horse history collection-request batching

- Status: Proposed
- Owner: HorseRacingPrediction team
- Created: 2026-09-16
- Updated: 2026-09-16

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | User approval of AC1–AC5 is required before production changes. |
| Verification | Not started | Focused Collector/API integration and regression gates remain. |
| Deployment/operation | Not started | No AWS, database schema, queue, or browser-concurrency change is planned. |

## Context

Horse profile collection currently traverses each history page and sends one collection-request HTTP call for every valid race row. The checked-in parser fixture contains 70 valid races, so one profile can hold the shared Playwright session through 70 sequential API calls.

Cross-horse Race execution is already globally deduplicated. Every valid row is normalized to `Race/JRA/yyyyMMdd:Course:Number`, and Collection Platform ordinary `Discovery` registration reuses an existing task with the same resource, definition and revision even after completion. Existing tests also prove concurrent registration creates one task. Therefore another preflight state lookup or age-cohort rule would add calls and races without reducing Playwright work.

The existing collection-request batch API supports 1–500 items, stable item identity, per-item outcomes, one database transaction and reuse of the same ordinary-registration invariant. This change uses it at the Horse history page boundary.

## Goals

- Replace per-race Horse-history request POSTs with one request per history page, chunked only when a page exceeds 500 valid unique races.
- Deduplicate rows by canonical Race identity before transport.
- Preserve discovery lane, priority, effective date, explicit result URL, requester metadata and pagination failure behavior.
- Validate every batch outcome and keep replay idempotent.

## Non-goals

- Claiming fewer Horse profile/history or Race-result Playwright navigations; those are unchanged.
- Snapshot-first, subject envelopes, global multi-Horse frontier/provenance, new queues, concurrency, or tabs.
- Using horse age/cohort as a correctness or deduplication key.
- Incremental history cutoff, Jockey/Trainer directory batching, or Jockey/Trainer history persistence.
- Adding a preflight collection-state query.

## Technical impact

`DiscoverHorseRaceHistoryAsync` will normalize all valid rows on the current page, deduplicate by canonical Race resource ID, construct existing `CollectionRequestBulkItem` values, and call `RequestManyAsync` once for each deterministic chunk of at most 500. It sends a page before navigating to the next page, preserving the current behavior where discoveries from earlier pages survive a later navigation failure.

Batch and item keys are derived from the current Horse task ID, zero-based history-page index, chunk index, and canonical Race ID using collision-free bounded encoding. Replay of the same task/page/chunk therefore returns the prior binding; a changed payload under the same item key is rejected. The Collector requires exactly one outcome for every submitted item and accepts only `Created` or `Reused` with request/task identities.

## Decisions

- Use canonical Race identity, never cohort membership, for deduplication.
- Preserve page-granular partial progress rather than buffering all pages until the end.
- Reuse the existing batch endpoint/store; do not add a new API or schema.
- Measure HTTP calls and collection-store transactions only. Existing global task reuse is verified but not claimed as a new browser optimization.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | A page containing 70 valid unique Horse-history races sends exactly one `RequestManyAsync` call with 70 items and zero single-item calls. A page above 500 is split deterministically into chunks of at most 500. | T1 | 70- and 501-row handler tests with exact call/item counts. | Not started |
| AC2 | Duplicate valid rows within a page produce one item per canonical Race ID. Lane, priority, revision, effective date, explicit URL, `requestedByHorseId`, `requestedByHorseName`, and optional `weekendPriorityUntil` equal the current single-request behavior. Malformed and excluded rows remain skipped. | T1 | Field-equivalence, duplicate, malformed and exclusion tests. | Not started |
| AC3 | Each page is durably requested before navigation to the next page. If page N navigation fails, requests for pages 1..N-1 remain submitted and no request is emitted for an unvisited page. Empty pages emit no request; stalled pagination remains a failure. | T1 | Two-page success/failure, empty and repeated-page tests. | Not started |
| AC4 | Replaying the same Horse task/page/chunk is idempotent. Missing, duplicate, unknown, rejected, or identity-less outcomes fail the collection attempt. Two Horses discovering the same Race, including after completion and concurrently, still create at most one Race task/outbox. | T1,T2 | Client outcome matrix and real-store/API replay/concurrency tests. | Not started |
| AC5 | Exact formatting, zero-warning Release build, Collector/API focused suites and full non-external regression tests pass. No AWS, schema, queue, API contract, domain-state, or browser-navigation/concurrency change is present. | T1,T2 | CI-equivalent commands, CodeGraph sync and final diff review. | Not started |

## Delivery plan

1. Freeze deterministic page/chunk/item identity and outcome validation.
2. Replace the per-row Horse history request loop with page-level batches.
3. Prove global Race task reuse through the real Collection Platform store/API boundary.
4. Run full verification and record measured request-count reduction without claiming browser reduction.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Implement Horse history page batching and focused behavior tests. Covers AC1–AC5. | Main | Lead tier | Approval | Horse subject handler and Collector tests | Request counts, fields, pagination, outcomes, replay | One batch per page/chunk with current semantics | Proposed |
| T2 | Add real-store cross-Horse dedupe/replay evidence and complete regressions. Covers AC4, AC5. | Main | Lead tier | T1 | API/Collection Platform integration tests and record | Store/API concurrency and CI-equivalent gates | One global Race task and clean final audit | Dependent |

Tasks are serialized because T2 verifies the exact contract emitted by T1. Read-only inventory was delegated; production writes remain under Main.

## Review gates

- **Design and task-split review** — 2026-09-16, reviewer: Main with read-only workers `/root/horse_history_dedupe_inventory` and `/root/subject_next_candidate_review`. Both inventories confirmed global cross-Horse Race task deduplication already exists and rejected a redundant state preflight. The lower-cost remaining fan-out is per-row HTTP. The design preserves page-level partial progress, uses the existing 500-item batch boundary, separates T1/T2 writes, and maps every AC to focused or integration evidence. Worker usage/cost telemetry is unavailable; both reports were accepted without rework.
- **Pre-implementation review** — Pending approval.
- **Checkpoint review** — Pending implementation.
- **Final review** — Pending implementation and evidence reconciliation.

## Documentation updates

- Existing canonical architecture and scraping documents were inspected. No update is required because task identity, browser navigation, persistence, queues and deployment topology remain unchanged.
- `docs/changes/20260915_subject-profile-history-bulk-ingestion/README.md` remains the authority for future multi-Horse frontier and subject-envelope work; this narrow transport change does not satisfy its ACs.

## Verification record

- 2026-09-16: CodeGraph traced Horse history parsing through `DiscoverHorseRaceHistoryAsync`, the request client, batch endpoint, `RequestManyAsync`, and `RequestCoreAsync`.
- 2026-09-16: Confirmed the 70-race parser fixture and the batch endpoint/store limit of 500 items.
- 2026-09-16: Confirmed ordinary `Discovery` reuse is keyed by normalized resource, definition and revision; completed and eight-way concurrent registrations are already covered by store tests.
- No production source, test, configuration, schema, AWS resource, or external data was changed before approval.

## Deviations and follow-up

- Jockey/Trainer directory batching may offer larger browser-navigation savings but requires a separately approved identity/navigation contract and evidence corpus.
- Incremental Horse history cutoff remains deferred until correction, ordering and overlap semantics are proven.
