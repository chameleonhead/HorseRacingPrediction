# 旧形式DLQメッセージからの収集再開

- Status: Approved
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-16
- Updated: 2026-09-16

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | DLQ reconcilerへ旧 `CollectionTaskNotification` v1の厳密な互換読取を追加した。 |
| Verification | Complete | 対象7件、Release build、非External全体（既存benchmarkの一時timeoutは単独再実行成功）を検証した。 |
| Deployment/operation | Not started | 本番DBバックアップ後に配備し、DLQ障害化、Recovery作成、再開、処理進行を段階確認する。 |

## Context

2026-09-16の本番調査では、管理画面に待機中1,770件、実行中0件が表示される一方、main SQSは空、DLQには63件、Lambdaは直近2時間の呼出しがなかった。DLQの標本は旧単件契約 `CollectionTaskNotification`（`taskId`、`dispatchGeneration`、`contractVersion: 1`）である。

現行CollectorとAPIのDLQ reconcilerは `CollectionDispatchEnvelope` の形だけを読取るため、旧単件メッセージを処理できない。reconcilerは不明なメッセージをDLQへ残し、Watchdogは配送済みReadyタスクを再配送しない。この組合せにより、対応するoutboxが配送済みのままReadyタスクが滞留し、容量制御下で後続配送も進まない。

単純なDLQ redriveは同じ旧メッセージを現行Collectorへ戻して再度失敗させるため行わない。また、main queue/DLQのpurgeやDBの直接書換えも行わない。

## Goals

- 旧 `CollectionTaskNotification` v1をDLQ reconcilerがTask IDとdispatch generationを推測なしで解釈できるようにする。
- 既存のDeadLetter・障害通知・Recoveryライフサイクルを通じて63件を監査可能に復旧する。
- 現行 `CollectionDispatchEnvelope` v2、破損/未知契約を残す安全性、世代ガードを維持する。
- 待機中タスクの配送を再開し、Lambda実行と完了件数の増加を本番で確認する。

## Non-goals

- SQS/Lambdaの並列度やPlaywrightバッチサイズを変更しない。
- DLQをpurgeしない。旧メッセージをmain queueへ直接redriveしない。
- Readyタスクを無条件に再配送するWatchdogへ変更しない。
- Snapshot-firstやスクレイピング処理自体の性能を変更しない。

## Experience and interaction design

画面の追加・変更は行わない。既存の `/jobs?view=attention` の障害グループ回復、パイプライン再開、`/jobs?view=recent` の進捗確認を使用する。運用中は件数を記録し、未知形式が1件でも残った場合は削除せず停止して内容を調査する。

## Documentation updates

- `docs/01-lambda-collector-architecture.md`: DLQ reconcilerが現行Envelopeに加えて移行元の厳密なv1単件通知を読めること、未知形式は保持することを記載する。
- `.github/workflows/collection-dlq-diagnostics.yml`: APIのDLQ関連ログ・直近バックアップ・障害グループを秘密情報を表示せず確認し、明示confirmation付きで `DeadLetterQueue` グループだけをRecovery・再開できる手動運用を追加する。
- 本change record: 配備前後の件数、イメージSHA、バックアップ、回復結果、Lambda/queue/画面の検証証拠を記録する。

## Technical impact

- DLQ本文の解析を、現行Envelopeと旧単件通知を区別する小さなparserへ分離する。
- APIのqueue設定と既定値をTerraformの物理DLQ名 `horse-racing-prediction-resource-collection-dlq` に一致させ、契約テストで固定する。
- 旧Readyタスクがmetadata属性を持たない場合に限り、canonical Race Resource ID（`yyyyMMdd:Course:Number`）をeffective date一致込みで厳密にfallback解析する。
- 旧通知は `contractVersion == 1`、空でないTask ID、正のdispatch generationを必須とし、1件だけのTask参照へ変換する。
- 現行Envelopeは現在の検証を維持する。旧通知にもEnvelopeを捏造せず、既存のTask単位reconcile経路へ明示的に渡す。
- valid/supportedなメッセージは既存規則どおり、各Task参照を世代付きでreconcileしてからSQSメッセージを削除する。terminalまたはsupersededなTaskをFailedへ戻さない。
- malformed、未知version、曖昧な形はTaskを推測せずDLQへ残し、エラーを記録する。
- 最初の現行世代TaskのDeadLetter化で、既存安全規則どおりpipelineは一時停止する。63件のreconcile完了を確認してから障害グループをRecoveryし、最後に明示的に再開する。

