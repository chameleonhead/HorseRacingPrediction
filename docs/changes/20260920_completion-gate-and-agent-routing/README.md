# Completion gate and agent routing review

- Status: Proposed
- Change record schema: 2
- Owner: HorseRacingPrediction team
- Created: 2026-09-20
- Updated: 2026-09-20

## Outcome

Replace the prose-only pre-handoff check with an executable completion mode, and clarify when cost-sensitive agents are eligible without weakening the existing persistence/concurrency risk boundary.

## Evidence and cause

- Incorrect outcome: a final response reported implementation progress while approved tasks T5-T8 were still `In progress` or `Dependent`.
- Immediate cause: the lead treated a focused build as a stopping point and did not reconcile the task and AC ledgers.
- Existing rule: DDD and `learn-from-implementation-failures` already require a zero-open-item check, so the decision rule was present.
- Reusable gap: the validator only diagnoses internally inconsistent records; it has no pre-handoff mode that fails merely because an otherwise valid `Approved` record still has open work.
- Routing evidence: the delegated persistence slice recorded requested model `runtime-default` and observed model unavailable. It touched EF persistence, migration, and snapshot files. The orchestration skill excludes persistence/migration from the lowest-cost coding route. Independent review found four integration/concurrency defects in lead-owned cross-store behavior, not an attributable low-cost worker defect.

## Concern and agreement ledger

| ID | Concern | Evidence and impact | Recommended disposition | Alternatives | Residual risk | AC/task/counterexample | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | A prose gate can be skipped again. | The prior rule existed but was not executed. | Add `--require-complete` to the change-record validator and require it immediately before final handoff. | Add another reminder only. | A command can still be omitted, so the skill must name it as the final-response gate. | AC1/T1; Approved record with open task must fail. | Recommend executable gate. | Pending | Open decision |
| C2 | Forcing cheap models onto persistence work can increase total rework. | The bounded worker still owned EF schema/migration files; four later defects were cross-store concurrency/integration issues. | Keep persistence, migration, concurrency, and final acceptance out of the low-cost coding route. | Always use the cheapest worker first. | Model telemetry is unavailable, so no model-quality ranking can be proven. | AC2/T2; persistence task is ineligible. | Keep current risk boundary. | Pending | Open decision |
| C3 | Read-only discovery may use more capability than necessary. | Inventory tasks were bounded and non-writing, but routing/model telemetry was not recorded. | Make explorer/cost-sensitive routing the default candidate for bounded read-only inventory, while recording telemetry as unavailable when not exposed. | Keep all exploration at inherited lead tier. | Fewer than five comparable measured samples prevent a persistent model-performance conclusion. | AC2/T2; read-only inventory eligibility. | Clarify eligibility, do not claim measured savings. | Pending | Open decision |

## Acceptance criteria

| ID | Criterion | Task | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | A pre-handoff validator mode exits nonzero unless the record is `Implemented`, all ACs are `Verified`, and every approved task, closure finding, and verification failure is closed. | T1 | Validator unit tests cover open Approved, open task/finding/failure, and complete records. | Not started |
| AC2 | Agent routing explicitly prefers explorer/cost-sensitive routing for bounded read-only or mechanical low-risk work, while excluding persistence/migration/concurrency and retaining telemetry honesty. | T2 | Skill diff and routing counterexamples pass skill validation. | Not started |
| AC3 | The failure analysis states that the prior interruption was an execution failure plus missing enforcement, without attributing unobserved model identity or cost. | T3 | Change-record validator and final diff review. | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Add and test strict pre-handoff validation mode. | Main | Lead | Approval | DDD validator, tests, DDD skill | Unit tests and real change-record counterexamples | Strict mode rejects open work and accepts completed record. | Proposed | Lead — workflow contract and completion semantics | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2 | Clarify low-cost routing eligibility and exclusions. | Main | Lead | Approval | agent-task-orchestration skill and tests/validator as applicable | Skill validator and scenario review | Read-only eligibility and persistence exclusion are explicit. | Proposed | Lead — persistent routing policy | none | unavailable; retries 0; corrections 0; reviews 0 |
| T3 | Record evidence, validate skills, and independently review. | Main | Lead/Review | T1-T2 | this change record, read-only review | skill-creator validator, change-record validator, final review | No open items and no unsupported model/cost claim. | Proposed | Review — independent process challenge | pending | unavailable; retries 0; corrections 0; reviews 0 |

## Review gates

- **Design and task-split review:** This is a small but persistent workflow-policy change. Main retains implementation because validator semantics and routing policy are shared repository contracts.
- **Concern and agreement review:** C1-C3 are open pending user approval. No production code or external state is affected.
- **Pre-implementation review:** Pending approval.
- **Checkpoint review:** Pending implementation.
- **Final review:** Pending strict validator, skill validation, and independent counterexamples.

## Documentation updates

- Update `.codex/skills/document-driven-development/SKILL.md` with the executable final-response command.
- Update `.codex/skills/agent-task-orchestration/SKILL.md` with bounded read-only routing preference and the existing high-risk exclusions.
- Update `learn-from-implementation-failures` only if implementation shows its existing enforcement guidance is insufficient; avoid duplicating the same rule across skills.

## Rollback

Revert the validator option and skill wording together. Existing change records remain valid because strict mode is opt-in for final handoff rather than historical validation.
