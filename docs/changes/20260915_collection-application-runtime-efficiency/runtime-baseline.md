# Collector runtime baseline

Measured on 2026-09-15 JST with read-only AWS CLI and CloudWatch Logs Insights queries. No Lambda, queue, alarm, or infrastructure setting was changed.

## Current configuration

| Setting | Repository IaC | Deployed value |
| --- | ---: | ---: |
| Lambda memory | 2,048 MiB | 2,048 MiB |
| Timeout | 900 seconds | 900 seconds |
| Ephemeral storage | 4,096 MiB | 4,096 MiB |
| Reserved concurrency | 1 | 1 |
| SQS batch size / batching window | 1 / 0 seconds | 1 / 0 seconds |
| Architecture | Image default | x86_64 |

The event source mapping is enabled with partial batch failure reporting. Collector error, throttle, and oldest-message alarms were `OK` when inspected.

## Observed runtime

CloudWatch `REPORT` records were aggregated without returning request IDs, URLs, resource IDs, payloads, credentials, lease tokens, or error text.

| Cohort | Runs | Duration p50 | Duration p95 | Maximum | Average memory | Peak memory | Cold starts |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Rolling 14 days | 5,750 | 590.406 ms | 145,663.794 ms | 850,002.120 ms | 1,073.38 MiB | 1,939.77 MiB | 83 (1.44%) |
| Since 2026-09-14 18:49:10 UTC deployment | 117 | 116,211.767 ms | 531,473.913 ms | 840,934.260 ms | 1,021.17 MiB | 1,082.42 MiB | 7 (5.98%) |

The latest cohort averaged 153,439.158 ms and billed 17,953,644 ms, or 35,907.288 GB-seconds at 2 GiB. These figures are operational scale indicators only: existing records do not identify task kind or a memory-configuration cohort, and the large distribution shift makes them unsuitable for power-selection decisions.

## Phase telemetry contract

`CollectionPlatformWorkerClient` emits one structured information event for each acquired task that reaches handler execution. Its bounded dimensions are `Definition`, `ResourceType`, `Result`, and `MemorySizeMiB`. Values are:

- `TotalMs`: worker execution from acquire start through completion reporting;
- `AcquireMs`: acquire HTTP request and response decoding;
- `HandlerMs`: definition-handler execution, including scraping and its persistence calls;
- `CompleteMs`: completion HTTP request;
- `UnattributedMs`: non-negative remainder of total time.

The event deliberately excludes URLs, resource IDs, payloads, API keys, lease tokens, and error text. Deterministic tests require successful-task phase coverage `(AcquireMs + HandlerMs + CompleteMs) / TotalMs` to be at least 95%, and verify one safe terminal summary for classified failures and cancellation. The coarse handler phase makes wall time accountable at low overhead; nested navigation, parsing, and persistence timing can be added later only when this phase identifies a diagnostic need.

## Memory and ephemeral-storage decision

No configuration change is justified yet. The latest deployment peak is only 52.9% of the 2,048 MiB allocation, but the rolling peak is 94.7%. Reducing memory from the latest cohort alone could therefore create memory pressure for a different task kind.

Existing telemetry also has no `/tmp` high-water measurement. Production application writes are limited to the small Lambda response and failure-reason files, while browser runtime use is opaque. Keep 4,096 MiB until invocation-boundary sampling can report only used-byte deltas, without filenames.

After deploying the phase event, use the same replay-safe corpus and ordering for at least 30 successful runs per task kind and configuration. Compare a small candidate set sequentially through the normal deployment path; do not introduce a tuning service. Select the minimum measured GB-seconds only when all of these gates pass:

1. duration p95 is no more than 5% above the baseline;
2. there are zero timeouts;
3. peak memory remains below 85% of the configured allocation;
4. persisted output and failure semantics remain identical.

The current data contains one unlabelled configuration, so it does not meet the 30-run-per-task-kind/configuration gate. Memory and `/tmp` remain unchanged with reason rather than being tuned from incomparable workloads. Live trials also incur Lambda GB-seconds, API load, and JRA traffic; begin with 1,536 MiB and test 1,024 MiB only if the first candidate passes.
