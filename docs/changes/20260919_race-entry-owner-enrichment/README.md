# Race出走馬の後着データ補完

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-26
- JRA site contract impact: Updated — 後続の`20260919_jra-site-collection-contract`でCard限定の取得元制約を正本化した。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Incomplete | 承認済み第一段階の専用天候/馬場欄parserと通常/refresh/aggregateのidentity拒否を実装。revision3全入口を統合検証中。馬主補正・確定待ちの全体は後段に残る。 |
| Verification | Incomplete | 原障害の公式URL、実HTML境界反例、API/domain拒否の副作用不変を検証済み。最新origin全体検証と本番照合は継続中。既存馬番不一致そのものは未修復。 |
| Deployment/operation | Unverified | 第一段階の修正版配備・限定再取得・再開は承認済み。短時間API停止も明示許容。配備・復旧観測は未実施。 |

> **2026-09-26 reopened:** [対象Raceの証拠・安全な補正設計](decisions/20260926-race-integrity.md)を本recordの追加提案とする。`race-fca5d100-9e2f-5074-a74c-bad8cdb4705f` のraw owner欠損16/16、公式馬番との不一致、G2/GIII不一致が判明した。従前の承認は、この同一性補正や隔離操作を承認したものではない。監視親のT2b/T3a・保存完全性findingへ接続し、親recordは変更しない。以下の9/19のVerifiedは当時の限定試験結果であり、追加基準IAC1–IAC5の完了を意味しない。

## 2026-09-26 変更提案（今回のレビュー対象）

> **承認記録:** 利用者の「お願いします。」により、停止復旧の追加範囲IAC7–IAC10、RC1–RC4、再開前のIAC3 identity gate、実装・配備・限定再取得・再開が承認された。既存馬主補正IAC1–IAC6の残りは別段階のまま、既存予想・結果の付替えを承認に含めない。以下の未承認表記は承認前の履歴。

> **停止調査を受けた追加設計:** [馬場状態の誤認修正・収集復旧](decisions/20260926-track-condition-recovery.md)をスコープへ追加した。会社名「青芝商事(株)」を芝の馬場状態と誤認する欠陥を修正し、新版配備・対象限定再取得・既存自動停止からの再開・本番観測までをIAC7–IAC10で扱う。新たなメンテナンス停止、全件再試行、安全装置の無効化は含まない。再開前に既知の誤馬番への副作用防止を先行する。今回は設計のみで実装・本番操作は未実施。

> **最新の利用者判断:** 全体停止は採用せず、[取得revision更新による再取得](decisions/20260926-revision-recollection.md)へ方針変更する。以下のoffline補正案・停止容認待ちは履歴であり、実行対象ではない。revision更新だけではCurrent Cardが再取得されないことと、誤馬番の保存境界が未解決のため、StatusはProposedを維持する。停止の承認を再度求めない。

> **馬番未確定への対応:** 利用者より木曜日時点では馬番が未確定との指摘を受け、[一括設計の公開段階](decisions/20260926-full-repair-plan.md)とIAC6を追加した。未確定は障害ではなく正常な確定待ち。仮entryを保存せず、Cardを完了扱いにせず、確定後の最新Cardを発走前に取得する。曜日で決め打ちせず公式の公開状態を判定する。今回も文書のみの更新であり、停止・補正の承認は未取得。

> **対応方針の具体化:** 利用者は「本番補正まで含めた一括の設計を先に確認する」を選択した。[一括設計案](decisions/20260926-full-repair-plan.md)に、取得修正・保存検証・停止中の限定補正・backup/復旧・配備後検証まで記載した。IC2–IC4は同書の処置で設計上具体化し、IC1の全体メンテナンス容認と時間帯は未決。実装・本番操作はまだ開始しない。

利用者の「まずは変更記録を作成」の依頼に基づく設計文書であり、実装承認ではない。既存recordを継続し、重複する変更記録は作成しない。今回の基準・作業・懸念の正本は[追加設計のIAC1–IAC5、IT1–IT5、IC1–IC4](decisions/20260926-race-integrity.md)。下記より後の9/19の承認・完了判定は履歴として保持し、今回への承認として流用しない。

