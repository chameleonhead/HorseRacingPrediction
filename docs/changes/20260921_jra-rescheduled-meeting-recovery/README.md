# JRA開催中止・代替開催を正しく引き継ぐ

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-21
- Updated: 2026-09-21

Approval: 2026-09-21、利用者が提示済みの具体対応とAC1-AC40を確認し「対応をお願いします」と明示承認した。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | Result link identity、Card/Result独立、代替開催request、domain lineage、wake lease隔離を実装した。 |
| Verification | Verified | live直近/Historical Result、Scraping 266、Collector 292、Domain 109、API 269、Release build、migration modelを検証した。 |
| Deployment/operation | Externally blocked | AWS/対象DBへ接続できないため、既存race-detail migration preview/applyと対象中山の本番確認は未実行。 |

## Context

2026-09-21の`Race/JRA/20260921:Nakayama:2/race-detail`で結果取得エラーが発生した。管理画面は認証画面へ転送されたためattempt本文は未取得であり、以下はJRA公式情報と現行コードからの強い推定である。

- JRAは9月21日の第4回中山第7日を台風で中止し、同じ出馬表のまま9月22日に代替開催すると公表した。
- 対象出馬表は9月21日・第4回中山第7日・発走10:20を保持する。
- 現行handlerはRace IDの日付と予定発走時刻から結果dueを計算するため、中止後も9月21日10:25以降は結果を探索する。
- 現行には開催全体の中止・代替先状態がなく、`RaceId(Date, Course, Number)`の厳密一致を要求する。
- 過去の代替開催では同じ開催回・開催日番号でも、結果ページの日付とCNAMEは実施日に変更される。

月曜日判定は原因ではない。公式JRAには2025-09-15（月曜）と2024-09-16（月曜）の通常結果が現存し、2026-09-21も阪神は通常開催である。一方、対象の旧中山Card URLは現在`DB検索エラー007`、代替日の2026-09-22には同じ`4回中山7日`のCardが存在する。したがって対象障害には、**予定日をRace identityとして固定したまま代替日を扱えないこと**と、**Card取得失敗がResult段階を止めること**が関与する。

提供されたLambdaログから、これとは独立した実行基盤欠陥も確認した。wake経路の`ExecuteWakeAsync`はgroup内taskの`CollectionTaskActiveElsewhereException`を捕捉せず、legacy envelope経路にだけ存在する継続処理が欠けている。そのため1件のlease競合が24件のRaceDay groupとLambda invocation全体を`Aborted`にする。これは取得失敗の直接原因ではないが、失敗の隔離と再試行を壊しているため同時に修正する。

### Correction after hypothesis testing

当初は代替開催identity不足だけを主原因と推定したが、live E2Eで通常の直近結果と2020年の過去結果も`JraRaceListPage`へ着地した。診断用traceとリンク一覧を追加して再実行したところ、両経路とも対象Rの直接URLではなく、同じ`accessS.html#`を共有する先頭要素`検索`をクリックしていた。実ページには`1レース=>...CNAME=pw01sde...`の直接Result URLが存在するが、selectorが別要素の`レース結果`文字列とfragment URL単位で結合し、navigatorが同URLの先頭要素を再選択するためである。つまり、代替開催とは独立した共通Result選択欠陥も確定した。

## Hypothesis validation ledger

