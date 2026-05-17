# AgentClient 実装計画

## 目的

このドキュメントは、HorseRacingPrediction.AgentClient を AI 主導のオーケストレータではなく、定常処理を安定実行するジョブ実行クライアントとして再定義する。

主目的は以下の 4 点である。

- JRA などの公式サイトから過去データとリアルタイムデータを安定収集する
- 収集済みデータを API 経由で登録し、予想処理へ渡す
- 必要な箇所に限って AI を補助利用する
- ジョブを永続管理し、プロセス終了後も途中から再開できるようにする

## 前提整理

### 運用方針

- 競馬予想の主処理は、構造化された過去データとリアルタイムデータに基づく機械的なパイプラインで行う
- AI エージェントは主系統の制御には使わない
- AI エージェントは以下の補助用途に限定する
  - 自由記述ページからの構造化抽出、ラベル付け
  - 抽出失敗時の要約、例外要因の説明補助
  - 予想結果の投稿文整形
  - 投稿先ごとの文体整形、要約

### 対象データソース

- 中央競馬の公式データソースとして JRA を優先する
- 将来的に地方競馬のデータソースを追加できるよう、主催者ごとの差分を Provider 単位で吸収する
- 公式情報と任意情報は同列に扱わず、信頼度と用途を分離する

## AgentClient の想定機能

### 1. 過去データ抽出機能

- 過去のレース結果、出馬表、開催情報、払戻、馬、騎手、調教師などを取得する
- 取得したデータを API 経由でデータベースへ登録する
- 平常時はバックフィルジョブとして継続的に実行する
- リアルタイム抽出から要求された過去成績データも、この機能が担う

### 2. リアルタイム抽出機能

- 直近開催レースの出馬表、馬場状態、出走馬、騎手、調教師、オッズ、結果公開状況などを取得する
- 必要に応じて、出走馬の過去成績取得のために過去データ抽出ジョブを要求する
- 任意情報ソースから馬の調子、展望、評価コメントなどを取得する
- 任意情報ソースのうち自由記述中心のものは AI による構造化抽出対象とする

### 3. 予想機能

- リアルタイムデータと過去データを入力として、今後実施予定のレースの予想を生成する
- 予想生成自体は AI 会話フローに依存しない構成を基本とし、必要なら補助的に自然言語要約を追加する
- 予想の根拠、信頼度、発行時刻、対象レース、入力データ版を保存可能にする

### 4. 予想投稿機能

- 予想結果を X、Slack、Discord などに投稿する
- 投稿前に媒体別の文字数制約、整形ルール、テンプレートを適用する
- 投稿文の整形や短縮、ハッシュタグ選定などの一部に AI を利用できるようにする

## AI 利用境界

### AI を使う処理

- 表形式ではないページ本文からの情報抽出
- 人手向けのレポート、投稿文、要約文の整形
- 障害発生時の観測ログ要約

### AI を使わない処理

- スケジューリング
- ジョブ優先度制御
- リトライ制御
- 冪等登録
- スクレイピングの定常遷移
- バックフィルの進捗管理
- レース開催状況に応じた負荷制御

## 主要課題

### 1. リアルタイム処理とバックフィル処理の競合

平常時は過去データ抽出を積極的に進めたいが、開催日、とくにレース進行中はリアルタイム抽出を優先し、不要なバックフィル実行を抑える必要がある。

### 2. リアルタイム要求による過去データ補完

出馬表取得時、出走馬の過去成績が不足していれば、その場で過去データ抽出ジョブを要求したい。ただし要求元を追跡し、同一対象の重複ジョブを抑止する必要がある。

### 3. プロセス停止後の途中再開

AgentClient が停止しても、ジョブの進行状況、取得済みページ、未完了タスク、リース状態を失わず、再起動後に継続できる必要がある。

### 4. 主催者ごとの差分

中央競馬と地方競馬では開催体系、レース ID、公開タイミング、画面構造が異なる可能性があるため、JRA 固定の設計にしない。

## 目標アーキテクチャ

```text
AgentClient
├─ Scheduler
├─ Job Dispatcher
├─ Job Store
├─ Workers
│  ├─ Historical Extraction Worker
│  ├─ Realtime Extraction Worker
│  ├─ Prediction Worker
│  └─ Publication Worker
├─ Provider Adapters
│  ├─ JRA Provider
│  └─ Local Racing Provider
└─ AI Assist Services
   ├─ Free Text Structuring
   └─ Post Formatter
```

