# DB主導の収集タスク即時実行

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-16
- Updated: 2026-09-16

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Implemented | schema v14、wake-only dispatcher、2段階execution lease、Worker API、DLQ監査を実装。 |
| Verification | Verified | Release build警告0、非External 1,037件成功・1件skip。 |
| Deployment/operation | Verified | run 35107576810でconcurrency 1のまま配備。health 200、pipeline active、Running 1を確認。 |

## Context

現行SQS messageはTask IDとgenerationを持つ実行指示であり、SQS visibility、DB Outbox、Task状態の三者が処理枠と再試行へ関与する。API 502ではDBがReady・画面がRunning 0でも、不可視messageが長時間残り後続を止めた。将来の並列化にはSQSを残すが、正しさをSQS状態へ依存させない。

## Goals

- DBをTask、優先順位、batch、処理枠、lease、再試行の唯一の正とする。
- SQSはLambdaを起こすwake通知だけに限定し、消失・重複・遅延してもTask状態を壊さない。
- `ExecutionSlots=1` では厳密に1 Envelope、将来Nへ上げれば最大N Envelopeを並列実行する。
- 正常完了後は待機Taskがあれば通常1秒以内、取得前502からは60秒以内に再起動する。
- 現在のEnvelope内batchとブラウザーセッション共有を維持する。

## Non-goals

- 今回の本番並列度は1から増やさない。
- Snapshot-firstやスクレイピング内容を変更しない。
- SQSを完全廃止したり複数queueへ分割したりしない。

## Experience and interaction design

管理画面のRunningはDBでexecution leaseを取得したTaskだけを表す。SQS不可視件数は処理中表示にも処理枠にも使用しない。pause中はacquire-nextが仕事を返さず、resume時にwakeを発行する。通常運用でpause/resumeによる復旧を不要にする。

## Documentation updates

- 実装時に `docs/01-lambda-collector-architecture.md` を更新し、DB主導acquire、wake-only SQS、slot/lease、障害回復をcanonicalにする。
- 現時点は提案のみのため、canonical文書は変更しない。

## Technical impact

```text
API/DB                                      Lambda
Ready Task                                   idle
  ↓ slotに空きあり                            ↑
wake tokenをSQSへ送信 ────────────────────────┘
                                            POST acquire-next
DB transactionで優先Taskをbatch化・slot取得 ──→ execution batch
                                            Playwright/API登録
                                            POST complete-batch
slot解放 → 次のwakeを即時送信
```

- SQS bodyはTask IDを含まないversion付きwake tokenにする。messageは実行命令ではない。
- Lambdaはwakeを受けるたびAPIの `acquire-next` を呼ぶ。APIはtransaction内でpause、空きslot、優先順位、互換batchを評価し、仕事がある場合だけEnvelopeとexecution leaseを返す。
- 空きがない、仕事がない、重複wakeの場合はNoWorkを返す。LambdaはSQS recordを成功応答でackする。
- acquire APIの502・接続失敗もwakeをackする。同じSQS messageを再試行しない。DB状態は変わっていないため、API復旧時のstartup scanまたは1秒周期scanが新しいwakeを送る。
- scanは `空きslot数 - 有効wake lease数` だけwakeを送る。wake leaseは30～45秒で失効し、SQSの可視・不可視件数を参照しない。
- acquire成功時は45秒の`StartPending`だけを作る。Lambdaが応答を受けて冪等な`start-batch`を送った場合だけ長い`Running` leaseへ移し、応答喪失時は短時間で回収する。`StartPending + Running <= ExecutionSlots` をDB immediate transactionで保証する。
- Envelope内の個別Task完了ではslotを解放せず、全Task終了または `complete-batch` で一度だけ解放する。
- Lambda hard timeout 15分より前にsoft deadlineを設け、残時間不足なら次のbatch内Taskを開始しない。実行中の強制終了では厳密な上限を守るためexecution lease満了後に回収する。
- SQS BatchSizeは1のまま、並列度はExecutionSlotsとLambda reserved concurrencyを同値にして増減する。
- SQS DLQはmalformed wakeやLambda runtime crashの監査用であり、Task失敗、slot解放、再試行の根拠にしない。
- wakeは冪等でDB scanが再発行できるため `maxReceiveCount=1` とし、同じ異常wakeを繰り返さない。
- SQSだけで復旧できない事態への安全網として、API startup scanと周期scanを必須にする。EventBridge等の追加定期起動は本番観測で必要性が示された場合だけ別変更とする。