| ID | Hypothesis | Fact / inference boundary | Falsification method | Result | Disposition |
| --- | --- | --- | --- | --- | --- |
| H1 | 月曜開催一般が失敗原因である。 | 同日中山だけ中止。JRAに複数年の通常月曜Resultが存在する。 | 公式の通常月曜Resultと中止告知を比較する。 | Falsified。曜日ではなく開催状態を判定すべき。 | Rejected with reason |
| H2 | 中山2Rの`PermanentFailure`は代替開催identity不足だけで起きた。 | 旧Card URLはエラー、翌日に同一開催identityのCardが存在。Result共通欠陥とCard先行制御も存在。 | 旧/新Card、handler stage順、normal/historical Result traceを比較する。 | Falsified as single cause。少なくとも3欠陥が独立している。 | 分離修正 |
| H3 | 現行Historical経路で過去Resultを再取得できる。 | JRA公式には2020年結果が存在する。 | `JraSiteE2ETests.古いRaceResult取得`をlive実行しリンク集合を採取する。 | Falsified。`検索=>accessS.html#`を誤クリック。対象Rの直接Result URLは存在。 | selector修正後に年代別再検証 |
| H4 | 直近Result経路は正常で、古い経路だけが壊れている。 | 従来fixtureは成功。 | `JraSiteE2ETests.完了済みRaceResult取得`をlive実行しリンク集合を採取する。 | Falsified。同じ`検索`誤クリックを再現。 | 共通Result選択欠陥として修正 |
| H5 | 全既存Raceを公式データから完全に再構築できる。 | Resultは1986年以降、2000年以前は不完全。過去Cardの保存保証なし。 | target DB inventory、年代別Result matrix、Card固有項目coverageをpreviewする。 | 「全レコードの分類・参照移行」は可能。「全項目の公式再構築」は反証済み。 | 欠損を明示する非破壊移行へ限定 |
| H6 | `ActiveElsewhere`がwake Lambda全体を落とす。 | 提供ログと`ExecuteWakeAsync`の未捕捉、legacy経路の捕捉を確認。 | wake経路の同条件integration test。 | コード・ログでverified、回帰testは未作成。 | 独立した確定修正候補 |

## Goals

1. 公式画面の開催回・競馬場・開催日番号を安定した開催identityとして取得する。
2. 同じ開催identityが別日に移った場合、旧予定日の取得を正常終端し、実施日resourceへ一度だけ引き継ぐ。
3. 実施日をdomain RaceDateとし、旧予定日へ結果を誤登録しない。
4. 代替未決定、中止のみ、再延期、通常の平日開催を区別する。
5. 管理画面で旧予定日から代替先を追跡できるようにする。

## Non-goals

- JRAニュース本文だけを正本としてスクレイピングしない。
- 曜日による開催可否判定を追加しない。
- 旧Race aggregateを削除したりイベント履歴を書き換えたりしない。
- 競走単体の取消・除外・競走中止の既存処理は変更しない。
- AWS CLIを前提とした復旧手順にはしない。

## Proposed behavior

修正は次の順序で分離する。後段が前段の失敗を隠さないよう、それぞれ独立した反例testを持つ。

1. **Resultリンク選択**: URL文字列だけでなく選択したDOMリンクのidentityを保持する。対象R番号を含む直接`CNAME=pw01sde...` URLを最優先し、期待する日付・場・R番号をparseして一致確認する。fragment controlしかない場合だけ、対象R番号を持つ同一要素をクリックする。別要素の`レース結果`や`検索`を同一URLだからという理由で結合しない。遷移後も`JraRaceResultPage.RaceId`を再検証する。
2. **Card/Result段階の独立**: `race-detail`はCard未取得をResult取得の前提にしない。結果確認時刻以後は、Cardの`DB検索エラー`、掲載範囲外、identity mismatchをstage outcomeへ残してResultへ進む。結果前は従来どおりCardを再試行する。Cardの永続的欠損だけで既に取得済みResultを失敗へ戻さない。
3. **代替開催**: 日付とは別に`Year + RaceCourse + MeetingNumber + MeetingDay + RaceNumber`をsource identityとして扱う。旧URLのエラーだけでは代替と断定せず、公式選択画面/新Cardで同一identityが別日に存在することを確認してから旧Raceをsupersedeする。
4. **lease競合隔離**: wake group内の`CollectionTaskActiveElsewhereException`はそのtaskだけをskip/redelivery対象とし、後続taskとgroup completionを継続する。

`Year + RaceCourse + MeetingNumber + MeetingDay`を`JraMeetingIdentity`、これにRaceNumberを加えたものをsource identityとする。日付は実施日でありsource identityそのものには含めない。

結果due後に結果が存在しない場合、公式開催選択を最大7日先までbounded探索する。年・場・開催回・開催日番号・Race番号が一致し、新日付が後日である場合だけ代替先と認定する。旧resource/taskを`NotApplicable / MeetingRescheduled`で終端し、実施日の新Race resource/requestを冪等作成する。出馬表は実施日から再取得して騎手変更等を反映する。

旧domain Raceは新しい`RaceStatus.Rescheduled`と代替先Race IDをイベントで保持し、削除せず予測・学習・通常の未結果監視から除外する。代替日未決定は`MeetingCancelledAwaitingDecision`として長いbackoffで待機する。再延期も同じidentityから一段ずつ追跡し、循環・重複を禁止する。競走単体中止は既存の公式RaceResult cancellationを維持する。