### 背景と目的

対象は2026年9月26日阪神11R（シリウスステークス）。調査時点では出走データの馬主が0/16件であり、画面の15件は馬プロフィールによる表示補完だった。公式出馬表では16件すべて取得可能。加えて全16頭の馬番とレース格付けが公式と不一致だった。公式馬識別子からのIDは保存済み16頭すべてと一致するため、馬番と馬の対応を正してから補正する必要がある。

目的は、公式出馬表の馬主を正しい出走馬へ保存し、再取得・再送でも欠落や別馬への誤帰属を生じさせないこと。初回の欠落原因は未確定であり、後着値を保存しない通常取得経路の欠陥とは区別する。

### 提案範囲と承認境界

1. 当時の入力・実行版を調査し、取得不能な証拠はその限界を明示する。
2. 公式馬番・馬識別子・レース識別情報を、副作用が発生する前に検証する。馬番未公表の一覧を仮の連番で保存しない。
3. 通常取得と明示的な再取得の双方で、同じ馬への後着値を非破壊・冪等に保存する。保存後の出走データを直接確認し、画面の補完表示を成功根拠にしない。
4. 対象レースに限定した補正前確認を設計する。旧新の馬番対応、馬主、格付け、予想・結果・履歴への影響を提示する。
5. 隔離・補正方法の確定と明示承認後にのみ配備・本番補正を行い、再取得後の整合性まで確認する。

今回は文書作成のみ。コード変更、配備、収集停止・取消、再収集要求、データ補正は行わない。他レースの一括補正、新しい取得元、Horse profileからの馬主コピー、画面刷新は対象外。予想・結果等の既存参照をどう移行するかは未決であり、既存entryの単純上書きを採用しない。

### 受け入れ条件と未決事項の要約

- IAC1: 初回原因の事実・仮説・証拠不足を分離し、再現可能範囲を確定する。
- IAC2: 再要求、実行中処理、適用経路を含め、不整合書込みを防ぐ隔離方法を検証する。
- IAC3: 不完全・不一致の入力を副作用前に拒否し、同一馬の馬主補完と再送時の冪等性を実経路で検証する。
- IAC4: 公式識別子に基づく補正前確認で旧新対応と参照先を示し、氏名だけの照合や未解決識別子を拒否する。
- IAC5: 別途承認された本番操作後、馬主16/16、公式馬番・格付けとの一致、関連履歴等の誤帰属がないことを確認する。
- IAC6: 木曜日等の馬番未確定を正常待機とし、確定後の再取得・並び順/頭数変更・遅着入力・再起動を通じて、仮採番なしで発走前の確定Card保存に到達する。
- IAC7–IAC8: 馬場状態を専用欄だけから取得し、会社名・馬名を誤認しない。本当の未知値や構造異常は保存前に拒否し、安全停止を維持する。
- IAC9–IAC10: 修正版配備・旧実行境界・identity gateを確認後、次revisionで失敗対象のみ再取得し、既存停止から再開する。本番の正しい保存・対象成功・他収集の進行・10分以上の再停止なしを確認する。

## Concern and agreement ledger

| ID | Concern and evidence | Proposed disposition | AC/task | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | IC1: 誤馬番のまま結果を取得すると誤帰属し得る。取消・pause単独では書込みを遮断しない | 最新revision案のRace単位排他・副作用前identity gate。参照があれば入替拒否。再開前にidentity gateを先行 | IAC2/IT2、IAC9/RT2 | 未検証の排他での補正に反対、検証必須の設計に同意 | 全体停止は不採用。新保存境界は承認待ち | Resolved in design |
| C2 | IC2: 初回入力と本番実行版が未取得 | 当時証拠を調査し、取得不能なら再現限界と代替fixtureを明記。実証済み欠陥と初回原因を分離する | IAC1/IT1 | 現在DOMだけによる原因断定に反対、処置に同意 | 今回承認は停止復旧のみ。馬主初回原因の判断は別段階 | Resolved in design |
| C3 | IC3: 馬番の補正でEntryIdを参照する予想・結果・履歴に影響 | 最新revision案のRace単位排他・参照gate・監査eventを採用。予想/結果/オッズ等があれば書込前拒否。停止中復元は不採用 | IAC4/IT4 | 自動付替えに反対、参照ゼロの場合だけの補正に同意 | 今回は付替え拒否を承認。オンライン入替は別段階 | Resolved in design |
| C4 | IC4: 表示補完と限定試験では保存欠損を見逃す | raw保存値、通常/refresh経路、再送event数を検証。表示確認だけを代替にしない | IAC3/IT3 | 検証方針に同意 | 停止復旧の保存安全性検証を承認 | Resolved in design |

