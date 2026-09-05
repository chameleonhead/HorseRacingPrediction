# RaceOps UI デザインガイドライン

この文書は HorseRacingPrediction の API 管理画面に適用する視覚・操作設計の唯一の正本です。Collector は画面を持たない実行サービスであり、本ガイドラインのUI適用対象ではありません。画面・URL・API の情報設計、および OOUI の object model は [管理サイト UI / UX 再設計](admin-ui-design.md) を正本とします。

実装は [Fluent UI ベースの管理画面刷新とデザインガイドライン統合](changes/20260830_fluent-ui-design-system/README.md) の承認後に開始します。

Fluent UI コンポーネントの選定方針、独自 CSS で外観を上書きしないという原則、Loading/Empty/Error/Disabled/Success の状態設計、キーボード操作可能性などのアクセシビリティ基本原則は `.codex/skills/blazor-fluent-ui-design/SKILL.md` を正とします。本書は、それを RaceOps 固有のデザイントークンと OOUI パターンへ適用するための規則だけを記載します。

## Principles

- アプリ起動時に `FluentDesignTheme` を配置し、ライトテーマと RaceOps の accent を一箇所で設定する。
- OOUI を composition model とし、画面を object、relationship、attribute、object-scoped action から組み立てる。
- 同じ object はどの画面でも同じ主ラベル、状態、詳細 URL を持つ。絵文字は使用せず、必要な場合だけ Fluent システムアイコンを補助的に使う。
- 詳細画面は「戻るリンク → 種別・状態の補助見出し → オブジェクト名の主見出し → 基本情報 → 関連 → 履歴・管理情報」の順を基本とし、戻るリンクをカード内や見出し横へ移動しない。主見出しに「馬」「騎手」などの種別名だけを表示しない。利用者向けの関係セクション見出しには「関連」を使う。
- 詳細画面のパンくずは `ナビゲーショングループ / オブジェクト種別` とする。日付、開催場、一覧ビュー、状態、検索条件はパンくずへ含めず、オブジェクトヘッダー、状態表示、戻るURLで表現する。
- 件数が不定の「関連」は横幅いっぱいの行リストを基本とする。同じ関係種別が複数件存在できるため、騎手・調教師・馬主などを固定列のボックスへ1件ずつ割り当てない。各行には関係名、リンク可能なオブジェクト名、件数・期間・最終日などの根拠を表示する。
- 関連をランキングとして表示する場合は、集計期間、順位基準、表示上限を見出し付近へ明記する。順位だけで意味を伝えず、集計値も行内に表示する。
- 履歴一覧は新しい順を既定とし、件数が増える場合は期間と主要な関連オブジェクトで検索できるようにする。検索条件とページはURLに保持する。
- 関係はラベル付きリンクで示す。文字列しか得られない場合は、未実装リンクを作らず関係名を添えて表示する。
- 操作は対象 object の詳細または collection header に置き、危険操作は対象・影響・理由を確認してから実行する。

## Foundations

### Color and theme

- ライトテーマを初期対象とする。Accent は `#0F6B52` を基準に Fluent design token から生成する。
- 状態は色だけに依存せず、日本語ラベルを必ず併記する。アイコンは任意の補助情報であり、絵文字は使用しない。

### RaceOps design tokens

モックの値は、以下の意味トークンとして採用する。実装では可能な限り対応する Fluent token を使い、RaceOps 固有のshell・relationship・domain layoutだけにこれらの値を参照する。ページ単位で近似色や任意の余白を増やさない。

