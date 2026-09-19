# レース根拠で同名競走馬を安全に解決する

- Status: Proposed
- Change record schema: 2
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19
- JRA site contract impact: None — 既存のプロフィールfieldと出走履歴を利用し、取得元・要素・fallback契約は変更しない。

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
- `docs/27-jra-site-collection-contract.md` はサイト要素の抽出契約を変更せず、既存のプロフィールfield・履歴を利用するため更新不要と判断した。

## Technical impact

### 同定根拠の優先順位

1. 正規化・検証済みのJRA Horse profile `sourceIdentity` がある場合は、従来どおりURL完全一致を使用する。
2. 生年月日がある場合は、従来どおりプロフィールの生年月日完全一致を使用する。
3. 両方を利用できず、かつ `discoveredFromType=Race`、`requestedByRaceId`、`EffectiveDate` が整合する場合だけ、
   同名候補プロフィールの出走履歴を調べる。日付、競馬場、レース番号を含むJRA結果URLからcanonical Race IDを復元し、
   起点レースと完全一致する候補が1頭だけなら、そのプロフィールURLをidentityとして採用する。
4. 起点レースの完全一致が得られない場合、公開プロフィールの生年月日が起点レース日より後、または抹消年月日が
   起点レース日より前の候補は「時系列矛盾」として除外できる。ただし時系列上成立する候補が1頭になっても、
   根拠レースの完全一致なしには自動選択せず、診断情報を増やした `MultipleCandidates` とする。

時系列情報は誤候補を説明・除外する補助証拠であり、正しい候補の積極的証明には使わない。これにより今回の2003年抹消馬は
明確に不適格と表示できる一方、同時期に存在する同名馬を年代だけで誤選択しない。

### 実装境界

- Horse同定コンテキストへ `ReferenceRaceId` と `ReferenceRaceDate` を追加し、handlerでtask provenanceを検証して渡す。
- `requestedByRaceId` の文字列を無条件に信用せず、Resource種別、provider、`EffectiveDate`、Race IDの日付を相互検証する。
- 同名候補探索は既存の最大32候補・証拠サイズ上限を維持する。
- 候補履歴は対象日を見つけるか、日付降順の履歴が対象日を過ぎるまでページ送りする。循環検出と既存キャンセルを維持する。
- 一意一致後も既存の名前・プロフィールURL検証を通し、保存時には採用したURLを `sourceIdentity` として保持する。
- 失敗診断には各候補のURL、公開生年月日、公開抹消年月日、起点レース一致有無、時系列矛盾理由をboundedな構造化情報として残す。
- Horse profile definitionを新revisionへ上げ、旧 `MultipleCandidates` のうちRace provenanceが完全な対象だけをpreview/recoveryする。

## Decisions

1. 今回の正しい候補は、2026年9月12日の起点レースを出走履歴に持つJRA現行プロフィールである。
2. 抹消年月日は候補排除には使えるが、候補採用の単独根拠にはしない。
3. `EffectiveDate` 単独ではRace provenanceにならない。`requestedByRaceId` と発見元属性の整合を必須にする。
4. source identityがある通常経路を時系列fallbackで上書きしない。
5. exact race evidenceが得られない場合は安全停止を維持する。

### Rejected alternatives

