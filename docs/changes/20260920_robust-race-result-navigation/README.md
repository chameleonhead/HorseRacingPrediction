# JRA出馬表から結果へ堅牢に遷移する

- Status: Proposed
- Change record schema: 2
- Owner: Main
- Created: 2026-09-20
- Updated: 2026-09-20

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Correction not started | 既存実装は保存済みResult URLの優先には対応したが、実JRAのCardリンク構造、JavaScript race選択、重複requestへのResult URL統合を満たさない。 |
| Verification | Superseded | 旧fixture中心の検証は成功したが、production-shaped cold pathを再現しておらず、AC1/AC3等の完了証拠としては無効。 |
| Deployment/operation | Deployed defect observed | PR #61配備後の`20260920:Nakayama:6`で同じ失敗を再現。修正版配備・read-only smoke・個別Recoveryは未実施。 |

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

### 2026-09-20 production再調査で判明した未解決不具合

配備済みrevisionによる`20260920:Nakayama:6`の実行は、Card取得後に
`accessS.html#`のRaceListへ留まり、`Expected=RaceResult; Actual=RaceList`で失敗した。
Lambda更新時刻より後のattemptであるため、旧コンテナではなく配備済みコードの欠陥である。

1. 実CardではRace番号と`レース結果`が別要素で表現され、race固有リンクのlabelは
   `レース結果`だけである。現行selectorとfixtureは`6レース結果`のような合成labelを前提にし、
   正しい`accessS.html?CNAME=...`を見落とす。
2. fallbackのRace選択はJavaScript controlで、hrefの`#`は遷移先URLではない。現行実装はbase URLで
   解決した`accessS.html#`を通常URLとしてnavigateし、クリックによる状態遷移を実行しない。
3. discoveryが後からResult URLを発見しても、同一Race・同一revisionのrequest/taskが存在すると
   `RequestCoreAsync`はlane/priorityだけを更新してreturnし、明示URLをresource locationへ統合しない。
4. productionログには最終URLとpage kindしかなく、候補label、raw href、選択route、棄却理由を
   attemptへ残さないため、JRA構造変更の判別にAWSと実サイトの再調査が必要になる。

旧Final reviewは、実サイトの分離要素とcold-sessionのJavaScript導線をfixtureへ保持しなかったため
supersededとする。これは予見可能なfixture fidelity不足であり、既存のExternal adapter fidelity gateを
適用しなかったプロセス不履行として扱う。`20260919_collection-monitor-root-cause-triage/README.md`は
利用者指定どおり変更対象外とする。

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

