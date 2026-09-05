# 競馬予想ドメイン設計

## 目的

競馬情報を横断的に蓄積し、レース前の予想とレース後の結果を同じデータ基盤で管理するドメインを定義する。

本ドメインは以下を満たす。

- レース、馬、騎手、調教師に関する情報を一貫した形式で保持できる
- 正規化済みデータと原文テキストを並行して保持できる
- 複数の情報源から取得したデータを結合し、追跡可能にできる
- 予想と結果を同一 RaceId で対比できる
- 監査可能な変更履歴を保持できる
- 汎用的な競馬情報データベースとして、手動登録と外部取り込みを同時に扱える

## 前提システム構成

- アーキテクチャ: CQRS + Event Sourcing
- アプリケーション実装: ASP.NET Core Web API
- 提供範囲: API のみ（フロントエンドは対象外）
- 認証: API キー認証

### 認証運用（軽量）

個人利用を前提に、まずはシンプルな API キー運用を採用する。

- API キーは環境変数に設定する
- API リクエストは固定ヘッダーでキーを受け取る
- キー一致時のみ書き込み API を許可する
- 将来必要になればユーザー管理を追加可能な拡張点を残す

推奨例:

- 環境変数: HORSE_RACING_API_KEY
- リクエストヘッダー: X-Api-Key

任意拡張:

- API キーと UserId の紐付け
- AI 自動処理主体を User と同じ扱いで記録

## スコープ

対象:

- レース情報
- 出馬表情報
- 天気・馬場などの観測情報
- 馬、騎手、調教師の基本情報
- 予想情報（複数）
- 確定結果・払戻情報
- ソース原文と抽出済みファクト
- 登録履歴と訂正履歴

対象外:

- 予想アルゴリズムの詳細実装
- UI の詳細設計

## 設計原則

1. 正規化データと原文データを分離する
2. 外部キーと正準 ID を分離する
3. 時点依存値は観測・スナップショットとして保持する
4. すべての正規化値を元データへトレース可能にする
5. 書き込みはコマンドとイベント、読み取りは用途別ビューへ分離する
6. 変更は上書きではなくイベント追記で管理する

## レースライフサイクル

レースは以下の状態で管理する。

- Draft: 開催情報のみ
- CardPublished: 出馬表公開
- PreRaceOpen: 予想受付中
- InProgress: 発走済み
- ResultDeclared: 結果確定
- PayoutDeclared: 払戻確定
- Closed: 評価処理完了

### 予想と結果の扱い

- 予想はレース前の判断履歴として保持する
- 結果は確定情報として別モデルに保持する
- 結果確定後も予想は不変履歴として残す
- 予想評価は結果反映後に生成する

## ドメインモデル

```text
Race
 ├─ RaceCard
 │   └─ Entry
 ├─ WeatherObservation
 ├─ TrackConditionObservation
 ├─ RaceResult
 ├─ PayoutResult
 ├─ PredictionTicket (0..*)
 └─ SourceDocument

PredictionTicket
 ├─ PredictionMark (0..*)
 ├─ BettingSuggestion (0..*)
 ├─ PredictionRationale (0..*)
 └─ PredictionEvaluation (0..*)
```

## コアエンティティ

### Race

- RaceId
- ExternalRaceKeys
- RaceDate
- RacecourseCode
- MeetingNumber
- DayNumber
- RaceNumber
- RaceName
- GradeCode
- SurfaceCode
- DistanceMeters
- DirectionCode
- LifecycleStatus
- RaceCardId
- RaceResultId
- PayoutResultId

### Entry

- EntryId
- RaceId
- HorseId
- JockeyId
- TrainerId
- GateNumber
- HorseNumber
- AssignedWeight
- SexCode
- Age
- DeclaredWeight
- DeclaredWeightDiff

### RaceResult

- RaceResultId
- RaceId
- DeclaredAt
- WinningHorseId
- StewardReportText

### EntryResult

- EntryResultId
- RaceId
- EntryId
- FinishPosition
- OfficialTime
- MarginText
- LastThreeFurlongTime
- AbnormalResultCode
- PrizeMoney

### PayoutResult

- PayoutResultId
- RaceId
- WinPayouts
- PlacePayouts
- QuinellaPayouts
- ExactaPayouts
- TrifectaPayouts

### Horse

- HorseId
- ExternalHorseKeys
- RegisteredName
- NormalizedName
- SexCode
- BirthDate

### Jockey

- JockeyId
- ExternalJockeyKeys
- DisplayName
- NormalizedName
- AffiliationCode

### Trainer

- TrainerId
- ExternalTrainerKeys
- DisplayName
- NormalizedName
- AffiliationCode

## 予想モデル（複数予想対応）

同一 RaceId に対して複数の予想を保持できる。