調査時点の詳細は[追加設計](decisions/20260926-race-integrity.md)を履歴として保持する。最新処置は[revision再取得設計](decisions/20260926-revision-recollection.md)と[停止復旧設計のRC1–RC4](decisions/20260926-track-condition-recovery.md)。旧offline案と全体停止の承認待ちは撤回済み。設計上の処置と実装検証は区別し、今回追加した復旧範囲を含めて確認を求めるため`Proposed`を維持する。

### Documentation updates（今回）

- 本README: 今回の提案範囲、受け入れ条件、承認境界を先頭で読める形に整理した。
- [馬場状態・停止復旧設計](decisions/20260926-track-condition-recovery.md): 障害証拠、専用欄抽出、安全停止維持、段階配備、限定再取得・再開、本番観測、IAC7–IAC10/RT1–RT3/RC1–RC4を追加した。
- [詳細設計・証拠](decisions/20260926-race-integrity.md): 調査結果、懸念台帳、AC/task対応、検証方法と次の操作を保持する。
- [一括設計案](decisions/20260926-full-repair-plan.md): 今回の依頼に基づく本番補正までの具体案。懸念の最新処置、実行停止条件、タスク分担を定義する。
- `docs/27-jra-site-collection-contract.md`: 既存のCard限定・プロフィール代用禁止は維持し、新しい馬番・identity・格付け検証は未承認提案として一括設計へのリンクを追加。現行実装が適合済みとは記載しない。

### 文書作成チェックポイント

> 最新checkpoint: Mainが停止対応設計を統合。今回も文書のみ。旧「IC1停止承認待ち」は履歴であり次操作ではない。次は追加AC/懸念台帳と既存の非停止方式を提示して承認を得る。承認後、RT1–RT2の検証、配備版確認、RT3の運用へ進む。既存の無関係な変更は保持し、本README、decisions配下、サイト契約の提案リンクだけを未コミットで残す。

- 停止復旧の設計検証: `python .codex/skills/document-driven-development/scripts/validate_change_records.py docs/changes/20260919_race-entry-owner-enrichment/README.md` はissues=0、`git diff --check`成功。文書のみのためbuild/test・CodeGraph syncは対象外。IAC7–IAC10はNot startedのまま、復旧済みとは報告しない。

- Mainが担当。短い単独文書整理であり、分割・委譲による効果がないためsubagentなし。実装タスクは追加設計のIT1–IT5を参照し、未確定判断をworkerへ渡さない。
- Design review: AC/task/検証の対応は追加設計に記載済み。IC1–IC3未解決のため承認gateは未通過。
- Pre-implementation / implementation final review: 未実施。今回の依頼では実装へ進まない。
- Next action: IC1の隔離方式とIC3の参照移行方式を具体化し、当時証拠の取得可否を確定してから設計承認を求める。
- 文書検証: change-record validatorはschema 2の懸念台帳見出しを要求したため正本へのリンクを明示し、再検証でissues=0。`git diff --check`成功。コード変更がないためbuild/testとCodeGraph同期は不要。

## 2026-09-19 設計・実装履歴

## Task plan

