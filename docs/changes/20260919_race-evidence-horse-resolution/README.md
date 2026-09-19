# レース根拠で同名競走馬を安全に解決する

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19
- JRA site contract impact: Updated — `docs/27-jra-site-collection-contract.md`へ、RaceResult馬名cellのHorse profile linkをidentityとして取得できること、年代により欠落し得ること、URL推測禁止を追記した。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 設計承認後に同定コンテキスト、候補検証、revisionを実装する。 |
| Verification | Not started | 同名馬、根拠レース、時系列矛盾、既存identity優先の反例テストが必要。 |
| Deployment/operation | Not started | 配備後に対象ジョブを新revisionでpreview/recoveryし、結果を確認する。 |

## Context

2026-09-19、`ロンドンコーリング` の horse-profile task はJRA公開検索で同名馬を2頭検出し、
`SubjectIdentification:MultipleCandidates` になった。候補の一方は2000年3月29日生まれの旧馬で、ユーザー確認では
抹消年月日が2003年、今回の起点は2026年のレースである。JRA公式の現行プロフィールには、別の同名馬として
2024年2月26日生まれの牝馬と2026年9月12日中山6Rの出走履歴が公開されている。

- 対象Resource: `horse-392840d9-3ba9-5665-943b-5a778da97f36`
- 現行コードではRace由来の子taskに `EffectiveDate` と `requestedByRaceId` を保存している。
- 一方、Horse同定へ渡す値は `name`、`birthDate`、`sourceIdentity` だけで、起点レースを使わない。
- `sourceIdentity` と `birthDate` がない場合、正規化名が同じ候補をすべて採用し、2件以上なら安全停止する。
- プロフィールparserは生年月日、抹消年月日を含む公開fieldと出走履歴を読み取れるが、同名候補の解決には使っていない。

## Goals

- Race由来taskに既に保存済みの起点レースを、同名馬の強い同定根拠として使用する。
- 現在・未来開催のRaceCard経路だけでなく、結果ページだけを取得する過去レースでも公式Horse identityを保持する。
- JRA公開プロフィールURLと生年月日の既存優先順位を維持する。
- 抹消年月日や生年月日が起点レース日と矛盾する候補を安全に除外する。
- 根拠レースとの一致を証明できない同名候補は、従来どおり自動選択しない。
- 対象の既存失敗を新revisionで再評価し、正しい現行馬へ収束させる。

## Non-goals

- 馬名、年齢の近さ、抹消日の近さだけによる推測選択はしない。
- identityが異なる同名馬をmergeしない。
- Race以外の血統Discovery等に、taskの便宜的な `EffectiveDate` を出走根拠として適用しない。
- JRA以外のプロフィール提供元や手動の本番データ書換えを追加しない。
- 旧attempt/failure履歴を書き換えない。

## Documentation updates

- `docs/22-collector-design.md`: JRA主体identityの正本へ、Race由来同名馬の根拠優先順位と安全停止条件を追記する。
- 本change record: 対象事象、設計、反例、受け入れ基準、実装・運用証拠の正本とする。
- `docs/27-jra-site-collection-contract.md`: RaceResult馬名cellのHorse profile linkを、馬主等のCard固有情報とは分離した主体identity取得元として明記し、過去レースでの欠落時fallbackとURL推測禁止を正本化する。

## Technical impact

### 同定根拠の優先順位

1. 正規化・検証済みのJRA Horse profile `sourceIdentity` がある場合は、従来どおりURL完全一致を使用する。
2. 生年月日がある場合は、従来どおりプロフィールの生年月日完全一致を使用する。
3. 過去レースの結果ページを含む起点Raceの該当馬番行に、JRA Horse profileへの検証可能なリンクがある場合は、
   Race ID、馬番、正規化馬名を相互検証して、そのリンクを `sourceIdentity` として採用する。
4. 上記を利用できず、かつ `discoveredFromType=Race`、`requestedByRaceId`、`EffectiveDate` が整合する場合だけ、
   同名候補プロフィールの全出走履歴を調べる。日付、競馬場、レース番号を含むJRA結果URLからcanonical Race IDを復元し、
   起点レースと完全一致する候補が1頭だけなら、そのプロフィールURLをidentityとして採用する。
