# 詳細画面のデザインシステム適合

- Status: Approved
- Owner: HorseRacingPrediction team
- Created: 2026-09-01

## Purpose

一覧だけでなく詳細画面を、`docs/design-guidelines.md` を正本として、`docs/changes/20260830_fluent-ui-design-system/mocks/` の情報階層と操作文法へ揃える。モックをピクセル単位で模写するのではなく、Fluent UI Blazorを基盤に、同じ意味の操作には同じcomponentと視覚役割を適用する。

## Design-token extraction

モックCSSからcolor、spacing、type、shape、layoutを抽出し、`docs/design-guidelines.md` の `RaceOps design tokens` として正本化した。主な判断は次のとおり。

- 色: accent `#0F6B52`、dark rail `#24332F`、active rail `#36564B`、page background `#F5F5F5`、surface `#FFFFFF`、border `#E1E1E1`。statusはsuccess / warning / danger / infoのforegroundと淡いbackgroundの対で定義する。
- 余白: 4px gridだけを使い、inline 4–12px、section 16px、page / major section 24px、large separation 32pxとする。モックに混在する20px、22pxなどはtokenに採用しない。
- shape: control 4px、surface 8px、large dialog 12px。pillはstatus / tagだけに限定する。
- type: eyebrow・support 13px、section 16px、desktop page title 28px、mobile page title 24px。時刻・金額・順位はtabular numberを使う。
- interaction: tabは2px accent underlineで選択状態を示し、Buttonのfilled / outline styleをtabの代用にしない。object actionだけをButtonにする。

## Audit findings

### 共通

| 観点 | モック／ガイドライン | 現在 | 問題 |
| --- | --- | --- | --- |
| section navigation | 横スクロール可能なtab strip。現在位置だけaccent underlineで示し、対象paneへ切り替える | `detail-tabs` / `detail-tab-list` にCSSがなく、通常の箇条書きリンクとして表示される。リンク先の全sectionも同時に縦積みされる | タブに見えず、現在位置・閲覧対象が分からない。 |
| object header | 戻る→種別→主見出し→object actionを一つのheader hierarchyに置く | headerの整列は改善済みだが、詳細ごとにstatus、説明、actionの有無と位置が揃っていない | 同種objectの主ラベルと操作の優先度が揺れる。 |
| section面 | 概要は短いfact panel、関係・履歴は全幅の行リスト、技術情報はdisclosure | `detail-card`、`entity-section`、`detail-section` が同じ薄いgrid定義だけで、surface、heading、行の役割が曖昧 | cardとlistの境界がなく、情報が長い一枚に見える。 |
| buttonとtab | view/section切替はTab、objectへの遷移はlink、object操作だけButton | section移動を無装飾anchor、名寄せ・再取得・補正などは文脈により同じ見た目にならない | 画面内移動と状態変更の区別が弱い。 |
| 状態・feedback | statusはtext+semantic color、失敗はMessageBar、診断はdisclosure | status labelの日本語化はあるが、Job detailのalert/attempt/technical CSSが未定義 | 失敗と技術詳細の主従が表示品質に反映されない。 |

### 画面別

| 画面 | モックとの差異 | 修正対象 |
| --- | --- | --- |
| `/horses/{id}` | 概要・関連・出走履歴を短いpanelとして読めるモックに対し、未スタイルtabと全sectionの長い連結になる | Profile fact panel、section tab、relation row、history rowを共通detail grammarへ揃える。 |
| `/jockeys/{id}`、`/trainers/{id}` | 馬と同じ構成なのに、詳細APIの正常loaded stateを再確認できていない。現行markupは馬と同じ未スタイルtabを共有する | 共通componentを適用し、API復旧後にloaded / error を再確認する。 |
| `/owners/{id}` | 名寄せはobject actionだが、関連・履歴と同じ縦連結の中で目立たない。dialogもモックの段階的な候補選択・影響確認の構成と異なる | tab pane化、relationship list化、名寄せdialogの段階と確認情報をFluent Dialog内で再構成する。 |
| `/races/{id}` | モックは出走表／オッズ／結果を相互排他的なtab paneとして扱う。現行は概要・取得状況・オッズ・出走がすべて同時に表示され、tab linkにもならない | Fluent Tabsでpaneを切替え、出走・オッズ・結果を用途別に分ける。概要と管理はobject-level sectionとして保持する。 |
| `/predictions/{id}` | 概要、印、操作がsurfaceと行の役割で区別されず、section navigationもない | 読み取り専用のdetail grammar（概要fact、印のlist、technical / operation note）へ揃える。 |
| `/jobs/{id}` | モックはstatus付きheader、注意喚起、ジョブ関係図、主要情報・試行履歴を明確に分ける。現行は`alert-panel`、`attempt-list`等のCSSがなく、関係図が3列カードに均等配置される | MessageBar、縦方向のjob map、試行timeline、technical disclosureをFluent primitive + domain layoutで実装する。 |
| `/acquisition-statuses/{key}` | モックは状態・最終取得・Providerのfacts、その後に履歴・関連を置く。現行は詳細gridと履歴・関連の表現が混ざる | fact panel、history row、relationship row、再取得の成功feedbackを統一する。 |
| `/*/edit` | モックは2列field、immutable note、理由、確認actionを持つ。現行formのsection、note、review、footerは画面ごとに統一されていない | Fluent field/MessageBar/Dialogを維持しつつ共通form layoutを適用する。 |

