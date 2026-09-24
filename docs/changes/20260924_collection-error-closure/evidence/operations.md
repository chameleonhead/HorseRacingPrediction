# 公開・配備・安定稼働確認

## 最新配備と再開（2026-09-24 23:03 JST）

- 独立GETで discovery:2026092106/task162c424b-ea16-43a5-8612-9d6dea32c4e1 が22:59:03.6729634にSucceeded、続く discovery:2026092109/taskb9c949cf-269b-4c2a-a5e3-219334da8a1d が23:00:17.0049667にSucceededを確認。両方CollectedMeetings=10/CancelledMeetings=1、20260921:Nakayama:4:7と公式0921日別番組を根拠に中止処理を通過。番組誤分類の修正は本番の独立後続成功で確認できた。
- 23:02:49 GETでpaused=false、discovery:2026092115へ進行。horse-profile task69ba8fc2はRunningのため成功とみなさない。約7分の進行は30分監視2周期・全対象・金曜鮮度の検証を代替しない。
- T1-A1 continuation5のread-only結果: race-detailの振替検出には有効CNAME、開催同一性、7日以内、request sinkが必要。日別中止判定はdiscovery専用で、既存S2は振替未確定時backoff。旧race-detail公開待ちの終端化は新しい判定条件になるため、この解除許可だけで変更しない。
- 提案（未承認、T2f/T3a/AC7）: 公式日別番組の開催全体中止と旧taskの開催identityが一致した場合だけUnavailable/NotApplicableで待機を終了し、旧ID・履歴を保持する。代替日の収集は独立し、旧IDへ代替結果を書かない。CNAME欠落、開催不一致、他場だけの中止、曖昧な告知、通信失敗では終端化しない。影響は既存backoff契約の変更、代替案は現行待機維持。利用者判断待ちで、実装・データ補正は未実行。受け入れ条件は一致時のみ終端、上記反例の非終端、後続開催の独立収集、履歴保持。設計詳細確定と承認後にMainが状態契約を保持し、局所実装の委譲可否を再評価する。

- PR69（分類修正）merge0e3f74aa、CI36004077096成功。配備36004708127はtransport欠落発見でverify中cancel、本番jobは実行せず。
- PR70はruntime9d33ac26とskill a7b1310bの別commit。GitHub4094263765の「別commitにする」指摘へcommit/file一覧の反証を返信4094270583、thread解決済み。既存CI36006843145成功、f07a190dへmerge。
- 既存app-deploy36007623965はverify/build-and-push/deploy-collector-lambda/deploy/migrate-race-entry-owners全成功。Ownerは従来のpreviewのみ、データ補正なし。新Workflowなし。
- 22:55:10 GET health200、Running0、停止notification f7ffb34f/task2c26ac7aの既知解析失敗を確認。22:55:26.7164129、Mainがresume1回（204）、GET paused=false。初回解除後の無条件反復ではなく、新revisionと同原因解消を確認した再開。Recovery/履歴削除/未知ID補正なし。
- 22:56:10、旧race-detail task e4f7d3bd（20260921:Nakayama:7、rev1）のattemptがRaceResultNotYetAvailableで待機へ戻り、Result AwaitingPublication facetとResolveResult stageが本番に保存された。HTTP証拠転送は独立観測できたが正常取得成功ではない。後続Nakayama10/12からdiscovery:2026092106 task162c424bへ進み、22:58時点paused=false。
- 中止日の旧race-detailが公開待ちを反復する問題は未解決。T1-A1 continuation5が既存契約と原因をread-only確認し、MainがT2f/T3a/AC7として保持。通知Recoveryや推測した代替Race IDで解消しない。
- 22:38の独立domain GETでは20260920:Nakayama:3に出走13/結果13、ResultDeclaredAt9/20 11:21を確認。metadataのdomainRaceId欠落時は既存DeterministicIdGeneratorを使用。既存データの存在証拠であり、21:57taskの新規保存や完全性の証明ではない。