- **抹消日が最も新しい候補を選ぶ:** 同時期の同名馬や抹消日欠落で誤選択するため不採用。
- **レース日より後に抹消された唯一の候補を選ぶ:** 将来生まれ、未出走、別の同名現役馬も成立し得るため不採用。
- **今回のHorse IDだけへURLを手入力する:** 再発防止にならず、旧task provenanceを活用できないため不採用。
- **同名2頭をmergeする:** 異なるJRA identityの別馬でありデータ破壊になるため不採用。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | taskの `EffectiveDate` はRace以外のDiscoveryでも設定される。 | 便宜日を出走日と誤認すると別馬を選ぶ。 | Race発見元、canonical `requestedByRaceId`、日付一致の3条件を必須にする。 | AC2/T1/T3 | Agree | Pending | Resolved in design |
| C2 | 抹消日以前には複数の同名馬が存在し得る。 | 時系列だけでは積極的同定にならない。 | exact race-history一致だけを採用根拠にし、抹消日・生年月日は矛盾排除と診断に限定する。 | AC3-AC5/T2/T3 | Agree | Pending | Resolved in design |
| C3 | 古いレースはプロフィール履歴の後続ページにある。 | 先頭ページだけではfalse negativeになる。 | 日付順と循環を検証しながらboundedにページ送りする。取得不能時は安全停止する。 | AC4/AC6/T2/T3 | Agree | Pending | Resolved in design |
| C4 | 保存済みURLの一時失敗後にfallbackすると強いidentityを失う。 | 一時障害が誤った別候補探索へ退行し得る。 | URL失敗は記録し、fallbackでも既知URLと異なる候補をexact race evidenceなしに採用しない。 | AC1/AC5/T2/T3 | Agree | Pending | Resolved in design |
| C5 | 対象画面は認証が必要で、本turnではtask metadataの実値をUIから取得できていない。コード上はRace子taskに根拠が保存されるが、対象legacy taskに揃うかは未確認。 | 対象だけは自動復旧対象外になる可能性がある。 | 実装前後のpreviewで3 provenance属性を確認し、不足なら新しいRaceCardからidentityを復元する別候補として表示し、推測適用しない。 | AC7/T4 | Agree | Pending | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 検証済みsource identityまたは生年月日があるtaskは既存根拠を優先し、race fallbackで別URLを選ばない。 | T1,T2,T3 | navigator/handler regression tests | Not started |
| AC2 | Race由来であること、canonical Race ID、EffectiveDateが相互整合しないtaskはrace evidenceを使用しない。 | T1,T3 | provenance counterexample tests | Not started |
| AC3 | 同名候補のうち起点レースの日付・場・番号に完全一致する公開履歴を持つ候補が1頭だけなら、そのURLをidentityとして収集成功する。 | T2,T3 | duplicate candidate fixture and handler integration | Not started |
| AC4 | 起点レース一致が0件または複数件なら自動選択せず、候補ごとの一致・時系列矛盾理由をboundedに記録する。 | T2,T3 | zero/multiple/page-boundary tests | Not started |
| AC5 | 抹消年月日がレース前、生年月日がレース後の候補は矛盾と判定するが、それだけで残り候補を成功扱いしない。 | T2,T3 | 2003/2026 and future-born counterexamples | Not started |
| AC6 | 履歴の複数ページ、循環、候補上限、キャンセル、JRA外結果を安全に処理し、アクセス量は同名fallback対象に限定される。 | T2,T3 | navigation bounds and cancellation tests | Not started |
| AC7 | 新revisionのpreviewで対象ロンドンコーリングtaskのprovenanceを確認し、安全条件を満たす場合だけ1件のRecoveryを作成する。満たさない場合は理由付きで未適用にする。 | T4 | production preview/apply evidence | Not started |
| AC8 | 既存の同名別馬、RaceEntry、旧failure履歴はmerge・削除・書換えされない。 | T3,T4 | persistence diff and production post-check | Not started |

## Delivery plan

1. task provenanceを同定コンテキストへ安全に接続する。
2. exact race-history照合、時系列矛盾診断、bounded候補証拠を実装する。
3. 反例テスト、既存回帰、format/buildを通す。
4. 新revisionを配備し、対象をpreviewして安全条件を満たす場合だけRecoveryする。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Race provenanceを検証しHorse同定コンテキストへ渡す。 | Main | Lead tier | Approval | Collector handler、Scraping model | handler/provenance tests | AC1-AC2 | Proposed |
| T2 | 同名候補のexact race照合と時系列矛盾診断を実装する。 | Main | Lead tier | T1 | Navigator、parser、failure evidence | navigator fixtures | AC3-AC6 | Proposed |
| T3 | 反例、統合、回帰、format/buildを検証する。 | Main | Lead/review tier | T1,T2 | tests | focused/full CI-equivalent gates | AC1-AC6,AC8 | Proposed |
| T4 | revision更新、preview、配備、対象Recovery、post-checkを行う。 | Main | Lead/review tier | T3 | definition、change record、production operation | preview/apply evidence | AC7-AC8 | Proposed |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** CodeGraph indexは利用不能だったため、handler、navigator、parser、task producer、UI/store、既存change recordを直接追跡した。2件の読み取り専用調査を分離し、主担当が結果を統合した。ACはproducer provenance、候補照合、反例、安全な本番Recoveryへ接続した。identityと本番データ完全性に関わるため実装はMainが直列所有する。
- **Concern and agreement review — 2026-09-19, reviewer: Main.** source identity優先、Race provenance検証、時系列だけで選択しないこと、履歴ページング、対象task metadata未確認を材料化した。Open decisionはなく、C1-C5は設計内で安全側へ解決した。ユーザー承認は未取得。
- **Pre-implementation review:** 承認後に実施する。
- **Checkpoint review:** 実装中に実施する。
- **Final review:** 全ACと本番post-check後に実施する。

## Verification record

- 2026-09-19: `JraRaceCollectionHandlers` がRace子taskへ `EffectiveDate`、`requestedByRaceId`、利用可能なら `sourceIdentity` を保存することを確認した。
- 2026-09-19: `JraSubjectCollectionHandler` と `JraNavigator.FindHorseAsync` は起点レースを読まず、URL、生年月日、名前だけで候補を判定することを確認した。
- 2026-09-19: JRA公式プロフィール `pw01dud102024102539/E3` に2024年2月26日生まれのロンドンコーリングと2026年9月12日中山6Rの履歴があることを確認した。
- 2026-09-19: 対象管理画面はログインを要求したため、本turnでは対象task metadataの実値をUI確認していない。コード経路と公開JRA情報から設計し、本番previewをAC7の必須gateとした。
- 2026-09-19: production codeは変更していない。

## Deviations and follow-up

- なし。承認前のため実装・本番操作は未実施。