## Proposed design and scope

1. `RaceOpsDetailTabs` をsemantic wrapperとして追加する。Fluent Tabsを使用し、URL fragmentで開くsectionを復元できるようにする。Tabはsection navigationだけに使い、補正・名寄せ・再取得・リランはButtonのままにする。
2. `RaceOpsDetailFacts` と `RaceOpsDetailSection` を追加する。factsはdesktopで複数列、mobileで1列、関係・履歴は全幅row listを標準にする。既存の `RaceOpsRelationshipGrid` は複数関係を一度に比較する必要がある箇所だけに限定する。
3. horse / jockey / trainer / owner / race / prediction / job / acquisition のloaded detailに上記wrapperを適用する。Jobとacquisitionの失敗・診断にはFluent MessageBarとnative `details` を使う。
4. edit画面と名寄せ・再取得・リランdialogを、field group、immutable note、review／impact、footer actionの順へ揃える。確認が必要な操作の実行契約は変更しない。
5. mobileではtab stripを横スクロール、factsを1列、関係・履歴を1列にする。320px / 720px / 1280pxでoverflow、keyboard tab操作、error/loading/empty状態を確認する。

## Acceptance criteria

- detail section navigationはtabとして見え、選択paneだけが表示され、URL fragmentで直接開ける。
- `/races/{id}` の出走表、オッズ、結果は同時表示されず、tabで切り替えられる。
- すべてのloaded detailで、戻る、種別、主ラベル、status、object action、facts、関連、履歴、technical detailsが一定の順序で表示される。
- Job / acquisitionの失敗は、利用者向けの説明と次の操作を表示し、ID・payload・完全なエラーはdisclosureに隔離する。
- 既存のURL、API契約、権限、補正／名寄せ／リラン／再取得の実行条件を保持する。
- 1280px、720px、320pxでbrowser比較を行い、テスト・ビルド結果をこの記録へ追記する。

## Self-review matrix

実装前のsource / mock比較の結果を以下に記録する。`不一致` は実装後に再比較し、`未確認` はfixtureまたは認証済みbrowserで検証するまで完了扱いにしない。

| 画面・状態 | shell / header | tab・pane | facts / section | rows / dialog | 現在の判定 |
| --- | --- | --- | --- | --- | --- |
| horse detail | headerは共通化済みだがfact surface未適用 | 未スタイルanchor、全pane表示 | profileが`dl`のみ | relation / historyはmockのrow grammar未適用 | 不一致 |
| jockey detail | horseと同様 | horseと同様 | loaded stateはAPI 500のため未確認 | history row未確認 | 不一致 / 未確認 |
| trainer detail | horseと同様 | horseと同様 | profile fact未適用 | relation / history row未適用 | 不一致 |
| owner detail / merge dialog | header actionはある | 未スタイルanchor、全pane表示 | profile / merge sectionのhierarchy不一致 | merge dialogにimpact / review構成がない | 不一致 |
| race detail | header actionはある | 未スタイルanchor、overview / status / odds / entries同時表示 | race factsは面・列の定義不一致 | participant rowとmanagementがpane未分離 | 不一致 |
| prediction detail | headerのみ | tabなし | overview / mark / operationが同じcard grammar | mark rowのtoken / responsive未確認 | 不一致 |
| job detail / rerun dialog | status headerとactionはある | tab対象外 | alert / facts / job map CSS未定義 | timeline / technical disclosure / dialog impact不一致 | 不一致 |
| acquisition detail / reacquire dialog | status headerとactionはある | tab対象外 | fact panel未適用 | history / relation row、success feedback、dialogを再確認 | 不一致 |
| horse / jockey / trainer / race edit | headerは共通化済み | 対象外 | edit section / immutable note / reviewの共通grammar不足 | mobile footer / confirmationを未確認 | 不一致 |

### Review gate after implementation

各画面について、mockと同じloaded stateで1280px・720px・320pxのscreenshot / DOMを比較する。確認項目は、token、tabの選択pane、Buttonとlinkの役割、facts、rows、dialog、loading / empty / error、keyboard操作である。認証にAPI key入力が必要なローカルbrowser検証は、入力直前に利用者の確認を得て実施する。

## Implementation checkpoint - 2026-09-02

