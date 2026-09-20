# JRA出馬表から結果へ堅牢に遷移する

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-20
- Updated: 2026-09-20

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | In progress | 利用者承認後、T1から依存順に実装する。 |
| Verification | Not started | production-shaped fixture、handler統合、既存Navigator回帰が必要。 |
| Deployment/operation | Not started | code配備後にread-only smokeを行い、個別Recoveryは別の明示判断とする。 |

## Context

productionの`20260919:Nakayama:3`はdomainに出走馬12頭、結果12頭、結果確定時刻を保持する一方、
revision 2 recoveryが`accessS.html#`のRaceListから対象3Rへ遷移できず
`JraCollectionException`で失敗した。Resourceには同一RaceのCard URLとResult URLが保存されていた。

現行`JraRaceDetailCollectionHandler`は、Card取得が不要な場合だけResult location候補を直接使用する。
Card取得が必要なattemptでは、同じsessionの`ToRaceResultAsync`へ委ね、出馬表上のshortcutに失敗すると
開催選択→Race番号、Recent、Historicalへフォールバックする。このため、具体的なResult URLが保存済みでも
汎用一覧探索へ退行し、一覧構造・掲載範囲・リンク文言の変化を受ける。

JRA公式のResultページには同一開催の出馬表・払戻・別Raceへの導線があり、具体的な
`accessS.html?CNAME=...`がRace identityを持つ。曜日やURL文字列だけを信頼せず、実ページのkindと
Race identityを必ず検証する。

## Goals

1. 同一attemptで取得した出馬表が示すResult URLを第一候補にする。
2. 保存済みの同一Race Result locationをCard要否にかかわらず使用する。
3. 具体的URLが使えない場合だけ、既存のCurrent/Recent/Historical遷移へフォールバックする。
4. 既存の正常なCard-only、Result-only、同一ページshortcut、過去結果検索を維持する。
5. URL候補の失敗を型付きで分類し、誤Race保存、無制限fallback、同revision無限retryを防ぐ。

## Non-goals

- JRAの非公開API、内部JSON、URL推測による結果取得は追加しない。
- Parserの結果項目、domain結果モデル、払戻確定条件は変更しない。
- 本承認にproduction個別Recovery、優先度変更、履歴削除を含めない。
- Horse/Jockey/Trainer/Owner recoveryの契約変更は含めない。

## Proposed transition

Resultが公式StartTime+graceでdueになった後、次の順で最大一つの成功ページを選ぶ。

1. **Current Card evidence** — 同一attemptでRace identityを検証済みのCardから、semantic snapshotで
   明示的なResult linkを抽出する。URLをJRA host、`accessS.html`、CNAME内の日付・場・Race番号で検証する。
2. **Persisted Result location** — `Artifact=Result`のActive候補を優先し、legacyの`Artifact=null`でも
   `JraRaceDetailUrl.Validate(..., RaceResult, expectedRace)`を満たす候補はResultとして扱う。
3. **Current-page shortcut compatibility** — 現在ページが同一Race Cardで、明示Result URLを抽出できなかった
   既存fixtureに限り、現行のsemantic link操作を一度だけ試す。
4. **Official selection fallback** — 上記が存在しない、またはページ不存在・掲載範囲外の場合だけ、現行の
   Current meeting→Recent meeting→Historical searchを使用する。

各候補はnavigate後に`JraRaceResultPage`かつ`page.RaceId == expectedRace`を満たして初めて成功とする。
Cardが必要だったかどうかはResult候補利用の条件にしない。

### Failure policy

| Evidence | Classification | Next transition |
| --- | --- | --- |
| 同一RaceのResult page | Succeeded | 保存へ進み、Result locationをActive/Resultにする。 |
| Result未公開を示す正規ページ | ResourceNotYetAvailable | fallbackで公開済みになる根拠はないため待機する。 |
| 404または掲載範囲外 | ResourceNotFound / OutOfDisplayedRange | 次の異なる公式導線へ一度だけ進む。 |
| 別Race・別page kind | UnexpectedPage / IdentityMismatch | 候補を非Activeにし、次候補へ進む。保存は禁止。 |
| timeout、5xx | TransientFailure | 同一attemptの追加navigationを止め、backoffする。 |
| 429・アクセス制限 | AccessLimited | 追加navigationを止め、長いbackoffにする。 |
| 同一Race Resultのparse/contract failure | Deterministic Blocked | 汎用一覧へ逃げずBlockedとして通知する。 |