## Decisions

- SQSはwake hintであり、durable work itemではない。DBに存在しない仕事をSQSから復元しない。
- 同じwake messageは再試行しない。既知のAPIエラーもackし、DB scanが新しいwakeを作る。
- DB commit後にacquire応答だけ失われる曖昧な502を考慮し、Playwright開始前の`start-batch`を必須にする。start合図がないbatchは45秒で期限切れとなる。
- slotはDBだけで厳密管理し、SQS visibilityや概算queue depthを容量判定に使用しない。
- 優先順位とbatch groupingはSQS送信時ではなく `acquire-next` 時に決め、古いqueue backlogで高優先度Taskが塞がれないようにする。
- 現在はslots/concurrency 1。将来Nへ変更するときも同じAPI、状態遷移、SQS契約を使う。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | slots 1ではDispatching＋Runningが最大1、Nでは最大Nとなり、複数dispatcher/Lambdaの同時取得でも超過しない。 | T1-T3 | concurrent DB/integration test | Verified |
| AC2 | 正常完了でslotを解放すると、待機Taskがあれば通常1秒以内に次のwakeを送りRunningへ進む。 | T1-T3 | completion-to-next E2E | Verified |
| AC3 | acquire-nextの502ではwakeをackし、API復旧後60秒以内に新wakeから処理を開始する。 | T2-T3 | 502/restart E2E | Verified |
| AC4 | wakeの消失、重複、遅延、順序逆転がTask/Attempt/API登録を重複・欠落させない。 | T1-T3 | transport fault E2E | Verified |
| AC5 | 優先順位とbatch groupingをacquire時に決め、既存のrace-day/weekend/definition batchと公平性を維持する。 | T1-T3 | scheduler/dispatcher regression | Verified |
| AC6 | batch内の個別Task完了ではslotを解放せず、Envelope終了時に一度だけ解放する。全terminal/supersededもslotを残さない。 | T1-T3 | mixed-result batch E2E | Verified |
| AC7 | pause、NoWork、RetryWaiting、既存execution lease中には余分な実処理を開始しない。 | T1-T3 | state transition tests | Verified |
| AC8 | hard timeout前に新Task開始を止め、強制終了後はexecution lease満了まで並列上限を守って回収する。 | T2-T3 | soft-timeout/crash E2E | Verified |
| AC9 | ExecutionSlots、Lambda concurrency、SQS BatchSize 1の設定不整合を配備または起動時に拒否する。 | T2-T4 | config contract/terraform test | Verified for current slot 1 |
| AC10 | DLQ messageはTask状態やslotを変更せず監査対象となり、通常処理の継続を妨げない。 | T2-T4 | DLQ reconciliation test | Verified |
| AC11 | 管理画面Running、DB execution lease、Lambda実行ログが一致し、待機あり・空きslotあり・Running 0の停滞を検知できる。 | T3-T4 | API/log/production observation | Verified |

## Delivery plan

1. DBへwake leaseとEnvelope execution slotを追加し、原子的な `acquire-next` と `complete-batch` を実装する。
2. dispatcherをTask固有Envelope送信からwake-only送信へ変更し、完了時起床・startup scan・1秒周期scanを接続する。
3. Collectorをwake受信→acquire-next→batch実行→complete-batchへ変更し、502/NoWorkをackする。
4. slots 1/N、batch、502、通知障害、timeout、crash、DLQのE2Eを追加する。
5. canonical文書とTerraform/configを更新し、slots/concurrency 1のまま段階配備する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | DB slot/wake leaseとacquire-next/complete-batchを実装 | Main | High capability | Approval | Store/API/contracts | concurrency/state tests | AC1, AC5-AC7証拠 | Verified |
| T2 | wake-only dispatcher、Collector、設定を接続 | Main | High capability | T1 | API/Collector/Terraform | transport tests | AC2-AC4, AC8-AC10証拠 | Verified |
| T3 | 障害注入E2Eと監視を追加 | Main | High capability | T1-T2 | tests/diagnostics | CI/時系列 | AC1-AC11証拠 | Verified |
| T4 | architecture更新と段階配備 | Main | High capability | T1-T3 | docs/deployment | 本番観測 | deploy run/metrics | Verified |

