# 収集運用を監視し change record を自動起票する

- Status: Proposed
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 利用者承認後に監視評価、読み取りAPI、change record生成コマンド、定期自動化を実装する。 |
| Verification | Not started | 検知・非検知・重複抑止・API障害・処理順違反のテストを実施する。 |
| Deployment/operation | Not started | 本番監視用API URL/認証情報を秘密情報ストアから参照できる実行環境で定期タスクを有効化する。 |

## Context

収集基盤は `CollectionTask`、`CollectionAttempt`、failure notification、pipeline pause、lane/priority と公平配分を永続化している。管理APIは進捗、要対応障害、タスク一覧、パイプライン状態を読み取れるが、運用者が管理画面を開かない限り異常の調査タスクは始まらない。

監視自動化は、実行のたびにリポジトリ全体を自由解釈させるのではなく、本番コード側で型付きの運用 finding を決定的に作り、定期実行側は finding を change record へ変換する責務に限定する。

## Goals

- 要対応の収集障害、長時間進まないタスク、予期しないパイプライン停止を定期的に検出する。
- lane、priority、available time、公平配分の契約に対する処理順の異常を、保存済みの事実から検出する。
- 新規の異常ごとに `docs/changes/yyyyMMdd_collection-attention-<fingerprint>/README.md` を `Proposed` で作成する。
- 同じ原因の異常を重複起票せず、観測時刻、件数、代表リソースと根拠を既存 record に追記する。
- 監視は読み取り専用とし、収集タスクの再実行、キャンセル、pipeline pause/resume、データ修復は行わない。

## Non-goals

- 検出した障害の自動修復、自動リトライ、優先度書き換え。
- change record の自動承認や、自動生成した変更の本番展開。
- ログだけに基づく推測的な起票。
- 収集以外の一般的なCI、インフラ、ドメインデータ品質の監視。

## Operational experience

1. 定期タスクが30分ごとに読み取り専用監視APIを呼び出す。
2. finding がなければ、異常なしと検査時刻だけを実行結果に残す。
3. finding があれば、安定 fingerprint で既存の未完了 change record を検索する。
4. 既存 record があれば観測履歴を追記し、なければ新しい `Proposed` record を作成する。
5. 定期タスクの結果に、新規起票、既存起票への追記、起票対象外を区別して示す。
6. 運用者が根拠と提案された受け入れ基準を確認し、通常の change record と同じく承認してから対応する。

## Detection contract

finding は少なくとも `Fingerprint`、`Kind`、`Severity`、`FirstObservedAt`、`LastObservedAt`、`Summary`、`Evidence`、`SuggestedScope` を持つ。fingerprint に可変の件数、時刻、タスクIDは含めず、原因分類と対象境界から作る。

| Kind | Initial rule | Notes |
| --- | --- | --- |
| `ActionableFailureGroup` | Open の failure group が1件以上ある | definition、status、error code、正規化したerror分類を fingerprint にする。 |
| `UnexpectedPipelinePause` | pipeline pause が30分以上継続する | 予定作業が明示された理由は allowlist で除外可能にする。 |
| `StalledActiveTask` | Running の lease または due の Ready/Pending が設定された最大時間を超える | 任意の現在時刻を注入してテスト可能にする。初期閾値は Running 30分、due Ready/Pending 60分。 |
| `RetryWaitingBacklog` | RetryWaiting がタスク種別ごとに24時間以上解消しない | 件数の一時的増加ではなく、経過時間を必須条件にする。 |
| `DispatchOrderViolation` | 同じlane内でdueの高priorityを越えて低priorityを処理した、またはlane allocatorの公平配分上限を超えた | available time、aging、compatibility group、現在のallocator stateを含めて正当な追い越しを除外する。 |
| `MonitorUnavailable` | API呼び出しまたは解析が2回連続失敗する | 単発失敗は実行結果に記録するだけで起票しない。 |

Threshold は設定値とし、finding と change record の根拠に実際に使った値を残す。`DispatchOrderViolation` は単な一覧並びでは判定せず、本番dispatcherと同じ選択契約に対する保存済み配送事実で判定する。

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
- Persistence: 原則として既存task/attempt/outbox/failure notificationから評価する。連続検知に追加状態が必要な場合は、監視チェックポイントだけを保存し、タスク状態は変更しない。
- Tooling: finding JSONを受け取り、change recordを新規作成または追記するリポジトリ内コマンドを追加する。dry-runを既定とする。
- Automation: プロジェクトを対象とする30分間隔の定期タスクから、まずdry-run、続いて適用を実行する。作成・追記したrecordのみを1つの独立したコミットにする。

## Decisions

- 起票先はGitHub Issueや外部タスク管理ではなく、本リポジトリの change record とする。
- 監視評価はLLMの自由判定ではなくテスト可能なドメインコードで行い、LLM/定期タスクは実行と要約に限定する。
- 初期運用は30分間隔とし、単発の監視通信失敗では起票しない。
- 起票は安全な可逆操作だが、修復は従来どおり個別の承認済み change record で実行する。
- 処理順検証は管理画面の安定sortではなく、dispatcherのlane allocator、priority、aging、available time、compatibility groupと実際の配送履歴を使う。

## Documentation updates

