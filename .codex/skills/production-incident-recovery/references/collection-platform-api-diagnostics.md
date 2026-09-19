# Collection Platform API Diagnostics

Use this procedure to collect evidence for a production collection failure without changing queue, task, or failure state.

## Safety boundary

- Treat the API key as a secret. Never put it in a command line, URL, source file, transcript, log, change record, commit, or saved output.
- Supply the key through an interactive secure prompt or an approved in-memory credential provider. Do not print request headers.
- During diagnosis, call only `GET` endpoints. Recovery, retry, dismiss, suppress, pause, resume, request, migration, and deletion endpoints require separate user authorization.
- Do not disable TLS validation. Use HTTPS in production; permit HTTP only for loopback local development.
- Preserve the original failure evidence before any authorized recovery action.

The administration page session and the administration API use different authentication mechanisms. A signed-in browser page does not by itself add the required `X-Api-Key` header to a direct API navigation.

## Preferred evidence sequence

1. Record the user-visible failure-group URL, observation time, displayed error, affected target count, and latest attempt time.
2. Read the failure group:
   `GET /api/admin/collection/failure-notifications/groups/{groupKey}?page=1&pageSize=100`
3. For each returned target, read its complete bounded history:
   `GET /api/admin/collection/resources/{type}/{provider}/{resourceId}/{definition}?requestHistoryPage=1&taskHistoryPage=1&attemptHistoryPage=1&historyPageSize=100`
4. For every distinct execution batch referenced by an attempt, read:
   `GET /api/admin/collection/execution-batches/{executionBatchId}`
5. Correlate the trigger, resource, request, task, attempts, requested/final URL, HTTP status, page identification, Lambda request ID, execution batch, and neighboring batch tasks.
6. Compare the API evidence with the handler and store code that produced and persisted the error. Derive routes and response contracts from the current source rather than guessing them:
   - `src/HorseRacingPrediction.Api/CollectionController/CollectionPlatformEndpointExtensions.cs`
   - `src/HorseRacingPrediction.Api/Web/ApiBrowsing/AdminApiClient.CollectionPlatform.cs`

Run the repository helper from the repository root:

```powershell
./.codex/skills/production-incident-recovery/scripts/Get-CollectionFailureDiagnostics.ps1 `
  -BaseUri 'https://production-host.example' `
  -FailureGroupKey 'FAILURE_GROUP_KEY'
```

The helper prompts for the key, performs only the three read operations above, and returns one structured object. Redirecting its output is optional; inspect it in memory by default because response data can contain operational identifiers and URLs.

## Evidence limits

Report only what the API actually persists. If a parent attempt stores a generic error after collapsing a batch response, the later API response cannot identify the rejected or missing child item unless that item-level outcome was persisted elsewhere. In that case:

- state that the item-level result is **not persisted and cannot be reconstructed from the current API**;
- locate the code where detailed outcomes become a generic exception;
- do not infer the failed item from timing, ordering, or neighboring tasks;
- propose persisting safe item-level fields such as `ItemKey`, outcome status, and error code for future diagnosis.

A successful HTTP response proves only that the diagnostic read succeeded. It does not prove recovery. Recovery requires a separate production signal such as a succeeding replacement task and continued pipeline progress.

## Minimum incident note

Record the following without secrets:

```text
Observed at: <timestamp and timezone>
Failure group: <group key, definition, count>
Targets: <resource keys and task IDs>
Attempts: <result, time, HTTP/page evidence, correlation IDs>
Batch context: <execution batch and neighboring task outcomes>
Evidence gap: <none, or exact field that was not persisted>
Conclusion: <fact-based root-cause boundary>
Mutation performed: none
```