5. 起点レースの完全一致が得られない場合、公開プロフィールの生年月日が起点レース日より後、または抹消年月日が
   起点レース日より前の候補は「時系列矛盾」として除外できる。ただし時系列上成立する候補が1頭になっても、
   根拠レースの完全一致なしには自動選択せず、診断情報を増やした `MultipleCandidates` とする。

時系列情報は誤候補を説明・除外する補助証拠であり、正しい候補の積極的証明には使わない。これにより今回の2003年抹消馬は
明確に不適格と表示できる一方、同時期に存在する同名馬を年代だけで誤選択しない。

### 実装境界

- `RaceResultPageParser` は結果行の馬名cellからJRA Horse profile linkを抽出し、既存の
  `RaceResultEntryBulkDto.HorseSourceIdentity` まで伝播する。結果のみを取得する過去レースでも、RaceCardと同じ
  canonical Horse ID、RaceEntry、horse-profile requestを作る。
- 現行の過去Result-only経路は結果保存後に `RequestReferencedSubjectsAsync` を呼ばないため、
  `RaceResultCollectionResult` または同等の検証済みsubject evidenceをRace handlerへ返し、結果保存成功後に
  Horse/Jockey/Trainerのrequest batchを作成する。CardとResultの両方がある場合はidentity付きHorseを優先して
  canonical payloadを1回だけ作り、同じRaceから二重taskを作らない。
- linkの採用にはJRA正規host・Horse profile path・非空の詳細parameter、該当Race ID、馬番、正規化馬名の一致を要求する。
  行にlinkがない古いレイアウトは正常なfallback対象とし、URLを組み立てて推測しない。
- Horse同定コンテキストへ `ReferenceRaceId` と `ReferenceRaceDate` を追加し、handlerでtask provenanceを検証して渡す。
- `requestedByRaceId` の文字列を無条件に信用せず、Resource種別、provider、`EffectiveDate`、Race IDの日付を相互検証する。
- 同名候補探索は既存の最大32候補・証拠サイズ上限を維持する。
- 候補履歴は現在の先頭ページだけで判断せず、対象日を見つけるか、日付降順の履歴が対象日を過ぎるまで全ページを
  boundedに辿る。古いレース、抹消済み馬、70戦超の履歴、履歴ページ境界を対象にし、循環検出と既存キャンセルを維持する。
- fallback探索には候補profile数、候補ごとの履歴page数、全体page数、経過時間の共有budgetを設ける。
  budget超過は候補不在と同一視せず `HistoricalRaceEvidenceBudgetExceeded` として安全停止し、同じrevisionで自動再試行しない。
- 過去レース結果が公開範囲外、結果行にlinkがない、プロフィール履歴から対象Raceを確認できない場合は
  `HistoricalRaceEvidenceUnavailable` として安全停止する。現役馬や最新候補へ置き換えない。
- 一意一致後も既存の名前・プロフィールURL検証を通し、保存時には採用したURLを `sourceIdentity` として保持する。
- 失敗診断には各候補のURL、公開生年月日、公開抹消年月日、起点レース一致有無、時系列矛盾理由をboundedな構造化情報として残す。
- Horse profile definitionを新revisionへ上げ、旧 `MultipleCandidates` のうちRace provenanceが完全な対象だけをpreview/recoveryする。
- identity付き再取得により既存のname由来Horse IDとcanonical Horse IDが分かれる場合、証明済みの起点RaceEntryだけを
  canonical Horseへ冪等に付け替える。旧Horseに他RaceEntry等の参照が残る場合はmerge/redirectせず要確認にし、
  参照がなくなった場合だけ既存repair境界で旧task抑止、failure解決、redirect/ledgerを行う。

## Decisions

1. 今回の正しい候補は、2026年9月12日の起点レースを出走履歴に持つJRA現行プロフィールである。
2. 抹消年月日は候補排除には使えるが、候補採用の単独根拠にはしない。
3. `EffectiveDate` 単独ではRace provenanceにならない。`requestedByRaceId` と発見元属性の整合を必須にする。
4. source identityがある通常経路を時系列fallbackで上書きしない。
5. exact race evidenceが得られない場合は安全停止を維持する。
6. 過去レースでは取得時点の年齢や現在の現役状態ではなく、対象開催日の公式結果行とプロフィール履歴を根拠にする。

### Rejected alternatives