## ジョブ管理方針

### 設計原則

- すべての実行単位をジョブとして永続化する
- ジョブは状態遷移とチェックポイントを持つ
- ワーカーはジョブストアから lease 取得して処理する
- リース期限切れ時は再取得可能にする
- ジョブ投入と API 登録は冪等にする
- 親ジョブから子ジョブを要求できるようにする

### 想定ジョブ種別

- ResultBackfillPlanningRequest
- ResultMonthDiscoveryRequest
- ResultDayDiscoveryRequest
- ResultDayCollectionRequest
- ResultRaceCollectionRequest
- RaceCardCollectionRequest
- HistoricalRaceResultCollectionRequest
- EntityProfileSyncRequest
- PredictionPreparation
- PredictionExecution
- PredictionPublication

### 結果抽出ジョブの分解方針

結果抽出は、依頼ジョブと実行ジョブを明確に分離する。

- ResultBackfillPlanningRequest
  - 未抽出期間を確認し、対象月の探索要求を登録する
  - 初回起動時は過去 3 年分の月を一括で要求する
- ResultMonthDiscoveryRequest
  - 当月・前月は JRA 結果トップページから開催日一覧を取得する
  - それ以前の月は JRA 結果検索を使って未取得日の候補を取得する
  - 日別状態が Complete でない日について ResultDayDiscoveryRequest を登録する
- ResultDayDiscoveryRequest
  - 当該日の開催場と対象レース一覧を確定する
  - その日の抽出対象総数を確定し、ResultDayCollectionRequest を登録する
- ResultDayCollectionRequest
  - 当該日の全開催場・全レース結果を抽出する親ジョブとして扱う
  - 必要に応じて ResultRaceCollectionRequest を順次要求する
- ResultRaceCollectionRequest
  - 1 レース分の結果取得と API 登録を行う
  - 馬・騎手・調教師の基本情報が不足していれば同期的に最低限登録する
  - 詳細プロフィールは EntityProfileSyncRequest を別ジョブで要求する
- EntityProfileSyncRequest
  - horse、jockey、trainer のプロフィールや詳細情報を後続で取得する
  - レース結果保存の同期処理から切り離し、再試行と障害管理を個別に行う

### 結果抽出に必要な日別状態

レース単位の状態だけでは、開催日当日の不完全データや日次再抽出の要否を判断しにくい。
そのため、日別の抽出状態を別 ReadModel として持つ。

- NotStarted
- Discovering
- Ready
- Running
- Partial
- Incomplete
- Complete
- RetryScheduled
- DeadLetter

日別状態には、少なくとも以下を保持する。

- ProviderType
- TargetMonth
- TargetDate
- ExpectedRaceCount
- CompletedRaceCount
- IncompleteReason
- LastCompletedAt
- RetryAfter
- LastError

### 想定ジョブ状態

- Pending
- Ready
- Running
- WaitingDependency
- Succeeded
- Failed
- Cancelled
- DeadLetter

### ジョブ共通属性

- JobId
- JobType
- ProviderType
- Priority
- RequestedByType
- RequestedById
- DeduplicationKey
- Payload
- Status
- AttemptCount
- LeaseOwner
- LeaseExpiresAt
- AvailableAt
- Checkpoint
- LastError
- CreatedAt
- UpdatedAt

### 再開性のための要件

- Pending と Running を永続ストアへ保存する
- Running はリース方式で管理し、クラッシュ時は lease timeout 後に Ready へ戻す
- 長時間ジョブは月単位、日単位、レース単位、馬単位で checkpoint を記録する
- API への書き込みは idempotency key を使い、途中再開時の重複登録を防ぐ
- 失敗は AttemptCount と LastError を保持し、上限超過時は DeadLetter へ送る
- 開催日当日のように結果が不完全な日付は、日別状態を Incomplete または RetryScheduled にして後続再抽出できるようにする

### 現在実装との差分

現状の AgentClient には、予想候補キューを JSON ファイルで保持する ProcessingStateStore が存在するが、扱えるのは予想キューと一部の収集記録に限られる。

今後は以下へ拡張する必要がある。

