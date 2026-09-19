# 収集運用専用GitHub Actionsを廃止する

- Status: Implemented
- Owner: Main
- Created: 2026-09-20
- Updated: 2026-09-20

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | PR #52で2つの収集運用workflowを削除し、契約テストと文書を更新してmainへ反映した。 |
| Verification | Verified | focused test、format、change-record validator、literal search、PR CIが成功した。 |
| Deployment/operation | Verified | `origin/main`で両workflowの不存在とapp/infra workflowの存続を確認した。 |

## Context

監視経路は既にCodexのローカルスケジュールタスクへ移行し、`collection-monitoring.yml`を削除した。利用者は残る収集運用専用workflowである`collection-dlq-diagnostics.yml`と`collection-maintenance.yml`も削除し、関連文書のGitHub Actions利用記述を整理するよう明示した。

## Goals

- 収集の診断、DLQ Recovery、登録済み補正recipeの実行経路をCodexタスクから管理APIを呼ぶ経路へ一本化する。
- GitHub Actions画面から収集状態を変更できる運用入口をなくす。
- 現行文書と契約テストが削除後の実行経路を正しく説明する。

## Non-goals

- `app-ci`、`app-deploy`、`infra-deploy`など開発・配備用workflowの削除。
- 管理API、Recovery、補正recipe、安全条件の削除。
- 過去のchange recordにある実行証拠の抹消。
- 本変更中のDLQ Recoveryまたはデータ補正の実行。

## Decisions

- `.github/workflows/collection-dlq-diagnostics.yml`と`.github/workflows/collection-maintenance.yml`を削除する。
- GitHub Actionsにあった確認文字列、production environment、OIDC、concurrencyは移植しない。Codexが操作する場合も、管理API側のsafe-to-apply判定、preview、pipeline drain、冪等性、対象上限を満たす個別change recordと実行証拠を必須とする。
- 履歴change recordのGitHub Actions実行結果は監査証跡として残し、現行入口と読める箇所にsupersession noteを加える。

## Documentation updates

- `docs/11-automation-design.md`: 手動診断、DLQ Recovery、登録済み補正をCodexタスクから管理APIを呼ぶ経路へ一本化し、収集運用専用GitHub Actionsを使用しない現行方針へ更新する。
- `docs/changes/20260916_legacy-dlq-recovery/README.md`: 削除対象workflowの記述が当時の証跡であり、現在は廃止済みであることを追記する。
- その他の`docs/changes`内のGitHub Actions記述は当時のCI/CDまたは監視実行証拠であり、履歴保持のため変更しない。

## Concern and agreement review

| ID | Concern and evidence | Impact | Disposition | AC/task | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 2 workflowは管理APIのRecovery、pipeline resume、補正apply、SNS smoke testを手動実行できた。削除後はGitHub UIから実行できない。 | 緊急時の代替入口とGitHub側のconfirmation/concurrency guardを失う。 | 利用者の一本化指示に従って削除する。Codex経由でもpreview、安全判定、drain、冪等性、上限、監査記録を必須とし、API key未設定時は人の資格情報入力が必要と明示する。 | AC1-AC3/T1 | 二重入口を残さず、失う能力を明示した上で削除する。 | 2026-09-20の明示的削除指示で受容 | Accepted risk |
| C2 | repositoryには開発・配備用GitHub Actionsも存在する。 | 一括削除するとCI/CDを破壊する。 | ファイル名と収集運用用途でscopeを限定し、app/infra workflowは保持する。 | AC1/T1 | 収集運用専用だけを削除する。 | 依頼の対象ファイル指定により確定 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | mainに`collection-dlq-diagnostics.yml`と`collection-maintenance.yml`が存在せず、app/infraのCI/CD workflowは残る。 | T1 | file inventory、CI | Verified |
| AC2 | 現行の正本文書が収集の手動診断、Recovery、補正をCodexタスクから管理API経由で行うと説明し、削除済みworkflowの利用を案内しない。 | T1 | repository-wide literal search、documentation review | Verified |
| AC3 | 契約テストが削除済みworkflowの内容を読まず、両workflowの不存在を固定する。 | T1 | `CollectionQueueCutoverContractTests` | Verified |

## Task plan

- **T1 — Main / High capability / Verified:** 2 workflowを削除し、契約テスト、正本文書、履歴supersession noteを更新した。AC1-AC3をfocused test、format、validator、literal search、PR CI、main file inventoryで検証した。

## Review gates

- **Design and task-split review — 2026-09-20, reviewer: Main.** 単一の削除・整合作業としてT1へ集約した。AC1-AC3は不存在、正本文書、契約テストで相互に検証する。委譲や並列書込は行わない。
- **Concern and agreement review — 2026-09-20, reviewer: Main.** 運用入口喪失とCI/CD誤削除をmaterial concernとして記録した。利用者の明示指示はC1のrisk受容とC2のscope確定を含む承認として扱い、StatusをApprovedとする。
- **Pre-implementation review — 2026-09-20, reviewer: Main.** T1はRunnable。最新`origin/main`の隔離worktreeを使い、ユーザーのdirty mainには触れない。対象外workflowと管理APIを変更しない。
- **Checkpoint review — 2026-09-20, reviewer: Main.** 削除差分をAC1-AC3と照合し、app/infra workflowと管理APIが無変更であることを確認した。focused test 11/11、format、validator、literal searchが成功。T1はmain mergeとCI待ちでIn progressを維持する。
- **Final review — 2026-09-20, reviewer: Main.** PR #52のCI成功とmerge commit `d48f792`、`origin/main`のfile inventoryを照合した。AC1-AC3とT1はすべてVerifiedで、対象外のapp/infra workflowは保持されている。未完了またはacceptance-blockingな外部項目はないためImplementedと判定した。

## Legacy-surface inventory

| Identifier | Surface | Classification | Required final evidence |
| --- | --- | --- | --- |
| `collection-dlq-diagnostics.yml` | `.github/workflows` | active → removed | mainで不存在、契約テスト成功 |
| `collection-maintenance.yml` | `.github/workflows` | active → removed | mainで不存在、契約テスト成功 |
| workflow名を含む過去change record | `docs/changes` | intentional history compatibility | 現行手順ではないことを分類 |
| app/infra GitHub Actions | `.github/workflows` | active, outside scope | mainに保持、CI成功 |

## Verification record

- 2026-09-20: CodeGraphは隔離worktreeにindexがなく利用不可だったため、repository-wide literal searchへ切り替えた。
- 2026-09-20: 削除対象はAPI経由のDLQ診断・Recovery・pipeline resumeと、補正preview/apply・SNS smoke testを提供していたことを確認した。
- 2026-09-20: `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`成功。`CollectionQueueCutoverContractTests`は11/11成功。変更した2 recordのvalidatorはissues=0。literal searchの残存参照は本record、不存在test、supersession付きの歴史証跡だけで、現行利用手順はない。
- 2026-09-20: PR #52のapp-ci run `35450766713`は成功し、merge commit `d48f792`としてmainへ反映した。`origin/main`で削除対象2ファイルが存在せず、`app-ci.yml`、`app-deploy.yml`、`infra-deploy.yml`が残ることを確認した。

## Deviations and follow-up

- CodexローカルrunnerのDPAPI資格情報が未設定の場合、管理API操作の前に利用者が`invoke_local_monitor.ps1 -ProvisionCredential`を一度実行する必要がある。本変更のworkflow削除自体を妨げない運用前提として扱う。
