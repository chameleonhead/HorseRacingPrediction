# 中山5R再停止後の補正前確認・限定補正設計

- Status: Proposed
- Owner: Main
- Updated: 2026-09-26
- Governing record: [馬主取得・出走割当](../README.md)
- Scope: 利用者の「お願いします」は補正案・影響確認への着手承認。本番データ変更・再開の承認ではない。本書の実装も明示承認後とする。

## 現状と今回の結論

2026-09-26 13:49:15 JSTからのidentity不一致による自動停止を維持。今回行った本番操作はGETのみ。第一段階の配備は完了しているが、親のIAC5/IAC6/IAC10とRT3は未完了である。

14:02 JSTの確認では、中山5Rは公式Card/Resultの14頭と保存済みHorseIdが全頭1対1で一致し、12頭の馬番が異なる。公式Cardの馬主は14/14、raw保存値は0/14。馬名の一致だけではなく、公式horseリンクから既存`DeterministicIdGenerator.BuildHorseId`で生成したIDを照合した。

予想・結果・払戻・保存オッズのAPI上の件数は0。ただし現在の公開GETではevent/snapshot、すべての派生履歴、修復候補、参照の完全性を同一versionで検証できない。**補正候補であり、補正可能と確定したわけではない。** この証拠不足を埋めるread-only previewと、適用時の再検証を先に実装する案とする。

## 本番観測（2026-09-26）

| 対象 | raw entry / owner欠損 | entryResults | payoutResult | 予想検索totalCount | admin odds snapshots |
| --- | --- | --- | --- | --- | --- |
| 中山5R `race-a8ad225c-0df0-576d-bd1a-24d9e15dce1e` | 14 / 14 | 0 | null | 0 | 0 |
| 阪神11R `race-fca5d100-9e2f-5074-a74c-bad8cdb4705f` | 16 / 16 | 0 | null | 0 | 0 |

- 中山5R観測14:02:05、阪神11R観測14:02:08 JST。GET `/api/races/{id}/context`、`/api/races/{id}`、`/api/predictions?raceId={id}&pageSize=1`、`/api/admin/races/{id}/odds-snapshots`。中山の`comparison`もtickets/results空、payout null。
- 通常race GETの`odds.isAvailable=false`は未保存の証拠ではない。実装がunavailableを返すため、独立したadmin odds GETを使った。context DTOもOddsSnapshotsを公開しない。
- 前checkpointの非存在property `results`から算出した件数は使わず、実在する`entryResults`を確認した。
- 中山5Rのraw gateNumberも14/14 null。一方表示用GETは枠や馬主を補完するため、補正成功の検証には使用しない。
- 初回は9/24木曜23:06にCardを保存し、馬主0/14。保存順は五十音順。元DOMが保存されていないため「当時の未確定表示の並びを採番した」は有力仮説であり、当時入力まで再現済みとはしない。
- 他Raceも同じ初回batchで取得されているが、未照合の全件を誤馬番とみなさない。対象拡大はまずread-only候補一覧に限定する。

## 中山5Rの対応表

[公式Card](https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604080520260926/AC)と[公式Result](https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde0106202604080520260926/68)で馬番・Horseリンクを突合。下表のsourceは公式リンクのCNAMEであり、推測URLではない。

| 保存馬番 | 公式馬番 | 馬名 | 公式source CNAME |
| --- | --- | --- | --- |
| 1 | 3 | ヴァイスリッター | pw01dud002024102432/A6 |
| 2 | 2 | エバーラスティング | pw01dud002024103598/D8 |
| 3 | 13 | サヴィッジブロウ | pw01dud002024100657/89 |
| 4 | 14 | シダモフレイバー | pw01dud002024106188/D1 |
| 5 | 12 | ジャロロッソ | pw01dud002024106725/3E |
| 6 | 7 | スペーストラベラー | pw01dud002024103415/CE |
| 7 | 11 | ゾネンバーデン | pw01dud002024103640/21 |
| 8 | 5 | ニシノカチオトコ | pw01dud002024103853/EC |
| 9 | 9 | ハナミヤビ | pw01dud002024103649/2C |
| 10 | 4 | フェイマスジャズ | pw01dud002024102965/24 |
| 11 | 6 | ブンブクチャガマ | pw01dud002024101095/AC |
| 12 | 8 | ブーケフォーミー | pw01dud002024101990/26 |
| 13 | 10 | ペプチドデバイス | pw01dud002024101727/79 |
| 14 | 1 | レックスダミアーニ | pw01dud002024106860/70 |

