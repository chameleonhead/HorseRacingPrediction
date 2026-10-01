# Delivery duration and prompt-process audit

Date: 2026-09-30

Scope: retrospective of the collection-starvation response through the successful deployment workflow on SHA `90d3e6121ea06f2553e55a4e929821f4fe971b47`. This is an evidence audit and a narrow reusable worker-prompt correction. It does not change the still-open production acceptance status.

## Exact observed facts

The initial incident-record commit is `e38f9801` at 2026-09-28 10:01:38Z. The first source-push commit, `0805e0d4`, has commit timestamp 2026-09-29 11:45:07Z, an interval of **25h43m29s** from that record. Its Actions runs were created at 11:47:53Z, 2m46s after the commit timestamp; commit time is therefore the measured endpoint for the requested record-to-first-push interval, not a claim about the exact remote push event time. The successful `app-deploy` run `36579828265` for SHA `90d3e6121ea06f2553e55a4e929821f4fe971b47` ended at 2026-09-29 14:16:37Z, **28h14m59s** after the record commit. Its recorded run duration was **12m39s**.

The ten incident-sequence Actions runs had zero observed queue delay (`startedAt - createdAt = 0` for each). Durations below are the GitHub run interval (`updatedAt - startedAt`), not summed job time or human/agent active time.

| Run | Workflow | SHA prefix | Duration | Result |
| --- | --- | --- | ---: | --- |
| [36563971396](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36563971396) | app-ci | `0805e0d4` | 7m12s | success |
| [36563971496](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36563971496) | app-deploy | `0805e0d4` | 5m25s | failure: Terraform fmt gate |
| [36565694641](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36565694641) | app-ci | `ddbc7482` | 7m26s | success |
| [36565694699](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36565694699) | app-deploy | `ddbc7482` | 11m21s | failure: CloudWatch dashboard JSON apply |
| [36570684244](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36570684244) | app-ci | `bfb14e08` | 7m23s | success |
| [36570684311](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36570684311) | app-deploy | `bfb14e08` | 36m42s | cancelled after bounded investigation of a non-returning validation helper |
| [36577651868](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36577651868) | app-ci | `9dd0324f` | 7m07s | success |
| [36577651833](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36577651833) | app-deploy | `9dd0324f` | 7m46s | failure: Terraform console child timed out on Ubuntu |
| [36579828330](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36579828330) | app-ci | `90d3e612` | 6m56s | success |
| [36579828265](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36579828265) | app-deploy | `90d3e612` | 12m39s | success |

The 16 existing `agent-audits/*.json` artifacts report an aggregate **13 retries, 11 Lead corrections, and 11 review passes**. These are audit counters as recorded in those artifacts, not durations. In those artifacts, observed model, total-token usage, reviewer usage, and elapsed-time telemetry are null/unavailable; audit metadata requests `gpt-6-luna` / high for the delegated work but does not establish which model actually ran. The artifacts do not provide active human review minutes. No token, currency, or human-time estimate is made here.

## Bounds and interpretation

The 25h43m29s and 28h14m59s values are elapsed calendar intervals across commits, local work, review, waits, CI, and deployment. They do not establish that an agent or person worked continuously for those periods. Zero Actions queue delay rules out queue waiting in these ten runs; it does not measure time spent before a run was created.

The records establish concrete avoidable contributors: local Terraform formatting was not run with the workflow-resolved Terraform version before the first push; the dashboard check did not validate the consumer's nested metric-row schema and exact semantic set before apply; the initial structural helper used captured, interactive console input and lacked effective child progress/bounds; and Windows success did not establish Ubuntu behavior for a platform-sensitive console helper. These failures account for rework and successive deploy attempts, but the available evidence does not support assigning a fraction of total elapsed time to each.

Other work was necessary or intentionally gated: reconstructing and testing the starvation invariant; closing F8–F14 across queue behavior, capacity accounting, exact dimensions, future-ready anomalies, snapshot consistency, back-pressure, and database contention; retry-safe deployment and post-run checks; independent Lead review; and the explicit schema-19 rollback-risk decision. The audit does not classify this safety work as wasted time. The pause remained in place after deployment, so production progress and prevalence are still unverified; this audit does not close T1 or T5.

## Prompt-preventability matrix

“Prompt-preventable” means a better dispatch contract could require an earlier observable check or surface an explicit blocker. It does not mean a prompt can guarantee external service behavior or remote success.

