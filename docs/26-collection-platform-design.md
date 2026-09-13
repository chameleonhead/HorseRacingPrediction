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
> 結果公開状態は開催日単位ではなくRace単位とする。同日内で発走前・発走後未公開・結果未確定・公開済みが混在しても、未公開のRaceだけを同じactive taskの `RetryWaiting` とし、公開済みRaceのCurrent化と後続Raceの出馬表更新を妨げない。

2026-09-14にこの提案を実装した。新規レース収集の正規形は `ResourceType.Race / race-detail` であり、直近5日はRaceCardと公式RaceResultの両方が成功した場合だけCurrent、それ以前は公式RaceResultの成功でCurrentになる。発走前・未公開・未確定は次回確認時刻を持つretryable availabilityとして扱う。

既存 `RaceCard` / `RaceResult` collection dataは、管理APIのpreview/applyでcanonical Raceへ統合する。Request/Task/Attempt IDを維持し、Request/Taskの移行元definition/revisionをprovenance列へ保存する。StateとLocationを統合し、不完全な直近Raceには同一transaction内で `DefinitionChanged` request/task/outboxを作成してから旧definitionを無効化する。空DB initializerも同じcanonical Raceとcompleteness policyを使用する。

## Invariants

1. `Resource != URL`。
2. task 作成時の URL を唯一の取得先として固定しない。
3. HTTP 200 ではなく expected Resource identity の検証成功を collection 成功条件にする。
4. current/applied revision の単純比較ではなく Resource ごとの required revision で stale を判定する。
5. Initial、Backfill、refresh、definition change、manual、recovery は同じ経路を使う。
6. 同じ Resource + Definition の active task は一件とする。Initial、Backfill、Discovery による通常登録は Resource + Definition + requested revision が既登録なら terminal status にかかわらず request/task/outbox を増やさない。requested revision の更新、ScheduledRefresh、DefinitionChanged、ManualRefresh、Recovery は再取得を許可し、terminal task は複数許容する。詳細は[収集済み対象の通常タスク重複登録抑止](changes/20260913_skip-duplicate-collection-registration/README.md)を参照する。
7. Backfill と Realtime は同じ状態正本を使い lane と公平配分で制御する。
8. state は Projection であり、request/task/attempt/domain write outcome が監査根拠である。
9. URL 一時障害は location の恒久無効を意味しない。
10. domain write 成功前に applied revision と Current 状態を進めない。
11. 旧 job store/runner/scheduler/API/UI を新基盤の恒久互換層として残さない。
12. 旧 job data/key は移行せず cutover 時に削除し、新状態と未完了 work は Domain Data と Discovery から再構築する。
13. 旧 main SQS queue と旧 DLQ は新 queue の smoke test 後、同じ cutover 内で削除する。
14. 実行多重度1の間はDB outboxを優先度付き待機場所とし、未解決Envelopeがなくなった時だけ次のEnvelopeをSQSへ送る。次Envelopeは送信時点のlane、priority、aging、公平配分で選ぶ。管理画面の収集ジョブ一覧は、成功完了していないジョブを `Succeeded` より先に置いたうえで、時刻と直前の配送履歴で変動する aging と公平配分を再現せず、lane（Realtime、Normal、Background）、保存済みpriority、ジョブ種類を表すDefinition IDからなる安定した運用順位をページング前に適用する。日本語表示名は並び順に使用しない。session互換性は同一Definitionに限定せず、レース系の同一開催日、出走馬プロフィール系の同一週末を単位とし、安全上限超過と別日分はDBで分割待機する。共有session内では検証済みLocationまたは現在画面の短絡遷移を優先し、identity不一致時だけ完全探索へfallbackする。詳細と検証結果は[収集キューの実行容量連動ディスパッチ](changes/20260913_capacity-aware-collection-dispatch/README.md)および[収集ジョブ一覧を運用優先順位で並べる](changes/20260914_collection-job-priority-order/README.md)を参照する。いずれも2026-09-14時点で実装済みである。