## Decisions

- 恒久的な移行互換としてAPI側DLQ reconcilerへv1読取を追加する。配備順序の差や保持期間中の旧メッセージ再出現にも安全に対応できるため、今回だけのDB修復スクリプトにはしない。
- Collector本体にv1実行互換を戻さない。旧メッセージは一度DeadLetterとして監査記録を作り、現行v2 EnvelopeでRecoveryする。
- 本番操作順は「バックアップ → 互換版配備 → DLQ reconcile確認 → Recovery作成 → pipeline再開」とする。reconcile前の再開やDLQ redriveはしない。
- Recoveryは既存APIを利用する。Recovery理由は通常登録のactive-task再利用対象外であり、停止したReadyタスクとは別の現行タスク・v2 outboxを作れる。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 正常な旧v1通知が現行世代TaskをDeadLetter化し、障害通知を作り、処理後にDLQメッセージを削除する。 | T1, T2 | API unit/integration test | Verified |
| AC2 | terminal、旧generation、重複配送を再処理してもTask/Attempt/障害通知を破損・重複させない。 | T1, T2 | idempotency/generation tests | Verified |
| AC3 | 現行v2 Envelopeのreconcile挙動を維持し、v1/v2混在batchを個別に処理できる。 | T1, T2 | mixed-contract tests | Verified |
| AC4 | malformed、未知version、曖昧なJSONは削除せずDLQに保持し、Task IDを推測しない。 | T1, T2 | invalid-contract tests | Verified |
| AC5 | canonical architecture文書が移行互換と安全規則を説明する。 | T3 | documentation review | Verified |
| AC6 | 配備前に本番DBバックアップ、queue/task件数、配備対象SHAを記録し、purge・直接redrive・DB直接更新を行わない。 | T4 | operation log and AWS evidence | Not started |
| AC7 | 本番DLQ 63件が既存障害ライフサイクルへ移され、未知/未処理メッセージが残る場合はRecovery前に停止して報告される。 | T4 | DLQ depth, API failure groups, logs | Not started |
| AC8 | 対象障害をRecoveryした後にpipelineを明示再開し、main queue/Lambda invocationが動き、waitingが継続的に減少し、同原因の新規DLQが発生しない。 | T4 | AWS metrics/logs and `/jobs?view=recent` | Not started |
| AC9 | Release build、関連テスト、非Externalテスト、`git diff --check`が成功する。 | T2, T5 | recorded command results | Verified |

## Delivery plan

1. 旧v1通知を厳密に識別するparserとreconciler互換を実装する。
2. v1/v2混在、世代/idempotency、破損/未知形式の回帰テストを追加する。
3. canonical architecture文書と本change recordを更新し、関連ビルド・テストを実行する。
4. コードをレビュー・コミット・pushし、CI成功を確認する。
5. 本番DBバックアップと事前値を取得して配備する。
6. DLQ reconcileの件数と障害グループを確認する。未知メッセージが残ればここで停止する。
7. 対象障害グループを既存APIでRecoveryし、pipelineを再開する。
8. main queue、Lambda、waiting/completed、DLQを時系列で確認し、結果を記録する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | v1互換parserとDLQ reconcileを実装 | Main | High capability | Approval | API collection controller files | T2 tests | reviewed diff | Verified |
| T2 | 契約・世代・混在・不正入力テストを追加 | Main | High capability | T1 | API test files | targeted tests | passing test output | Verified |
| T3 | canonical architectureとchange recordを更新 | Main | High capability | T1 | docs only | doc review | updated documents | Verified |
| T4 | CI後に本番へ段階配備・回復・監視 | Main | High capability | T1-T3, T5 | AWS/API operational state and change record evidence only | AWS/API/UI observations | before/after evidence | Dependent |
| T5 | 全差分と受け入れ条件の最終レビュー | Main | High capability | T1-T3 | read-only plus review fixes | build/tests/diff/status | reviewed diff and AC matrix | Dependent |

## Review gates

