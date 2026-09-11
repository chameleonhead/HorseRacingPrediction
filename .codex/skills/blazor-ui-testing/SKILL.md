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

## Usability Scenario Verification

画面構成または主要導線を変更した場合は、見た目の確認だけでなく、実装前に定義した主要ユースケースを初見の利用者の順序で実行する。

各シナリオについて記録または確認する。

- Primary Actionを説明なしで発見できるか
- 完了までの操作数と画面遷移数
- 操作前に必要な判断材料が表示されているか
- 内部IDや技術用語を知らなくても進めるか
- 誤入力、通信失敗、空結果から次の行動が分かるか
- 大量データでも対象と状態を走査できるか

テストデータが少ない正常系だけで判断しない。少なくとも通常、空、失敗、大量または長い値のうち、変更によって影響を受ける状態を確認する。

## Pre-implementation Review Gate

実装前の画面案を、要件への適合だけでなく利用者の操作順でレビューする。レビュー担当が別にいない場合も、実装者自身が以下を明示的に確認してからコーディングへ進む。

- 画面に常設する必要がない操作をDialogやMenuへ移せないか
- 詳細な調査をDialogへ詰め込まず、独立Pageにすべきでないか
- 状態切替、検索、主要操作の優先順位が競合していないか
- 既存の成功している画面・モック・共通Componentを再利用できないか
- 受け入れ基準をユーザーから観測可能なテストへ変換できるか

このレビューで見つかった問題を残したまま「実装後に直す」前提で着手しない。実装でしか判断できない密度や折り返しは、ブラウザー反復の検証項目へ明示的に送る。

## 完了条件

UI変更を完了する前に、

- build
- existing tests
- relevant component tests
- 必要なbrowser verification
- 主要ユースケースの操作確認
- 大規模なUI変更では、問題の特定と修正を含む複数回のbrowser verification

を実行する。

テスト失敗を無視して完了扱いにしない。
