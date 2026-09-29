# API route inventory

Read-only inventory supporting `docs/28-api-client-design.md`. Source of truth is endpoint registration in `src/HorseRacingPrediction.Api/Extensions/EndpointExtensions.cs` and the endpoint files cited below. CodeGraph was attempted first but its CLI was not on PATH and no MCP tool was available; source inspection was used instead.

## Coverage and existing consumers

The endpoint source scan found **136 registered Minimal API operations**: Collection 44, Races 29, Predictions 11, Horses 9, Jockeys 8, Trainers 7, Repairs 7, Owners 6, Memos 5, PredictionScheduling 3, Identity 2, MachineLearning 2, Subjects 2, Health 1. Method totals: GET 53, POST 62, PUT 10, PATCH 10, DELETE 1. Health is operational/non-business; the other 135 are business APIs. Paths in the table are fully expanded: endpoint methods registered on the `/api` groups have the `/api` prefix included.

ApiClient currently contains only `IRaceQueryService` and `IPredictionWriteService` abstractions; it has no Refit interfaces or generated/HTTP implementations. `Refit` and `Refit.HttpClientFactory` 16.3.0 are already referenced. Runtime callers currently include Predictor's `ApiOnlyPredictionWorkflow`, Agents' `RaceQueryTools` and `PredictionWriteTools`, Collector's `HttpRaceQueryService`, `HttpPredictionWriteService`, `HttpDataCollectionWriteService`, and its collection request/profile/identity/odds clients. Api's own administration UI uses the separate `Web/ApiBrowsing/AdminApiClient`; Api endpoint services also call ApiClient interfaces for identity and repair flows. These are evidence of consumers, not a proposal to adopt the new Refit clients in this change.

| Route family / target client family | Current consumer group / endpoint file folder |
|---|---|
| Races / `IRacesApi` | Api admin UI and endpoint services; Collector adapters; Predictor's race query flow |
| Horses / `IHorsesApi` | Api admin UI and endpoint services; Collector's entity/history write adapter; race query flow |
| Jockeys / `IJockeysApi` | Api admin UI and endpoint services; race query flow |
| Trainers / `ITrainersApi` | Api admin UI and endpoint services; race query flow |
| Owners / `IOwnersApi` | Api admin UI and owner identity/repair services |
| Predictions / `IPredictionsApi` | Predictor write flow and Api admin UI |
| Memos / `IMemosApi` | Api admin UI; memo operations used by agents/application consumers |
| MachineLearning / `IMachineLearningApi` | Api admin UI / prediction consumers |
| Collection / `ICollectionApi` | Api administration UI and Collector worker/request sink |
| PredictionScheduling / `IPredictionSchedulingApi` | Predictor scheduling / Api operations |
| Repairs / `IRepairsApi` | Api repair UI and repair endpoint services |
| Identity / `IIdentityApi` | Api endpoint services and Collector identity verifiers |
| Subjects / `ISubjectsApi` | Api administration UI and Collector JRA profile client |
| Health / excluded (unchanged) | operational health probes |

The consumer grouping above is by subsystem; it does not claim every subsystem calls every route in that family. ApiClient's existing query/write abstractions are used by `IRaceQueryService`/`IPredictionWriteService` consumers. Collection runtime contracts and Store types are currently defined under `HorseRacingPrediction.CollectionOperations` and `HorseRacingPrediction.Api.CollectionController`; those runtime/persistence types must not be moved wholesale into wire contracts. The serialized request/response surface needs explicit wire DTOs and boundary mapping.

## Current wire-contract evidence and review traps

この節は変更前の観測。改定設計では入力/戻りデータがある側にRequest/Responseと必要なDTOラップを用意し、既存利用者も追従する。入力/戻りなしの空型・空JSONは作らず従来204を維持する。旧JSON構造全体の互換性は受け入れ条件ではなく、README/正規設計を優先する。

