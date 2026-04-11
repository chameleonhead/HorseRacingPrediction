# 自動処理設計（分離ドキュメント）

このドキュメントは、ドメイン設計から分離した自動処理の責務を定義する。

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

## 予想評価の再計算

ワークフロー管理は行わず、整合性維持に必要な最小処理のみ扱う。

- 結果イベントまたは訂正イベント発生時に再計算対象をマークする
- 即時再計算できる場合は EvaluatePredictionTicket を発行する
- 即時再計算できない場合は RecalculatePredictionEvaluation を後続処理で発行する
- 再計算失敗時は EvaluationStatus を Failed に更新する
