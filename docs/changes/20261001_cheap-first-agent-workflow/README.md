# Cheap-first coding-agent workflow

- Status: Implemented
- Change record schema: 2
- Owner: Main agent / user
- Created: 2026-10-01
- Updated: 2026-10-03

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Policy edits | Complete | The orchestration skill now covers ordinary serialized repository work and defines an explicit first-implementation-write gate. |
| Verification | Complete | T8-A2 accepted AC9-AC10 after five independent scenarios and all mechanical gates passed. |
| Deployment/operation | Not applicable | No application or runtime changes. |

## Context and goals

The repository already requests Luna/high for eligible delegated work, but current rules still permit routine execution and failed verification to move broadly to the Lead/high tier. This proposal makes Router, Cheap Planner, Cheap Executor, Verifier, and Strong Planner explicit workflow roles. They are logical functions, not five required spawned agents. Cheap Planner and Executor may be the same `gpt-6-luna` / `high` agent.

The Luna/high default is an explicit user-directed preference, not a proven cost or quality claim. Keep requested model, observed model, and telemetry availability distinct. The five-comparable-outcome rule applies to efficiency claims, not as a gate against the requested policy update.

## Decisions

1. **Router and cheap route.** Router considers ambiguity, risk, dependencies, and verifiability, not file count. Conventional existing-pattern changes, multi-file mechanical work, tests, and clear fixes use the cheap route. Strong is not the default executor.
2. **Plan before edits.** Cheap Planner first inspects the target, similar implementations, conventions/dependencies, tests, and repository guidance. Its short plan records purpose, findings, intended files, steps, unresolved specification questions, and verification commands. Resolve repository questions by inspection. Ask only when repository evidence cannot resolve a material specification choice.
3. **Strong Planner boundary.** Use a high-capability model for unresolved material ambiguity, architecture/public-contract tradeoffs, design conflicts, or decomposition that remains too broad for a cheap executor. It analyzes and returns bounded, ordered executable slices to Cheap Executor; it does not implement. Cheap unavailability does not silently authorize Strong implementation; keep ordinary implementation blocked pending an eligible executor.
4. **Cause-based retries.** After a failed gate, Cheap classifies the cause. Local compile/test/pattern/implementation defects receive cheap correction and rerun. Different causes do not add up to escalation. Around two attempts with the same cause trigger Router reassessment, not automatic promotion. Strong planning is warranted only when evidence shows a design conflict, material ambiguity, architecture limitation, or unshrinkable task.
5. **Scope and governance.** Pause edits in an affected slice when scope grows; replan internally and continue independent approved slices. Renew approval only for changed requirements, external contract, acceptance criteria, or material risk. Preserve DDD approval, AC/task traceability, exclusive ownership, mechanical evidence, audit, independent review, failure closure, and accountable Lead acceptance. Lead accountability does not require a high-capability model.
6. **Verification and measurement.** Name applicable build/test/formatter/lint/type/architecture commands and expected results. LLM self-assessment cannot pass a task. Use audits for outcome observation; do not infer model, usage, or cost. Reconsider routing for material quality/scope/security/data/false-completion failures or repeated evidence of unacceptable rework.

## Documentation updates

