# 成績収集を過去5日まで対象にする

- Status: Implemented
- Created: 2026-09-08
- Updated: 2026-09-08

## Context
9/8基準の自動登録は過去2日までのため、9/5が対象外になっていた。

## Goals
JSTの当日から5日前まで（両端を含む）の開催日を成績収集対象にする。9/8なら9/3〜9/8となる。

## Decisions
ユーザーの「過去5日に変更してください」を本仕様の明示的承認として記録する。アプリ既定値、配布設定、本番LambdaとTerraformの設定を5に揃える。

## Non-goals
監視処理によるリース失効の修正、手動ジョブ投入、範囲外のバックフィルは含めない。

## Documentation updates
- `docs/22-collector-design.md`: 自動登録の既定範囲を過去5日と明記する。収集設定の設計説明を維持する。

## Acceptance criteria
- ResultLookbackDaysの既定値・配布設定・本番Lambda設定が5である。
- 当日〜5日前の開催日が対象で、開催なし・収集完了済みの日付の除外は既存動作を維持する。
- 既存の設定テストと登録統合テストが成功する。

## Verification record
- アプリ既定値、appsettings.json、TerraformのLambda環境変数を5に変更。
- 本番Lambdaの既存環境変数を保持し、RevisionIdを条件として `AgentProcessing__ResultLookbackDays=5` を反映。再取得で `LastUpdateStatus=Successful` と設定値5を確認。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --artifacts-path .artifacts/result-lookback-five-days --filter 'FullyQualifiedName~AgentProcessingOptionsTests|FullyQualifiedName~ScrapingRegistrationServiceIntegrationTests' --verbosity quiet`: ビルド成功、4件合格。
- 通常出力先での初回ビルドは既存Collectorプロセスのファイルロックで失敗したため、分離出力先で再検証した。
- `git diff --check`: 成功。Terraform CLIは未導入のためvalidate未実行。変更は既存環境変数マップへの文字列設定1件追加。

## Deviations and follow-up
仕様差分なし。新設定は次回の収集計画に適用される。9/5のジョブ投入・取得完了は今回の検証範囲外。調査済みの監視処理のリース回収不具合は別途修正が必要。