| カテゴリ | Token | 値 | 用途 |
| --- | --- | --- | --- |
| accent | `raceops-accent` | `#0F6B52` | primary action、active tab indicator、object link、focus indicator。 |
| shell | `raceops-rail-background` | `#24332F` | desktop railとmobile app bar / drawerの背景。 |
| shell | `raceops-rail-foreground` | `#E5EFEB` | rail navigation text。brandは`#FFFFFF`、section labelは`#B8C9C3`。 |
| shell | `raceops-rail-active` | `#36564B` | active / hover navigation itemの面。 |
| neutral | `raceops-page-background` | `#F5F5F5` | page canvas。surfaceと明確に区別する。 |
| neutral | `raceops-surface` | `#FFFFFF` | fact panel、dialog、form sectionの面。 |
| neutral | `raceops-text` / `raceops-muted` | `#242424` / `#616161` | primary text / supplement text。 |
| neutral | `raceops-border` | `#E1E1E1` | list、tab strip、panelのlow-emphasis separator。 |
| interaction | `raceops-row-hover` | `#EEF6F2` | object row、selectable candidateのhover / selected surface。 |
| status | `success` | foreground `#107C10`、background `#E7F4E7` | 成功・完了。 |
| status | `warning` | foreground `#8A5A00`、background `#FFF4CE` | 要確認・依存待ち。 |
| status | `danger` | foreground `#B3261E`、background `#FDE7E6` | 失敗・破壊的操作。 |
| status | `info` | foreground `#185ABD`、background `#E5F1FB` | 実行中・情報。 |

| カテゴリ | Token | 値 | 用途 |
| --- | --- | --- | --- |
| spacing | `space-1`〜`space-8` | 4 / 8 / 12 / 16 / 24 / 32 px | 要素内4–12px、section内16px、page / major section 24px、large separation32px。20px、22pxなどの任意値は使わない。 |
| layout | `rail-width` / `content-max` | 240 px / 1440 px | desktop shellの固定railと本文最大幅。 |
| layout | `content-padding` | desktop 24 px、tablet 20 px、mobile 12 px | 本文の左右余白。 |
| size | `control-height` / `touch-target` | 40 px / 44 px以上 | desktop controlの視覚高さとmobile操作領域。 |
| shape | `radius-control` / `radius-surface` / `radius-dialog` | 4 / 8 / 12 px | Fluent control、通常surface、large dialogの順。statusだけ999px pillを許可する。 |
| border | `border-subtle` / `border-focus` | 1 px / 2 px | 通常separatorとkeyboard focus。focus色はaccent。 |
| type | `eyebrow` / `body-support` / `section-title` / `page-title` | 13 / 13 / 16 / 28 px | 補助種別、補助説明、section heading、desktop主見出し。mobile主見出しは24px。 |
| type | `numeric` | tabular-nums | 時刻、金額、順位、count。 |

#### Token application rules

- tabは`space-1`のitem間隔、`space-2`の左右padding、2px accent indicator、1px neutral borderを使う。active stateはaccent text + indicatorで示し、Buttonのfillやoutlineをtab代わりに使わない。
- detail fact panelはsurface、1px border、`space-4`の内側余白、`space-4`のfact間隔を使う。desktopでは最大3列、mobileでは1列または意味の近い2列までとする。
- relationship / history rowは1px separator、縦12–16px・横4pxのpadding、desktop 12–16pxの列間隔を使う。hoverは`raceops-row-hover`、linkはaccentを使う。
- error / warning / successのfeedbackはFluent MessageBarを使い、上記status toneは内容理解の補助に限る。raw error、payload、IDはsurface内のdisclosureへ入れる。
- dialogはsurface、最大幅520px（名寄せは920px）、desktop内側余白24px、mobileはfull height / edge-to-edgeを許可する。通常cardにshadowを付けず、dialog / drawerだけelevationを使う。

### Typography, spacing, shape, and elevation

- Fluent type ramp と OS UI font stack を使う。見出しは size、weight、余白を組み合わせて階層化する。
- spacing は 4 px grid（4 / 8 / 12 / 16 / 24 / 32）だけを使う。
- コントロールは Fluent 既定の shape、面コンテナは原則 8 px radius。pill は status と tag に限定する。
- shadow は dialog、menu、drawer のような重なりにだけ使い、通常カードの区切りは background と border を優先する。
- motion は 120–200 ms の状態変化に限り、`prefers-reduced-motion` を尊重する。

## Layout and responsive behavior

- Desktop: 240 px navigation rail、最大 1440 px の本文、24 px のページ余白。
- Tablet: navigation は折りたたみ可能、本文余白は 20 px。
- Mobile (`<= 720 CSS px`): 48 px top app bar と drawer、本文余白は 12 px。desktop table を縮小表示せず object list へ再構成する。
- 通常の入力・ボタンは 32–40 px の視覚密度を保ち、タップ target は 44 x 44 CSS px 以上にする。
- desktop の比較一覧は DataGrid / list-detail を使える。mobile は 1 object 1 item とし、同じ主ラベル、状態、主要属性、関係を優先順位どおり表示する。