承認済み停止復旧の実行台帳。IAC7–10の基準・詳細は停止復旧設計を正本とし、既存IT1–IT5の馬主補正は別段階として保持する。
監査validatorはAC数字のみを受理するため、監査ID AC108=IAC8、AC109=IAC9と対応させる（基準の追加・変更ではない）。

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| RT1 | IAC7–8 専用欄取得 | Main | Lead | approval | src/HorseRacingPrediction.Scraping; tests/HorseRacingPrediction.Scraping.Tests | HTML/snapshot/parser regression | 実行記録を追加設計へ追記 | In progress | Lead — public contract | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| RT2 | IAC8–9 保存前identity/revision | Main | Lead | approval | src/HorseRacingPrediction.Api; src/HorseRacingPrediction.Domain; tests/HorseRacingPrediction.Api.Tests/RaceEndpointsTests.cs; tests/HorseRacingPrediction.Domain.Tests/RaceAggregateBulkCollectionTests.cs | API/domain/collector regression | 実行記録を追加設計へ追記 | In progress | Lead — persistence/integration | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| RT2-tests | IAC8–9 identity反例 | identity_guard_tests | Worker | RT2 contract | tests/HorseRacingPrediction.Api.Tests/CollectedRaceIdentityGuardTests.cs | 5 tests passed, related 33 passed; Main source/domain反証review | agent-audits/RT2-tests-A1.json | Verified | Worker — frozen test contract | RT2-tests-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| RT3 | IAC9–10 配備/復旧 | Main | Lead | RT1, RT2, RT2-tests | production approved targets | 限定再要求・終端成功・10分観測 | 未実施 | Dependent | Lead — security/final acceptance | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |

## 2026-09-19 設計・実装履歴（本文）

> **2026-09-19 design correction:** ownerはRaceCardを取得できた場合だけ取得でき、過去RaceResultや
> 現在のHorse profileからは復元できない。全owner欠損Raceへ補正要求を作る現行migrationは、
> Card取得期間外にも実行不能な要求を作るため、push/deploy/applyしてはならない。
> [JRAサイト収集契約とCard限定補正](../20260919_jra-site-collection-contract/README.md)の承認・実装後に再度Approvedとする。

> **Resolution:** 後続変更を実装し、preview/applyはCard探索期間内だけを要求対象とし、期間外は
> `補正不能`、期間内で公式Cardがない場合はretryしない`公式Cardなし`として扱うよう修正した。
> 本recordはproduction migration post-checkを残すため`Approved`へ戻す。

## 調査結果

対象Race `race-429efd4b-9d45-5464-99fb-ffcbe19d8e55`（2026-09-19 阪神11R）は、
画面上で16頭中15頭の馬主が欠落している。一方、`Race / 20260919:Hanshin:11 / race-detail` は
2026-09-19 15:50:41 JSTに成功し、未解決failureは0件である。

`ApplyBulkRaceResult` は同じ`EntryId`が既に存在すると、そのentryを無条件に`continue`する。
結果ページ等からownerなしでentryが先に登録された場合、後から出馬表が`OwnerName`を取得しても保存されない。
Race APIはentryのownerがnullの場合にHorse profileのownerを表示補完するため、プロフィール取得済みの1頭だけが
表示され、残る15頭が`—`になる。したがってparserの全面失敗ではなく、後着データの永続化漏れである。

成功判定はbulk commandの受付とRace結果保存を基準にしており、全entryのowner充足を成功条件に含めない。
このため不完全なRaceでもcollection taskは成功し得る。

## Incident ledger

```text
Incident: 2026-09-19 阪神11Rで16頭中15頭の馬主が欠落
Temporary recovery: なし（画面・GET・コードのread-only調査のみ）
External/input condition: CardとResultが別時刻に公開され、Result由来entryが先に存在し得る
Technical defect: ApplyBulkRaceResultが既存EntryIdを更新せず、後着のOwnerNameを破棄する
Workflow gap: 成功判定にentry enrichmentの保存結果・欠損観測がない
Corrective proposal: 既存entryへの非破壊・冪等マージと、明示的なrepair preview/apply
Permanent fix: Not started
Remaining risk: 2026-09-19の他Raceおよび過去Raceに同型欠損があり得る
```

## 方針

