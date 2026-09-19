# Resource 中心の競馬情報収集基盤

## 位置づけ

本書は、収集対象・状態・再取得・抽出 revision・URL 候補・scheduling を統一する Collector 制御設計の正本である。配置境界は [01-lambda-collector-architecture.md](01-lambda-collector-architecture.md)、現行動作は [22-collector-design.md](22-collector-design.md)、JRA Page/Parser/Navigator は [23-jra-scraping-redesign.md](23-jra-scraping-redesign.md) を参照する。

設計変更の承認と実装進捗は [change record](changes/20260911_unified-collection-platform/README.md) を正とする。既存収集ジョブは compatibility layer として残さず、新基盤へ controlled cutover 後に production code から削除する。完全置換の論点は [decision record](changes/20260911_unified-collection-platform/decisions/full-job-replacement.md) に定義する。2026-09-11 に承認され、実装中である。

## Core concepts

- Resource: 何を取得するか。provider と論理 ID で識別し URL を Identity にしない。
- CollectionDefinition: Resource の何を観測するか。
- CollectionRevision: どの抽出仕様で観測するか。
- CollectionRequest: なぜ今取得するか。
- CollectionTask: 実行すべき仕事。履歴として何件でも作れる。
- CollectionAttempt: 実行時に何が起きたか。
- CollectionState: 現在どこまで正しいデータを持つかを示す Projection。
- ResourceLocation: 今アクセスできそうな候補。複数保持し毎回検証する。
- ResourceReference: 収集済み Resource から発見した別 Resource との関係。

詳細なモデル、制約、状態遷移、公平 scheduling、location fallback、移行手順、受け入れ基準は change record に定義する。承認後、Phase 1 に先立って確定した型・table・API 契約を本書へ同期する。

> 2026-09-14 提案: `ResourceType.Race` の `race-card` と `race-result` を単一 `race-detail` definition へ統合する。JST の対象日が今日から 5 日前以降なら出馬表を先に domain write し、当日以前は同一 task/session で結果へ遷移する。それより古い場合だけ結果を直接取得する。直近レースは両方の保存成功まで Current にせず、結果未公開・未来日は次回時刻付きで待機する。Location schema は増やさず、JRA URL と取得ページ identity で入口を検証する。切替時は旧collection dataを捨てず、Resource/State/Location/Request/Task/FailureをIDと移行元provenanceを維持して `race-detail` へtransactionalにマージし、不足する直近レースへ補完requestを作ってから旧definitionを無効化する。切替と検証の正は [直近レースの出馬表・結果を一体収集する](changes/20260914_recent-race-detail-collection/README.md) とし、承認・実装までは既存 definition を維持する。

> 2026-09-19 再提案: 上記統合のResource/Task一意性は維持するが、Task statusを複数artifactの完全性正本として使わない。同じcanonical Race配下にCard/Result facet state、更新可能な公式StartTime evidence、ArtifactKind付きLocationを持たせ、1件のactive `race-detail` taskをfacetを前進させるcontrollerとする。Attemptはstage outcomeをappend-onlyに保存し、Race全体状態はfacetから導出する。これにより同一Raceのjobを増やさず、Card failureとResult waitを分離できる。正本は[Raceリソース中心の取得状態機械](changes/20260919_race-detail-phase-recovery/README.md)とし、承認前は現行動作を変更しない。
> 結果公開状態は開催日単位ではなくRace単位とする。同日内で発走前・発走後未公開・結果未確定・公開済みが混在しても、未公開のRaceだけを同じactive taskの `RetryWaiting` とし、公開済みRaceのCurrent化と後続Raceの出馬表更新を妨げない。

2026-09-14にこの提案を実装した。新規レース収集の正規形は `ResourceType.Race / race-detail` であり、直近5日はRaceCardと公式RaceResultの両方が成功した場合だけCurrent、それ以前は公式RaceResultの成功でCurrentになる。発走前・未公開・未確定は次回確認時刻を持つretryable availabilityとして扱う。

直近5日という判定はRaceCardを試す優先条件であり、公式画面の保持期間を保証しない。RaceCardが`OutOfDisplayedRange`になった場合に結果導線へ動的に切り替え、期待される掲載終了を全体停止へ波及させない変更案と状態分類は[Dynamic race source fallback](changes/20260917_dynamic-race-source-fallback/README.md)を正本とする。グローバルなterminal failure停止規則は弱めず、承認前は現行動作を維持する。

