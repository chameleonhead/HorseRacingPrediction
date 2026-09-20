# 登録済み主体名称の検索・選択補正ツール

- Status: Implemented
- Owner: Main
- Created: 2026-09-20
- Updated: 2026-09-20

## Context

競走馬・騎手・調教師には、JRA画面由来の登録区分、減量記号、所属表記、全半角差、余分な空白を含む名称が登録済みの場合がある。収集時は `JraSubjectNameNormalizer` を利用する経路が増えたが、既存主体を横断検索し、補正前後を確認して選択適用する運用画面はない。個別詳細画面のデータ補正では、対象IDを先に知り、正規化後の値を手入力する必要がある。

「その他設定」に、対象種別と検索語で登録済み主体を検索し、現在値と共通規則による補正後を比較し、安全な候補だけを選択して補正するツールを追加する。

## Goals

- 競走馬・騎手・調教師を、対象種別と名称またはIDでserver-side検索できる。
- 共通の `JraSubjectNameNormalizer` から、表示名と正規化名の補正案を生成して比較表示する。
- 利用者が候補を選び、確認Dialogを経て補正できる。
- 検索後にデータが変化した対象、同種別の別IDと正規化名が衝突する対象、変更のない対象を安全に除外する。
- 既存の補正イベントと監査理由を使用し、ID、alias、プロフィール、収集履歴を変更しない。

## Non-goals

- 馬主名称はOwner alias／統合モデルが異なるため対象外とする。
- 同じ正規化名を持つ別IDを自動統合しない。
- JRAサイトを検索せず、既存登録値だけから補正案を作る。
- IDの再生成、RaceEntry参照の付け替え、収集Taskの作成、productionへの自動適用は行わない。
- 任意の名称を自由入力して一括変更する汎用編集画面にはしない。

## User flow and UI design

1. `/settings` の「データ品質」内に「登録済み名称の正規化」を表示する。
2. 対象種別（競走馬・騎手・調教師）を必須選択し、名称またはIDを入力して「検索」する。
3. 検索結果をページングされた `FluentDataGrid` で表示する。列は選択、対象、現在の名称、補正後、判定の順とする。
4. 変更あり・衝突なし・検索後に未変更の候補だけを選択できる。「このページの補正可能候補を選択」を提供する。
5. 画面下部のPrimary Action「選択した名称を補正」で確認Dialogを開く。件数、対象種別、補正前後、IDや統合を変更しないことを表示する。
6. 適用後は成功・状態変化によるskip・失敗を区別して表示し、同じ検索条件で再検索する。

検索前、検索中、0件、変更候補なし、取得失敗、適用中、部分的な状態変化を別状態として表示する。狭幅では結果を既存Settingsのrepair-card patternへreflowし、対象、前後比較、判定、選択操作を保持する。キーボード操作、ラベル、focus、Dialogのfocus trapはFluent UI標準を利用する。

## Mock

- [その他設定: 登録済み名称の正規化](mocks/settings-subject-name-normalization.md)

## Data and API design

- `GET /api/admin/repairs/subject-name-normalization` に `subjectType`, `query`, `page`, `pageSize` を渡す。空検索による全件取得は許可せず、queryはtrim後1文字以上、page sizeは最大50とする。
- 検索対象は各read modelのID、表示名、正規化名。結果は現在値、補正案、変更有無、衝突ID、適用可否、安定したmanifest tokenを返す。
- 表示名の補正案は `CanonicalizeDisplayName`、正規化名は `NormalizeIdentityName` を使う。既存の個別補正APIと同じdomain correction commandを実行する専用bulk endpointを追加する。
- `POST /api/admin/repairs/subject-name-normalization/apply` は選択したID、現在値、補正案、manifest tokenを検証する。検索後に値が変化した対象はskipし、別IDとの衝突は拒否する。
- 各対象は既存の `HorseDataCorrected` / `JockeyDataCorrected` / `TrainerDataCorrected` を記録し、理由を「その他設定: 登録済み名称の正規化」とする。複数aggregateを跨ぐ全体transactionは約束せず、対象別結果を返して部分成功を明示する。
- 補正後の候補が空文字になる場合は適用不可。ID、alias、所属、性別、生年月日等は変更しない。

## Documentation updates

- `docs/20-admin-ui-design.md`: 「その他設定」のデータ品質ツールとして、検索→選択→確認→補正の導線、安全境界、対象種別を追記する。同文書を管理UIの正本として維持する。
- 本change recordとmockに、変更固有のAPI、状態、受け入れ条件を記録する。

## Decisions

