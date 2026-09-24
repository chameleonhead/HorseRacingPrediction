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

T1c追加read-only探索: `/root/collection_code_map`（既存requested gpt-6-sol/medium、observed/usage不明）へ9da0e2dbの既知preflight/retirementとOwner同定境界を委譲。MainはGit/CI/本番gateを保持。書込・本番API・資格情報・新規testなし、既存symbol/test引用を成果とする。既存T1-A1 continuationとして扱い、結果採用前にMainが安全境界と照合する。

20:24頃JSTのGETで、中止開催groupのcanary通知dbca6a70-d70b-4ffe-8a6f-bbc90e643e68、task8886fd12-8ace-4786-a4ab-1ac6405d96e4、resource discovery:2026092103が残存。配備後この通知1件のRecoveryと一度resumeする限定操作承認を非同期で依頼。回答前には実行しない。

## PR #66 / 配備前追加closure

PR #66はCI35992760489成功後、632d24afでmainへmerge。既存app-deploy35993358449はverify中にcancelし、build-and-push / Collector / API配備は未実行。理由はmerge後に確認したGitHub inline review4093059292のhistorical Card revision指摘。必須checkだけではreview完了を示さないため、次のmerge前はreview本文・全inline指摘・最新headを独立照合する。既存review gateの適用漏れとして本recordにclosureを残し、恒久model/routing変更は行わない。

指摘経路をMainのStore→lease→実handler→Store反例で確認。ただしS2および承認済み20260921 record AC14は過去Card取得不能時のResult-only成功を許容するため、全体をValidationFailureに変える案は契約不適合として不採用。独立reviewerも同境界を確認。局所修正はCard版数不足の取得不能をNotApplicable stageで明示し、Card facetの旧AppliedRevision/LastPersistedAtを保持したUnavailable、ResultはCurrent新版とする。全体Currentを全facet同版数保証へ変更しない。初回反例は3件中1失敗、修正後は既存の近日日付/既存Result保持を含む5件成功。

追加closureの検証: Collector非External310成功、format verify成功、独立read-only再review阻害事項0。新しいtest名は `RaceDetail_HistoricalCardUpgrade_ExposesUnavailableFacetWithoutBlockingResult`。旧Card版数不足、同版数、Cardなし過去Raceの3ケースを実Store/handlerで確認。hintは永続metadataから引き継がずStoreがfacetから再計算する。

T1c探索結果をMainが既存契約と照合: SubjectResourceMissingの候補/retirement内部部品は公開経路に未接続、旧ID参照有無を安全判定に使わない箇所があり一括実行不可。Ownerは名称だけで同一性を断定せずRaceEntry/alias/canonical ID証拠が必要。現在の配備sliceにID補正やretirement公開を追加しない。残課題T1c/T2fは未完了のまま。