既存 `RaceCard` / `RaceResult` collection dataは、管理APIのpreview/applyでcanonical Raceへ統合する。Request/Task/Attempt IDを維持し、Request/Taskの移行元definition/revisionをprovenance列へ保存する。StateとLocationを統合し、不完全な直近Raceには同一transaction内で `DefinitionChanged` request/task/outboxを作成してから旧definitionを無効化する。空DB initializerも同じcanonical Raceとcompleteness policyを使用する。

## Invariants

1. `Resource != URL`。
2. task 作成時の URL を唯一の取得先として固定しない。
3. HTTP 200 ではなく expected Resource identity の検証成功を collection 成功条件にする。
4. current/applied revision の単純比較ではなく Resource ごとの required revision で stale を判定する。
5. Initial、Backfill、refresh、definition change、manual、recovery は同じ経路を使う。
6. 同じ Resource + Definition の active task は一件とする。Initial、Backfill、Discovery による通常登録は Resource + Definition + requested revision が既登録なら terminal status にかかわらず request/task/outbox を増やさない。requested revision の更新、ScheduledRefresh、DefinitionChanged、ManualRefresh、Recovery は再取得を許可し、terminal task は複数許容する。詳細は[収集済み対象の通常タスク重複登録抑止](changes/20260913_skip-duplicate-collection-registration/README.md)を参照する。
7. Backfill と Realtime は同じ状態正本を使い lane と公平配分で制御する。
   2026-09-15 実装: 管理画面からの期間レース再取得は Backfill と区別し、JST の包括日付範囲を1日1 `race-discovery` taskへ展開する。既存 state があっても terminal task 後の明示的再取得を許可し、同じ batch の再送と active task だけを重複抑止する。詳細と検証は[期間を指定してレース情報を再取得する](changes/20260915_race-period-recollection/README.md)を正とする。