## Review gates

- **Design and task-split review (2026-09-16)** — Task固有SQS messageを正とする構造がvisibility、Outbox、DB Taskの三重状態を生むことを確認した。SQSをwake hintへ限定し、DB transactionだけでslotと仕事を決定する構成が、厳密な現在1枠と将来N枠を両立する最小の責務分離と判断した。batch途中解放、複数instance競合、502、通知消失、hard timeout、DLQ、設定不整合をAC1–AC11へ追跡した。判断: 承認待ち。
- **Pre-implementation review (2026-09-16)** — ユーザー承認を受領。共有する永続モデル/API契約をT1で先に固定し、T2–T4は依存状態とした。Mainがschema/API/統合を所有し、workerはread-only inventoryと独立テスト観点に限定する。
- **Checkpoint review (2026-09-16)** — 独立レビューでacquire commit後の応答喪失を検出した。ユーザーが提示した「短いLease内の作業開始合図」を明示的な`StartPending → start-batch → Running`へ具体化した。DB/SQS非原子性は送信前予約と期限回収、旧v1/v2 Envelopeはschema v14で現行Ready outboxを再開しCollectorでack、DLQはTask非変更の監査専用として解消した。
- **Final review (2026-09-16)** — AC1–AC10をStore/API/Collector/infraのテストへ追跡し、AC11を本番health、pipeline状態、Running/Waiting集計、DLQログへ追跡した。独立レビューのblockerだった曖昧な502、DB/SQS非原子性、旧Envelope、DLQ Task変更、複数writer競合を、StartPending、送信前予約、schema v14再開、監査専用DLQ、SQLite immediate transactionで解消した。未完了の承認済みTaskまたはblocking findingはない。

## Verification record

- 2026-09-16: 本番でwaiting 1,758、running 0、pipeline稼働、main SQS visible 0/not-visible 2、DLQ 0を確認した。
- 2026-09-16: Task固有messageがAPI再起動中のacquire 502でpartial failureとなり、SQS visibilityと送信済みOutboxが後続を止めたことを確認した。
- 2026-09-16: 現行Task leaseとLambda hard timeoutはいずれも900秒、SQS visibility 5,400秒、maxReceiveCount 3、BatchSize 1、reserved concurrency 1であることを確認した。
- 2026-09-16: 現行batchは1 Envelope内で複数Taskを順次acquireするため、slotを個別Task完了ではなくEnvelope lifecycleへ結び付ける必要があると確認した。
- 2026-09-16: schema v14でexecution leaseを追加し、同一wakeの冪等acquire、StartPending期限回収、complete-batchの冪等解放を集中テストで確認した。
- 2026-09-16: valid wakeのNoWork/502はack、malformed wakeだけpartial failure、legacy Envelopeは再実行せずackするテストを追加した。
- 2026-09-16: Collector集中テスト97件、API集中テスト15件が成功した。
- 2026-09-16: `dotnet format --verify-no-changes`、Release build（警告0）、非External回帰1,037件成功・1件skipを確認した。
- 2026-09-16: commit `7b0208b`をmainへpushし、app-ci run 35107576831とapp-deploy run 35107576810が成功した。
- 2026-09-16: 本番health 200。配備後inspect run 35108916105でpipeline active、waiting 1,594、running 1、recent 1,334、DLQ reconciliation errorなしを確認した。1分後のrun 35109073941でもrunning 1を維持し、処理枠1を超過していない。
- 2026-09-16: 配備直前backup `collection-platform-predeploy-20260916-142811.db`（16,195,584 bytes）が作成されたことを確認した。

## Deviations and follow-up

承認時の「acquireで即Running」を、応答喪失502でもAC3を満たすため`StartPending`と`start-batch`の2段階へ具体化した。これはユーザーが先に示した短いLeaseと作業開始合図を実装したもので、外部要件の変更ではない。Snapshot-firstは対象外のまま変更していない。
