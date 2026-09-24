# 公開・配備・安定稼働確認

## 2026-09-24 公開開始

利用者の「変更をプッシュした上で、安定稼働まで面倒を見てもらえますか？」により、承認済み修正のbranch/commit/pushを許可。現在の基準mainはff95b224でfetch後も不変。
独立worktreeの本変更だけを専用branch `codex/collection-error-closure` へ公開する。元作業ツリーのUI/Program/skill等の変更は含めない。

以前のActions禁止はまだ撤回されていない。app-ciはPR/main、app-deployはmainで起動するため、専用branchへのpushのみ先行しPR/main反映は確認待ち。既存の収集監視Actionsは復活させない。
AWS read-only get-functionは認証期限切れ。利用者へaws loginを依頼済み。認証完了を推測せず、配備revision/schema/rollbackを確認する前に本番変更しない。

## 残るgate

1. 利用者のAWS再認証と、今回のテスト/配備Actionsの利用可否。
2. O1-O3: API/Collector/DBの版・互換性、backup/rollback、配備先の確認。Actions以外を選ぶ場合も同じgateを満たす。
3. O4-O7: 代表通知を原因別1件、初回最大5件に限定し、対象と安全条件を明示してRecovery/resumeの操作承認を確認する。履歴削除・曖昧な主体ID補正・一括再実行はしない。
4. 本番の対象終端・独立後続成功・2周期以上の再停止なし・金曜Card/結果鮮度を実測。公開/merge/配備だけでImplementedにしない。

MainがGit公開と本番操作gateを直列所有。今回新規coding/委譲なし、既存reviewと実経路の証拠を引き継ぎ、push前にformat/build/非External全体testを再実行する。

## 追加指示と既存配備バグの閉鎖

利用者が既存CI/CDでの配備を明示許可し、新Workflow追加は禁止と確定。既存バグはそのまま修正する指示を受領。PR/main反映と既存app-ci/app-deployを利用する。監視Workflowは追加・復活しない。
配備前reviewで `trap resume EXIT` が移行失敗時にも再開し、元からpausedの状態も消す既存欠陥を確認。これはAC4/O2/O7の停止維持に反する局所欠陥であり、安全契約を変えず閉鎖する。
Mainが既存app-deploy.ymlのみの移行stepを修正する。事前GETのisPausedを厳格boolean確認→pause→apply→preview成功→事前稼働時だけresume。失敗・不明な事前状態・元からpausedではresumeしない。別Workflowは作らない。
Mainが成功/失敗/元paused/応答不正のshell反例を実行し、既存reviewerがread-onlyで配備境界を確認する。独立したworkflow safety修正としてcommitを分ける。role/model/telemetryは既存T2e-A1 continuationを引き継ぎ、観測値は推測しない。

独立reviewでCollector先行更新の混在時間帯、migration drain不足、stop失敗無視後のSQLite backupを指摘。AC4/O2/O7の局所closureとして既存Workflowに配備前pause/drainを追加し、元pause状態をjob outputで引継ぐ。API停止成功/非稼働/nonempty WALなしをbackup前に必須化。失敗時は自動resumeせず、元のpause reasonも上書きしない。
`tests/scripts/test-deploy-pipeline-state.ps1` は実Workflow内shellの16反例をcurl/jq outcome stubで実行し成功。jq自身のparser検証ではなくshellの制御フロー検証である。既存app-ci/app-deployのverifyに同testを追加、新Workflowは0。
20:09 JST read-only monitorは29 findings/27 actionableで不変。続くpipeline/tasks GETはpaused=true、Running配列0件。GitHub上の最新成功app-deploy run35875662471はff95b224（今回基準）で、今回schema差分なし。ただしAWS直接runtime版数確認の代替として過大主張しない。
公開済み: d1f82d7e（設計/証拠）、80b094f4（収集経路の整合修正）。後者はmetadata/lease/facet/handlerを同じ実保存反例で検証する一つの復旧単位であり、片方だけでは回帰gateを満たさない。push前再検証はformat成功、build警告0/エラー0、非External1198成功+既存1skip。別目的の配備ガード修正は別commitにする。

再reviewのdrain中未知障害反例を受け、元稼働の自動resumeにも要対応failure0件のGET検査を追加。新旧問わず残件があれば停止維持する保守的gate。17 shell反例成功。17件はjq/curlのoutcome stubによる実shell検証であり、本番動作の証明ではない。現在の正本docs/11へ同時反映。

最終read-only再reviewで上記反例閉鎖を確認。MainはSSH scriptにset -euを明記し、stop/copy等の失敗後に処理が継続しないことを静的gateへ追加。reviewerの継続3回をT2e-A1へ追記（累計6回、usage不明）。残存するoperator pauseとresume間の競合には条件付きAPIが必要だが、現在の本番は元からpausedで自動resumeを通らない。本sliceで新たな本番再開権限を追加しない。

PR #66、app-ci run35992648221は17反例全件PASS後にpwsh wrapperのLASTEXITCODE伝播で失敗。最後の意図した負ケースの1が残るtest harness局所欠陥で、成功終端exit 0を明示して再検証する。実際のassert/throwは引き続き失敗する。配備は未開始。
