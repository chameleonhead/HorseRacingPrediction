# Subject profile and history bulk ingestion

- Status: Proposed
- Owner: HorseRacingPrediction team
- Created: 2026-09-15
- Updated: 2026-09-15

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | Requires successful race-detail pilot evidence and separate approval. |
| Verification | Not started | Horse/Jockey/Trainer integration and performance evidence remain. |
| Deployment/operation | Not started | No subject workflow has changed. |

## Context

This follow-up is extracted from [Snapshot-first collection and bulk ingestion](../20260915_snapshot-first-bulk-ingestion/README.md) so approval of the `race-detail` pilot cannot authorize subject expansion. It applies only after the pilot passes its equivalence, failure, HTTP and total-cost gates. Browser mechanics are supplied by the independently approved [Playwright efficiency change](../20260915_playwright-collection-efficiency/README.md).

## Goals

- Capture up to 12 Horse profile/history tasks in one sequential session and submit one normalized envelope.
- Union historical races across horses by canonical Race identity and collect each required result once.
- Resolve Jockey or Trainer batches with one directory traversal per type/group and reuse verified navigation descriptors.
- Preserve bounded provenance, cycle prevention, identity validation and rolling-upgrade compatibility.

## Non-goals

- Jockey/Trainer race-history persistence, cohort-based correctness, unproven history high-watermarks, Owner/odds migration or parallel pages.
- Approval through the Snapshot-first pilot; this record always requires separate explicit approval.

## Design

Horse tasks keep the existing maximum of 12 per compatible envelope. Validated direct locations are preferred; birth year is only an ordering hint. Every history page is captured before ingestion and produces descriptors keyed by `(Provider, race-detail, yyyyMMdd:Course:RaceNumber)`. Global set lookup skips Current work, reuses active work and creates one missing/stale task. Provenance is a unique bounded Race-to-Horse relation, not task identity. Horse and Race are separate waves with visited `(Resource, Definition, Revision)` cycle guards.

Jockey/Trainer tasks traverse each active kana directory group once per same-definition batch and the retired directory once for unresolved names. Stable URLs or versioned navigation descriptors are reused only after subject validation; stale descriptors fall back to one batched rebuild. Trainer POST/session semantics are not represented as guessed terminal URLs.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Up to 12 Horse tasks use one sequential session, capture every history page, release the browser and send one profile/history envelope with zero legacy per-race request calls. | S1 | Multi-Horse workflow and pagination tests. | Not started |
| AC2 | Historical races deduplicate globally by provider/canonical Race/revision. One race has one active task/result capture; provenance is unique, capped at 32 requesters, rejects overflow, and merges highest priority/earliest due. | S2 | 18-requester, 32/33, replay/concurrency/state integration matrix. | Not started |
| AC3 | One complete Race-result capture populates all runners/requesters. Horse/Race waves remain separate, and operation visited keys prevent recursive cycles. | S2 | Horse→frontier→unique Race→full-field E2E. | Not started |
| AC4 | Direct and search-fallback Horse paths are measured separately; history pages/races, unique-race reduction, browser time and ingestion time are reported. The existing 70-race warm parse remains below 1 ms p95. | S1, S4 | Repeatable parser and bounded workflow benchmark. | Not started |
| AC5 | A same-definition Jockey or Trainer batch traverses each active kana group at most once and retired directory at most once, validates every identity and sends one envelope after browser release. | S3 | 1/10/100-name directory fixtures with duplicates/fallback. | Not started |
| AC6 | Verified URLs/descriptors are reused before discovery; stale/invalid values fall back once and cannot update the wrong subject. Jockey/Trainer history is not created. | S3 | Direct, POST descriptor, stale, duplicate and replay tests. | Not started |
| AC7 | v1 definition-scoped envelopes redeliver safely; current/v2 mixed WeekendSubjects and compatible subject envelopes route item-by-item without lost or duplicate accepted captures. | S1–S3 | Old/new deployment order and supported mixed-pair matrix. | Not started |
| AC8 | Existing tests, formatting, Release build and Horse/Jockey/Trainer happy/restart paths pass; no subject rollout starts unless the prerequisite pilot remains within its approved cost/failure gates. | S1–S4 | CI-equivalent suite, prerequisite evidence link and rollout checklist. | Not started |

## Delivery plan

1. Verify the race-detail pilot record is approved, implemented and has passed its go/no-go evidence.
2. Freeze subject payloads and mixed-envelope compatibility.
3. Implement Horse envelope capture and Race frontier first; verify before Jockey/Trainer work.
4. Implement Jockey and Trainer directory/profile batching separately.
5. Benchmark and deploy each subject type behind independent rollback controls.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| S1 | Define/implement Horse profile/history envelope capture and apply. Covers AC1, AC4, AC7, AC8. | Main | Lead tier | Playwright AC1–AC9 Verified with final baseline frozen; Snapshot-first pilot AC1–AC11 Verified with go decision | Subject contracts, Horse Collector/API and tests | Pagination/replay/equivalence | One Horse envelope path | Dependent |
| S2 | Implement global Race frontier/provenance/cycle guard. Covers AC2, AC3, AC7, AC8. | Main | Lead tier | S1 | CollectionOperations/API and tests | Concurrency/state/E2E | One task per required Race | Dependent |
| S3 | Implement Jockey/Trainer directory/profile batching and descriptors. Covers AC5–AC8. | Main | Lead tier | Playwright AC1–AC9 Verified with final baseline frozen; Snapshot-first pilot AC1–AC11 Verified with go decision | Subject navigation/Collector/API and tests | Directory/identity/replay tests | One traversal/envelope per subject kind | Dependent |
| S4 | Measure and review subject rollout. Covers AC4, AC8. | Worker | Worker tier | S1–S3 | Metrics/reports/docs | Benchmark and CI evidence | Independent go/no-go per subject kind | Dependent |

All tasks remain `Dependent` until prerequisite records are verified and this record is explicitly approved. Horse and Jockey/Trainer rollouts may be split into narrower approval records if their evidence diverges.

## Review gates

- **Design and task-split review** — 2026-09-15, independent R10/R12. R10 required formal removal from the pilot, canonical-link correction and exact prerequisites. This record now exclusively owns subject expansion; every task is `Dependent` on separate approval and applicable prerequisite evidence. R12 returned `PASS`.
- **Pre-implementation review** — Blocked on prerequisite evidence and explicit approval.
- **Checkpoint review** — Pending implementation.
- **Final review** — Pending full evidence reconciliation.

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: identifies this as the separate authority for subject expansion.
- `docs/01-lambda-collector-architecture.md`: keeps race-detail as initial pilot scope.

## Verification record

- 2026-09-15: Existing 70-race fixture parsed in about 0.08–0.09 ms warm; one bounded live diagnostic took about 44 seconds and was navigation dominated.
- 2026-09-15: Read-only inspection confirmed shared Horse races, repeated Jockey/Trainer directory traversal and lack of Jockey/Trainer history persistence.
- No production code, database or AWS resource was changed.

## Deviations and follow-up

- Incremental history cutoff and Jockey/Trainer history require later evidence and approval.
