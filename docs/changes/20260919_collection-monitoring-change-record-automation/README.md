# 収集運用を監視し起票と安全な過去ジョブ補正を自動化する

- Status: Proposed
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 利用者承認後に監視評価、読み取りAPI、change record生成コマンド、過去ジョブ補正recipe、定期自動化を実装する。 |
| Verification | Not started | 検知・非検知・分類・重複抑止・API障害・処理順違反・補正の冪等性/中断/上限/監査のテストを実施する。 |
| Deployment/operation | Not started | 本番監視用API URL/認証情報を秘密情報ストアから参照できる実行環境で定期タスクを有効化する。 |

## Context

収集基盤は `CollectionTask`、`CollectionAttempt`、failure notification、pipeline pause、lane/priority と公平配分を永続化している。管理APIは進捗、要対応障害、タスク一覧、パイプライン状態を読み取れるが、運用者が管理画面を開かない限り異常の調査タスクは始まらない。

監視自動化は、実行のたびにリポジトリ全体を自由解釈させるのではなく、本番コード側で型付きの運用 finding を決定的に作り、定期実行側は finding を change record へ変換する責務に限定する。

## Goals

- 要対応の収集障害、長時間進まないタスク、予期しないパイプライン停止を定期的に検出する。
- lane、priority、available time、公平配分の契約に対する処理順の異常を、保存済みの事実から検出する。
- 新規の異常ごとに `docs/changes/yyyyMMdd_collection-attention-<fingerprint>/README.md` を `Proposed` で作成する。
- 同じ原因の異常を重複起票せず、観測時刻、件数、代表リソースと根拠を既存 record に追記する。
- プログラムバグは修正用change record、未知の過去ジョブエラーは調査・対応方法検討用change recordとして起票する。
- 既知の過去ジョブエラーは、登録済みの自動安全な補正レシピが対象と事前条件を一意に検証できる場合だけ、previewと上限付きapplyで自動補正する。

## Non-goals

- 未登録、曖昧、または事前条件不一致の障害に対する推測的なデータ補正、自動リトライ、優先度書き換え。
- 検知したプログラムバグの未承認自動修正・自動デプロイ。
- change record の自動承認や、自動生成した変更の本番展開。
- ログだけに基づく推測的な起票。
- 収集以外の一般的なCI、インフラ、ドメインデータ品質の監視。

## Operational experience

1. 定期タスクが30分ごとに読み取り専用監視APIを呼び出す。
2. finding がなければ、異常なしと検査時刻だけを実行結果に残す。
3. finding があれば、安定 fingerprint で既存の未完了 change record を検索する。
4. `ProgramBug` なら再現根拠と修正タスクを持つ `Proposed` record を作成する。未承認のコード修正はしない。
5. `KnownHistoricalJobError` なら登録済みレシピをpreviewし、全事前条件を満たす対象だけ補正する。除外・失敗対象は個別に隔離する。
6. `UnknownHistoricalJobError` なら代表証拠、再現の有無、影響範囲、対応候補、安全な次の調査を記載した `Proposed` record を作成する。データは変更しない。
7. 同じfingerprintの既存 record があれば観測履歴と自動補正履歴を追記し、重複起票しない。
8. 定期タスクの結果に、新規起票、既存起票への追記、自動補正、安全上の隔離を区別して示す。
9. 運用者はバグ修正または未知エラー対応の根拠と受け入れ基準を確認し、通常の change record と同じく承認してから対応する。

## Detection contract

finding は少なくとも `Fingerprint`、`Kind`、`Classification`、`Severity`、`FirstObservedAt`、`LastObservedAt`、`Summary`、`Evidence`、`SuggestedScope`、`RecoveryRecipeId` を持つ。`Classification` は `ProgramBug`、`KnownHistoricalJobError`、`UnknownHistoricalJobError`、`OperationalCondition` のいずれかとする。fingerprint に可変の件数、時刻、タスクIDは含めず、原因分類と対象境界から作る。

