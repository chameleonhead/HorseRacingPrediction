# 収集キューの実行容量連動ディスパッチ

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

収集APIはDB outboxから5秒ごとに最大10 EnvelopeをSQSへ送る。一方、本番Collector Lambdaの予約済み同時実行数は1であり、SQS event source mappingも1 messageずつ起動する。このため、実行できない後続Envelopeまで先にSQSへ積まれ、あとから到着したRealtime・高priority Taskが、既に投入済みのBackground・低priority messageを追い越せない。

Taskと優先順位の正本はDBにあるため、SQSを長期の待機場所にせず、実行枠が空く直前までDB outboxで待機させる。

## Goals

- 本番の実行多重度1に合わせ、未解決の論理Envelopeを原則1件だけdispatch対象にする。
- 1 Envelopeが永続的に解決した後にだけ、次のdue Task群を選びSQSへ投入する。
- Envelope選択のたびに既存のlane、priority、aging、公平配分を適用し、新しく到着した重要Taskを既投入の大量messageの後ろへ固定しない。
- API再起動、Lambda失敗、SQS再配信、lease切れ、複数API instanceでも二重投入や停止を起こさない。
- ローカルqueueでも同じ容量制御契約を使う。

## Non-goals

- Lambdaの予約済み同時実行数を1から増やさない。
- SQS event source mappingの`batch_size = 1`を変更しない。
- Task、Attempt、State、Envelopeの粒度や、互換Taskを1 Envelopeへまとめるマイクロバッチ方式を廃止しない。
- SQS側のmessage priority機能に依存しない。
- 実行中Envelopeを高priority Task到着時に中断しない。

## Experience and interaction design

- 通常時、SQSには実行中または直ちに実行される1 Envelopeだけが存在し、残りは管理可能なDB outboxで待機する。SQS送信成功・DB確定失敗の不確定区間では同一generationの物理messageが一時的に重複し得るが、論理実行は重複させない。
- 実行中Envelopeが終わると、dispatcherの次回周期（既定5秒以内）で最新候補から次を選ぶ。
- Realtimeを優先するが、Realtimeを4 Envelope連続で選んだ後はdue Backgroundを1 Envelope選ぶ既存規則を維持する。
- 同じEnvelope内ではsession互換キーと収集グループ別上限を維持する。Envelope選択はlane/priority/fairness、Envelope内は画面遷移コストを抑える開催場・レース番号・Resource種別の安定順序で逐次実行する。
- 運用者が停止したpipeline、DLQサーキットブレーカー、未来の`AvailableAt`は従来どおり投入を抑止する。

## Documentation updates

- `docs/26-collection-platform-design.md`: DB outboxを優先度付き待機場所とし、実行容量が空いた時だけSQSへEnvelopeを送る不変条件を追加する。本change recordが提案中であることを明記する。
- `docs/22-collector-design.md`も関連箇所を確認したが、別変更の未コミット編集が存在し、旧基盤と移行期の説明が中心であるため本変更では編集しない。Resource中心基盤の正本である`docs/26-collection-platform-design.md`へ集約する。

## Technical impact

- `CollectionQueueOptions`に最大未解決Envelope数を追加し、本番既定値を1とする。将来Lambda多重度を増やす場合は同じ値へ明示的に合わせる。
- `CollectionPlatformStore`に、現在generationのうちSQSへ配送済みで、まだterminalでもsupersededでもないEnvelope数を数える処理を追加する。
- outbox予約時に「未解決Envelope + 有効な配送予約」が上限未満であることを同一transaction内で再確認する。複数dispatcherが同時に空き枠を観測しても上限を超えて予約できないようにする。
- 容量を占有するのはEnvelope単位とする。同じEnvelopeの複数Taskを複数枠として数えない。
- Envelopeは、そのEnvelopeに含まれる現在generationのTask参照がすべてterminal、またはretry等で新generationへ進んだ時に解決済みとみなす。SQSの可視件数だけでは判定しない。
- 送信失敗時は短期reservation expiry後に再選択できる。送信成功後・DB確定失敗による重複は既存のgeneration/Acquire guardで業務実行を重複させない。
- dispatcherは空き枠数までしかEnvelopeを形成しない。枠が空くたび`CollectionLaneAllocator`で先頭Taskを選び、そのTaskが属するsession互換グループからEnvelopeを形成するため、lane/priority/agingはSQS投入前に最新候補へ適用される。
- watchdogがstalled Ready Taskを新generationへ進めた場合、旧Envelopeはsupersededとして枠を解放し、新generationだけが再投入候補になる。