観測継続: 新規成功taskと次の独立成功、元programme誤分類の再発なし、Card/Result保存証拠を確認する。未確認期間・金曜/開催日鮮度は未検証のまま。全体StatusはApproved。

## 2026-09-24 停止解除の判断権限

transport公開前checkpoint: 最終実payload値に合わせたRelease build0 warning/error、全solution非External1217 passed/既存1skip、全solution format verify、audit/DDD/skill validator、diff check全て成功。次は同じ修正だけをcommit/push→PR既存CI/review→既存app-deploy→本番GET/resume。スキル補強だけは別目的commitに分離する。Owner/profile、旧失敗Recovery、将来鮮度は未完了。証拠欠落/重複facetの元失敗commandも成功した全体試験に含まれ、未解決test失敗なし。

T2e-A1 continuation13はP1閉鎖を独立確認。全stage履歴、部分失敗後のCard保存日時/版数、Result独立状態、lease事前照合を保持。testの所有者検証成功時Persisted=true/失敗ValidationFailureを実handlerへ一致させる補足も採用する。DDD skill validator成功。新ruleの観測対象は同HTTP4件のfirst-write/partial-failureと旧payload反例、今後別DTOを持つ完了通知変更で適用、単一processへ負荷が波及する場合はこの限定条件を再検討する。

P1の反証は実際に同一ResourcePk/Artifact tracking例外を再現。Store.Localから既存tracked facetを再利用する最小修正後、HTTP4件成功（複数Card stage、Card保存後Owner検証失敗+Result保存、公式Unavailable、旧payload）。field欠落と複数stageを落としたfixtureの共通原因は、handler/store単体結果を跨process契約の証明として採用した検証不足。learn-from-implementation-failures/skill-creatorに従いDDDの既存transport gateへ「optional証拠fieldの両端対応と実payloadの同一entity複数stage」を1項目だけ追記。routing/model既定値は変更しない。validatorとHTTP反例再実行をgateとし、他の単一process変更へ一律負荷を追加しない。

上記reviewの追加反証でP1: 実handlerは初回CardにPersistCard→ValidateCardOwnersの複数stageを出す。StoreのDB検索は同transactionでAdded未保存facetを返さず同じ複合PKを二重追加する。transport開通後の初回完了が500になるため、配備前blockingへ訂正。実sequenceのHTTP反例を追加し、Storeの同一resource/artifact tracked entityを再利用して履歴全stageを保存する。MainがStoreと同testを排他所有、API/DB設計は変更しない。再現→修正→再reviewをgateとする。

T2e-A1 continuation12 independent review: blockingなし。API末尾optional追加による旧payload互換、persistedとNotApplicableの区別、Storeのlease照合が証拠writeより先、internal API認証を確認。wrong-lease/片側failureのHTTP追加反例は推奨だが既存Store/handler検証で現差分のblockingではない。fake handlerの境界testは本番保存や過去証拠復元の証明ではなく、本番新attemptの独立GETが必要。モデル/usage取得不能、retry/promotionなし。

HTTP境界再現: 新規CollectionCompletionTransportTestsは修正前2件がfacet期待2/実際0で失敗し旧payload1件成功。両端転送修正後3件成功（通常Card/Result、Card公式Unavailable+Result成功、旧payload）。全体成功をfacet保存の代理にしない。既存Store直呼びtestは通っていてもtransport欠落を捕捉しなかったため、今後は実HTTP回帰を保持。app-deploy36004708127 cancelledを確認、本番deploy job未実行。

