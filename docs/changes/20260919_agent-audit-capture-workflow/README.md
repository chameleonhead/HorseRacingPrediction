# Agent audit capture workflow

- Status: Implemented
- Change record schema: 2
- Owner: HorseRacingPrediction team
- Created: 2026-09-19
- Updated: 2026-09-20

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Design | Verified | User approved the compact workflow and requested implementation plus evaluation on 2026-09-20. |
| Code | Verified | Compact policy, validator, fixtures, DDD integration, and Collector retrospective are complete. |
| Verification | Verified | 26 tests, record validation, changed discovery, skill validation, diff checks, and independent acceptance review passed. |

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

### 1. Reuse the task plan; do not create a duplicate ledger

The existing DDD task plan remains the single human-readable ledger. Add only three compact fields when orchestration applies: `Routing`, `Audit`, and `Result metrics`. Do not repeat objective, scope, verification, evidence, or state in a second table.

`Routing` states lead/worker/reviewer and a short reason. `Audit` is `none` for lead-only or links delegated JSON. `Result metrics` uses a compact tuple: `usage availability; retries; lead corrections; review passes`. A successful lead-only task normally adds no JSON and no narrative audit.

### 2. Record only meaningful lifecycle events

Do not update audit text for every ordinary state transition. Record at these boundaries only:

- delegation dispatch: create one JSON attempt with scope, requested route, start revision, and telemetry availability;
- worker completion: add outcome, retries, correction counts, verification, and observed telemetry if available;
- material verification failure: open one failure-ledger row and close it after the original gate succeeds;
- final review: aggregate the four compact result metrics; do not reproduce tool logs.

Commentary updates, ordinary successful commands, and state changes without new evidence create no audit entry.

### 3. Validator-backed gates

Add a repository validator that checks changed active records by extending the existing task plan rather than requiring a second large table. It validates delegated JSON only when a task actually delegates. The validator must distinguish:

- lead-only tasks: no delegated JSON and no per-attempt metrics required;
- delegated coding: compact audit plus one JSON per attempt;
- unavailable telemetry: allowed only with an explicit reason;
- historical records: diagnostic by default, without rewriting history;
- verification failures: no `Verified` task while a linked material failure is open.

The validator must not require active-minute estimates, token fields the runtime does not expose, prose explanations repeated per row, or audit artifacts for short lead-only tasks.

The exact validator command becomes part of pre-implementation, checkpoint, and final-review gates in `agent-task-orchestration` and `document-driven-development`.

### 4. Routing-decision evidence

Record one short routing reason per task. Do not write a separate justification document. For a small lead-only change, `Lead — single short task` is sufficient. For multi-task work, a blanket cross-cutting label remains insufficient when a clearly separable worker/review stream exists.

### 5. Forward validation

Validate the revised workflow against two isolated scenarios and measure audit size:

1. A lead-only multi-task change: the extended task plan is sufficient and produces no JSON.
2. A mixed delegated change: only delegated attempts create JSON; missing attempt audit, overlapping active write scopes, and open material failures are rejected.

For the fixture changes, the compact Markdown audit additions must remain below 20 nonblank lines and the normal successful delegated JSON must stay below 2,500 UTF-8 bytes unless provider telemetry itself exceeds that bound. If the bound is exceeded, simplify the schema rather than accepting recurring overhead.

## Material concerns

