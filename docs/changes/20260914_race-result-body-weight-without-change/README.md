# 増減値のないレース結果馬体重への対応

- Status: Implemented
- Owner: Codex
- Created: 2026-09-14
- Updated: 2026-09-14

## Context

2025年2月16日東京11RのJRA公式レース結果では、サトノカルナバルの馬体重が増減値なしの `518` と掲載されている。現在のレース結果パーサーは `518(-2)`、`518(0)`、`518(初出走)` のような括弧付き形式だけを受理するため、`JraValueParseException` となる。

## Goals

- 3桁の馬体重だけが掲載された結果を正常に収集する。
- 増減値が掲載されていない場合は欠損として保持する。

## Non-goals

- 馬体重が空欄の場合の既存動作を変更しない。
- 数字以外の未知の値を正常値として許容しない。
- 障害の再取得操作や本番データをこの変更で直接更新しない。

## Documentation updates

変更記録以外の文書更新は不要。`docs/22-collector-design.md` を確認したが、個別HTML値の許容形式はパーサーとテストが正規の仕様であり、収集基盤設計の変更には当たらない。

## Technical impact

`RaceResultPageParser` の馬体重正規表現で括弧付き増減部を任意にする。括弧部がなければ `BodyWeightChange` は `null` とする既存モデルをそのまま利用する。

## Decisions

- `518` は `BodyWeight=518`、`BodyWeightChange=null` として受理する。
- `518()` や `計不` など、既知形式ではない非空値は引き続き `JraValueParseException` とする。
- ユーザーは2026年9月14日、原因調査結果と上記修正方針に対して「そのように対応」と明示承認した。

## Acceptance criteria

- **AC-1 (T1):** 馬体重 `518` を含むレース結果を解析すると、馬体重518、増減値なしとして成功する。
- **AC-2 (T1):** `482(0)`、`494(+2)`、`400 (初出走)` の既存形式が従来どおり解析できる。
- **AC-3 (T1):** `計不` などの非空かつ未知の形式は引き続き `JraValueParseException` になる。

## Delivery plan

パーサーの許容形式と回帰テストを同じ変更として実装し、対象テスト、ソリューションビルド、CI相当のフォーマット検証を実行する。

## Task plan

- **T1:** Owner=Codex; Model tier=High capability; Depends on=なし; Write scope=`RaceResultPageParser.cs`、同テスト、当変更記録; Verification=パーサーテスト、ビルド、フォーマット検証; Completion evidence=関連58テスト成功、全体ビルド成功; State=Verified。

## Review gates

- **Design and task-split review:** Codexが原因値、JRA公式表示、正規表現、既存のnullableモデルを照合。単一の局所修正でAC-1〜AC-3を検証でき、委譲は不要と判断した。
- **Pre-implementation review:** T1は承認済みで依存なしのため `Runnable`。対象外のユーザー変更には触れず、パーサー、テスト、当記録だけを書き込む。
- **Checkpoint review:** 正規表現の括弧部のみを任意化し、増減値なしの実例テストを追加。既存の括弧付き形式と未知値拒否を含む関連58テストが成功した。
- **Final review:** T1およびAC-1〜AC-3はすべて `Verified`。変更は承認済み3ファイルに限定され、承認範囲内の未完了事項はない。

## Verification record

- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --no-restore --filter "FullyQualifiedName~RaceResultPageParserTests"` — 成功（58/58）。AC-1〜AC-3を検証。
- `codegraph sync .` — 成功（既存インデックスは最新）。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` — 成功。
- `dotnet build HorseRacingPrediction.sln --no-restore` — 成功（0エラー）。別プロセスのtesthostによる一時的なDLLロック再試行警告5件が出たが、再試行後に全プロジェクトをビルドできた。

## Deviations and follow-up

設計との差分、残課題ともになし。本番前環境の障害再取得はデプロイ後の運用操作であり、このコード変更には含めない。