追加closure（T2b/T2e/T3a、AC2/3/6/7）: 成功taskのfacet空を調査した結果、handlerのStageOutcomes/RaceEvidenceがworker HTTP DTOおよびAPI DTO/変換で両方欠落することをT1-A1 continuation4とMainが照合。GETは保存済みfacetを省略せず読むため、単なる表示問題ではない。既存保存契約の配線bugとして両端へoptional fieldを転送し、worker→実HTTP endpoint→SQLite→GETの回帰で保存・部分Unavailable・旧payload互換を確認する。架空の過去証拠は補完しない。Mainが2 production filesとAPI境界testを排他所有（transport/persistence判断と統合不可分）。独立reviewはread-only。PR69 CI36004077096成功・0e3f74aa merge済みだがapp-deploy36004708127をverify段階でcancelし、本番mutation前に追加修正を同じ既存CI/CDへ含める。停止解除はその後。

利用者「停止状態は任意に判断して解除してください」により、安全確認後のpipeline resumeをMain判断で実行可能とする。既知原因の修正配備13966c9c/all jobs successと、停止理由が同じ中止開催discovery通知であることを再確認した。今回まずresumeのみ1回、失敗通知Recovery・履歴削除・未知データ補正は行わない。既存待機taskの再開を観測し、新たな未知停止や同原因再停止では解除を反復しない。Mainが本番操作・判断・本recordを排他所有し、短い直列操作のため委譲なし（T3a/AC3,4）。事前Running/health確認→POST resume→GET状態→最大10分のtask進行観測を行う。既定dispatchは1秒/5秒だがruntime設定は未取得、周期完了や安定稼働を推測しない。親recordはApprovedを維持する。

実行:21:56:35.1517329 JST resume1回、直後GET paused=false。task32ce5564-43b3-4073-8671-a95e1e6988bf（20260920:Nakayama:3/race-detail）は21:57:16成功、Applied/Required2、LastCollectedAt更新。ただしfacet空のためCard/Result個別保存完了とは断定しない。後続discovery task2c26ac7a-6a46-45bd-bf4a-fe6630b7bc82（discovery:2026092000）が21:58:22にJraPageParseExceptionで失敗し再停止、追加resumeなし。CollectedMeetings7/CancelledMeetings1の証拠を保持、9/21番組URLでRaceListの競馬場解析失敗。
AC2/4/7の局所closure: 中止確認で残った日別番組ページの通常開催表をRaceList parserが誤認し、次のnavigationのcurrent-page最適化が例外になる。番組全体を単一courseへ推測せず、既知の公式日別番組URLはRaceList分類対象外とする。未知JRADB parse failureの安全停止は維持。Mainがparser/回帰/E2E/本recordを排他所有（page identity判断と統合検証不可分、短いfixtureの分割費用過大）。既存read-only explorerのT1-A1 continuationで経路を独立確認、telemetry不明。CodeGraph exploreはindex不在でfallbackを明示、rg使用。回帰は番組ページの取消確認→同一sessionで正常開催へ移動と、通常一覧/未知解析失敗保持。実サイトでも連続navigationを検証してから既存CI/CDへ進む。

## 2026-09-24 公開開始

分類修正の公開前gate: Release solution build成功（0 warning/error）、全体非Externalは1213 passed /既存1 skipped、format全体verify成功、audit/DDD各validator成功、diff check成功。次は専用branchへcommit/push→既存app-ciとreview→merge→既存app-deploy→GETとMain判断resume→代表/後続観測。変更10ファイルは全てこのclosure、目的外未コミットなし。親AC3/6/7は未検証のまま。

独立review T2e-A1 continuation11: blockingなし。公式exact host/path除外、JRADBの解析失敗保持、current-page probeから通常遷移を独立照合。fakeは別日正常遷移、同日別場はlive E2Eで検証というcoverage境界を記録。本番観測は別gate。requested gpt-6-sol/high、observed model/usageは取得不能、再指摘・昇格なし。Release buildは警告/エラー0。T1-A1 continuation3の経路仮説はMainのred→greenとliveで反証確認。

