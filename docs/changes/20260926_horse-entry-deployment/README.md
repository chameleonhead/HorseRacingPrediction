# 馬基準出走識別の本番配備

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-26
- Updated: 2026-09-26

## Context / approval

利用者は実装・ローカル検証完了の報告後、「本番デプロイをお願いします。データ削除は実行済みです」と明示した。[実装記録](../20260926_horse-based-race-entry/README.md)のprogram-only境界を、本記録の配備スコープだけ拡張する。新しいデータ削除・移行・既存障害の一括retry・incident pause解除は含めない。以前承認された短時間API停止は既存配備方式で必要な範囲に限定する。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | 167c318a、文書e3ba3feb、Release0warning/error・1323pass/1skip |
| Verification | In progress | Linux CI、配備前remote状態、配備後health/同版確認 |
| Deployment/operation | Externally blocked | 現行API502、ローカルAWS認証期限切れ、既存SSH鍵は接続拒否。認証更新を依頼済み |

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | user reports data deleted; remote DB状態は未確認 | 旧Entry混在リスク | 追加削除せず空データ/旧Entry残存を読取確認。残存時に勝手に移行しない | AC-D2/T-D2 | Agree | データ削除済み・配備を明示依頼 | Resolved in design |
| C2 | /health=502、現APIのpipeline/Running確認が配備前提 | 通常workflow停止 | 安全gateを迂回せず接続回復後に状態確認し最小起動を判断 | AC-D2/T-D2 | Agree | データ削除後の意図的container停止と説明・継続指示 | Resolved in design |
| C3 | 新EntryId/nullable DTOは旧版と非互換 | 混在/rollback時の破損 | API/collector同一SHA、pause/drain、新規書込後の旧binary単純rollback禁止 | AC-D1–3/T-D2–3 | Agree | 馬基準仕様と配備を承認 | Resolved in design |
| C4 | コード成功と本番収集復旧は別 | 完了誤認 | health/版/元pauseを確認、既存incident pauseは解除せず結果を分離報告 | AC-D3/T-D3 | Agree | 配備依頼、追加削除/incident解除は依頼なし | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC-D1 | 最終PR headのLinux CI成功後、API/collectorの同じ確定版を配備 | T-D1,T-D3 | GitHub checks/workflow image SHA/health | Not started |
| AC-D2 | 旧データ混在と稼働中旧workerを確認し、追加データ削除・未知pause解除なし | T-D2,T-D3 | remote read-only inventory、pause/drain、workflow状態 | Not started |
| AC-D3 | 本番healthと配備版・収集状態を検証し、復旧と未検証/除外を区別して記録 | T-D3,T-D4 | health/API GET/実行ログ/PRコメント | Not started |

## Task plan

| ID | Task | Owner / tier | Depends on | Write scope | Verification / evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T-D1 | PR/CIの準備と結果確認 | Main/Lead | - | current branch/docs/GitHub PR | final head/checks | In progress | 外部publishと統合の責任をMain保持 | none | unavailable |
| T-D2 | 配備前の本番状態・空DB/502の確認 | Main/Lead | credentials | production read-only;必要最小起動判断 | health502、AWSexpired、SSH拒否を観測 | Externally blocked | 認証・本番停止・データ完全性判断はMain専有 | none | unavailable |
| T-D2R | 既存配備方式の独立確認 | entry_reference_inventory/Reviewer | fixed deploy scope | local read-only | app-deploy.yml:344–374のAPI gate、collection DB/queue独立性、旧binary rollback制約を確認 | Verified | 分離した読取review、操作権限なし | none | model/token unavailable |
| T-D3 | 同版配備・稼働確認 | Main/Lead | T-D1,T-D2 | existing production deployment | workflow/health/版/元pause維持 | Dependent | 本番mutationの順序とscopeをMain保持 | none | unavailable |
| T-D4 | 記録・branch終了 | Main/Lead | T-D3 | PR comments;必要時new docs branch | origin record reconcile、merged branch削除確認 | Dependent | 最終判定とGit保全をMain保持 | none | unavailable |

## Execution / rollback

1. 既存実装branchをPR化しLinux CIを検証。最終headの成功前にmergeしない。
2. 認証更新後、container/service/logsと新旧DB/collection状態を読み取る。認証情報は出力・文書へ記録しない。通常workflowのAPI生存前提が満たせない場合は配備を開始しない。
3. 既存app-deployのpause/drain→collector→API→health→migration no-op確認の順序を利用。userが削除済みと述べた範囲以外のデータは削除しない。実行前のpauseを保持する。
4. 失敗時は安全停止を維持。旧データへ新IDを混在させる/新版データへ旧binaryを戻す操作はしない。追加の破壊的操作やscope拡大が必要なら根拠と選択肢を示す。
5. マージ後の結果はまずPRコメント/実行ログに保存。同じmerged branchへcommit/pushしない。リポジトリ内の完了状態更新が必要なら最新mainから別branch/PRを用い、各merged local/remote branchを安全確認後に削除する。

## Review gates / documentation updates

Mainが標準配備順序とnon-goalsを固定。ユーザーの配備承認に基づく既存手順の実行であり、未承認の移行/全体retryへ拡張しない。独立readerは手順の危険箇所だけ照合し、Mainが適用判断を保持する。現在の外部blocker解消後にpre-deployment gateを再確認する。

既存手順自体は変更せず、実装記録へこの承認済み配備記録のリンクを追加する。正本 `docs/26-collection-platform-design.md` の既存pause保持規則に従うため運用仕様の変更なし。配備を新規承認した事実と実際の配備完了は区別する。

## Verification / next action

read-only: GitHub main=8bbee4b0、同版の前回app-deploy36230620670は成功。現在branchはe3ba3febでPRなし。2026-09-26今回GET /health=502。AWS STSはsession expired、SSHubuntu/ec2-userはいずれもpublickey拒否。秘密を記録せず利用者へaws loginを依頼。CodeGraphはindex未存在、通常検索へ切替。

次操作は本書とlinkの検証/commit→PR/CI。配備前gateを満たす経路の確認後にT-D2を再開。配備成功と確認できるまでは完了を報告しない。

追記: 利用者は502について「本番データの削除後、コンテナを止めているため」と説明し継続を指示。意図的停止として扱い、未知のアプリ障害と断定しない。AWS認証は再確認でも期限切れであり、停止理由とは独立した接続blockerとして更新を依頼した。独立reviewも、停止中APIでは既存workflowの事前gateが通らないこと、DB削除とSQSは別であることを確認。Mainはgateを迂回せず、接続回復後の状態確認を維持する。

最新指示: 配備は必ずGitHubへのpush（PRまたはmain直接）経由とする。ローカルから本番を変更しない。PR/CIを先行し、既存workflowに停止中からの起動経路がない点を明示する。ローカルAWS認証更新はこの経路の前提にしない。