1. `ApplyBulkRaceResult`は既存entryを読み飛ばさず、後着した収集値をマージする。
2. null/空値で既存の非null値を消さない。異なる非null値は最新の検証済みpayloadを採用する。
3. 実効値が変わらない再送はeventを追加しない。
4. 馬主だけを特例にせず、既存のentry update contractで安全に扱える収集項目を同じ規則にする。
5. collection成功とデータ完全性を分離し、owner欠損数をstage outcome/運用表示で観測できるようにする。
6. 既存Raceは自動再投入しない。日付・Raceを指定したread-only previewと、確認後の明示applyを用意する。
7. repairは新しいRace taskを増殖させず、既存Race resourceへ冪等に再収集・マージする。

## 追加対策案（再承認対象）

### 要求revisionを必ず実行する

- 収集要求の正本は個々のtaskではなく、resource/definitionの`RequiredRevision`と未充足requestとする。
- 既存の低いrevisionのtaskへ要求が統合された場合、そのtaskが成功すれば現行どおり高いrevisionの後続taskを作る。
- 既存taskが失敗・中断・dead-letterになった場合も、未充足の高いrevision requestがあれば、terminal化と同じtransactionで後続taskを1件だけ作る。
- 同じresource/definitionにactive taskは常に最大1件とし、同じbatchの再実行でrequest/taskを重複生成しない。
- 後続taskは元taskのpriorityを自動昇格せず、未充足requestに記録されたlane/priorityを用いる。既存failure notificationの解決は後続taskが実際に成功した時だけ行う。

### データ補正をrace-detail migrationへ含める

- 馬主欠損補正を独立した手動repairで終わらせず、既存の`race-detail` migrationのpreview/apply/progressへ統合する。
- previewは構造移行件数に加えて、保存済みRaceごとのentry数、entry-level owner欠損数、正規化後のRace resource ID、実行可否、既存要求・実行状態をread-onlyで表示する。
- applyはpipelineのpauseとactive taskのdrainを前提に、構造移行後、owner欠損Raceへ`race-detail` revision 2の補正要求を作る。値をHorse profileからコピーしたり推測で直接上書きせず、公式Cardを再取得してaggregateの非破壊mergeを通す。
- migration batch IDは版と対象を基に決定的に生成し、途中失敗・API再起動・apply再実行でも同じrequestを再利用する。大量対象は固定サイズでcheckpointし、完了済み対象を飛ばして再開する。
- apply直後は「補正要求済み」であり「補正完了」とは扱わない。progressは`補正済み`、`処理待ち/実行中`、`取得失敗`、`公式情報なし`を分け、欠損が0件になったRaceだけを完了とする。
- 設定画面からmigrationのpreview、明示apply、進捗・失敗対象確認を行えるようにする。deploy workflowはpreviewのerrorが0件であることを確認してapplyし、resume後は非同期補正の進捗を確認可能にする。

### 旧repair画面の扱い

- migration適用前の個別診断用としてpreview APIは残す。
- apply操作はmigrationへ一本化し、同じ補正要求を作る入口を複数残さない。既存の個別apply API/UIはmigrationへの互換呼び出しにするか、production callerが0件であることを確認して廃止する。
- 「Race画面でHorse profileから表示補完されている件数」と「entry-level owner欠損数」は異なるため、migration画面では後者であることを明記する。

## 先行変更から併せて是正する点

- 出馬表のdomain writeが失敗しても、parse済みの公式発走時刻evidenceは失わない。
- Resource Locationへ`Card` / `Result` / `Unknown`のartifact種別を保存し、異なるartifactの候補を
  成功候補として使わない。legacy locationは自動推定せず`Unknown`のまま、page identity確認後だけ昇格する。
- 上記は既存error taskの自動復旧、priority変更、notification解決を行わない。

## Acceptance criteria