### Session互換グループ

- レース系は`Provider + RaceDay + EffectiveDate + Lane`をsession互換キーとし、RaceCardとRaceResultを対象にする。同一開催日なら競馬場をまたいで同じEnvelopeへ入れる。時刻制約と再取得頻度が異なるRaceOddsは本変更では混在させず、従来どおりDefinition単位とする。
- レース系Envelopeの選択は先頭Taskのlane/priorityを既存規則で決める。Envelope内は開催場、レース番号、RaceCard、RaceResult、TaskIdの順で安定させ、同一レースの出馬表から結果タブへ移る近道を使えるようにする。RaceResultはRaceCardの業務完了を前提としないが、同一Envelopeに両方ある場合はナビゲーション効率のため隣接させる。
- 出走馬プロフィール系は`Provider + WeekendSubjects + weekendPriorityUntil + Lane`をsession互換キーとし、同じ週末の出走馬を同一browser sessionで連続処理する。既存のTask属性`weekendPriorityUntil`を永続キーとして使い、dispatch時の現在日付から推測し直さない。
- Jockey、Trainerや通常の過去馬プロフィールは、出走馬週末グループへ無条件に混ぜない。出走馬から発見され、同じ週末キーを明示的に引き継いだ対象だけを追加候補とする。
- グループが設定上限またはpayload上限を超える場合、安定順序で複数Envelopeへ分割する。未送信分はDB outboxに残し、先行Envelope解決後に他のlane/priority候補と再比較してから次を送る。
- 別開催日のレース結果は別グループとする。同じ日付内の競馬場横断処理を優先し、別日のEnvelopeは日付、lane、priority、agingによって順次選ぶ。
- 初期上限は`RaceDayMaxTasks = 24`、`WeekendSubjectsMaxTasks = 12`、従来Definition互換groupは既存`DefinitionMaxTasks`とする。1日3場のRaceCard 36件は24件+12件、RaceCard/Result 72件は最大24件ずつに分かれる。ただし後続分は先にEnvelope化・SQS送信せず、先行解決後に再選択する。
- payload上限250KBとLambda残時間チェックは件数上限より優先する。実測により初期値を変える場合は設定既定値と検証記録を同じ変更内で更新する。

### Envelope契約とWorker検証

- `CollectionDispatchEnvelope`をcontract version 2へ上げる。`CollectionDispatchCompatibilityKey`は単一`Definition`ではなく、`Provider`、`GroupKind`（`Definition` / `RaceDay` / `WeekendSubjects`）、`GroupKey`、`Lane`を持つ。
- `CollectionDispatchTaskReference`は`TaskId + DispatchGeneration`のままとし、DefinitionやpriorityをSQS payloadの正本にしない。Workerは各TaskをAcquireして得たDB正本からgroup keyを再計算し、Envelope keyとの一致を検証する。
- `Definition` groupの`GroupKey`はDefinition ID、`RaceDay`は`EffectiveDate`の`yyyy-MM-dd`、`WeekendSubjects`はTask属性`weekendPriorityUntil`の`yyyy-MM-dd`とする。
- Workerはversion 1と2を読み取る。version 1は従来の単一Definition互換として実行し、version 2だけが定義横断groupを許可する。未対応version・group key不一致は実行せずSQS部分失敗へ返す。
- `JraSessionExecutionScope`の互換検証を新group keyへ変更する。全JRA handlerが同じsingleton `IJraSessionFactory`を使用する既存DI契約を維持し、RaceCard、RaceResult、Horse profile間で1つのsessionを再利用する。

