---
name: agent-task-orchestration
description: Plan and govern delegated coding, research, and review work with explicit scope, evidence, parallelization, escalation, and outcome-cost gates; use when a task should be split across agents or model tiers.
---

# Agent Task Orchestration

Use this skill when a task has separable workstreams and delegation could reduce elapsed time or cost. The lead remains accountable for the design, integration, and final decision. Do not delegate authority for requirements, security-sensitive decisions, destructive actions, or acceptance of the final result.

## Operating model

Router, Cheap Planner, Cheap Executor, Verifier, and Strong Planner are logical responsibilities; do not spawn five agents by default. Cheap Planner and Cheap Executor may be the same agent. Router and Verifier use Cheap by default; Router does not normally edit code. The Lead owns requirements, approval, risk decisions, integration, and final acceptance, but these responsibilities do not require a high-capability model for routine implementation or review.

For eligible coding, investigation, test authoring, mechanical verification, and local correction in this repository, request `gpt-6-luna` with reasoning effort `high`. This is the user-directed default, not an empirical claim about price or quality. Record the concrete requested model at dispatch and keep observed model, observation source, and usage telemetry separate; use null plus a reason when unavailable. Do not substitute a different model tier because a task spans multiple files.

Use a high-capability model only as Strong Planner for material requirement ambiguity that repository investigation cannot resolve, an architecture/public-contract decision with important trade-offs, a conflict between requirements and existing design, or decomposition that still leaves an unexecutable task. Strong Planner analyzes and returns ordered, bounded tasks for Cheap Executor and does not implement ordinary code. If the cheap model is unavailable, record ordinary execution as externally blocked rather than silently handing implementation to Strong.

## Required lead workflow

1. Define the outcome, constraints, acceptance criteria, and risk level before delegation.
   Before approval, surface material routing, caller-assumption, telemetry, attribution, verification, cost, and review-burden concerns in the governing concern ledger. Convert resolved concerns into acceptance criteria and counterexamples; do not wait for the user to ask whether concerns exist.
2. Router classifies the task as cheap execution, cheap re-planning/splitting, a necessary specification question, or Strong Planner. Choose by ambiguity, risk, design authority, and independent verifiability, not by file count. A conventional multi-file change may stay cheap when its pattern and contract are clear.
3. Before any code edit, Cheap Planner inspects the repository: target location, analogous implementation, naming and DI patterns, test layout, libraries, project dependencies, and API/DTO structure as relevant. Repository questions are investigated, not asked.
4. Cheap Planner writes a concise execution plan with these fields: purpose; investigation findings; files to change; ordered implementation steps; unresolved specification questions; and verification commands with expected results. Before asking, consult AGENTS, design documents, analogous code, and safe defaults within approved behavior. Ask only when a material specification choice remains, subject to Execution Mode's narrower question gates. Do not start implementation before this plan exists.
5. Cheap Executor follows the plan and its exact write scope, preferring existing patterns. Do not add unrelated refactoring, abstractions, or adjacent improvements. If scope grows, pause edits in the affected slice and re-plan internally. Continue independent approved slices. Seek renewed approval only if a requirement, public contract, acceptance criterion, or material risk decision changes.
6. Verifier runs the planned mechanical checks: applicable build, unit/integration tests, formatter, lint, type check, or architecture test. A task cannot pass on an LLM's self-assessment; record command and outcome. An unavailable command is a blocker, not a passing result.
7. After planning, after a verification failure, when scope expands, and when the same cause recurs, Router re-evaluates the route. On failure, Cheap first classifies the cause and investigates repository facts. Fix local compile, assertion, null, API-use, pattern, and implementation errors on the cheap route and rerun the failed gate. Different causes do not accumulate toward promotion. About two attempts with the same cause trigger Router reassessment, not automatic escalation.
8. Send a case to Strong Planner only when it meets the operating-model criteria above. Strong returns concrete ordered tasks with frozen decisions and checks to Cheap Executor; if a cheap executor is unavailable, keep the implementation blocked. Do not use Strong for routine coding because it may be faster.
9. For each work item record `id`, objective, owner, model tier, dependencies, read/write scope, deliverable, verification, completion evidence, and state. Use exclusive owners for overlapping files. Parallelize only when independent slices save total prompt, integration, review, and rework effort.
10. Integrate slices and review against the existing AC-to-task groups. Lead resolves conflicts and accepts the integrated outcome based on executable evidence. For delegated work, record requested and observed models separately and use [the audit schema and gates](references/execution-audit.md); include reviewer usage, corrections, re-verification, and audit overhead when available.

## Audit-first recording gate

Reuse the DDD task plan as the human-readable audit; do not create a second ledger. For multi-task changes add only `Routing`, `Audit`, and `Result metrics` columns. `Routing` is the owner tier plus a short reason. `Audit` is `none` for lead-only work or the delegated attempt ID. `Result metrics` is `usage availability; retries; lead corrections; review passes`. If telemetry is unavailable, keep the value null in JSON and use one short reason; never infer it.

Record audit changes only for:

- delegated attempt dispatch and completion;
- a material verification failure and its closure;
- final aggregation of the four result metrics.

