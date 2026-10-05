# 常駐サービスのループ調査

対象ソース:09566e95。コード・設定・本番状態の変更なし。登録条件とソース上の周期を調査し、実機の全サービス稼働・実効設定やプロセス別CPU計測の証明とは区別する。前の5秒間隔提案は未承認。

## API側の登録一覧

リポジトリ実装のHostedServiceは最大8種類:定期実行7、Channelイベント待ち1。DI登録はProgram.cs:149–205とCollectionBackgroundSchedulerRegistration.cs:12–16。BackgroundSchedulersEnabled既定trueで3種類、常時登録2種類、QueueEnabledで2種類、SQS選択で計測1種類。Watchdog/DLQには個別Enabledによる実行前returnがあり、登録件数と実際に動くループ数は同じとは限らない。標準デプロイはCollectionQueueEnabledtrue/SQS、実機の外部overrideは未確認。

| サービス | 方式・周期 | 役割・空振り時の仕事 | ソース |
| --- | --- | --- | --- |
| CollectionPlatformOutboxDispatcher | 定期、既定1秒待機 | lease回収、lane状態、配送候補検索、予約・SQS起動。毎周期に候補検索が先行する。 | CollectionPlatformOutboxDispatcher.cs:40–93 |
| CollectionScheduleService | 定期、1分待機 | task lease回収・定期収集予定からジョブ登録。期限判定前の全状態取得と候補別のactive task照会あり。 | CollectionScheduleService.cs:13–36; CollectionPlatformStore.cs:1224–1237 |
| CollectionPlanningScheduler | 定期、1分待機 | 定義更新・未充足revision等をジョブへ反映。予定登録とは責務が異なる。 | CollectionPlanningScheduler.cs:20–35 |
| CollectionPlatformWatchdogService | 定期、既定5分待機 | 期限切れtask lease回収。現行storeは再配送・dead-letter結果を0で返す。停止中はpipeline読取でreturn。 | CollectionPlatformOperationsServices.cs:15–38; CollectionPlatformStore.cs:3455–3478 |
| CollectionBackfillRecoveryService | 定期、5分待機 | 中断したbackfill展開を復旧。 | CollectionPlatformOperationsServices.cs:97–110 |
| CollectionPlatformDeadLetterReconciler | 定期、既定30秒待機 | SQS DLQメッセージの監査。通常cycleのstore操作はなく、空queueでもSQS問合せ。 | CollectionPlatformOperationsServices.cs:51–89 |
| CollectionPipelineAlertDispatchService | 定期、5秒待機 | pipeline状態を読み、所定の障害停止時のみ未送信通知をSNS送信。通常時も状態読取あり。 | CollectionPlatformOperationsServices.cs:121–151 |
| CollectionDispatchMetricQueue | イベント駆動 | Channelにデータが来るまでawait。到着後の50ms集約窓は空キューを20回/秒走査する周期ではない。snapshot仕事は送られたときだけ実行。 | CollectionDispatchMetricQueue.cs:103–154 |

周期は処理終了後の待機時間であり、各処理の実行時間が追加される。1秒設定を必ず毎秒1回実行と解釈しない。framework/Webサーバー内部ループ、有限foreach、リクエスト内の再試行、ブラウザーのページ走査はこのサービス件数に含めない。

## 別プロセス・画面側のループ

- PredictorにPredictionExecutionService1種類。EnabledとEnablePredictionExecution両方がtrueのときだけ回り、PredictionIntervalMinutes設定（最小1分）だけ処理後待機する（PredictionExecutionService.cs:34–69、Predictor/Program.cs:44登録）。標準appsettingsは60分、ソースoptions既定/開発appsettingsは5分。現在のLightsail composeはapi/caddyのみで、この予想サービスをLightsail常駐件数に加えない。これでリポジトリ独自HostedService定義は9種類。
- Collector/Program.cs:98–106のrunLocalQueueモードはHostedServiceではない別CLI常駐ループ1種類。ReceiveAsyncの15分引数はメッセージleaseであり、必ず15分待機するlong pollの証明ではない。空受信時1秒待機。本番Collector Lambdaのイベント呼出しにこのCLIループを混ぜない。
- 画面のPeriodicTimerは2種類:CollectionOperations.razor:188は30秒、Jobs.razor:283は15秒で、JobsはautoRefresh・非busy・実行中存在またはrunning/waiting画面条件で再取得する。各画面/接続ごとに追加取得が発生し得る。API常駐HostedService8件とは別枠。Weather.razorの500msは単発待機でループではない。