## Existing-data migration

移行対象は本番DBに存在する全JRA Race domain aggregate、collection resource/location/request/task/attempt/facet、予測・学習・主体履歴等のRace参照とする。JRA全史を新規収集する作業ではなく、既存データを全件棚卸しして負債を残さないcutoverとする。

1. **Inventory/preview**: stored Result URL、page identity、meeting number/day、実施日、domain参照を読み取り専用で抽出し、`Canonical`、`RescheduledSource`、`Replacement`、`Ambiguous`、`OfficiallyUnavailable`へ分類する。件数と対象IDをpreview artifactへ保存する。
2. **Authoritative enrichment**: Resultが取得可能なRaceはJRAの過去結果導線から再取得し、実施日・場・開催回・開催日番号・Race番号を検証する。JRAは1986年以降の結果を掲載するが、2000年以前は不完全・表示差異があり得るため、欠損を推測補完しない。
3. **Canonical creation**: 代替先Raceが未作成なら、公式結果の実施日を使ったcanonical Race/resourceを作成する。Resultは再収集する。過去Cardが公式に取得できない場合はResultからCard固有項目（馬主等）を復元しない。
4. **Non-destructive supersession**: event storeの旧aggregate IDや過去attemptを移動・書換えない。旧Raceへ`Rescheduled` lineage event、旧collection resourceへsupersession relationを追加する。task/attemptは元resourceの監査証拠として保持する。
5. **Reference cutover**: 現行参照（予測、学習、履歴、監視、再取得、UI）をcanonical Raceへ切り替える。projectionはlineageを解決して旧予定日Raceを通常集計から除外する。未分類旧参照が0件になるまでapplyを完了扱いにしない。
6. **Operational cutover**: dispatcherを短時間drainし、preview hashとDB identityを再確認してapplyする。running taskはleaseを奪わず終了を待つかcancel requestを行い、完了後に旧resourceを非dispatch化する。rollbackは新イベント・relationを無効化する補償操作で行い、旧履歴を削除しない。

