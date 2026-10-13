# Full data migration preview — interaction mock

Proposal only. This is an evidence-gathering screen; it does not authorize or perform migration.

## Desktop — operation running

Settings → Data migration preview
`/settings/data-migration-preview/{operationId:guid}`

```
Full local-store inventory                                  Read-only
This is a non-atomic observation. It is not a migration plan or target census.

[Start preview — disabled while an operation is active] [Refresh status]

Operation: Running · Started 14:20 UTC · Last update 14:21 UTC
Progress: 2 / 4 configured stores observed · 19 / 47 mapped tables classified

Coverage by configured store
EventStore             Observed  · 14:20 UTC · 17 / 20 mapped tables
CollectionPlatform     Reading   · 14:21 UTC · 2 / 25 mapped tables
PredictionSchedule     Not started
LocalCollectionQueue   Not configured
External queue/files   Not inspected by this preview

Evidence page: CollectionPlatform · at most 128 observed keys in this bounded page
Table / evidence role                 State             Count / note
collection_resources                 Observed          typed identity fields only
collection_task_outbox               UnknownShape      payload type unsupported
collection_attempts                  Not inspected     later phase; not yet read

[Next page]

Separate store timestamps do not form one consistent snapshot. Unknown or
unsupported data remains unresolved. No identity is inferred from names.
There is no Apply, Execute, Resume, Reset, or job Retry action on this page.
```

The schema summary labels its count “physical objects (tables and indexes)” and explicitly says it is not a table or row census. Per-table `rowsInspected` is the number of keys observed so far, not a total. The page separately lists the supported row phases and the schema-only, unsupported, not-configured, and external/not-inspected surfaces.

## Loading, stale state, or interruption

```
Status refresh in progress…
Previous operation details remain visible and are marked STALE after any failed continuation or status refresh, including 400, 429, 503, and an empty response.

The command stopped before completing its configured scope.
Reason: ChildInterrupted (safe reason code)
Last observed: EventStore 14:20 UTC; CollectionPlatform 14:21 UTC
Unobserved: PredictionSchedule, LocalCollectionQueue

[Start a fresh preview] [Refresh status]
```

No automatic polling is used. Refresh is user-triggered. A restarted API/child never silently resumes at a cursor; it reads durable status and shows interruption/unknown. Starting again creates a new observation after the server revalidates configured stores and schema assumptions.

## Empty and incomplete states

An empty configured store is shown only when its bounded row scan completed and confirmed no rows. `NotConfigured`, `Unavailable`, `SchemaOnly`, `NotInspected`, `UnknownShape`, and `Truncated` are separate states, never synonyms for empty. Counts are explicitly labeled bounded observations, not total target counts when a page or surface is incomplete.

## Narrow viewport — 390px

Cards preserve store/table, evidence status, observation time, bounded count, and why evidence is incomplete. The non-atomic warning and disabled/loading state remain visible. Actions wrap without horizontal page scrolling; keyboard order is Start/Refresh, coverage, then Next. No raw operation arguments, paths, cursor, event or metadata payloads are shown.