候補数とfallbackをattempt単位で上限化し、具体URLが一つ成功した後に汎用探索を実行しない。

## Compatibility invariants

- Result locationしかない過去Raceは、従来どおりCardを探索せず直接Resultを取得できる。
- Cardしかない当日Raceは、Card保存後、StartTime+graceまでResult navigationを0件に保つ。
- 同一CardページのResult linkで成功する既存経路は維持する。
- 具体URLがないCurrent/Recent/Historical Raceは現行Navigator fallbackを維持する。
- Card失敗とResult成功、Card成功とResult未公開を独立facetとして保存する。
- 既にResult facetがCurrentでrequired revisionを満たす場合、関連主体RecoveryのためにResultを再取得しない。
- facetが欠落していてもdomainに完全なCard/Resultがあり、同一性とrevision provenanceを証明できる場合だけ、
  read-model reconciliationでfacetを復元する。推測だけでCurrentにしない。

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: 具体Result evidenceを汎用一覧より優先する提案へのリンクを追加する。
- `docs/27-jra-site-collection-contract.md`: Card→Resultの候補優先順位とidentity validationを本変更の提案として案内する。
- `docs/26-collection-platform-design.md`: 承認後、location artifact分類とfacet reconciliationが状態機械の現行契約を変える場合だけ更新する。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 具体Result URLを文字列だけで信用すると別Raceや古い導線を保存し得る。 | 誤Race結果は重大なデータ汚染。 | host/path/CNAME事前検証とpage kind/RaceId事後検証を両方必須にする。 | AC1,AC4/T1,T2/identity mismatch | 必須 | Approved, 2026-09-20 | Resolved in design |
| C2 | timeout/429後もfallbackを連続するとJRA負荷と制限を悪化させる。 | cascading failure。 | transient/access failureではattemptを止め、候補fallbackは404・範囲外・identity mismatchに限定する。 | AC5/T2/http matrix | 必須 | Approved, 2026-09-20 | Resolved in design |
| C3 | 新しい直接URL優先で既存のCurrent/Recent/Historical正常経路を削る危険。 | 過去Raceやlegacy taskが退行。 | 既存Navigatorを最終fallbackとして保持し、既存fixtureを非回帰gateにする。 | AC2,AC3,AC7/T2,T3/legacy fixtures | 必須 | Approved, 2026-09-20 | Resolved in design |
| C4 | facet未生成Raceを監視対象外にすると、取得失敗が解消したように見える。 | 偽陰性。 | 公式Card/Result locationまたはdomain evidenceがあるfacet欠落をcoverage findingに含める。 | AC6/T3/production-shaped monitor | 必須 | Approved, 2026-09-20 | Resolved in design |
| C5 | domain結果完成済みの関連主体Recoveryが結果再取得を行っている。 | 不要なJRAアクセスと偽のRace失敗。 | Result Currentまたは証明済みreconciliation後はResult stageをskipし、関連主体結果とRace facetを分離する。 | AC6,AC8/T2,T3/completed-domain counterexample | 必須 | Approved, 2026-09-20 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 同一attemptのCardが有効なResult URLを示す場合、汎用開催選択を開かず、そのURLから同一Race Resultを保存する。 | T1,T2 | Card→Result production-shaped handler E2E | Not started |
| AC2 | Active/legacy-nullの保存済みResult URLが同一Raceとして検証できる場合、Card要否に関係なく直接利用する。 | T1,T2 | persisted-location matrix | Not started |
| AC3 | 具体Result URLがない場合、既存Current/Recent/Historical経路が従来どおり成功する。 | T2,T3 | existing Navigator regression suite | Not started |
| AC4 | 別Race、Card URL、未知host、壊れたCNAMEをResultとして保存せず、候補状態と診断を残す。 | T1,T2 | URL/page identity negative matrix | Not started |
| AC5 | 未公開、404/範囲外、timeout/5xx、429、parse failureが設計表どおり待機・限定fallback・backoff・Blockedになる。 | T2,T3 | fake-clock/http/store integration | Not started |
| AC6 | CardとResultのfacetが独立更新され、facet欠落がcoverage監視から消えず、完成済みResultは関連主体Recoveryで再取得されない。 | T2,T3 | store/monitor/domain-complete counterexample | Not started |
| AC7 | Card-only、Result-only、同一Card shortcut、過去結果検索、取消、未確定結果の既存正常テストがすべて成功する。 | T3 | targeted plus non-External regression | Not started |
| AC8 | `20260919:Nakayama:3`相当fixtureで、結果12件完成後の関連主体RecoveryがJRA Result navigation 0件で終わり、Race本体をFailedにしない。 | T2,T3 | production-shaped recovery E2E | Not started |
| AC9 | production個別Recoveryを実装検証に混ぜず、配備後read-only smokeの合格条件と別承認のRecovery条件を記録する。 | T4 | deployment/operation checklist | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | Routing | Audit | Result metrics | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Card/Result URL evidenceとartifact分類契約を実装する。AC1,AC2,AC4 | Main | Lead | Approval | Scraping page/parser、Collector URL classifier、tests | parser/URL identity matrix | typed candidates | Lead public contract ownership | none | unavailable; retries 0; corrections 0; reviews 0 | In progress |
| T2 | race-detailのResult遷移と完成済みResult skipを実装する。AC1-AC6,AC8 | Main | Lead | T1 | Collector handler、workflow contract、tests | handler/store production-shaped E2E | bounded transition | Lead integration of shared state machine | none | unavailable; retries 0; corrections 0; reviews 0 | Dependent |
| T3 | 既存Navigator非回帰とfacet/monitor偽陰性を閉じる。AC3,AC5-AC8 | Main | Lead | T2 | Navigator、CollectionOperations、Monitoring、tests | targeted and non-External solution tests | compatibility evidence | Lead integration and final acceptance risk | none | unavailable; retries 0; corrections 0; reviews 0 | Dependent |
| T4 | 配備後read-only smokeと個別Recoveryの別承認条件を固定する。AC9 | Main | Lead | T3 | change record/docs only | read-only production checklist | safe operation boundary | Lead final acceptance ownership | none | unavailable; retries 0; corrections 0; reviews 0 | Dependent |

