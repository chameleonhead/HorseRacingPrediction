# Resource 中心の競馬情報収集基盤

## 位置づけ

本書は、収集対象・状態・再取得・抽出 revision・URL 候補・scheduling を統一する Collector 制御設計の正本である。配置境界は [01-lambda-collector-architecture.md](01-lambda-collector-architecture.md)、現行動作は [22-collector-design.md](22-collector-design.md)、JRA Page/Parser/Navigator は [23-jra-scraping-redesign.md](23-jra-scraping-redesign.md) を参照する。

設計変更の承認と実装進捗は [change record](changes/20260911_unified-collection-platform/README.md) を正とする。既存収集ジョブは compatibility layer として残さず、新基盤へ controlled cutover 後に production code から削除する。完全置換の論点は [decision record](changes/20260911_unified-collection-platform/decisions/full-job-replacement.md) に定義する。2026-09-11 現在は Proposed であり、本設計に基づく production 実装は開始していない。

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

## Invariants

1. `Resource != URL`。
2. task 作成時の URL を唯一の取得先として固定しない。
3. HTTP 200 ではなく expected Resource identity の検証成功を collection 成功条件にする。
4. current/applied revision の単純比較ではなく Resource ごとの required revision で stale を判定する。
5. Initial、Backfill、refresh、definition change、manual、recovery は同じ経路を使う。
6. 同じ Resource + Definition の active task は一件、terminal task は複数許容する。
7. Backfill と Realtime は同じ状態正本を使い lane と公平配分で制御する。
8. state は Projection であり、request/task/attempt/domain write outcome が監査根拠である。
9. URL 一時障害は location の恒久無効を意味しない。
10. domain write 成功前に applied revision と Current 状態を進めない。
11. 旧 job store/runner/scheduler/API/UI を新基盤の恒久互換層として残さない。
12. 旧状態は意味的に移行し、未完了 work と監査履歴を黙って破棄しない。