## OOUI patterns

### Object header

主ラベル、種類、状態、最終更新、状態依存 CTA をまとめる。内部 ID や enum 名は主見出しに使わず technical details へ置く。

### Condensed object item

一覧は次の順序を守る。

1. 種類と主ラベル
2. 状態
3. 判断に必要な主要属性 2–3 件
4. 主な関係
5. canonical detail への affordance

状態が利用者の判断に意味を持つ object では、2 に日本語ラベル付きの status を置く。状態を持たない object では status の領域を省略し、空の badge やダミー状態を表示しない。

一覧には低リスクの移動を置き、再投入・編集などは object detail で実行する。ジョブの全体一時停止・再開のような collection-scoped action は collection header に置き、個別ジョブ取消を標準UIに置かない。

収集によって生成されるオブジェクトの一覧には初期登録ボタンを置かない。訂正可能なオブジェクトは、詳細ヘッダー右側の同じ位置へ「編集」を置き、専用の編集状態へ遷移させる。状態遷移しか許可しない運用オブジェクトへ、意味の曖昧な「編集」を追加しない。

### Relationships and technical details

relationship は `RaceOpsEntityLink` で統一する。payload、lease、ID、raw error、enum は `RaceOpsTechnicalDetails` の disclosure に置き、通常の判断を妨げない。

親子関係は、`親` → `現在` → `子` の階層と接続線・インデントで表現し、各ノード全体をリンクにする。関連オブジェクト一覧には対象日やレースなど、ジョブ階層と重複しない関係だけを表示する。

## Component policy

1. Fluent standard: Button、TextField、Select、Checkbox、DatePicker、DataGrid、Tabs、Dialog、Toast、MessageBar、ProgressRing、Tooltip、Menu。
2. RaceOps semantic wrappers: `RaceOpsAppShell`、`RaceOpsPageHeader`、`RaceOpsObjectHeader`、`RaceOpsObjectItem`、`RaceOpsStatusBadge`、`RaceOpsRelationshipList`、`RaceOpsEmptyState`、`RaceOpsEntityLink`、`RaceOpsTechnicalDetails`。
3. Page-specific CSS: race participation row のような domain layout だけ。Button、input、font、color を再定義しない。

## States and feedback

- Loading: page は ProgressRing と説明、局所更新は対象 control の busy state を示す。
- Empty: 0件の理由、現在の filter、次の action を表示する。error と混同しない。
- Error: MessageBar に復旧可能な説明と retry を置き、technical error は disclosure に置く。
- Success: 本文の object state と timeline を更新し、Toast は補助通知に限る。
- Confirmation: destructive / risky action は Dialog または mobile bottom sheet で対象・影響・理由・busy state を示す。
- Conflict: 実行しなかった事実と理由を表示し、最新 object を再取得する。

## Queue pause and resume

キュー全体の停止・再開は収集ジョブ一覧の page header にだけ置く。個別ジョブ詳細には取消操作を置かない。運用ナビゲーションは利用者の目的に合わせて「収集ジョブ」「データ取得状況」とし、収集対象日は独立メニューではなく収集ジョブの対象日検索と日別サマリーで表現する。

- `すべてのジョブを一時停止`: 実行前に SQS 本体・DLQ の purge、dispatcher の送信停止、Running は完了まで継続、Ready / Pending はDB保持であることを説明する。
- 停止中: primary action を `再開` に切り替え、停止済み、Running 件数、保持中の Ready / Pending 件数を表示する。
- `再開`: Ready を priority 降順、AvailableAt・CreatedAt・JobId の順で再投入する。Pending は再投入せず通常条件を待つ。
- 操作の進行、完了、失敗、再投入件数は MessageBar と監査履歴で確認できる。Toast だけで完了させない。

## Accessibility

