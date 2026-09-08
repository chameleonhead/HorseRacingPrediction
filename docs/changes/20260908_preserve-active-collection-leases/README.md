# 監視処理による有効リースの破棄を修正する

- Status: Implemented
- Created: 2026-09-08
- Updated: 2026-09-08

## Context
本番調査で、5分ごとの監視が期限を確認せずRunningジョブをReadyへ戻し、Lambdaの完了更新が無効なリースとして拒否されることを確認した。

## Decisions
原因説明後のユーザーの「修正をお願いします」を承認として記録する。監視回収は対象ジョブ種別かつ期限が存在し現在時刻以下のリースに限定する。期限未設定・期限内のジョブは変更しない。状態更新拒否をAPIの警告ログへ記録する（リーストークンは記録しない）。

## Acceptance criteria
- 期限内のRunningジョブは監視後も元のリースで完了でき、再送通知が増えない。
- 期限ちょうど・期限超過の対象ジョブのみReadyへ戻り、古いリースの完了は拒否、新しい通知とリースで完了できる。
- 回収の繰り返しで重複通知が発生せず、対象外ジョブは保持される。
- 日時のUTCオフセットが異なっても同じ時刻として比較する。

## Documentation updates
- `docs/01-lambda-collector-architecture.md`: 現行の30分リースと期限切れ監視の条件を明記し、14分deadlineより短い10分リースという不整合を修正する。

## Verification record
- 修正前の回帰テスト3ケース中、有効リースを保持するケースが失敗し、問題を再現した。
- `ProcessingStateStore.RequeueRunningJobsAsync` を期限あり・期限到達済みに限定した。SQLiteのDateTimeOffset比較制約に合わせ、候補取得後に.NET上で比較する。
- リース不一致での状態更新拒否をAPI警告ログへ記録する。リーストークンは出力しない。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --artifacts-path .artifacts/lease-watchdog-fix --filter 'TestCategory!=External' --verbosity quiet`: ビルド成功、102件合格。
- 期限未設定ケースを追加後、同プロジェクトで `--filter 'FullyQualifiedName~RequeueRunningJobsAsync_'`: 回帰テスト4件合格。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --artifacts-path .artifacts/lease-watchdog-api --filter 'TestCategory!=External' --verbosity quiet`: ビルド成功、116件合格。
- `git diff --check`: 成功。既存のブラウザー変更はコミット対象から除外した。

## Deviations and follow-up
設計差分なし。コードと検証は完了。本番への反映は未実施であり、稼働中APIには修正版のデプロイが必要。

## Non-goals
手動再送、DB修正、既存の未コミットのブラウザー変更は含めない。