### Navigation route contract

- 「最適なルート」は、対象identityを検証できる経路のうち、既知の有効な`ResourceLocation`への直接遷移、現在画面内の短絡操作、トップ/カレンダーからの完全探索の順に選ぶことと定義する。短い経路がidentity不一致、リンク欠落、未公開、navigation例外になった場合だけ完全探索へfallbackする。
- 同一開催場・次レースのRaceCardは、現在のRaceCardからレース番号をクリックし、競馬トップ、カレンダー、開催一覧へ戻らない。
- 同一日・別開催場のRaceCardは、現在画面の開催場切替と対象レース番号を使い、競馬トップとカレンダーへ戻らない。
- 同一レースのRaceCard→RaceResultは現在画面の「レース結果」導線を使い、結果トップや開催選択から探索し直さない。到達ページのRaceIdが一致しない場合は既存のfull navigationへfallbackする。
- 同一日・同一開催場のRaceResult連続処理は、現在の結果画面から次レース番号を使う。別開催場では現在画面の開催選択を優先し、利用できない場合だけ結果トップへ戻る。
- 出走馬プロフィールは検証済み`ResourceLocation`があれば直接遷移する。同一週末Envelope内でも各Taskの氏名・生年月日・source identityを必ず再検証する。Locationがない、または不一致の場合だけ公式検索経路を使い、前Taskのプロフィール内容を再利用しない。
- route optimizationは結果の正しさより優先しない。各短絡テストは「期待操作が行われた」だけでなく、「不要な`NavigateAsync(KeibaTop/Calendar/ResultTop)`や検索操作が行われていないこと」と、最終page kind/RaceId/subject identityの一致を同時に検証する。
- 短絡失敗テストでは、誤ったRaceId、別開催場の同一レース番号、別subject、欠落リンクを与え、full routeへfallbackして正しい対象だけを保存することを検証する。

### 容量スロットの永続判定

- `CollectionQueueOptions.MaxInFlightEnvelopes`を追加し、既定値と本番値を1にする。`DispatchBatchSize`は削除せず、`min(空きスロット数, DispatchBatchSize)`を1周期の送信上限として後方互換を保つ。
- `CollectionPlatformStore.TryReserveDispatchesWithinCapacityAsync`を追加し、候補Outbox ID、reservation token、Envelope ID、現在時刻、reservation期限、容量上限を受ける。
- 同一SQLite write transaction内で、(a) pipelineが未停止、(b) 現在generationの未解決配送済みEnvelope、(c) 期限内reservation Envelopeを`EnvelopeId`のdistinct件数で数え、上限未満の場合だけ候補行を予約する。
- 配送済みEnvelopeは、Outboxの`DispatchGeneration`とTaskの現在`DispatchGeneration`が一致し、Taskが`Ready`または`Running`である行を1件以上含む間だけ未解決とする。Taskがterminal、`RetryWaiting`、`WaitingDiscovery`、または新generationへ進んだ行は枠を占有しない。
- reservationは`DispatchedAt is null`かつ`ReservedUntilUnixMilliseconds > now`のEnvelopeを枠として数える。期限切れreservationは枠を占有せず再予約可能とする。
- SQLiteの単一writerとtransactionにより複数processのcheck-and-reserveを直列化する。busy/競合は送信せず次周期で再試行し、transaction外で観測した空き数だけを根拠に送信しない。
- SQS送信成功後にDB確定できなかったreservationは期限まで枠を占有する。期限後の再送でEnvelopeが重複しても、既存Task generationとAcquire guardにより二重Attemptを防ぐ。
- Lambdaの`reserved_concurrent_executions=1`を実行多重度の最終防壁とする。DB容量制御は不要な先行投入を抑え、TaskのActive lease/generation guardは重複messageから二重業務実行を防ぐ。

### Fairnessの永続性