- **抹消日が最も新しい候補を選ぶ:** 同時期の同名馬や抹消日欠落で誤選択するため不採用。
- **レース日より後に抹消された唯一の候補を選ぶ:** 将来生まれ、未出走、別の同名現役馬も成立し得るため不採用。
- **今回のHorse IDだけへURLを手入力する:** 再発防止にならず、旧task provenanceを活用できないため不採用。
- **同名2頭をmergeする:** 異なるJRA identityの別馬でありデータ破壊になるため不採用。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | taskの `EffectiveDate` はRace以外のDiscoveryでも設定される。 | 便宜日を出走日と誤認すると別馬を選ぶ。 | Race発見元、canonical `requestedByRaceId`、日付一致の3条件を必須にする。 | AC2/T1/T3 | Agree | Approved 2026-09-19 | Resolved in design |
| C2 | 抹消日以前には複数の同名馬が存在し得る。 | 時系列だけでは積極的同定にならない。 | exact race-history一致だけを採用根拠にし、抹消日・生年月日は矛盾排除と診断に限定する。 | AC3-AC5/T2/T3 | Agree | Approved 2026-09-19 | Resolved in design |
| C3 | 古いレースはプロフィール履歴の後続ページにある。 | 先頭ページだけではfalse negativeになる。 | 日付順と循環を検証しながらboundedにページ送りする。取得不能時は安全停止する。 | AC4/AC7/T2/T3 | Agree | Approved 2026-09-19 | Resolved in design |
| C4 | 保存済みURLの一時失敗後にfallbackすると強いidentityを失う。 | 一時障害が誤った別候補探索へ退行し得る。 | URL失敗は記録し、fallbackでも既知URLと異なる候補をexact race evidenceなしに採用しない。 | AC1/AC5/T2/T3 | Agree | Approved 2026-09-19 | Resolved in design |
| C5 | 対象画面は認証が必要で、本turnではtask metadataの実値をUIから取得できていない。コード上はRace子taskに根拠が保存されるが、対象legacy taskに揃うかは未確認。 | 対象だけは自動復旧対象外になる可能性がある。 | 実装前後のpreviewで3 provenance属性を確認し、不足なら起点RaceResult/Cardからidentityを再取得する別候補として表示し、推測適用しない。 | AC8/T4 | Agree | Approved 2026-09-19 | Resolved in design |
| C6 | 過去レースはRaceCardが取得対象外または非公開で、プロフィール履歴も後続ページに分割される。 | 現行開催向けの先頭ページだけの設計では、過去収集が失敗または同名誤選択になる。 | 結果行の公式Horse linkを第一根拠として保存し、欠落時は全履歴のexact Race照合へfallbackする。双方で証明できない場合は安全停止する。 | AC3-AC7/T1-T4 | Agree | Approved 2026-09-19 | Resolved in design |
| C7 | 過去結果の性齢から生年を逆算すると、旧年齢表記や開催日境界の影響を受ける。 | 古いレースで別世代の同名馬を誤選択し得る。 | 性齢は診断表示に限定し、source linkまたはexact Race一致の代用にしない。 | AC4-AC6/T2,T3 | Agree | Approved 2026-09-19 | Resolved in design |
| C8 | 現行Result-only経路は結果保存後に主体request batchを作らず、`RaceResultCollectionResult`も検証済みsubject行を返さない。 | parser/DTOへlinkを足すだけでは過去レースのhorse-profile収集に接続されない。 | 結果保存→検証済みsubject evidence返却→重複排除したrequest batchまでを同じACでend-to-end検証する。 | AC3/AC10/T1,T3 | Agree | Approved 2026-09-19 | Resolved in design |
| C9 | 同名候補は最大32件で、各プロフィール履歴は複数ページになり得る。 | 1 taskが大量のJRA navigationを発生させ、timeoutやアクセス負荷を増やす。 | profile/page/timeの共有budgetを設け、超過は非retryの識別不能として記録する。通常のidentity付き経路には追加アクセスしない。 | AC7/AC11/T2,T3 | Agree | Approved 2026-09-19 | Resolved in design |
| C10 | identity取得でname由来Horseとは別のcanonical Horse IDが生成される。 | 正しい新taskが成功しても、旧RaceEntry・旧failure・旧Horseが残り二重表示になり得る。 | 証明済みRaceEntryだけを付替え、参照残存時はmergeしない。参照ゼロ時だけ既存repairのredirect/ledger境界で閉じる。 | AC9/AC12/T3,T4 | Agree | Approved 2026-09-19 | Resolved in design |
| C11 | 過去年代のRaceResult馬名cellにHorse linkが常にあることは未確認であり、JRA公式案内は結果掲載範囲を示してもDOM要素を保証しない。 | 外部E2Eを確認せず実装すると年代別layoutで欠落・誤linkを見逃す。 | 直近、2000年代、可能な最古年代の実画面を確認し、fixture化する。link欠落年代は正式fallbackとして扱い、取得可を保証しない。 | AC3/AC10/T1,T3 | Agree | Approved 2026-09-19 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 検証済みsource identityまたは生年月日があるtaskは既存根拠を優先し、race fallbackで別URLを選ばない。 | T1,T2,T3 | navigator/handler regression tests | Not started |
| AC2 | Race由来であること、canonical Race ID、EffectiveDateが相互整合しないtaskはrace evidenceを使用しない。 | T1,T3 | provenance counterexample tests | Not started |
| AC3 | 結果だけを取得する過去レースでも、結果行の検証済みHorse linkがRaceResult保存、canonical RaceEntry、horse-profile requestまで失われず伝播する。 | T1,T3 | historical result fixture through parser/handler/API persistence | Not started |
| AC4 | 結果行linkを利用できない場合、同名候補のうち起点レースの日付・場・番号に完全一致する公開履歴を持つ候補が1頭だけなら、そのURLをidentityとして収集成功する。 | T2,T3 | duplicate candidate fixture and handler integration | Not started |
| AC5 | 起点レース一致が0件または複数件なら自動選択せず、候補ごとの一致・時系列矛盾・過去証拠取得不能理由をboundedに記録する。 | T2,T3 | zero/multiple/unavailable/page-boundary tests | Not started |
| AC6 | 抹消年月日がレース前、生年月日がレース後の候補は矛盾と判定するが、それだけで残り候補を成功扱いしない。結果ページの性齢から逆算した生年も採用根拠にしない。 | T2,T3 | 2003/2026, future-born and old-age-notation counterexamples | Not started |
| AC7 | 先頭ページより古いレース、70戦超、抹消済み馬、履歴ページ境界、循環、候補上限、キャンセル、JRA外結果を安全に処理し、アクセス量は同名fallback対象に限定される。 | T2,T3 | historical navigation bounds and cancellation tests | Not started |
| AC8 | 新revisionのpreviewで対象ロンドンコーリングtaskのprovenanceを確認し、安全条件を満たす場合だけ1件のRecoveryを作成する。満たさない場合は理由付きで未適用にする。 | T4 | production preview/apply evidence | Not started |
| AC9 | 既存の同名別馬、RaceEntry、旧failure履歴はmerge・削除・書換えされず、過去RaceEntryは再取得時も同じcanonical Horseへ収束する。 | T3,T4 | persistence diff, idempotent reacquisition and production post-check | Not started |
| AC10 | Result-onlyの実経路で、結果行linkがparser、workflow result、API保存、RaceEntry、重複排除されたsubject request、horse-profile handlerまで接続される。Card併用時も同じHorse taskを二重作成しない。 | T1,T3 | transport/persistence/dispatch end-to-end test | Not started |
| AC11 | fallback探索はprofile/page/timeの共有budget内で動作し、超過を候補なしや一時障害へ丸めず、同revisionで自動再試行しない。identity付き通常経路の追加navigationは0である。 | T2,T3 | budget, telemetry and retry-classification tests | Not started |
| AC12 | name由来旧Horseとcanonical Horseが分かれる場合、証明済みRaceEntryだけが冪等に付け替わる。旧Horseに参照が残ればmerge/redirectせず、参照ゼロの場合だけ旧task/failureが既存ledger付きrepairで閉じる。 | T3,T4 | repair preview/apply, reference and repeat-run tests | Not started |