| ID | Observable criterion | Verification | State |
| --- | --- | --- | --- |
| AC1 | ownerなしで先に登録済みのentryへ、後着CardのOwnerNameが保存される。 | domain integration test | Verified |
| AC2 | null/空の後着値は既存ownerを消さず、同じpayloadの再送はaggregate event/versionを増やさない。 | replay/non-destructive tests | Verified |
| AC3 | owner以外の対象収集項目も同じmerge規則で補完され、HorseId/EntryIdの同一性は変更しない。 | field matrix tests | Verified |
| AC4 | Card parserが返したowner件数と保存後owner件数を検証し、欠損を成功taskの内部に隠さず構造化outcomeへ残す。 | collector/API integration test | Verified |
| AC5 | 対象Raceの再収集後、16頭すべての馬主がRace API/画面で表示される。 | production read-only verification | Connected |
| AC6 | 2026-09-19の対象範囲をpreviewすると、Race別のentry総数・owner欠損数・repair可否を表示し、データを変更しない。 | before/after invariant test | Verified |
| AC7 | repair applyは明示選択したRaceだけを処理し、task/request/priority/notificationを自動変更せず、再実行も冪等である。 | repair integration test | Verified |
| AC8 | Card write failure時もparse済み公式StartTime evidenceをcompletionへ返す。 | handler failure test | Verified |
| AC9 | Locationはartifact種別を持ち、Card URLをResult、Result URLをCardの成功候補にしない。legacyはUnknownからpage identity確認後のみ昇格する。 | migration/store/navigation tests | Verified |
| AC10 | 既存error jobの自動復旧・自動再投入・priority変更・notification解決を行わない。 | DB invariant test | Verified |
| AC11 | 低revisionのactive taskへ高revision要求が統合された後、元taskが成功・失敗・中断・dead-letterのどれで終わっても、未充足要求から高revisionの後続taskがちょうど1件作られる。 | store integration/concurrency tests | Verified |
| AC12 | race-detail migration previewで、構造移行対象とowner欠損Race、entry総数、欠損数、実行可否、既存処理状態をデータ変更なしで確認できる。 | API integration/before-after invariant tests | Verified |
| AC13 | migration applyはpause・drain済みの場合だけ実行でき、owner欠損Raceへrevision 2の補正要求を冪等に登録する。Horse profile値の直接コピーや既存priorityの変更は行わない。 | migration/store integration tests | Verified |
| AC14 | migrationが途中失敗またはAPI再起動されても、同じmigrationを再実行すると完了済み対象を飛ばして継続し、request/taskを重複生成しない。 | restart/idempotency/partial-failure tests | Verified |
| AC15 | 設定画面でmigrationのpreview、明示apply、補正済み・処理中・失敗・公式情報なしの進捗を確認でき、Race APIのentry-level owner欠損が0件のRaceだけが補正完了になる。 | component/API/E2E and production post-check | Connected |

## Task split

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | entry merge contract | Main | Lead | Approval | Domain + domain tests | AC1-AC3 | targeted tests | Verified |
| T2 | completeness outcome | Main | Lead | T1 | Collector/API + tests | AC4 | targeted tests | Verified |
| T3 | repair preview/apply | Main | Lead | T1 | Operations/API/UI + tests | AC5-AC7, AC10 | integration/component tests | In progress |
| T4 | evidence/location correction | Main | Lead | Approval | Collection platform + tests/migration | AC8-AC10 | migration/store/handler tests | Verified |
| T5 | final review | Main | Lead/Review | T1-T4 | Change record only | traceability audit | audit evidence | In progress |
| T6 | terminal時の未充足revision実体化 | Main | Lead | Re-approval | Collection platform store + tests | AC11 | success/failure/cancel/dead-letter/concurrency tests | Verified |
| T7 | migration preview/apply/progress契約 | Main | Lead | T6 | Operations/API contracts + tests | AC12-AC14 | persistence-boundary integration tests | Verified |
| T8 | migration設定画面と旧repair入口整理 | Main | Lead | T7 | Settings/API client/UI + tests | AC12, AC15 | component/API tests and caller inventory | Verified |
| T9 | deploy migration・本番補正検証 | Main | Lead/Review | T6-T8 | deploy workflow, change record | AC13-AC15 | cutover evidence and production read-only post-check | Dependent |