- `AGENTS.md`: conflicting high-model execution, final-review, retry, and sampling rules were replaced with this single policy; unrelated safeguards remain.
- `.codex/skills/agent-task-orchestration/SKILL.md`: generic tier/escalation text (including `verification fails after one focused correction`) was replaced with the role flow, mandatory short plan, cause-based Router reassessment, and Strong planning-only boundary.
- `.codex/skills/document-driven-development/SKILL.md`: Lead final acceptance is accountable review, not mandatory high-model usage; DDD gates remain.
- `.codex/skills/agent-task-orchestration/references/execution-audit.md`: user-directed default changes are distinguished from evidence-based efficiency recommendations; telemetry rules remain.
- Historical records `docs/changes/20260914_model-tiered-agent-orchestration/README.md` and `docs/changes/20260927_default-luna-coding-routing/README.md` remain unchanged as historical evidence, not current policy.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Cheap Luna/high is the ordinary implementation, correction, and mechanical-verification route; Strong does not implement routine work. | T2-T5 | Policy review; S1-S2 | Verified |
| AC2 | Every coding task has a repository-informed concise plan before edits, with findings, files, steps, questions, and checks. | T3-T5 | Policy review; S3-S4 | Verified |
| AC3 | Logical roles do not require five spawns; Strong returns executable ordered tasks to Cheap. | T2-T5 | Policy review; S7 | Verified |
| AC4 | Failure handling is cause-based; different causes may stay cheap and repeated same-cause failure triggers reassessment, not automatic escalation. | T2-T5 | Policy review; S5-S6 | Verified |
| AC5 | Mechanical command evidence, not LLM self-evaluation, determines verification. | T2-T5 | Policy review; S8; validators | Verified |
| AC6 | DDD approval, ownership, AC/task traceability, independent evidence, audits, and final acceptance remain consistent. | T2-T6 | Skill/record validators; integrated review | Verified |
| AC7 | User-directed routing preference is separate from efficiency claims; requested/observed model and unavailable telemetry remain distinct. | T2-T5 | Audit schema and policy review | Verified |
| AC8 | No contradictory active routing rule remains; historical records and application code are untouched, and docs validate. | T2-T6 | Exact policy search, `git diff --check`, `git status`, validators | Verified |
| AC9 | The orchestration skill is discoverable for ordinary serialized repository development and investigation, not only work that appears worth splitting or delegating. | T7-T8 | frontmatter validation and an independent serialized-task forward scenario | Verified |
| AC10 | Before the first implementation edit, the workflow records skill loading, the repository-informed plan, route/model availability, and a passing applicable audit gate; an unavailable required executor becomes `Externally blocked` before editing. | T7-T8 | policy assertions, validator tests, and unavailable-worker forward scenario | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Inventory policy conflicts | workflow_inventory | gpt-6-luna/high requested; observed unavailable | - | Read-only + this record | Line-referenced findings | Findings below | Verified | Lead policy judgment; bounded read-only inventory | none | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | Replace active AGENTS routing policy | Cheap Executor; Lead accepts | gpt-6-luna/high | Approval | `AGENTS.md` | Policy inspection, phrase search, `git diff --check` | T2-A1 audit and diff | Verified | Cheap for frozen text edit | T2-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T3 | Replace orchestration and audit-reference routing/retry rules | Cheap Executor; Lead accepts | gpt-6-luna/high | T2 | `.codex/skills/agent-task-orchestration/SKILL.md`; `.codex/skills/agent-task-orchestration/references/execution-audit.md` | `quick_validate.py`, audit tests, S1-S9 | T3-A1/A2 audits and integrated diff | Verified | Cheap for bounded documentation | T3-A1, T3-A2 | unavailable; retries 1; corrections 0; reviews 1 |
| T4 | Clarify model-neutral DDD Lead acceptance | Cheap Executor; Lead accepts | gpt-6-luna/high | Frozen decision; separate file | `.codex/skills/document-driven-development/SKILL.md` | `quick_validate.py`, DDD tests, AC review | T4-A1 audit and integrated diff | Verified | Cheap for bounded documentation | T4-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T5 | Run independent forward scenarios and final validation | Independent Cheap Verifier | gpt-6-luna/high | T2-T4 | Read-only + this record | Validators, scenarios, diff/status checks | Independent review report and focused rerun | Verified | Read-only independent review | none | unavailable; retries 1; corrections 1; reviews 1 |
| T6 | Perform Lead integrated acceptance and reconcile record | Lead; model-neutral | no model selected | T5 | This change record | Integrated policy/AC/task review | Final acceptance decision and reconciled status | Verified | Lead final acceptance | none | unavailable; retries 0; corrections 0; reviews 1 |
| T7 | Tighten skill discovery and add the first-write orchestration gate with regression tests | Cheap Executor | gpt-6-luna/high requested; observed unavailable | AC9-AC10 approved | `.codex/skills/agent-task-orchestration/SKILL.md`<br>`.codex/skills/agent-task-orchestration/scripts` | skill quick validator; focused skill tests; unavailable-worker and serialized-task scenarios | bounded diff and executable evidence | Verified | Worker — bounded skill and semantic policy-test edit authorized by user | T7-A1, T7-A2 | unavailable; retries 0; corrections 1; reviews 2 |
| T8 | Independently verify the entry and first-write gates | Cheap Verifier | gpt-6-luna/high requested; observed unavailable | T7 | `read-only`<br>`docs/changes/20261001_cheap-first-agent-workflow` | skill validator, full orchestration tests, audit validator, `git diff --check` | AC9-AC10 independent evidence | Verified | Worker — independent read-only scenario and mechanical verification | T8-A1, T8-A2 | unavailable; retries 0; corrections 0; reviews 2 |
| T9 | Perform Lead integrated acceptance and close the reopened record | Lead | model-neutral | T8 | this record only | zero-open-item and scope review | reconciled AC/task/concern evidence | Verified | Lead final acceptance | none | unavailable; retries 0; corrections 0; reviews 1 |

## Forward tests

