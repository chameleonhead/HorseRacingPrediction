# 収集Workerの同一セッション・マイクロバッチ化

- Status: Approved
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-12
- Updated: 2026-09-12

## Context

現在の収集基盤はResource/Task/Attemptをレース単位で管理している。この粒度は部分再取得、Active重複防止、失敗箇所の特定には適切だが、本番実行も `SQS batch_size = 1`、`Records[0]`、1 taskごとの`IJraSessionFactory.CreateAsync`になっている。そのため同日・同開催のRaceCardが複数あっても、Lambda invocationとPlaywright browser初期化をレース数だけ繰り返す。

状態管理の粒度と実行資源の粒度を分離し、複数の独立Taskを同じLambda invocation・Playwrightセッションで処理する。

## Goals

- 同一日・同一提供元・互換性のあるCollectionDefinitionのTaskを、1回のLambda invocationでまとめて処理する。
- 1つのPlaywright/JRA sessionをバッチ内で再利用する。
- Task/Attempt/State/Locationはレース単位を維持し、各Taskを個別に成功・公開待ち・再試行・失敗へ確定する。
- Realtimeの遅延、Lambda 15分上限、サイト負荷を制御可能にする。
- ローカルSQLite queueでも同じbatch executorを使用する。

## Non-goals

- ResourceKeyやCollectionTaskを開催日単位へ変更しない。
- 12レースのうち1件の失敗で全件を再取得しない。
- RaceCard、Odds、Result、Horse等の異なる画面を無条件に同じbatchへ混在させない。
- Lambdaを常駐Workerへ変更しない。

## Experience and interaction design

- 管理画面では従来どおりレース単位で状態・Attempt・再取得を確認する。
- Task詳細に実行バッチIDとバッチ内順序を表示し、同じbrowser sessionで処理されたTaskを追跡可能にする。
- 運用画面に直近のbatch件数、1 invocationあたり処理Task数、browser起動時間、Taskあたり処理時間を表示する。

## Documentation updates

- `docs/changes/20260911_unified-collection-platform/README.md`: 状態管理粒度と実行batch粒度の分離、SQS/Lambda本番構成、検証結果を追記する。
- `docs/changes/20260911_unified-collection-platform/cutover-runbook.md`: batch部分失敗、timeout、再配信、運用メトリクスの確認手順を追記する。

## Technical impact

- API outbox dispatcherが互換Taskを`CollectionDispatchEnvelope`へまとめ、1つのSQS messageとして送信する。SQS message数による最大10件制限ではなく、設定可能なTask件数と256KB payload上限で制御する。
- SQS event source mappingは`batch_size=1`を維持し、1 message / 1 Lambda invocation / 1互換groupとする。
- Collector entryはEnvelopeの全Task参照を契約検証し、TaskごとのAcquire/Complete結果を集約する。
- JRA handlerに共有sessionを受け取れるbatch実行境界を追加する。既存単体handlerは1件batchとして同じ経路を利用する。
- 互換キーの初期仕様は `Provider + Definition + EffectiveDate` とする。RaceCard/Result/Oddsは別group、Horse/Jockey/Trainerも種別ごとに分ける。
- group内は同一sessionで逐次処理する。JRAへの並列アクセスは行わない。
- TaskごとにAcquire/Attempt/Completeを行い、1件のParse/Validation失敗を他Taskへ波及させない。
- browser/session自体が壊れた場合は、未処理TaskをTransientFailureまたはSQS部分失敗として返す。
- Lambda残り時間が安全マージン未満になったら未着手recordを処理せず再配信する。

### SQS・Task・Attemptの対応関係

