# 週末レース情報の期限内収集を監視する

- Status: Proposed
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 期限内充足率の評価とfinding生成は未実装。 |
| Verification | Not started | 金曜出馬表、レース後結果、欠落・遅延fixtureが必要。 |
| Deployment/operation | Not started | 承認後に30分監視へ接続する。 |

## Context

現行監視は失敗、pipeline停止、task停滞、配送順違反を検出するが、処理が動いていても必要なレース情報が期限までにdomainへ保存されているかを判定しない。このため「監視成功」と「利用目的を満たす収集成功」が一致しない。

2026-09-19 21:05 JSTの本番read-only調査では、9月19日分は中山12・阪神12の計24レースすべてで出走頭数と結果が保存され、結果保存時刻は12:49–16:41 JSTだった。一方、翌9月20日分は公式discovery由来の `race-detail` taskが24件存在するにもかかわらず、race summaryは0件だった。代表の中山8Rは9月17日18:35 JSTに一度実行され、`RaceNotStarted` として9月20日9:35 JSTまで待機しているが、domainのレース・出走馬情報を確認できない。金曜時点の出走馬充足要件を満たしていない。

## Goals

- 正常判定に、週末レースの出馬表と結果の期限内充足率を含める。
- 公式discoveryで発見した対象を分母にし、固定の競馬場数やレース数に依存しない。
- 欠落と遅延を、pipeline/taskの稼働状態とは独立したfindingとして通知・起票する。
- 欠落時に、未発見、task未作成、task待機、card未保存、result未保存を区別する。

## Non-goals

- JRA公開前の情報を欠落と判定すること。
- レース数を常に12R×固定競馬場として扱うこと。
- 未確認の欠落に対するデータ補正、強制再実行、pipeline操作。
- 予測モデルの精度や、馬・騎手・調教師プロフィールの完全性を同じSLOへ含めること。

## Freshness contract

```text
公式開催・race discovery
        │
        ├─ 金曜18:00 JST ──> 週末の全発見レースに出馬表・出走馬があるか
        │                         └─ 不足: WeekendCardCoverageMissing
        │
        └─ 各レース発走予定 + 30分 ──> 公式結果がdomainへ保存されたか
                                  └─ 不足: RaceResultFreshnessMiss

開催日18:30 JST ──> 当日全レースの結果充足率を再集計
                       └─ 100%未満: RaceDayResultCoverageMissing
```

### Friday card checkpoint

- 毎週金曜18:00 JST以降、直後の土曜・日曜について評価する。時刻は設定値とする。
- 分母は、公式calendar/race list discoveryで発見され、`race-detail` resource/taskが作成されたレースのdistinct canonical resource IDとする。
- 分子は、domain race summaryが存在し、`EntryCount > 0` で、collection artifactのCardがpersist済みのレースとする。
- discovery自体が開催を列挙できていない場合は充足率100%とせず、`WeekendDiscoveryCoverageUnknown` とする。
- 18:00時点で100%未満ならHigh、21:00時点でも未解消ならCriticalとして通知する。

### Post-race result checkpoint

- 各レースの公式発走予定時刻に30分のgraceを加えた時刻以降に評価する。
- 分母は当該時刻を過ぎた発見済みレース、分子は `ResultDeclaredAt` がありResult artifactがpersist済みのレースとする。
- 期限超過30分以内はMedium、30分超または開催日18:30 JST時点の未取得はHighとする。
- 中止・取消など公式にレース全体が不成立の場合は、`NotApplicable` の公式根拠が保存されていれば充足として数える。

### Evidence and lifecycle

findingには対象日、評価時刻、期限、分母・分子・欠落数、欠落race ID、task/artifact状態、最新attempt、classifier versionを含める。同じ対象日・checkpointのfingerprintへ継続観測を集約し、100%到達時に正常化を一度通知する。race名、entry名や外部ログ本文は必要最小限にし、秘密情報を含めない。

## Response policy

- discovery済みでcard未保存だがtaskが未来の結果確認まで待機している場合は、program bug候補として修正用change recordを起票する。
- due taskの配送遅延なら運用conditionとしてlane/capacityを調査する。
- 未発見ならcalendar/discovery経路を調査する。
- 結果公開待ちはgrace内なら正常、期限後は再取得候補を提示する。
- 自動再実行は、別途承認された冪等recipeと回数上限がある場合だけ行う。

## Technical impact

