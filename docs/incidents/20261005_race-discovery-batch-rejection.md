# Race discovery batch rejection investigation

Observed at: 2026-10-05 09:37 JST. User requested diagnosis only.
Failure group: 3731ACE9AD8977C8, race-discovery, one target.
Resource: Race/JRA/discovery:2026100300.
Task: 99fc3008-1ab6-45e9-8eba-219ded5a2092.
Mutation performed: none. No retry, suppression, pipeline change, deployment or source edit.

## Scope and routing

Lead loaded agent-task-orchestration and production-incident-recovery plus API diagnostics reference/script in this turn. R1 is Verified: bounded source investigation on existing collection_resume_diagnosis, requested gpt-6-luna/high; observed runtime model/token data unavailable. Read-only source/test scope; no implementation or production access delegated. Lead owns credential security, GET-only evidence and acceptance. R2 Lead production evidence is Verified: approved in-memory AWS Lambda credential provider, bounded helper diagnostics for one group/target, matching Lambda logs. No key/header/raw credential recorded. Existing dirty AGENTS/skills/routing changes preserved. Acceptance: explain persisted failure and source path, distinguish known condition from unpersisted inner reason, recommend safe corrective options without mutation. No code test authoring/build required for diagnosis; existing source/tests supply independent checks, not a claimed recovery proof.

## API evidence

Preferred read-only Get-CollectionFailureDiagnostics.ps1 returned one target, complete bounded two-attempt history and both correlated batches.

- Attempt1: 04Oct02:10:25–02:11:33, Result5/RaceListNotYetAvailable.10Oct Tokyo selection absent, future publication hypothesis, CollectedMeetings8/Cancelled0. Deferred until05Oct09:00.
- Attempt2: 05Oct09:06:24–09:07:40, Result3/InvalidOperationException: `Race discovery batch response was incomplete or rejected.` PermanentFailure task status6. RequestedUrl/FinalUrl/HttpStatusCode not persisted (null).
- Failed notification f537c8dd-f5a9-4773-8774-5a5bfd7cd918; batch4ff090a6-ebcd-4486-8ce4-1ad0b11d4710; Lambda request110f6f1b-efe0-5f6a-9291-0c2703df4850.
- Execution batch declares batchTaskCount8 but contains only this one persisted attempted task. This is not the count of race payload items, and does not identify a rejected child race.

## Correlated Lambda evidence

Exact request START09:06:23, END09:07:52 JST. Navigation completed for September calendar and26/27Sep result lists, October calendar and03/04Oct race lists.10Oct Tokyo/Kyoto starts were logged without matching completion in that invocation. Those dates alone do not identify an API-rejected race.

Runtime: PermanentFailure, HandlerMs75,666.4363, HandlerApiMs7,169.6862, HandlerApiCallCount1; total task78,737.6874ms. Logs show neither original batch item results nor a specific rejected/missing ItemKey. Current persisted notification/attempt stores only the generic exception. Source investigation must locate the collapse/validation boundary before concluding what is known and unknown.

## Source findings and conclusion

Exact exception originates in JraRaceCollectionHandlers.cs:230–248 ValidateDiscoveryBatchResponse, after decoding the batch response. It rejects differing submitted/returned ItemKey multisets, unaccepted outcome statuses (including Rejected), or invalid receipt IDs. Created/Reused require nonempty RequestId and TaskId; Held requires nonempty RequestId and optional valid TaskId; Accepted compatibility fallback requires both IDs absent. Lead independently verified this predicate with current CodeGraph source.

CollectionRequestBulkOutcomeDto.cs:5–8 supplies per-item ItemKey, Status, receipt IDs, ErrorCode and Message. CreateCollectionTaskBatchEndpoint.cs:79–91 returns Submission.ExplicitItems and HTTP207 for partial outcomes. Store explicit rejection reasons include ResourceSuppressed, IdempotencyMismatch and InvalidRequest (CollectionPlatformStore.cs:3137–3208). These are possible contract reasons, not a finding that any particular one caused this invocation. Missing keys/receipt integrity failures cannot be ruled out from persisted evidence.

Existing Discovery_RejectsBatchWithPerItemFailureOrMissingOutcome covers rejected and missing outcomes but only asserts the generic exception; client and API contract tests preserve per-item error/status/idempotency fields. No new test or runtime reproduction was performed. The API supplies detailed outcomes, but Collector collapses them into a generic exception and does not persist the rejected/missing ItemKey, outcome status or error code. Original item-level cause is not persisted and cannot be reconstructed from the current API or available correlated Lambda logs. Timing/navigation order/execution-batch size are not substitutes.

The supplied URL is an internal discovery-job key, not an external race page. Discovery uses a reference date and ordinarily examines a date window; collecting several meetings is expected, not itself evidence of a malformed URL. The observed latest failure is a batch response validation failure, distinct from the earlier future-unpublished defer.

Corrective proposal (not implemented): retain bounded safe per-item key/status/error-code plus missing/duplicate/receipt-integrity classification in attempt diagnostics; preserve existing integrity stops and redact free-form message/URLs. Determine the actual rejected item/condition before changing isolation/retry behavior. Accepted children may already have been registered before validation throws; blindly replaying the entire mutating batch is not a diagnostic read. No retry, behavior change or deployment approved in this diagnosis.

Final Lead acceptance: both read-only tasks Verified; runtime exception/timestamps match source predicate; uncertainty explicitly bounded and no individual rejected race/reason inferred. Public page fetch was inaccessible, authenticated administration API supplied the actual failure evidence. Investigation complete; recovery/permanent implementation not performed. Existing unrelated edits remain untouched; only this incident artifact was created.

## Approved recovery continuation

The user subsequently approved the staged fix/recovery; [governing approved record](../changes/20261005_race-discovery-recovery/README.md) owns acceptance.10:16 JST re-read confirms pipeline paused and original failed task/attempt2 unchanged.10:17 bounded child-state snapshot contains140 distinct race-detail/race-odds tasks for26/27Sep,03/04Oct,10/11Oct; safe task/resource/status/attempt/revision/lane fields retained in session memory for post-recovery comparison. This is a date-scoped universe, not evidence that all these tasks belonged to the original response. No production mutations yet. Existing completion evidence lost per-item outcomes; new-parent recovery alone cannot prove original-batch cause correction.

10:25 actionable notification read (limit20) returned5: selected race-discovery failure and4 horse-profile/SubjectNotIdentified failures. No recovery task IDs set. Other notifications are retained and not in the retry selector; no classification is inferred from an absent response field. Recovery resume review will use source/attempt safety evidence, not erase these notifications to satisfy the deployment guard.

11:20 implementation checkpoint: ordinary same-parent discovery replay with changed StartTime reproduced IdempotencyMismatch through the actual handler/client/API/store before correction. Deterministic content-scoped submission identity now accepts that changed observation while retaining prior child request/task IDs, including a succeeded child's unchanged attempt count. Adding a race creates only that new child. Explicit Backfill/PeriodRecollection retain their legacy identity, and deliberately reusing a caller batch ID with different content still rejects. This establishes a producer defect and its bounded correction; it does not recover the lost original per-item outcome or establish its exact historical reason. Final local Collector423/423 and API432/1known skip pass; final Linux, rollout and target recovery remain pending.11:13 authenticated read confirmed still paused; no production mutation performed.
