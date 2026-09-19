# Raceリソース中心の取得状態機械

- Status: Approved
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Completed | Race facet/evidence/stage outcome、revision 2 controller、管理画面表示を実装した。 |
| Verification | Completed | API build、Collector 261件、API 245件（既存skip 1件）とchange-record validatorを実行した。 |
| Deployment/operation | Not started | push/deployは本変更依頼に含めず、既存エラージョブの復旧も実行していない。 |

> Follow-up: 既存entryがある場合に後着Cardの馬主等を保存しない問題と、未実装だったLocation artifact分類・
> write失敗時の発走時刻evidence保持は
> [Race出走馬の後着データ補完](../20260919_race-entry-owner-enrichment/README.md)で扱う。

## Context

本番では1つの`Race / race-detail`が出馬表保存と結果取得を担う。出馬表保存結果のerrorを確認せず後続の
`RaceNotStarted`や`RaceResultNotYetAvailable`で上書きするバグと、`startTime`欠落時に全レースを9:35扱いする
バグが発生した。

最初の改修案は単一Taskへphase列を足す案、次の案はCardとResultを別Definition/Taskへ分離する案だった。
しかし後者は、実態が1つのRaceリソースであるにもかかわらず同一Raceのジョブを複数作り、一覧、priority、通知、
Recovery判断を重複させる。前者もTaskをデータ完全性の正本にすると複合成功条件とcrash windowを残す。

根本原因は「Raceの取得済み内容」と「そのために現在実行する作業」を同じTask statusで表現していることである。
Raceリソースの内容状態を正本とし、Taskをその状態を前進させる単一の実行コントローラへ限定する。

### Incident ledger

```text
Incident: 2026-09-19 14:12 JST、当日後半レースが発走前に6～8回結果確認、翌日・翌々日48レースが未保存
Temporary recovery: なし（GETと画面参照のみ）
External/input condition: CardとResultは異なる時刻に公開され、公式StartTimeを欠く場合がある
Technical defect: Task statusだけで複数artifactの完全性を表し、card errorをresult waitが上書きした
Workflow gap: Resource completenessとTask execution lifecycleを分離しなかった
Corrective proposal: 本change record
Permanent fix: Not started
Remaining risk: 今週末の既存不足とlegacy attemptの判定不能
```

## Fundamental principles

1. canonical `Race / yyyyMMdd:Course:Number`は1件だけ持つ。
2. 同一Raceのactive `race-detail` Taskは全stageを通して最大1件だけ持つ。
3. Card/Resultの取得状態、公開条件、失敗はRaceリソース配下のfacet stateを正本とする。
4. Task statusは「実行可能か、実行中か、次回まで待つか、これ以上自動実行しないか」だけを表す。
5. 1 Attemptは実行したstageごとの構造化outcomeをappend-onlyで残し、後続stageは前段の事実を上書きしない。
6. 既存エラージョブの復旧は本変更から切り離し、自動再投入・昇格・通知解決を行わない。

## Proposed model

### One resource, one active task

```text
Race resource: 20260919:Hanshin:10
  Evidence
    OfficialStartTime = 15:10 (JRA card, verified)
  Facets
    Card   = Current / last persisted / failure / location
    Result = AwaitingStart / next due / failure / location
  Active task
    race-detail (1件だけ)
      NextAction = AwaitOfficialStart
      AvailableAt = 15:15
```

CardとResultを別Resource、別Definition、別Taskにはしない。`RaceArtifactState`を
`(ResourcePk, ArtifactKind)`で持ち、`ArtifactKind`は`Card`と`Result`とする。各facetは少なくとも次を持つ。

- Status: Unknown / AwaitingPublication / Due / Collecting / Current / Blocked / Unavailable
- AppliedRevision / RequiredRevision
- LastObservedAt / LastPersistedAt / NextDueAt
- LastAttemptId / ErrorCode / ErrorImpact
- verified Locationとpage identity

公式StartTimeとprovenanceはtask metadataではなく、更新可能なRace scheduling evidenceとして保持する。
task metadataは作成時入力のまま変更せず、active taskは実行時に最新evidence/facetを読む。

