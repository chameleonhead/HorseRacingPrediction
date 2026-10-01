# Collection API 契約変更後のデプロイガードと障害一括再取得を修復する

- Status: Implemented
- Change record schema: 2
- Owner: Main/Lead
- Created: 2026-10-02
- Updated: 2026-10-02

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | deploy guard/restoreの3 wrapperと障害group一括再取得のmembership snapshot経路を修正 |
| Verification | Complete | shell 14ケース、collector contract 14件、API focused 30件、Release build、format、non-External 1,578件成功（1 skipped） |
| Deployment/operation | Not started | push・再デプロイ・pipeline再開は承認済みNon-goal。別途明示依頼後に実施する |

## Context

GitHub Actions run `36903965106` は `Pause and drain collection before changing deployed versions` で `jq: Invalid pipeline state` となった。`GET /api/v2/admin/collection/pipeline-state` は `bf8ad76f` 以降 `{ "pipeline": { "isPaused": ... } }` を返すが、workflow は旧形式の `.isPaused` を参照している。直前の run `36871731558` は旧APIに対する事前ガードを通過して新APIを配置した後、同じ不一致により復元段階で失敗した。

同じworkflowには、v2のタスク一覧を `.items` として読む箇所（正しくは `.page.items`）と、失敗通知を配列として読む箇所（正しくは `.notifications`）も残っている。現行shell testのmockが旧形式を返すため、この不一致を検出できなかった。

管理画面の障害グループ詳細では「対象をまとめて再取得」が `GroupKey` だけを送信する。一方、`POST /api/v2/admin/collection/recovery-batches` のGroupKey selectorは、確認後に対象が変わった状態で誤って一括操作しないため `ExpectedNotificationIds` を必須にしている。そのため現行ボタンは必ず400となる。詳細取得時点でstoreはグループ全件のIDを把握しているが、page DTOへ変換する直前に `NotificationIds=[]` へ消している。

## Goals

- デプロイ前pause/drainとデプロイ後状態復元が、現在のv2 response wrapperを正しく検証する。
- shell testを実APIと同じwrapper形状にし、wrapper名・内部配列名の退行を検出する。
- 障害詳細の「対象をまとめて再取得」が、表示時点のグループ構成を競合条件として送信し、正常に再取得を依頼できる。
- 対象構成が確認後に変化した場合は一括操作を行わず、更新して再試行できるエラーを表示する。

## Non-goals

- Collection API route、selector種別、pause/drainの安全方針を変更しない。
- `ExpectedNotificationIds` を任意化せず、staleなグループに対する誤操作防止を弱めない。
- 既存の失敗通知、収集task、pipeline状態をこの実装作業から変更しない。
- push、デプロイ、失敗taskの再実行、pipeline再開は別途明示された運用操作とする。

## Experience and interaction design

画面構成、Primary Action、Dialog、操作数は変更しない。「対象をまとめて再取得」→確認Dialog→「再取得を依頼」という既存導線を維持する。成功時は既存の作成・統合件数を表示する。確認後にグループ構成が変わった409は技術例外を露出せず、「対象が更新されたため、画面を更新してもう一度確認してください」と案内し、再確認を促す。

Loading、empty、選択対象の再取得、検索・ページング、RecoveryAllowed判定は変更しない。標準Fluent componentと既存レイアウトを維持するため、新規CSSやvisual mockは不要。

## Documentation updates

- このchange recordを、今回の事故原因、契約追従、受け入れ基準、検証結果の正準記録として追加する。
- `docs/26-collection-platform-design.md`、`docs/20-admin-ui-design.md`、`docs/28-api-client-design.md`を確認した。公開設計や操作方針は変更せず、既存契約どおりにconsumerを修復するため更新不要。
- 既存の `docs/changes/20260930_deploy-preflight-v2-routes/README.md` と `docs/changes/20260913_collection-failure-investigation-ui/README.md` は履歴記録として変更せず、本記録から退行原因を明示する。

## Technical impact

### Affected contract ledger