- Monitoring snapshotへ、対象期間のrace resources、Card/Result artifact、task/attempt、公式発走予定、domain race summaryの読み取りprojectionを追加する。
- `CollectionMonitoringService`へ `WeekendDiscoveryCoverageUnknown`、`WeekendCardCoverageMissing`、`RaceResultFreshnessMiss`、`RaceDayResultCoverageMissing` を追加する。
- 30分監視に加え、金曜18:00/21:00と開催日18:30のcheckpointを同じ決定的evaluatorで評価する。
- 通知とchange record writerは既存fingerprint/lifecycle契約を再利用する。

## Documentation updates

- `docs/11-automation-design.md`: 正常判定へ期限内データ充足を加え、本recordを詳細契約の正本としてリンクする。
- `docs/26-collection-platform-design.md`: 実装承認後にsnapshot/artifactの永続評価契約を反映する。現時点では本recordが提案の正本であるため変更しない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 金曜18:00 JST以降、直後の週末についてdiscovery済みレース数、出馬表保存数、出走馬保存数、欠落raceを報告する。 | T1,T2 | Friday boundary fixtures and API integration test | Not started |
| AC2 | discoveryが不完全または未実行の場合、0/0を正常とせずcoverage unknownとして通知する。 | T1,T2 | empty/partial discovery tests | Not started |
| AC3 | 各レースの発走予定+30分と開催日18:30で結果保存を評価し、未取得raceと遅延時間を報告する。 | T1,T2 | clock-controlled result fixtures | Not started |
| AC4 | 取消・中止、延期、開催中止を公式状態に従って分母または充足へ正しく反映する。 | T1 | cancellation/postponement tests | Not started |
| AC5 | 同一日・checkpointの継続欠落を重複起票せず、解消時に一度だけ正常化通知する。 | T2,T3 | lifecycle/idempotency tests | Not started |
| AC6 | 評価は読み取り専用で、未承認のtask再実行、pipeline操作、データ補正を行わない。 | T1-T3 | before/after persistence assertions | Not started |
| AC7 | 2026-09-19の本番形状fixtureで土曜24/24のcard/resultを正常、日曜0/24のdomain cardをHigh異常と判定する。 | T1-T3 | production-shaped regression fixture | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | coverage snapshot、期限設定、4 findingの決定的評価を実装する。AC1-AC4,AC6,AC7 | Main | Lead tier | Approval | CollectionOperations/API monitoring and tests | evaluator/store tests | boundary and denominator evidence | Proposed |
| T2 | monitoring endpoint、writer、通知へcoverage evidenceとlifecycleを接続する。AC1-AC3,AC5-AC7 | Main | Lead tier | T1 | API/tooling/automation tests | integration/golden tests | generated finding and notification | Dependent |
| T3 | 本番read-only shadow、金曜/開催後checkpoint、正常化を検証し文書を更新する。AC5-AC7 | Main + operator | Lead tier | T1,T2 | automation/docs | production shadow and secret scan | scheduled evidence | Dependent |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** 現行monitoring evaluator、race discovery/detail、domain race summary、artifact/task状態、本番read-only evidenceを照合した。分母を固定レース数ではなく公式discovery由来resourceとし、金曜cardと発走後resultを別checkpointにする。snapshot/evaluator/APIは同じ状態契約を扱うためMainが直列実装する。
- **Pre-implementation review:** 利用者によるAC1-AC7の明示承認後に行う。
- **Checkpoint review:** evaluator、endpoint/notification、本番shadowごとに実diffとAC matrixを照合する。
- **Final review:** AC1-AC7、読み取り専用境界、金曜/発走後fixture、重複抑止、正常化、本番shadowを照合する。

## Verification record

- 2026-09-19 21:05 JST: 本番race summaryをread-only確認。9月19日は24レース、EntryCountあり24、ResultDeclaredAtあり24。中山12/12、阪神12/12。
- 同時刻: 9月20日のrace summaryは0件。一方、task searchでは `race-detail` が24件存在し、すべて翌日9:35 JSTを次回時刻とするReady状態だった。
- 代表の中山8Rは9月17日18:35 JSTのattemptが `RaceNotStarted` で終了し、required revision 1、applied revision 0、last collectedなし。金曜時点でdomainのcard/entriesを確認できない。
- 現行監視はtask停止・滞留を検出するが、上記のdomain completenessをfindingにしないことを確認した。

## Human decision required

- 金曜出馬表checkpointを18:00 JST、結果graceを発走予定+30分、日次結果checkpointを18:30 JSTとする設計を承認するか。
- 承認前は監視コード、収集コード、データ、task状態を変更しない。
