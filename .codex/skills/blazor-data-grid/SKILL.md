---
name: blazor-data-grid
description: >
  Use when creating or modifying tabular data views in Blazor.
  Defines FluentDataGrid usage, columns, sorting, filtering,
  pagination, row actions, loading, and empty states.
---

# Blazor Data Grid

表形式データには原則として
FluentDataGridを使用する。

独自table/grid実装を作らない。

## Columns

列はユーザーにとっての重要度で並べる。

データモデルのProperty順を
そのまま列順にしない。

基本:

Identification
→ Important attributes
→ Status
→ Date / metadata
→ Actions

## Sorting

ユーザーが比較する可能性が高い列のみ
sort可能にする。

すべての列を機械的にsortableにしない。

## Filtering

軽量な条件はDataGrid上部のToolbarに置く。

条件が多い場合は
Filter UIを別途開く構成を検討する。

## Pagination

大量データを一度に取得・描画しない。

既存のPagination方式がある場合は
それに合わせる。

## Row Actions

各Rowに多数のButtonを並べない。

頻度の高い操作のみ直接表示し、
その他はMenuへまとめる。

## Row Navigation

Row clickで詳細へ移動する場合でも、
keyboard操作可能にする。

clickableであることが
視覚的・意味的に分かるようにする。

## Loading

データ取得中であることを明示する。

既存データ更新時に
画面全体をLoadingへ置き換えない。

## Empty

0件の場合は
空のGridだけを表示しない。

検索結果0件と、
データ自体が存在しない状態を
必要に応じて区別する。

## Responsive

すべての列を狭い画面へ
無理に押し込まない。

低優先情報の表示方法を変更する。

重要な識別情報と主要Actionは維持する。