| Kind | Initial rule | Notes |
| --- | --- | --- |
| `ActionableFailureGroup` | Open の failure group が1件以上ある | definition、status、error code、正規化したerror分類を fingerprint にする。 |
| `UnexpectedPipelinePause` | pipeline pause が30分以上継続する | 予定作業が明示された理由は allowlist で除外可能にする。 |
| `StalledActiveTask` | Running の lease または due の Ready/Pending が設定された最大時間を超える | 任意の現在時刻を注入してテスト可能にする。初期閾値は Running 30分、due Ready/Pending 60分。 |
| `RetryWaitingBacklog` | RetryWaiting がタスク種別ごとに24時間以上解消しない | 件数の一時的増加ではなく、経過時間を必須条件にする。 |
| `DispatchOrderViolation` | 同じlane内でdueの高priorityを越えて低priorityを処理した、またはlane allocatorの公平配分上限を超えた | available time、aging、compatibility group、現在のallocator stateを含めて正当な追い越しを除外する。 |
| `MonitorUnavailable` | API呼び出しまたは解析が2回連続失敗する | 単発失敗は実行結果に記録するだけで起票しない。 |

Threshold は設定値とし、finding と change record の根拠に実際に使った値を残す。`DispatchOrderViolation` は単な一覧並びでは判定せず、本番dispatcherと同じ選択契約に対する保存済み配送事実で判定する。

### Automatic correction recipe contract

自動補正はエラーコードだけでは実行せず、コードレビューと専用テストを経て登録されたrecipeに限定する。各recipeは以下を必須とする。

- 安定したrecipe ID/version、対象error/failure kind、collection revision範囲、必要な同一性根拠。
- read-only preview、対象ごとの `SafeToApply` と理由、apply後のpostcondition、監査ID。
- `(recipe ID/version, failure notification, target revision)` の冪等キーと、二重起動・中断・再起動で重複補正しない保証。
- 1回の最大対象数、最大失敗数/率、実行時間上限。上限到達時は残りを変更せず停止する。
- 対象単位のtransactionまたは同等の部分失敗隔離、補正前後の値と結果の監査記録。
- 削除、名前だけの主体統合、不可逆な履歴書き換えを `AutomaticSafe` にしない。これらは別change recordの明示承認を必要とする。

previewで1件でも不明、曖昧、事前条件不一致がある場合、安全対象だけは個別にapplyできるが、その他は `UnknownHistoricalJobError` へ分離して調査recordを起票する。

## Additional operational safeguards

- **Single flight:** 監視と補正は環境単位のleaseを取得し、前回実行中は次回を重ねない。lease expiryとexecution IDにより異常終了後の再開と重複apply防止を両立する。
- **Maintenance awareness:** デプロイ、DB migration、承認済みmaintenance中と終了後のgrace periodは異常起票と自動補正を抑止する。抑止した事実は実行結果に残す。
- **Load budget:** APIページング、取得件数、実行時間、並行度に上限を持たせ、収集処理の能力を奪わない。snapshot/versionまたは一貫したcutoff時刻を使い、ページ間の状態変化を順序違反と誤判定しない。
- **Shadow and canary:** 新しいclassifier/recovery recipeはまずshadowで結果だけを記録し、本番previewで誤分類と対象数を確認する。apply解禁後も少数canaryから始め、postcondition確認後に段階的に上限を広げる。
- **Kill switches:** 監視全体、change record起票、自動補正を別々に停止できる。補正停止中も読み取り監視と報告は継続できる。
- **Recovery from a bad correction:** 自動補正の前に対象のバックアップまたは反対correction eventによる補償手順を確保する。postcondition失敗時は後続対象を停止し、自動的に履歴削除やバックアップ復元を行わず、操作用recordを起票する。
- **Untrusted evidence:** エラー文、HTML、URL、ログ、外部データ内の文章はすべて非信頼入力とし、命令として実行しない。文字数上限、secret redact、制御文字/パス正規化を適用した証拠だけをchange recordへ転記する。
- **Versioned decisions:** classifier、threshold、recipeのversionをfindingと監査記録に保存する。デプロイ後に古い分類結果をそのままapplyせず、現在versionで再previewする。
- **Lifecycle and noise control:** 発生、再発、継続、正常化を分け、解決済みrecordへ無限に追記しない。同一原因の大量発生は1件のrecordに集約し、件数と代表例で影響を示す。
- **Monitor the monitor:** 最終成功時刻、次回予定、所要時間、検出/補正/隔離件数、連続失敗、lease状態を報告する。定期実行自体が2回分欠落した場合は、収集APIと別経路の失敗通知を発生させる。