Do not create audit entries for commentary, ordinary successful commands, or routine state transitions. Lead-only tasks require no JSON. A short task may record `Lead — single short task` without further routing prose.

Evaluate delegation per separable workstream after attempting to split decisions from mechanical execution. A cross-cutting change may still contain disjoint read-only exploration, fixture work, mapping, frozen-query implementation, or independent review. For each lead-owned stream, record why delegation was unsuitable: architecture/public contract, persistence/migration, security/privacy, destructive action, unresolved ambiguity, overlapping writes, unavailable worker, integration/final acceptance, single short task, or delegation/review cost exceeding likely benefit. A blanket statement that the whole change overlaps is not sufficient.

For delegated coding, create one compact JSON audit per attempt as described in [the execution-audit reference](references/execution-audit.md). Run `python scripts/audit_agent_execution.py <changed-change-record-path>` after the pre-implementation task plan, after delegated completion, and before final review. A missing validator is a blocking process defect, not permission to skip the audit. Keep normal successful delegated JSON below 2,500 UTF-8 bytes and the task-plan audit additions below 20 nonblank lines in workflow fixtures; simplify before exceeding those bounds.

Before declaring the work complete, the lead performs a focused self-audit of the plan and dependencies, tier/routing choices, worker evidence, material acceptance gaps, and applicable skill instructions, proportional to the task's risk and size. Record material findings and their evidence; a short, low-risk task does not need a separate checklist. Do not complete while a material approved item is runnable, unverified, or unresolved. If the audit demonstrates a reusable process failure, apply `learn-from-implementation-failures`, update the narrowest applicable skill, validate it, and rerun the affected gate. Correct isolated implementation mistakes locally without turning each one into a skill rule.

If a foreseeable concern is first raised only after completion, compare it with the approved design and tests. Reopen the originating record when it invalidates an AC or completion evidence, add the missing counterexample, and apply `learn-from-implementation-failures` when the review process failed to surface the concern. Do not classify it as a future enhancement merely because it was noticed late.

Do not accept worker-authored tests as the only quality evidence for a material change. Use an independent counterexample, existing regression suite, invariant, real-path trace, or end-to-end check. Record why an existing suite is sufficient for a low-risk mechanical change.

## Worker prompt contract

Every delegated prompt must state:

- task objective and why it matters;
- in-scope files/systems and explicit write scope (or `read-only`);
- out-of-scope actions, especially destructive operations and external side effects;
- dependencies, starting revision/state, and assumptions;
- linked acceptance-criterion IDs, the local evidence the slice must produce, and required completion evidence (tests, commands, links, or cited sources);
- test responsibility: the tests to create or update, the smallest relevant test command to run during implementation, the broader regression command required before handoff, and the expected result. If no test change is appropriate, require a concrete reason and identify the existing test or independent evidence that covers the change;
- frozen decisions and invariants, decisions the worker must not change, and at least one relevant counterexample;
- the implementation freedom that remains and the exact boundary at which the worker returns the decision to the lead;
- expected output format, including changed files and unresolved risks;
- escalation boundary: a material ambiguity remains after repository investigation, frozen requirements conflict, the same root cause persists through focused corrections, an unsafe operation is implicated, or scope would change an acceptance criterion; an ordinary failed check alone is not a Strong Planner trigger;
- whether parallel work is allowed and what other work it must not overlap.

If any required item is unavailable, the worker should stop at the boundary and report the missing information rather than inventing it. A coding worker does not report completion until it has created or updated the contracted tests and run the contracted commands successfully. If execution is genuinely unavailable, it reports the exact blocker and leaves the task incomplete for lead classification; source inspection alone is not a passing test result.

## Declarative infrastructure and generated-artifact prompt gate

Add this gate to a worker prompt when the assigned change edits declarative infrastructure or generates structured artifacts consumed by another system, such as dashboards, deployment manifests, or schemas. It does not apply to an ordinary application refactor that produces no such artifact.

