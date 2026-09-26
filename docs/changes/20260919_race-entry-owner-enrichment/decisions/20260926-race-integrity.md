# 2026-09-26 阪神11Rの取得・保存整合性

- Governing record: [Race出走馬の後着データ補完](../README.md)（Proposed）
- Owner: 本調査threadの主担当。監視親のT2b/T3a・保存完全性findingへ接続する。
- Authorization: 証拠整理・設計文書のみ。コード、deploy、新Workflow、本番mutation、再収集、retry、Recovery、DB補正は未承認。
- Parent boundary: `docs/changes/20260919_collection-monitor-root-cause-triage/README.md` は変更禁止。

> 2026-09-26 follow-up: 利用者の指示で[本番補正までの一括設計](20260926-full-repair-plan.md)を具体化した。以下の証拠・懸念は調査時点の履歴を保持する。最新処置は一括設計を優先し、IC2–IC4は設計上の処置を提示済み、IC1の全体停止容認は未決。IAC1–IAC5は未完了のまま維持する。

## 観測証拠と限界

本threadの2026-09-26 JST初回調査（JRA表示オッズ時刻03:18付近）の証拠。以下はその時点のsnapshotであり、15:50以後の実行・現在状態を保証しない。APIキーは記録しない。

対象: `race-fca5d100-9e2f-5074-a74c-bad8cdb4705f`、2026-09-26 阪神11R シリウスステークス。

| Evidence | Observed |
| --- | --- |
| GET `/api/races/{raceId}/context` | 16 entryのOwnerNameがすべてnull、gateNumberもすべてnull。 |
| GET `/api/races/{raceId}`・出走表 | owner表示は15/16。APIはentry ownerがnullならHorse profileから補完するため、保存完全性の証拠にはならない。GradeCode=G2、結果は未登録。 |
| GET `/api/horses/horse-5c58b057-dea6-50c6-96d1-1f4dbeb09dff` | ダブルジョークのownerもnull。 |
| GET `/api/admin/collection/resources/Race/Jra/20260926%3AHanshin%3A11/race-detail?...historyPageSize=100` | task `2bd306b4-6715-4422-a19e-efed5eb4aa60`、attempt `2bc16324-e145-4bb7-803d-b4c2b998266c`。9/24 23:07:43–48 JST。PersistCard=true、ValidateCardOwnersはRaceCardOwnerIncomplete / OwnerCount=0/16。親attemptはRaceNotStartedで9/26 15:50待機、failure一覧は空。 |
| 同resource | metadataはcourse/number、domainRaceIdなし。公式開始時刻15:45。Card artifactはstatus=5、appliedRevision=2、requiredRevision=2。実行batch `66e1e501-de5d-4ee5-97f8-f9584954cf15`。 |
| [公式Card](https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0109202604081120260926/08) | 同じレース日・競馬場・R、GIII。16頭の各 `.owner` に値あり。ダブルジョークはゴドルフィン。 |

| 馬名（説明用。補正キーにしない） | 保存馬番 | 公式馬番 |
| --- | --- | --- |
| ヴァルツァーシャル | 1 | 11 |
| ダブルジョーク | 9 | 8 |
| ジューンアヲニヨシ | 6 | 1 |

全16頭が公式と異なる馬番を持つ。保存順は五十音順に見えるが、初回ページが枠順確定前だったという原因は**仮説**。当時のHTML・semantic snapshotと配備revisionは未取得。現在の公式DOMは当時のDOMの代用にならない。

ローカル現行の `JraSiteE2ETests.現在週RaceCard取得` は1件成功。ただし別の公開Raceを選ぶテストであり、この対象・9/24入力・Lambda版の再現証拠ではない。AWS CLIはsession expiredで配備版確認不能。ローカルmainはorigin/mainと分岐している。通常bulkの既存entry除外、parserのowner抽出、snapshotの48断片上限はローカルと観測済みorigin/main `149d6a0d`で一致するが、そのSHAが本番実行版だったとは断定しない。

## 確定したコード上の問題と前回答の訂正