S1. Small conventional component change → Luna/high; short plan precedes edits.
S2. Clear multi-file DTO/mapper work → cheap route despite file count.
S3. Target/DI/test location unknown → repository search and similar-code inspection before any question.
S4. Material error/search behavior cannot be inferred → ask only for that specification decision.
S5. Namespace/import compile failure → cheap diagnosis, correction, relevant rerun.
S6. Two different causes (e.g. import then assertion) → no promotion by count; same cause near two attempts → Router reassesses.
S7. Persistent design mismatch → Strong analyzes and returns ordered cheap-executable tasks; Strong does not implement.
S8. Failed build/test or missing command output → remain incomplete; scope growth pauses the affected slice for replanning.
S9. Fragmentation increases rework → combine or resize cheap slices / have Lead review the recorded default; Strong stays limited to planning and decision support.

## Concern and agreement ledger

| ID | Evidence and impact | Proposed disposition / trace | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- |
| C1 | `AGENTS.md:94,98-100` and orchestration retry rule allow broad high-tier execution/escalation. | Replace with cheap-first route and cause-based review / AC1,4; T2-T3; S1-S7 | Recommend | Approved | Resolved in design |
| C2 | Five samples cannot justify efficiency but should not block the user's explicit route preference. | User-directed policy now; five-sample evidence only for efficiency claim / AC7; T2-T5 | Recommend | Approved | Resolved in design |
| C3 | Five spawned roles or file-count routing adds overhead. | Roles are logical; route by ambiguity/risk/verifiability / AC1,3; S1-S2 | Recommend | Approved | Resolved in design |
| C4 | Cheap-model unavailability/failure could silently make Strong the executor. | Keep ordinary implementation blocked until eligible executor; Strong is planner only / AC3-4; S7 | Recommend | Approved | Resolved in design |
| C5 | Removing high-tier defaults could accidentally weaken verification or DDD. | Preserve all governance and mechanical gates; Lead acceptance is model-neutral / AC5-6; S8 | Recommend | Approved | Resolved in design |
| C6 | Keeping old and new operational rules together creates conflicting instructions. | Replace conflicting active text; do not rewrite history / AC8; T2-T5 | Recommend deletion/replacement | Approved: delete conflicting wording | Resolved in design |
| C7 | On 2026-10-03 the dispatcher task began implementation before this skill was loaded. `AGENTS.md` requires the workflow for normal development, while this skill's description only advertises tasks that should be split or delegated. The body also lacks one compact, explicit pre-first-write checkpoint. | Expand only this skill's discovery description to ordinary repository work and add a four-fact first-write gate. Do not add a second orchestration framework or weaken DDD / AC9-AC10; T7-T9 | Recommend the narrow correction; required executor unavailability must block edits rather than silently using Lead. | Approved on 2026-10-03: 「変更をお願いします」 | Resolved in design |

## Inventory evidence

- `AGENTS.md:94,98-100` combines high-capability final audit and broad promotion/execution authority. `:101` already favors focused Luna correction; `:105-111` contain scope, testing, audit, cost, and DDD safeguards to preserve.
- Orchestration SKILL `:12-20` defines generic tiers and balanced/abstract worker routes; `:74-90` includes escalation after one focused correction; `:101-105` places evidence gates on persistent routing changes.
- DDD SKILL `:24-54` already provides approval, planning, mechanical evidence, checkpoint, and audit gates. Audit reference `:92-105` correctly separates requested and observed telemetry but currently applies five-sample restriction to persistent default changes.
- Historical records are left untouched; application code is out of scope.

## Approval and execution plan

User explicitly approved AC1-AC8 and C1-C6 on 2026-10-01: 「承認します。変更をお願いします」. All concern dispositions below are accepted as designed.

Pre-implementation review (Lead): previous investigation supplied the file inventory. Frozen decisions are D1-D6. T2-T4 had exclusive file owners and were serialized to keep canonical wording consistent; T5 is a separate read-only review. Requested execution model is gpt-6-luna/high; observed model and usage are unavailable. Worker contract: edit only assigned docs, preserve unrelated changes/history, follow approved decisions, and report changed paths, risks, and command results. Validate skills, audit/DDD records, S1-S9, `git diff --check`, and `git status`. No source tests need modification; existing validator suites and independent scenarios are sufficient evidence for prose-only policy changes. Local correction stays on Cheap; design/scope changes return to Lead and material approval changes return the record to Proposed.

Short plan, completed through T2-T4: (1) replaced the AGENTS routing section; (2) rewrote orchestration routing, planning, retry, and verifier rules; (3) aligned audit measurement and DDD final-review model wording; (4) ran validators and validator suites, then requested independent S1-S8 review. That review found one ambiguous audit phrase; it was corrected under T3-A2, which adds S9 to regression coverage. T2-T5 are Verified after the independent S1-S9 rerun and final workspace checks. No unresolved specification question remains. Material contract changes return to Proposed; local defects are corrected within scope.

