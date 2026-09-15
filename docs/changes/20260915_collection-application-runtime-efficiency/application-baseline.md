# Application persistence and HTTP baseline

Measured on 2026-09-15 against the pre-change source and the fixed workload of 12 races × 18 entries.

## Race-result persistence

The fixed comparison assumes the Horse, Jockey, and Trainer identities referenced by the result envelope already exist. This isolates the race-result persistence stage from subject discovery and is representative of a replay or of the normal workflow after subject profiles have been collected.

| Counter | Baseline | Candidate | Reduction |
| --- | ---: | ---: | ---: |
| Race command publishes / race | 42 | 1 | 97.6% |
| Race aggregate commits / 12 races | 504 | 12 | 97.6% |
| Subject existence queries / race | up to 54 | at most 3 | 94.4% |
| Subject existence queries / 12 races | up to 648 | at most 36 | 94.4% |

The candidate validates every entry before calling the one Race command. `RaceAggregateBulkCollectionTests` verifies invalid references and duplicate entry IDs emit zero events. `BulkRaceResultCommandTests` verifies the complete envelope is handled by one command. `RaceEndpointsTests.DeclareRaceResultBulk_WithEighteenEntries_PersistsOnceAndReplayAddsNoEvents` verifies all 18 persisted results and that a byte-equivalent replay appends no EventStore rows.

Related subjects are prefetched with one `Contains` query per type and missing identities are still registered through their own aggregate commands. Whole-race distributed atomicity is intentionally outside this change.

## Collector subject upserts

| Counter | Baseline | Candidate | Reduction |
| --- | ---: | ---: | ---: |
| HTTP requests / Horse, Jockey, or Trainer upsert | 2 (GET + POST/PUT) | 1 (PUT) | 50.0% |
| HTTP requests / 18 Horse + 18 Jockey + 18 Trainer upserts | 108 | 54 | 50.0% |

The API PUT handlers now create a missing aggregate and update an existing aggregate. Equal normalized values emit no event. A concurrent identical Horse PUT test verifies both callers succeed, and a collector transport test verifies exactly three PUTs and zero GETs for one Horse/Jockey/Trainer set.

## Referenced-subject request batching

Read-only call-path inventory found that the dominant remaining HTTP fan-out was not subject persistence: after each race-card capture, `RequestReferencedSubjectsAsync` posted one collection request for every distinct Horse, Jockey, Trainer, and Owner. The result-bulk API was already one request per race.

For the fixed 12-race × 18-entry comparison, the bounded worst case is 72 distinct referenced subjects per race:

| Counter | Baseline | Candidate | Reduction |
| --- | ---: | ---: | ---: |
| Referenced-subject request POSTs / race | 72 | 1 | 98.6% |
| Result + citation + referenced-request POSTs / race | 74 | 3 | 95.9% |
| Result + citation + referenced-request POSTs / 12 races | 888 | 36 | 95.9% |
| Race + referenced-request write transactions / race | 114 | 2 | 98.2% |
| Race + referenced-request write transactions / 12 races | 1,368 | 24 | 98.2% |

With 48 distinct referenced subjects per race, the lower-bound observed model is 600→36 calls, a 94.0% reduction. Both bounds exceed AC6's 80% gate. The batch endpoint retains per-item `Created`, `Reused`, or `Rejected` outcomes; `batchId:itemKey` is bound to a persisted canonical payload fingerprint. The Collector batch ID includes the source task ID, so response-loss replay of the same task reuses requests. A later task receives a distinct request identity but remains subject to the store's existing ordinary `Discovery` deduplication and scheduled-refresh policy; batching does not redefine freshness.

The server applies valid items sequentially through the shared `RequestCoreAsync` invariant path inside one database transaction and commits once per race batch. Tests assert one database transaction commit for multiple items, one batch POST, race-level deduplication, partial rejection, payload-bound replay identity, no extra replay tasks, and compatibility of the existing single-item client contract.