- 予想だけでなく抽出、補完、投稿も同じジョブモデルで管理する
- JSON 単一ファイルではなく、複数ワーカーや優先度制御に耐える永続ストアへ移行する
- lease、dependency、priority、checkpoint を標準属性として扱う
- レース単位の収集状態だけでなく、月別・日別の探索進捗と再抽出状態を保持する
- 結果抽出は「月探索」「日探索」「日次収集」「レース収集」に分けて監査可能にする
- horse、jockey、trainer の最低限登録と詳細同期要求を分離する

## 優先度制御

### 基本優先順位

1. 開催中レースに関わるリアルタイム抽出
2. 直近開催レースの予想準備と予想実行
3. 投稿期限が近い予想投稿
4. リアルタイム抽出から派生した過去成績補完
5. 平常時の過去データバックフィル

### 運用ルール

- 開催中または開催直前の時間帯はバックフィルの同時実行数を強く制限する
- 非開催時間帯はバックフィルの実行数を増やす
- リアルタイム抽出から要求された過去データ抽出は、通常バックフィルより高優先度で実行する
- 同一レース、同一馬、同一開催日の重複要求は DeduplicationKey で統合する
- 当面は、AgentClient 全体で同時実行可能なジョブを 1 件までに制限する
- 単一実行制御は job type ごとの batch size ではなく、グローバルな dispatcher lease で保証する

### レース進行に応じたモード

- LiveMode
  - リアルタイム抽出最優先
  - バックフィルは停止または最小並列度
- PreRaceMode
  - 出馬表、馬場、オッズ、過去成績補完を優先
  - バックフィルは低並列度
- IdleMode
  - バックフィル優先
  - 翌開催に向けた予想準備を継続

## 依存関係制御

### 例: 結果データ抽出の基本シナリオ

1. ResultBackfillPlanningRequest が未抽出期間を確認する
2. 初回起動時は過去 3 年分の月に対して ResultMonthDiscoveryRequest を登録する
3. ResultMonthDiscoveryRequest が月ごとの取得経路を選択する
4. 当月・前月は結果トップページ、それ以前は結果検索を使って未完了日を列挙する
5. 各日付に対して ResultDayDiscoveryRequest を登録する
6. ResultDayDiscoveryRequest が開催場と対象レース数を確定し、ResultDayCollectionRequest を登録する
7. ResultDayCollectionRequest が ResultRaceCollectionRequest を順次実行し、日別完了状態を更新する
8. 不完全な日付は Incomplete または RetryScheduled とし、後続再抽出対象に残す

### 例: 出馬表から過去成績補完を要求する場合

1. RaceCardCollectionRequest が出馬表を取得する
2. 出走馬ごとに必要な過去成績の有無を確認する
3. 不足があれば、出馬表の「過去の成績」から過去に出走したレース参照を抽出する
4. 未登録の過去レースごとに HistoricalRaceResultCollectionRequest を要求する
5. PredictionPreparation は不足レース結果ジョブが完了するまで WaitingDependency になる
6. 依存完了後に PredictionExecution を開始する

### 依存関係の扱い

- 親子ジョブ関係をジョブストアで追跡する
- 子ジョブ失敗時は親ジョブを Failed または PartialReady に遷移させる方針を job type ごとに定義する
- 予想実行では、最低限必要なデータが揃っていれば degraded mode を許可するかどうかを明示する
- ResultDayCollectionRequest は、その日に期待される全 ResultRaceCollectionRequest の完了を見て Complete または Incomplete を確定する
- EntityProfileSyncRequest の失敗はレース結果保存自体を巻き戻さず、取得障害として別管理にする

## データソース抽象化

### Provider 境界

- RaceProvider
- RaceScheduleProvider
- RaceCardProvider
- RaceResultProvider
- HorseHistoryProvider
- TrackConditionProvider

### 抽象化の狙い

- JRA 固有の画面遷移を AgentClient 全体へ漏らさない
- 地方競馬追加時は Provider 実装とマッピング定義の追加で吸収する
- ジョブ種別は共通にし、ProviderType と Payload で差分を表現する

## 永続ストア方針

### 必須条件

- プロセス再起動後もジョブが失われない
- 複数状態の更新を破損しにくい
- 検索条件で Ready ジョブを引ける
- リース期限切れジョブを再取得できる
- 監査のため履歴を追える

### 推奨案

- 第一候補: API 配下または Infrastructure 配下の永続 DB に Job テーブルを持つ
- 第二候補: AgentClient 専用の SQLite を用意し、ローカル単体でも継続実行できるようにする

