# Source table and reference scope

This is a proposal for a future read-only preview, not a complete transformation specification. The source-defined physical table names and counts come from [the M2 inventory](../20261011_recurring-horse-identity-conflict/persistence-inventory.md): EventStore 20, CollectionPlatform 25, PredictionSchedule 1, LocalCollectionQueue 1, total 47. The source for EventStore keysets is `EventStoreDbContextModelSnapshot.cs`; for CollectionPlatform it is `CollectionPlatformDbContext.cs`; the two single-table keys are in `PredictionScheduleStore.cs` and `LocalCollectionQueue.cs`. Runtime schema reconciliation must compare those mappings/keys with the configured files; it must never assume the target has exactly these tables. If an actual key/type differs from the known model, that table is `Unsupported/UnknownShape`, not silently skipped.

`Inspect` below means bounded typed evidence, not permission to dump rows. All 47 mapped tables are enumerated with their source-defined stable primary-key keyset and proposed reference role. Only the listed key, identity/reference, fixed status/type, and minimal policy fields may be read into typed evidence. Raw event/metadata/JSON, source URLs, names of users/actors, paths, and arbitrary columns are not exposed. A table whose schema/payload does not match the source-supported shape is `UnknownShape`/`Unsupported`/`Incomplete`, never silently skipped or represented as empty. Unknown extra physical objects are counted by safe category only. `SchemaOnly` is not a row census; all 47 listed tables must either be row-inspected under the role below or explicitly report why their rows could not be safely interpreted.

## EventStore — 20 mapped tables

| Physical table | Stable keyset key | Proposed row/reference evidence role |
| --- | --- | --- |
| `EventEntity` | `GlobalSequenceNumber` | Enumerate aggregate ID/type/sequence and native event-type/version status; typed parsers inspect identity/reference roles for supported event families. Unknown or malformed event type/shape is counted, never omitted. No event JSON. |
| `SnapshotEntity` | `Id` | Inspect aggregate identity/sequence and snapshot type/version/size classification. Do not deserialize or return snapshot bytes; unsupported payloads block completeness. |
| `HorseIdentityRepairCandidates` | `CandidateId` | Inspect old/target horse keys and bounded persisted repair state/provenance codes; preserve as audit evidence, not proof by itself. |
| `HorseIdentityRepairRedirects` | `SourceHorseId` | Inspect source/target horse IDs and redirect state to expose conflicts/cycles; do not follow or rewrite redirects. |
| `HorseRaceHistories` | `HorseId` | Inspect HorseId and typed RaceId references from supported Entries shape; never infer HorseNumber from RaceId. |
| `Horses` | `HorseId` | Root horse IDs and approved current display/name/birth fields; no name-only mapping. |
| `HorseWeightHistories` | `HorseId` | Inspect HorseId and supported typed observation keys; preserve measurements/history. |
| `JockeyRaceHistories` | `JockeyId` | Inspect JockeyId and typed RaceId references from supported Entries shape; preserve outcomes. |
| `Jockeys` | `JockeyId` | Root jockey IDs and approved name fields; never normalize/merge by display name. |
| `JraSubjectProfileReadModel` | `SubjectId` | Inspect SubjectId, canonical source-identity validation status, and bounded profile evidence status; do not expose URL/source JSON. |
| `MemoSubjects` | `SubjectKey` | Inspect typed subject references and association status only; memo content is not returned. |
| `OwnerAliasMappings` | `NormalizedAlias` | Inspect alias/owner key references and uniqueness/conflict state; do not infer owner equivalence from spelling. |
| `OwnerMergeAudits` | `AuditId` | Inspect bounded old/new owner keys and audit-shape status; preserve history, do not apply merges. |
| `PredictionComparisons` | `RaceId` | Inspect typed race/subject key references and comparison-shape status; do not return picks or values. |
| `PredictionTickets` | `PredictionTicketId` | Inspect typed referenced aggregate IDs and ticket-shape status; preserve user decisions/content. |
| `RacePredictionContexts` | `RaceId` | Inspect RaceId and supported typed participant-reference shape; unknown JSON/version is explicit incomplete. |
| `RaceResults` | `RaceId` | Inspect RaceId and supported result-reference shape; do not recalculate or return result details. |
| `RaceSummaries` | `RaceId` | Inspect RaceId and supported summary-reference shape; no inferred identity mapping. |
| `SubjectIdentificationRepairIssues` | `IssueId` | Inspect typed issue subject key and state/category code; no free-form diagnostic payload. |
| `Trainers` | `TrainerId` | Root trainer IDs and approved names; no name-only mapping. |

## CollectionPlatform — 25 mapped tables