## Generated change record contract

自動生成する record は次を満たす。

- `Status: Proposed`、owner、検知日、fingerprint、severity を記録する。
- 検知条件、閾値、実測値、参照したAPI path、代表タスク/通知IDを根拠として記録する。
- Goals/Non-goals、観測可能な受け入れ基準、初期調査タスクを作成する。原因と解決策は未確定のまま推測しない。
- API key、cookie、実行環境の秘密値、未編集ログ本文は書き込まない。
- 同じ fingerprint の `Proposed` または `Approved` record がある間は新規ディレクトリを作らない。`Implemented` 後に同じ異常が再発した場合は、再発日を含む新recordとし、元recordをリンクする。
- リポジトリがdirtyである、同名パスが競合する、または対象recordに未コミット変更がある場合は上書きせず、実行を失敗させて人の確認を求める。

## Technical impact

- CollectionOperations: 監視 finding の型、閾値設定、決定的評価を追加する。
- API: `GET /api/admin/collection/monitoring/findings` を追加する。新endpointは読み取り専用で、既存のAPI key境界の内側とする。
- Recovery: 既知過去エラーのrecipe registry、preview/apply、冪等実行記録、postcondition確認、circuit breakerを追加する。
- Persistence: 原則として既存task/attempt/outbox/failure notificationから評価する。連続検知チェックポイントと自動補正の冪等・監査記録は保存するが、失敗task/attemptの履歴は書き換えない。
- Tooling: finding JSONを受け取り、分類に応じてchange record生成またはrecovery preview/applyを行うリポジトリ内コマンドを追加する。dry-runを既定とする。
- Automation: プロジェクトを対象とする30分間隔の定期タスクから、まずdry-run、続いて起票と登録済み安全recipeのapplyを実行する。作成・追記したrecordのみを1つの独立したコミットにする。

## Decisions

- 起票先はGitHub Issueや外部タスク管理ではなく、本リポジトリの change record とする。
- 監視評価はLLMの自由判定ではなくテスト可能なドメインコードで行い、LLM/定期タスクは実行と要約に限定する。
- 初期運用は30分間隔とし、単発の監視通信失敗では起票しない。
- プログラムバグと未知エラーはchange recordの明示承認前に修正しない。既知過去エラーは、承認済みrecipeの `AutomaticSafe` 範囲だけ自動補正する。
- 初期の自動補正recipeは、すでに本番実績と専用テストがある `SubjectIdentificationAutoRecovery` のrevision-gated補正だけとする。新しいrecipeの追加は、対象・同一性根拠・事前/事後条件・上限・本番previewを別の承認済みchange recordで確定してからregistryへ追加する。
- 未知エラーのchange recordには単な起票だけでなく、原因仮説、追加調査、対応案、各案のリスク、推奨する次の操作を記載する。
- 処理順検証は管理画面の安定sortではなく、dispatcherのlane allocator、priority、aging、available time、compatibility groupと実際の配送履歴を使う。

## Documentation updates