### Typed acquisition state machine

plannerはRace resourceとfacet stateから次のstageを決める。

```text
Discover/resolve Card
  → Persist Card
  → Await official start
  → Discover/resolve Result
  → Persist confirmed Result or official cancellation
  → Complete automatic acquisition
```

- Card公開前: 同じTaskを低頻度待機へ戻す。
- Card取得成功: Card facetをCurrentにし、Resultの公式開始時刻まで同じTaskを待機させる。
- Card保存失敗: Card facetをBlockedにし、failure notificationを残す。Resultは公式時刻/URLが判明すれば独立stageとして進める。
- 発走前: Result navigationを呼ばない。
- StartTime不明: 9:35等を推測せず、Card/meeting discoveryから公式時刻または検証済みResult URLを得るまで待つ。
- Result未公開/未確定: Result facetだけを待機へ戻し、Card facetを変更しない。
- Result確定/公式取消: Result facetをCurrent/Unavailable terminalにする。
- 自動で進められるstageがなくBlocked facetがあれば、Taskを終了し要対応を残す。Recovery taskは作らない。

同じAttemptでCard後にResultがdueなら、同一sessionの現在ページから続けて処理できる。Attempt completionは
各stage outcomeと次のTask scheduling decisionを1つのlease-fenced transactionで保存する。

### Resource completeness

Race全体の表示状態はfacetから導出し、Task statusをデータ完全性として使わない。

- `Card取得済み / Result発走待ち`
- `Card保存エラー / Result取得済み`
- `Card取得済み / Result公開待ち`
- `Card legacy判定不能 / Result legacy判定不能`

Prediction readinessはCard facet、確定結果利用可否はResult facetを参照する。`Result=Current`だけでCardをCurrentにせず、
Cardの失敗だけでResult保存を禁止しない。

## Correctness boundaries

### Location resolution and write

Location候補の評価はnavigation、page kind、Race ID検証までとする。domain writeを同じcatchで囲まず、write failureを
Location failureへ変換しない。検証済みpageへのcore writeは1回だけ行い、失敗後に完全探索して二重writeしない。

Locationは同一Race resourceに保持しつつ`ArtifactKind`を付け、Card URLをResult stage、Result URLをCard stageで
成功扱いにしない。legacy Locationはread-only表示ではUnknownとし、実行時のpage identity検証でのみ昇格する。

### Domain write and crash recovery

- Card/Result core writeはstage別の安定idempotency keyを使う。
- domain commit後のAPI応答消失は同じwriteを安全に再送し、追加event 0を保証する。
- write receipt取得後、facet checkpoint前のlease expiryでもstale workerは状態更新できない。
- 再実行はdomain実体またはidempotent receiptを照合し、facetを前進させる。
- Attemptはappend-only、facetはlease token/dispatch generation/attempt numberでfenceし、Currentから退行させない。
- 関連主体bulkのisolated failureは対象/error codeを残すが、成功したCard core facetを巻き戻さない。

## Scheduling and efficiency

- Race resourceごとにactive task最大1件なので、Card/Result分離によるジョブ増殖はない。
- 同日Raceは既存RaceDay Envelopeへまとめ、1 Lambda・1 browser sessionを共有する。
- Envelope内は競馬場・レース番号順、各Race内はdue stage順に処理する。
- 未来の発走待ちはoutboxへ出さず、`AvailableAt`到達後だけRealtimeへ上げる。
- Card公開待ち・StartTime不足をRealtime/100で反復しない。
- current-page shortcut、no-wait Snapshot、1 page逐次操作、bulk domain write、関連主体1 batchを維持する。
- NetworkIdle、Load、固定sleep、parallel pageを導入しない。

## Attempt and failure semantics

既存`AttemptCount`は総実行回数の監査値として維持する。stage outcomeから次を別集計する。

- AvailabilityCheckCount
- TechnicalFailureCount
- CardWriteFailureCount
- ResultWriteFailureCount

