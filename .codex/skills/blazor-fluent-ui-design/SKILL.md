---
name: blazor-fluent-ui-design
description: >
  Use when creating, redesigning, or reviewing Blazor UI screens and components
  using Microsoft.FluentUI.AspNetCore.Components.
  Guides Fluent UI component selection, layout, visual hierarchy,
  interaction patterns, responsiveness, and accessibility.
---

# Blazor Fluent UI Design

Blazorの画面・コンポーネントを作成または変更するとき、
Microsoft Fluent UI Blazorをアプリケーションの
基本デザインシステムとして使用する。

目的はMicrosoft製品を模倣することではなく、
Fluentの設計原則を利用して、
一貫性があり、読みやすく、操作を予測しやすい
業務UIを構築することである。

## 1. 実装前に確認する

実装前に以下を確認する。

1. この画面の目的
2. ユーザーが主に扱うObject
3. 最も重要な情報
4. Primary Action
5. 使用できる既存コンポーネント
6. Loading / Empty / Error状態

大規模な変更では、
これらを短いImplementation Noteとして整理してから実装する。

小さな変更では文書化は不要。

### ユーザビリティを実装契約にする

「使いやすい」を主観のまま扱わず、主要ユーザーと達成したい操作を先に定義する。
各主要ユースケースについて、少なくとも以下を決める。

- 開始地点と完了状態
- Primary Actionを発見できる場所
- 許容する操作数と画面遷移数
- 判断に必要な情報と、通常は隠してよい技術情報
- 誤操作、空、失敗、大量データ時の復旧方法

画面構成を変更する場合は、実装前に提案をセルフレビューする。ユーザーの操作順に画面をたどり、次を満たさない案はコード化する前に修正する。

- 初見でも次の操作をラベルと配置から推測できる
- 最頻操作が、低頻度操作や技術設定に埋もれていない
- 同じ重要度に見える要素が、実際にも同じ重要度である
- 一覧から対象、状態、問題、次の行動を走査できる
- Dialogと独立Pageの選択が、操作時間と情報量に合っている
- 既存画面や承認済みモックとの一貫性を説明できる

セルフレビューの結果は、大規模変更ではImplementation Noteに「採用案・退けた案・理由」として残す。軽微な変更では作業中の判断でよい。

## 2. Fluent UIを優先する

UIを独自実装する前に、
Fluent UI Blazorに対応コンポーネントがないか確認する。

例:

- Button → FluentButton
- Table → FluentDataGrid
- Dialog → FluentDialog
- Input → FluentTextField等
- Select → FluentSelect / FluentCombobox
- Tabs → FluentTabs
- Menu → FluentMenu
- Tooltip → FluentTooltip

Fluent UIに存在するUIを
HTML/CSSで再実装しない。

使用するAPIは推測せず、
プロジェクトで使用している
Microsoft.FluentUI.AspNetCore.Componentsのバージョンと
実際のAPIを確認する。

## 3. Fluentの見た目を壊さない

Fluent UIコンポーネントの外観を
画面ごとのCSSで再設計しない。

特に以下を避ける。

- 任意の色
- 任意の角丸
- 任意の影
- 独自focus ring
- FluentButton/Input/DataGrid等の外観変更

色や状態表現には、
可能な限りFluentのDesign TokenやAppearanceを使用する。

独自CSSは主にレイアウト調整に使用する。

## 4. 情報階層を優先する

装飾より以下を優先する。

- hierarchy
- alignment
- spacing
- scanability
- predictable interaction

関連する情報は近づけ、
異なる情報はspacingによって分離する。

罫線やCardを
単なるグルーピング目的で多用しない。

特に以下を避ける。

- すべてをCardで囲む
- Cardの入れ子
- 巨大なページタイトル
- 装飾的なgradient
- 強いshadow
- 不要なanimation

業務アプリとして適切な情報密度を維持する。

## 5. Actionの優先順位を明確にする

画面またはDialogのPrimary Actionは
原則として1つにする。

例:

- 保存
- 作成
- 登録
- 承認

Primary Actionには
FluentのAccent Appearanceを使用する。

Secondary ActionはNeutralにする。

削除などのDestructive Actionは、
色だけで危険性を表現しない。

一覧の各行に多数のButtonを並べず、
頻度の低い操作はMenuへの集約を検討する。

## 6. 状態を設計する

データを表示する画面では必要に応じて、

- Loading
- Empty
- Error
- Disabled
- Success

を設計する。

Empty Stateでは、
単に空のDataGridを表示するのではなく、
「なぜ空なのか」「次に何ができるか」が
分かるようにする。

技術的な例外メッセージを
そのままユーザーへ表示しない。

## 7. Responsive

Desktopの画面を単純に縮小しない。

狭い画面では必要に応じて、

- 横並びを縦積みにする
- Toolbarを折り返す
- Secondary ActionをMenuへ移す
- DataGridの低優先情報を減らす

など、情報の優先順位に基づいてreflowする。

重要な情報や操作を
画面幅だけを理由に消さない。

## 8. Accessibility

Fluent UIコンポーネントが提供する
標準のアクセシビリティを優先する。

最低限以下を確認する。

- keyboardで操作できる
- focusが視認できる
- inputにlabelがある
- icon-only actionにaccessible nameがある
- heading hierarchyが正しい
- 色だけで状態を表現していない

独自ARIAは必要な場合だけ追加する。

## 9. 共通化

Fluent Componentを直接ラップするだけの
無意味な共通コンポーネントは作らない。

以下のいずれかが共通する場合に共通化を検討する。

- UI Pattern
- Interaction
- Domain semantics

例えば以下は共通化候補になる。

- PageHeader
- StatusBadge
- SearchToolbar
- EntityPicker
- DateRangeField
- ConfirmDialog

基本的な依存方向は、

Fluent Component
    ↓
Shared / Domain Component
    ↓
Page

とする。

## 10. 完了前に確認する

実装後、以下を確認する。

- Fluent UIコンポーネントを優先したか
- 不要な独自CSSを追加していないか
- Primary Actionが明確か
- spacingとalignmentが一貫しているか
- Loading / Empty / Errorを考慮したか
- 狭い画面でも破綻しないか
- keyboardで操作できるか
- build / testが成功するか

画面構成または主要導線を変更した場合、コードだけを見て完了としない。実データを表示したブラウザーで、次の反復を行う。

1. 主要ユースケースを操作する。
2. 情報階層、密度、整列、文言、操作フィードバック、レスポンシブ、アクセシビリティから最大の未解決問題を1つ特定する。
3. その問題を修正し、同じユースケースを再度操作する。
4. 重大な問題がなくなるまで繰り返す。

大きな再設計では、デスクトップと狭幅を含む複数回の観察・修正を必須とする。文言だけの変更など、表示構造に影響しない変更へ機械的な反復回数を課さない。

完了判定には、主要操作の発見性、不要な常設要素の有無、操作数、状態からの復旧、キーボード操作、狭幅での情報保持について、観測結果またはテスト結果が必要である。

迷った場合は、
独自デザインを追加するより
標準的で単純なFluent UIパターンを選択する。