| ID | Concern | Impact | Proposed disposition | State |
| --- | --- | --- | --- | --- |
| C1 | Requiring full delegated JSON for lead-only work would add high overhead and false data. | Small changes become process-heavy or invent telemetry. | Use compact ledger for all multi-task work; JSON only for delegated coding. | Resolved in design |
| C2 | Retrofitting all historical records would rewrite history and create unverifiable estimates. | Audit data becomes misleading. | Validate changed/active records; historical records remain diagnostic unless explicitly repaired from evidence. | Resolved in design |
| C3 | Token/model telemetry may not be exposed. | A strict presence check could encourage fabrication. | Require availability state and reason; keep measurements null. | Resolved in design |
| C4 | Markdown-only checks can become brittle. | Formatting changes cause false failures. | Validate structured headings/tables and linked JSON semantics, not exact prose or column spacing. | Resolved in design |
| C5 | A validator alone cannot prove good routing. | Formally complete but weak decisions may pass. | Require final lead review and independent forward scenarios; validator checks evidence presence and invariants only. | Resolved in design |
| C6 | Detailed per-task ledgers can consume more tokens than the implementation decision they measure. | Audit becomes the dominant cost and discourages useful delegation. | Reuse the task plan, record only dispatch/completion/material failure/final aggregation, cap fixture audit size, and omit lead-only JSON. | Resolved in design and independently measured |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | The orchestration skill reuses the DDD task plan and records only delegation dispatch, worker completion, material verification failure, and final aggregation. | T1 | Skill diff and scenario review. | Verified |
| AC2 | Lead-only tasks require no JSON; a short routing reason in the task plan is sufficient. | T1,T2 | Lead-only validator fixture. | Verified |
| AC3 | Delegated coding retains one schema-valid JSON artifact per attempt and distinguishes requested from observed model data. | T1,T2 | Delegated validator fixtures. | Verified |
| AC4 | Missing delegated attempts, overlapping active write scopes, and open material verification failures cause validator failure. | T2 | Negative fixtures. | Verified |
| AC5 | Unavailable provider telemetry remains null with one compact reason; valid task plans and closed failures pass. | T2 | Positive fixtures. | Verified |
| AC6 | DDD checkpoint/final gates invoke the validator, while historical records remain diagnostic rather than silently rewritten. | T3 | Skill diff and historical fixture. | Verified |
| AC7 | Both forward scenarios produce the intended decisions, skill validation passes, Markdown audit additions stay below 20 nonblank lines, and normal delegated JSON stays below 2,500 bytes. | T4 | Skill/validator tests, size assertions, independent review. | Verified |
| AC8 | The original Collector change receives an evidence-limited retrospective entry without invented tokens, model identity, or elapsed time. | T5 | Record diff and final review. | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Add compact lifecycle gates to orchestration instructions/reference. | Main | Lead | Approval | `.codex/skills/agent-task-orchestration/` | Skill validator and scenario inspection | Compact policy passes validation. | Verified | Lead — policy decision | none | unavailable; retries 0; corrections 0; reviews 2 |
| T2 | Implement compact task-plan/delegated-attempt validator and fixtures. | audit-validator worker; Main integrates | Worker/Lead | Frozen schema | `scripts/audit_agent_execution.py`; `tests/scripts/test_audit_agent_execution.py` | Automated positive/negative tests and size assertions | 25 tests, both records, changed discovery, and size gates pass. | Verified | Worker — bounded code | `T2-A1.json` | unavailable; retries 1; corrections 3; reviews 5 |
| T3 | Connect the validator to DDD gates. | Main | Lead | T2 | `.codex/skills/document-driven-development/` | Skill validator and actual record validation | DDD invokes compact gate and skill validates. | Verified | Lead — canonical workflow | none | unavailable; retries 0; corrections 0; reviews 2 |
| T4 | Evaluate lead-only and delegated workflows independently, including audit cost. | Review worker | Review | T1-T3 | read-only | Forward scenarios and size/coverage assessment | Final independent review accepted after four evidence-driven corrections. | Verified | Reviewer — independent challenge | `T4-A1.json`, `T4-A2.json`, `T4-A3.json`, `T4-A4.json`, `T4-A5.json` | unavailable; retries 0; corrections 0; reviews 5 |
| T5 | Add a compact evidence-limited retrospective to the Collector record. | Main | Lead | T4 | `docs/changes/20260919_collector-cost-reduction/` | Record validation | Historical unknowns are explicit; record validates. | Verified | Lead — historical evidence | none | unavailable; retries 0; corrections 0; reviews 1 |

## Review gates

- **Design/task-split review:** Main reconstructed the failure from the original record, orchestration skill, DDD skill, and absent validator. Validator implementation is separable after the schema is frozen; policy, integration, and final acceptance remain with Main.
- **Concern/agreement review:** C1-C5 cover overhead, historical integrity, missing telemetry, brittle validation, and formal-compliance risk. No open technical decision remains; user approval is required before implementation.
- **Pre-implementation review:** User approved the summarized AC1-AC8 and C1-C5 on 2026-09-19. The compact ledger in this record is frozen. Main owns policy files, records, integration, and final acceptance. `audit-validator` owns only the validator and its tests; it must not edit skills or change records. Required evidence is positive/negative tests covering lead-only, delegated-attempt, unavailable telemetry, overlapping active scopes, and open failures. Ambiguity, schema expansion, or failed verification escalates to Main. T1 and T2 are runnable with disjoint write scopes; T3-T5 remain dependent.
- **Checkpoint review:** Validator lifecycle, reconciliation, discovery, and false-positive findings CF1-CF11 were corrected with counterexamples; all failure rows are closed.
- **Final review:** T4-A5 independently accepted the corrected workflow. All AC1-AC8 and task rows are `Verified`; no accepted-scope blocker remains.