したがって件数は「API8 HostedService」「別Predictor1」「local Collector CLI1」「画面更新2」を分けて報告する。定義数の単純合計を本番同時実行数と表示しない。

## 確認済みの重複と候補

1. **同条件の二重検索は確認済み**。dispatcherが各周期で呼ぶ公開ReclaimExpiredExecutionLeasesAsyncは、期限切れExecutionLeasesをCountした後、private helperが同じpredicateをToListする。期限切れ0でも両方実行し、gateと即時transactionを取得する（CollectionPlatformStore.cs:1900–1914,4217–4228）。Countは計測用返値に必要だが、一覧件数から得られる可能性がある。返値・トランザクション・全callerを保つprovider回帰なしに削除しない。実CPU寄与率は未計測。
2. **配送容量が満杯でも候補検索が先行する**。DispatchOnceAsync:74–93はreclaim/lane/pending検索を先に実行し、その後予約時にcapacityを検証する。1 in-flight envelope構成で長い実行中に空振りとなる候補処理の削減余地。ただし先行チェックは最終atomic予約を置換できない。実際に無効だった回数やコストは未計測。
3. **task lease回収の重複は確認済み**。schedulerのReclaimExpiredLeasesAsync（1分）とwatchdogのRunWatchdogAsync（5分）は、同じReclaimExpiredAsyncを呼ぶ。現行watchdogは回収件数以外を0で返し、旧来の再配送/dead-letter判定は行わない（store:1240–1256,3455–3478）。両方有効な構成では重複するが、scheduler無効時のwatchdog独立復旧経路を残す必要がある。dispatcherのexecution-envelope lease回収は別のlease種別なので同一視して削除しない。
4. **通常時の通知確認は空振りが多い構造**。5秒ごとのpipeline状態読取は障害通知が必要でない間も続く。状態変更通知を主経路・周期確認を再送保険にする案はあり得るが、多instance/再起動/未送信復旧を保つ設計が必要。通知機能自体は必要。
5. SchedulerとPlannerの同じ1分周期、metricの50ms集約は、周期の数字だけで重複・busy loopと判定しない。タスク登録、revision反映、待機・集約は別の責務。

6. **schedulerの取得上限がDB側で効いていないことは確認済み**。GetDueStatesAsyncはCollecting以外のstate/resource行をToListし、NextCollectionAtの期限判定・並び替え・Take既定500をメモリーで行う（store:1224–1237）。毎分呼ばれ、返却500件でもDB取得は500件に制限されない。さらにpolicy.ShouldCollect候補ごとHasActiveTaskAsyncを呼ぶ（ScheduleService:18–23）ため、件数に応じて個別検索が増える。期限/上限をSQL側へ移して同じ予定・順序・500件結果を保つのが有力候補で、NextCollectionAtインデックスの追加だけでは現在のメモリー判定を高速化できない。実行計画・結果等価性・実データ件数/CPU計測なしに利益量を断定しない。

結論:不要と確定した常駐サービス全体はまだない。一方、同一周期内の重複DB検索は存在する。「全部の周期を遅くする」より先にこの重複とcapacity-full時の仕事を減らす候補を評価する。前のinterval/index案に勝手に実装範囲を追加せず、必要な設計・実caller回帰・承認を経る。

## 調査・受入れ

Requested既存gpt-6-luna/high read-only調査、observed model/token telemetry unavailable。Leadが登録一覧をCodeGraph後の全src/tools AddHostedService/BackgroundService/IHostedService検索で独立照合し、lease二重検索、scheduler/watchdogの同一core、dispatcher順序、GetDueStatesのメモリー判定を現行sourceで確認。Collectorの15分を空queue待機とするworker報告は、ReceiveAsyncのvisibilityTimeoutと空SELECT時即returnの現行sourceで反証し不採用。Predictor60分も標準設定に限定し、source既定5分と区別した。旧watchdog役割の初期記述も現行return(reclaimed,0,0)に訂正。既存の独立検証gateが検出した局所的な証拠誤りであり、未確認内容を最終結果へ採用しない。テスト変更・build/testはread-only調査なので実施不要で、成功した実装検証とは表現しない。ソース制御のdirty状態に本タスクの実装ファイル追加なし。実CPU・live SQL profileは未取得のまま明示する。
