# Lambda 異常終了時のエラー案内を分かりやすくする

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-10
- Updated: 2026-09-10

## Context

SQS から Collector Lambda を起動したものの、ジョブ結果を API へ報告できないまま Lambda が異常終了すると、通知は dead-letter queue（DLQ）へ移動する。API の `CollectionDeadLetterQueueReconciler` は該当ジョブを失敗へ確定するが、現在の `LastError` は次の固定文言である。

> Message moved to the collection dead-letter queue (Lambda execution failed).

この文言は利用者にとって、何が失敗したのか、画面で原因を確認できるのか、次に何をすべきかが分かりにくい。また、SQS の redrive で DLQ に入るメッセージは元の配送通知であり、Lambda の例外本文を含まないため、この経路だけから具体的な原因を断定することはできない。

通常の処理例外や内部 deadline 超過は Collector が API へ具体的な失敗理由を保存する既存経路を維持する。本変更は、その報告が行われないまま Lambda 呼び出し自体が失敗した場合のフォールバック案内を対象とする。

## Goals

- DLQ や Lambda の仕組みを知らなくても、収集処理が異常終了したことを理解できる
- この画面だけでは詳細原因を取得できないことを明示し、誤った原因を表示しない
- 原因確認とジョブのリランという次の対応を案内する
- Collector が保存済みの具体的なエラーを、汎用文言で上書きしない

## Non-goals

- CloudWatch Logs の内容を管理画面へ取り込む
- Lambda のログ画面への直接リンクを追加する
- DLQ、再試行、停止ポリシーを変更する
- 既に保存されている過去ジョブのエラー文言を移行する

## Experience and interaction design

ジョブ詳細の既存エラー警告、試行履歴、技術情報のレイアウトは変更しない。Lambda が結果を報告できないまま異常終了した場合だけ、固定の英語文を次の日本語案内へ置き換える。

> 収集処理が、結果を記録できないまま異常終了しました。原因の詳細は Lambda の実行ログで確認してください。原因を解消した後、このジョブをリランしてください。

3文すべてを短い案内として警告内に常時表示する。完全な文言は既存の技術情報欄でも確認できる。`ErrorMessageSplitter` はブラウザログ等の長い技術情報だけを折りたたむ既存用途のままとし、本案内を分割するための特別扱いは追加しない。

警告は既存どおり `role="alert"` 相当の `RaceOpsAlert` を使い、色だけに依存せず見出しと文章で状態を伝える。新しい操作やレスポンシブレイアウト変更は加えない。

## Documentation updates

- `docs/01-lambda-collector-architecture.md`: Lambda が API へ失敗理由を報告できない場合の DLQ フォールバック表示と、その制約を AWS 構成の正本へ追記する。
- この change record: 背景、表示文言、実装境界、受け入れ条件、検証結果の記録元とする。

## Technical impact

- `CollectionDeadLetterQueueReconciler` が `FailJobAsync` へ渡す固定エラー文言を変更する。
- `FailJobAsync` の既存仕様を確認し、既に具体的なエラーを持つ終端ジョブを汎用文言で上書きしないことをテストで固定する。
- ジョブ詳細は既存の `ErrorMessageSplitter` と `RaceOpsAlert` を継続利用し、新しい UI コンポーネントや CSS は追加しない。

## Decisions

- 実際には取得できない例外原因を推測して表示しない。詳細確認先を Lambda の実行ログとして案内する。
- 利用者向け本文では `dead-letter queue` や `DLQ` を主要説明に使わない。配送基盤の詳細は技術情報と設計文書に留める。
- RequestId は DLQ の元メッセージから得られないため、本変更では表示しない。ジョブ ID と更新時刻を手掛かりにログを確認する。
- ログ取得 API や CloudWatch への直接リンクは、権限・リージョン・ログストリーム特定方法を別途設計する必要があるため対象外とする。

## Acceptance criteria

- Lambda が API へ結果を報告できないまま異常終了し、配送通知が DLQ へ移った場合、ジョブ詳細に日本語で「結果を記録できないまま異常終了した」ことが表示される
- 表示は、詳細原因が Lambda の実行ログにあることと、原因解消後にリランすることを案内する
- 固定の英語文 `Message moved to the collection dead-letter queue (Lambda execution failed).` は新しい失敗記録に使用されない
- Collector が API へ具体的な失敗理由を保存済みの場合、その理由は DLQ 回収によって汎用文言へ置き換わらない
- DLQ メッセージは従来どおり回収後に削除され、失敗回数の記録、閾値到達時の収集停止と通知は変わらない
- ジョブ詳細の既存エラー警告は狭い画面でも文章が欠落せず、読み上げ対象のままである

## Delivery plan

1. `CollectionDeadLetterQueueReconciler` のフォールバック文言と上書き条件を実装する。
2. DLQ 回収時の表示用エラー、具体的エラーの保持、メッセージ削除と停止閾値の既存挙動を API テストで確認する。
3. API と UI の関連テスト、ビルド、`git diff --check` を実行する。
4. 検証結果と設計差分を本記録へ追記し、状態を `Implemented` にする。

## Verification record

- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --filter FullyQualifiedName~CollectionDeadLetterQueueReconcilerTests --no-restore`: 成功（3件、失敗0件）
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore`: 成功（123件、失敗0件、既存の明示的スキップ1件）
- `dotnet build HorseRacingPrediction.sln --no-restore`: 成功（警告0件、エラー0件）
- `git diff --check`: 成功

実装は、DLQ 回収専用の状態更新経路で未終端ジョブだけを失敗へ確定する。通常例外により既に終端状態と具体的なエラーが保存されているジョブは変更しない。既存の警告コンポーネントをそのまま利用するため、レイアウト、読み上げ、狭幅表示の構造に変更はない。

## Deviations and follow-up

- 計画時は先頭文だけを常時表示し、後続文を `ErrorMessageSplitter` で補足表示する想定だった。実装確認により同コンポーネントはブラウザログ等の技術マーカーだけを分割することが分かったため、短い3文をすべて警告内に常時表示する形へ記録を訂正した。表示階層や操作に影響する設計変更ではない。
- CloudWatch Logs の直接表示やリンクは対象外のままである。