## Delivery plan

1. 現在・過去のRaceCard/ResultからHorse identityを抽出し、task provenanceへ安全に接続する。
2. 結果行linkがない場合のexact race-history照合、時系列矛盾診断、bounded候補証拠を実装する。
3. 反例テスト、既存回帰、format/buildを通す。
4. 新revisionを配備し、対象をpreviewして安全条件を満たす場合だけRecoveryする。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | RaceCard/過去RaceResultのHorse linkとRace provenanceを検証し、Result-only主体requestまで伝播する。 | Main | Lead tier | Approval | Race result parser/model/workflow、Collector handler | historical parser/transport/dispatch tests | AC1-AC3,AC10 | In progress |
| T2 | link欠落時の同名候補exact race照合、時系列矛盾診断、共有探索budgetを実装する。 | Main | Lead tier | T1 | Navigator、subject model/parser、failure evidence | historical navigator/budget fixtures | AC4-AC7,AC11 | Dependent |
| T3 | 過去開催、反例、主体dispatch、Horse ID分岐、冪等再取得、統合、回帰、format/buildを検証する。 | Main | Lead/review tier | T1,T2 | tests | focused/full CI-equivalent gates | AC1-AC7,AC9-AC12 | Dependent |
| T4 | revision更新、repair preview、配備、対象Recovery、過去再取得post-checkを行う。 | Main | Lead/review tier | T3 | definition、repair、change record、production operation | preview/apply evidence | AC8-AC9,AC12 | Dependent |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** CodeGraph indexは利用不能だったため、handler、navigator、parser、task producer、UI/store、既存change recordを直接追跡した。2件の読み取り専用調査を分離し、主担当が結果を統合した。ACはproducer provenance、候補照合、反例、安全な本番Recoveryへ接続した。identityと本番データ完全性に関わるため実装はMainが直列所有する。
- **Concern and agreement review — 2026-09-19, reviewer: Main.** source identity優先、Race provenance検証、時系列だけで選択しないこと、過去RaceResultの公式link伝播、Result-only主体dispatch、全履歴ページングと共有budget、旧年齢表記、Horse ID分岐後のrepair、年代別実画面証拠、対象task metadata未確認を材料化した。Open decisionはなく、C1-C11は設計内で安全側へ解決した。ユーザー承認は未取得。
- **Pre-implementation review — 2026-09-19, reviewer: Main.** ユーザーの「対応をお願いします」を、直前に要約した設計、AC1-AC12、C1-C11の処置に対する明示承認として記録した。T1をIn progress、T2-T4を依存順にDependentとした。T1はparser/model/workflow/handlerの共有contractをMainが直列所有する。並行委譲は書込なしのテスト・経路棚卸しに限定し、identity、persistence、repair、本番操作の判断はMainが保持する。各taskはfocused testと実transport証拠が得られなければVerifiedにしない。
- **Checkpoint review:** 実装中に実施する。
- **Final review:** 全ACと本番post-check後に実施する。

