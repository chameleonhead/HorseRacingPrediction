# JRA公開状態駆動のRace取得

- Status: Proposed
- Change record schema: 2
- Owner: Main
- Created: 2026-09-20
- Updated: 2026-09-20
- JRA site contract impact: Proposed — 曜日固定ではなく公式開催日とCard/Result公開証拠を取得契約にする。

## Context

2026-09-20のproduction read-only snapshotで、対象日24 Raceのcardが6件だけ保存され、18件が
`WeekendCardCoverageMissing` (`f9acf64f21c590dc`) となった。代表attemptはCardページまで到達したが、
card-only payloadが未確定のentry resultを作り `Entry results require a declared race result.` で拒否された。
さらに後続の`RaceNotStarted`がcard失敗を上書きし、次回実行が発走時刻付近まで延期された。

既存の[Raceリソース中心の取得状態機械](../20260919_race-detail-phase-recovery/README.md)がCard/Result facetと
single active taskを定めている。本変更はその代替ではなく、未完了のT7 activationとbulk write契約を、
曜日非依存の公閏証拠駆動として閉じる。

## JRA official evidence

- JRA公式FAQでは、通常の出馬情報は木曜16時すぎ、馬・枠番号付き出馬表は原則レース前日10時すぎに発表される。
  GI・一部重賞、3日間開催の月曜日重賞、年始、代替競馬は別時刻になり得る。JRAサイト反映に約15分かかる場合がある。
  <https://www.jra.go.jp/faq/pop02/2_2.html>
- 2026年の公式発表表も、一般競走は実施日前日10時すぎとしつつ、重賞ごとに木曜14時、金曜9時、10時などの例外を持つ。
  <https://www.jra.go.jp/keiba/program/2026/pdf/touroku-touhyou01.pdf>
- JRA公式の開催日程は、予定後の取消・延期・発走時刻変更があるため、最新の出馬表で確認するよう案内している。
  <https://www.jra.go.jp/keiba/calendar2026/2026/6/0607.html>

したがって「金曜に土日分を取る」は監視checkpointには使えても、取得ロジックの正本にはできない。
正本は公式calendar/race listから得た開催日、Card link/page identity、公式発走時刻、Result link/page identityとする。

## Proposed design

1. `race-discovery`は曜日を判定せず、公式calendarの開催日と実動窓内のrace listを列挙する。土日、月曜祝日、代替開催、年始を同じ経路で扱う。
2. race listがCard linkとRace identityを返した時点でCard facetをdueにする。木曜/金曜という曜日でdueを決めない。
3. Card未公開は`AwaitingPublication`とし、JRA公式の通常窓はprobe開始のhintにだけ使う。実際のlink/page identityが真実の根拠であり、例外開催でも同じ状態遷移にする。
4. card-only write contractはEntriesとCard固有値だけを受け取り、未確定の`EntryResults`を作らない。Result writeは明示的な結果証拠があるentryだけを受け取る。
5. Card core write、関連主体job、Result core writeを別outcomeにする。関連主体の部分失敗でCurrent Cardを巻き戻さない。
6. Resultは曜日ではなく最新の公式StartTime+graceでdueにする。CardがBlockedでもResultは独立して取得できる。
7. Card/Resultのavailability、retryable failure、deterministic failureをtyped outcomeで分ける。availabilityは上限付き低頻度retry、retryable failureはbackoff、deterministic failureはBlockedとし同revisionを無限再試行しない。
8. 監視は`Weekend*`を取得条件に使わず、開催日ごの`RaceCardCoverageMissing`と`RaceResultCoverageMissing`を公式evidence基準で評価する。既存fingerprintはlifecycle互換を保つ。

## Non-goals and safety boundary

- 本承認はproduction deploy、pipeline pause/resume、既存taskの強制再実行、priority/AvailableAt書き換え、データ補正、履歴削除を許可しない。
- JRA非公閏API、内部JSON、推測URLを新しい取得元にしない。
- 過去Cardの無制限探索や、Result/Horse profileからCard固有値を推測しない。

## Documentation updates

