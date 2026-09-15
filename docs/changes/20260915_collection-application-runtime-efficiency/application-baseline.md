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

## HTTP acceptance-gap finding

The already-deployed result collector entered this change with one `/api/races/result-bulk` HTTP request per race, so grouping the server-side Race commands cannot reduce that external call further. Removing subject existence GETs yields 50%, not the AC6 target of 80%, for the subject-upsert portion of the fixed workload. Reaching 80% for HTTP would require a new multi-subject transport contract or cross-task buffering, both beyond the approved idempotent single-subject upsert design. No baseline is relabelled as an improvement: the 97.6% result applies to Race write transactions, not external HTTP calls.