1. `RaceCardPageParser.ParseEntries` は馬番を読めなくても出現順で採番する。公式馬番のない一覧を確定出走entryとして保存できる構造がある。ただし、この分岐が当該attemptで実行された証拠は未取得。
2. `JraRaceDetailCollectionHandler` はworkflowによる保存後に `result.Entries` のowner数を数える。この値はparse結果であり、DB再読込件数ではない。保存前gateにも、raw保存後照合にもなっていない。レース開始待ちのreturnがowner validation failureより先になる経路がある。
3. `ApplyCollectedRaceResultBulkAsync` の通常経路は既存EntryId（馬番由来）のHorseIdを採用し、既存entryを `BulkRaceResultData.Entries` から除外する。domainにマージ実装があっても通常経路の後着ownerは届かない。incoming公式Horse identityと既存Horseの不一致を全件検証するgateがない。
4. `RefreshExistingData=true` は別経路 `RefreshCollectedRaceAsync`。前回答の「再取得ではすべて既存entryを除外する」という一般化は誤り。この経路は馬番でentryを探しHorseIdを更新し得るが、同じentryに残るowner・体重等をnull coalesceするため、馬の入替時に別馬の値が引き継がれるリスクがある。さらに関連主体書込みが全件検証前に行われる。
5. `LaterRaceCard_EnrichesOwner_AndReplayDoesNotCreateMoreEvents` は明示refreshを呼び、表示補完のあるRace APIだけをassertする。通常bulk、raw owner、event数、馬番入替を検証していない。試験名だけから冪等性や通常経路の補完をVerifiedとしない。
6. G2/GIII不一致は観測事実。grade parserはページ全体の画像・本文も探索するが、当該誤値の具体的生成経路は未確定。現在名からG3を推測してDBを上書きしない。

## 15:50再実行と隔離判断

観測時のmetadataにはdomainRaceIdがなく、既知コードでは通常bulkへ入る条件を満たす。再実行時metadataや配備版の再確認は必要。通常bulkが公式結果を既存の誤馬番EntryIdへ保存すると、着順・時計・払戻評価・馬/騎手の履歴や予想評価へ誤った帰属が波及し得る。副作用の実発生は未確認。owner検査は結果保存より先に必ず停止するgateではない。

技術的には対象Raceの書込み隔離が必要。既存のtask cancel APIは単一taskの停止要求であり、新要求・scheduler・既にAPIへ渡ったpayloadまで含む隔離保証は未確認。pipeline pauseも実行中書込み停止を保証しない。既存suppressionはidentity repair用途の契約を持ち、便宜的な隔離として流用しない。

推奨手順（**未承認・未実行**）: 対象Raceに関するactive task、lease、ingestion workをGETで再確認し、書込み受付とdispatchの双方で拒否できる既存機構があるか検証する。確認できればRace限定holdを提案する。なければglobal pauseだけを安全保証と称さず、既存実行のdrainとAPI書込み拒否を含む別の具体的操作案を承認対象にする。監視親による代行補正は禁止。15:50を過ぎた場合はまず新attemptとraw dataを再読込し、未発生として扱わない。

## 修正設計案と承認境界

- 取得: 明示的な公式馬番と公式Horse source identityを全頭で検証する。馬番未公表の一覧はentry保存対象外とし、暫定連番を使わない。馬主欠損を含む不完全Cardは保存前に分類する。Resultにownerを要求しない。
- 保存: 通常bulk・明示refresh・ingestion適用すべてで、関連主体作成を含む副作用前にRace identity、馬番、Horse source identityの対応を全件検証する。同一馬の後着値のみ冪等・非破壊mergeする。不一致は拒否して構造化findingにする。
- 補正: 対象Race限定のread-only previewを先に作り、公式source identityで解決した対応表、旧/新entry、owner、grade、参照中の履歴/予想/結果を提示する。氏名だけ、誤馬番だけ、現在Horse profileのownerを根拠にしない。identity未解決・重複は補正拒否。
- apply設計は、事前backup、対象version/fingerprintの一致、並行writer遮断、冪等operation ID、event監査を必須とする。旧entryに紐づく履歴・予想・結果の扱いが決まるまでapply仕様は確定しない。単なる既存entry上書き、全件migration、直接DB更新を承認依頼に含めない。
- 検証: raw contextとsource identity対応表の一致、公式Cardのowner16/16、G3、馬番対応、関連read modelへの誤帰属不存在を確認する。UIの表示補完値を成功条件にしない。

## Concern and agreement ledger