過去結果の取得可能範囲と既存データの完全移行は別である。公式に再取得できないCard固有値を新しいcanonical Raceへ完全復元することは保証しないが、欠損理由とprovenanceを明示し、誤った値や永続retryを残さない。

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: 安定した開催identityと実施日resourceへの引継ぎを追記する。
- `docs/27-jra-site-collection-contract.md`: 開催中止、代替開催、通常平日開催の区別を追記する。
- `docs/changes/20260920_robust-race-result-navigation/README.md`: 変更しない。前回のURL遷移修正とは別問題である。
- `docs/changes/20260919_collection-monitor-root-cause-triage/README.md`: 利用者指定により変更しない。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 待機だけではJRA結果が実施日identityになる。 | 永久retryまたはidentity mismatch。 | 開催identityで検証し実施日resourceを正規化する。 | AC1-AC3/T1-T3 | 必須 | Pending | Resolved in design |
| C2 | 出走馬一致だけでは再投票や類似レースを誤認し得る。 | 誤Race紐付け。 | 年・場・開催回・開催日番号・Race番号の完全一致を必須にする。 | AC2,AC5/T1,T2 | 必須 | Pending | Resolved in design |
| C3 | 旧Raceの日付上書き・削除は履歴を破壊する。 | 不可逆な整合性破壊。 | `Rescheduled`イベントで保存し、実施日Raceを新規作成する。 | AC3,AC4/T3 | 必須 | Pending | Resolved in design |
| C4 | 代替日未決定の場合がある。 | 日付推測・過剰アクセス。 | 公式画面に同一identityが出るまで既定7日・長いbackoffで待機する。 | AC1,AC5/T2 | 必須 | Pending | Resolved in design |
| C5 | 月曜を特殊扱いすると正常な祝日開催を壊す。 | 正常開催の退行。 | 曜日は使わず公式identityと日付を使う。 | AC6/T4 | 必須 | Pending | Resolved in design |
| C6 | live正常/Historical Resultが同じ`検索`fragment誤クリックを再現し、旧Card URLはDB error、新Cardは翌日に存在する。 | 単一原因修正では再発する。 | link identity、stage独立、代替identityを別々に修正・検証する。 | AC1-AC7/T0,T4,T5 | 必須 | Pending | Resolved in design |
| C7 | wake経路は`ActiveElsewhere`を未処理で再throwする一方、legacy経路はitem failure化して後続を継続する。 | 単一lease競合がgroup全体をcrashさせる。 | wake経路でも競合を型付きskip/redeliveryとして扱い、後続taskとbatch completeを継続する。 | AC8/T6 | 必須 | Pending | Resolved in design |
| C8 | JRAは1986年以降の結果を公開するが、2000年以前は不完全・表示差異があり、過去Cardは同じ公開保証がない。 | 全項目の完全再構築を保証できない。 | Resultを正本に可能な項目だけ再取得し、Card固有値は推測せず`OfficiallyUnavailable` provenanceを残す。 | AC9,AC12/T7 | 必須 | Pending | Accepted risk |
| C9 | event storeのaggregate ID・過去attemptを物理移動すると監査履歴が偽装される。 | 追跡不能・参照破壊。 | 旧データを保持し、lineage event/relationとcanonical projection cutoverで移行する。 | AC9-AC11/T7 | 必須 | Pending | Resolved in design |
| C10 | migration中のrunning taskとdispatcherが旧resourceへ書き込む可能性がある。 | 二重作成・partial cutover。 | preview、drain、DB identity/hash再確認、transactional apply、post-check、resumeを必須化する。 | AC10,AC11/T7 | 必須 | Pending | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 開催全体が中止され代替日未決定なら、構造エラーにせず`MeetingCancelledAwaitingDecision`でbounded retryする。 | T1,T2,T4 | cancellation integration | Verified |
| AC2 | 同じ年・場・開催回・開催日番号・Race番号が後日公式画面に現れた場合だけ代替先と認定する。 | T1,T2,T4 | identity matrix | Verified |
| AC3 | 9月21日中山2R相当で旧taskが`MeetingRescheduled`終端し、9月22日のrequestが一件だけ作成され、結果は実施日Raceへ保存される。 | T2,T3,T4 | handler/store/domain E2E | Verified |
| AC4 | 旧domain Raceは削除・上書きされず代替先を保持し、未結果monitor・予測・学習対象から除外される。 | T3,T4 | event/read-model/API tests | Verified |
| AC5 | 再延期、重複配信、7日以内に代替なし、identity不一致で循環・重複・誤登録が起きない。 | T2-T4 | idempotency/counterexample tests | Verified |
| AC6 | 通常の月曜開催、週末、競走単体中止、既存Card→Result遷移が従来どおり動く。 | T4 | non-External regression | Verified |
| AC7 | 配備後、対象旧taskの終端、代替先resource、実施日出馬表、結果取得進行を管理画面から追跡できる。 | T5 | production checklist | Externally blocked |
| AC8 | wake RaceDay group内の1 taskが`ActiveElsewhere`でもLambdaは未処理例外で終了せず、当該taskを重複実行せずに後続taskとbatch completionを継続する。 | T6 | wake invocation integration | Verified |
| AC9 | 本番の全JRA Raceと全collection履歴がpreviewで分類され、未分類・重複canonical・循環lineageが0件になる。 | T7 | migration preview/invariant report | Externally blocked |
| AC10 | 代替開催対象は実施日canonical Raceへ参照が切り替わり、旧Race/resource/task/attemptは監査証拠として保持されるがdispatch・予測・学習・未結果監視の対象にならない。 | T3,T7 | target DB cutover post-check | Externally blocked |
| AC11 | migrationはpreview hash、対象DB、backup、drain、apply、post-check、resume、rollback evidenceを持ち、途中失敗時にmixed active stateを残さない。 | T7 | cutover rehearsal and production checklist | Externally blocked |
| AC12 | 公式Resultを取得可能な既存Raceは再検証され、取得不能またはCard固有欠損は推測されずprovenance付きで可視化される。 | T4,T7 | historical sample matrix and unavailable report | Externally blocked |
| AC13 | 直近・Historicalの一覧で対象Rの直接Result URLがある場合はそれを選び、`検索`、メニュー、オッズ等の同一fragment URLを誤クリックしない。fragment-only fixtureでは対象R要素そのものをクリックする。 | T0,T4 | selector unit + live E2E | Verified |
| AC14 | 結果確認時刻後にCardがDB error/掲載範囲外でもResult段階へ進み、Result成功時はCard欠損をstage outcomeとして保持してtaskを成功または明示的部分成功にする。結果前の正常Card再試行は維持する。 | T2,T4 | handler matrix | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T0 | 正常直近・中止当日・HistoricalのResult遷移をproduction-shapedに比較し、RaceListで止まる最初の分岐を確定する。 | Main | High capability | - | read-only live evidence/tests/change record | terminal trace and link inventory | H1-H5 dispositions | Verified |
| T0a | Result selectorがURLではなく選択要素identityを返し、direct Result URLを優先するよう修正する。AC13 | Worker | Low-cost coding | Approval | selector/navigator/tests | selector unit + live E2E | normal/historical pass | Verified |
| T1 | 開催identityをCard/List/Resultから解析する。AC1,AC2 | Main | High capability | Approval | Scraping models/parsers/tests | identity matrix | typed identity | Verified |
| T2 | bounded代替探索とresource引継ぎを実装する。AC1-AC3,AC5 | Main | High capability | T1 | Navigator/handlers/store/tests | reschedule E2E | idempotent transition | Verified |
| T3 | domain/read modelへRescheduled状態と代替先を追加する。AC3,AC4 | Main | High capability | T1 | Domain/Application/API/migration/tests | event/read-model tests | lineage | Verified |
| T4 | production-shaped fixtureと全非回帰を検証する。AC1-AC6 | Main | High capability | T2,T3 | tests/fixtures/docs | format/build/non-External | CI evidence | Verified |
| T5 | 配備し本番進行を確認する。AC7 | Main | High capability | T4 | deployment/change record | deploy/UI evidence | production trace | Externally blocked |
| T6 | wake経路のlease競合を隔離する。AC8 | Worker | Low-cost coding | Approval | Lambda invocation/tests | active-elsewhere group integration | no group crash | Verified |
| T7 | 全既存Race/collection/参照をpreview・移行・post-checkする。AC9-AC12 | Main | High capability | T1-T4,T6 | migration tool, projections, operational artifacts | rehearsal plus target DB invariants | zero unclassified active debt | Externally blocked |

