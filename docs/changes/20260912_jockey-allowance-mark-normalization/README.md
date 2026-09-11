# 騎手名の減量記号正規化

- Status: Implemented
- Owner: HorseRacingPrediction team
- Created: 2026-09-12
- Updated: 2026-09-12

## Context

JRA の出馬表では、見習騎手・女性騎手等の負担重量の減量を `▲`、`△`、`☆`、`★`、`◇` などの記号で騎手名の直前に表示する。これらは騎手名の一部ではない。

現行コードには一部の収集経路で先頭記号を除去する処理があるが、表示名と正規化名で処理が統一されておらず、過去または別経路で収集されたデータには記号付きの騎手名が残り得る。実例として森田誠也の詳細画面に `▲` が表示されている。

## Goals

- すべての騎手データ収集経路で、JRA の減量記号を騎手名から除外する。
- 表示名と正規化名に同じ規則を適用し、記号の有無による別 ID・重複登録を防ぐ。
- 保存済みの記号付き騎手データを安全かつ再実行可能な方法で補正する。
- 森田誠也を含む騎手詳細・一覧・レース表示で、騎手名に減量記号を表示しない。

## Non-goals

- 出馬表上で減量情報を独立した属性として新規保存・表示すること。
- 予想印として使用する `▲`、`△` 等の意味や表示を変更すること。
- 騎手名の一般的な表記ゆれ統合を今回の補正へ含めること。

## Documentation updates

- `docs/10-domain-design.md`: 騎手名の正規化では JRA の減量記号を識別子・表示名に含めないことを、正規化方針の正本へ追記する。
- `docs/00-system-architecture.md` を確認したが、コンポーネント構成や責務分担は変わらないため更新しない。

## Technical impact

- 騎手名の正規化処理を共通化し、少なくとも `▲`、`△`、`☆`、`★`、`◇`、`▽` を先頭から除去する。前後空白や記号後の空白も吸収する。
- 出馬表パーサー、Collector の騎手 upsert、出走登録時の騎手解決で共通規則を使用する。
- 呼び出し側から正規化名が渡された場合にも、表示名と同様に減量記号を除去してから ID を決定する。
- 保存済みデータの補正はイベントソーシングの訂正経路を使用し、既存の参照関係を壊さない。記号付き ID と記号なし ID が併存する場合は、自動的に参照を付け替えず、重複を検出して明示的に記録する。

## Decisions

- 減量記号は騎手の恒久的な属性や氏名ではなくレース時点の負担重量表現なので、騎手マスターの表示名・正規化名・ID生成キーから除外する。
- 防御をパーサーだけに置かず、永続化境界でも正規化する。これにより別の取得元や将来のパーサーから渡された値にも同じ規則を適用する。
- 既存 ID は参照整合性を優先して維持し、同一集約内の表示名・正規化名を訂正する。ID統合が必要な重複は今回の自動補正対象外とし、検出可能にする。

## Acceptance criteria

| ID | Criterion | Status |
| --- | --- | --- |
| AC-1 | 対象記号 `▲`、`△`、`☆`、`★`、`◇`、`▽` のいずれかが先頭に付いた騎手名を収集すると、表示名と正規化名から記号が除去される | Verified |
| AC-2 | 記号前後の空白、および連続する対象記号があっても、騎手名本体を保持して正規化できる | Verified |
| AC-3 | 記号付きと記号なしの入力から同じ騎手 ID が決定され、新規重複を作らない | Verified |
| AC-4 | 記号を含まない騎手名および予想印の保存・表示には回帰がない | Verified |
| AC-5 | 保存済みの記号付き騎手を列挙・補正でき、再実行しても追加変更や重複を生じない | Verified |
| AC-6 | 森田誠也の詳細表示が `森田 誠也`（既存の空白規則に従う）となり、先頭に `▲` が表示されない | Verified |
| AC-7 | 記号付き ID と記号なし ID の重複候補がある場合、参照を破壊せず検出結果が運用者に分かる | Verified |

## Delivery plan

1. 共通の騎手名正規化処理と単体テストを追加する。
2. 出馬表パーサーと Collector の全対象経路を共通処理へ接続する。
3. 保存済みデータを訂正する再実行可能な補正処理と、重複候補の検出を追加する。
4. パーサー、Collector、API、画面経路の回帰テストを実行する。
5. ローカル環境で森田誠也および他の対象記号を確認し、検証結果を本記録へ追記する。

## Verification record

- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --filter "FullyQualifiedName~RaceCardPageParserTests" --no-restore`: 20件成功。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --filter "FullyQualifiedName~JockeyNameNormalizerTests" --no-restore`: 10件成功。
- `dotnet test HorseRacingPrediction.sln --no-restore`: Contracts 38件、Domain 96件、Application 56件、Agents 107件、MachineLearning 14件、Infrastructure 11件、Scraping 218件成功・1件スキップ。起動中のローカル API が出力 DLL をロックしていたため、API プロジェクトのビルド段階のみ失敗。今回の対象プロジェクトとテストには失敗なし。
- ローカル API の騎手106件を調査し、減量記号付き19件（`▲` 9件、`△` 3件、`◇` 4件、`★` 1件、`☆` 2件）を既存 ID のまま訂正。訂正後の再走査は残件0、重複候補0。
- `tools/repair-jockey-allowance-marks.ps1 -WhatIf`: 106件走査、補正候補0、重複候補0。再実行時に変更が発生しないことを確認。
- ブラウザーで `/jockeys/jockey-9d449542-d548-50f1-929d-21dc12274cb3` を確認し、見出しが `森田 誠也` であることを確認。

## Deviations and follow-up

- 全ソリューションテストの API プロジェクト部分は、検証対象として起動中のローカル API が DLL をロックしていたため実行できなかった。対象コードを含む ApiClient、Scraping、Collector のビルドと関連テストは成功している。
