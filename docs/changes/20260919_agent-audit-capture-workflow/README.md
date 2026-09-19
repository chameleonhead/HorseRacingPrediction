# Agent audit capture workflow

- Status: Proposed
- Change record schema: 2
- Owner: HorseRacingPrediction team
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Design | Proposed | Root cause and corrective workflow are documented for approval. |
| Code | Not started | Skill, template/reference, and validator changes are approval-gated. |
| Verification | Not started | Validator fixtures and independent forward scenarios remain. |

## Context

The `20260919_collector-cost-reduction` change retained implementation and verification evidence but did not retain the orchestration audit required by repository policy. Its task table omitted read scope, write scope, deliverable/completion evidence, usage/effort, model-verification state, review effort, rework, and per-stream routing decisions. No delegated task existed, so the absence of delegated `agent-audits/*.json` was valid; the absence of a complete compact orchestration record was not.

The same run demonstrated why the missing data matters. Full-suite verification exposed an obsolete test, and later self-review found a dead branch, an incorrect latest-attempt query, and stale design text. These were fixed, but the record cannot quantify when they were found, correction/reverification effort, or whether a separate reviewer would have reduced outcome cost.

## Verified causes

1. `agent-task-orchestration` describes a compact handoff record but does not require creating it before implementation starts.
2. Detailed JSON and the existing reference are explicitly delegated-task oriented. The agent incorrectly treated “no delegation” as “no orchestration audit.”
3. The task table could omit required columns without any executable validation gate.
4. The repository instructions reference `scripts/audit_agent_execution.py`, but that script is absent.
5. Usage and model metadata were unavailable, but the workflow did not force explicit `unavailable` values and reasons; omission was therefore silent.
6. Verification failures were narrated in prose instead of being opened and closed in a failure ledger as they occurred.

## Proposed workflow

### 1. Audit-first task ledger

Before implementation, every multi-task change creates a ledger with: ID, objective, owner, tier, dependencies, read scope, write scope, deliverable, verification, completion evidence, state, routing reason, usage/effort availability, model-verification state, review/rework, and lead decision. Unknown telemetry is recorded as `unavailable` with a reason, never omitted.

This compact ledger applies whether work is delegated or lead-only. Delegated coding additionally requires the existing versioned JSON artifact per attempt.

### 2. State-transition updates

Audit data is updated when a task changes state, not reconstructed at the end:

- `Runnable`/`In progress`: record routing decision, start revision, expected scope, and verification command.
- Worker completion or lead checkpoint: record patch attribution, observed model source, usage availability, review findings, corrections, and re-verification.
- Verification failure: immediately open a failure-ledger row containing the original command and observed result.
- `Verified`: require completion evidence, closed failures, review decision, and effort/telemetry availability fields.

### 3. Validator-backed gates

Add a repository validator that checks changed active records for the compact ledger and state-dependent required fields. It also validates delegated JSON artifacts against the documented schema. The validator must distinguish:

- lead-only tasks: no delegated JSON required, compact audit still required;
- delegated coding: compact audit plus one JSON per attempt;
- unavailable telemetry: allowed only with an explicit reason;
- historical records: diagnostic by default, without rewriting history;
- verification failures: no `Verified` task while a linked failure is open.

The exact validator command becomes part of pre-implementation, checkpoint, and final-review gates in `agent-task-orchestration` and `document-driven-development`.

### 4. Routing-decision evidence

“No delegation” must be evaluated per separable workstream. A blanket cross-cutting label is insufficient when read-only exploration, fixture work, or independent review has a disjoint scope. For every lead-owned stream, record the applicable reason: architecture/public contract, persistence, destructive operation, unresolved ambiguity, overlapping writes, unavailable worker, or delegation cost exceeding benefit.

### 5. Forward validation

Validate the revised workflow against two isolated scenarios:

1. A lead-only cross-cutting change: validator accepts explicit unavailable telemetry and no delegated JSON, but rejects missing routing/review fields.
2. A mixed delegated change: validator rejects a missing attempt audit, missing observed-model source/reason, overlapping write scopes, and an open verification failure; it accepts the corrected artifacts.

## Material concerns