| Physical table | Stable keyset key | Proposed row evidence role |
| --- | --- | --- |
| `race_repair_holds` | `(RaceId, Generation)` | Inspect RaceId and bounded hold state; identify blocked operations, do not release holds. |
| `collection_resources` | `ResourcePk` | Inspect ResourcePk, Type, Provider, ResourceId, and bounded canonical identity status; do not expose AttributesJson/source URL. |
| `collection_resource_suppressions` | `(Type, Provider, ResourceId)` | Inspect resource key and suppression state; never remove or bypass. |
| `collection_definitions` | `DefinitionId` | Inspect definition ID, revision/schema/status shape; no definition mutation. |
| `collection_revisions` | `(DefinitionId, Revision)` | Inspect revision/definition key references and bounded state. |
| `collection_revision_impacts` | `ImpactId` | Inspect revision and affected-resource key references; affected counts are bounded observations. |
| `collection_states` | `(ResourcePk, DefinitionId)` | Inspect ResourcePk and state key/status shape; no lease/state transition. |
| `collection_requests` | `RequestId` | Inspect request ID and typed resource/operation references; no requester identity or raw request body. |
| `collection_request_batch_bindings` | `BatchItemId` | Inspect batch/request references and uniqueness state. |
| `collection_tasks` | `TaskId` | Inspect task/request/resource references and bounded task status; no raw payload or retry. |
| `collection_active_tasks` | `(ResourcePk, DefinitionId)` | Inspect active task/resource keys and exclusivity state; no cancellation/resume. |
| `collection_attempts` | `AttemptId` | Inspect attempt/task references, ordinal and terminal/status shape only; no exception text. |
| `resource_locations` | `LocationId` | Inspect ResourcePk and location kind/status; never return path or locator value. |
| `collection_task_outbox` | `OutboxId` | Inspect task/event reference and delivery state/ordinal; no payload and no delivery/ack. |
| `collection_attempt_stage_outcomes` | `StageOutcomeId` | Inspect attempt/stage keys and bounded status; no free-form details. |
| `race_artifact_states` | `(ResourcePk, Artifact)` | Inspect RaceId/resource references and artifact-state key/status only; no artifact path. |
| `race_scheduling_evidence` | `ResourcePk` | Inspect RaceId and bounded evidence kind/state; no raw evidence payload. |
| `collection_execution_leases` | `ExecutionBatchId` | Inspect operation/resource key and lease-expiry/status classification; never renew or release a lease. |
| `collection_dispatcher_fairness_state` | `StateId` | Inspect scheduler key/version/status shape only; no scheduler mutation. |
| `collection_platform_controls` | `ControlId` | Inspect control key/version/presence and safe status codes; do not alter pause/maintenance controls. |
| `collection_failure_notifications` | `NotificationId` | Inspect task/resource reference and notification state; no free-form reason/message. |
| `collection_subject_identification_selections` | `NotificationId` | Inspect selection key/typed subject reference and state; no selection mutation. |
| `collection_backfill_batches` | `BatchId` | Inspect batch ID/status and bounded batch/resource references; do not replay. |
| `collection_batch_recovery_progress` | `BatchId` | Inspect batch/progress keys and state; do not resume recovery. |
| `collection_batch_recovery_scan` | `ScanId` | Inspect scan/batch cursor-presence and state only; never return raw cursor or mutate scan. |

## PredictionSchedule and LocalCollectionQueue

| Store / physical table | Stable keyset key | Proposed row evidence role |
| --- | --- | --- |
| PredictionSchedule / `prediction_candidates` | `RaceId` | Inspect RaceId, schedule state, availability/lease presence and supported key shape; do not claim the underlying domain race is valid from the schedule row alone. |
| LocalCollectionQueue / `local_collection_messages` | `message_id` | If configured, inspect message key, delivery/visibility/receive-count/DLQ status and supported payload-type classification without returning JSON. If unconfigured, report `NotConfigured`; if provider is external, report the external queue `NotInspected`, not zero. |

## Objects and sources beyond the 47 application tables

The preview also reconciles safe metadata for configured SQLite physical objects: expected tables/indexes and known migration/schema-history/provider-lock objects versus unknown tables/views/triggers/indexes. It exposes counts and safe status, not raw object names or SQL. Schema drift, unknown physical objects, missing configured files, unsupported columns/types, malformed events, duplicate or missing keys, over-limit values, and read failures all block completeness for the affected surface.

The following are not represented by the four-store row scan and remain explicit `NotInspected`/external coverage: SQS and DLQ, Lambda and scheduled delivery state, external files and repair sidecars, backups/manifests, logs, model/object storage, host mounts/configuration, and any stores not discovered by the configured allowlist. M2 found references to these surfaces but did not establish their deployed contents. A successful preview cannot be called full target migration coverage until the parent obtains an independently authorized target census and resolves these exclusions.

## Bounds and reconciliation

- Keyset scan each table independently with a fixed, documented page cap and bounded composite key; no OFFSET, unbounded in-memory keyset, or one query/request per entity.
- Per-row payload length is checked in SQL before materialization; parser depth, event/version allowlist, rows, references, response/journal bytes, and elapsed time are capped. Excess becomes visible `Truncated`/`Unsupported`/`UnknownShape` status.
- Reconcile per-table scanned keys/typed evidence/unknown counts to the bounded page ledger. Counts are observations, not a point-in-time total when pages were read at distinct times.
- Preserve unique-key, event-sequence, stream/snapshot, resource, request/task/attempt/outbox/lease, and sidecar invariants. The preview reports collisions and missing references; it does not solve them.
- A supported typed reader is not a transformation specification. Every eventual mapping and consumer closure remains subject to target samples, domain review, replay tests, and explicit parent approval.
