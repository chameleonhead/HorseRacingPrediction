# Collector cost and navigation reduction

- Status: Approved
- Change record schema: 2
- Owner: HorseRacingPrediction team
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | API-authoritative job creation, obsolete-job retirement, direct URLs with fallback, 404 classification, and log filters are implemented. |
| Verification | Verified | Focused tests and Release build pass. Final gates are recorded below. |
| Deployment/operation | Externally blocked | Deployment, cleanup execution, and 48-hour production observation require a separate production operation. |

## Context and accepted design

CloudWatch showed 4,612 of 6,213 tasks scraping JRA before profile persistence failed with `SubjectProjectionNotReady`. The fixed one-minute value was the Collector result returned for this 404. Code tracing established that this was not a legitimate wait: the race bulk API already persisted authoritative subject IDs, while the Collector independently recomputed IDs and created profile jobs afterward.

The accepted correction is:

1. After a successful race-card bulk write, the API creates Horse/Jockey/Trainer/Owner profile jobs from the exact IDs in persisted `EntryDetails`. A stable race/subject fingerprint makes replay idempotent.
2. The Collector no longer creates race-derived profile jobs. A profile-save 404 is isolated permanent `SubjectResourceMissing`, not normal projection lag, so the former one-minute retry is removed.
3. A dry-run-first maintenance endpoint selects only active, race-derived historical jobs with `SubjectProjectionNotReady` whose resource is absent or mismatched to the stated race. Execute mode cancels waiting work, requests cancellation for running work, clears active/outbox state, supersedes failure notifications, and retains request/task/attempt history.
4. Race parsing carries allowlisted HTTPS JRA Horse/Jockey/Trainer profile URLs. A URL is only a fast path: terminal type/name is validated, and any failure, stale structure, or identity mismatch falls back to the existing official JRA top/search/directory flow.
5. Production HTTP and Playwright detail categories move to Warning. Development remains Debug. Lambda memory stays at 2,048 MB.

Job registration follows the successful race write rather than sharing its domain transaction. Registration errors are returned in the bulk response, and replay safely retries them. This avoids jobs for failed writes without introducing a distributed transaction.

The pre-existing `PlaywrightWebBrowser.cs` edit was reviewed after the main implementation: browser creation does not navigate to `SearchBaseUrl`, so the misleading URL field was removed and the lifecycle message was correctly lowered from Information to Debug. It is included in a separate reviewed commit. `.codex/worktrees/` remains unrelated user-owned state.

## Goals and non-goals

Goals are authoritative job identity, audited retirement of obsolete jobs, safe direct navigation with fallback, and reduced logging cost. Non-goals are guessing kana from kanji, deleting history, removing identity validation, automatically operating production, changing concurrency, or changing Lambda memory.

## Concern and agreement ledger