## Review gates

### Design and task-split review

- Reviewer: Main
- Decision: Approved design is internally consistent; every AC maps to a task and verification.
- Boundary: no automatic recovery, priority mutation, notification resolution, or broad retry.
- Sequencing: schema/location and shared contracts are serialized; no parallel writer is used.

### Pre-implementation review

- Reviewer: Main
- Runnable frontier: T1 and T4. Main owns both and will serialize shared contract/schema edits.
- Inputs: production Race/job evidence, aggregate/event/read-model paths, collector handler/store paths.
- Escalation: material external behavior change, destructive repair, or inability to preserve old collector/API compatibility returns the record to Proposed.
- Expected evidence: production-shaped regression, replay/idempotency, null-preservation, migration/store/handler, repair invariant, UI/component tests.

## Documentation updates

- `docs/changes/20260919_race-detail-phase-recovery/README.md`: links this follow-up for the previously open Location artifact and scheduling-evidence corrections.
- `.github/workflows/app-deploy.yml`: APIと新Collectorの両配備後に、pause、running task drain、preview、冪等apply、resumeを行うproduction migration jobを追加した。
- No additional canonical architecture document currently defines entry enrichment or operator repair semantics; this approved change record is the canonical change-specific source.

## Deployment and recovery gate

API/schemaを先行し、Collector互換を確認してからCollectorを配備する。配備後にread-only previewを実行し、
対象Raceを明示してrepair applyする。対象RaceのAPIと画面でownerが揃うことを確認するまで完了としない。
既存error jobは本repairに混ぜない。

追加変更後は、`race-detail` migrationを次の順で実行する。

1. 対象DBのbackupを取得し、API/Collectorの互換版を配備する。
2. pipelineをpauseし、実行中taskが0件になるまでdrainする。
3. migration previewで構造移行error、owner欠損対象、既存処理状態を確認する。
4. migration applyで構造移行とrevision 2補正要求の登録を行う。
5. pipelineをresumeし、migration progressで補正完了・失敗・公式情報なしを追跡する。
6. 対象Race API/画面とentry-level集計で欠損0件を確認する。欠損または失敗が残る限りmigrationは完了扱いにしない。

## Additional design and task-split review

- Reviewer: Main
- Finding: `RequiredRevision`はactive task成功時には後続taskを生成するが、失敗・中断・dead-letter経路では未充足requestの実体化保証がない。単純なactive taskのrevision上書きでは実行中payloadとの不整合が起きるため採用しない。
- Decision: 全terminal経路で未充足requestを再評価し、transaction内でactive taskを入れ替える共通処理を採用する。
- Migration boundary: 補正値の直接書換えは行わず、公式Card再取得と既存aggregate mergeをデータ補正手段とする。個別error jobの一括復旧は対象外。
- Task split: persistenceとmigration契約が共有状態を変更するためT6-T9は直列実行し、並列writerは使用しない。
- Approval gate: AC11-AC15と旧repair入口の一本化は外部・運用仕様の追加であるため、再承認まではproduction codeを変更しない。

## Additional pre-implementation review

- Reviewer: Main
- Approval: 2026-09-19にユーザーがAC1-AC15とmigration一本化を承認した。
- Runnable frontier: T6。T7はterminal実体化基盤、T8はmigration API契約、T9は全コード・検証に依存する。
- Write ownership: Mainがstore、API contract、Settings UI、deploy workflow、tests、change recordを直列で所有する。共有永続状態と共通contractがあるため並列writerは使用しない。
- Required evidence: terminal種別ごとの後続task一意性、migration preview不変性、apply/restart冪等性、UI状態、workflow contract、全関連回帰、format、CodeGraph、diff/status。
- Escalation: 直接データ書換えが必要、公式Cardから補正不能、既存error jobの自動復旧が必要になった場合は承認範囲外として再設計する。

## Checkpoint review