- `docs/11-automation-design.md`: 収集運用監視、バグ/未知エラーのchange record起票、登録済み安全recipeに限定した過去ジョブ自動補正の境界を追記し、本recordを詳細契約の正本としてリンクする。
- `docs/26-collection-platform-design.md` と `docs/22-collector-design.md` を確認した。前者には別変更の未コミット編集があるため本設計段階では更新せず、本監視の詳細は当recordと `docs/11-automation-design.md` に保持する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 設定された時刻に収集運用監視が実行され、正常時は change record を作らず検査結果だけを報告する。 | T1-T5 | evaluator/API/command tests、scheduled run evidence | Not started |
| AC2 | Open failure、予期しない長時間pause、停滞task/retry、処理順違反を閾値・実測値・根拠付きfindingとし、バグ、既知過去エラー、未知過去エラー、運用状態に分類する。 | T1,T2 | deterministic evaluator and endpoint tests | Not started |
| AC3 | プログラムバグごとに再現根拠、影響範囲、修正タスク、受け入れ基準を持つ `Proposed` change record が作られる。 | T3,T5 | golden-file and scheduled run evidence | Not started |
| AC4 | 同じfingerprintの未完了recordがある場合は新規起票せず観測履歴を追記し、同じ入力の再実行で履歴を重複追記しない。 | T3 | idempotency tests | Not started |
| AC5 | 既知過去ジョブエラーは登録済みrecipeのpreviewで全事前条件を満たした対象だけ自動補正され、postconditionと補正前後が監査できる。 | T3,T4,T5 | recipe integration and audit tests、production preview/apply evidence | Not started |
| AC6 | 同recipeの二重起動、中断、再起動、部分失敗で補正が重複せず、上限またはpostcondition失敗時は未処理対象を変更せず停止する。 | T4,T5 | concurrency/restart/circuit-breaker tests | Not started |
| AC7 | 未知過去ジョブエラーはデータを変更せず、代表証拠、影響範囲、原因仮説、対応案とリスク、推奨する次の調査を持つ `Proposed` recordとして起票される。 | T3,T5 | unknown-error fixtures and golden files | Not started |
| AC8 | 監視と起票は収集状態を変更せず、バグ修正と未知エラー対応は明示承認前にコードまたはデータを変更しない。 | T1-T5 | boundary and before/after persistence assertions | Not started |
| AC9 | APIまたは認証の単発失敗は起票せず、2回連続失敗時だけ監視不能recordを1件起票する。復旧後は正常化を既存recordに追記する。 | T3,T5 | failure-sequence tests | Not started |
| AC10 | dirty worktree、同名競合、既存recordの未コミット変更を上書きせず、対象パスと必要な人手を報告して安全に停止する。 | T3,T5 | dirty/conflict tests | Not started |
| AC11 | 秘密情報、未編集ログ、補正対象の不必要な個人情報がchange record、コマンド出力、コミットに含まれない。 | T3-T5 | golden-file and secret-scan tests | Not started |
| AC12 | 関連テスト、Release build、CI同等format、`git diff --check`、change-record validator、CodeGraph同期・再照会が成功する。 | T6 | recorded commands and results | Not started |
| AC13 | 監視/補正の重複実行が防止され、デプロイ・DB migration・maintenance中とgrace periodは誤起票と自動補正を抑止し、抑止理由が確認できる。 | T1,T4,T5 | lease/concurrency/maintenance-window tests | Not started |
| AC14 | API負荷と実行時間が上限内に収まり、一貫したcutoffで評価される。新classifier/recipeはshadowおよび少数canaryの成功前に全件applyされない。 | T1,T2,T4,T5 | load/snapshot/shadow/canary tests and production evidence | Not started |
| AC15 | 監視、起票、自動補正を独立に停止でき、補正前にバックアップまたは補償手順が確認される。postcondition失敗時は後続applyが停止し、復旧操作用recordが起票される。 | T4,T5 | kill-switch/backup/compensation/postcondition tests | Not started |
| AC16 | エラー文、HTML、URL、ログ内の命令文が実行されず、制限・正規化・redactされた証拠だけがrecordに入る。判定と補正は現在のclassifier/recipe versionで再検証される。 | T1,T3-T5 | adversarial-input/version-drift tests | Not started |
| AC17 | 自動化の最終成功時刻、次回予定、所要時間、件数、連続失敗、leaseを確認でき、2回分の定期実行欠落は収集APIと別経路で通知される。大量発生はfingerprint単位に集約される。 | T3,T5 | heartbeat/missed-run/noise-control tests and scheduled evidence | Not started |

## Delivery plan