- `docs/11-automation-design.md`: 収集運用監視、change record自動起票、自動修復を行わない境界を追記し、本recordを詳細契約の正本としてリンクする。
- `docs/26-collection-platform-design.md` と `docs/22-collector-design.md` を確認した。前者には別変更の未コミット編集があるため本設計段階では更新せず、本監視の詳細は当recordと `docs/11-automation-design.md` に保持する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 設定された時刻に収集運用監視が実行され、正常時は change record を作らず検査結果だけを報告する。 | T1-T4 | evaluator/API/command tests、scheduled run evidence | Not started |
| AC2 | Open failure、予期しない長時間pause、停滞task/retry、処理順違反を型付きfindingとして閾値・実測値・根拠付きで返す。 | T1,T2 | deterministic evaluator and endpoint tests | Not started |
| AC3 | findingごとに根拠、初期調査、受け入れ基準を持つ `Proposed` change record が作られ、秘密情報と未編集ログは含まれない。 | T3,T4 | golden-file and secret-scan tests、scheduled run evidence | Not started |
| AC4 | 同じfingerprintの未完了recordがある場合は新規起票せず観測履歴を追記し、同じ入力の再実行で履歴を重複追記しない。 | T3 | idempotency tests | Not started |
| AC5 | 監視が収集タスク、failure notification、pipeline、優先度、ドメインデータを変更しない。 | T1-T4 | API method review、before/after persistence assertions | Not started |
| AC6 | APIまたは認証の単発失敗は起票せず、2回連続失敗時だけ監視不能recordを1件起票する。復旧後は正常化を既存recordに追記する。 | T3,T4 | failure-sequence tests | Not started |
| AC7 | dirty worktree、同名競合、既存recordの未コミット変更を上書きせず、対象パスと必要な人手を報告して安全に停止する。 | T3,T4 | dirty/conflict tests | Not started |
| AC8 | 自動生成したrecordは修復を自動実行せず、利用者の明示承認前にプロダクションコードを変更しない。 | T3,T4 | prompt/command boundary test and review | Not started |
| AC9 | 関連テスト、Release build、CI同等format、`git diff --check`、change-record validator、CodeGraph同期・再照会が成功する。 | T5 | recorded commands and results | Not started |

## Delivery plan

1. 監視 finding contractと閾値設定、保存データからの評価を実装する。
2. 既存API key境界内に読み取り専用endpointを接続する。
3. dry-run/apply、fingerprint重複排除、不正入力拒否、dirty worktree保護を持つchange record生成コマンドを実装する。
4. 本番URLとAPI keyを実行時に秘密情報から受け取るプロジェク定期タスクを登録する。
5. テスト、本番read-only smoke、初回dry-run、初回applyを検証し、実行結果を本recordに記録する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | finding型、閾値、収集運用評価を実装する。AC2,AC5 | Main | Lead tier | Approval | CollectionOperationsとfocused tests | evaluator tests | 各findingの決定的証拠 | Proposed |
| T2 | 読み取り専用監視APIを追加する。AC1,AC2,AC5 | Main | Lead tier | T1 | API endpointとAPI tests | endpoint/auth/read-only tests | 実API契約の証拠 | Proposed |
| T3 | change record生成コマンドと重複・競合保護を実装する。AC3,AC4,AC6-AC8 | Main | Lead tier | T1 | tooling、template、tool tests | golden/idempotency/conflict tests | 生成差分とテス結果 | Proposed |
| T4 | 30分間隔の定期タスクを登録し、dry-runからapplyへ移行する。AC1,AC3,AC6-AC8 | Main + operator | Lead tier | T2,T3 | automation configuration、change recordのみ | dry-run/apply evidence | task ID、実行結果、作成record | Proposed |
| T5 | 回帰、CI同等検証、CodeGraph、文書、最終監査を完了する。AC9 | Main | Lead/review tier | T1-T4 | tests、docs、本record | full verification matrix | AC/task全件の完了証拠 | Proposed |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** 既存のprogress、task search、failure notification、pipeline API、dispatcherのlane/priority contract、GitHub運用workflowを確認した。管理一覧のsortはruntimeのaging/公平配分を意図的に再現しないため、一覧だけで処理順違反を判定しない設計とした。AC1-AC9はT1-T5と検証に双方向で追跡される。評価contract、自動生成文書、定期実行は依存関係が強く共有状態を扱うため、現時点ではMainが直列で担当する。独立したテス追加に分割できる状態になった場合だけworker委譲を再評価する。
- **Pre-implementation review:** 利用者承認後、コード変更前に実施する。
- **Checkpoint review:** 評価/API、生成コマンド、定期実行の各チェックポイントで実施する。
- **Final review:** AC1-AC9、T1-T5、秘密情報非混入、実行中の別変更非混入を照合する。

## Verification record

- 2026-09-19: CodeGraph で `CollectionTaskStatus`、`CollectionPlatformStore.GetProgressAsync`、`SearchTasksAsync`、`CollectionPlatformOutboxDispatcher` の現行経路を確認した。
- 2026-09-19: 既存APIにprogress、task search、failure notification groups、pipeline、dashboardがあることを確認した。
- 2026-09-19: 既存GitHub Actionsがproduction API URLとAPI keyをrepository secretから受け取る運用境界を持つことを確認した。秘密値は取得・表示していない。
- 2026-09-19: 作業ツリーに別目的の変更があることを確認し、本変更ではそれらを編集していない。

## Deviations and follow-up

- 本番監視を有効にするには、実行環境が既存のproduction API URLとAPI keyを参照できる必要がある。値自体をchange record、コマンド出力、コミットに残さない。
- この設計の実装開始には利用者の明示承認が必要である。