- Reviewer: Main
- Finding: direct candidateのLocation分類だけでは、自動探索後に新規保存するRequested/Final URLがUnknownのまま残る漏れを検出した。
- Fix: stage outcomeのURLとartifactを照合し、新規・redirect Locationにも分類を保存する。明示URLの仮Location IDはDB更新対象から除外する。
- Regression: schema baseline/concurrency、Location store、Card/Result navigation、write failure evidenceを追加・再実行した。
- Workflow assessment: 完了前のチェックポイントで検出・修正できた単発実装漏れであり、既存DDD/セルフレビュー規約の不足は実証されていないためskill変更は行わない。

## Verification record

- `dotnet test tests/HorseRacingPrediction.Domain.Tests/HorseRacingPrediction.Domain.Tests.csproj --no-restore`: 107 passed.
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-restore`: 265 passed.
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore`: 247 passed, 1 existing skip.
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: passed after formatting.
- `git diff --check`: passed.
- `codegraph sync .`: completed; index reported current.
- Production AC5 remains connected but unverified until deployment and explicit selected-Race repair.

### Additional implementation verification

- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-restore`: 269 passed。success/failure/cancel/dead-letterの全terminal経路、schema v17、同時初期化を含む。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore`: 247 passed, 1 existing skip。migration preview/apply replay、設定画面を含む。
- `dotnet build HorseRacingPrediction.sln --no-restore`: passed、0 warnings / 0 errors。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: passed。
- `python .codex/skills/document-driven-development/scripts/validate_change_records.py docs/changes/20260919_race-entry-owner-enrichment/README.md`: issues=0。
- `codegraph sync .`と`codegraph explore "MaterializeUnsatisfiedRevisionAsync callers and race-entry-owners migration endpoint production path"`: production storeからAPI、設定画面、testまでの接続を確認した。
- `git diff --check`: passed。

## Additional checkpoint and final review

- Reviewer: Main
- Checkpoint finding: 単一request APIはactive taskへ統合した高revisionを`RequiredRevision`へ反映していなかった。bulkだけの局所修正では同型不具合が残るため、単一・bulk双方でrequestを永続化し、terminal共通処理から後続taskを生成するよう修正した。
- Persistence finding: 後続taskが元taskのlane/priority/metadataを流用すると新revision要求の条件を失うため、schema v17でrequestへ`Lane`、`Priority`、`MetadataJson`を追加し、後続taskはrequestから復元する。
- Migration review: 固定batch `migration:race-entry-owners:v2`、全Race read-only scan、pause/running-drain gate、公式Card再取得、進捗分類を実経路へ接続した。Horse profileからの直接コピーは存在しない。**ただし後続確認で、全Race scanから全欠損Raceへ要求する設計はCard取得期間外のRaceを補正できないことが判明したため、本項の実装完了判定を撤回し、再設計を要する。**
- Deployment checkpoint — 2026-09-19: migration完了後も各deployが先にpipelineをpauseしてrunning task drainを待つため、run 35445524181が不要な5分待機後に失敗した。read-only previewの `remaining == 0` をpause前に判定し、完了済みならmigration jobを成功終了する。remainingがある場合のpause/drain/apply/resume境界は維持する。
- Deployment correction — 2026-09-19: run 35446972643で `remaining > 0` が再発し、通常収集中のRunning task drain待ちで再びdeployを停止した。本record自体が対象選定の再設計待ちで`Proposed`へ戻っているため、deploy時migrationはread-only previewだけに変更し、未承認のapplyとpipeline pauseを除去する。
- Legacy surface: 旧日付preview/apply APIは外部互換用に残し、applyはpause/drainと同じ固定migration batchへ委譲する。旧Web client/UI callerは0件で、設定画面の操作入口はmigrationへ一本化した。
- Final state: T6-T8の基盤部分は検証済みだが、owner補正対象の選定はCard取得元制約に違反している。T9、AC5、AC13-AC15は再設計後に再検証するため、本recordを`Proposed`へ戻す。
- Workflow assessment: テストが承認後の実装中に既存単一request APIの同型欠損を検出し、承認済み範囲内で根本修正・再検証できた。既存DDD/セルフレビュー規約の不足を示す反復失敗ではないためskill変更は行わない。
