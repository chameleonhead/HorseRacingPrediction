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
| Verification | In progress | 最終headのLinux CIとapp-deploy verify成功。本番切替未実施のため配備後検証は未完了 |
| Deployment/operation | Externally blocked | app-deploy 36249216632が既存pause/drain APIへのHTTP502で失敗。API/collector切替は未実施 |

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | user reports data deleted; remote DB状態は未確認 | 旧Entry混在リスク | 追加削除せず空データ/旧Entry残存を読取確認。残存時に勝手に移行しない | AC-D2/T-D2 | Agree | データ削除済み・配備を明示依頼 | Resolved in design |
| C2 | /health=502、現APIのpipeline/Running確認が配備前提 | 通常workflowが失敗する可能性 | 既存gateは変更/迂回せず実行。失敗時は停止を維持して結果報告 | AC-D2/T-D2 | 配備成功は断定できないがfail-closedの既存手順実行には同意 | 現状のまま配備・問題ないことを利用者が確認済みと明示 | Accepted risk |
| C3 | 新EntryId/nullable DTOは旧版と非互換 | 混在/rollback時の破損 | API/collector同一SHA、pause/drain、新規書込後の旧binary単純rollback禁止 | AC-D1–3/T-D2–3 | Agree | 馬基準仕様と配備を承認 | Resolved in design |
| C4 | コード成功と本番収集復旧は別 | 完了誤認 | health/版/元pauseを確認、既存incident pauseは解除せず結果を分離報告 | AC-D3/T-D3 | Agree | 配備依頼、追加削除/incident解除は依頼なし | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC-D1 | 最終PR headのLinux CI成功後、API/collectorの同じ確定版を配備 | T-D1,T-D3 | CI成功、イメージ公開成功、本番切替未実施 | Connected |
| AC-D2 | 旧データ混在と稼働中旧workerを確認し、追加データ削除・未知pause解除なし | T-D2,T-D3 | 利用者確認済み、実行時pause/drainは502で未確認、追加削除/resumeなし | Connected |
| AC-D3 | 本番healthと配備版・収集状態を検証し、復旧と未検証/除外を区別して記録 | T-D3,T-D4 | 配備失敗の実行ログ/PRコメント。本番更新未実施のため稼働確認未完了 | Connected |

## Task plan

| ID | Task | Owner / tier | Depends on | Write scope | Verification / evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T-D1 | PR/CIの準備と結果確認 | Main/Lead | - | current branch/docs/GitHub PR | PR99 final d201f979 CI36248880189成功、merge9b90e193 | Verified | 外部publishと統合の責任をMain保持 | none | unavailable |
| T-D2 | 配備前提の確認 | Main/Lead | user confirmation | read-only and user evidence | 利用者が削除・意図的停止・問題ないことを確認済みと明示。実行時pause/drainは既存workflowで強制 | Verified | ローカル本番変更なし、既存gateの結果はT-D3で確認 | none | unavailable |
| T-D2R | 既存配備方式の独立確認 | entry_reference_inventory/Reviewer | fixed deploy scope | local read-only | app-deploy.yml:344–374のAPI gate、collection DB/queue独立性、旧binary rollback制約を確認 | Verified | 分離した読取review、操作権限なし | none | model/token unavailable |
| T-D3 | 同版配備・稼働確認 | Main/Lead | T-D1,T-D2 | existing production deployment | run36249216632 pause/drain HTTP502で失敗。本番切替未実施 | Externally blocked | 本番mutationの順序とscopeをMain保持 | none | unavailable |
| T-D4 | 記録・branch終了 | Main/Lead | T-D3 result | PR comments;new docs branch | PR99コメントで失敗結果保存、実装local/remote branch削除確認。本書は別docs PRで反映 | Verified | 最終判定とGit保全をMain保持 | none | unavailable |

## Execution / rollback

1. 既存実装branchをPR化しLinux CIを検証。最終headの成功前にmergeしない。
2. 最新の利用者判断に従い、削除/意図的停止/配備前提は利用者確認済みとして既存workflowを実行する。追加のローカル本番操作や新しい配備経路は作らない。API生存前提が満たせなければ既存gateで停止し、迂回しない。
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

最終配備判断: 停止状態からの配備経路追加案に対し、利用者は「現状のままでデプロイすればよいです。問題ないことはこちらで確認しています」と明示。配備方式変更を行わず、CI成功後にPR #99をmergeして既存app-deployを実行する。Mainは502時に既存gateが失敗する可能性を説明済み。成功を推測せず実際のworkflow結果を記録し、失敗時の自動迂回・削除・resumeは行わない。この判断をC2へ反映し、ローカル接続待ちは解除する。

## Deployment attempt result / final review

- PR #99 final head `d201f9791cb7c89f0038d7e0e6643f486e510fcc` のLinux CI [36248880189](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36248880189)が全項目成功。merge SHA `9b90e19340923fbed14b67139b812326082b2ba4`。
- [app-deploy 36249216632](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36249216632)はverify/build-and-push成功後、2026-09-26T14:46:43Zに `Pause and drain collection before changing deployed versions` でHTTP502、curl exit22となり失敗。collectorのTerraform更新前に停止し、API deploy/owner previewはskipped。本番API/collectorは新版へ切り替わっていない。
- 追加データ削除、ローカル本番変更、gate迂回、pause解除は実行していない。公開済みimageだけを本番反映と扱わない。
- [PR99実行結果コメント](https://github.com/chameleonhead/HorseRacingPrediction/pull/99#issuecomment-5847191019)に保存。実装branchはclean/merge ancestry/tree一致確認後、local/remoteとも削除。merge後に同branchへcommitなし。本結果は最新mainから別docs branchで反映。
- Main final review: T-D3がAC-D1〜3を阻害しているため本記録はApprovedのまま。本番配備完了・収集復旧とは報告しない。
- 次操作: 現行workflowが必要とするAPI応答の回復、または停止状態からの配備手順変更について利用者の方針を確認。その条件が変わるまで同じworkflowの盲目的再実行はしない。再開後は既存gate/同版切替/health/元pauseを確認する。意図的に残す未コミットファイルなし。
