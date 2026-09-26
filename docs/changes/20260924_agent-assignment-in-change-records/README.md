# 設計時のエージェント割当と変更完了責任を明記する

- Status: Implemented
- Change record schema: 2
- Owner: Main
- Created: 2026-09-24
- Updated: 2026-09-24

## Context and approval

利用者が「変更ドキュメントにエージェントを記載する件について、スキルも修正」「収集処理の改修が完了するまでを今回の変更」と明示依頼した。本recordは前者の狭いskill編集を記録する。収集本体の実装・本番操作の承認ではない。

## Decisions and scope

設計時のtask planにplanned agent role、tier、選定理由、排他write scope、依存、検証/受入責任、引継ぎ/昇格条件を記載する。実行時model/agent IDと観測telemetryは設計上の予定と区別する。小変更でsubagentを強制しない。利用者が求めた実装完了を調査・別record・PR作成で置き換えない。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | 対象3 Markdownの指示を更新。実行コードは変更なし |
| Verification | Verified | skill 2件・DDD 2 record・agent audit、差分と反例review成功 |
| Deployment/operation | Not applicable | skillのローカル指示変更のみ。本番操作なし |

## Documentation updates

- `.codex/skills/document-driven-development/SKILL.md`: 設計承認前のagent割当とparent完了責任。
- `.codex/skills/document-driven-development/references/change-record-format.md`: task plan記載要件へ反映。
- `.codex/skills/agent-task-orchestration/SKILL.md`: 設計時roleと実行時ID、検証責任、parent ACとの接続。

frontmatter、agent UI設定、validatorコード、model既定値、本番権限は変更しない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 設計承認前に役割・tier・scope・依存・検証・昇格を記載する指示がformatと両skillで整合する | T1 | diff review / skill validators | Verified |
| AC2 | 単一Lead作業を許容し、予定modelと観測modelを混同せず、本番権限を拡張しない | T1 | 小変更/未起動/本番待ちの反例review | Verified |
| AC3 | 実装/復旧が目的なら未解決原因を別recordへ移してもparent完了にしない | T1 | 収集recordへの適用と依存review | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | skill/formatを最小更新し検証 | Main | Lead | 利用者の明示依頼 | 上記3 Markdownと本record | quick_validate / DDD validator / diff / 反例review | 下記Verification record | Verified | Lead — single short task、指示整合性の最終判定 | none | unavailable; retries 0; corrections 1; reviews 1 |

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | エージェント記載を必須とすると小変更にも委譲を強制し得る | 過剰な工数 | Lead単独の簡潔な記載を許容 | AC2/T1/小変更反例 | 推奨 | 明示依頼の範囲内で適用 | Resolved in design |
| C2 | 設計上のmodel予定や完了目標を実行実績・本番権限と混同し得る | 偽完了・無許可操作 | 予定/観測を分離、既存の操作gate維持 | AC2,AC3/T1/未起動と本番待ち反例 | 推奨 | 明示依頼の範囲内で適用 | Resolved in design |

model固定、共有write scope、適用範囲も確認し、利用者指示外へのpolicy拡張は行わない。

## Verification record

- `python -X utf8 .../skill-creator/scripts/quick_validate.py` を両skillへ実行: 2件ともvalid。初回はWindowsのcp932読み込みで一方が失敗したため、skill内容を変更せずUTF-8 modeで再検証した。
- DDD validator: 本recordと収集recordでissues=0。初回のconcern ledger形式不備を修正し、元gateを再実行した。
- `scripts/audit_agent_execution.py`: 両record valid。`git diff --check`: 問題なし（既存Git設定に基づくCRLF警告のみ）。
- Main反例review: (1) 単一小変更はLead単独で記載できる、(2) 未起動workerのrole予定は観測model/成功値にしない、(3) 子recordへ移った原因と配備待ちはparent未完了で残る。委譲/権限の自動拡大なし。
- 収集recordではT2f/AC7/T3cを追加して実際に適用した。収集本体はProposed・未実装/本番未確認のまま。本skill変更のImplementedを収集改修完了と解釈しない。