| ID | Evidence / impact | Recommendation, alternative and residual risk | AC/task | Agent / user disposition | State |
| --- | --- | --- | --- | --- | --- |
| IC1 | 誤馬番で再実行すると結果等が別馬へ帰属し得る | Race限定write holdを優先。cancel単独は代替にならない。実在する隔離機構・進行中writerの確認が未完 | IAC2/IT2 | Agent: 隔離推奨、未検証の停止保証に反対。User: pending | Open decision |
| IC2 | 初回0/16、馬番、gradeの生成原因が未確定 | 当時snapshot/log・配備版を取得。現在DOMによる推測fixは採用しない。snapshot未保存なら再現限界を明示し、同型入力fixtureで防止条件を検証 | IAC1/IT1 | Agent: 原因仮説を確定扱いしない。User: pending | Open decision |
| IC3 | EntryIdの意味を入れ替えると予想・結果・履歴が影響を受ける | 参照棚卸しと公式identity解決後に専用補正を設計。ownerのみコピーは却下。補正方式・rollback未確定 | IAC4/IT4 | Agent: 現状の直接applyに反対。User: pending | Open decision |
| IC4 | 表示fallbackがraw欠損を隠す／別経路テストのみ | raw owner・event count・通常/refresh/ingestionのcounterexampleを必須化。UI観測単独を却下 | IAC3/IT3 | Agent: 同意。User: pending | Resolved in design |

Open decisionが残るため、今回の文書は調査・設計checkpointであり、補正実行の承認依頼はまだ行わない。

## Acceptance criteria and task plan

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| IAC1 | 初回欠損の証拠/仮説/取得不能証拠を分離し、対象実行版と再現可能範囲を確定 | IT1 | attempt/batch/log/snapshotと対象fixture | Connected |
| IAC2 | 対象Raceへの不整合書込みを再要求・実行中・ingestion経路でも防げる隔離案を検証 | IT2 | lease/並行適用を含む阻止テストと操作preview | Not started |
| IAC3 | 不完全Cardと馬番/Horse identity不一致は副作用前に拒否。同一馬の後着ownerはraw entryへ保存され、再送でevent増殖しない | IT3 | 通常bulk/refresh/ingestion統合test、関連主体不変、null保持 | Not started |
| IAC4 | 公式identityに基づく補正previewが参照先と旧新対応を示し、氏名のみ/誤馬番キー/未解決identityを拒否 | IT4 | 入替・重複・stale version・既存結果/予想の反例 | Not started |
| IAC5 | 別途承認後の配備・限定補正でraw owner16/16、公式馬番・grade一致、履歴等の誤帰属なしを確認 | IT5 | backup/hold/apply/replay/再起動/post-checkと本番GET | Not started |
| IAC6 | 馬番未確定を正常待機として扱い、仮entryを作らずCard未完了を維持。確定後の最新Cardを発走前に取得し、遅着未確定入力で退行しない | IT3 | 未確定→確定・頭数変更・例外日程・待機再起動・次回Card予定のscheduler/handler/persistence統合test | Not started |

| ID | Task | Owner / tier / reason | Depends on | Write scope | Verification / completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- |
| IT1 | 初回原因と実行版を確定 | Main / Lead、過去証拠の解釈が必要 | 当時証拠アクセス | 本補足のみ | 本文証拠、残るsnapshot/版確認 | In progress |
| IT2 | 限定隔離の実在gate・並行性検証と操作提案 | Main / Lead、安全性判断 | IT1と最新GET | 本補足のみ | 正確な対象と阻止境界 | Proposed |
| IT3 | 取得/保存前gateとraw完全性修正 | Main / Lead、identity契約が未確定 | IC1/IC2解消、明示承認 | 承認後にScraping/API/Collector/Domainと関連testsを直列所有 | IAC3、受け入れ後に実装sliceの委譲可否を再評価 | Proposed |
| IT4 | 参照棚卸しと補正preview/apply設計 | Main / Lead、データ帰属判断 | IC3解消 | 本補足のみ。実装は別途承認 | IAC4 | Proposed |
| IT5 | 配備・限定補正・本番照合 | Main / Lead、運用権限と安全性 | IT2–IT4検証、明示運用承認 | 承認済み対象のみ | IAC5 | Dependent |

今回の証拠整理は短い単独文書作業のためsubagentなし。実装分割は未確定のidentity判断をworkerへ渡さず、設計確定後に再検討する。

## Review gates, documentation and next action

### 2026-09-26 03:40 JST read-only follow-up