- `Program.cs` installs `JstDateTimeOffsetJsonConverter` for Minimal API JSON. Contract converters/types under `Contracts/Time` are part of the wire/time behavior and require explicit preservation in Refit serialization.
- The global API-key middleware protects all paths other than health/UI/static paths. API paths use the `X-Api-Key` header (configurable header name); `/health` is anonymous. The ordinary `/api` write group additionally applies API-key, race-write, and active-collection filters. V2 admin/internal routes are protected by the global middleware, even when not in that write group.
- Minimal API GET arguments are query values, not JSON bodies. Preserve omitted values, defaults, repeated/collection query values, and date formatting as defined by each handler. Route constraints such as `:guid` also appear in the table.
- No endpoint was found returning a file/download or stream; health returns a small JSON object. Collection batch endpoints can use 202/207 and `Location`; callers should preserve those status/location semantics and not assume every successful POST is 200.
- Enums are serialized under the API's System.Text.Json web defaults; there is no API-level `JsonStringEnumConverter` registration found in `Program.cs`. Do not change enum/string or numeric behavior during namespace/type moves.
- DTOs are shared across production projects/tests, not just ApiClient. Broad namespace changes affect Contracts tests, Api endpoint bindings, Api UI/AdminApiClient, Collector, Predictor, Agents, Scraping and CollectionOperations consumers. Use IDE Rename Symbol / namespace-aware refactoring or `dotnet format`/Roslyn-based edits with a compile gate; avoid unrestricted text replacement where same names resolve to different project types.

## Complete registered route ledger

The endpoint class is the source reference. For grouped `/api` routes, the route below includes the common group prefix. Collection and other explicit routes are copied as registered. The endpoint file signature/handler is the authority for query/body and response DTO shape; this ledger is for exhaustive method/path coverage.