- `docs/23-jra-scraping-redesign.md`: 分離要素からの直接Result選択、JS controlのclick、後発location mergeを現行提案として追記する。
- `docs/27-jra-site-collection-contract.md`: Card→Resultの候補優先順位、direct URLとclick targetの区別、identity validation、冪等location統合を外部サイト契約として追記する。
- `docs/26-collection-platform-design.md`: 承認後、location artifact分類とfacet reconciliationが状態機械の現行契約を変える場合だけ更新する。
- `docs/changes/20260919_collection-monitor-root-cause-triage/README.md`: 利用者指定により変更しない。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 具体Result URLを文字列だけで信用すると別Raceや古い導線を保存し得る。 | 誤Race結果は重大なデータ汚染。 | host/path/CNAME事前検証とpage kind/RaceId事後検証を両方必須にする。 | AC1,AC4/T1,T2/identity mismatch | 必須 | Approved, 2026-09-20 | Resolved in design |
| C2 | timeout/429後もfallbackを連続するとJRA負荷と制限を悪化させる。 | cascading failure。 | transient/access failureではattemptを止め、候補fallbackは404・範囲外・identity mismatchに限定する。 | AC5/T2/http matrix | 必須 | Approved, 2026-09-20 | Resolved in design |
| C3 | 新しい直接URL優先で既存のCurrent/Recent/Historical正常経路を削る危険。 | 過去Raceやlegacy taskが退行。 | 既存Navigatorを最終fallbackとして保持し、既存fixtureを非回帰gateにする。 | AC2,AC3,AC7/T2,T3/legacy fixtures | 必須 | Approved, 2026-09-20 | Resolved in design |
| C4 | facet未生成Raceを監視対象外にすると、取得失敗が解消したように見える。 | 偽陰性。 | 公式Card/Result locationまたはdomain evidenceがあるfacet欠落をcoverage findingに含める。 | AC6/T3/production-shaped monitor | 必須 | Approved, 2026-09-20 | Resolved in design |
| C5 | domain結果完成済みの関連主体Recoveryが結果再取得を行っている。 | 不要なJRAアクセスと偽のRace失敗。 | Result Currentまたは証明済みreconciliation後はResult stageをskipし、関連主体結果とRace facetを分離する。 | AC6,AC8/T2,T3/completed-domain counterexample | 必須 | Approved, 2026-09-20 | Resolved in design |
| C6 | 実CardではRace番号とResult種別が別要素で、合成label fixtureは本番構造を失っていた。 | 正しい直接Result URLを見落とし、壊れたfallbackへ進む。 | raw hrefを保持し、同一Card文脈の`accessS.html?CNAME`をexpected RaceIdで検証する。分離要素と誤候補先行fixtureを必須にする。 | AC1,AC4,AC10/T1,T4/production-shaped cold path | 必須 | Pending | Resolved in design |
| C7 | `#`はJavaScript controlとして有効だが、直接navigate先としては無効。 | RaceListに留まりResult未取得。 | direct URL候補とclick targetを型で分離し、fragment-only controlは要素clickする。遷移後のkind/RaceId検証は共通化する。 | AC3,AC5/T2,T4/JS control fixture | 必須 | Pending | Resolved in design |
| C8 | 重複requestは後発explicit Result URLを捨てる。 | Card-onlyの古いtaskが永久に汎用探索へ依存する。 | validated locationをresource単位で冪等upsertし、active/既存taskの有無にかかわらずCard/Result artifactをmergeする。競合・重複テストを行う。 | AC6/T3/store concurrency | 必須 | Pending | Resolved in design |
| C9 | 詳細な候補・route情報がattemptに残らない。 | 外部サイト変更と実装欠陥の切り分けが遅れる。 | secretを含めない構造化診断としてroute、label、raw/resolved URL、棄却理由、final kind/identityをログ・attemptへ保存する。 | AC8/T2,T3/diagnostic assertion | 必須 | Pending | Resolved in design |
| C10 | live JRAは変動し、live testだけでは再現性がない一方、fixtureだけでは現行互換を証明できない。 | 偽陽性または不安定なgate。 | sanitized production-shaped fixtureを決定的gate、現行JRAへのread-only bounded smokeをpre-release gateとし、両方を要求する。 | AC9,AC10/T4/CI+smoke | 必須 | Pending | Resolved in design |
| C11 | failed taskのRecoveryは外部状態を変更し、未配備revisionで再実行すれば同じ失敗を増やす。 | JRA負荷・履歴汚染。 | 修正版配備後にread-only smokeを先行し、対象task Recoveryは利用者の別の明示指示まで対象外とする。 | AC9/T5/operation checklist | 必須 | Pending | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | cold sessionの同一Race Cardで、Race番号要素とlabel=`レース結果`要素が分離していても、同じ文脈のvalidated `accessS.html?CNAME`を選び、汎用一覧を開かずResultを保存する。 | T1,T2,T4 | sanitized Card→Result handler E2E | Connected |
| AC2 | Active/legacy-nullの保存済みResult URLが同一Raceとして検証できる場合、Card要否に関係なく直接利用する。 | T1,T2 | persisted-location matrix | Verified |
| AC3 | 直接Result URLがない場合、fragment-only/JavaScript race controlをURL navigateせず正しい要素としてclickし、Current/Recent/Historical各経路で同一Race Resultへ到達する。 | T2,T4 | JS control and legacy route E2E | Connected |
| AC4 | global menu、別Race、Card URL、未知host、壊れたCNAMEを棄却し、誤候補が先にあっても正しい候補を選び、terminal kind/RaceId不一致を保存しない。 | T1,T2,T4 | ambiguous candidate negative matrix | Connected |
| AC5 | 未公開、404/範囲外、timeout/5xx、429、parse failureが設計表どおり待機・限定fallback・backoff・Blockedになり、同一失敗を無制限retryしない。 | T2,T4 | fake-clock/http/store integration | Connected |
| AC6 | discoveryが後からvalidated Result URLを発見したとき、既存request/taskの有無にかかわらずResult locationを冪等mergeし、Card/Result facet独立性と完成済みResult skipを維持する。 | T3,T4 | duplicate/active/concurrent store integration | Connected |
| AC7 | Card-only、Result-only、同一Card shortcut、過去結果検索、取消、未確定結果を含む既存正常遷移がすべて成功する。 | T4 | targeted plus non-External regression | Connected |
| AC8 | attempt診断からroute、候補label、raw/resolved URL、選択・棄却理由、requested/final URL、page kind、Race identityを追跡できる。 | T2,T3,T4 | persisted diagnostic assertions | Not started |
| AC9 | 修正版配備後、現行JRAの非破壊read-only smokeがcold/warm両経路で同一Race Resultを確認する。個別Recoveryは別の明示指示まで行わない。 | T5 | deployment/operation checklist | Not started |
| AC10 | 分離要素、誤候補先行、JS `#` controlを保持したproduction-shaped fixtureが修正前に失敗し修正後に成功し、通常CIとLinux runner相当のformat/build/non-External testが成功する。 | T4 | fixture regression and CI parity | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | Routing | Audit | Result metrics | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Card snapshotからexpected Raceの直接Result候補を抽出・検証する。AC1,AC2,AC4 | Main | Lead | Approval | Scraping snapshot/selector、URL identity、tests | separated/ambiguous candidate matrix | typed direct candidates | Lead external contract ownership | none | unavailable; retries 0; corrections 0; reviews 0 | Proposed |
| T2 | direct URLとJS click controlを分離し、全fallbackを共通terminal validationへ接続する。AC1,AC3-AC5,AC8 | Main | Lead | T1 | Navigator、browser interaction、handler diagnostics、tests | cold/warm navigation E2E | bounded validated transition | Lead browser/state-machine integration | none | unavailable; retries 0; corrections 0; reviews 0 | Proposed |
| T3 | 後発Result locationを既存resource/taskへ冪等mergeし診断を永続化する。AC6,AC8 | Main | Lead | T1 | Collection request/store/API、schemaが必要ならmigration、tests | duplicate/active/concurrent integration | persisted Result location | Lead persistence/concurrency ownership | none | unavailable; retries 0; corrections 0; reviews 0 | Proposed |
| T4 | production-shaped fixturesと全非回帰・CI parityを検証する。AC1,AC3-AC8,AC10 | Main | Lead | T2,T3 | tests/fixtures/change record | exact CI commands and counterexamples | green deterministic gates | Lead final acceptance ownership | none | unavailable; retries 0; corrections 0; reviews 0 | Proposed |
| T5 | 配備とread-only smokeを行い、Recovery境界を維持する。AC9 | Main | Lead | T4 | deployment/change record only | deploy status and current JRA cold/warm smoke | production compatibility evidence | Lead operation ownership | none | unavailable; retries 0; corrections 0; reviews 0 | Proposed |

