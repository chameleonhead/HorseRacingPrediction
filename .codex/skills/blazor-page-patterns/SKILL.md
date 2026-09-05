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

## 既存Patternを優先する

新しいPage Patternを作る前に、
既存画面に同じユースケースがないか確認する。

同じ種類の操作に
異なる画面構造を導入しない。