## Review gates

- **Design and task-split review — 2026-09-20, reviewer: Main.** URL evidence、handler遷移、facet/monitor、運用確認は同じRace状態機械を共有し、分離した並列書込は競合するためMain/Leadが依存順に保持する。既存Navigatorは置換せずfallbackとして維持する。
- **Concern and agreement review — 2026-09-20, reviewer: Main.** 誤Race保存、JRA過負荷、legacy退行、monitor偽陰性、完成済みResult再取得をmaterial concernとしてC1-C5へ記録した。設計上のOpen decisionはない。利用者のdispositionと明示承認を待つ。
- **Pre-implementation review — 2026-09-20, reviewer: Main.** 利用者がAC1-AC9とC1-C5を明示承認した。T1をIn progress、T2-T4をDependentとする。T1は`JraRaceDetailUrl`/page evidence tests、T2は`JraDirectCollectionHandlerTests`とstore integration、T3はNavigator既存suite・monitor evaluator・非External全回帰、T4はread-only smoke条件とする。identity不一致、429/5xx、既存fallback退行、facet誤Currentが出た場合は次taskへ進まず修正する。

## Verification record

- 2026-09-20: production read-onlyで`20260919:Nakayama:3`のCard/Result URL、domain entry/result各12件、revision 2の`accessS.html#` RaceList失敗、facet欠落を確認した。
- 2026-09-20: 現行handlerは`!requiresCard`の場合だけResult locationを直接使うこと、`ToRaceResultAsync`が同一ページshortcut後にCurrent/Recent/Historicalへfallbackすることを確認した。
- 2026-09-20: JRA公式Resultページが同一開催の出馬表・払戻・Race選択への導線を持つことを確認した。

## Approval request

承認対象はAC1-AC9とC1-C5の処置に限定する。承認によりURL候補分類、Result遷移、facet/monitor、対応テストと文書更新の実装を開始する。production個別Recovery、priority変更、履歴削除は承認対象外とする。