## Review gates

- **Design and task-split review — 2026-09-20, reviewer: Main.** URL evidence、handler遷移、facet/monitor、運用確認は同じRace状態機械を共有し、分離した並列書込は競合するためMain/Leadが依存順に保持する。既存Navigatorは置換せずfallbackとして維持する。
- **Concern and agreement review — 2026-09-20, reviewer: Main.** 誤Race保存、JRA過負荷、legacy退行、monitor偽陰性、完成済みResult再取得をmaterial concernとしてC1-C5へ記録した。設計上のOpen decisionはない。利用者のdispositionと明示承認を待つ。
- **Pre-implementation review — 2026-09-20, reviewer: Main.** 利用者がAC1-AC9とC1-C5を明示承認した。T1をIn progress、T2-T4をDependentとする。T1は`JraRaceDetailUrl`/page evidence tests、T2は`JraDirectCollectionHandlerTests`とstore integration、T3はNavigator既存suite・monitor evaluator・非External全回帰、T4はread-only smoke条件とする。identity不一致、429/5xx、既存fallback退行、facet誤Currentが出た場合は次taskへ進まず修正する。
- **Final review — 2026-09-20, reviewer: Main（superseded）.** fixtureが実Cardの分離要素とJS controlを再現せず、production cold pathと後発location mergeも未検証だったため、完了判定を撤回した。
- **Correction design and task-split review — 2026-09-20, reviewer: Main.** selector、browser遷移、永続化はそれぞれ独立検証できるが、外部契約・状態機械・concurrencyを含むためMain/Leadが依存順に保持する。全ACにtaskと反例を割り当て、既存正常遷移をAC7で明示した。
- **Correction concern and agreement review — 2026-09-20, reviewer: Main.** C6-C11を追加した。設計上のOpen decisionはなく、利用者の明示承認を待つ。承認まではproduction codeを変更しない。

## Verification record

- 2026-09-20: production read-onlyで`20260919:Nakayama:3`のCard/Result URL、domain entry/result各12件、revision 2の`accessS.html#` RaceList失敗、facet欠落を確認した。
- 2026-09-20: 現行handlerは`!requiresCard`の場合だけResult locationを直接使うこと、`ToRaceResultAsync`が同一ページshortcut後にCurrent/Recent/Historicalへfallbackすることを確認した。
- 2026-09-20: JRA公式Resultページが同一開催の出馬表・払戻・Race選択への導線を持つことを確認した。
- 2026-09-20: `JraDirectCollectionHandlerTests` 28件、`CollectionMonitoringServiceTests` 17件が成功した。
- 2026-09-20: `dotnet test HorseRacingPrediction.sln --no-restore --filter "TestCategory!=External" -c Release` は成功1160件、既存skip 1件、失敗0件だった。
- 2026-09-20: production個別Recovery、priority変更、履歴削除は実施していない。配備後smokeは対象RaceのResult location/facet、最新attempt、monitor findingをread-onlyで確認し、個別Recoveryは別承認後に限る。
- 2026-09-20: PR #61配備後の`20260920:Nakayama:6` attemptが`accessS.html#` RaceListで失敗した。実Cardのrace固有`レース結果`リンクは`accessS.html?CNAME=...`で同一Race Resultへ成功した。
- 2026-09-20: selectorがrace番号labelとResult labelの同一URL groupを要求する一方、実リンクlabelは`レース結果`のみであること、fallbackがresolved `accessS.html#`をnavigateすることを確認した。
- 2026-09-20: ordinary duplicate requestはactive/existing taskを返す前に後発explicit URLをlocationへ統合しないことを確認した。

## Approval request

旧完了判定を撤回し、AC1-AC10とC1-C11のうち追加・再接続した条件について再承認を求める。
承認はdirect URL検出、JS click fallback、Result location merge、構造化診断、production-shaped回帰、
配備後read-only smokeを許可する。production個別Recovery、priority変更、履歴削除は含めない。