AttemptのトップレベルresultだけでRaceの取得状態を決めない。詳細にはstage、requested/final URL、page identity、
write receipt、error impact、次のstage/時刻を残す。

## Legacy compatibility and no-recovery boundary

### Allowed migration

facet/evidence/typed outcome/ArtifactKind付きLocationに必要なadditive schema migrationは許可する。旧rowは変更せず、
nullable/default Unknownとする。enum ordinalを挿入変更せず、stable codeまたは末尾追加を使う。

### Prohibited recovery mutation

本変更では既存taskのDefinition、Status、Priority、AvailableAt、Notificationを変更せず、Recovery task、request、outbox、
backup fileを生成しない。旧terminal taskを再開しない。read-only auditからapply/requeueへ接続しない。

既存active taskは既に保存されたscheduleに従って継続するが、自然に実行された時だけ明確なバグ修正と新しいfacet checkpointを
適用する。特別な再投入・前倒し・優先度変更は行わない。

### Controlled deployment

1. API first: additive schema、old/new completion受理、Unknown対応read pathを配備する。
2. UI/read model: facet表示を配備する。旧dataは書き戻さずlegacy判定不能を表示する。
3. Collector: feature flag offで新state machineを配備し、旧completion互換を確認する。
4. Version matrix: 旧Collector→新API、新Collector→新API、in-flight旧completion、rollbackを検証する。
5. Activation: 新規に作成するRace taskだけrevision 2を使う。既存taskをrevision 2へ自動upgradeしない。
6. Observe: 新規Raceでジョブ数、stage、発走前navigation、性能、failure分類を確認する。

## Read-only audit and UI

一覧はRaceを1行のまま維持し、Card/Resultの2列またはcompact statusを表示する。詳細はfacetごとの最終取得、次回予定、
公開確認、技術失敗を表示する。availability待ちにはretryを促さず、同revisionの決定的failureへgeneric requeueを出さない。

auditはGET/read-onlyのみで、pause中にも実行でき、AsOf、JST期間、total、pagination、truncatedを返す。実行前後で
task/request/attempt/outbox/facet/location/priority/notification/pipeline control/domain row/backup fileが不変であることを検証する。

Loading、空、API失敗、legacy判定不能、部分取得、大量データ、狭幅、keyboard操作を既存Fluent patternで扱う。

## Mocks

- [Raceリソース詳細のfacet表示](mocks/job-detail-phases.md)

## Alternatives considered