### Design revision checkpoint — 2026-09-20

The user objected that the first approved audit ledger was too detailed and could consume disproportionate tokens. Main agrees: the 17-column duplicate ledger demonstrates the concern. Implementation is paused with existing edits preserved. The revised design removes the duplicate ledger, reduces recording to four lifecycle events and four aggregate metrics, omits lead-only JSON, and adds explicit size bounds. Renewed approval is required before adapting the partial implementation.

- **Re-approval:** On 2026-09-20 the user authorized the compact implementation and requested an evaluation of its effectiveness and overhead. T1-T2 are `In progress`; T3-T5 are dependency-gated.

## Verification failure ledger

| ID | Task | Original command | Observed failure | Classification | Disposition | Rerun | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| F1 | T1 | `python C:/Users/yuto.nagano/.codex/skills/.system/skill-creator/scripts/quick_validate.py .codex/skills/agent-task-orchestration` | Python 3.13 decoded the UTF-8 skill as CP932 and raised `UnicodeDecodeError`; the second ASCII-only skill validated. | Environment mismatch | Reran both skill validators with `PYTHONUTF8=1`; both returned `Skill is valid!`. No skill content was changed for the host code page. | `PYTHONUTF8=1 python .../quick_validate.py <skill>` — passed for both skills | Verified |
| F2 | T2 | `python scripts/audit_agent_execution.py docs/changes/20260919_agent-audit-capture-workflow` | Validator required an attempt JSON for delegated T4 even though its state was `Dependent` and no attempt had started. | Deterministic validator regression | Attempt IDs are now required only after a delegated task becomes active or verified; the dependent-worker counterexample and full validator tests pass. | Original record command reached only the intentionally open F2 gate; after recording this closure it passed. | Verified |
| F3 | T2 | `Get-Item scripts/audit_agent_execution.py,tests/scripts/test_audit_agent_execution.py` | Compact-rewrite worker remained running without response and both owned files were absent during replacement. | Delegation execution failure | Worker was interrupted once, restored both files, completed the compact rewrite, and all 12 tests plus actual-record validation passed. | Original file-existence check and full validator gates now pass | Verified |
| F4 | T2 | `python scripts/audit_agent_execution.py docs/changes/20260919_agent-audit-capture-workflow/README.md` | A delegated read-only review normalized Task-plan scope to empty but compared JSON `read-only` literally, causing a false mismatch. | Deterministic validator regression | JSON and Task-plan scopes now share the same read-only normalization; the delegated read-only counterexample and 13-test suite pass. | Original record command passed after this closure | Verified |
| F5 | T4 | `python scripts/audit_agent_execution.py docs/changes/20260919_agent-audit-capture-workflow/README.md` | Task plan counted the second independent review attempt as a worker retry, while delegated JSON defines retries as correction retries within attempts. | Record semantics mismatch | Retries remain 0; repeated independent review is represented by two Audit IDs and review-pass counts. | Original record command passed | Verified |
| F6 | T4 | `python scripts/audit_agent_execution.py docs/changes/20260919_agent-audit-capture-workflow/README.md` | T4 was marked `Verified` while its final review attempt correctly had decision `revise`. | Record semantics mismatch | T4 remains `In progress` until a final accepted review attempt closes the findings. | Original record command passed | Verified |

## Closure findings