## Review and verification

- Design/task split: T2-T4 had separate file ownership and were executed sequentially to keep policy wording consistent. T5 depends on all edits. Lead retains policy interpretation and final acceptance; cheap roles perform bounded edits and commands.
- Pre-implementation review: completed above before canonical edits.
- Read-only inventory requested gpt-6-luna/high; observed model and usage telemetry are unavailable.
- Alternatives considered: keeping generic high-tier execution and relying on case-by-case judgment leaves the default ambiguous; treating five samples as an approval gate blocks the user's explicit preference; spawning all five roles for every task adds unnecessary overhead. Residual risks are model availability and unknown execution telemetry; preserve explicit blocker handling and make no efficiency claim without evidence.
- After policy edits: both skill quick validators passed; the audit validator accepted this record; DDD record validation reported zero issues; the existing audit test suite passed 33 tests; `git diff --check` passed. The first independent S1-S8 review found one ambiguous audit phrase that could imply Strong implementation; it is logged as T3-A1 revise, corrected, and the focused S1-S9 rerun passed (T3-A2). AC1-AC8 and T1-T5 are Verified. T6 is Verified after Lead integrated acceptance.
- Final review: AC1-AC10 and T1-T9 are Verified. No approved work or blocking finding remains. Commit the validated policy and evidence as one purpose; no application deployment is required.

### Review finding and closure

- T3-A1: independent review found `execution-audit.md` said to “promote the route” when fragmentation increased cost, which could contradict Strong planning-only. Recorded as one escaped wording defect; corrected to have the Lead reconsider the user-directed default while retaining the Strong boundary. T3-A2 is the focused correction and verification record.
- Forward coverage adds S9 to test cost/rework deterioration without allowing automatic promotion of implementation to Strong.
- T8-A1 found that an older skill-local audit script rejects an active record whose schema differs from its assumptions. T7-A2 clarified that the repository-root `python scripts/audit_agent_execution.py <change-record-path>` command is the canonical pre-edit gate and that other validators apply only when their governed artifact and current state support pre-edit validation. T8-A2 then accepted AC9-AC10 across five semantic scenarios. The older script remains outside this approved scope and is not presented as canonical.

### Final integrated acceptance

Lead accepted the integrated four-file policy against AC1-AC8 and the independent S1-S9 evidence. Final decision clarification explicitly assigns Router/Verifier to Cheap, keeps Router out of ordinary code edits, requires checking guidance/patterns/safe defaults before specification questions, and preserves Execution Mode question gates. This was policy adjudication, not application implementation. The attempted duplicate Lead patch did not apply; delegated policy edits were preserved. Initial commentary claiming delegation unavailable was incorrect: the original dispatch was accepted and the worker delivered the changes.

Lead also accepted the reopened change against AC9-AC10. The dispatcher failure demonstrated a missing discovery and first-write control, T7 added the narrow skill entry and regression assertions, T8-A1 exposed validator ambiguity, T7-A2 resolved the canonical-command boundary, and T8-A2 independently accepted the result. The changed implementation scope remains limited to the orchestration skill and its tests; application code and the separately blocked dispatcher incident record are excluded.

Mechanical checks (bundled Python executable `C:/Users/yuto.nagano/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`):

- `scripts/audit_agent_execution.py docs/changes/20261001_cheap-first-agent-workflow`: valid.
- `.codex/skills/document-driven-development/scripts/validate_change_records.py docs/changes/20261001_cheap-first-agent-workflow/README.md`: issues=0.
- `-m unittest discover -s .codex/skills/document-driven-development/scripts -p test_validate_change_records.py`: 10 tests passed, independently executed by Lead.
- `-m unittest discover -s .codex/skills/agent-task-orchestration/scripts -p 'test_*.py'`: 18 tests passed, independently executed by Lead.
- Worker reports the existing audit suite passed 33 tests and skill-creator `quick_validate.py` passed for both modified skill directories using temporary PyYAML support. The temporary dependency folder was removed. Final decision clarification changes only prose, not validated frontmatter or links.
- `git diff --check` and explicit changed-path review passed; only the four canonical documents and this change directory changed. No historical record, application code, secret or generated artifact is included.
- No dotnet build/test/format or CodeGraph sync is applicable to prose-only edits. CodeGraph CLI lookup was unavailable; ordinary file inventory was used instead.

The worker noted an existing validator inconsistency for active-at-dispatch JSON. Completed attempt validation passed; no claim is made that active JSON validation passed. That pre-existing validator behavior is outside this routing-policy change and is not an application or policy acceptance blocker. Model/usage telemetry remains unavailable, so no efficiency claim is made.