| Consumer | Method/path | Current response contract | Required consumer access | Disposition |
| --- | --- | --- | --- | --- |
| deploy preflight | GET `/api/v2/admin/collection/pipeline-state` | `GetCollectionPipelineResponse.Pipeline` | `.pipeline.isPaused` | 修正対象 |
| deploy preflight | GET `/api/v2/admin/collection/tasks?status=Running&limit=1` | `ListCollectionTasksResponse.Page` | `.page.items` | 修正対象 |
| post-deploy restore | GET `/api/v2/admin/collection/pipeline-state` | `GetCollectionPipelineResponse.Pipeline` | `.pipeline.isPaused` | 修正対象 |
| post-deploy restore | GET `/api/v2/admin/collection/tasks?status=Running&limit=1` | `ListCollectionTasksResponse.Page` | `.page.items` | 修正対象 |
| post-deploy restore | GET `/api/v2/admin/collection/failure-notifications?view=Actionable&limit=1` | `ListFailureNotificationsResponse.Notifications` | `.notifications` | 修正対象 |
| failure group detail | GET `/api/v2/admin/collection/failure-notification-groups/{groupKey}` | `GetFailureNotificationGroupResponse.Page` | `page.group.notificationIds`を全グループ構成として保持 | 修正対象 |
| failure group recovery | POST `/api/v2/admin/collection/recovery-batches` | `CreateCollectionRecoveryBatchResponse.Recovery` | `selectorType=GroupKey`と`expectedNotificationIds`を送信 | 修正対象 |

発見した7 consumer operationを全件in-scopeとし、route/methodは変更しない。追加のAPI route、legacy route、互換fallbackは導入しない。

## Hypothesis ledger

| ID | Claim | Fact/inference boundary | Supporting/contradicting evidence | Falsification and result | Disposition |
| --- | --- | --- | --- | --- | --- |
| H1 | CI失敗はdrain timeoutではなくpipeline wrapper不一致 | run logとsourceは事実 | run `36903965106`は開始1秒後に`Invalid pipeline state`。API契約は`Pipeline` wrapper、workflowは`.isPaused` | 現行production-shaped JSONを同じjqへ入力するとexit 5 | Verified |
| H2 | 最初のjqだけ直すと後続でも失敗する | source比較は事実 | tasksは`Page.Items`、notificationsは`Notifications` wrapperだがworkflowは`.items`/root array | DTOとendpointを全3 responseで照合 | Verified |
| H3 | 一括再取得の400はExpectedNotificationIds欠落 | source経路は事実。本番HTTP bodyの直接観測は未取得 | UIは`new()`、clientはnullを送信、endpointはGroupKey時にnon-nullを必須化 | component/HTTP serverで現行click payloadを再現し400条件と一致 | Verified |
| H4 | 詳細responseへ全IDを保持すれば追加fetchなしで安全に送信できる | store/queryとDTOは事実、採用は設計判断 | storeは全actionable notificationsからgroupを構築後、line 3560でIDだけ空にする。DTOはNotificationIdsを既に公開 | group detail testで全ID、page itemsは指定pageだけ、POSTで同じIDを確認 | Resolved in design |

## Decisions

1. workflow側を現在のtyped v2 response wrapperへ合わせ、API側に旧flattened responseを復活させない。
2. preflightとrestoreの全response readを有限ledgerどおり同時に修正する。
3. shell mockは実DTOと同じJSONにし、jqをstubで無条件成功させず、実jqでwrapper/propertyを検証するfixtureへ改める。
4. failure group detail responseの`Group.NotificationIds`へ検索・ページング前のactionable group全件IDを保持する。`Items`と`TotalCount`は従来どおり検索・ページング結果を表す。
5. 一括再取得は`pageData.Group.NotificationIds`を`ExpectedNotificationIds`として送る。選択対象の再取得は現行のNotificationIds selectorを維持する。
6. membership変更時の409は再取得を実行せず、更新・再確認を促す利用者向けメッセージとして表示する。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | responseの1項目だけ直すとtasks/notificationsで次のデプロイが再停止する | 修正後もdeploy不能 | 7 operation ledgerを一括修正し、各production-shaped fixtureを検証 | AC1/AC2, T1/T3 | 必須 | Approved | Resolved in design |
| C2 | GroupKeyだけを許可すると確認後に増えた対象まで意図せず再取得する | 予期しない運用変更 | ExpectedNotificationIds必須契約を維持し、detail取得時点の全IDを送る | AC3/AC4, T2/T3 | 必須 | Approved | Resolved in design |
| C3 | page itemsだけをexpected listにすると、複数ページgroupが必ず409または一部操作になる | 一括操作の意味を壊す | storeが既に保持する検索前・ページング前のgroup全IDをresponseへ載せる | AC3, T2 | 必須 | Approved | Resolved in design |
| C4 | group ID配列は最大10,000 GUIDでpayloadが増える | detail responseが大きくなる | recovery endpointの既存上限と同じ10,000までを許容。上限超過は既存fail-closedを維持し、将来token化は本ACを阻害しない別設計 | AC3/AC4, T2 | 推奨 | Approved | Accepted risk |
| C5 | 実装だけでは現在pause中の可能性がある本番を復旧しない | CI再実行前に運用判断が必要 | push/deploy/resumeは自動実施せず、コード完了後に状態と次の安全操作を報告する | AC5, T4 | 必須 | Approved | Excluded follow-up |