- 現行`CollectionLaneAllocator`の連続Realtime数はAPI process内メモリだけにあり、再起動で失われる。本変更では直近の現在generation配送履歴から、最後の非Realtime Envelope以降の連続Realtime Envelope数をDBで再構成して選択器へ渡す。
- カウント単位はTask数ではなくEnvelope数とし、同一Envelope内のTask数にかかわらず1回と数える。4 Realtime Envelope後にdue Backgroundを1 Envelope選ぶ。

### Implementation map

| Path | Concrete change |
|---|---|
| `CollectionModels.cs` | `CollectionDispatchGroupKind`、Envelope v2互換キーを追加し、`PendingCollectionDispatch`へResource属性を渡す。v1 deserialize互換を維持する。 |
| `CollectionPlatformStore.cs` | pending候補へ属性を投影し、未解決/予約Envelope数と連続Realtime数を求め、容量確認とOutbox予約を一transactionで行う。 |
| `CollectionExecutionContracts.cs` | allocatorへ永続化済み連続Realtime数を入力できるようにし、選択自体は既存lane rank、priority、agingを維持する。 |
| `CollectionPlatformOutboxDispatcher.cs` | 先頭候補からgroup kind/keyを決め、RaceDay 24、WeekendSubjects 12、Definition別上限でdue候補だけを、session内の最短遷移を可能にする順序へ安定sortしてEnvelope化する。空き容量がない周期は送信しない。 |
| `CollectionQueueOptions.cs` / `appsettings.json` | `MaxInFlightEnvelopes=1`、`RaceDayMaxTasks=24`、`WeekendSubjectsMaxTasks=12`を追加する。 |
| `CollectionPlatformWorkerClient.cs` / `JraSessionExecutionScope.cs` | AcquireしたTaskからv2 group keyを再計算し、Envelopeとの一致を検証して共有sessionを使う。v1検証経路も残す。 |
| `CollectionLambdaInvocation.cs` / local queue entry | v1/v2 Envelopeを受理し、既存の部分失敗、残時間、Task別Complete契約を両versionへ適用する。 |
| `JraNavigator.cs` / `JraNavigator.Subjects.cs` | 同一開催日・同一週末の現在画面短絡を維持・拡張し、identity検証失敗時だけfull navigationへfallbackする。 |
| API/Collector tests | 容量、複数dispatcher、再起動fairness、同日cross-definition、同週末、分割再選択、v1/v2、retry/timeout/duplicateの境界テストを追加する。 |
| `JraNavigatorTests.cs` / handler E2E tests | browser操作履歴を記録し、統合対象の各連続遷移で最短経路、禁止したトップ復帰、identity検証、fallback、最終domain writeを確認する。 |
| Terraform contract tests | Lambda予約多重度1、SQS batch size 1、APIの`MaxInFlightEnvelopes=1`の整合を検証する。 |

DB schema列は追加しない。既存Outboxの`EnvelopeId`、`DispatchedAt`、`ReservedUntilUnixMilliseconds`、`DispatchGeneration`と、Task/Resourceの現行状態・属性から容量とgroupを導出する。

## Decisions

- 採用: DB状態に基づく1-in-flight Envelope制御。SQSのApproximateNumberOfMessagesは遅延・不可視message・処理中状態を正確に表せず、正本にもならないため容量判定には使わない。
- 採用: 完了通知で直接次を送るのではなく、永続状態更新後の短周期dispatcherが送る。Complete APIの成功とSQS ackの順序に依存せず、再起動後も自己回復できる。
- 採用: マイクロバッチをsession互換グループへ拡張する。1 Envelope内のTaskは実行途中に追い越せないが、同一開催日・同一週末のbrowser session共有効果を得る。新規Realtimeは次Envelope選択時に優先する。
- 採用: 固定12件を全グループ共通上限にしない。上限超過はDB上で複数Envelope候補へ分割し、SQSへ先積みしない。
- 採用: RaceDay初期上限24、WeekendSubjects初期上限12。1会場12レースという偶然の件数ではなく、1 invocationの安全な作業量として設定化する。
- 採用: Envelope v2を追加し、Collectorを先にv1/v2対応させてからAPI producerをv2へ切り替える。既存v1 messageを破壊しない。
- 不採用: SQSへ全件送り、message groupや複数queueで優先順位を表現する。多重度1では既投入順の固定を解消しにくく、DBとの二重正本になる。
- 不採用: Lambda完了時にWorkerから直接次Envelopeを送る。Collectorへplanning責務とSQS書込権限を移し、失敗時の回復経路が増える。