1. 監視 finding contractと閾値設定、保存データからの評価を実装する。
2. 既存API key境界内に読み取り専用endpointを接続する。
3. バグ修正用と未知エラー調査用のchange record生成コマンドを、fingerprint重複排除、不正入力拒否、dirty worktree保護付きで実装する。
4. 既知過去エラー用のrecipe registry、preview/apply、冪等・監査・postcondition・circuit breakerを実装する。
5. 本番URLとAPI keyを実行時に秘密情報から受け取るプロジェクト定期タスクを登録する。
6. テスト、本番read-only smoke、起票dry-run、recipe preview、制限付き初回applyを検証し、実行結果を本recordに記録する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | finding型、閾値、4分類、version、一貫したcutoff、収集運用評価を実装する。AC2,AC8,AC13,AC14,AC16 | Main | Lead tier | Approval | CollectionOperationsとfocused tests | evaluator tests | 各findingの決定的証拠 | Proposed |
| T2 | 読み取り専用監視APIを取得件数/時間上限付きで追加する。AC1,AC2,AC8,AC14 | Main | Lead tier | T1 | API endpointとAPI tests | endpoint/auth/read-only/load tests | 実API契約の証拠 | Proposed |
| T3 | バグ/未知エラー用change record生成、証拠sanitize、lifecycle/noise control、重複・競合保護を実装する。AC3,AC4,AC7-AC11,AC16,AC17 | Main | Lead tier | T1 | tooling、template、tool tests | golden/idempotency/conflict/adversarial tests | 生成差分とテスト結果 | Proposed |
| T4 | 既知過去エラーのrecipe registry、single-flight、kill switch、preview/canary/apply、補償手順を実装し、初期recipeとして既存のrevision-gated主体自動復旧を登録する。AC5,AC6,AC8,AC11,AC13-AC16 | Main | Lead tier | T1 | CollectionOperations、API、persistence、tests | recipe/idempotency/concurrency/safety tests | 安全補正と監査の証拠 | Proposed |
| T5 | 30分間隔の定期タスク、maintenance抑止、死活監視を登録し、起票dry-runとrecipe shadow/previewから少数canary、制限付きapplyへ移行する。AC1,AC3,AC5-AC11,AC13-AC17 | Main + operator | Lead tier | T2-T4 | automation configuration、change record、登録済みrecovery APIのみ | dry-run/shadow/preview/canary/apply evidence | task ID、実行結果、作成record、補正監査 | Proposed |
| T6 | 回帰、CI同等検証、CodeGraph、文書、最終監査を完了する。AC12 | Main | Lead/review tier | T1-T5 | tests、docs、本record | full verification matrix | AC/task全件の完了証拠 | Proposed |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** 既存のprogress、task search、failure notification、pipeline API、dispatcherのlane/priority contract、GitHub運用workflowを確認した。管理一覧のsortはruntimeのaging/公平配分を意図的に再現しないため、一覧だけで処理順違反を判定しない設計とした。追加要件に対し、既存のrevision-gated主体自動復旧が一意性根拠、preview/apply、failure/revision冪等キー、部分失敗隔離を持つことを確認し、同じ安全契約を登録式recipeに一般化する。自動補正を本番常設する上で必要な、single-flight、maintenance抑止、負荷上限、shadow/canary、kill switch、補償手順、非信頼証拠対策、version再検証、死活監視を追加した。AC1-AC17はT1-T6と検証に双方向で追跡される。評価contract、補正のデータ整合性、自動生成文書、定期実行は依存関係が強く共有状態を扱うため、現時点ではMainが直列で担当する。
- **Pre-implementation review:** 利用者承認後、コード変更前に実施する。
- **Checkpoint review:** 評価/API、生成コマンド、定期実行の各チェックポイントで実施する。
- **Final review:** AC1-AC17、T1-T6、自動補正の実データ経路と監査証拠、shadow/canary、kill switch、補償手順、定期実行の死活、秘密情報非混入、実行中の別変更非混入を照合する。

## Verification record

- 2026-09-19: CodeGraph で `CollectionTaskStatus`、`CollectionPlatformStore.GetProgressAsync`、`SearchTasksAsync`、`CollectionPlatformOutboxDispatcher` の現行経路を確認した。
- 2026-09-19: 既存APIにprogress、task search、failure notification groups、pipeline、dashboardがあることを確認した。
- 2026-09-19: 既存GitHub Actionsがproduction API URLとAPI keyをrepository secretから受け取る運用境界を持つことを確認した。秘密値は取得・表示していない。
- 2026-09-19: 追加要件を受け、既存の `SubjectIdentificationAutoRecovery` とそのchange recordを確認した。現行実装は修正済みrevision、failure/target revision単位の冪等キー、対象単位の例外隔離、曖昧・名前欠落のskipを持つ。これを一般化する一方、エラーコードだけの無条件applyは認めない。
- 2026-09-19: 作業ツリーに別目的の変更があることを確認し、本変更ではそれらを編集していない。

## Deviations and follow-up

- 本番監視を有効にするには、実行環境が既存のproduction API URLとAPI keyを参照できる必要がある。値自体をchange record、コマンド出力、コミットに残さない。
- この設計の実装開始には利用者の明示承認が必要である。
