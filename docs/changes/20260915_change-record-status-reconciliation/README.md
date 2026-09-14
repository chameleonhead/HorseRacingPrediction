# Change recordの状態同期漏れを防止する

- Status: Implemented
- Owner: Main
- Created: 2026-09-15
- Updated: 2026-09-15

## Context

実装済みの収集基盤change recordを棚卸しした際、record全体のStatusと、コード実装、検証、本番運用の部分状態が混同された。マイクロバッチの本番証拠は別recordへ記録され元recordへ同期されず、Horse repairは非正規の `Implemented (production repair pending)` を使用していた。一方、circuit breakerとURL fallbackは本番ACが未完了なので意図的に `Approved` であり、Statusだけでは実装有無を分類できなかった。

## Goals

- Statusを承認済みscope全体の状態として正規化する。
- コード、検証、配備・運用の部分状態をStatusと分離する。
- AC、task、残作業、Statusの矛盾をcommit前に検出する。
- 別recordで得た完了証拠を元recordへ戻す責務を明示する。

## Non-goals

- 既存change recordを一括して自動書換えすること。
- 未完了ACを弱めて `Implemented` に変更すること。
- 収集基盤のプロダクションコードや本番データを変更すること。

## Documentation updates

- `.codex/skills/document-driven-development/SKILL.md`: Status reconciliation gateと証拠同期規則を追加する。
- `.codex/skills/document-driven-development/references/change-record-format.md`: 正規Status、Completion summary、状態付きAC表を標準書式にする。
- `docs/changes/20260915_complete-pending-collection-changes/README.md`: 原因分析と本対策へのリンクを記録する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Statusは4つの正規値だけを使い、コード・検証・配備の部分状態をCompletion summaryで分離できる。 | T1 | skill/format review、validator tests | Verified |
| AC2 | AC表が状態列を持ち、未完了ACと `Implemented` の矛盾、非正規Statusをvalidatorが検出する。 | T2 | fixture-based validator tests | Verified |
| AC3 | 別recordや本番作業で得た証拠を元recordへ同期するclosure ownerと同一作業内更新規則が定義される。 | T1 | skill review | Verified |
| AC4 | 棚卸しはRecord、Code、Verification、Deployment/operationの4軸で分類する。 | T1 | skill review | Verified |
| AC5 | 変更したskillがskill validatorに成功し、status validator自身のテストと今回の既存recordへのdry-runが成功する。 | T3 | quick_validate、unit test、dry-run | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | DDD skillとformatへ状態同期・4軸分類・closure owner規則を追加する（AC1, AC3, AC4）。 | Main | High capability | - | DDD skill、format、本記録 | diff review、skill validator | rules linked to observed failures | Verified |
| T2 | change record status validatorとfixture testを追加する（AC2）。 | Main | High capability | T1 | DDD skill scripts | unit test | known contradictions detected | Verified |
| T3 | validator、skill、既存record dry-run、diff/statusを検証し記録する（AC5）。 | Main | High capability | T1, T2 | 本記録、統合記録 | exact commands | verification log | Verified |

## Review gates

- **Design and task-split review** — Reviewer: Main。Inputs: Git履歴、対象5record、現行DDD/failure/orchestration skill。Decision: 原因は実装欠落ではなく、部分状態の表現不足、証拠のrecord間分散、非正規Status、棚卸し分類ミス。最小修正先はDDD skill、format、同skill配下validator。共有規則なのでMainが直列実装する。Follow-up: ユーザーの「対応をお願いします」を本対策の明示承認として扱う。
- **Pre-implementation review** — Reviewer: Main。Inputs: Approved scope、現在の未コミット文書。Decision: T1をRunnable、T2/T3をDependentとする。プロダクションコード、本番環境、既存recordの自動書換えは対象外。Escalation: validatorが既存の正当な履歴を誤って変更要求する場合は自動修正せず診断専用に狭める。
- **Checkpoint review** — Reviewer: Main。Inputs: skill/format diff、validatorと4 fixture tests。Decision: 正規Status、部分状態、証拠同期、4軸棚卸しをDDDへ追加し、非正規Status、stateなしAC、Implementedと未完了ACの矛盾をvalidatorが検出した。Follow-up: 統合record自身のState列不足を修正してT3を再実行する。
- **Final review** — Reviewer: Main。Inputs: AC1–AC5、T1–T3、skill diff、fixture tests、対象record dry-run、diff check。Decision: 全項目Verified。プロダクションコード・本番状態を変更せず、観測された失敗に限定したルールと診断validatorを追加したためImplementedとする。

## Verification record

- 2026-09-15: 対象recordとprocess ruleのGit履歴を確認。強いzero-open-item/final-review規則は対象実装後の2026-09-14に追加されたことを確認した。
- 2026-09-15: `python .codex/skills/document-driven-development/scripts/test_validate_change_records.py` 成功（4件）。
- 2026-09-15: `python C:/Users/yuto.nagano/.codex/skills/.system/skill-creator/scripts/quick_validate.py .codex/skills/document-driven-development` 成功。
- 2026-09-15: 新規2recordへのvalidator適用で初回に統合recordのState列不足を検出し、修正後 `issues=0` を確認した。
- 2026-09-15: `git diff --check` 成功。既存recordの自動書換えは行っていない。

## Deviations and follow-up

- 既存recordのStatus修正は、各recordの未完了ACを確認する統合実装で行う。本対策だけで一括変更しない。