8. state は Projection であり、request/task/attempt/domain write outcome が監査根拠である。
9. URL 一時障害は location の恒久無効を意味しない。
10. パラメーターなしのJRA `access*.html` は主体を識別する終端locationではないため、Subject Discovery/Recoveryの取得先として使用・再保存しない。Horse/Jockey/TrainerはResource属性の名前等からNavigator Discoveryを行い、OwnerはRaceEntry由来の名前で内部identityを解決する。
10. domain write 成功前に applied revision と Current 状態を進めない。
11. 旧 job store/runner/scheduler/API/UI を新基盤の恒久互換層として残さない。
12. 旧 job data/key は移行せず cutover 時に削除し、新状態と未完了 work は Domain Data と Discovery から再構築する。
13. 旧 main SQS queue と旧 DLQ は新 queue の smoke test 後、同じ cutover 内で削除する。
14. 実行多重度1の間はDB outboxを優先度付き待機場所とし、未解決Envelopeがなくなった時だけ次のEnvelopeをSQSへ送る。次Envelopeは送信時点のlane、priority、aging、公平配分で選ぶ。全laneにdue処理がある場合は `Realtime×4 → Normal×1 → Realtime×4 → Background×1`（80%/10%/10%）で配送し、Realtimeがない場合はNormalとBackgroundを1:1で交互に配送する。空のlaneの枠は他のdue laneへ譲り、処理能力を遊休させない。各lane内ではpriority、aging、利用可能時刻、作成時刻、Task IDの順で安定選択する。管理画面の収集ジョブ一覧は、成功完了していないジョブを `Succeeded` より先に置いたうえで、時刻と直前の配送履歴で変動する aging と公平配分を再現せず、lane（Realtime、Normal、Background）、保存済みpriority、ジョブ種類を表すDefinition IDからなる安定した運用順位をページング前に適用する。日本語表示名は並び順に使用しない。session互換性は同一Definitionに限定せず、レース系の同一開催日、出走馬プロフィール系の同一週末を単位とし、安全上限超過と別日分はDBで分割待機する。共有session内では検証済みLocationまたは現在画面の短絡遷移を優先し、identity不一致時だけ完全探索へfallbackする。詳細と検証結果は[収集キューの実行容量連動ディスパッチ](changes/20260913_capacity-aware-collection-dispatch/README.md)、[収集ジョブ一覧を運用優先順位で並べる](changes/20260914_collection-job-priority-order/README.md)、[通常レーンの公平配分](changes/20260917_collection-normal-lane-fairness/README.md)を参照する。
15. Realtime の競走馬プロフィールから作る過去レースは Normal/Low、Normal または Background の競走馬プロフィールから作る過去レースは Background/Background とし、horse-history を Realtime へ推移的に増殖させない。同一 Resource/Definition の非終端 task に重複依頼が来た場合、低い依頼では降格せず、既存値と新規値の高い lane（Realtime > Normal > Background）および高い数値 priority を同じ Task ID へ採用する。Running task の現在の attempt は中断・重複実行せず、保存した昇格値を次回 retry/dispatch から使用する。既存 task の一括 migration は行わず、新しい重複依頼が来た時だけこのマージ規則を適用する。詳細契約は[競走馬の過去レースを段階的に低優先化する](changes/20260917_bound-realtime-discovery/README.md)を参照する。
16. 主体プロフィールの同定失敗は、一時アクセス障害、既知の名前正規化、誤生成参照、提供元対象外、曖昧同定、ページ構造エラーへ分類する。同じrevisionの盲目的な再試行は行わず、修正済みの新revisionと決定的な同一性根拠がある対象だけをfailureごと・target revisionごとの冪等キーで一度、自動Recoveryする。提供元対象外と誤生成参照は再取得せず理由付き終端にし、曖昧対象、名前欠落、構造エラーは誤結合せず要対応に残す。plannerは対象単位で失敗を隔離し、成功済み処置を巻き戻さず後続対象を継続する。詳細契約は[主体識別エラーを分類し安全に自動復旧する](changes/20260918_subject-identification-auto-recovery/README.md)を参照する。
17. 運用者が主体識別の古い失敗通知を対応不要と判断した場合は、task、attempt、Resource、domain dataを削除・改変せず、対象notificationだけを`Superseded`として閉じる。これはResource抑止や成功回復を意味せず、同じResourceで将来発生した新しい失敗は別notificationとして通知する。検証と状態遷移は原子的に行い、Recovery開始済み通知との競合では一部更新しない。画面契約と検証は[主体識別の古い失敗通知を画面から対応不要にする](changes/20260918_dismiss-subject-repair-candidates/README.md)を参照する。
18. 収集運用監視は同一cutoffの読み取り専用snapshotを使い、要対応失敗、停止、滞留、優先順異常を評価する。単一配送Envelope内の互換タスクは追い越しとみなさず、既に滞留している高優先度タスクを独立した配送判断が3回以上追い越した場合だけ順序異常候補とする。既知の過去エラーだけをversion付きrecipe、事前backup、single-flight、canary、独立kill switchの下で補正する。バグと未知エラーはProposed change recordを作るが、自動承認・コード変更・未知データ補正は行わない。詳細は[収集運用を監視し起票と安全な過去ジョブ補正を自動化する](changes/20260919_collection-monitoring-change-record-automation/README.md)を参照する。
18. Horse/Jockey/Trainer/Ownerの保存と後続Task生成は、同じ一括主体解決境界が返すcanonical IDを共有する。高速化経路もsource identity、名称正規化、Owner aliasを欠落させず、producerは表示名から独自にIDを再計算しない。過去Taskの互換修復は強い識別子、起点RaceEntry、既存alias、redirectを根拠にpreview/applyし、名前だけの自動統合、履歴削除、無期限再試行を行わない。詳細は[主体ジョブのcanonical ID整合と過去ジョブフォールバック](changes/20260918_canonical-subject-job-fallback/README.md)を参照する。

> 2026-09-18提案: 同一URL内の主体一覧→プロフィール遷移では、通信静止や本文の存在ではなく、期待するプロフィール見出しと対象名を画面成立条件にする。成立しない既知の構造エラーはFailed/要対応として対象単位に終端し、同revisionで再試行せず、無関係な収集全体は停止しない。完了通知に型付きfailure impactを持たせ、明示的に隔離可能な場合だけ非停止とする。既存要対応はpreviewで原因分類し、一意性を証明できる対象だけをrevision recoveryする。impact欠落、未分類の恒久障害、内容検証失敗は従来どおり安全停止する。設計と承認状態は[プロフィール画面を確実に待機し、既存エラーを安全に復旧する](changes/20260918_isolate-structural-collection-failures/README.md)を正とし、承認・実装までは現行の全体停止規則を維持する。
