---
name: blazor-ui-testing
description: >
  Use when implementing or reviewing Blazor UI changes.
  Defines the expected UI verification strategy using
  component tests and browser tests without over-testing implementation details.
---

# Blazor UI Testing

Blazor UI変更では、
変更内容に応じた最小限のUIテストを行う。

すべてをE2Eで確認しない。

## Component Test

Component単体の状態・Interactionは
bUnitを優先する。

確認対象:

- conditional rendering
- user interaction
- validation
- event callback
- loading / error / empty state
- permissionによる表示変更

内部実装ではなく
ユーザーから観測可能な挙動をテストする。

## Browser Test

実ブラウザーでしか確認できない重要フローは
Playwright等のBrowser Testを使用する。

例:

- Navigation
- Dialog
- focus
- complex interaction
- responsive behavior
- JavaScript連携

## Test Scope

変更したUIに対して、
最も安価に問題を検出できるテストを選択する。

Component Testで十分なものを
E2E Testにしない。

## Visual Verification

UI変更では最低限、

- layout
- overflow
- narrow viewport
- loading
- empty
- error

を確認する。

可能であれば既存のVisual Regression手段を使用する。

## 完了条件

UI変更を完了する前に、

- build
- existing tests
- relevant component tests
- 必要なbrowser verification

を実行する。

テスト失敗を無視して完了扱いにしない。