- 対象はHorse/Jockey/Trainerに限定し、Ownerはalias統合の既存機能へ残す。
- 検索結果の全件自動補正ではなく、利用者が前後を比較して明示選択する。
- 正規化アルゴリズムを画面・APIへ複製せず、Contractsの共通normalizerを正本とする。
- 衝突候補は補正も統合も行わず、対象IDを表示して個別確認へ送る。
- 検索結果を適用契約として信用せず、apply時に現行値と衝突を再検証する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | その他設定で競走馬・騎手・調教師を選び、名称またはIDを入力すると、該当する登録済み主体が最大50件単位で検索・ページ移動できる。 | T1,T3 | API integration、component test | Verified |
| AC2 | 各結果に現在の表示名・正規化名、共通normalizerによる補正後、変更理由、適用可否が表示される。 | T1,T3 | normalizer fixtures、component test | Verified |
| AC3 | 変更あり・衝突なしの候補だけを個別またはページ単位で選択でき、確認Dialogから補正できる。 | T2,T3 | API/component/browser test | Verified |
| AC4 | 正規化後が空、変更なし、同種別の別IDと衝突する候補は選択不可で、理由と衝突先IDを確認できる。自動統合しない。 | T1,T2,T3 | collision/empty/no-op tests | Verified |
| AC5 | apply時に現行値を再検証し、検索後に変更された対象は上書きせずskipする。成功・skip・失敗を対象別に返し、再送しても追加変更しない。 | T2,T3 | concurrency/idempotency integration tests | Verified |
| AC6 | 補正は既存domain correction eventと固定監査理由を使い、ID、alias、プロフィール、所属等を変更しない。 | T2 | domain/API integration tests、event assertions | Verified |
| AC7 | Loading、検索前、0件、補正候補なし、検索失敗、適用失敗・部分成功を区別し、入力と選択を安全に維持または再評価できる。 | T3 | component tests | Verified |
| AC8 | desktopと狭幅で前後比較とPrimary Actionを失わず、label、keyboard、focus trapを含む操作性を確認する。 | T3 | component and browser verification | Verified |
| AC9 | 馬主、ID再生成、alias統合、収集Task、既存の無関係なSettings機能とproductionデータを変更しない。 | T1-T4 | diff/integration review | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 検索・preview契約、normalizer適用、衝突判定を実装する。 | Main | Lead — cross-subject query/public API contract | Approval | API contracts/endpoints、query tests | API integration tests | AC1,AC2,AC4 | Verified |
| T2 | manifest再検証と既存correction commandによる対象別applyを実装する。 | Main | Lead — data integrity/audit semantics | T1 | API/domain integration tests | concurrency/idempotency/event tests | AC3-AC6 | Verified |
| T3 | Settingsの検索・一覧・選択・確認・結果UIを実装する。 | Main | Lead — shared Settings file has existing uncommitted work and UI integration | T1,T2 | Settings/AdminApiClient/CSS/component tests | component/browser tests | AC1-AC5,AC7,AC8 | Verified |
| T4 | 正本文書、format/build/regression、実ブラウザー確認、最終レビューを行う。 | Main | Lead — integration/final acceptance | T1-T3 | docs/tests only | CI-equivalent gates | AC8,AC9 | Verified |

## Concern and agreement review

| Concern | Evidence / impact | Disposition | Linked criteria |
| --- | --- | --- | --- |
| 正規化後の同名衝突 | 表記違いが同一人物とは限らず、ID統合は不可逆性が高い。 | `Resolved in design`: 衝突候補は選択不可。統合しない。 | AC4,AC9 |
| 検索後の同時更新 | 古いpreviewを適用すると新しい手修正を上書きする。 | `Resolved in design`: manifestと現在値をapply時に再検証しskipする。 | AC5 |
| 複数対象の部分失敗 | EventFlow aggregateを跨ぐatomic transactionはない。全成功表示は誤解を生む。 | `Resolved in design`: 対象別結果と再検索を表示し、冪等再送を保証する。 | AC5,AC7 |
| Ownerの名称モデル | Ownerはalias mappingとmergeを正本とし、他3主体と同じcorrection eventを持たない。 | `Excluded follow-up`: 既存Owner管理を使用し、本ツールから除外する。Owner: product/operations。ACを阻害しない。 | AC9 |
| 正規化規則の将来変更 | preview時とapply時で規則が変わる可能性がある。 | `Resolved in design`: applyはmanifestに加え現行normalizerで補正案も再計算し、不一致をskipする。 | AC5 |
| production適用 | UI提供と実データ補正は別の操作であり、選択なしに自動変更してはならない。 | `Resolved in design`: deploy後も利用者の検索・選択・確認が必要。今回の実装からproduction操作を除外する。 | AC3,AC9 |