| 層 | 粒度 | 正本・役割 |
|---|---|---|
| Resource / CollectionState | レース等の論理Resource単位 | 現在どこまで正しいデータを持つかの正本 |
| CollectionRequest | Resourceを取得する理由ごと | 初回、Discovery、Refresh、Recoveryを記録 |
| CollectionTask | Resource + Definitionの実行1回ごと | 状態遷移、priority、lane、dispatch generationの正本 |
| DispatchOutbox | CollectionTaskのdispatch generationごとに1行 | 未配送・配送予約・配送済みをTask単位で保持 |
| CollectionDispatchEnvelope / SQS message | 同一互換キーの複数Outbox行を束ねた配送1回 | TaskIdとdispatch generationの組だけを配送。業務状態の正本にしない |
| CollectionAttempt | AcquireされたTaskごとに1件 | 個別の開始・終了・結果・エラーの正本 |
| CollectionExecutionBatch | Lambda invocation内の互換Task groupごと | session共有と運用計測の相関情報。Task状態の正本にしない |

SQS recordには`messageId`があるが、Taskのidentityには使わない。Envelope IDとSQS message IDを配送記録へ、Attemptへ`ExecutionBatchId`と`LambdaRequestId`を記録し、SQS/Lambda/DBを相互追跡可能にする。receipt handleは短命かつ機密性があるため永続化しない。

### message確定規則

- Envelope内の各Task参照について、APIは`Acquired`、`AlreadyTerminal`、`SupersededGeneration`、`ActiveElsewhere`を区別して返す。現在の一律409応答は拡張する。
- `AlreadyTerminal`と`SupersededGeneration`は解決済みとしてskipする。Task状態は変更しない。
- handlerの業務結果をAPIへ正常にCompleteできたTaskは、結果がParseFailure等でも配送上は解決済みとする。必要なRecovery/retryはAPIのoutboxが新しいdispatch generationで生成する。
- Envelope内の全Task参照が、完了・retry登録済み・terminal・新generationへ更新済みのいずれかとして永続的に解決した場合だけ、SQS messageを成功応答して削除する。
- `ActiveElsewhere`、API通信失敗、Complete結果不明、Lambda timeout時の未着手Taskが1件でも残れば、そのEnvelope message IDを`batchItemFailures`へ返す。再配信時は解決済みTaskをskipし、未解決Taskだけを処理する。
- 未対応version・破損EnvelopeはTaskを推測せず`batchItemFailures`へ返し、SQS redrive policyでDLQへ送る。
- APIがretryable completionを受理した場合、元Envelope内のTask参照は解決済みとし、API outboxが増分したdispatch generationを後続Envelopeで送る。元messageの再配信とAPI retryを二重に使わない。

### leaseと部分失敗

EnvelopeのTaskを一括Acquireして長時間保持せず、共有session作成後に各Taskを処理直前でAcquireする。Taskごとの完了直後にCompleteを送信し、Envelope末尾でまとめて確定しない。長いTaskではheartbeatを継続する。Lambda終了が迫った場合は新規Taskを開始せず、未着手Taskを未解決としてmessage再配信へ残す。報告不能時はlease expiryとgeneration更新をWatchdogへ委ねる。

SQS event source mappingの`ReportBatchItemFailures`を実際に機能させるため、custom runtime bootstrapはCollectorが出力したEnvelope message IDをLambda success responseの`batchItemFailures`へ返す。現在の「成功なら空配列、失敗ならinvocation全体error」は廃止する。

### batch形成規則

Outbox dispatcherがDB上のTask情報から互換groupを形成する。初期の互換キーは`Provider + Definition + EffectiveDate + Lane`とし、Envelopeには互換キーと各`TaskId + DispatchGeneration`を格納する。CollectorはAcquire結果がEnvelopeの互換キーと一致することを再検証し、SQS payloadだけを信用しない。同日12 RaceCardは設定上限が12以上なら1 Envelope、1 invocation、1 browser sessionで処理できる。

Outboxのpriority/lane配分は従来どおり維持する。dispatcherはfair allocatorが選んだ先頭Taskを基準に、同じ互換キーのOutbox行だけを上限まで追加する。Envelope内はpriority、created time、TaskIdで安定sortする。異なるlaneを同じEnvelopeへ入れず、Realtime優先とBackground starvation防止を維持する。