- **Design and task-split review (2026-09-16)** — Mainが本番証拠、現行parser、watchdog、既存Recovery経路を照合した。AC1–AC9はT1–T5へ追跡済みで、共有ファイルへの並列書込はない。危険なredrive/purge/DB直接更新を除外し、コードと本番操作を同じ高能力ownerが担当する。判断: 承認待ち。
- **Pre-implementation review (2026-09-16)** — ユーザー承認を受領。T1をRunnableからIn progress、T2–T5を依存順にDependentとした。既存v2経路を維持し、旧v1の3フィールドを厳密検証する最小差分から着手する。
- **Checkpoint review (2026-09-16, code)** — v1互換、混在、重複、未知version、追加フィールド、破損JSONを差分とテストで照合した。現行v2の既存テストを維持し、Task状態変更後だけメッセージを削除する既存順序も不変。T1–T3をVerified、T4–T5を配備待ちとした。
- **Checkpoint review** — parser/test完了時と本番reconcile完了時に実施する。
- **Final review** — 全ACと本番進行証拠を照合してからImplementedとする。

## Verification record

- 2026-09-16: `/jobs?view=recent` と管理APIでtotal 2,928、waiting 1,770、running 0、recent completed 1,158、pipeline未停止を確認した。
- 2026-09-16: main SQSはvisible/not-visible/delayedが0、DLQはvisible 63、Lambdaは直近2時間にinvocation/error/throttle/duration datapointなし、最新log streamは2026-09-15であることを確認した。
- 2026-09-16: DLQを削除せずvisibility timeout 0で標本確認し、本文が旧 `CollectionTaskNotification` v1であることを確認した。
- 2026-09-16: 現行Collector/API reconcilerはEnvelope形だけをdeserializeし、Watchdogは配送済みReadyタスクを再配送しないこと、既存Recovery APIは `CollectionReason.Recovery` で新タスクを作ることをコードから確認した。
- 2026-09-16: `CollectionPlatformOperationsServicesTests` 7件成功。正常v1、v1/v2混在、重複通知、contractVersion欠落/未知、追加フィールド、破損JSON、既存v2を検証した。
- 2026-09-16: Release solution buildは警告0・エラー0。`TestCategory!=External` はContracts 43、Domain 105、Application 57、Infrastructure 13、MachineLearning 14、Agents 106、Collector 224、API 223成功・1 skip。Scrapingは241件成功後、ローカルPlaywright benchmark 1件がNetworkIdle待機30秒で一時timeoutしたが、同一テストの単独再実行は0.8秒で成功した。
- 2026-09-16: commit `30a9335` のapp-ci `35086212436` とapp-deploy `35086212442` が成功。deployはAPI停止後のcollection-platform DB世代バックアップ、API health、Lambda image `sha-30a9335...` の反映を完了した。配備後もDLQ visible 63件が変わらなかったため、Recovery前の停止条件に従い読取専用ログ診断を追加した。
- 2026-09-16: 読取専用診断run `35087464233` で、10:50 UTCのcollection-platform backup（15,908,864 bytes）と、APIが旧既定名 `horse-racing-prediction-collector-dlq` を解決して `QueueDoesNotExistException` を反復していることを確認した。実在するTerraform DLQ名へ設定・既定値を揃え、再発防止の契約テストを追加する。
- 2026-09-16: 修正版deploy `35087589348` 成功後、DLQは63→42→21→3→0件へ減少した。診断run `35089055561` で未知/未処理DLQが0件、actionableな対象は `race-detail / DeadLetterQueue` 1グループ4通知だけと確認した。残り59通知はstoreの世代・終端guardにより状態変更不要として安全にackされた。
- 2026-09-16: Recovery run `35089206558` は4通知を既存active task 4件へ関連付けてpipelineを再開し、main queue in-flight 1件まで進んだ。その先頭Race `20260419:Nakayama:9` は移行前Taskのためcourse/number metadataを持たず、canonical Resource IDは完全なのにhandlerが `InvalidOperationException` で停止した。Resource ID fallbackは日付一致・3要素・有効course/numberを必須とし、不整合IDは引き続き拒否する。

## Deviations and follow-up

なし。承認前のためプロダクションコードと本番状態は変更していない。