初期段階では SQLite で開始し、後に API 側集中管理へ移行できるよう、IJobStore 抽象を先に導入する。

### 保存対象

- Job
- JobDependency
- JobExecutionLog
- JobCheckpoint
- JobLeaseHistory

## 実装方針

### フェーズ 1: ジョブ基盤の導入

- AgentClient に IJobStore を導入する
- SQLite 実装を追加する
- 既存の ProcessingStateStore を prediction 専用から汎用 job store へ置き換える
- ジョブ状態遷移、lease、retry、dead letter を実装する

### フェーズ 2: 抽出処理のジョブ化

- 現在の ScrapingRegistrationService を単純ループから dispatcher へ再構成する
- 予定収集、出馬表収集、結果収集を依頼ジョブと実行ジョブへ分解する
- 結果収集は「月探索」「日探索」「日次収集」「レース収集」に分解する
- 日別完了状態と再抽出待ち状態を導入する
- 各ジョブで checkpoint を記録する

### フェーズ 3: リアルタイム優先制御

- Scheduler に LiveMode、PreRaceMode、IdleMode 判定を追加する
- 開催日、開催時刻、公開状況をもとにバックフィル優先度を切り替える
- 当面はグローバル同時実行数を 1 に固定し、その中で優先度だけを切り替える
- リアルタイム抽出から過去成績補完ジョブを生成できるようにする

### フェーズ 4: 予想処理の再構成

- PredictionPreparation と PredictionExecution を分離する
- 入力データ不足時の WaitingDependency 制御を実装する
- 予想結果に入力データ版と根拠情報を紐づける

### フェーズ 5: 投稿処理の追加

- 投稿先別の Publisher 実装を追加する
- 予想投稿をジョブ化する
- AI ベースの投稿文整形を補助機能として接続する

### フェーズ 6: Provider 拡張

- JRA Provider の責務を明確化する
- 地方競馬用 Provider を追加可能なインターフェースへ整理する
- 主催者ごとの ID 解決とレース時刻表現の差異を吸収する

## コンポーネント再編案

### 残すもの

- スクレイパー本体
- PredictionWorkflow の中核ロジック
- API 経由の書き込みサービス

### 分割または置換するもの

- ScrapingRegistrationService
  - scheduler と dispatcher に分離する
- PredictionExecutionService
  - prediction worker に置換する
- ProcessingStateStore
  - 汎用 job store に置換する

## 監視と運用

### 必要な可観測性

- ジョブ投入数
- ジョブ成功率
- ジョブ平均待機時間
- lease timeout 回数
- dead letter 件数
- Provider 別失敗率
- API 登録失敗率

### 運用用クエリ/機能

- 未完了ジョブ一覧
- 依存待ちジョブ一覧
- dead letter 再投入
- 特定開催日の再収集要求
- 特定馬の過去成績再収集要求

## 実施計画

### 直近で着手すべき項目

1. AgentClient の責務を「ジョブ実行クライアント」に固定する
2. 汎用ジョブモデルと状態遷移を定義する
3. SQLite ベースの IJobStore を導入する
4. 既存の予想キューを新 job store へ移行する
5. 出馬表収集から過去成績補完要求を発行できるようにする

### 実装順の理由

- 再開可能なジョブ基盤がないと、後続の優先度制御や依存制御を安全に実装できない
- リアルタイム抽出とバックフィルの競合制御は job store と scheduler が前提になる
- 投稿機能はジョブ基盤完成後に追加しても主系統を壊しにくい

## 決めておきたい詳細項目

- LiveMode へ移行する時刻判定を、開催日ベース、発走時刻ベースのどちらで管理するか
- 過去成績不足時に、予想を待たせるか、不完全データで予想するか
- ジョブ永続ストアをローカル SQLite で完結させるか、API 管理に寄せるか
- 地方競馬をいつのフェーズで対象に含めるか

## 結論

AgentClient は、AI が判断を主導するアプリケーションではなく、優先度制御と途中再開が可能なジョブ実行基盤として再設計するべきである。

特に重要なのは以下である。

- リアルタイム抽出とバックフィルの優先度制御
- リアルタイム要求から過去データ補完ジョブを派生させる仕組み
- lease と checkpoint を持つ永続 job store
- JRA 固定にしない Provider 境界

この方針で進めれば、まずは中央競馬を安定運用しつつ、将来の地方競馬対応と投稿自動化を無理なく拡張できる。