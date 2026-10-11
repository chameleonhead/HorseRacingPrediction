# Source-defined persistence inventory

M2 investigation at HEAD `3c91af76afb84c7fbfaf96a9b502324fd669c6b1`, 2026-10-11. This is source coverage, **not** the actual mounted production schema, records or queue contents. Apply plans require reconciliation with a target census. See [migration method](whole-data-migration.md).

## Physical table ledger

| Store | Application tables | Count / authority |
| --- | --- | --- |
| EventStore | `EventEntity`, `SnapshotEntity`, `HorseIdentityRepairCandidates`, `HorseIdentityRepairRedirects`, `HorseRaceHistories`, `Horses`, `HorseWeightHistories`, `JockeyRaceHistories`, `Jockeys`, `JraSubjectProfileReadModel`, `MemoSubjects`, `OwnerAliasMappings`, `OwnerMergeAudits`, `PredictionComparisons`, `PredictionTickets`, `RacePredictionContexts`, `RaceResults`, `RaceSummaries`, `SubjectIdentificationRepairIssues`, `Trainers` | 20; `Infrastructure/Persistence/Migrations/EventStoreDbContextModelSnapshot.cs` ToTable mappings, lines 53–718. `MemoSubjects` is a physical table for MemoBySubjectReadModel, not an inferred DbSet name. |
| CollectionPlatform | `race_repair_holds`, `collection_resources`, `collection_resource_suppressions`, `collection_definitions`, `collection_revisions`, `collection_revision_impacts`, `collection_states`, `collection_requests`, `collection_request_batch_bindings`, `collection_tasks`, `collection_active_tasks`, `collection_attempts`, `resource_locations`, `collection_task_outbox`, `collection_attempt_stage_outcomes`, `race_artifact_states`, `race_scheduling_evidence`, `collection_execution_leases`, `collection_dispatcher_fairness_state`, `collection_platform_controls`, `collection_failure_notifications`, `collection_subject_identification_selections`, `collection_backfill_batches`, `collection_batch_recovery_progress`, `collection_batch_recovery_scan` | 25; `CollectionOperations/CollectionPlatform/CollectionPlatformDbContext.cs:53–200` explicit ToTable mappings. Schema version 25. |
| PredictionSchedule | `prediction_candidates` | 1; `CollectionOperations/PredictionScheduling/PredictionScheduleStore.cs:15–22,182–198`; default `prediction-scheduling-state/prediction-executions.db`. |
| Optional LocalCollectionQueue | `local_collection_messages` | 1; `CollectionOperations/CollectionPlatform/LocalCollectionQueue.cs:22–38,71+`; JSON dispatch/wake payload, receipt/visibility/receive count/DLQ state. Actual production use must be checked. |

Paths above are under `src/HorseRacingPrediction.` where appropriate (`Infrastructure`, `CollectionOperations`). Total: **20 + 25 + 1 + 1 = 47 source-defined application tables**. Metadata `__EFMigrationsHistory`, `collection_schema_history`, provider locks and SQLite internals are separate and also require preservation/classification. They are not discarded because they are excluded from the application-table count.

Independent Lead checks: ToTable occurrence counts returned 20 and 25 respectively; `git diff --name-only -- src` was empty. Worker reconciled explicit mappings, not DbSet counts. Runtime models, actual deployed schema and unknown extra tables must still be compared before any claim of target completeness.

## Preservation and transformation classes