Envelope作成時はOutbox行を短いreservation leaseで確保し、複数API instanceが同じ行を別Envelopeへ束ねないようにする。SQS Send成功後の配送済み更新は対象Outbox行を1 transactionで確定する。Send成功後にDB更新が失敗した場合は同じTaskが別Envelopeで再送され得るが、dispatch generationとAcquire guardによりAttemptは重複生成しない。

## Decisions

- SQS messageとTaskを1対1にはせず、1 Envelope message対複数Taskとする。ただしEnvelopeはTaskId/generationの配送集合に限定し、巨大な「1日分Task」にはしない。個別の優先度、再取得、Attempt、URL fallbackはTask側に維持する。
- 一括化はOutbox配送時と実行時だけ行う。同日12レースを1 invocationへまとめられる一方、Task状態の正本は統合しない。
- 最初から過度に大きなbatchにはしない。実測したp95時間とLambda残時間を基に、RaceCard/Result/Odds別の上限を設定可能にする。
- Realtime Oddsは待ち時間を優先し、batchが満杯になるまで待たない。RaceCard/Result/Backfillは数秒の集約待ちを許容する。
- Lambda containerのwarm reuseだけには依存せず、1 invocation内でbrowser sessionを明示的に共有する。
- Envelope外のTaskを「ついで取得」しない。Taskと配送Envelopeの所有関係を崩さず、batch化はSQS messageに明記されたTask参照に限定する。
- `CollectionExecutionBatch`は観測用Projectionであり、個別Taskの成功条件やActive制約を置き換えない。
- 未来開催の未公開分類は[未来開催レース探索の公開待ち扱い](../20260912_future-race-discovery-waiting/README.md)で別途修正する。マイクロバッチ化によって未公開ページを障害扱いする現在の問題を隠さない。
- SQS messageの配送結果と運用上の障害解決状態は[収集障害の対応状態ライフサイクル](../20260912_collection-failure-lifecycle/README.md)に従って分離する。message ackやSNS PublishedだけでFailureを解決済みにしない。

## Acceptance criteria

1. 同日RaceCard 12件を1 DispatchEnvelope/SQS message/1 invocationで受け、Playwright session factoryの呼出しが1回になる。
2. 各Taskに独立したAttemptが作られ、9件成功・1件ParseFailureを個別に確定できる。
3. 1件のAcquire conflictまたは重複配信が他11件の処理を妨げない。
4. session初期化失敗は対象groupをretryableとし、永続的なResource failureにしない。
5. 実行途中のsession破損後、未処理Taskは再配信またはretryable状態となり、処理済みTaskは重複完了しない。
6. Lambda残り時間不足時は未着手Taskを含むEnvelope messageを再配信し、再配信時は完了済みTaskをskipする。
7. RealtimeがBackgroundに埋もれず、既存のstarvation防止を維持する。
8. ローカルqueue実行も複数messageを取得し、同じbatch executorでsessionを共有する。
9. 1件実行は既存と同じ結果になり、Direct URL、fallback、Location検証を維持する。
10. Envelope最大Task数、Outbox集約猶予、definition別最大件数を設定可能にする。
11. 同日12 RaceCardの試験でbrowser起動回数、総時間、Task成功率を記録し、単件方式との比較をchange recordへ残す。
12. SQS/LambdaのEnvelope再配信を自動テストし、全Task参照が永続的に解決したmessageだけが削除される契約を検証する。
13. APIがretryable completionを受理した場合、旧generation messageは成功応答となり、新generation messageだけが後続実行を担当する。
14. 古いgenerationのSQS再配信はAcquire conflictでackされ、新しいAttemptを作らない。
15. batch途中でLambdaが終了しても、Complete済みTask、retryable完了済みTask、未着手recordを混同しない。
16. AttemptからExecutionBatchId、SQS message ID、Lambda request IDを確認でき、運用画面から同一batchのTaskへ遷移できる。
17. Envelopeに含まれないTaskをbatch executorが先取りしない。
18. 同じOutbox行が複数dispatcherに予約されず、Send成功・DB更新失敗による重複EnvelopeでもAttemptを二重生成しない。
19. DLQ reconciliationはEnvelope内の未解決Taskだけを対象とし、完了済みTaskをFailedへ戻さない。