全14頭の公式sourceからのIDは現在保存済みIDと一致。中山だけの結果で阪神その他の安全性を代用しない。馬主はCardだけを正本とし、Result/profileからの補完は行わない。

## 補正方法（提案）

既存の[非停止・revision再取得案](20260926-revision-recollection.md)を以下で具体化する。新しい全体停止、直接SQL、旧event書換え、既存予想・結果等の自動付替えは採用しない。

1. **予防修正**: 公式の馬番未公表を正常な確定待ちにする。行位置による仮採番、推測枠、Card完了扱いをしない。曜日固定でなくページ状態で判定し、確定後に再取得。予想生成は確定済み出走割当に限定する。通常/refreshの同一馬への後着ownerと格付け保存、artifact要求版との比較も修正する。
2. **read-only preview**: 対象Race・旧割当・全頭公式identity・新割当・owner/grade差分・出典/取得日時・aggregate/projection版・全参照分類・拒否理由を返す。全参照を列挙できない、旧状態と食い違う、同名だけの照合、頭数やHorse集合の不一致、未知event/参照があれば`Blocked`。新しいpreviewは補正・再要求・通知解決を行わない。
3. **保護された補正経路**: 対象Raceだけの共通書込排他とexpected versionを検証し、preview後に追加された参照も再走査する。通常bulkの不一致拒否を緩めず、明示対象・承認済みpreview fingerprint・操作IDに限定した補正commandへ分離する。全関連writer、予想生成中の入力snapshot、別process、旧leaseも対象にし、read/check/write間の割込みを防ぐ。実装の排他試験が成立しなければapply不可。
4. **一括eventと派生再構成**: 全頭の旧新対応を持つ監査可能な単一補正eventでRace割当を変更する。番号順の`EntryRegistered`繰返しで入れ替えない。旧占有馬の属性を継承せず、同じHorseの情報と公式Cardだけを採用。entry登録だけから生成されたindex/出走予定履歴は、当該Raceの旧集合を除いて全頭を再構成する。成績、予想、オッズ、払戻、手動記録など独立した意味を持つ参照は移行せず拒否する。旧eventは保存し、replayで同一状態に到達させる。
5. **部分失敗・再送**: 操作IDで二重eventを防ぐ。commit前失敗はデータ変更なし。commit後projection失敗は対象Raceを整合性確認待ちとし、該当Raceの書込みと予想利用を拒否、再投影してraw一致まで解除しない。process終了後もこのgateを保持する。旧版binaryへの単純rollbackで未知eventを無視しない。backupは取得・復元試験するが本番restoreは別途承認。
6. **限定運用**: 実装・検証・配備後、まず本番previewを提示して適用承認を得る。中山5R、阪神11Rを個別に判定し、条件を満たす承認対象だけ補正。次の未使用取得revision（現在3使用済み、候補4）を全入口で揃え、旧Current Cardも再取得する。元の中山10R失敗、中山5R、阪神11Rの必要な要求を重複なく追跡し、停止原因の解消を確認して再開する。全件再投入、failure消去、安全停止無効化はしない。

## 参照検査と証拠の範囲

`EntryId = raceId-entry-{HorseNumber:D2}`なので番号の入替はEntryIdの指す馬を変える。単なる数値欄変更ではない。

| 分類 | 永続化・影響 | preview/applyの処置 |
| --- | --- | --- |
| Race割当/index | Race state/events/snapshots、RacePredictionContexts.Entries、RaceResults/PredictionComparisons.EntryIndexes | 最新event版とprojection版を一致確認。旧新集合を一括更新/再構成 |
| 独立参照 | PredictionTicketsのMarks/Evaluations・betting selection、結果event/EntryResults、払戻Combination、OddsSnapshotsのHorseNumber/Market/Selection | 1件でもあれば自動補正不可。削除や意味の付替えを行わない |
| 派生履歴 | HorseRaceHistories、JockeyRaceHistories、HorseWeightHistories | 純粋なentry登録由来のみ再構成可能。成績・別観測や説明できない差分は拒否。旧新Horse/Jockey全体を検査 |
| 別修復・ユーザー参照 | HorseIdentityRepairCandidates（RaceId/EntryId）、memo等の汎用subject/linkや自由記述参照 | 関係がある、または安全に分類不能なら拒否。文字列置換をしない |
| Race共通観測 | weather/track condition | 馬番依存ではないが変更せず保持・比較する |