- Name the exact downstream consumer and validate the parsed artifact against its semantic schema and contract. String matches, a successful render, row counts, or cardinality alone are insufficient. When the artifact defines a set of entries, compare exact membership, uniqueness, and field or dimension names and values against an independently constructed expected set.
- Include applicable counterexamples for malformed nesting or types, changed required fields or dimensions, duplicate entries, and omitted entries. Each must fail at the semantic contract assertion, not only at setup or parsing.
- For formatter/tool parity and push gates, apply the existing [CI parity and push closure procedure](../learn-from-implementation-failures/SKILL.md#ci-parity-and-push-closure). In the prompt, identify the workflow's exact command, tool version, and runner OS. Require matching evidence before push; if the exact version or OS is unavailable, report that check as unverified and blocked rather than treating a different local tool or platform as proof.
- Require child validation tools to run noninteractively with explicit stdin/EOF behavior, stage-labeled or streamed progress, a per-child timeout and outer bound, and process-tree termination on timeout. Include a deliberately stalled child case when child execution is part of the helper's correctness or operational risk.
- For shell, pipe, path, console, or process behavior that may vary by platform, require validation on the workflow runner OS. If that execution is unavailable before push, preserve runner compatibility as unverified.

This section adds artifact semantics and helper-process requirements to the worker prompt contract. General CI parity remains governed by the linked procedure above.

## Suitability and routing gates

Route ordinary execution to the requested cheap model after task-specific repository inspection and planning. Keep specification, approval, material architecture/public-contract, security/privacy, persistence, concurrency, destructive, and conflicting-requirement decisions with the Lead; use Strong Planner only if a difficult decision must be resolved or tasks must be made executable. The decision owner and executor may be different agents.

Before starting, mark each item `Runnable` only if its write scope is disjoint from active items and dependencies are satisfied. Otherwise mark it `Dependent` or serialize it. After completion, mark it `Verified` only when the stated mechanical evidence passes; a prose claim without evidence remains `In progress` until classified as `Dependent`, `Externally blocked`, or `Rejected with reason`.

## Failure classification and route reassessment

After a failed verification, Cheap Planner records the failed command, symptom, and cause category before correction. First check for a missed existing pattern, factory, DI registration, dependency, or test setup. Keep these cases on the cheap route:

- compile, namespace, or type errors;
- a local assertion or test setup error;
- null handling, API-call, or implementation omissions;
- repository-understanding gaps that further inspection can resolve.

Different-cause failures do not accumulate toward promotion. About two focused failures with the same root cause are a Router reassessment point, not an automatic Strong escalation. Continue cheap correction where the cause remains local and independently verifiable. Strong Planner is warranted when evidence shows an unresolved design conflict, material ambiguity, architecture/public-contract limitation, or a task that cannot be reduced further. Strong Planner returns a revised task contract; it does not implement it.

Run Router initially, after the short plan, after any verifier failure, when scope expands, and when a root cause repeats. Router chooses cheap continuation, cheap re-planning/splitting, a specific specification question, or Strong Planner. A material approved-design change returns the change record to `Proposed`; internal task/scope refinement that preserves approved acceptance does not require a user question.

The Lead reviews at decomposition, before integration, and after final verification as accountable owner; this does not require a high-capability reviewer. Review the integrated attributable diff and executable evidence by AC group, including invariants, non-regression, scope, and open failures. Drill down only when group evidence fails/conflicts, a frozen decision or scope changes, or risk-sensitive independent proof is missing. Resolve the cause and rerun the affected gate before acceptance.

## Outcome and cost measurement

Use observable gates for routing decisions:

- **Quality gate**: all acceptance criteria pass and required checks have evidence.
- **Scope gate**: only authorized files/systems changed; no secret, generated, or unrelated changes were introduced.
- **Rework gate**: count retries, escalations, and lead fixes attributable to the worker.
- **Efficiency gate**: compare elapsed time and measured usage/cost to the lead-only baseline when available. Track `cost per successful outcome = total measured delegated + review cost / outcomes passing quality and scope gates`; if cost data is unavailable, record that limitation and use effort/rework as a proxy.

Keep the user-directed cheap route as the default; use outcome data to improve task boundaries, prompts, sequencing, and verification. Rework by itself does not automatically promote implementation to Strong. If repeated comparable evidence shows that a route harms successful-outcome quality or total effort, the Lead may re-plan the task or recommend revising the user-directed route under the recorded rollback conditions.

Count the full cost of a successful result: task/prompt preparation, worker usage, integration, acceptance-group review, conditional detail review, automated review, human active review time when supplied, retries, lead corrections, any planning escalation, re-verification, and audit overhead. Keep unavailable units separate; do not invent currency conversion or labor rates. Compare routes only across similar task difficulty and attributable patches. If fragmentation makes this total worse, combine slices. Do not infer model, token, time, or cost data that telemetry does not provide.

An explicit user-directed model/reasoning preference may be applied without five prior samples, but must not be described as a measured efficiency improvement. An efficiency recommendation requires at least five comparable successful outcomes or other approval-changing evidence such as a material security/data/scope/false-completion failure. Record one bounded adjustment, independent forward test, observation period, rollback conditions, and escaped defects. Recommendations do not authorize silently changing the approved policy.

## Handoff record

For each task, retain a compact record with:

```text
Task: <id and objective>
Owner/tier: <role and capability tier>
Dependencies: <ids or none>
Read scope: <paths/systems>
Write scope: <paths/systems or read-only>
Acceptance/evidence: <checks and results>
State: Proposed | Runnable | In progress | Dependent | Externally blocked | Rejected with reason | Verified
Usage/effort: <measured values or unavailable>
Model verification: <requested, observed, source, verified | mismatch | unavailable>
Review effort/cost: <review passes, model usage, active minutes, correction and re-verification, or unavailable>
Rework/escalation: <count and reason>
Lead decision: accept | revise | promote | reject
```

Keep the compact record in the governing change record and delegated coding details in its `agent-audits/<task-id>.json` artifact. Validate artifacts with `scripts/audit_agent_execution.py`. Do not store prompt text, source text, credentials, or secrets. If the workflow is repository work, follow the repository's change-record and approval rules before editing production code.
