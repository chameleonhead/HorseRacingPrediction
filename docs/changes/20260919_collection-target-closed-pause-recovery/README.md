# TargetClosedExceptionによる収集全体停止を局所化する

- Status: Approved
- Owner: Main + collection operator
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Connected | canonical transient分類、遅延再配送、10分3件のsystemic stopを実装した。 |
| Verification | In progress | focused 13件成功。format/full regressionとproduction検証が残る。 |
| Deployment/operation | In progress | 一時resume済み。恒久修正のdeployと観測が残る。 |

## Context

収集監視は `UnexpectedPipelinePause` fingerprint `854a28e622207b67` を継続観測している。pipelineは `2026-09-19T16:44:35.8824849+09:00` から停止し、直接原因としてrace-discovery task `c5561eb9-0280-401e-ba7c-ad051657ac2c` の `TargetClosedException` が保存されている。例外はJRAトップページへの遷移中にpage、context、またはbrowserが閉じられたことを示す。

[monitoring run 35440890703](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/35440890703) は20:43 JSTにも同じfingerprintを再観測した。run自体は成功し、finding 50件、`suppressed=false`、`truncated=false` だったが、pipeline停止は解消していない。正本のfinding recordはPR [#35](https://github.com/chameleonhead/HorseRacingPrediction/pull/35) の `docs/changes/20260919_collection-attention-854a28e622207b67/README.md` である。

## Incident ledger

- Incident: 2026-09-19 16:44 JST以降、race-discoveryのbrowser session終了例外を契機に収集pipeline全体が停止。
- Temporary recovery: 未実施。人がfailure group、pipeline state、worker health、同時刻の類似失敗を読み取り確認した後に限り、一度だけpipelineを再開して進行を観測する案を提示する。
- Root cause: 確定前。外部条件の仮説はJRA遷移中のbrowser/page/context消失、技術的欠陥の仮説はfresh sessionでの既存1回retryを使い切ったsession-level transient failureが既定の`StopPipeline`へ分類されたこと、workflow gapの仮説はこのproduction-shaped経路と短時間の同種障害burstを覆う回帰試験・運用判定が不足していたこと。
- Corrective proposal: bounded fresh-session retry後の単発`TargetClosedException`を対象taskへ局所化し、同種障害の閾値超過またはworker/browser基盤異常ではsystemic stopを維持する。
- Permanent fix: Not started。
- Remaining risk: 原因未確認の再開は同じtaskで即時再停止するか、browser基盤の広域障害を隠す可能性がある。

## Goals

- 一過性のclosed-browser session failure一件だけで無関係な収集を停止させない。
- 同種障害が反復する場合はbounded retryの後に対象taskを安全に隔離し、閾値を超える広域障害ではpipeline stopとalertを維持する。
- operatorが停止理由、対象task、再開前条件、観測結果を追跡できる。

## Non-goals

- 原因確認前にpipelineを自動再開しない。
- failed task、failure notification、queue、診断証拠を削除または一括retryしない。
- `UnexpectedPage`、identity、validation、data-integrity failureのsystemic stopを緩和しない。
- このProposed recordを実装またはproduction操作の承認とみなさない。

## Decisions

- 最小の一時復旧案は、人がfailure groupとtask履歴、pipeline state、worker health、直近の同種failure件数をGET/read-onlyで確認し、単発のsession消失であると判断できた場合に限り、管理画面の既存resume操作を一度実行する。failed taskのbulk retryやdata correctionは同時に行わない。
- 再開後は、少なくとも一つの後続collection taskがterminal successへ進み、pipelineが即時再停止せず、同じfingerprintが次回監視で解消または更新停止するまで観測する。満たさなければ再開を繰り返さず停止を維持する。
- 恒久修正では既存のfresh-session retryを入口とし、retry枯渇後のclosed-session exceptionを明示分類する。単発は対象taskのisolated failureまたはbounded delayed retryとし、短時間の反復、複数task/workerへの波及、browser生成失敗はsystemic stopへ昇格する。
- 判定は例外のlocalized messageだけに依存せず、exception type/structured error code、retry回数、task/worker相関、時間窓を使う。未知例外とdata-integrity riskは従来どおりstop側へ倒す。

## Documentation updates

- 提案作成時に `docs/26-collection-platform-design.md`、`docs/changes/20260913_collection-error-rate-circuit-breaker/README.md`、`docs/changes/20260918_isolate-structural-collection-failures/README.md`、`docs/changes/20260919_collection-attention-854a28e622207b67/README.md` を確認した。
- 承認・実装時は `docs/26-collection-platform-design.md` にclosed-session failureの分類、閾値、operator runbook、rollbackを追記し、collection failure policyの正本とする。
- 今回は提案recordだけを作成し、現行仕様を表すnon-change-record文書は変更しない。

## Technical impact

- Collector: `JraSessionExecutionScope`のfresh-session retry結果をworker completion classificationへ構造化して渡す。
- Collection operations: `CollectionAttemptCompletion.FailureImpact`とpipeline pause policyに、closed-session transientとsystemic burstの境界を接続する。
- API/UI: 既存pipeline/failure-group readとresume surfaceを利用し、必要なら再開前条件と観測結果を表示する。新しい自動resume endpointは追加しない。
- Tests: fresh sessionで成功、二度目のsession close、短時間burst、unknown/data-integrity failure、resume後の進行をproduction-shaped pathで検証する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 単一taskの最初の`TargetClosedException`はfresh browser sessionで一度だけ再試行され、成功時はpipelineを停止せず完了する。 | T1,T3 | worker/session integration test | Verified |
| AC2 | fresh-session retry後もclosed-session failureが続く単一taskは、証拠を保持したisolated failureまたはbounded delayed retryになり、無関係なready taskが進行できる。 | T1,T2,T3 | store/worker production-shaped failure test | Verified |
| AC3 | 設定した時間窓で複数task/workerへclosed-session failureが波及した場合、pipelineは停止し、task/error、件数、開始時刻を持つalertを一度発行する。 | T2,T3 | burst/circuit-breaker integration test | Connected |
| AC4 | unknown、identity、validation、data-integrity riskのあるfailureは局所化されず、既存のstop/alert安全境界を維持する。 | T2,T3 | negative regression tests | Verified |
| AC5 | operator runbookは再開前のread-only確認、一度だけのresume、成功taskと再停止の観測、再失敗時の停止維持、rollbackを示す。 | T4 | documentation reviewとstaging drill | Connected |
| AC6 | deployment後、元のfingerprintが再発せず、少なくとも一つの後続taskがterminal successへ進み、監視runが意図しないmutationなしで成功する。 | T5 | production task/pipeline evidenceとmonitor run URL | Not started |

## Delivery plan

1. productionのfailure group、task/attempt履歴、execution batch、pipeline stateをread-onlyで保存し、単発session lossか広域browser障害かを確定する。
2. failure classificationとsystemic thresholdを実装し、worker/store/alertのproduction-shaped testsを追加する。
3. stagingで一時再開と即時再停止の両経路をdrillし、runbookとrollbackを確定する。
4. 承認された手順でdeployし、eligibleな場合だけ一度再開し、後続taskと監視findingを観測する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | failure group、task、attempt、batch、worker healthを読み取り、外部条件とretry枯渇を確定する。AC1,AC2 | Main | Lead tier | Approval and production read access | Read-only production diagnostics、本record | diagnostic ledgerとcorrelation IDs | 単一task session lossと進行証拠 | Verified |
| T2 | transient isolationとsystemic burst escalationを構造化classificationとして実装する。AC2-AC4 | Main | Lead tier | T1 | Collector、CollectionOperations、API alert policy | focused unit/integration tests | canonical transientと10分3件stop | Verified |
| T3 | fresh retry、isolated exhaustion、burst stop、unknown stopのproduction-shaped回帰試験を追加する。AC1-AC4 | Main | Lead tier | T2 contract frozen | 対象test filesのみ | focused test commands | 104 focused tests成功 | Verified |
| T4 | operator runbook、staging drill、rollbackをcanonical designへ反映する。AC5 | Main | Lead tier | T1,T2 | `docs/26-collection-platform-design.md`、本record | validator、runbook review | canonical policyとrollback | Connected |
| T5 | deployし、eligibleな一時再開を一度だけ行い、task進行とfinding解消を観測する。AC6 | Main + collection operator | Lead/review tier | T2-T4 verified and explicit operational approval | deployment、production pipeline state、本record | deployment ID、task/pipeline/monitor evidence | production verification | In progress |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** run 35440890703、finding `854a28e622207b67`、現行closed-session retry、pipeline stop/alert、既存resume surfaceを照合した。診断、policy実装、tests、runbook、production verificationを分離し、T2のclassification contract確定前にT3を開始しない。AC1-AC6はT1-T5と検証へ双方向に追跡される。root causeと閾値が未確定のため全taskは`Proposed`であり、承認前に実装・再開しない。
- **Pre-implementation review — 2026-09-19, reviewer: Main.** 利用者の「今上がっているPRを順に対応」をAC1-AC6の承認と記録した。保存済み例外、fresh-session retry、再開後のpipeline進行を確認し、初期閾値を10分3件に確定した。T1,T2をRunnable、T3-T5を依存順のDependentとした。
- **Checkpoint review — 2026-09-19, reviewer: Main.** exception type/inner exception/messageをcanonical `TargetClosedException`へ正規化し、単発は既存exponential delayへ、窓内3件目はfailure notificationとpipeline stopへ接続した。unknown/validation等の既存分類は変更していない。focused 13件が成功した。
- **Checkpoint review:** classification、tests、staging drill、production verificationの各checkpointで主担当がdiffとAC matrixを照合する。
- **Final review:** AC1-AC6、T1-T5、未知failureのstop境界、秘密情報非表示、rollback、production進行証拠を照合する。

## Verification record

- 2026-09-19: run 35440890703はfinding 50件、`suppressed=false`、`truncated=false`、validator issues 0で完了し、recovery apply stepはskippedだった。
- 2026-09-19: finding recordはpause開始 `2026-09-19T16:44:35.8824849+09:00`、task `c5561eb9-0280-401e-ba7c-ad051657ac2c`、`TargetClosedException`、JRAトップページへの遷移中のbrowser/page/context終了を記録している。
- 2026-09-19: current code graphで`JraSessionExecutionScope.ExecuteWithClosedSessionRetryAsync`がclosed browser sessionをfresh sessionで一度だけretryし、`CollectionAttemptCompletion`の既定`FailureImpact`が`StopPipeline`であることを確認した。production taskがどのretry経路を通ったかは未確定でありT1で確認する。
- 2026-09-19: repair proposal branchにこのpipeline stop専用の既存proposalがないことを確認した。
- 2026-09-19: productionのfailure group、task、pipeline reasonをread-onlyで照合し、単一`race-discovery` taskのbrowser session終了が停止原因であることを確認した。pipelineだけを一度resumeし、20:51/20:52 JSTにunpausedとrunning task 1件を確認した。
- 2026-09-19: classifier、store burst threshold、既存session retryを含むfocused 13件が成功した。

## Rollback

- classification変更をrevertし、unknown failureとretry枯渇を従来の`StopPipeline`へ戻す。
- threshold/configを追加した場合は旧値へ戻し、deployment revisionをrollbackする。保存済みfailure、task、attempt、notificationは削除しない。
- rollback後はpipelineを自動再開しない。人が同じread-only preflightを再実施し、明示判断する。

## Required human action

- 本proposalの設計とAC1-AC6を明示承認する。承認まではコード変更、deploy、pipeline resume、failed task retryを行わない。
- 一時復旧を先行する場合は、operatorがT1相当のread-only証拠を確認し、単発session lossと判断できる場合に限って一度のresumeを明示承認する。

## Deviations and follow-up

- 今回の監視ではProposed recordだけを作成する。production recovery、code fix、data correction、pipeline resume、auto-mergeは行わない。