event storeとすべての関連projectionの双方を検査する。削除済みticketや未反映eventも「GETに見えないから存在しない」と扱わない。型・schemaのallowlistにない関連状態は拒否し、previewへ理由を返す。全アプリの無関係な自由記述の意味まで自動証明はできないため、対象Race/旧Entry参照のある記録を保守的に止める。参照検査範囲をAPI応答に明示する。

棚卸し根拠: `EventStoreDbContext.cs`の全read-model登録、`RaceOddsSnapshotRecorded.cs`、`PayoutResultDeclared.cs`、`PredictionTicketReadModel.cs`、`HorseIdentityRepairReadModels.cs`。既存のhorse/jockey/weight locatorは旧新subjectへ配送し、EntryRegisteredで該当EntryIdだけを削除/追加するため、順次入替自体が必ず行消失を起こすわけではない。ただしhorse/jockey履歴は同EntryIdの旧成績値を保持する。結果がある状態での付替えを拒否する根拠となる。単一補正eventの選択はその事実を踏まえた全頭検証・監査・部分失敗対策であり、既存の行消失を実証したとの主張ではない。MemoSubjectTypeはHorse/Trainer/Jockey/Raceのみだが、Content/Linksは自由記述。対象Raceと影響Horseのmemo、および明示的な旧ID参照を検査し、関連する曖昧記録は拒否する。

## Concern and agreement ledger

| ID | Evidence / impact | Recommended disposition / alternative / residual risk | AC/task | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- |
| C201 | RP-C1: 公開GETにevent版・全履歴がない。0件だけで誤補正し得る | authoritative previewとapply時再走査。不明は拒否。API件数のみの即時補正は不採用。残riskは新未知参照を拒否して顕在化 | RP2/RP-T2 | Agree | 提示中 | Resolved in design |
| C202 | RP-C2: EntryIdが番号由来。入替で派生履歴や独立参照が別馬を指す | 単一event＋純粋な派生情報だけ再構成、独立参照は拒否。全参照自動移行は不採用。実データで非zeroが出れば別設計 | RP2–3/RP-T2–3 | Agree | 提示中、apply未承認 | Resolved in design |
| C203 | RP-C3: pipeline pauseはAPI writerを止めず、preview直後にも参照が増える | Race単位共通排他・version・永続的な未整合gate・複数process試験。排他不能構成は補正拒否。未検証のfile lockを安全保証としない | RP3/RP-T3 | Agree | 新規全体停止は不要という判断を維持 | Resolved in design |
| C204 | RP-C4: Cardの公開期間を過ぎるとownerを公式から再取得できない | 取得可能対象だけ。Result/profile代用禁止。期間切れは未補正を明示し完了扱いにしない。公開終了前の成功は保証しない | RP1/RP4/RP-T1/RP-T4 | Agree | 提示中 | Resolved in design |
| C205 | RP-C5: 配備成功後も既存不整合で再停止した | 対象の終端成功＋raw整合＋他収集の進行＋10分以上再停止なしで復旧判定。無条件resume/guard解除は不採用。新たな不明障害時は停止維持 | RP4/RP-T4 | Agree | 提示中、再開未承認 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC201 (RP1) | 未確定Cardは仮entry/予想を作らず正常待機。確定後は正しい番号・owner・gradeを保存し、新revisionで旧Card再取得、再送で重複なし | RP-T1, RP-T4 | 木曜型HTML→確定Card→保存の統合試験、並び/頭数変更・再起動・既存値保持反例 | Not started |
| AC202 (RP2) | previewが全頭source照合・旧新差分・参照分類・version・拒否理由を示し、無変更。未知/参照ありを補正可能と誤判定しない | RP-T2 | 実14頭反例、oddsのみ/未反映event/削除ticket/memo/修復候補/集合違い/別Race、DB前後同一 | Not started |
| AC203 (RP3) | 参照なしと承認された対象だけをRace単位排他＋単一補正eventで更新。履歴の誤帰属なし、競合/旧lease/再送/中断/replayに耐え、不整合状態を利用しない | RP-T3 | 複数process/共有volume、projection障害・再起動、馬番循環入替、同一騎手、独立参照割込、旧版互換とbackup復元試験 | Not started |
| AC204 (RP4) | 別途承認後の限定補正・次revision再取得・再開を経て、中山5Rと阪神11Rのraw番号/owner/grade/関連履歴整合、原障害対象の成功、他収集進行、10分以上再停止なし | RP-T4 | 本番preview承認記録、対象要求/終端、raw検証、観測時刻。owner14/14・16/16、説明できない参照差分0 | Not started |