| Family | HTTP | Full path | Endpoint source |
|---|---|---|---|
| Races | GET | `/api/races` | `SearchRacesEndpoint.cs` |
| Races | POST | `/api/races` | `CreateRaceEndpoint.cs` |
| Races | GET | `/api/races/{raceId}` | `GetRaceEndpoint.cs` |
| Races | PATCH | `/api/races/{raceId}` | `CorrectRaceDataEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/card/publish` | `PublishRaceCardEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/close` | `CloseRaceLifecycleEndpoint.cs` |
| Races | GET | `/api/races/{raceId}/comparison` | `GetPredictionComparisonEndpoint.cs` |
| Races | GET | `/api/races/{raceId}/context` | `GetRacePredictionContextEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/entries` | `RegisterEntryEndpoint.cs` |
| Races | PUT | `/api/races/{raceId}/entries/{entryId}` | `UpdateEntryCollectedDataEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/entries/{entryId}/result` | `DeclareEntryResultEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/open-pre-race` | `OpenPreRaceEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/payout` | `DeclarePayoutResultEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/reschedule` | `MarkRaceRescheduledEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/result` | `DeclareRaceResultEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/start` | `StartRaceEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/track-condition` | `RecordTrackConditionEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/weather` | `RecordWeatherObservationEndpoint.cs` |
| Races | POST | `/api/races/result-bulk` | `DeclareRaceResultBulkEndpoint.cs` |
| Races | POST | `/api/v2/admin/races` | `CreateRaceFromScheduleEndpoint.cs` |
| Races | POST | `/api/v2/admin/races/{raceId}/entry-repair-previews` | `PreviewRaceEntryRepairEndpoint.cs` |
| Races | GET | `/api/v2/admin/races/{raceId}/entry-repair/assignment-fence-state` | `GetRaceAssignmentFenceEndpoint.cs` |
| Races | PATCH | `/api/v2/admin/races/{raceId}/entry-repair/hold` | `ReleaseRaceEntryRepairHoldEndpoint.cs` |
| Races | PUT | `/api/v2/admin/races/{raceId}/entry-repair/hold` | `UpdateRaceEntryRepairHoldEndpoint.cs` |
| Races | GET | `/api/v2/admin/races/{raceId}/entry-repair/hold-state` | `GetRaceEntryRepairHoldEndpoint.cs` |
| Races | GET | `/api/v2/admin/races/{raceId}/entry-repair/inspection` | `GetRaceEntryRepairEndpoint.cs` |
| Races | POST | `/api/v2/admin/races/{raceId}/entry-repairs` | `ApplyRaceEntryRepairEndpoint.cs` |
| Races | GET | `/api/v2/admin/races/{raceId}/odds-snapshot-records` | `ListRaceOddsSnapshotsEndpoint.cs` |
| Races | POST | `/api/v2/admin/races/{raceId}/odds-snapshot-records` | `CreateRaceOddsSnapshotEndpoint.cs` |
| Horses | GET | `/api/horses` | `SearchHorsesEndpoint.cs` |
| Horses | POST | `/api/horses` | `RegisterHorseEndpoint.cs` |
| Horses | GET | `/api/horses/{horseId}` | `GetHorseProfileEndpoint.cs` |
| Horses | PATCH | `/api/horses/{horseId}` | `CorrectHorseDataEndpoint.cs` |
| Horses | PUT | `/api/horses/{horseId}` | `UpdateHorseProfileEndpoint.cs` |
| Horses | POST | `/api/horses/{horseId}/aliases` | `MergeHorseAliasEndpoint.cs` |
| Horses | GET | `/api/horses/{horseId}/participations` | `GetHorseParticipationsEndpoint.cs` |
| Horses | GET | `/api/horses/{horseId}/race-history` | `GetHorseRaceHistoryEndpoint.cs` |
| Horses | GET | `/api/horses/{horseId}/weight-history` | `GetHorseWeightHistoryEndpoint.cs` |
| Jockeys | GET | `/api/jockeys` | `SearchJockeysEndpoint.cs` |
| Jockeys | POST | `/api/jockeys` | `RegisterJockeyEndpoint.cs` |
| Jockeys | GET | `/api/jockeys/{jockeyId}` | `GetJockeyProfileEndpoint.cs` |
| Jockeys | PATCH | `/api/jockeys/{jockeyId}` | `CorrectJockeyDataEndpoint.cs` |
| Jockeys | PUT | `/api/jockeys/{jockeyId}` | `UpdateJockeyProfileEndpoint.cs` |
| Jockeys | POST | `/api/jockeys/{jockeyId}/aliases` | `MergeJockeyAliasEndpoint.cs` |
| Jockeys | GET | `/api/jockeys/{jockeyId}/participations` | `GetJockeyParticipationsEndpoint.cs` |
| Jockeys | GET | `/api/jockeys/{jockeyId}/race-history` | `GetJockeyRaceHistoryEndpoint.cs` |
| Trainers | GET | `/api/trainers` | `SearchTrainersEndpoint.cs` |
| Trainers | POST | `/api/trainers` | `RegisterTrainerEndpoint.cs` |
| Trainers | GET | `/api/trainers/{trainerId}` | `GetTrainerProfileEndpoint.cs` |
| Trainers | PATCH | `/api/trainers/{trainerId}` | `CorrectTrainerDataEndpoint.cs` |
| Trainers | PUT | `/api/trainers/{trainerId}` | `UpdateTrainerProfileEndpoint.cs` |
| Trainers | POST | `/api/trainers/{trainerId}/aliases` | `MergeTrainerAliasEndpoint.cs` |
| Trainers | GET | `/api/trainers/{trainerId}/participations` | `GetTrainerParticipationsEndpoint.cs` |
| Owners | GET | `/api/admin/repairs/owner-identity` | `PreviewOwnerIdentityRecoveryEndpoint.cs` |
| Owners | POST | `/api/admin/repairs/owner-identity/execute` | `ExecuteOwnerIdentityRecoveryEndpoint.cs` |
| Owners | GET | `/api/owners` | `SearchOwnersEndpoint.cs` |
| Owners | GET | `/api/owners/{ownerId}` | `GetOwnerEndpoint.cs` |
| Owners | PUT | `/api/owners/{ownerId}` | `UpdateOwnerEndpoint.cs` |
| Owners | POST | `/api/owners/{ownerId}/merge` | `MergeOwnerEndpoint.cs` |
| Predictions | GET | `/api/predictions` | `SearchPredictionTicketsEndpoint.cs` |
| Predictions | POST | `/api/predictions` | `CreatePredictionTicketEndpoint.cs` |
| Predictions | GET | `/api/predictions/{predictionTicketId}` | `GetPredictionTicketEndpoint.cs` |
| Predictions | PATCH | `/api/predictions/{predictionTicketId}` | `CorrectPredictionMetadataEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/betting-suggestions` | `AddBettingSuggestionEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/evaluate` | `EvaluatePredictionTicketEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/finalize` | `FinalizePredictionTicketEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/marks` | `AddPredictionMarkEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/rationales` | `AddPredictionRationaleEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/recalculate-evaluation` | `RecalculatePredictionEvaluationEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/withdraw` | `WithdrawPredictionTicketEndpoint.cs` |
| Memos | POST | `/api/memos` | `CreateMemoEndpoint.cs` |
| Memos | DELETE | `/api/memos/{memoId}` | `DeleteMemoEndpoint.cs` |
| Memos | PUT | `/api/memos/{memoId}` | `UpdateMemoEndpoint.cs` |
| Memos | PUT | `/api/memos/{memoId}/subjects` | `ChangeMemoSubjectsEndpoint.cs` |
| Memos | GET | `/api/memos/by-subject/{subjectType}/{subjectId}` | `GetMemosBySubjectEndpoint.cs` |
| MachineLearning | POST | `/api/ml/train` | `TrainMlModelEndpoint.cs` |
| MachineLearning | GET | `/api/races/{raceId}/ml-prediction` | `GetMlPredictionEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/backfill-batches` | `ListBackfillBatchesEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/backfill-batches` | `CreateBackfillBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/backfill-batches/{id}` | `GetBackfillBatchEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/backfill-batches/{id}/recovery-batches` | `RecoverBackfillHolesEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/definitions/{definition}/revisions` | `CreateCollectionRevisionEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/execution-batches/{id:guid}` | `GetExecutionBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/failure-notification-groups` | `ListFailureNotificationGroupsEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/failure-notification-groups/{groupKey}` | `GetFailureNotificationGroupEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/failure-notifications` | `ListFailureNotificationsEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/known-recovery-batches` | `CreateKnownRecoveryBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/migration-previews/owner-identity` | `GetOwnerIdentityMigrationPreviewEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/migration-previews/race-detail` | `PreviewRaceDetailMigrationEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/migration-previews/race-entry-owner-repair` | `PreviewRaceEntryOwnerMigrationEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/migrations/race-detail` | `ApplyRaceDetailMigrationEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/migrations/race-entry-owner-repair` | `GetRaceEntryOwnerMigrationEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/migrations/race-entry-owner-repair` | `ApplyRaceEntryOwnerMigrationEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/operations/dashboard` | `GetCollectionDashboardEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/operations/monitoring-findings` | `GetCollectionMonitoringFindingsEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/operations/progress` | `GetCollectionProgressEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/operations/task-view-counts` | `GetTaskViewCountsEndpoint.cs` |
| Collection | PUT | `/api/v2/admin/collection/pipeline` | `SetCollectionPipelineEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/pipeline-state` | `GetCollectionPipelineEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/race-entry-owner-repair-batches` | `CreateRaceEntryOwnerRepairBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/race-entry-owner-repair-candidates` | `ListRaceEntryOwnerRepairCandidatesEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/races/{raceId}/readiness` | `GetRaceCollectionReadinessEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/recollection-batches` | `GetRevisionRecollectionProgressEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/recollection-batches` | `CreateRecollectionBatchEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/recollection-previews` | `PreviewRacePeriodRecollectionEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/recovery-batches` | `CreateCollectionRecoveryBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/recovery-previews/known` | `GetKnownRecoveryPreviewEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/resources/{type}/{provider}/{resourceId}/definitions/{definition}` | `GetCollectionResourceDetailEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/resources/{type}/{provider}/{resourceId}/definitions/{definition}/state` | `GetCollectionResourceStateEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/revision-impact-previews` | `PreviewRevisionImpactEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/states` | `SearchCollectionStatesEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/task-batch-previews` | `PreviewCollectionTaskBatchEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/task-batches` | `CreateCollectionTaskBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/tasks` | `ListCollectionTasksEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/tasks` | `CreateCollectionTaskEndpoint.cs` |
| Collection | PATCH | `/api/v2/admin/collection/tasks/{taskId:guid}` | `CancelCollectionTaskEndpoint.cs` |
| Collection | PATCH | `/api/v2/internal/collection/execution-batches/{id:guid}` | `TransitionCollectionExecutionEndpoint.cs` |
| Collection | POST | `/api/v2/internal/collection/execution-leases` | `AcquireNextExecutionEndpoint.cs` |
| Collection | POST | `/api/v2/internal/collection/tasks/{id:guid}/attempts` | `CompleteCollectionTaskAttemptEndpoint.cs` |
| Collection | POST | `/api/v2/internal/collection/tasks/{id:guid}/leases` | `AcquireCollectionTaskEndpoint.cs` |
| Collection | PATCH | `/api/v2/internal/collection/tasks/{id:guid}/leases/{leaseId}` | `HeartbeatCollectionTaskLeaseEndpoint.cs` |
| PredictionScheduling | POST | `/api/v2/internal/prediction-candidate-leases` | `AcquirePredictionCandidateLeasesEndpoint.cs` |
| PredictionScheduling | POST | `/api/v2/internal/prediction-candidates` | `EnqueuePredictionCandidatesEndpoint.cs` |
| PredictionScheduling | PATCH | `/api/v2/internal/prediction-candidates/{raceId}` | `TransitionPredictionCandidateEndpoint.cs` |
| Repairs | GET | `/api/admin/repairs/20260913-jra-horse-identity` | `GetHorseIdentityRepairEndpoint.cs` |
| Repairs | POST | `/api/admin/repairs/20260913-jra-horse-identity/apply` | `ApplyHorseIdentityRepairEndpoint.cs` |
| Repairs | GET | `/api/admin/repairs/subject-identification` | `GetSubjectIdentificationRepairEndpoint.cs` |
| Repairs | POST | `/api/admin/repairs/subject-identification/dismiss` | `DismissSubjectIdentificationFailuresEndpoint.cs` |
| Repairs | POST | `/api/admin/repairs/subject-identification/execute` | `ExecuteSubjectIdentificationRepairEndpoint.cs` |
| Repairs | GET | `/api/admin/repairs/subject-name-normalization` | `GetSubjectNameNormalizationEndpoint.cs` |
| Repairs | POST | `/api/admin/repairs/subject-name-normalization/apply` | `ApplySubjectNameNormalizationEndpoint.cs` |
| Identity | POST | `/api/identity/horse` | `ResolveHorseIdentityEndpoint.cs` |
| Identity | POST | `/api/identity/race` | `ResolveRaceIdentityEndpoint.cs` |
| Subjects | PUT | `/api/v2/admin/subjects/{kind}/{subjectId}/profile` | `PutSubjectProfileEndpoint.cs` |
| Subjects | GET | `/api/v2/admin/subjects/{kind}/{subjectId}/profiles/current` | `GetSubjectProfileEndpoint.cs` |
| Health | GET | `/health` | `GetHealthEndpoint.cs` |
