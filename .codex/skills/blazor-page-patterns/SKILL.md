---
name: blazor-page-patterns
description: >
  Use when creating or restructuring Blazor application pages.
  Defines standard page structures for list, detail, create,
  edit, settings, and dashboard screens.
---

# Blazor Page Patterns

Blazorの業務画面では、
画面ごとに独自の構造を作らず、
既存または標準Page Patternを使用する。

デザイン判断には
blazor-fluent-ui-designの原則を適用する。

適用中のchange recordがある場合、Page種別、PageHeader、dialog/page選択、主要導線、
既存pattern再利用の判断を関連する受入れ基準へ対応づけ、検証方法と完了証拠をVerification recordへ接続する。
ページ構造の設計はこのスキルの責務とし、操作確認・browser verificationの証拠は
`blazor-ui-testing`へ記録する。change recordのACにない構造変更を、実装都合だけで追加しない。

## 基本構造

標準ページは原則として、

Page
├─ PageHeader
│  ├─ Breadcrumb / Context
│  ├─ Title
│  └─ Actions
└─ PageContent

とする。

検索・フィルターはPageHeaderではなく、
対象コンテンツの近くに配置する。

## List Page

一覧画面:

PageHeader
├─ Title
└─ Create Action

Toolbar
├─ Search
├─ Filter
└─ View options

DataGrid

Pagination

一覧操作をPageHeaderへ大量に配置しない。

## Detail Page

詳細画面:

PageHeader
├─ Context
├─ Title
├─ Status
└─ Actions

Summary

Sections

History / Related information

情報をすべてCardで囲わず、
見出しとspacingによる構造化を優先する。

## Create / Edit Page

PageHeader
└─ Title

Form
├─ Section
├─ Section
└─ Actions

フォームが大規模な場合は
意味のあるSectionへ分割する。

保存Actionの位置は
アプリケーション内で統一する。

## Settings Page

Settings
├─ Navigation
└─ Setting Content

設定項目が増える場合、
巨大な単一フォームにしない。

意味のあるカテゴリへ分割する。

## DialogかPageか

短時間で完了し、
現在のContextを維持する必要がある操作のみ
Dialogを使用する。

複雑な入力や多段階操作はPageにする。

選択時は実装都合ではなく、利用者が保持すべきContextと情報量を基準にする。

- 種類選択後に短い入力を完了する操作はDialog候補
- 履歴比較、原因調査、複数Section、固有URLや再訪が必要な対象はDetail Page候補
- 頻度の低い複数操作を一覧へ常設して密度を下げない
- Dialog内でさらに複雑なDialogを開く構造は避ける

## 既存Patternを優先する

新しいPage Patternを作る前に、
既存画面に同じユースケースがないか確認する。

同じ種類の操作に
異なる画面構造を導入しない。
