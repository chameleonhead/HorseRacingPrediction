# 自動処理設計（分離ドキュメント）

収集 scheduling の次期共通設計では、Resource + CollectionDefinition の状態を policy で評価し、NextCollectionAt、動的 priority、Realtime/Normal/Background lane を決める。Backfill と当日収集は別基盤にせず、公平配分と aging で starvation を防ぐ。正本は [26-collection-platform-design.md](26-collection-platform-design.md) とする。2026-09-11 現在は Proposed である。

このドキュメントは、ドメイン設計から分離した自動処理の責務を定義する。

具体的な実行主体（Collector / Predictor）とLLM利用方針は [00-system-architecture.md](00-system-architecture.md) を参照。

## 目的

- 取り込み、正規化、突合、結果反映などの自動処理を整理する
- ドメインイベントとの接続点を定義する
- 手動運用との境界を明確にする

## 自動処理の責務

### 1. Data Collection

- レース情報、出馬表、馬、騎手、調教師、結果を収集する
- SourceDocument を作成する

### 2. Normalization

- 表記ゆれ、単位、コードを正規化する
- ExtractedFact を生成する

### 3. Entity Resolution

- 同一主体の突合を行い Canonical ID を解決する
- EntityAlias を更新する

### 4. Result Import

- 結果、着順、払戻を反映する
- RaceResultDeclared / EntryResultDeclared / PayoutResultDeclared を発行する

### 5. Projection Maintenance

- イベントを購読して read model を再構築する
- 再投影を実行できるようにする

## API との接続

- 書き込みは ASP.NET Core API のコマンドエンドポイント経由で実行する
- 認証は API キーで行い、PerformedByType/PerformedById に記録する
- API キーは環境変数 HORSE_RACING_API_KEY から読み取る想定とする
- リクエストヘッダー X-Api-Key を照合する

## 運用方針

- 自動処理の失敗は再実行可能にする
- 取り込み時刻とデータソースを必ず保存する
- 訂正は上書きせず訂正イベントで表現する

## 収集運用の監視、起票、過去ジョブ補正

要対応のfailure notification、長時間停滞したactive task、予期しないpipeline pause、lane/priority/公平配分契約に反する処理順を定期的に評価する。実行層は型付きの決定的findingを返し、定期自動化は新規findingごとに `Proposed` change recordを作る。同じfingerprintの未完了recordは新規作成せず、観測履歴を追記する。

プログラムバグは修正用change record、未知の過去ジョブエラーは原因仮説・影響範囲・対応候補・推奨調査を持つchange recordとして起票する。既知の過去ジョブエラーは、一意性根拠、revision条件、preview、冪等キー、postcondition、実行上限、監査記録を持つ登録済みの自動安全recipeに限り自動補正する。曖昧・未登録・事前条件不一致の対象は変更せず、未知エラー調査へ分離する。

バグ修正と未知エラー対応の自動承認・自動デプロイは行わない。評価契約、自動補正の安全境界、重複抑止、秘密情報境界、受け入れ基準は [収集運用を監視し起票と安全な過去ジョブ補正を自動化する](changes/20260919_collection-monitoring-change-record-automation/README.md) を正本とする。2026-09-19に、読み取り専用評価API、change record生成、既知エラーのrevision-gated補正、定期workflowを実装した。
安定化期間の定期実行主体はCodexのプロジェクトスケジュールタスクとし、GitHub workflowはAPI keyを使う手動probeとしてCodexから起動する。single-flight、maintenance抑止、負荷上限、起票と補正の独立kill switch、事前backup、外部証拠の非信頼扱いを必須とする。実行エラー、未知エラー、プログラムバグは原因・修正案・検証手順を持つchange recordまでを自動化し、人の承認なしにコード変更、未知データ補正、mergeを実行しない。既知データ補正も安定化期間中は自動applyせずpreviewに限定する。

CodexのローカルスケジュールはPCとデスクトップアプリの稼働に依存するため、恒久的な24時間監視の正本にはしない。安定化後は、収集サービス内のdurable lease付き監視またはクラウドスケジューラを検知の正本、Codexを診断・修正案作成の担当とする構成を再評価する。

## 長期収集計画の定期見直し（提案）

長期バックフィルと馬公式情報補完は、開始時の計画だけで完走させず、永続化された `AcquisitionPlanReview` ジョブで既定15分ごとに再評価する。今週末の出馬表と出走予定馬公式情報の不足を最初に確認し、当日・直近結果、長期バックフィルの順に、未実行ジョブの優先度と次回投入量を調整する。状態別件数、最終進捗、チェックポイント、反復失敗、キュー状態、判断理由、次回予定を保存する。

見直しは実行中ジョブを強制中断せず、成功済みチェックポイントをやり直さず、停止・保留・DeadLetterを自動解除しない。同一時間枠の重複と古い見直し世代による上書きを防ぐ。リース切れ・配送漏れ・DLQサーキットブレーカーは既存watchdogの責務とし、見直しジョブは再配送を行わない。詳細は [過去レースと馬公式情報の自律収集](changes/20260909_autonomous-historical-race-backfill/README.md) を参照。承認前のため未実装である。

## 予想評価の再計算

ワークフロー管理は行わず、整合性維持に必要な最小処理のみ扱う。

- 結果イベントまたは訂正イベント発生時に再計算対象をマークする
- 即時再計算できる場合は EvaluatePredictionTicket を発行する
- 即時再計算できない場合は RecalculatePredictionEvaluation を後続処理で発行する
- 再計算失敗時は EvaluationStatus を Failed に更新する

## 責務と実行主体の対応

| 自動処理の責務 | 現在の実行主体 |
|---|---|
| Data Collection | Collector（[22-collector-design.md](22-collector-design.md)） |
| Normalization / Entity Resolution | Api（書き込みコマンド内、[10-domain-design.md](10-domain-design.md)） |
| Result Import | Collector → Api |
| Projection Maintenance | Api |
| 予想生成 | Predictor（ML/APIベース、[25-predictor-design.md](25-predictor-design.md)） |
| SNS投稿文生成 | Predictor（マルチエージェントLLM、[25-predictor-design.md](25-predictor-design.md)） |