| Finding | Evidence and effect | Prompt-process correction / limit |
| --- | --- | --- |
| T4b F8 | Telemetry queries/publication and completion lookups could affect dispatcher/acquire/completion progress. Review passes returned the missing critical-path resilience checks. | Require consumer-path proof that telemetry failure, delay, saturation, and shutdown cannot block business operations; require bounded queue and query/publish timeouts plus injected failures. This was added to the T4b acceptance evidence. |
| T4b F9 | Capacity telemetry counted rows differently from the store's envelope-slot admission predicate. | Require comparison to the consumer's actual semantic unit (distinct in-flight envelopes plus leases and legacy envelopes), with multi-task-envelope and legacy counterexamples. A plausible count or row total is insufficient. |
| T4b F10 | Dashboard/alarm queries requested dimension sets that did not exactly match emitted series. | Require a complete producer-to-consumer metric schema map and exact set comparison of metric name plus dimension names/values for every query. |
| T4b F11 | Ready-without-current-outbox metrics excluded future-available tasks, hiding an anomaly. | Require separate exact predicates for anomaly and eligible-work metrics, with a future-available Ready counterexample. |
| T4b F12 | Snapshot fields could come from different database states. | Require one coherent read boundary and a concurrency counterexample that would expose mixed-time aggregates. |
| T4b F13 | Remote publishing could add unbounded latency, memory pressure, or shutdown delay. | Require nonblocking enqueue, bounded capacity/batch/timeouts, safe queue-full behavior, and deterministic saturation/timeout/shutdown checks. |
| T4b F14 | Snapshot work shared the store gate and materialized backlog-sized rows before filtering/grouping. | Require bounded consumer-level output, independent coherent read state, SQL-side filtering/aggregation, and both a large-backlog and blocked-reader concurrency counterexample. |
| Terraform fmt | The first deployment run stopped at `terraform fmt -check -recursive`; the then-current local process had not checked with the exact workflow Terraform version. | Before push, name the exact formatter, version, command, and runner OS and require parity evidence. If an exact tool/OS is unavailable, report that as an unverified blocker rather than implying the gate passed. |
| CloudWatch flatten / F16 | Apply returned 576 schema errors because recursive `flatten` erased metric-row arrays. A row-count check alone would not prove rows were valid. | Validate the generated artifact against the consumer schema and exact canonical semantic set; include malformed shape, changed dimension, duplicate, and omitted-row counterexamples. The AWS API remains the final external validation. |
| Hung console / F17 | The helper produced no completion output while capturing child stdout/stderr; the run required a bounded cancellation and the exact active child was unclear. | Require noninteractive child invocation, explicit stdin/EOF behavior, streamed or stage-labeled progress, per-child timeout, process-tree termination, and an outer workflow bound. |
| Ubuntu console / F18 | The interactive console child timed out on Ubuntu after passing locally on Windows. | Require execution on the workflow runner OS for platform-sensitive helpers before treating local success as remote parity. If that runner is unavailable before push, retain an explicit unverified status. |
| Rollback review | The record surfaced that schema 19 was incompatible with deployed `ab1cda45` and no compatible pre-fix pair was evidenced. The user explicitly accepted this residual risk for this deployment; the deployment was authorized with the limitation visible. | Earlier compatibility evidence can make the decision concrete sooner, but cannot remove the technical constraint or substitute for user disposition. Preserve rollback compatibility as an independent deployment gate and record accepted risk separately from verification. This was a necessary safety decision, not an avoidable CI defect. |

## Reusable worker-prompt clauses

For work that changes declarative infrastructure or generates an artifact consumed by another system, include these clauses in the bounded worker prompt:

> **Consumer contract:** Identify the exact downstream consumer and validate the generated artifact at that boundary. Check parsed semantics against the consumer schema and an independently defined exact expected set; string matching, a successful local render, row counts, or cardinality alone are insufficient. Include malformed-shape, wrong-field/dimension, duplicate, and omission counterexamples where applicable.
>
> **CI parity:** Reproduce the repository workflow's exact formatter/tool command, pinned or resolved version, and runner OS. Record command and version evidence. If exact parity cannot be run, report it as an explicit unverified blocker before push; do not imply local success proves the workflow gate.
>
> **Child process behavior:** Run child tools noninteractively with explicit stdin/EOF behavior. Stream or emit stage-labeled progress, bound each child and the outer step, and terminate the child process tree on timeout. Include a deliberately stalled child counterexample.
>
> **Platform-sensitive helpers:** When the helper depends on shell, pipes, path, process, or console behavior, execute the same validation on the workflow runner OS before handoff. If that runner is not available, preserve the remote result as unverified.

The skill update adds this gate only for declarative/generated infrastructure work. It points to the existing CI parity section for general workflow parity rules rather than copying them.

## Lightweight forward test of the revised gate

| Scenario | Prompt contract applied | Expected routing / evidence |
| --- | --- | --- |
| Generated dashboard JSON, matching runner available | Require parsed consumer schema, exact metric/dimension set and malformed/duplicate/omission negatives; exact formatter/tool/version/OS; noninteractive bounded children with progress; run on the matching runner. | `Runnable` only with those checks assigned; local or string/row-count-only validation is insufficient. Matching-runner semantic test is required before `Verified`. |
| Ordinary C# refactor with no generated infrastructure artifact | Infrastructure-artifact condition is false; use the existing coding/test prompt contract and ordinary relevant CI checks. | Do not add the generated-artifact schema/set, child-process, or matching-runner gate. The task remains eligible for its normal route. |

This forward test checks that the new rule catches the demonstrated dashboard/runner failure class and remains conditional; it does not claim a new product test or change the task's production code.

## Verification and remaining status

- Read-only Git history and GitHub Actions run metadata support the endpoint timestamps, zero queue-delay calculation, and ten run intervals above.
- The 16 JSON audit files parse; their recorded counters sum to the totals above. Null telemetry remains unavailable.
- The change record remains `In progress` for T1/T5 production acceptance. This audit does not authorize or perform push, workflow dispatch, rollback, resume, or production access.
- Skill validation and diff/audit/route/status checks are recorded in the governing README task row after completion.