## Acceptance criteria

1. due outboxが複数Envelope分あっても、通常経路でdispatch予約・送信される未解決の論理Envelopeは1件だけである。
2. 1 EnvelopeのTaskが実行中またはSQS再配信待ちの間、次のEnvelopeを送らない。
3. 先行Envelopeの全Taskがterminalまたは新generationへ移ると、次回dispatcher周期で次の1 Envelopeを送る。
4. BackgroundがDB待機中にRealtime・高priority Taskが到着した場合、空き枠でRealtimeを先に送る。
5. Realtimeが継続しても4 Envelope連続後はdue Backgroundを1 Envelope送り、既存starvation防止を維持する。
6. 同一開催日のRaceCardとRaceResultは競馬場をまたいで同じsession互換Envelopeへまとまり、Envelope内はpriority降順と定義済みtie-breakで安定する。
7. 複数dispatcherが同時に実行されても、配送予約を含む未解決Envelope数が設定上限を超えない。
8. API再起動後も配送済み未解決Envelopeを認識し、重複Envelopeを送らない。
9. SQS送信失敗、送信後DB確定失敗、Lambda失敗、lease expiry、retry generation更新の各経路で、枠が永久占有されずTaskの業務実行も重複しない。
10. pipeline停止、DLQサーキットブレーカー、`AvailableAt`、aggregation delayの既存抑止条件を維持する。
11. ローカルqueueでも、先行Envelope解決前は後続を投入せず、解決後に次を投入する。
12. 本番設定とTerraformのLambda予約多重度がともに1であることを契約テストし、容量設定の不一致を検出する。
13. 同一週末キーを持つ出走馬プロフィールは同じsession互換Envelopeへまとまり、週末キーのない通常プロフィールを誤って混在させない。
14. 1日3場・各12レース相当およびRaceCard/Result混在が上限を超える場合、未送信分はDBに残り、先行Envelope解決後に最新priorityで再選択される。
15. 別日のRaceResultは別Envelopeとなり、古い日付の大量Taskが後着RealtimeをSQS投入順で塞がない。
16. 最大構成のEnvelopeがLambda 15分上限の安全マージン内で終了するか、残時間不足時に未着手Taskを既存再配信契約で安全に回収できる。
17. Envelope v2のgroup keyをWorkerがAcquire結果から再検証し、不一致Taskを実行しない。既存version 1 Envelopeは移行中も処理できる。
18. API再起動後も連続Realtime Envelope数が配送履歴から復元され、Background starvation上限がリセットされない。
19. SQS送信成功・DB確定失敗から物理messageが重複しても、Lambda予約多重度、Task generation、Active leaseにより同時業務実行と二重Attemptを起こさず、解決後に重複messageをskipできる。
20. 同一開催場のRaceCard連続処理はレース番号短絡を使い、2件目で競馬トップ、カレンダー、開催一覧へ戻らない。
21. 同一日・別開催場のRaceCard連続処理は開催場切替を使い、競馬トップとカレンダーへ戻らず正しいRaceIdへ到達する。
22. 同一レースのRaceCard→RaceResultは現在画面の結果導線を使い、結果トップから探索し直さない。
23. 同一日RaceResultの同一開催場・別開催場の連続処理が、それぞれ利用可能な現在画面短絡を選び、別RaceIdへ到達した場合はfull navigationへfallbackする。
24. 同一週末の出走馬プロフィールは検証済みLocationへ直接遷移し、Taskごとのsubject identityを再検証する。Location欠落・不一致時だけ公式検索へfallbackする。
25. 各最短経路テストはbrowser操作列の肯定・否定assertionと最終page identityを検証し、handler E2Eは正しいTaskのComplete/domain writeまで確認する。
26. 共有sessionで前Taskの画面状態が混入しても、別開催場の同一レース番号・別subjectを成功扱いしない。