- **Card/Resultを別Definition/Taskにする**: 状態は単純だが同一Raceのジョブを増やし、priority、一覧、通知、Recovery判断が重複するため撤回。
- **Taskにだけphase列を追加する**: Task再作成や履歴でresource completenessを失い、domain dataとの二重正本になるため不採用。
- **Race resource facet + child task**: facetは正しくてもジョブ数が増えるため不採用。
- **Race resource facet + 1 persistent task**: resource truthと実行制御を分離でき、ジョブ増殖せずsession高速化も維持できるため採用。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 同一Raceのactive `race-detail` taskは全stageを通して最大1件で、Card/Result用の追加taskを作らない。 | T1, T3 | store/concurrency E2E | Not started |
| AC2 | Card/Result facetはRace resource配下に独立保存され、Result成功だけでCardがCurrentにならず、Card失敗でもResultを保存できる。 | T1, T2, T3 | persistence/handler E2E | Not started |
| AC3 | card error、result availability、result errorはstage outcomeとしてappend-onlyに残り、後続stageが前段原因を上書きしない。 | T2, T3 | production-shaped failure test | Not started |
| AC4 | 公式StartTime+grace前のResult navigation/API callは0。時刻不明でも9:35 pollingへ落ちず、公式evidenceを待つ。 | T2, T3 | fake clock/navigation count | Not started |
| AC5 | 後続discoveryで得た公式StartTimeがresource evidenceへ反映され、active taskはimmutableな旧metadataでなく最新evidenceからAvailableAtを決める。 | T3 | store/scheduler integration | Not started |
| AC6 | Location評価とdomain writeが分離され、write失敗でLocationをSuspectにせず、完全探索や2回目writeを行わない。 | T2 | handler/navigation tests | Not started |
| AC7 | domain commit/API応答消失/checkpoint応答消失/lease expiry/duplicate/stale completion後も追加event 0、facet退行0で再開する。 | T2, T3, T6 | crash/restart matrix | Not started |
| AC8 | 関連主体のisolated failureは対象/error codeを残すがCard core facetを巻き戻さず、同じ関連taskを増殖させない。 | T2, T3 | partial failure/idempotency | Not started |
| AC9 | 24 Raceのmixed Envelopeでbrowser生成1、共有session、未到来Result browser work 0、p95回帰5%以内、14分上限へ2分以上余裕を維持する。 | T3, T6 | fixed corpus performance | Not started |
| AC10 | 一覧・詳細はRaceを1件のまま表示し、Execution/Card/Result/Operational actionと総実行/公開確認/技術失敗を区別する。 | T4, T5 | API/bUnit/browser | Not started |
| AC11 | legacy task/attempt/locationはUnknownを含め履歴保持され、新taskへの分割、再開、再投入、priority変更、通知解決を行わない。 | T3, T5, T7 | before/after DB invariant | Not started |
| AC12 | audit GET前後で全collection/domain/control rowとbackup fileが不変で、auditからapply/requeueへ遷移しない。 | T5, T6 | no-mutation integration | Not started |
| AC13 | API-first配備、old/new completion、in-flight task、feature flag、rollbackが成功し、既存active taskをrevision 2へ自動upgradeしない。 | T3, T6, T7 | compatibility matrix | Not started |
| AC14 | availability待ちにmanual retryを促さず、同revisionの決定的failureをgeneric requeueできない。監視は通知してもRecoveryを作らない。 | T4, T5 | API/component/monitoring tests | Not started |
| AC15 | 配備後30分以上の新規実行で発走前Resultアクセス、error masking、高頻度loop、同一Raceの複数active taskがない。既存エラー件数は自動変更されない。 | T7 | production observation | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Race facet/evidence/state machine/typed outcome契約を確定する（AC1–AC15）。 | Main | High capability | - | contracts、change record | design review | frozen contract | Verified |
| T2 | handlerをstage分離し、Location/write分離、structured error、冪等writeを実装する（AC2–AC8）。 | Main | High capability | T1 | Scraping、Collector、tests | handler/transport E2E | typed stage outcome | Verified |
| T3 | facet store、planner、schedule、single active guard、completion fencingを実装する（AC1–AC9, AC11, AC13）。 | Main | High capability | T1, T2 | CollectionOperations、API、tests | store/concurrency/crash tests | 1 Race 1 active task | Verified |
| T4 | Race一覧・詳細のfacet表示を実装する（AC10, AC14）。 | Main | High capability | T3 | Admin API/UI/tests | bUnit/browser | 1行＋facet表示 | Verified |
| T5 | read-only auditとRecovery導線隔離を実装する（AC10–AC12, AC14）。 | Main | High capability | T3 | query API/UI/tests | no-mutation tests | auditのみ | Verified |
| T6 | crash/restart、性能、全回帰、version matrixを検証する（AC1–AC14）。 | Main | High capability | T2–T5 | tests、verification record | format/build/non-External/E2E | regression evidence | Verified |
| T7 | controlled activation、新規Race観測、read-only audit、文書同期を行う（AC11, AC13, AC15）。 | Main | High capability | T6 | deployment/production/docs | CI/CD、production evidence | 無増殖・無自動復旧 | Runnable |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** 本番72件、現行Race resource/active guard、handler/store/scheduler、RaceDay Envelope/session共有を照合した。Card/Result別Task案はユーザー指摘どおり同一Raceのjobを増殖させるため撤回した。Task-only phase案もresource truthを持てないため不採用とし、Race facetを正本、単一persistent taskをcontrollerとする案を採用した。AC1–AC15をT1–T7へ追跡した。
- **Independent architecture review.** Definition分離案とstage案を比較。現行session/fencingを維持できるstage案の証拠と、Definition分離の状態独立性をRace facetへ統合した。
- **Independent risk review.** phase所有者、crash window、no-recovery entry point、schema/data migration、version skewのblocking指摘をfacet正本、no-mutation AC、API-first cutoverへ反映した。
- **Pre-implementation review — 2026-09-19.** T1–T6を実装対象、T7の本番配備・観測を後続のpush/deploy対象として分類した。
- **Checkpoint review — 2026-09-19.** additive schema v15、completion transaction、revision 2 request、handler stage、一覧・詳細表示を順にビルド・テストした。
- **Final review — 2026-09-19.** single-active guardを維持し、子Task生成なし、9:35 fallback削除、Card/Result独立facet、stage append-only、Location/write catch分離、revision 1 active task非昇格をsourceとテストで確認した。本番AC15は未配備のため未観測。

