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

### 既存CI/CDでの配備安全条件

既存`app-deploy`はCollector更新前にpipelineを停止し、実行中taskが0件になるまで上限付きで待つ。元から停止中なら理由と停止状態を保持する。
API停止成功・非稼働・未checkpointのSQLite WALなしを確認してからDBをbackupする。確認失敗時は配備を中断し、不完全なbackupを正常扱いしない。
配備時の旧race collection job移行（race-detail apply/preview）は実行しない。配備とhealth確認後にも停止/実行中0件を確認し、配備前に稼働中で、再開直前に要対応failureが0件の場合だけ自動再開する。新旧どちらの要対応でも残る場合や不明/失敗時は停止を維持し、明示的な復旧判断へ戻す。新しいWorkflowは追加しない。
変更・反例・運用結果は [collection error closure](changes/20260924_collection-error-closure/evidence/operations.md) を参照する。

### Collector Lambda の一時資源

Collector Lambda は共有 `/tmp` を直接 cleanup 対象にしない。`/tmp/horse-racing-prediction-collector` をアプリ所有rootとし、invocationごとのchildへPlaywrightのtemp、HOME/XDG cache、runtime event/responseを隔離する。collectorとPlaywright/Chromium子孫はinvocation専用process groupで起動し、直接child終了またはbootstrap signal時に当該groupだけをTERM、bounded wait、必要時KILLしてからchildを削除する。invocation sessionはcollectorを起動する直前にcore sizeを0とし、異常終了のstatusとtext diagnosticは維持したままcollector/子孫のraw core file生成だけを抑止する。このpolicyはbootstrap親やunrelated processへ適用しない。正常終了・報告済み失敗・signal終了では当該childだけを削除し、runtime強制終了で後処理できなかったchildは次のbootstrap初期化時にownership markerと非稼働leaseを検証して回収する。空・不正・bootstrap自身と同じPGID、marker不正、symlink、root外、稼働中processのchildは削除・終了対象にせず、process名検索や共有processの一括停止を行わない。

invocation前後には所有rootの使用量、filesystem空き容量、取得可能な場合の空きinode、invocation directory数だけを記録する。原因識別が必要な安全停止中の診断revisionでは、削除前後とprocess-group終了前後の同じ容量値に加え、collector稼働中に親子関係から取得したbounded descendantについて、取得数、生存数、対象group外生存数、削除済みfd数だけを記録する。全`/proc`走査は行わず、PID/PGID実値、directory名、command line、環境変数、URL、資格情報、ページ内容は記録しない。ephemeral storage増量だけで残留を隠さず、cleanup失敗、ENOSPC、相関ID欠落は安全停止と原因調査へ戻す。実装・反例・本番検証は [Playwright 一時領域 ENOSPC の恒久対策](changes/20260925_playwright-tmp-enospc/README.md) を正本とする。

- 自動処理の失敗は再実行可能にする
- 取り込み時刻とデータソースを必ず保存する
- 訂正は上書きせず訂正イベントで表現する

## 収集運用の監視、起票、過去ジョブ補正

要対応のfailure notification、長時間停滞したactive task、予期しないpipeline pause、lane/priority/公平配分契約に反する処理順を定期的に評価する。実行層は型付きの決定的findingを返し、各actionable findingを原因仮説、影響、所有task、次の安全な操作へ対応付ける。fingerprint別recordは監査履歴として保持するが、反復findingごとの新規recordやPRは作成しない。

監視APIはprobeの実行成否とは別に `Healthy`、`FindingRecorded`、`ActionRequired`、`MonitorFailed` を返す。原因・所有task・次操作が欠けたactionable findingは `MonitorFailed` とする。Codex heartbeatはDPAPIで保護した資格情報を使うローカルrunnerからAPIを直接読み、要対応、復旧、監視欠落、週末データ鮮度を同じスレッドへ配送する。登録済みデータ補正canaryと限定的なpipeline継続だけを自動実行できる。

プログラムバグは修正用change record、未知の過去ジョブエラーは原因仮説・影響範囲・対応候補・推奨調査を持つchange recordとして起票する。既知の過去ジョブエラーは、一意性根拠、revision条件、preview、冪等キー、postcondition、実行上限、監査記録を持つ登録済みの自動安全recipeに限り自動補正する。曖昧・未登録・事前条件不一致の対象は変更せず、未知エラー調査へ分離する。

バグ修正と未知エラー対応の自動承認・自動デプロイは行わない。評価契約、自動補正の安全境界、重複抑止、秘密情報境界、受け入れ基準は [収集運用を監視し起票と安全な過去ジョブ補正を自動化する](changes/20260919_collection-monitoring-change-record-automation/README.md) を正本とする。2026-09-19に、読み取り専用評価API、change record生成、既知エラーのrevision-gated補正を実装した。
安定化期間の定期実行、手動診断、DLQ Recovery、登録済み補正recipeのpreview/applyはCodexのプロジェクトタスクから管理APIを呼ぶ経路へ一本化する。収集運用専用のGitHub Actions workflowは使用しない。single-flight、maintenance抑止、負荷上限、起票と補正の独立kill switch、事前backup、外部証拠の非信頼扱いを必須とする。実行エラー、未知エラー、プログラムバグは原因・修正案・検証手順を持つchange recordまでを自動化し、人の承認なしにコード変更、未知データ補正、mergeを実行しない。既知データ補正も安定化期間中は自動applyせずpreviewに限定する。

CodexのローカルスケジュールはPCとデスクトップアプリの稼働に依存するため、恒久的な24時間監視の正本にはしない。安定化後は、収集サービス内のdurable lease付き監視またはクラウドスケジューラを検知の正本、Codexを診断・修正案作成の担当とする構成を再評価する。

正常判定はpipelineやtaskが動いていることだけでは完了しない。週末開催について、公式discoveryで発見したレースを分母に、金曜時点の出馬表・出走馬と、各レース終了後の結果が期限内にdomainへ保存された割合を評価する。0件を無条件に正常とせず、discovery未完了と開催なしを区別する。期限、severity、例外状態、通知lifecycleは [週末レース情報の期限内収集を監視する](changes/20260919_collection-freshness-slo/README.md) を正本とする。
監視の実行経路はCodexのローカルスケジュールタスクに一本化し、`collection-monitoring` GitHub Actions workflowは削除する。手動診断も同じローカルrunnerを使用し、監視からchange recordの自動生成、branch push、PR作成は行わない。ローカルrunnerの資格情報は `%LOCALAPPDATA%\HorseRacingPrediction\CollectionMonitor` 配下へ保存し、API keyはWindows DPAPIで現在ユーザーに暗号化する。詳細は [収集監視を観測記録から原因分析と実行タスクへ変更する](changes/20260919_collection-monitor-root-cause-triage/README.md) を正本とする。

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