## Acceptance-criterion matrix

| AC | Status | Evidence |
|---|---|---|
| 1-3, 7-8 | Verified | `CollectionPlatformOutboxDispatcherTests`の容量1、完了解放、同時dispatcherテスト。容量確認と予約はstore transaction内で実行。 |
| 4-5, 18 | Verified | 実dispatcherのRealtime優先と4回後Backgroundを、dispatcher再生成を挟んで検証。履歴から連続数を復元。 |
| 6, 13-15 | Verified | RaceCard/Result同日混在、同週末Horse、RaceDay 25件の24+DB残留分割をdispatcher境界で検証。 |
| 9-12, 16, 19 | Verified | 既存reservation/generation/lease/partial failure/local queue/cutover契約テストと全非External回帰。Lambda残時間不足は未着手Taskを再配信する既存テストで検証。 |
| 17 | Verified | WorkerのRaceDay cross-definition検証、group不一致拒否、Lambda v1継続処理と未対応version拒否を検証。 |
| 20-23, 25-26 | Verified | `JraNavigatorTests` 42件。追加のRaceCard→RaceResult短絡、既存の同一開催・別開催、誤Race fallback、禁止トップ復帰を操作履歴とidentityで検証。 |
| 24 | Verified | 既存Subject handler/location/identity回帰と全Collector/Scrapingテスト、およびWeekendSubjects group/Worker group検証が成功。 |

## Delivery plan

1. storeの未解決Envelope判定と原子的な容量付きreservationを実装する。
2. レース同一日・出走馬同一週末のsession互換キーと業務順序を実装する。
3. dispatcherを空き枠連動にし、既存fair allocatorを枠取得時の選択へ接続する。
4. 統合対象ごとの最短ナビゲーションとidentity不一致fallbackを、操作履歴を記録するNavigatorテストで実装・検証する。
5. 単一・複数dispatcher、優先順位、日付/週末group、上限分割、完了、retry、再起動、送信失敗のstore/dispatcherテストを追加する。
6. API→outbox→queue→共有session Worker→ナビゲーション→Acquire/Complete/domain write→次dispatchの本番相当E2Eを追加する。
7. 設定、Terraform契約、最大構成、ローカルqueue回帰を検証し、結果を本記録へ追記する。
8. Collector v1/v2互換を先にdeployし、version 1 smoke、API v2 producer切替、version 2混在group smokeの順に本番導入する。

## Review findings

- **Resolved — cross-definition contract:** 現行EnvelopeとWorkerは単一Definition一致を必須としており、文書だけのgroup変更ではRaceCard/Result混在が動作しない。version 2のgroup contractとAcquire後再検証を実装範囲へ追加した。
- **Resolved — atomic capacity:** 単純な未解決件数取得後の既存reservationでは、複数API instanceが同時に1枠ずつ確保できる。容量確認とreservationを同一SQLite write transactionへ統合した。
- **Resolved — restart fairness:** 現行fair allocatorの連続Realtime数はメモリ状態である。配送履歴から再構成し、API再起動でBackground保証が消えない契約を追加した。
- **Resolved — grouping source:** 週末を現在時刻から推測すると再試行時にgroupが変わる。既存`weekendPriorityUntil`属性を永続group keyとして指定した。
- **Resolved — size ambiguity:** 固定12件の意味がDefinitionごとか会場ごか不明だった。RaceDay 24、WeekendSubjects 12を初期値とし、超過分をDB再選択へ戻す仕様にした。
- **Resolved — rollout compatibility:** producer/consumer同時更新を前提にすると旧SQS messageがDLQ化し得る。Collector v1/v2対応を先にdeployする順序を追加した。
- **Resolved — exactly-once boundary:** SQS送信成功・DB確定失敗では物理messageの厳密な1件保証は不可能である。保証を1論理Envelope・1同時業務実行と定義し、reserved concurrency、generation、Active leaseをE2Eで検証する。
- **Resolved — navigation efficiency evidence:** session共有だけでは画面遷移の短縮を証明できない。統合可能となったRaceCard/Result/WeekendSubjectsの連続パターンについて、browser操作列、禁止遷移、最終identity、fallback、domain writeまでを検証対象に追加した。
- **Resolved — initial limits:** 24/12を安全側の初期運用値として設定した。Lambda残時間不足時の再配信契約を自動テストし、実サイト計測で必要になれば設定値だけを縮小できる。