- 03:40:40 JST開始のAPI再確認ではpipelineは稼働中。対象task `2bd306b4-6715-4422-a19e-efed5eb4aa60` はReady、attemptは既出の1件のみ、次回15:50 JST。ownerはraw 0/16のまま。公開resource DTOからlease token/expiryは確認できず、残存writer不存在の証明にはならない。
- batch `66e1e501-de5d-4ee5-97f8-f9584954cf15` は9/24 23:06:12–23:07:54 JSTに完了。中山・阪神各12件、対象はordinal 23。dispatch envelope `6ae174b7-52f7-4fe7-93d1-c4b2fd4ec218`、Lambda request `0ac78d31-055b-54e1-a622-ee1e35c9af2a`。24件はRaceNotStartedだが、他23件にもowner欠損があるとは推定しない。
- comparison GETはpredictionTickets/entryResultsとも空、payoutResult/winnerはnull。これは当該APIの投影範囲での確認であり、イベント・全履歴・全参照不存在の証明ではない。
- 16頭のhorse GETはaliasesがすべて空。しかし公式Cardの各horse hrefから `JRA|CNAME` をUUID v5（既存Horse namespace）で計算すると、**16/16が保存済みHorseIdと一致**。名称ではなく公式identityによる対応を確定できた。したがってalias不足で補正preview全体が停止する状況ではない。公式馬番順の保存馬番は `6,7,13,8,10,4,12,9,16,5,1,11,14,15,3,2`。この一致は馬番入替の安全なapplyまで承認するものではない。
- IC1の実在gate: `RaceActiveCollectionEndpointFilter` は有効leaseなら許可し、active mutationがなければ無効/未指定leaseでも許可する。`HasActiveRaceMutationAsync` はResourceIdまたはattributes.domainRaceIdを照合するが、対象resourceは日付/競馬場/R形式かつdomainRaceIdなし。対象Raceを遮断するholdとして利用できない。
- cancelは非Running taskをCancelledにした後 `MaterializeUnsatisfiedRevisionAsync` を呼び、後続taskを作り得る。Runningは取消要求のみ。pipeline pauseはcontrol更新と取得/dispatch抑止であり、書込み受付拒否ではない。identity repair suppressionはfailureをSupersededにするなど副作用があり、便宜的holdには流用しない。
- ローカルと観測済みorigin/mainのAPIに独立したingestion状態照会ルートを特定できなかった。ingestion未処理件数を0とは報告しない。配備版・当時snapshot/log・lease/適用中writerの確認はAWS認証期限切れにより証拠不足。
- **操作案は未承認・未実行**。既存APIだけによるRace限定の完全隔離は確認できない。cancel単独、pause単独、強制refreshを安全な処置として提案しない。次の設計判断は、Race限定の永続write gateとdispatch holdを実装・配備するか、全体メンテナンスの影響を受容してdispatch停止＋writer drain＋書込み受付遮断を行うか。後者の配備環境と遮断方法はまだ未検証であり、そのまま実行できる手順ではない。
- AWSなしでも進められる範囲: 上記identity対応に基づくread-only補正preview設計、EntryId参照のコード棚卸し、通常/refresh共通の副作用前gate設計。IC1–IC3は未解消、コード・本番操作の承認は未取得。新record/Workflowおよび親recordへの変更なし。

- Design/task-split: Main。初回原因、隔離の実在性、参照移行方式が未確定のため設計承認gate未通過。
- Checkpoint: 本番GET・公式DOM・local/origin/mainの経路を照合。前回答の初回原因と再取得経路の断定を修正。過去のowner-enrichment検証では今回の通常bulk欠陥を閉じられない。
- Pre-implementation/Final implementation review: 未実施。コード・本番操作は未承認。
- Documentation updates: 今回は既存recordと本補足のみ。`docs/27-jra-site-collection-contract.md` のCard由来owner・代用禁止契約は維持し、実装提案の確定時に未確定一覧の保存禁止とidentity gateを正本へ追記する。親recordと新Workflowは変更しない。
- Next action: 最新resource/contextのGETと実行batch取得、AWS再認証後の実行版/log照合、対象の公式source identityと既存alias対応・entry参照棚卸し。未解決IC1–IC3を具体化してから承認対象を提示する。
- Verification commands: `python .codex/skills/document-driven-development/scripts/validate_change_records.py docs/changes/20260919_race-entry-owner-enrichment/README.md`、`git diff --check`。文書のみでbuild/test再実行不要。前ターンのJRA外部test 1 passedは上記の限定証拠。
- Verification result (2026-09-26): change-record validator `issues=0`、`git diff --check`成功（既存ファイル等のLF/CRLF警告のみ）。
- Intentional uncommitted files: 本recordのREADMEと本補足。既存ユーザー変更はそのまま保持。今回コード、本番データ、queue、taskを変更していない。