| ID | Task | Finding | Evidence | Correction | Counterexample | State |
| --- | --- | --- | --- | --- | --- | --- |
| CF1 | T2 | The existing delegated JSON schema accepts only completed attempts, conflicting with audit creation at dispatch time. | `completedAt`, final quality gates, and final verdict are required by the detailed validator. | Active tasks now validate the started subset; `Verified` tasks validate the complete schema. Completion data is never prefilled. | Active worker with null completion fields and explicit unavailable telemetry passes; the full 13-test suite passes. | Verified |
| CF2 | T2 | Task-plan metrics are not cross-checked with delegated JSON; T2 already recorded reviews 5 versus JSON 4. | Independent T4 review and current artifacts. | Validator now compares usage availability, retries, corrections, and reviews. | Mismatch fixtures fail; actual records pass. | Verified |
| CF3 | T1,T2 | Compact JSON omits model-unavailable reason and required difficulty/gate/challenge/promotion/escaped-defect/review-effort availability. | Independent T4 comparison with repository orchestration policy. | Compact scalar/null fields were added while retaining the bound. | Missing fields fail; current JSON stays below 1,500 bytes. | Verified |
| CF4 | T2 | JSON-only changes are absent from default changed-record discovery; duplicate/malformed orphan audits may be ignored. | Independent T4 review of `_git_paths` and audit indexing. | Changed JSON maps to parent README; duplicates and malformed/orphan audits fail. | `--changed` and targeted fixtures pass. | Verified |
| CF5 | T2 | Delegation detection and scope parsing have bounded false-negative risks; `sys.stderr` lacks an import on an error path. | Independent T4 source review. | Explicit Audit IDs establish delegation, wildcard scope is rejected, and `sys` is imported. | Reviewer/alias, wildcard, and git-error fixtures pass. | Verified |
| CF6 | T2 | Requiring every historical attempt to succeed prevents a failed attempt followed by a successful retry from ever reaching `Verified`. | T4-A2 independent review. | All attempts must complete; only the final attempt must accept with all gates true. | Failed A1 plus accepted A2 passes. | Verified |
| CF7 | T1,T2 | Active attempts prefill review telemetry as unavailable before review completes. | T4-A2 and active T4-A2 JSON. | Active review/elapsed telemetry stays null; completed unavailable data requires a reason. | Active and completed lifecycle fixtures pass. | Verified |
| CF8 | T1,T2 | Worker elapsed effort availability is absent. | T4-A2 comparison with orchestration policy. | Added compact elapsed availability/minutes/reason. | Measured and unavailable fixtures pass. | Verified |
| CF9 | T2 | Completed delegated Task-plan metrics can say unavailable even when JSON has numeric results, bypassing reconciliation. | T4-A2 validator review. | Completed delegated metrics must be numeric and equal JSON aggregates. | Unavailable-vs-numeric fixture fails. | Verified |
| CF10 | T2 | Free-text `review` in lead-only Routing was treated as delegation and required JSON. | T4-A3 independent review reproduced `Lead: final review` as a false positive. | Review terms imply delegation only in Owner/Model tier or an explicitly `Reviewer`-prefixed Routing value; worker/delegated signals remain accepted. | Independent direct counterexample passed; corrected automated regression test exercises the same text. | Verified |
| CF11 | T2 | The first CF10 regression test replaced a string absent from its fixture and therefore retested the unchanged row. | T4-A4 independent review inspected the fixture and test expression. | Replace the actual fixture text and assert the intended routing text is present before validation. | Corrected test and T4-A5 independent counterexamples pass. | Verified |

## Implementation and evaluation

- The task plan adds three columns without adding fixture lines: 7 nonblank lines both before and after; the representative two-task fixture grows by 220 UTF-8 bytes (398 to 618).
- Compact delegated audit payloads measured 954-1,231 bytes, less than half of the 2,500-byte limit. Lead-only tasks create no JSON.
- Required outcome, retry, correction, review, scope, route, difficulty, elapsed availability, and telemetry-availability fields are retained. Unexposed model, token, and elapsed values remain null with a reason rather than being estimated.
- Five independent review attempts found and closed lifecycle, reconciliation, discovery, routing, and test-quality defects. The final attempt accepted the workflow with no open material finding.

## Verification results

- `python tests/scripts/test_audit_agent_execution.py`: 26 passed.
- `python -m py_compile scripts/audit_agent_execution.py tests/scripts/test_audit_agent_execution.py`: passed.
- Explicit workflow and Collector record validation plus `--changed`: passed.
- `quick_validate.py` for both modified skills with UTF-8 mode: passed.
- `git diff --check`: passed; line-ending warnings only.

## Documentation updates

- Proposed updates: `.codex/skills/agent-task-orchestration/SKILL.md` and its execution-audit reference become the canonical orchestration recording workflow.
- Proposed update: `.codex/skills/document-driven-development/SKILL.md` invokes the executable audit gate.
- No canonical source is changed before approval.

## Rollback

If the validator produces excessive false failures, revert the validator and skill changes together. Retain this record and its fixtures as evidence. Rollback must not weaken the existing prohibition on invented telemetry or remove delegated-task audit requirements.