## Verification record

- 2026-09-13: 現行コードとTerraformを確認。dispatcherは`DispatchIntervalSeconds=5`、`DispatchBatchSize=10`、Lambdaは`reserved_concurrent_executions=1`、event source mappingは`batch_size=1`である。
- 2026-09-13: 現行dispatcherはdue outboxを一度に最大10 Envelope送信し、SQSまたはDB上の未解決Envelope数を容量判定に使っていない。fair allocatorはSQS送信順だけを決めるため、後着の高priority Taskは既投入messageを追い越せない。
- 2026-09-13: ユーザーが設計を明示承認し、StatusをApprovedへ更新した。ここから実装を開始する。
- 2026-09-13: ユーザー確認により、RaceCardとRaceResultの同日混在、競馬場横断、および出走馬の同一週末groupを設計へ追加した。固定12件を共通上限とせず、上限超過・別日分はDB待機して先行Envelope解決後に再選択する。
- 2026-09-13: ユーザー確認により、統合対象の共有sessionが最適な画面遷移を選ぶことの検証をスコープへ追加した。最短操作の肯定assertionだけでなく、不要なトップ復帰・再検索の否定assertion、identity不一致時のfallback、domain writeまでを受け入れ基準とした。
- 2026-09-13: Envelope contract v2、Definition/RaceDay/WeekendSubjects group、v1読み取り互換、Acquire後group identity検証を実装した。DB schema列は追加せず、既存Outbox/Task/Resource属性からgroupと容量を導出する。
- 2026-09-13: `MaxInFlightEnvelopes=1`を本番設定へ追加し、未解決配送済みEnvelopeと有効reservationを数えた容量確認・予約を同一store transactionへ接続した。due outbox全体からlane/priorityを選ぶため、古いBackground上位100件が後着Realtimeを隠す制限も除去した。
- 2026-09-13: RaceDayは24 Task、WeekendSubjectsは12 Taskを初期上限とし、同日RaceCard/Resultの競馬場横断混在、同週末Horse、上限超過分のDB残留を実dispatcherで検証した。
- 2026-09-13: Navigatorの共有session短絡をレビューし、同一開催RaceCard、別開催切替、結果連続処理、誤Race fallbackの既存テストに加え、RaceCard→同一RaceResultが結果トップへ戻らない操作履歴テストを追加した。
- 2026-09-13: Release solution buildは警告0・エラー0。`TestCategory!=External`はContracts 43、Domain 96、Application 56、Infrastructure 12、MachineLearning 14、Agents 106、Scraping 214、Collector 157、API 182成功・1 skip。対象テストはOutbox dispatcher 7件、Navigator 42件、CollectionPlatform系API 11件、CollectionPlatform/Cutover系Collector 134件が成功した。

## Deviations and follow-up

- 新しいナビゲーション本体の分岐追加は不要だった。同一開催RaceCard、開催場切替、RaceResult直接リンク、identity不一致fallback、SubjectのLocation優先が既に実装されていたため、本変更ではmixed Envelopeの順序接続と不足していたRaceCard→RaceResult操作列テストを追加した。
- 外部JRAサイトを使う性能測定は非External検証に含めていない。15分残時間不足時の安全な未着手再配信は自動テスト済みであり、24/12の上限は設定で縮小可能である。