直前closure検証: `ToRaceListAsync_AfterCancellationProgramme_ContinuesNormalNavigation` は修正前に本番同形のJraPageParseExceptionで失敗、修正後成功。focused72件、Scraping非External277件、実サイトsame-session1件成功。既知番組URL除外と未知JRADB一覧のparse failure維持を反例で確認。全solutionのCIと同じformat verify成功。Release build、全体regression、独立reviewと配備/本番観測は続行中。外部情報の追加取得元・停止policy・永続化・ID契約は変更しない。JRA site contract impact: Updated（正本27の日別番組と一覧の境界）。

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

## PR #67 追加レビューの閉鎖

GitHub inline 4093202841（移行でCard証拠が消える）と4093202851（Unavailable後の次版数の不足表示が更新されない）を同じ承認済み保存契約の既存バグとして修正。MainがStore/testsを排他所有し、恒久routing/Workflowの追加はしない。
read-only reviewerの追加2回で、既存unified rev2 aggregateをrev1へ降格する移行経路、Blocked/Unknown facetの欠落保存証拠、Currentなのに日時不明な旧stateを反例として追加。既存unified aggregateは維持し、未完了の補完taskはそのRequiredRevisionを使う。previewも同じ判定。既存facetはstatus/error/要求版数を保持し、Applied0かつ保存日時nullのときだけ日時のある旧保存証拠を補う。片方だけ欠落した不整合や、既に削除された旧証拠は推測補正しない。
focused 13件成功（移行、既存rev2完全/不完全、日時不明、既存facet、Card不足rev2→3）。途中失敗はテスト用direct URL navigatorの未設定で修正。独立read-only再reviewで新規blockerなし。全体回帰・build・CIが残るため本番完了ではない。限定Recovery/resumeは引き続き利用者回答待ち。

push前checkpoint: Collector非External 318件成功、solution build警告0/エラー0。format全体検査で今回追加したinitializerの改行のみ指摘され修正、変更2ファイルのwhitespace再検査成功。audit/DDD validator成功、diff check成功。次は同じPR67へpush、最新CIと全review指摘を照合して既存app-deployへ進む。本番の限定回復/2周期/鮮度、profile同定は未完了。意図的な目的外未コミットファイルなし。

PR67の追加commit943f4580はCI35996540844成功、既存inline2指摘は根拠付き返信・解決済みで追加指摘なし。2026-09-24T12:07:40Zに13966c9cとしてmerge。既存app-deploy35997116414で事前検証中。21:02:52 JSTのread-only monitorは29 findings/27 actionableで不変。requiresHuman=falseは分類対応済みの指標で、正常性の証拠ではない。デプロイ成功・本番復旧・安定稼働はまだ未確認。

## 配備結果 / 操作承認gate

2026-09-24 21:18頃JST、既存app-deploy35997116414（13966c9c）はverify / build-and-push / deploy-collector-lambda / deploy / migrate-race-entry-ownersの全job成功。API health、legacy移行apply後のpreview収束検査を通過。owner jobは名称に反して承認待ちpreviewのみで、Ownerデータ補正は行っていない。新Workflowなし。
21:19:01 JSTの独立runner GETは29 findings/27 actionable、pipeline GETはpaused=true、更新時刻2026-09-23T23:51:34.5602218+09:00を保持、Running0。配備が元pauseを解除しないことを確認。Workflow成功は実際の収集終端の代わりではない。API自体はrevision情報を返さず、runtime SHAはCI/CDの配備証拠に依存する限界を維持。
限定Recovery通知dbca6a70-d70b-4ffe-8a6f-bbc90e643e68一件＋一度resumeの承認は未回答。MainによるRecovery、resume、履歴削除、failure解決、未知ID補正は未実行。次の許可後に対象残存/停止/Runningを再GET→限定操作→代表終端と独立後続→2周期/鮮度を確認する。Owner/profileの残課題は親recordに残す。親はApprovedのまま、Implemented/安定稼働と報告しない。