### Design research routing

| Task | Owner/tier | Scope and evidence | State | Lead decision |
| --- | --- | --- | --- | --- |
| R1 境界案比較 | Worker / high capability | read-only。Definition/Resource/saga案、現行Envelope証拠 | Verified | job増殖懸念を受けfacetへ統合 |
| R2 性能・冪等監査 | Worker / high capability | read-only。typed stage、Location/write、startTime、session、性能gate | Verified | stage実行モデルとgateを採用 |
| R3 永続化・cutoverレビュー | Review / high capability | read-only。phase ownership、crash、version skew、no-mutation | Verified | Resource facet正本へ反映 |

利用量・費用は取得不能。3件とも再試行0、repository write 0。主担当がsourceと要件へ照合して採否を決定した。

## Documentation updates

- `docs/22-collector-design.md`: Race resource中心、single active task、facet state、typed stageを記載する。
- `docs/26-collection-platform-design.md`: resource completenessとtask lifecycleの分離、no-recovery境界を記載する。
- `docs/20-admin-ui-design.md`: Race 1行とCard/Result facet、回数分離、retry抑止を記載する。
- `docs/23-jra-scraping-redesign.md`: typed stage、Location/write分離、共有sessionを記載する。

## Verification record

- 2026-09-19: `dotnet test tests/HorseRacingPrediction.Collector.Tests/...` 261件成功。
- 2026-09-19: `dotnet test tests/HorseRacingPrediction.Api.Tests/...` 244件成功、既存skip 1件（最終再実行前の同一suiteでも成功）。
- 2026-09-19: `dotnet build src/HorseRacingPrediction.Api/... --no-restore` 警告0・エラー0。
- 2026-09-19: schema v15の新規作成・既存DB migration、並行初期化、facet/evidence/stage永続化をテストした。
- 2026-09-19: 既存エラーへのRecovery作成、再投入、priority変更、通知解決を実装・実行していない。

- 2026-09-19: 本番`20260919:Hanshin:10`の7 attemptがすべて`RaceResultNotYetAvailable`、次回14:15:10 JSTを確認した。
- 2026-09-19: 今週末72件は9月19日State Pending 17 / Collecting 1 / Current 6、9月20日・21日は各Pending 24を確認した。
- 2026-09-19: domain保存済みは9月19日14件、9月20日・21日は0件を確認した。
- 2026-09-19: `20260920:Hanshin:1`は1 attempt後、metadataにStartTimeなし、次回9月20日9:35を確認した。
- 2026-09-19: CodeGraph/sourceでcard error未検査、Location/write catch混同、9:35 fallback、active task metadata固定、RaceDay session共有を確認した。
- 2026-09-19: 独立監査3件を主担当が統合し、ユーザー指摘を受けDefinition分離案を撤回した。

## Deviations and follow-up

- 単一Taskへの単純なphase追加案とCard/Result別Task案は、いずれも本記録内で撤回した。
- 承認前のため、再実行、pause/resume、Recovery、data migration、production code変更は行っていない。
- 既存エラージョブの復旧は別途検討中の運用に委ね、本変更の完了条件へ含めない。