- `docs/27-jra-site-collection-contract.md`: 公式calendarと実page evidenceを正本とする曜日非依存契約の提案リンクを追加する。承認前のため現行動作は変更しない。
- その他の正本文書は、承認後の実装と同時に現行契約へ更新する。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Recommended disposition | AC/task/counterexample | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | JRA公式も通常時刻に複数例外があると明記。 | 曜日/固定時刻は取得漏れを生む。 | 固定時刻はprobe hintに限定し、公式calendar/link/page identityを正本にする。 | AC1,AC2/T1,T3/月曜・代替開催 | 必須 | Pending | Resolved in design |
| C2 | `IsRaceCard` だけでresult全破棄するとhybrid callerの結果を沈黙破棄する。 | 結果欠損 | Card/Result commandを明示分離するか、矛盾payloadをvalidation errorにする。 | AC3/T2/hybrid payload | 沈黙破棄不可 | Pending | Resolved in design |
| C3 | bulk responseのErrorsにcore writeと関連jobが混在。 | Card成功をBlockedと誤判定。 | typed stage outcomeとcore receiptを別記録し、関連job失敗でCardを退行させない。 | AC4/T2/subject partial failure | 必須 | Pending | Resolved in design |
| C4 | availabilityとdeterministic failureの一律retryは無駄な負荷または取得放棄を生む。 | JRA負荷、欠落固定 | typed policy、backoff、deadline、Blocked通知を独立テストする。 | AC5,AC6/T3/未公開・HTTP一過性・validation | 必須 | Pending | Resolved in design |
| C5 | 既存production taskは旧scheduleを保持。 | code deployだけで現findingが解消しない可能性。 | 実装と復旧を分離し、本承認では復旧mutationを許可しない。 | AC9/T5/legacy task | 安全境界を維持 | Pending | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 土・日・月曜祝日・平日・代替開催を同じcalendar→discovery→race-detail経路で列挙し、曜日による除外がない。 | T1,T3 | clock-controlled calendar/discovery E2E | Not started |
| AC2 | Card link/page identityの公閏後にCardがdueとなり、公閏前は低頻度待機、取得後はCard facet `Current`となる。 | T1,T3 | 通常・GI例外・3日間開催fixture | Not started |
| AC3 | card-only writeはEntriesだけ、result writeは明示的なEntryResultsだけを保存し、矛盾hybrid payloadを沈黙破棄しない。 | T2 | API transport integration and validation tests | Not started |
| AC4 | Card core成功後の関連主体失敗はCardを巻き戻さず、core failureは後続result waitに上書きされない。 | T2,T3 | partial-failure and masking counterexamples | Not started |
| AC5 | availability、retryable failure、deterministic failureが別のstage outcome/facetとなり、backoffとBlockedが契約どおりに決まる。 | T3 | scheduler/store/handler integration | Not started |
| AC6 | Resultは最新公式StartTime+graceまでnavigation 0、以後にdueとなり、Card BlockedでもResult保存が成功する。 | T3 | fake-clock E2E and Card-blocked counterexample | Not started |
| AC7 | 開催日ごのCard/Result coverageが独立評価され、0/0を正常にせず、既存finding lifecycleを重複起票しない。 | T4 | evaluator/store/API tests | Not started |
| AC8 | 同一Raceのactive `race-detail` taskは最大1件、duplicate/response loss/lease expiry後も追加event 0、facet退行0。 | T3 | concurrency/crash matrix | Not started |
| AC9 | 実装検証でproduction task、pipeline、domain data、historyを変更せず、deploy/recoveryを別承認に保つ。 | T5 | before/after invariant and read-only audit | Not started |
| AC10 | fixed corpusの共有browser sessionと14分上限を維持し、未来Resultのbrowser workは0。 | T3,T5 | performance regression | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | JRA公式evidenceと曜日非依discovery/due契約を固定する。AC1,AC2 | Main | Lead | Approval | Contracts, canonical docs, fixtures | official-source review and fixtures | frozen evidence contract | Proposed |
| T2 | Card/Result bulk commandとcore/related outcomeを分離する。AC3,AC4 | Main | Lead | T1 | API/Application/Collector/tests | transport and partial-failure tests | no implicit results/no rollback | Dependent |
| T3 | facet planner、typed retry、single-task controllerを完結させる。AC1,AC2,AC4-AC6,AC8,AC10 | Main | Lead | T1,T2 | CollectionOperations/Collector/tests | scheduler/store/handler E2E | weekday-independent progression | Dependent |
| T4 | 開催日ベースのcoverage monitorとlifecycle互換を実装する。AC7 | Main | Lead | T3 | Monitoring/tests/docs | evaluator/store/API | generic race-day findings | Dependent |
| T5 | 回帰・性能・read-only invariant・文書を最終照合する。AC9,AC10 | Main | Lead | T2-T4 | tests/docs only | CI-equivalent gates/read-only audit | approval-scope evidence | Dependent |

## Review gates

- **Design and task-split review — 2026-09-20, reviewer: Main.** 既存phase-recoveryのresource/facet/single-task設計を維持し、曜日非依存の公式evidence、bulk contract、typed retry、generic coverageに分割した。共有契約・状態機械・永続化・production整合性を跨ぐため実装と統合はMain/Leadが所有する。
- **Concern and agreement review — 2026-09-20, reviewer: Main.** 例外開催、payload沈黙破棄、部分成功、retry誤分類、legacy taskをmaterial concernとし、C1-C5の処置とACに反映した。Open decisionはないが、user dispositionは未承認。
- **Pre-implementation review:** 承認後にT1をRunnableとし、T2-T5を依存順に進める。実装前にテスファイルと最小/回帰コマンドを記録する。

## Verification record

- 2026-09-20: JRA公式FAQ、2026年出馬表発表表、公式開催日程を確認。通常窓と重賞・3日間開催・年始・代替開催の例外を設計に反映した。
- 2026-09-20: production GETで24 discovered / card current 6 / missing 18、代表の `RaceCardWriteRejected`、card失敗後のresult waitを確認。mutationは実施していない。
- 2026-09-20: 先行の暂定実装は、hybrid payloadと部分成功を正しく扱えないため取り下げ。production codeは承認前状態へ戻した。

## Approval request

本承認はAC1-AC10とC1-C5の処置に限定する。production deploy、既存taskの復旧、pipeline操作、データ補正、履歴削除は含まない。