| ID | Concern | Resolution | State |
| --- | --- | --- | --- |
| C1 | Kanji does not safely identify a kana group. | Use source URLs and retain complete official fallback. | Resolved in design |
| C2 | Preflight adds a request and still races the write. | Fix ownership at the API boundary; no preflight/backoff. | Resolved in design |
| C3 | Direct URLs may change or identify the wrong subject. | Host/path filtering, terminal identity validation, and official fallback. | Resolved in design |
| C4 | Cleanup could hide evidence or cancel valid work. | Exact selection, dry-run default, retained history, explicit execute. | Resolved in design |
| C5 | Broad logging filters could hide outcomes. | Filter exact framework/browser categories only. | Resolved in design |
| C6 | Memory approaches the allocation. | Keep 2,048 MB and measure before separate tuning. | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Verification | State |
| --- | --- | --- | --- |
| AC1 | A successful race-card bulk write creates jobs with exact persisted IDs; replay creates no duplicate task IDs. | API integration test. | Verified |
| AC2 | Collector neither recomputes IDs nor requests race-derived profile jobs. | Handler test and call-path audit. | Verified |
| AC3 | Profile-save 404 returns isolated permanent `SubjectResourceMissing` without the one-minute retry. | Handler test. | Verified |
| AC4 | Cleanup selects only qualifying obsolete jobs and terminates them without deleting history. | Store transition test and endpoint review. | Verified |
| AC5 | Parsing captures allowlisted JRA profile URLs and carries them without changing deterministic identity. | Parser/API tests. | Verified |
| AC6 | A persisted URL is tried directly; failure or mismatch falls back through official discovery and cannot persist a wrong profile. | Existing location/fallback tests and parser tests. | Verified |
| AC7 | Production suppresses HTTP/browser-detail Information logs; Warning/Error and development Debug remain. | Configuration inspection/build. | Verified |
| AC8 | Lambda remains at 2,048 MB. | No infrastructure diff. | Verified |
| AC9 | Deployment is followed by reviewed cleanup and a normalized 48-hour comparison. | Production operation. | Externally blocked |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Move profile-job creation to the bulk API. | Main | Lead | Approval | API bulk endpoint, Collector race handler, related tests | API/Collector tests | Authoritative IDs and replay idempotency passed. | Verified | Lead — public contract and architecture | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T2 | Reclassify 404 and remove projection retry. | Main | Lead | T1 | Subject handler and tests | Handler test | 404 is isolated permanent failure without retry. | Verified | Lead — failure contract | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T3 | Add dry-run/execute obsolete-job retirement. | Main | Lead | T1 | Collection store, admin endpoint, tests | Store/endpoint tests | History-preserving cancellation path passed. | Verified | Lead — persistence and destructive criteria | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T4 | Propagate URLs and preserve validated fallback. | Main | Lead | Approval | Scraping models/parser/workflow and tests | Parser/fallback tests | Direct URL extraction and safe fallback passed. | Verified | Lead — public parsing contract | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T5 | Apply exact production log filters. | Main | Lead | Approval | Collector configuration | Inspection/build | Exact categories configured; Release build passed. | Verified | Lead — small configuration integrated with change | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T6 | Integrate and locally verify. | Main | Lead | T1-T5 | Entire approved change | Tests/build/format/diff/CodeGraph | 1,113 tests passed; build and final gates passed. | Verified | Lead — integration and final acceptance | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T7 | Deploy, review/execute cleanup, and observe 48 hours. | Operations | External | T6 | Production only | CloudWatch report | Awaiting explicit production operation. | Externally blocked | Operations — external authority required | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |

No agents were used. A later process review found that read-only exploration, fixture work, and independent review were separable even though architecture, persistence, and integration correctly remained with Main. Historical model, token, retry, correction, and review-count telemetry was not captured and is intentionally recorded as unavailable rather than reconstructed.

## Review gates

- **Design/task split:** The initial preflight/backoff proposal was replaced after tracing API-owned registration. The approved design fixes job ownership and adds audited cleanup.
- **Pre-implementation:** The user approved the revised implementation, URL fallback, and termination rather than deletion on 2026-09-19.
- **Checkpoint:** Focused tests cover API idempotency, Collector non-registration, 404 semantics, cleanup transition, URL extraction, and fallback. A full-suite-only timing failure passed alone and was classified as load-sensitive.
- **Final review:** T6 passed all local gates. T7 remains an explicit external production operation.

## Verification record

- `RaceEndpointsTests`: 26/26 passed.
- Subject handler and cleanup focused set: 33/33 passed.
- `RaceCardPageParserTests`: 20/20 passed.
- `CollectionOperationsEndpointTests`: 14/14 passed after explicit service binding was added.
- Load-sensitive `NavigateForSnapshotAsync_CalendarWaitsForVisibleRacecourseBeyondThreeSeconds` passed alone.
- Release solution build passed with zero warnings/errors.
- Full solution tests excluding `External`: 1,113 passed, 1 skipped, 0 failed.
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: passed.
- `dotnet build HorseRacingPrediction.sln -c Release --no-restore`: passed with zero warnings/errors.
- `git diff --check`: passed; line-ending notices are informational.
- CodeGraph was synchronized and the final call-path audit confirmed API-owned registration and no Collector race-derived registration path.
- Follow-up review confirmed `SearchBaseUrl` is stored at creation but only consumed by `SearchAsync`; changing the creation log to Debug and omitting that URL does not alter navigation behavior.

## Deployment and rollback

Deploy API and Collector together. Run cleanup with `execute=false`, review every selection, and only then invoke `execute=true`. Rollback redeploys the prior API/Collector; cancelled history remains auditable and a subject can be explicitly re-requested. Do not change Lambda memory during this rollout.

## Follow-up

- AC9/T7: production deployment, reviewed cleanup execution, and normalized 48-hour observation.
- Consider memory tuning only after p99/maximum memory and browser-resource failures show adequate headroom.