## Review gates

- **Design and task-split review — 2026-09-21, reviewer: Main.** identity、collection引継ぎ、domain lineageは整合性境界を共有するためMainが依存順に保持する。全ACにtaskと反例を割り当てた。
- **Concern and agreement review — 2026-09-21, reviewer: Main.** live E2E、実リンク集合、旧/新Card、handler制御を比較し、単一原因仮説を棄却した。コード修正の4境界は承認可能。target DB inventoryが必要なmigration applyだけを後段gateへ分離する。
- **Pre-implementation review — 2026-09-21, reviewer: Main.** 利用者の明示承認後、link selectionとlease isolationだけをexclusive write scopeでworkerへ委譲し、identity・persistence・migrationはMainに保持した。
- **Checkpoint review — 2026-09-21, reviewer: Main.** live Result 2経路、代替fixture、Card/Result due境界、lease競合、domain lineage、EF migrationを個別証拠へ追跡した。production apply以外の承認済みコード項目をVerifiedとした。
- **CI closure — 2026-09-21.** current modelから`EnsureCreated`された旧table-set fixtureには`ReplacementRaceId`列が既に存在する一方、baseline判定が`AddRaceRescheduleLineage`を履歴登録せず、同migrationを再適用して重複列になっていた。3 read model全ての列存在を反証条件として検査し、存在時だけ同migrationをbaselineへ含めた。`SqliteDbContextProviderTests` 8/8で旧データ保持と全migration履歴を再検証した。
- **Final review — pending production access.** T5/T7のproduction preview/apply/post-check後に実施する。

## Verification record