| Surface | Planned treatment; exact mapping remains T3 |
| --- | --- |
| EventEntity / SnapshotEntity | Typed, version-aware reference/key transformation only for proven mappings. Preserve event IDs, sequence and domain meaning. Stream/snapshot collision blocks; serializer metadata and replay must be validated. |
| Horse/Jockey/Trainer entities, profile and histories | Preserve valid unmapped IDs; transform mapped keys and references, including compound keys. Unknown source/name/DOB relations block identity mapping. |
| Race context/result/comparison and jockey-history JSON | Transform mapped typed identity/EntryId references; preserve results, money, times, ordering and observations. No raw string replacement. |
| PredictionTickets, MemoSubjects and their source events | Preserve user/Predictor/actor input, reasoning, marks, links and IDs. Transform only proven typed references; preserve historical citation URLs/evidence as history. Re-fetch is not restoration. |
| OwnerAliasMappings / OwnerMergeAudits | Owner is not an aggregate. Derived owner DTO ID, alias rules, merge audits and persisted OwnerName must be reviewed together; do not rewrite original names to normalize IDs. |
| Horse identity repair and subject-identification repair rows | Preserve provenance and historical audit meaning; mapped operational refs require recipes. Existing permanent redirects are not proof of physical migration. Removal is a separately verified cutover step, not blind cleanup. |
| Collection resources/locations/states/suppressions | ResourcePk is distinct from ResourceId; unique `(Type,Provider,ResourceId)` collisions require rules. Preserve request/state meaning and locators. Profile locators are not always identity evidence. |
| Collection tasks/requests/batch binding/attempts/outbox/leases | Preserve history, idempotency and delivery ownership. All relational keys and metadata must reconcile. Decide typed operational transformation versus retaining historical source work and creating canonical Recovery, not arbitrary task-ID changes. |
| Collection revision/definition/fairness/control/backfill/recovery tables | Copy unchanged unless an explicit map crosses a typed field. Scheduling controls and schema version are not domain identities. Confirm active reservations and controls before resume. |
| Prediction candidate schedule | Keep valid RaceId; transform only proved race mapping. Preserve scheduling semantics; resolve active lease/restart state before cutover. |
| Local queue / SQS and DLQ | Inspect/fence/drain/reconcile dispatch payloads and inflight/redelivery. SQLite copies cannot capture SQS. Preserve evidence; no blanket purge. |

This is a coverage plan, **not** a complete column/event transformation specification. Every table/column/event version/JSON shape will be assigned a concrete keep/transform/history-only disposition in T3 using actual target data. Unknown structures block a full manifest.

## Other durable/external surfaces

- `.race-locks` repair sidecars and per-operation backup manifests: `RaceWriteCoordinator.cs:19–79,149–170` verifies files, hashes, OperationId/Fingerprint against the repair event and projection/snapshot consistency. Historical transformation cannot ignore them. Independent Lead CodeGraph confirmed those checks.
- Production volumes, database filename/config overrides, generated backup packages, raw collector response/failure/artifact files and logs: preserve/classify by actual mount census. Invocation output is not automatically an authoritative domain store or safely disposable.
- SQS main/DLQ, Lambda executions and scheduled consumers/producers: external delivery boundary requiring actual environment evidence.
- ML model APIs accept save/load streams; no durable model-path or application object-store client was established by the bounded source search. This is not proof that the host has no such storage. Verify host/config/infrastructure before excluding them.
- Secrets and credentials are outside domain rekeying. Never copy them into diagnostic reports, logs or change records; protect any required operational backup.

## Identity producer inventory

Aggregate families: Horse, Jockey, Trainer, Race, PredictionTicket, Memo. Owner is derived from persisted owner names with alias/merge records. Direct API creation permits explicit IDs or new IDs; current collector/parser/write-client paths generate source-aware Horse IDs, name-normalized Jockey/Trainer IDs, date/course/number Race IDs and RaceId+HorseId-derived EntryIds. Discovery, imports, manual/bulk/refresh/result paths and prediction writers must reconcile with the same approved contract.

No global rekey is proposed for valid caller-created aggregates or Memo/Ticket/User/Actor/Operation/Task/Request/Attempt IDs. A mismatching freshly generated ID alone does not prove the existing identity is wrong. Source-less/manual identities need explicit classification, not forced external-ID conversion.

## Existing tool limits / independent evidence

- `SqliteDatabaseMigrator` handles EventStore schema and per-store backup; `CollectionPlatformSchemaMigrator` handles collection schema v25. Neither migrates domain identity across stores.
- `RaceEntryRepairInspector.cs:43–170` hydrates one race and compares selected projections; it is not a demonstrated whole-store rebuild tool.
- `20260723133851_InitialEventStore.cs:250–259` enforces event `(AggregateId,AggregateSequenceNumber)` and snapshot `(AggregateName,AggregateId,AggregateSequenceNumber)` uniqueness. Combining old/target streams by sequence renumbering is not an approved solution.
- Existing test families `RaceEntryRepairProjectionComparisonTests`, `RaceEntryOwnerRepairEndpointsTests`, `SharedCollectionIdentityTests`, `SqliteDbContextProviderTests`, `EventPersistenceTests` cover selected paths. **None were rerun in M2 and none proves the new whole-dataset migration.** New transformation/clone/cutover tests and target evidence are required.
- Required future canonical-document reconciliation: `docs/00-system-architecture.md`, `docs/10-domain-design.md`, `docs/26-collection-platform-design.md`, collector/Lambda documents; predictor/source-contract documents only where that contract changes. Historical records remain historical.