Open decisionはない。C4の再検討条件はgroup detail payload/応答時間が運用上問題になること、または10,000超のactionable groupが発生すること。C5のownerはMain/Leadと利用者で、明示依頼後にproduction evidenceを再取得する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | current v2 wrapperを返すproduction APIに対し、preflightが元のpause状態を取得し、pause後にrunning taskが0になるまで待機できる | T1, T3 | production-shaped shell testのrunning/paused/draining/invalid各case | Verified |
| AC2 | deploy成功後のrestoreがpipeline・tasks・actionable failuresのwrapperを正しく読み、安全条件を満たす場合だけ元の状態へ戻す | T1, T3 | shell testのresume/preserve/failure各case | Verified |
| AC3 | 複数ページを含む障害group詳細で「対象をまとめて再取得」を確定すると、表示時点の全notification IDをexpected listとして1回送信し、作成・統合件数を表示する | T2, T3 | endpoint test、bUnit component test、実HTTP handler payload assertion | Verified |
| AC4 | 確認後にgroup構成が変わった場合はtaskを作らず、更新して再確認する案内を表示する。選択対象再取得、検索、ページングは退行しない | T2, T3 | stale membership endpoint/component testと既存component regression | Verified |
| AC5 | build、format、関連test、CI相当non-External test、contract inventory、diff/statusが成功し、秘密情報や本番mutationを含まない | T3, T4 | workflow相当commandと最終レビュー | Verified |

## Delivery plan

設計承認後、T1とT2を直列に実装し、それぞれfocused testを通す。T3で統合された実HTTP consumer pathとCI parityを検証し、T4で記録と最終差分を照合する。workflowとAPI/UIのwrite scopeは異なるが、共通契約testと最終受け入れの統合負担が小規模実装を上回るためLeadが一貫して担当する。

## Task plan

| ID | Task | Owner | Model tier | Routing | Depends on | Write scope | Verification | Completion evidence | Audit | Result metrics | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | deploy guard/restoreのwrapper参照とproduction-shaped shell testを修正 | Main/Lead | Lead tier | public運用安全gateと実行順序の判断を保持する短い変更 | Approval | `.github/workflows/app-deploy.yml`; `tests/scripts/test-deploy-pipeline-state.ps1`;必要なworkflow contract test | shell test、collector contract test | shell 14ケース、contract 14件成功 | none | unavailable; retries 0; corrections 0; reviews 1 | Verified |
| T2 | failure group全IDのread contractと一括再取得consumer、競合時表示を修正 | Main/Lead | Lead tier | API concurrency contractとUI操作を同時に受け入れるため直列Lead所有 | Approval, T1 | group store/endpoint contract、AdminApiClient、`CollectionFailureGroupDetail.razor`、対応API/component tests | focused endpoint/component tests | focused 30件、全ID payload・409 message assertions成功 | none | unavailable; retries 1 formatting/compile correction; corrections 2; reviews 1 | Verified |
| T3 | finite contract ledger全件と統合回帰を検証 | Main/Lead | Lead tier | verifier/final integration責任 | T1, T2 | testsと本change recordのみ | Release build、format、関連/solution tests、CodeGraph sync/query | verification record | none | unavailable; retries 0; corrections 0; reviews 1 | Verified |
| T4 | DDD checkpoint/final review、commit準備 | Main/Lead | Lead tier | final acceptanceはLead責任 | T3 | 本change recordのみ | validator、diff-check、status、AC/task照合 | final review | none | unavailable; retries 0; corrections 0; reviews 1 | Verified |

実装中にroute/method変更、ExpectedNotificationIds契約の緩和、10,000超group対応、またはproduction mutationが必要と判明した場合は設計変更として停止し、Proposedへ戻す。局所的なwrapper/property/test fixture修正は承認範囲内で自律修正する。

## Review gates

