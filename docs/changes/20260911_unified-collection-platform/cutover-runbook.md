# Collection queue cutover runbook

This runbook is the executable order for replacing the legacy collection queues. It does not authorize deleting domain data, source citations, credentials, or IAM access keys.

## Exact queue identities

| Role | Main queue | Dead-letter queue |
|---|---|---|
| Legacy | `horse-racing-prediction-collector` | `horse-racing-prediction-collector-dlq` |
| Replacement | `horse-racing-prediction-resource-collection` | `horse-racing-prediction-resource-collection-dlq` |

The replacement queue carries only the `{ taskId, dispatchGeneration }` contract. Never send legacy payloads to it or new payloads to the legacy queue.
The serialized contract also requires `contractVersion: 1`. Missing/unknown versions, an empty task ID, and a non-positive dispatch generation are poison messages: the collector must perform no API acquisition for them, SQS retries them up to the redrive limit, and the DLQ alarm requires operator inspection.

## Production failure matrix

| Failure | Expected automatic behavior | Operator check/action |
|---|---|---|
| Duplicate SQS delivery | API acquisition/generation checks keep execution idempotent; an expired lease is recovered by the watchdog | Confirm no overlapping active attempts and eventual terminal state |
| API/network unavailable before acquisition | Lambda fails; SQS retries up to 3 receives before DLQ | Check API health, Lambda `Errors`, queue age, then redrive only after recovery |
| Lambda timeout/process crash after acquisition | Message is redelivered; an active lease prevents overlap; watchdog reclaims the expired lease | Check attempt/lease timestamps before manual recovery |
| HTTP 429/5xx from source | Worker records a retryable attempt; location remains usable and API schedules the next task | Check retry time and access-limit policy; do not redrive the SQS message manually |
| Malformed/legacy/unknown-version message | No collection API call is made; message reaches DLQ after transport retries and the reconciler retains it | Preserve body/attributes for diagnosis, fix producer, then remove; do not redrive an unsupported body |
| DLQ reconciliation or deletion interrupted | Reconciliation is generation-aware and can run again | Confirm task state and DLQ depth converge; never purge before evidence capture |
| Reserved concurrency throttling | SQS retains work and queue-age alarm detects delay | Check `Throttles`; distinguish expected concurrency=1 backpressure from a concurrency/configuration fault |
| Deployment/cutover failure | Both queues remain during Gates 1-3 and event source can be switched back | Follow Gate 3 rollback; do not delete legacy queues or old job DB |

The Lambda timeout is 900 seconds and the queue visibility timeout is 5,400 seconds (six times the function timeout). Source retention is four days and DLQ retention is fourteen days. Keep these relationships when tuning values. `batch_size = 1` deliberately isolates poison messages; partial-batch response remains enabled as a deployment invariant.

Before redriving a DLQ message, record its body, message attributes, approximate receive count, task detail/attempt history, Lambda request ID, and API health window. Redrive only supported `contractVersion: 1` messages whose underlying fault has been corrected. Do not redrive malformed or legacy payloads.

## Gate 1: provision without switching

Use both defaults:

```text
activate_resource_collection_queue = false
retain_legacy_collection_queues    = true
```

Run `terraform plan` and confirm that it creates the two replacement queues without destroying either legacy queue. Apply that plan. Confirm that `queue_url` still names `horse-racing-prediction-collector`, while `resource_collection_queue_url` names the replacement.

The checked-in helper produces the gated plan (and applies it only when `-Apply` is supplied):

```powershell
./infra/collector-lambda/Invoke-CollectionQueueCutover.ps1 -Stage Provision -VarFile <tfvars-path>
```

If Terraform state predates the addition of `count` to the legacy resources, verify that the plan associates the existing objects with `aws_sqs_queue.collector[0]` and `aws_sqs_queue.collector_dlq[0]`. Stop if it proposes replacing either legacy queue.

## Gate 2: connect the replacement

Stop legacy producers and allow any already leased legacy work to finish. Deploy the API and collector versions that understand the new message contract. Then set:

```text
activate_resource_collection_queue = true
retain_legacy_collection_queues    = true
```

Review the plan. It must move the Lambda event source to the replacement queue and keep both legacy queues. Apply it, deploy the API with the resulting `queue_url`, and verify that no component is still producing messages to the legacy queue.

```powershell
./infra/collector-lambda/Invoke-CollectionQueueCutover.ps1 -Stage Activate -VarFile <tfvars-path>
```

## Gate 3: smoke test

Submit one known, non-destructive collection request through the production API. Record its resource key and task ID. The gate succeeds only when all of these are observed:

1. the outbox entry is dispatched to `horse-racing-prediction-resource-collection`;
2. Lambda receives the same task ID and dispatch generation;
3. exactly one active attempt reaches a terminal success state;
4. the expected domain record is written or updated idempotently;
5. CollectionState is current and operator projections show no unexpected failure;
6. neither legacy queue receives a new message;
7. the replacement DLQ remains empty.

On failure, keep both legacy queues, disable new producers, set `activate_resource_collection_queue = false`, apply, diagnose, and repeat the gate. Do not delete data or queues.

## Gate 4: delete approved legacy targets

Only after Gate 3 is recorded as successful, set:

```text
activate_resource_collection_queue = true
retain_legacy_collection_queues    = false
```

The configuration rejects legacy deletion while the replacement is inactive. Review the plan and confirm that the only SQS deletions are:

- `horse-racing-prediction-collector`
- `horse-racing-prediction-collector-dlq`

Apply the plan. Separately run the approved old job-data cleanup after its backup/dry-run checks. Do not delete domain data, citations, IAM users, or access keys.

旧 job DB は専用ツールで削除する。既定動作は dry-run であり、削除候補は指定した state directory 直下の
`collection-tasks.db`、`collection-tasks.db-wal`、`collection-tasks.db-shm` だけである。

```powershell
dotnet run --project tools/HorseRacingPrediction.CollectionCutover -- --state-dir <state-directory>
```

候補を確認後、Gate 3 で成功した新 CollectionPlatform の task ID を指定して実行する。ツールは
`collection-platform.db` 内で当該 task が `Succeeded` であることを確認し、削除対象を
`<state-directory>/cutover-backups/<timestamp>/` へ先にコピーしてから削除する。

```powershell
dotnet run --project tools/HorseRacingPrediction.CollectionCutover -- `
  --state-dir <state-directory> `
  --execute `
  --confirm-delete-legacy-job-db `
  --smoke-task-id <task-id>
```

再実行は成功し、追加の削除・空のバックアップ作成を行わない。`eventstore.db`、
`collection-platform.db`、`prediction-executions.db`、source citation、および上記3ファイル以外の
sidecarは対象外である。このツールはAWSへ接続せず、旧SQS/DLQ削除はTerraform helperだけで行う。

The helper refuses to plan this stage without the successful smoke task ID:

```powershell
./infra/collector-lambda/Invoke-CollectionQueueCutover.ps1 -Stage DeleteLegacy -VarFile <tfvars-path> -SmokeTaskId <task-id>
```

## Evidence to retain

Retain the three reviewed plans, apply results, smoke task and attempt IDs, queue metrics/screenshots, DLQ depth, old job-data cleanup dry-run and result, and the operator who confirmed each gate. Never place secrets in this record.