- `RaceOpsDetailTabs` を追加し、horse / jockey / trainer / owner / race / prediction detailの既存section navigationをFluent Tabsの相互排他的paneへ置換した。tabとButtonを混同せず、編集・名寄せ・再取得・リランはobject actionとしてFluent Buttonに残した。
- detail panel、heading、history / relationship row、numeric value、job alert / attempt、acquisition facts / MessageBarの共通layoutをtokenに合わせて追加した。Jobとacquisitionの利用者向けの失敗・成功feedbackはFluent MessageBarを使用する。
- `AGENTS.md` に、local development authenticationの利用者許可と、認証情報をログ・文書・commitへ残さない規則を追記した。
- 隔離出力先で `dotnet build src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --no-restore -v:minimal -p:BaseOutputPath=...` を実行し、warning 0 / error 0を確認した。

### Remaining verification

- 起動済みの通常出力APIはRazor更新を読み込まないため、隔離buildまたは開発サーバーを起動してから、認証済みbrowserで1280px / 720px / 320pxのvisual self-reviewを行う。
- jockey detailのloaded API 500、各edit pageとdialogのvisual reviewは未完了。これらを確認し、不一致があれば同じ変更記録に修正と再確認結果を追記する。

## Repeatable visual verification approach - 2026-09-02

比較の信頼性を確保するため、以下を必須手順とする。

1. 通常出力先へwarning 0 / error 0でbuildしてから、同じoutputを使うdevelopment serverを起動する。isolated outputだけではstatic web assetが不足するため、視覚比較の対象にしない。
2. 開発設定のAPI keyでlocalhostへ認証し、一覧で実データのobject IDを取得してloaded detailを開く。API keyは画面・log・recordへ残さない。
3. browser DOM snapshotでtablist、selected tab、tabpanel、link / button、error / disclosureを検証し、screenshotでcolor、surface、spacing、overflowを確認する。APIを直接呼び、profile・history等が200であることも別に確認して、データ取得失敗とUI失敗を混同しない。
4. 1280px、720px、320pxを同じobject・同じtabで確認し、各screen-stateをself-review matrixへ`一致` / `不一致` / `未確認`として反映する。

### Initial browser result

- 通常outputを再build・再起動した後、`/horses/horse-247a0288-8e3f-5c45-a46a-f033f3fbb4e9` のloaded stateで確認した。概要・関係・履歴・管理・メモがFluent tablistとして表示され、関係tabを選択すると関係tabpanelだけが表示された。horizontal overflowはなかった。
- 同horseのprofile、participations、race-history APIはそれぞれHTTP 200を確認した。先行のerror stateは、更新前サーバー／browser sessionを比較対象にしていたためであり、現行outputのUIエラーではない。

### Continued browser result - 2026-09-02

- 通常出力を再buildしたdevelopment serverで、race、owner、jockey、trainer、jobのloaded detailを実データから開いた。1280pxでは全対象で横overflowなし、race / owner / jockey / trainerではtablist、選択tab、tabpanelの遷移を確認した。
- 320pxではrace、job、predictionのempty state、acquisitionのempty state、owner / jockey / trainer detailを確認した。全対象でdocument horizontal overflowは発生しなかった。predictionとacquisitionはローカルfixtureにloaded detailがないため、empty stateのみを確認した。
- 狭幅のowner detailで、Fluent Tabsのoverflow menuが`FluentMenuProvider`未配置によりErrorBoundaryへ到達する不具合を再現した。`App`およびinteractiveな`MainLayout`へproviderを配置し、再build・再起動後にownerのloaded detailと編集dialog起動、jockey / trainer detailでerrorなしを再確認した。
- `dotnet build src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --no-restore -v:minimal` はwarning 0 / error 0で成功した。

### Open verification scope

- prediction / acquisitionはloaded detail用fixtureがないため、loaded detailおよび再取得dialogの実データ検証が未完了である。既存データを変更せずに確認できるfixtureを用意した時点で、この記録のmatrixを更新する。

### Shell token conformance - 2026-09-02

- mockの共通shellと実装を照合し、railのpaddingを`24px 16px`、brand下の余白を`28px`、nav linkのpaddingを`10px 12px`、desktop / mobile page titleを`28px` / `24px`、filter toolbar gapを`8px`へ統一した。
- ブラウザーが旧CSSを使用して差分判定を誤らないよう、`app.css`の参照へversion queryを付与した。再build・再起動後の実画面で、上記computed styleとhorizontal overflowなしを確認した。

## Documentation updates

- `docs/changes/20260901_detail-design-conformance/README.md`: 本変更の要件、監査、判断、検証結果の正本として追加する。
- `docs/design-guidelines.md`: モックから抽出したRaceOps design token、tab / fact panel / list row / dialogの適用規則、Fluent Tabsのcomponent policyを追加した。この文書が色・余白・shape・typeの正本である。

## Approval

2026-09-01 に利用者が、本変更記録のscopeに沿った実装と、モック比較を必須にするセルフレビューを承認した。
