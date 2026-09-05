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

迷った場合は、
独自デザインを追加するより
標準的で単純なFluent UIパターンを選択する。