- 1 レース : N PredictionTicket
- 1 PredictionTicket : N PredictionMark
- 1 PredictionTicket : N BettingSuggestion
- 1 PredictionTicket : N PredictionRationale
- 1 PredictionTicket : N PredictionEvaluation

### PredictionTicket

- PredictionTicketId
- RaceId
- PredictorType
- PredictorId
- TicketStatus
- PredictedAt
- Version
- ConfidenceScore
- SummaryComment

### PredictionMark

- PredictionMarkId
- PredictionTicketId
- EntryId
- MarkCode
- PredictedRank
- Score
- Comment

### BettingSuggestion

- BettingSuggestionId
- PredictionTicketId
- BetTypeCode
- SelectionExpression
- StakeAmount
- ExpectedValue

### PredictionRationale

- PredictionRationaleId
- PredictionTicketId
- SubjectType
- SubjectId
- SignalType
- SignalValue
- ExplanationText

### PredictionEvaluation

- PredictionEvaluationId
- PredictionTicketId
- RaceId
- EvaluatedAt
- EvaluationRevision
- HitTypeCodes
- ScoreSummary
- ReturnAmount
- Roi

## 観測モデル

### WeatherObservation

- WeatherObservationId
- RaceId
- ObservationTime
- WeatherCode
- WeatherText
- TemperatureCelsius
- HumidityPercent
- WindDirectionCode
- WindSpeedMeterPerSecond

### TrackConditionObservation

- TrackConditionObservationId
- RaceId
- ObservationTime
- TurfConditionCode
- DirtConditionCode
- GoingDescriptionText

## 原文・監査モデル

### SourceDocument

- SourceDocumentId
- SourceType
- SourceName
- SourceUrl
- RetrievedAt
- ContentType
- RawText
- RawJson
- Checksum

### ExtractedFact

- ExtractedFactId
- SourceDocumentId
- SubjectType
- SubjectTemporaryKey
- FieldName
- RawValue
- NormalizedValue
- Confidence

### EntityAlias

- EntityAliasId
- EntityType
- CanonicalEntityId
- AliasType
- AliasValue
- SourceName
- IsPrimary

### DataRegistration

- DataRegistrationId
- RegisteredByType (User, Agent, System)
- RegisteredById
- RegistrationChannel
- RegisteredAt
- ReasonCode
- Comment

## 正規化方針

1. コード体系の統一
- 天気、馬場、性別、所属、グレード、方向はコード化

2. 表記ゆれ吸収
- 馬名、騎手名、調教師名、競馬場名に NormalizedName を持つ

3. 単位統一
- 距離: meter
- 斤量: kilogram
- 気温: celsius
- 風速: meter per second

4. 時点管理
- 天気、馬場、オッズ、馬体重、人気は観測時刻付きで保持

## CQRS + ES 設計

### 集約

- RaceAggregate
- HorseAggregate
- JockeyAggregate
- TrainerAggregate
- PredictionAggregate

### イベントストリーム

- Race-{RaceId}
- Horse-{HorseId}
- Jockey-{JockeyId}
- Trainer-{TrainerId}
- Prediction-{PredictionTicketId}

### コマンド

#### RaceAggregate

- CreateRace
- PublishRaceCard
- RegisterEntry
- RecordWeatherObservation
- RecordTrackConditionObservation
- StartRace
- DeclareRaceResult
- DeclareEntryResult
- DeclarePayoutResult
- CloseRaceLifecycle
- CorrectRaceData

#### HorseAggregate

- RegisterHorse
- UpdateHorseProfile
- MergeHorseAlias
- CorrectHorseData

#### JockeyAggregate

- RegisterJockey
- UpdateJockeyProfile
- MergeJockeyAlias
- CorrectJockeyData

#### TrainerAggregate

- RegisterTrainer
- UpdateTrainerProfile
- MergeTrainerAlias
- CorrectTrainerData

#### PredictionAggregate

- CreatePredictionTicket
- AddPredictionMark
- AddBettingSuggestion
- AddPredictionRationale
- FinalizePredictionTicket
- WithdrawPredictionTicket
- EvaluatePredictionTicket
- RecalculatePredictionEvaluation
- CorrectPredictionMetadata

### イベント

#### RaceAggregate

- RaceCreated
- RaceCardPublished
- EntryRegistered
- RaceWeatherObserved
- RaceTrackConditionObserved
- RaceStarted
- RaceLifecycleStatusChanged
- RaceResultDeclared
- EntryResultDeclared
- PayoutResultDeclared
- RaceDataCorrected
- RaceClosed

#### HorseAggregate

- HorseRegistered
- HorseProfileUpdated
- HorseAliasMerged
- HorseDataCorrected

#### JockeyAggregate

- JockeyRegistered
- JockeyProfileUpdated
- JockeyAliasMerged
- JockeyDataCorrected

#### TrainerAggregate

- TrainerRegistered
- TrainerProfileUpdated
- TrainerAliasMerged
- TrainerDataCorrected