## Rejected alternatives

- 全登録対象の一括自動正規化: 衝突・誤同一視を確認できないため不採用。
- UIだけで既存個別PATCHを順に呼ぶ: 衝突判定、同時更新、対象別監査を一つの契約で保証できないため不採用。
- Ownerを同時対応: alias/mergeモデルが異なり、同じ名称補正として扱えないため不採用。
- 自由入力による一括名称変更: 今回の目的は共通規則の適用であり、任意編集は誤操作範囲が広いため不採用。

## Review gates

- **Design and task-split review — 2026-09-20, Main:** 既存Settings repair pattern、Horse/Jockey/Trainer correction endpointsとevents、Owner alias管理、共通normalizer、検索APIを確認した。検索preview、apply、UIは契約・データ整合・共有画面で依存が強く、現在のworktreeにはSettings隣接の別変更もあるためMainが直列統合する。AC1-AC9をAPI、domain event、component、browser、diff reviewへ対応させた。Open decisionはない。
- **Pre-implementation review — 2026-09-20, Main:** ユーザーが設計、AC1-AC9、concern dispositionを承認した。T1を`Runnable`、T2-T4を依存順に`Dependent`とする。API integration testで検索・衝突・空・no-op、apply再検証・event・冪等性を作成し、component testで検索前/loading/empty/error/選択/Dialog/部分結果、browserでdesktop/narrowとfocusを確認する。Owner、ID、alias、collection task、production状態へ触れる必要が出た場合は`Proposed`へ戻す。

## Verification record

- Design inventory: `JraSubjectNameNormalizer` はHorseの登録区分、Jockeyの減量記号と所属、Trainer/Jockeyの末尾所属を表示名から除去し、identity名では空白も除去する。
- Design inventory: Horse/Jockey/Trainerには既存のcorrection command/event/APIがあり、IDを変えず監査理由付きで名称を補正できる。
- Design inventory: Ownerは `OwnerAliasMappingReadModel` とmerge/update APIを用いる別モデルのため除外した。
- Design inventory: `/settings` には選択式repair grid/card、確認Dialog、loading/empty/error patternがあり、同じ操作体系を再利用できる。
- API integration: `SubjectNameNormalizationEndpointsTests` 5件で3主体の正規化preview、同名衝突、適用と再送、preview後の並行変更skip、空検索とOwner拒否を確認した。
- UI component: `HorseIdentityRepairSettingsComponentTests` を含むSettingsテストで検索、前後比較、選択、確認Dialog、apply、入力validationを確認した。
- Regression: `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj -c Release --no-restore -v:minimal` は274 passed、1 skipped、0 failed。
- Build/format: API Release buildは0 warning/0 error。`dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` は成功した。
- Browser verification: ローカルDevelopment環境の`/settings`をdesktopと幅700pxで確認した。初回観測でFluent inputのlabel配置ずれを検出し、field wrapperと全幅指定を追加後、対象種別→名称/ID→検索の順、狭幅での縦積み、Primary Action保持を再確認した。productionデータは操作していない。
- Scope review: APIはHorse/Jockey/Trainerだけを許可し、applyは既存correction commandへ固定理由`その他設定: 登録済み名称の正規化`を渡す。Owner、ID、alias、collection taskを変更するコードはない。
- CodeGraph: `.codegraph/`が存在せず、`codegraph sync .`は未初期化として終了したため、graph-based evidenceは使用していない。
- Change-record validator: `scripts/validate_change_records.py` はリポジトリに存在せず実行不能だった。Status/AC/task/completion summaryはFinal reviewで手動照合した。

## Checkpoint review

- **AC1-AC5:** server-side count/Skip/Take、共通normalizer、衝突判定、manifest再検証、対象別結果が実API経路へ接続され、integration/component testsが成功した。
- **AC6-AC9:** 既存domain commandと固定監査理由を再利用し、Owner等の除外境界を維持した。実ブラウザーのdesktop/narrow反復で発見した入力配置を修正し、無関係な既存変更を差分から除外した。

## Final review

- MainがAC1-AC9を実装差分、API全テスト、format、実ブラウザー観測へ照合し、全項目を`Verified`と判定した。
- 承認済みタスクはすべて`Verified`で、未完了の`Runnable`、`Dependent`、`Externally blocked`はない。
- production deploymentおよびproductionデータ補正はNon-goalのままで、今回実行していない。

## Completion summary

- Code: Complete
- Verification: Complete
- Deployment/operation: Not requested; productionへの適用は実施していない

## Remaining work

- なし。