## Verification record

- 2026-09-19: `JraRaceCollectionHandlers` がRace子taskへ `EffectiveDate`、`requestedByRaceId`、利用可能なら `sourceIdentity` を保存することを確認した。
- 2026-09-19: `JraSubjectCollectionHandler` と `JraNavigator.FindHorseAsync` は起点レースを読まず、URL、生年月日、名前だけで候補を判定することを確認した。
- 2026-09-19: JRA公式プロフィール `pw01dud102024102539/E3` に2024年2月26日生まれのロンドンコーリングと2026年9月12日中山6Rの履歴があることを確認した。
- 2026-09-19: 対象管理画面はログインを要求したため、本turnでは対象task metadataの実値をUI確認していない。コード経路と公開JRA情報から設計し、本番previewをAC8の必須gateとした。
- 2026-09-19: production codeは変更していない。
- 2026-09-19: ユーザー指摘を受け、過去レースを結果ページだけから取得する経路を追加調査した。契約には
  `RaceResultEntryBulkDto.HorseSourceIdentity` が既にある一方、`RaceResultEntry` とparserは結果行のHorse linkを保持していないことを確認し、結果行linkの抽出・伝播を第一経路、全プロフィール履歴照合をfallbackとする設計へ更新した。
- 2026-09-19: 追加レビューで、Result-only分岐は `RequestReferencedSubjectsAsync` を呼ばず、workflow resultも主体行を返さない接続欠落を確認した。さらに最大32候補×複数履歴pageの負荷、name由来Horse IDの残存、年代別Horse linkの外部証拠不足をmaterial concernとして追加し、AC10-AC12とC8-C11で閉じた。

## Deviations and follow-up

- なし。承認前のため実装・本番操作は未実施。
