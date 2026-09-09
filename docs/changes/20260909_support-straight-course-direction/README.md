# JRA直線コース方向の収集対応

- Status: Implemented
- Owner: HorseRacingPrediction team
- Created: 2026-09-09
- Updated: 2026-09-09

## Context

HorseHistoryDiscovery の基本情報収集中に、2026年8月15日 新潟11RのRaceResultページが `Course.Direction` の未知値として拒否された。JRAのコース表記は `芝・直` であり、現在の `CourseDirection` と保存処理が `左` / `右` だけを既知値として扱うことが原因である。

- RaceResult URL: `https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1004202602071120260815/CA`
- FieldName: `Course.Direction`
- RawValue: `芝・直`
- RequestId: `da3b5be3-cc6e-5155-af00-38f94bd3ca4e`

RaceCardもRaceResultと同じ `ParseCourseSpec` を利用するため、解析モデルを正しく拡張すれば両ページ種別で同じ表記を処理できる。一方、収集Workflowにはページ種別ごとに方向コード変換があり、すべての経路で明示的な対応が必要である。

## Goals

- JRAの正式な直線コース表記 `芝・直` を既知値として解析する。
- 方向情報を欠落させず、既存の `DirectionCode` に `直` として保存する。
- RaceResultとRaceCardの新規収集・再取得の全経路で同じ値を維持する。
- 左回り・右回り・レイアウト注記・未知方向の既存挙動を維持する。

## Non-goals

- 既に解析失敗または未収集となったレースの一括再収集。
- 方向適性スコアの算出方式変更。
- JRAに存在が確認されていない方向表記の許容。
- データベーススキーマの変更。`DirectionCode` は既存の文字列項目を利用する。

## Documentation updates

- `docs/10-domain-design.md`: Raceの `DirectionCode` の既知値を `左` / `右` / `直` と明記し、ドメイン上の正規値の参照先とする。
- `docs/23-jra-scraping-redesign.md` と `docs/24-jra-html-change-diagnostics.md` を確認した。前者は過去の設計・監査記録、後者は診断手順であり、今回の正規値追加によって記載内容の変更は不要。

## Technical impact

- `CourseDirection` に直線を表す値を追加する。
- `RaceResultPageParser.ParseCourseSpec` で方向トークン `直` を直線として解析する。この共通処理はRaceCardにも適用される。
- `JraRaceResultCollectionWorkflow`、`JraRaceCardCollectionWorkflow`、RaceCard再取得経路で直線方向を `DirectionCode = "直"` に変換する。
- パーサーとWorkflowに回帰テストを追加する。DB migrationは不要。

## Decisions

- `直` を `Layout` や方向なしとして扱わず、`CourseDirection` の独立した既知値として扱う。左・右と同じJRA方向表記位置に現れ、下流の方向別履歴集計でも区別できる必要があるため。
- 永続化コードはJRA表記と既存コード体系に合わせて `直` とする。
- 未知の方向は引き続き `JraUnexpectedValueException` とし、将来の表記変更を黙って欠落させない。

## Acceptance criteria

1. `1,000メートル（芝・直）` を含むRaceResultスナップショットを解析すると、距離1000m、芝、直線方向、レイアウトなし、RawLayout=`芝・直` が得られる。
2. 同じ共通パーサーを使うRaceCardでも `芝・直` をエラーなく解析できる。
3. RaceResult収集、RaceCard収集、RaceCard再取得の各書き込み要求で `DirectionCode` が `直` になる。
4. `左`、`右`、`芝・右 外`、`芝 外内` の既存テストが成功する。
5. `北` など未定義の方向は従来どおり `FieldName=Course.Direction` の例外になる。
6. Scrapingプロジェクトの非Externalテストとソリューション全体のビルドが成功する。

## Delivery plan

1. 解析モデルと共通パーサーを拡張し、直線表記のパーサーテストを追加する。
2. RaceResult / RaceCard / RaceCard再取得の方向コード変換を拡張し、Workflowテストで保存値を検証する。
3. 関連回帰テスト、ビルド、`git diff --check` を実行する。
4. 検証結果と設計差分を本記録へ追記し、状態を `Implemented` に更新する。

## Verification record

- 2026-09-09: `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --filter "FullyQualifiedName!~E2ETests" --no-restore` — 成功（189件、失敗0件、スキップ0件）。`芝・直` の解析とRaceResult / RaceCard通常収集 / RaceCard再取得の `DirectionCode=直` 保存を含む。
- 2026-09-09: `dotnet build HorseRacingPrediction.sln --no-restore` — 成功（警告0件、エラー0件）。
- 2026-09-09: `git diff --check` — 成功。
- 実サイトへの再取得はデータ変更を伴うため本変更の検証には含めていない。対象のHorseHistoryDiscovery再実行で運用確認できる。

## Deviations and follow-up

- 承認済み設計からの差分はない。
- この変更だけでは失敗済みの対象レースは自動再実行されない。実装後、HorseHistoryDiscoveryまたは対象の再取得操作を再実行する必要がある。
