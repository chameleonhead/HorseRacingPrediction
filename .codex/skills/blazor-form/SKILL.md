---
name: blazor-form
description: >
  Use when creating or modifying data-entry forms in Blazor.
  Defines form layout, Fluent input selection, validation,
  submission behavior, and form interaction patterns.
---

# Blazor Form

Blazorで入力フォームを作成・変更するときに使用する。

## Fluent Inputを使用する

Fluent UI Blazorに対応Componentが存在する場合、
HTML input/select等を直接使用しない。

用途に合ったComponentを選択する。

## Form Layout

基本は縦方向。

Label
Input
Validation / Supporting text

を一つのFieldとして扱う。

関連性が明確な項目のみ横並びにする。

例:

姓 | 名

開始日 | 終了日

画面幅が狭い場合は縦積みにする。

## 入力順序

ユーザーが業務上考える順序と
入力順序を一致させる。

データモデルのProperty順を
そのままUIへ反映しない。

## Validation

Validationは入力項目の近くに表示する。

送信後にページ上部だけへ
エラー一覧を表示する設計は避ける。

必要に応じてsummaryとinline errorを併用する。

## Required

必須項目は一貫した方法で示す。

placeholderだけをlabel代わりに使用しない。

## Submit

保存中は二重送信を防止する。

必要に応じてProgress状態を表示する。

成功後の挙動を明確にする。

例:

- 一覧へ戻る
- 詳細へ移動
- そのまま編集を継続

## Unsaved Changes

入力内容が失われる可能性がある場合、
必要に応じて離脱確認を行う。

## Large Form

大きなフォームは
業務上意味のあるSectionへ分割する。

Dialogへ巨大なフォームを入れない。

## 完了確認

- keyboardだけで入力できる
- labelが存在する
- validationが項目付近にある
- narrow viewportで破綻しない
- 二重送信できない
- error時に入力値が失われない