RP1は親IAC3/IAC6、RP2–3はIAC2/IAC4、RP4はIAC5/IAC9/IAC10へ接続する。親の未完了範囲を調査文書の完成で除外しない。

## Task plan / assignment

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| RP-T1 | 未確定/後着保存/revision再取得 | Main（設計と保存契約）、契約固定後のfixtureはbounded coding worker | Lead＋cost-sensitive fixture worker | 設計承認 | Main: parser/collector/contractsと対応test、worker: 指定新fixture/testのみ | workerが最小/関連回帰、Mainが統合・実HTML反証 | 未着手 | Proposed |
| RP-T2 | authoritative read-only preview/参照分類 | Main、read-only explorerが棚卸し支援 | Lead（永続化/安全判定）、balanced explorer | 設計承認 | Main: application/API previewと対応test。explorer: read-only | event/projection完全性・無変更・未知参照拒否 | 現在API証拠と棚卸しのみ、実装未着手 | Proposed |
| RP-T3 | Race排他/単一event/派生更新/未整合gate | Main | Lead | RP-T2 contract | domain/application/infrastructure/API writerと対応test、共有変更は直列 | cross-process、失敗再開、全writer、replay、snapshot、独立反例 | 未着手 | Proposed |
| RP-T4 | 統合review・配備・preview提示・承認後補正/再開 | Main、read-only independent reviewer | Lead＋review tier | RP-T1–3、適用は別途承認 | Mainのみproduction指定対象とdocs。reviewer書込なし | 全CI、対象本番終端/raw/10分観測 | 未着手。親RT3のblockerを引継ぐ | Proposed |

Lead保持理由はRP-T1の外部公開契約/保存結合、RP-T2のデータ完全性、RP-T3の永続化/並行性/共有write、RP-T4の本番権限/最終受入。凍結後のHTML fixtureだけを低コストworker候補に分離し、exact file ownershipをdispatch前に固定。判断変更・未知永続化・範囲拡大はLeadへ戻す。全test結果はMainが独立に受入れ、worker自己申告だけで完了にしない。

## Current investigation / review gates

- Main: 本番GETと公式HTML照合、統合設計、文書の唯一write owner。コード・本番mutationなし。
- Read-only explorer `entry_reference_inventory`: 既存agent再利用、requested `gpt-6-sol`、observed model/token/active effort unavailable。CodeGraph＋永続化/投影sourceの棚卸し。test変更/実行なし（設計調査であり実装成功の主張なし）。Mainが公式ID照合、admin odds GET、投影sourceで独立反証。初回再試行0、focus追加質問1、write0。coding audit JSONの新規作成対象ではない。
- Design/task-split review: Main。データ損失・未知参照・同時書込みをfail-closedに限定し、実装と本番apply承認を分離。既存排他が十分という仮定はしない。
- Concern review: RP-C1–5の処置を設計に反映。previewによる実データ適用可否は後段gateであり、現在安全と断言しない。
- Pre-implementation: 未実施、承認待ち。
- Checkpoint: 調査成果は14頭source一致/12頭番号差、2RaceのAPI参照0とその限界。コード完成/復旧ではない。
- Final implementation review: 未実施。次は本設計の承認、実装・関連回帰・配備、authoritative本番preview、具体差分に対する別途apply承認の順。未承認apply/resumeは行わない。

## Documentation updates / verification

- 親READMEへ現時点の追加提案と未完了状態を反映。旧revision案から本書へ具体化リンク。
- `docs/27-jra-site-collection-contract.md`の未承認提案リンクを本書へ接続し、配備済みidentity拒否と未実装の限定補正を区別する。
- 本turnはdocsのみ。build/test/CodeGraph syncは対象外。既存アプリが新設計を満たす証拠として以前のテスト結果を流用しない。
- `python .codex/skills/document-driven-development/scripts/validate_change_records.py docs/changes/20260919_race-entry-owner-enrichment/README.md`: issues=0。
- `python scripts/audit_agent_execution.py docs/changes/20260919_race-entry-owner-enrichment`: valid。`git diff --check`: passed。
- Main最終調査review: official source ID照合、admin odds、投影履歴の参照保持という独立証拠を照合。設計調査の成果を受入れ、実装RP1–4と親復旧は未完了のまま保持。実装承認待ちが次のgateであり、文書commitは復旧完了を意味しない。
- 補足文書単体のvalidatorは独自RP IDを認識せず2件の診断を返した。基準の意味を変えずAC201–204/C201–205の機械可読IDを併記し、親・本書の再検証はissues=0、audit valid。親validator成功だけで単体成功とは扱わない。