- 2026-09-21: JRA公式は第4回中山第7日を9月21日に中止し、同じ出馬表で9月22日に代替開催すると告知した。
- 2026-09-21: 対象中山2Rは9月21日・第4回中山第7日・発走10:20を保持する。
- 2026-09-21: 過去の第1回東京第4日・第2回京都第4日の代替結果は予定日2月8日ではなく実施日2月10日を日付とCNAMEに使用する。
- 2026-09-21: 現行handlerは`RaceId.Date + StartTime + grace`で結果dueを決め、開催中止・代替関係を保持しない。`RaceId`は日付・場・番号の厳密identityである。
- 2026-09-21: 提供ログで2R/3Rが`PermanentFailure`となった後、同一RaceDay groupのtask acquireが`ActiveElsewhere`を返し、未処理例外でLambdaが`Aborted`した。
- 2026-09-21: `ExecuteWakeAsync`はgroup loopで`ActiveElsewhere`を捕捉しないが、legacy `ExecuteAsync`は同例外をitem failure化して他taskを継続することを確認した。
- 2026-09-21: JRA公式FAQは1986年以降の結果を公開し、2000年以前は不完全・表示差異があると明記する。2002年以降は成績表PDFも提供される。過去Card固有項目の同等な公開保証は確認できない。
- 2026-09-21: live `JraSiteE2ETests.古いRaceResult取得`は2020年の公式Resultが存在するにもかかわらず`JraRaceListPage`へ着地して失敗した。H3を反証した。
- 2026-09-21: live `JraSiteE2ETests.完了済みRaceResult取得`も`JraRaceListPage`へ着地して失敗した。H4と「代替開催だけが原因」というH2を反証した。
- 2026-09-21: 診断traceでは直近/Historicalの双方が`Route=RaceControlClick; CandidateLabel=検索; RawUrl=...accessS.html#`だった。失敗後の実リンク集合には対象Rの`CNAME=pw01sde...`直接URLが存在した。URL groupでpurposeを結合し、navigatorが同URLの先頭要素を再選択する実装が共通原因である。
- 2026-09-21: 9月21日中山2Rの旧Card URLはJRAの`DB検索エラー007`、9月22日には同じ`4回中山7日`のCardが存在する。JRA告知は出馬表内容を維持する一方、2Rを含む騎手変更も明記しており、新実施日のCard再取得が必要である。
- 2026-09-21: JRAには2025-09-15（月曜）中山と2024-09-16（月曜）中山の通常Resultが存在する。H1を反証した。
- 2026-09-21: workflow failureは、外部条件の事実からアプリの根本原因と完全移行可能性を先に推論し、production-shaped実経路を提案前に実行しなかったことである。`.codex/skills/document-driven-development/SKILL.md`へmaterial hypothesis validation gateを追加し、skill validator成功を確認した。
- 2026-09-21: skill forward testとして、本incidentは未検証仮説がある間は`Proposed/Open decision`に留まり、terminal reproduction後にだけ承認候補へ進むことを確認した。反対に、因果・migration・完了判断を変えない誤字修正ではhypothesis ledgerを要求しないため、低risk作業を不必要に停止しない。
- 2026-09-21: Result selector worker（requested/observed=`gpt-5.6-luna`/role固定、usage unavailable）はselector/navigator/testsだけを変更し、Lead review 1回、修正0回で統合した。58 focused testsとlive 2 E2Eで独立検証した。
- 2026-09-21: ActiveElsewhere worker（requested/observed=`gpt-5.6-luna`/role固定、usage unavailable）はLambda invocation/testsだけを変更し、Lead review 1回、修正0回で統合した。14 focused testsとCollector 292件で独立検証した。
- 2026-09-21: `JraSiteE2ETests.完了済みRaceResult取得`と`古いRaceResult取得`は修正後live実行で2/2成功した。
- 2026-09-21: non-External Scraping 266/266、Collector 293/293、Domain 109/109、API 269成功・1 skipに加えreschedule API focused test成功、Release solution build警告0/エラー0。
- 2026-09-21: `AddRaceRescheduleLineage` migrationを生成し、`has-pending-model-changes`は変更なしを確認した。
- 2026-09-21: 既存の`/migrations/race-detail/preview`とpause必須`apply`はlegacy RaceCard/Result resource、request、task、attempt、location、stateを非破壊で統合する。対象DB接続不可のためproduction preview/apply件数と不変条件は未確認でありT7/AC9-AC12を完了扱いにしない。
- Mutation performed: documentation only. Production recovery and retry were not executed.

## Approval request

コード修正部分は、共通Result selector、Card/Result段階独立、代替identity、lease競合隔離の4境界まで仮説検証できたため承認対象にできる。データ移行は、全既存レコードを分類・canonical参照へ移すことを対象とするが、AWS/対象DBへ接続できない現時点では件数・欠損率・apply可否を確定しない。まずread-only previewを実装・実行し、`Ambiguous`と`OfficiallyUnavailable`を含む全件分類を提示した後、不可逆なapplyについて別途明示確認を得る。公式に存在しない過去Card項目の完全復元は受け入れ条件に含めない。
