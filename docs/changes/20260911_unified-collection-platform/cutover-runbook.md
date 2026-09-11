# Collection queue cutover runbook

This runbook is the executable order for replacing the legacy collection queues. It does not authorize deleting domain data, source citations, credentials, or IAM access keys.

## Exact queue identities

| Role | Main queue | Dead-letter queue |
|---|---|---|
| Legacy | `horse-racing-prediction-collector` | `horse-racing-prediction-collector-dlq` |
| Replacement | `horse-racing-prediction-resource-collection` | `horse-racing-prediction-resource-collection-dlq` |

The replacement queue carries only the `{ taskId, dispatchGeneration }` contract. Never send legacy payloads to it or new payloads to the legacy queue.

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

The helper refuses to plan this stage without the successful smoke task ID:

```powershell
./infra/collector-lambda/Invoke-CollectionQueueCutover.ps1 -Stage DeleteLegacy -VarFile <tfvars-path> -SmokeTaskId <task-id>
```

## Evidence to retain

Retain the three reviewed plans, apply results, smoke task and attempt IDs, queue metrics/screenshots, DLQ depth, old job-data cleanup dry-run and result, and the operator who confirmed each gate. Never place secrets in this record.