## Delivery plan

1. 現在のSQS event/bootstrapとTask executorの複数record契約テストを追加する。
2. session共有可能なbatch handler境界とTask別完了処理を実装する。
3. RaceCardから適用し、Result、Odds、subjectへ展開する。
4. ローカルqueueを同じbatch executorへ接続する。
5. timeout、duplicate、partial failure、session crashを検証する。
6. 同日12レースで単件方式とbatch方式の速度・browser起動数・網羅性を比較する。
7. TerraformのSQS batch設定を変更し、本番smoke後にメトリクスを確認する。

## Verification record

- 2026-09-12: 現行本番経路を調査。Terraformは`batch_size=1`、Collector entryはSQS `Records[0]`のみを読み、Worker clientは1通知だけAcquire/Completeする。RaceCard/Result/Odds/subject handlerはいずれもTask内で`IJraSessionFactory.CreateAsync`を呼ぶため、Resource単位の実行ごとにPlaywright browserを生成している。
- 2026-09-12: 配送Envelope、互換キーによるgroup形成、Task件数・definition別件数・payload上限、Outbox予約lease、複数Outbox行の一括配送確定、SQS message ID記録を実装した。SQLiteで予約中の行が別dispatcherから不可視になり、期限後に再予約でき、予約所有者だけが配送確定できることを検証した。
- 2026-09-12: custom runtimeはCollectorが生成した`batchItemFailures`をLambda success responseとして返す。正常Envelope、Task途中失敗、未対応version、残時間不足、複数SQS recordの一部破損を外部サービスなしで検証し、マイクロバッチ関連Collectorテスト10件が成功した。
- 2026-09-12: API/Collector本体のRelease buildは警告0・エラー0。Outbox dispatcher、DLQ reconciliation、Envelope end-to-endの対象テストは成功した。未来開催Discoveryの期待値変更は別change recordで検証中であり、本変更の結果には含めない。
- 2026-09-12: schema v6を追記し、TaskのAcquire時にAttemptへ`ExecutionBatchId`、`DispatchEnvelopeId`、SQS message ID、Lambda request ID、バッチ内順序・件数を保存するようにした。v4/v5のmigrationは変更せず、既存DBはv6へ前進適用される。Resourceの試行履歴から実行バッチ詳細へ遷移でき、同じバッチで実行された各Resourceの収集詳細へ移動できる。ローカルqueueは`local-{messageId}`を配送IDとして記録し、Lambda由来ではないことを区別する。
- 2026-09-12: 実行バッチ詳細に処理件数、開始・終了、所要時間、Envelope/SQS/Lambda識別子、Task別の順序・結果を表示した。集約された「直近batch件数・平均Task数・browser起動時間」のダッシュボードProjectionは、独立した永続集計モデルが必要になるため今回の最小実装には含めず、Attempt相関から確認できる個別batch表示を先行した。browser起動時間の集約表示はfollow-upとする。
- 2026-09-12: `CollectionPlatformStoreTests`（49件）と`CollectionLambdaInvocationTests`を含む対象テストが成功し、相関情報の永続化、同一batchの2 Task参照、SQS/Lambda識別子と順序の伝搬を確認した。`JobDetailComponentTests`（6件）が成功し、試行履歴からbatch詳細への導線、batch詳細からResource詳細への導線、運用識別子の表示を確認した。API/Collector Release buildは警告0・エラー0。

## Implementation deviation

- `CollectionExecutionBatch`専用テーブルは追加せず、Attemptの相関列を正本としてオンデマンドProjectionを構成した。個別Taskの状態管理を増やさずAcceptance 16を満たせるためである。
- 運用画面の集約メトリクスのうち、個別batchの件数・所要時間・Task結果は実装した。複数batch横断の件数、1 invocationあたり平均Task数、browser起動時間は計測イベントと保持期間の設計が必要なため未実装であり、今回の完了範囲には含めない。