#### PredictionAggregate

- PredictionTicketCreated
- PredictionMarkAdded
- BettingSuggestionAdded
- PredictionRationaleAdded
- PredictionTicketFinalized
- PredictionTicketWithdrawn
- PredictionTicketEvaluated
- PredictionEvaluationRecalculated
- PredictionMetadataCorrected

### イベント共通メタデータ

- EventId
- AggregateId
- AggregateType
- EventType
- OccurredAt
- Version
- PerformedByType
- PerformedById
- CorrelationId
- CausationId
- SourceDocumentIds
- RegistrationChannel

## 軽量不変条件

厳格固定ではなく、運用を邪魔しない「緩い制約」を採用する。

### レース状態遷移

- 基本遷移は Draft -> CardPublished -> PreRaceOpen -> InProgress -> ResultDeclared -> PayoutDeclared -> Closed
- 訂正時のみ、任意状態で CorrectRaceData を許可する
- 状態を巻き戻す必要がある場合は、上書きではなく訂正イベントで表現する

### 予想登録

- 同一 RaceId に対して複数 PredictionTicket を許可する
- 予想締切は厳密ブロックしない
- 発走後登録は TicketStatus で LateSubmitted として区別できるようにする

### 結果訂正

- 結果確定後の修正は許可する
- 修正時は PredictionEvaluation を再計算対象にマークする

## 予想評価の再計算ポリシー

ワークフロー管理自体はスコープ外とし、整合性を保つための最小ルールのみ定義する。

### 基本方針

- PredictionEvaluation は世代管理する
- 最新評価は EvaluationRevision の最大値を採用する
- 過去評価は履歴として保持する

### 再計算トリガー

以下のイベント発生時に、対象 PredictionTicket を再計算対象にする。

- RaceResultDeclared
- EntryResultDeclared
- PayoutResultDeclared
- RaceDataCorrected
- PredictionMetadataCorrected

### 再計算方式

- 自動再計算: 結果確定時に即時 EvaluatePredictionTicket を実行
- 手動再計算: 必要時に RecalculatePredictionEvaluation を実行
- どちらの方式を使うかは運用側で選択可能

### 不整合対策

- PredictionComparisonView に EvaluationStatus を持たせる
- 状態は Ready / RecalculationRequired / Failed を想定する
- RecalculationRequired の場合は最新結果との差分がありうることを API で明示する

### API 応答方針

- 評価が未実行でも予想と結果そのものは返す
- 評価が古い場合は stale フラグを返す
- 呼び出し側が再計算を実行するかどうかを判断できるようにする

## リードモデル

### RacePredictionContext

レース前の予想登録 API が参照する読み取りモデル。

- Race
- WeatherObservation
- TrackConditionObservation
- Entries
- RecentSignals

### RaceResultView

結果表示 API が参照する読み取りモデル。

- Race
- RaceResult
- EntryResults
- PayoutResult

### PredictionComparisonView

予想と結果の比較 API が参照する読み取りモデル。

- Race
- PredictionTickets
- PredictionEvaluations
- EntryResults

## 主要ユースケース

### ユースケース 1: レースと出馬表を登録する

1. CreateRace
2. PublishRaceCard
3. RegisterEntry
4. RacePredictionContext を更新

### ユースケース 2: 予想を登録する（複数可）

1. CreatePredictionTicket
2. AddPredictionMark / AddBettingSuggestion / AddPredictionRationale
3. FinalizePredictionTicket
4. 同一 RaceId に対して別の PredictionTicket を追加可能

### ユースケース 3: 結果を確定する

1. DeclareRaceResult
2. DeclareEntryResult
3. DeclarePayoutResult
4. EvaluatePredictionTicket
5. RaceResultView / PredictionComparisonView を更新

### ユースケース 4: データを訂正する

1. CorrectRaceData などの訂正コマンドを発行
2. 訂正イベントを追記
3. リードモデルを再投影

## API 設計方針（概要）

- 書き込み API はコマンドを受け取る
- 読み取り API は用途別リードモデルを返す
- API キーをヘッダーで受け取り、登録主体を監査情報へ反映する

## .NET プロジェクト構成案

```text
src/
  HorseRacingPrediction.Domain/
    Aggregates/
    Commands/
    Events/
    ReadModels/
    ValueObjects/
  HorseRacingPrediction.Application/
    CommandHandlers/
    QueryHandlers/
    Projections/
  HorseRacingPrediction.Infrastructure/
    EventStore/
    ReadStore/
    Authentication/
  HorseRacingPrediction.Api/
    Controllers/
    Middlewares/
```

## 次に定義すべきもの

1. コマンド/イベントの C# 契約
2. 集約ごとの不変条件
3. イベントストア永続化方式
4. API キー認証ミドルウェア
5. Projection 更新戦略