- **Design and task-split review (2026-10-02, Main/Lead):** run log、v2 endpoint/DTO、workflow、shell test、failure detail UI/client/store/endpointを照合。7 consumer operationを有限ledger化し、AC1～AC5へ全件対応した。UI構成変更なし。小規模だが運用安全gateとAPI concurrency契約を跨ぐため、分離委譲のprompt/audit/review費用が実装を上回り、Lead直列所有とした。
- **Concern and agreement review (2026-10-02, Main/Lead):** caller assumption、API互換、stale操作、複数ページ、payload上限、production recovery、test blind spotを確認。C1～C5で処置を定義し、Open decisionなし。C4は上限監視条件付きのaccepted risk、C5は本実装ACを阻害しない明示的運用follow-up。ユーザー承認待ち。
- **User approval (2026-10-02):** ユーザーが設計、AC1～AC5、C1～C5の処置を「対応をお願いします」と明示承認。
- **Pre-implementation review (2026-10-02, Main/Lead):** T1をRunnable、T2～T4を直列依存のDependentと分類。T1はworkflowとproduction-shaped shell fixtureだけを変更し、route・pause/resume方針を変えない。T2は全group IDsのread contract、既存client/UI、対応testだけを変更し、ExpectedNotificationIds必須と選択対象経路を維持する。各focused test、Release build、format、non-External solution test、CodeGraph sync、diff/statusを完了証拠とする。route/method、競合契約、production mutationが必要なら設計へ戻す。
- **Checkpoint review (2026-10-02, Main/Lead):** AC1/AC2はworkflowの全3 response wrapperとshell fixtureを照合し、old flattened mockを排除。running/paused、取得失敗、不正state、pause失敗、不正task、drain未完了、actionable failureの14ケースと既存contract 14件が成功。AC3/AC4はstore全membership→GET wrapper→AdminApiClient→component click→POST expected IDsの実経路を確認し、検索・page size 2でも3件全IDを保持、成功receiptと409案内を検証。初回focused buildの必須引数不足とnullable warning、format whitespaceを局所修正して同gateを再実行した。設計逸脱なし。
- **Final review (2026-10-02, Main/Lead):** 7 operation ledgerを差分と実行証拠へ一対一照合。API route/method、ExpectedNotificationIds必須、pause/resume安全条件、選択対象再取得を維持。Release build 0 warning/0 error、format、non-External solution test、CodeGraph sync、diff-check成功。AC1～AC5とT1～T4は全件Verified、未完了findingなし。push/deploy/resumeは承認済みNon-goalとして残る。

## Verification record

- 調査時点のworktreeは`main...origin/main`、HEAD `06e84a9d66123b40be49ad0266a926133412b588`、変更なし。
- GitHub Actions run `36903965106`: preflight開始約1秒後、`.isPaused`の型検査で`Invalid pipeline state`、exit 5。
- GitHub Actions run `36871731558`: 旧APIへのpreflight後に`8a1571d4`を配置し、新APIへのrestoreでexit 1。`8a1571d4`はwrapper導入commit `bf8ad76f`を含む。
- 現行`test-deploy-pipeline-state.ps1`: 全case成功するが、mockは旧pipeline responseを返し、jq stubが実property accessを検証しないためproduction failureを見逃すことを確認。
- `CreateCollectionRecoveryBatchEndpoint`: GroupKey selectorはnon-null ExpectedNotificationIdsと現在値の完全一致を要求。現行UIは`new()`でnullを送る。
- `GetActionableFailureGroupPageAsync`: 全group notificationsを取得した後、response構築直前にNotificationIdsを空配列へ置換する。

実装後の検証:

- `./tests/scripts/test-deploy-pipeline-state.ps1 -Bash 'C:\Program Files\Git\bin\bash.exe'`: restore 7ケース、guard 7ケース、合計14ケースすべてPASS。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/... --filter FullyQualifiedName~CollectionQueueCutoverContractTests`: 14 passed。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/... --filter "FullyQualifiedName~CollectionAdministrationComponentTests|FullyQualifiedName~CollectionOperationsEndpointTests"`: 30 passed。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: exit 0。
- `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release`: 0 warnings、0 errors。
- `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External"`: 1,578 passed、1 skipped、0 failed。
- `codegraph sync .`: success。再queryでstore→endpointおよびAdminApiClient→componentの変更経路を確認。
- `git diff --check`: success。秘密情報、本番mutation、生成物なし。

## Deviations and follow-up

設計からの逸脱なし。ブラウザーでのvisual確認は、画面構成・配置・CSSを変更せず、既存FluentButton/Dialog導線のイベントとloading/success/error状態をbUnitで実HTTP client越しに検証したため不要と判断した。push・deploy・pipeline resumeはNon-goalどおり未実施。