| ID | Concern | Impact | Proposed disposition | State |
| --- | --- | --- | --- | --- |
| C1 | Requiring full delegated JSON for lead-only work would add high overhead and false data. | Small changes become process-heavy or invent telemetry. | Use compact ledger for all multi-task work; JSON only for delegated coding. | Resolved in design |
| C2 | Retrofitting all historical records would rewrite history and create unverifiable estimates. | Audit data becomes misleading. | Validate changed/active records; historical records remain diagnostic unless explicitly repaired from evidence. | Resolved in design |
| C3 | Token/model telemetry may not be exposed. | A strict presence check could encourage fabrication. | Require availability state and reason; keep measurements null. | Resolved in design |
| C4 | Markdown-only checks can become brittle. | Formatting changes cause false failures. | Validate structured headings/tables and linked JSON semantics, not exact prose or column spacing. | Resolved in design |
| C5 | A validator alone cannot prove good routing. | Formally complete but weak decisions may pass. | Require final lead review and independent forward scenarios; validator checks evidence presence and invariants only. | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | The orchestration skill requires audit creation before implementation and update at every task state transition for delegated and lead-only multi-task work. | T1 | Skill diff and scenario review. | Not started |
| AC2 | Lead-only tasks explicitly retain routing reason, telemetry availability, review/rework, completion evidence, and lead decision without requiring delegated JSON. | T1,T2 | Lead-only validator fixture. | Not started |
| AC3 | Delegated coding retains one schema-valid JSON artifact per attempt and distinguishes requested from observed model data. | T1,T2 | Delegated validator fixtures. | Not started |
| AC4 | Missing required fields, overlapping active write scopes, missing delegated attempts, and open verification failures cause validator failure. | T2 | Negative fixtures. | Not started |
| AC5 | Explicit unavailable telemetry with a reason, valid non-overlapping tasks, and closed failure records pass. | T2 | Positive fixtures. | Not started |
| AC6 | DDD checkpoint/final gates invoke the validator, while historical records remain diagnostic rather than silently rewritten. | T3 | Skill diff and historical fixture. | Not started |
| AC7 | Both forward scenarios produce the intended accept/reject decisions, and the skill validator passes. | T4 | `quick_validate.py`, audit validator tests, independent scenario review. | Not started |
| AC8 | The original Collector change receives an evidence-limited retrospective entry without invented tokens, model identity, or elapsed time. | T5 | Record diff and final review. | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Add audit-first and state-transition gates to orchestration instructions/reference. | Main | Lead | Approval | `.codex/skills/agent-task-orchestration/` | Skill validator and scenario inspection | Required fields and timing are explicit. | Proposed |
| T2 | Implement compact-ledger and delegated-attempt validator with positive/negative fixtures. | Worker candidate; Main integrates | Worker/Lead | T1 contract | `scripts/`, validator tests/fixtures | Automated validator tests | Every AC2-AC5 counterexample is exercised. | Dependent |
| T3 | Connect the validator to DDD checkpoint/final gates and document the canonical command. | Main | Lead | T2 | `.codex/skills/document-driven-development/` | Skill validator | DDD references one executable gate. | Dependent |
| T4 | Run independent lead-only and mixed-delegation forward scenarios. | Review worker candidate | Review | T1-T3 | Read-only; isolated temp artifacts | Scenario outcomes | Independent accept/reject evidence. | Dependent |
| T5 | Append an evidence-limited retrospective audit to the Collector record. | Main | Lead | T1-T4 | `docs/changes/20260919_collector-cost-reduction/` | Record validation | Missing fields say unavailable; no estimates. | Dependent |

## Review gates

- **Design/task-split review:** Main reconstructed the failure from the original record, orchestration skill, DDD skill, and absent validator. Validator implementation is separable after the schema is frozen; policy, integration, and final acceptance remain with Main.
- **Concern/agreement review:** C1-C5 cover overhead, historical integrity, missing telemetry, brittle validation, and formal-compliance risk. No open technical decision remains; user approval is required before implementation.
- **Pre-implementation review:** Pending approval. It will freeze the compact ledger shape, assign non-overlapping scopes, and record exact worker inputs and escalation conditions.
- **Checkpoint review:** Pending.
- **Final review:** Pending.

## Documentation updates

- Proposed updates: `.codex/skills/agent-task-orchestration/SKILL.md` and its execution-audit reference become the canonical orchestration recording workflow.
- Proposed update: `.codex/skills/document-driven-development/SKILL.md` invokes the executable audit gate.
- No canonical source is changed before approval.

## Rollback

If the validator produces excessive false failures, revert the validator and skill changes together. Retain this record and its fixtures as evidence. Rollback must not weaken the existing prohibition on invented telemetry or remove delegated-task audit requirements.
