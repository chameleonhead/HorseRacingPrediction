# Fluent UI 再構成 引き継ぎ

## 現在地

変更セットは `Approved`。カスタム CSS を削除して Fluent UI 標準部品中心に再構成する方針は承認済みだが、全画面への展開は未完了。

直近コミット:

- `94c9edd Rebuild jobs UI with Fluent components`
- `469e9fd Align collection headers with design system`

作業ツリーはクリーン。

## 実装済み

- `src/HorseRacingPrediction.Api/wwwroot/app.css` を作り直した。旧 CSS の Button、Field、Card、Dialog、table、badge の視覚上書きは削除済み。現在の CSS は app shell、responsive、ドメイン行の配置だけを担う。
- `Shared/DesignSystem/` を追加し、`RaceOpsPageHeader`、`RaceOpsStatusBadge`、`RaceOpsObjectList`、`RaceOpsObjectListItem` を置いた。いずれも API client に依存しない表示専用 component。
- `/jobs` は上記 component と `FluentGrid`、`FluentSelect`、`FluentTextField`、`FluentButton` を使う形へ移行済み。行では英語の処理種別と DeduplicationKey を主表示から外した。
- レース、馬、騎手、調教師、馬主、予想票の collection header は `RaceOpsPageHeader` に移行済み。各一覧行、詳細、編集はまだ旧 markup のまま。

## 視覚比較から残った問題

ユーザー提供の比較画像では、旧実装はモックに対して検索エリアが高い、一覧がカード化している、技術名が主表示になる、状態・対象・更新の視線順が分断されていた。再構成後の `/jobs` はまだ実ブラウザー比較をしていないため、次作業の最初に確認する。

アプリ内ブラウザーは ChatGPT を不安定化させるため使わない。Windows automation も既存 Edge タブの URL を安全に判定できず停止した。比較する場合は、既存タブを操作せず、外部ブラウザーの InPrivate / 一時プロファイルだけを使う。

## 次に行うこと

1. `/jobs` を 1440 px、720 px、320 px でモックと比較する。行密度、検索バー、状態ラベル、ナビゲーション、dialog を対象にする。
2. 画面比較で得た調整を `RaceOpsObjectListItem` と最小 CSS に反映する。標準 Fluent component の色・形・余白を CSS で再定義しない。
3. collection 一覧を `Races`、`Horses`、`Jockeys`、`Trainers`、`Owners`、`Predictions`、`AcquisitionStatuses` の順で `RaceOpsObjectList` に移行する。page component は API 呼び出し、URL 同期、状態遷移だけを残す。
4. 詳細・編集画面を、共通 header、関連一覧、technical details、フォーム section に分割する。詳細のオブジェクト別仕様は変更セットと `docs/design-guidelines.md` を参照する。
5. navigation / app shell はまだ手書き `aside` / `NavLink` が残る。Fluent UI Blazor v4 の対応 component を使う形へ移し、provider は layout root に一箇所だけ置く。

## 検証と記録

最後に成功した検証:

- `dotnet build src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --no-restore -v:minimal`
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -v:minimal`（96 件）

各画面単位で、変更セットの README に実装内容・検証・未完了範囲を追記してからコミットする。SQLite の `eventstore.db-shm` と `eventstore.db-wal` が生成された場合は、サーバー停止を確認してから除去し、コミットに含めない。