- WCAG 2.2 AA を基準に、text、icon、state color の contrast を確認する。
- semantic landmark、見出し、label、table header、DOM順を保つ。
- Tab / Shift+Tab / Enter / Space / Escape で操作を完結でき、`focus-visible` を消さない。
- 320 CSS px と 200% zoom で主要操作へ到達できる。色、位置、アイコンだけで必須・状態を伝えない。
- 絵文字は使用しない。状態は文字ラベルと色で成立させ、必要な場合だけ Fluent System Icons を補助的に使う。
- モバイルナビゲーションの開閉には Fluent System Icons の `Navigation 24 Regular` を使用する。ボタンは 44 × 44 CSS px 以上の選択領域と「メニュー」のアクセシブルネームを持ち、アイコン自体は装飾として読み上げ対象から外す。
- forced colors と reduced motion を阻害しない。

## Writing

- UI は日本語の利用者語彙を使う。英語 enum、ID、実装用語は technical details だけに表示する。
- 状態は「失敗」「依存待ち」「実行中」「完了」「中止」のように、次の判断へ役立つ語を使う。
- error は「何が起きたか」「影響」「次にできること」の順で書く。

## Review checklist

- その画面は object を主語にし、canonical detail と関係リンクを持つか。
- 主ラベル、状態、主要属性、関係、CTA の優先順位は一貫しているか。
- Fluent standard component か RaceOps semantic wrapper を使い、独自の視覚 component を増やしていないか。
- loading、empty、error、success、disabled、conflict、permission-denied を扱うか。
- 320 / 720 / 1280 CSS px、keyboard、screen reader、200% zoom で確認したか。

### Mock conformance self-review

UI変更の完了前に、担当者は対象画面のモックと実装を同じ状態・viewportで比較し、以下を変更記録へ残す。依頼者が個別に指摘しなくても、このreviewを完了条件とする。

1. **状態を揃える**: list、loaded detail、edit、dialog、loading、empty、errorのうち対象状態をモックと実装で揃える。fixtureがない場合は理由と代替検証を記録し、未確認を「一致」と判定しない。
2. **構造を比較する**: shell、header、tab、toolbar、fact、section、relationship、history、dialogのDOM順と可視状態を確認する。tabは選択paneだけが可視、Buttonはobject actionだけであることを確認する。
3. **tokenを比較する**: rail / page / surface / border / status tone、page padding、section gap、control height、type hierarchy、radiusを `RaceOps design tokens` と照合する。任意のhex、20px等の非token値、Buttonによるtab代用を検出したら不一致とする。
4. **responsiveを比較する**: 1280px、720px、320pxで横overflow、tab strip、action、fact、relation / history row、dialog footerを確認する。mobileの操作領域は44px以上とする。
5. **操作を比較する**: keyboardでtab移動・選択、link遷移、DialogのEscape / focus trap、disabled / busy feedbackを確認する。危険操作は実行せず、open stateまでを確認する。
6. **結論を記録する**: 画面ごとに `一致`、`意図した差異`、`未確認`、`不一致` を記録する。不一致・未確認があれば、原因、修正、再確認結果を同じ変更記録に追記してから `Implemented` とする。
### 一覧行から詳細への遷移

- 一覧行が単一オブジェクトを表す場合、行内の空白、主ラベル、補助属性を含む行全体を詳細への選択領域にする。
- 行内に別の絞り込みリンクや操作ボタンがある場合、その要素は自身の操作を優先し、行の詳細遷移を発火させない。
- クリック可能な行はポインター、hover、`focus-visible` を持ち、`Enter` と `Space` でも詳細へ遷移できる。
- 詳細専用リンクを併置する場合も、行全体の操作と同じ遷移先にする。

### 一覧の補助列

- 時刻、状態、件数、詳細リンクのような短い補助列は `max-content` を基本とし、バッジのためだけに可変幅の列を確保しない。
- 状態バッジは内容幅で左寄せし、詳細リンクは行末へ揃える。両者の間隔は spacing token で一定にする。
- 情報は、より広いスコープ、オブジェクトの識別、利用者の判断に重要な属性、補助属性、操作の順に配置する。見た目の列揃えだけを理由に細粒度の値を先頭へ移動しない。
- 同じ関係にある属性は広い粒度から狭い粒度へ並べる。レースでは日付、開催場、発走時刻の順とし、ジョブでは処理名と対象を更新時刻より先にする。
- 時刻は等幅数字で縦位置を揃えるが、時刻自体がその画面の主オブジェクトまたは最優先の走査軸でない限り先頭列に置かない。
