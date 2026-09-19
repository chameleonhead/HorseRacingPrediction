# GitHub Actionsによる監視PR作成権限を復旧する

- Status: Approved
- Owner: Main + repository administrator
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Connected | publicationとPR create/reuseを独立stepへ分離した。 |
| Verification | In progress | YAML/diff/secret gate後、open PRなしのproduction inspectで作成を確認する。 |
| Deployment/operation | Connected | repository設定をActions PR作成許可へ変更済み。 |

## Context

2026-09-19 14:40 JSTの予定実行 [collection-monitoring run 35424599533](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/35424599533) は、`Validate and publish change records` stepでfinding branchへのcommit/pushまで成功した後、`gh pr create` が `GitHub Actions is not permitted to create or approve pull requests (createPullRequest)` を返して失敗した。

workflowは `.github/workflows/collection-monitoring.yml` で `contents: write` と `pull-requests: write` を宣言しているため、原因仮説はworkflow内のjob権限不足ではなく、repositoryまたはorganizationのActions設定が `GITHUB_TOKEN` によるPull Request作成を禁止していることにある。

現在はPR [#35](https://github.com/chameleonhead/HorseRacingPrediction/pull/35) がOPENであるため、後続の手動`inspect` runはPR作成処理をskipして成功している。しかしPR #35がmergeまたはcloseされた後、次のfinding publicationで同じ失敗が再発し得る。後続runの成功は権限問題の恒久解消を証明しない。

## Incident ledger

- Incident: 2026-09-19 14:40 JST、予定監視run 35424599533がPR作成時に失敗。
- Temporary recovery: finding branchへの記録は保存され、人手でPR #35がOPENになった後の`inspect` runは成功している。
- Root cause: repositoryまたはorganizationのActions設定が、workflowの`GITHUB_TOKEN`によるPull Request作成を拒否した可能性が高い。workflow内には`pull-requests: write`が既にある。
- Corrective proposal: repository管理者が最小権限でActionsのPR作成を許可し、workflow側に設定preflightと再現可能な診断を追加する。組織policyで許可できない場合は、当該repositoryだけに限定したGitHub App tokenを代替とする。
- Permanent fix: Not started。
- Remaining risk: PR #35が閉じた後、change record publicationが再び失敗する可能性がある。

## Goals

- open monitoring PRが存在しない状態でも、sanitizedなProposed change recordを安定branchへpushし、レビュー用PRを作成できる。
- PR作成権限が欠ける場合、secretやログ全文を残さず、必要な管理者操作を明示して失敗できる。
- workflowに付与する権限を監視recordのcommit/pushとPR作成に必要な範囲へ限定する。

## Non-goals

- 監視finding、fingerprint、recovery recipeの判定仕様は変更しない。
- production data補正、pipeline再開、コード修正の自動承認、PRの自動mergeは行わない。
- 現在のPR #35を検証のためにcloseまたはmergeしない。
- organization全体のActions権限を本変更だけで一律に拡張しない。

## Decisions

- 第一候補は、repository単位でGitHub ActionsによるPull Request作成を許可する。organization policyがrepository overrideを禁止する場合は、管理者承認のうえ当該repositoryだけへinstallしたGitHub Appの短命tokenを`gh pr create`にのみ使う。
- repository設定を変更する場合も、workflowの明示的な`permissions`を維持し、不要な`actions: write`、`administration: write`、承認操作、auto-mergeは追加しない。
- 権限preflightはfinding API読取やrecord生成より前に実行し、権限不足を最小事実と修復手順を持つ明示的なstep failureとして返す。
- 動作検証は一時branchと無害なdocumentation-only PRを使う。既存のPR #35やproduction findingを検証目的で変更しない。
- 一時PRとbranchは検証後にclose/deleteし、repository設定を戻すrollback手順を残す。

## Open decision

- repository単位のActions設定を変更できるかを管理者が確認する。変更できない場合だけGitHub App方式へ切り替え、長期secretを避ける。fine-grained personal access tokenは個人依存と失効運用を増やすため採用しない。

## Documentation updates

- 提案作成時に `docs/11-automation-design.md`、`docs/26-collection-platform-design.md`、`docs/changes/20260919_collection-monitoring-change-record-automation/README.md` を確認した。
- 承認・実施時は `docs/11-automation-design.md` にPR作成権限の前提、preflight、GitHub App fallback、rollbackを追記し、運用設計の正本とする。
- collection domainの契約は変わらないため `docs/26-collection-platform-design.md` の更新は不要。

## Technical impact

- Repository/organization settings: Actions workflow permissionsのPR作成許可。
- Workflow: `.github/workflows/collection-monitoring.yml` の権限preflightと診断。GitHub App fallbackを選ぶ場合だけtoken生成stepと必要なrepository secretsを追加する。
- Documentation: `docs/11-automation-design.md` に運用前提と復旧/rollback手順を追加する。
- Production API、database、collection task、finding contentには影響しない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | open monitoring PRがない条件を既存PR #35へ影響させず再現し、workflow tokenが無害な一時PRを作成できる。 | T1,T3 | 一時branch/PRのURLとworkflow run URL | Connected |
| AC2 | PR作成権限がない場合、workflowはPR作成前の専用stepで停止し、管理者が変更すべき設定とrun URLを示し、secretを出力しない。 | T2,T3 | 権限不足fixtureまたはmockを使うworkflow/tool testとログ検査 | Connected |
| AC3 | workflowのtoken権限はcontents writeとpull requests writeの必要範囲を超えず、PR承認とauto-mergeを実行しない。 | T1,T2,T4 | workflow review、GitHub設定/API確認、secret-pattern scan | Verified |
| AC4 | 設定変更後の`mode=inspect`がfinding読取、record validation、`git diff --check`、branch push、PR reuse/create、recovery previewまで成功し、recovery applyを実行しない。 | T3,T4 | 完了したinspect runとstep一覧 | Connected |
| AC5 | 設定を戻す手順、一時PR/branchの除去、GitHub App採用時のinstall/token失効手順が文書化される。 | T4 | `docs/11-automation-design.md` review | Verified |

## Delivery plan

1. repository管理者がrepository override可否と現在のActions workflow permissionを確認し、第一候補またはGitHub App fallbackを承認する。
2. workflowへ設定preflightと最小診断を追加し、選択した認証方式だけを接続する。
3. 一時branchとdocumentation-only PRで作成経路を確認し、一時資産を除去する。
4. `mode=inspect`を実行し、record publicationとpreview-only境界を検証する。
5. canonical automation document、検証証拠、rollbackを更新する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | repository単位のActions設定可否を確認し、許可設定またはrepository限定GitHub App方式を確定する。AC1,AC3 | Repository administrator + Main | Lead tier | Approval | GitHub repository settings | 設定画面または権限API、権限レビュー | repository方式・default read・PR作成許可 | Verified |
| T2 | PR権限preflightとsecret非表示の診断をworkflowへ追加する。AC2,AC3 | Main | Lead tier | T1 | `.github/workflows/collection-monitoring.yml`、必要なworkflow tests | YAML parse、権限不足fixture/mock、ログ検査 | 独立create/reuse stepと最小診断 | Verified |
| T3 | 一時branch/PRとproduction secret境界の`inspect`でcreate/reuse経路を検証する。AC1,AC2,AC4 | Main + repository administrator | Lead tier | T1,T2 | 一時GitHub branch/PR、read-only production probe | run URL、PR URL、recovery step skip | 作成と再利用の両経路 | In progress |
| T4 | canonical運用文書、rollback、検証結果を更新し最終監査する。AC3-AC5 | Main | Lead/review tier | T2,T3 | `docs/11-automation-design.md`、本record | validator、`git diff --check`、secret scan | 文書・validator・diff gate | Verified |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** run 35424599533の失敗step、current workflow permissions、後続inspect成功、PR #35のOPEN/CLEAN状態を照合した。設定変更はrepository管理者、workflow診断と検証はMainに分離し、既存PRやproduction dataを変更しない境界を設定した。AC1-AC5はT1-T4と検証へ双方向に追跡される。repository override可否が未確定のため全taskを`Proposed`とし、承認前に実装しない。
- **Pre-implementation review — 2026-09-19, reviewer: Main.** 利用者の「今上がっているPRを順に対応」をAC1-AC5の承認と記録した。repository設定方式を採用し、GitHub App/PATは使用しない。T1,T2をRunnable、T3,T4をDependentとした。
- **Checkpoint review — 2026-09-19, reviewer: Main.** repository設定はdefault token readを維持したままActions PR作成許可だけを有効化した。workflowはvalidation/pushとPR create/reuseを分離し、失敗時に設定境界を示す。production create検証はmerge後に実施する。
- **Checkpoint review:** 設定、workflow、PR作成検証の各checkpointで実施する。
- **Final review:** AC1-AC5、T1-T4、最小権限、secret非表示、一時資産除去、rollbackを照合する。

## Verification record

- 2026-09-19: run 35424599533はfinding branchへのcommit `130658f` とpushに成功後、`createPullRequest`権限拒否で失敗した。失敗stepは`Validate and publish change records`。
- 2026-09-19: current workflowは`contents: write`と`pull-requests: write`を明示している。
- 2026-09-19: 後続inspect run 35431432061は成功したが、既存PR #35を再利用したためPR作成権限を再検証していない。
- 2026-09-19: PR #35はOPEN、非draft、merge state CLEAN、base `main`、head `automation/collection-monitoring-findings`。
- 2026-09-19: repository内のchange recordを検索し、同じ`createPullRequest`権限拒否を扱う既存proposalがないことを確認した。

## Rollback

- repository設定方式: workflowのPR作成を停止したうえで、ActionsによるPR作成許可を元へ戻す。作成済み一時PRをcloseし一時branchをdeleteする。
- GitHub App方式: workflowのApp token参照を外し、repositoryへのApp installationを削除し、App秘密鍵を失効・repository secretを削除する。一時PR/branchも除去する。
- rollback後もfinding branchの既存commitとPR #35は削除せず、監査証拠として保持する。

## Required human action

- repositoryまたはorganization管理者が、repository単位でGitHub ActionsによるPull Request作成を許可できるか判断する。
- 本proposalの設計とAC1-AC5を明示承認する。承認までは設定変更、workflow変更、一時PR作成を行わない。

## Deviations and follow-up

- 今回の監視runでは提案recordだけを作成し、権限設定、workflow、production、PR #35は変更しない。
- `actions/checkout@v4`のNode.js 20 deprecation警告は今回のPR作成失敗と独立した非blocking警告であり、本recordのscope外とする。
- 設定値を事前GETするpreflightはworkflow tokenにrepository administration readを追加し得るため採用せず、最小権限の実create/reuse stepをcapability checkとする。
