---
name: agent-task-orchestration
description: Plan and govern delegated coding, research, and review work with explicit scope, evidence, parallelization, escalation, and outcome-cost gates; use when a task should be split across agents or model tiers.
---

# Agent Task Orchestration

Use this skill when a task has separable workstreams and delegation could reduce elapsed time or cost. The lead remains accountable for the design, integration, and final decision. Do not delegate authority for requirements, security-sensitive decisions, destructive actions, or acceptance of the final result.

## Operating model

Treat model labels as capability/cost tiers, not fixed product names. Select the least capable tier that can meet the task's risk and ambiguity:

- **Lead tier**: owns requirements, design, decomposition, integration, and final review. Use for ambiguous work, cross-cutting changes, architecture, risk decisions, and conflict resolution.
- **Worker tier**: handles bounded, low-ambiguity research, code discovery, mechanical implementation, or test authoring under a written contract.
- **Review tier**: independently checks the lead's plan or worker output when correctness, security, compatibility, or regression risk justifies it. It may be the lead tier or a separate capable tier.

Do not hard-code model IDs as permanent policy or infer pricing. At dispatch, however, record the concrete requested model ID rather than `runtime-default` or an abstract worker name so the run can be audited. Record the selected tier, measured usage if available, elapsed time, retries, and delegated result so routing can be tuned later.

For coding, prefer the lowest-cost eligible route, not the lowest-cost model unconditionally. A frozen, narrow, independently verifiable task with no architecture, public-contract, persistence/migration, concurrency, security/privacy, or destructive decision may start with the current cost-sensitive coding model. In this repository that is configured as `low_cost_coding_worker`; resolve current availability at execution time. Bounded multi-file work may use the balanced worker tier. The lead retains ambiguous or high-risk work and all final acceptance.

## Required lead workflow

1. Define the outcome, constraints, acceptance criteria, and risk level before delegation.
   Before approval, surface material routing, caller-assumption, telemetry, attribution, verification, cost, and review-burden concerns in the governing concern ledger. Convert resolved concerns into acceptance criteria and counterexamples; do not wait for the user to ask whether concerns exist.
2. Before assigning a lead tier, separate decision work from execution under frozen decisions. Split only work with a clear boundary, exclusive write owner, and independent verification, and only when prompt, integration, and review overhead is likely to remain worthwhile. For each work item record: `id`, objective, owner, tier, dependencies, read scope, write scope, deliverable, verification command or evidence, and completion state. Record a concrete non-delegation reason for every remaining lead-owned stream.
   In a change record, record the planned executable agent role and selection reason during design, before approval, not only when dispatching. Distinguish the coordinating lead, read-only explorer, bounded coding worker, and independent reviewer when applicable; do not require all roles for every task. Assign who executes tests and who accepts the evidence, identify parallel versus serialized ownership, and state the condition for promotion to the lead. At dispatch replace role-level intent with the concrete requested model and actual agent ID; never prefill observed execution or success. Child tasks retain a link to the parent's completion criteria, so creating or handing off a task cannot close the parent outcome.
3. Decide whether items can run in parallel. Parallelize only when they have disjoint write scopes and no ordering dependency. Serialize shared-file edits, schema/API changes, migrations, and integration work. Treat generated files, migration snapshots, shared contracts, and formatters that rewrite common files as shared write scopes even when workers edit different source files. Assign them one owner and record when a reviewed contract is frozen before dependent workers begin.
4. Send each worker a complete prompt using the contract below. A worker must not infer missing authority from repository access.
5. Integrate compatible slices, then review them by the existing acceptance-criterion-to-task mapping. Closely related criteria may form one review group. Review each group once against its integrated attributable diff and evidence; inspect individual tasks or diffs only when an escalation trigger is present. The lead resolves conflicts and owns the final integrated change.
6. Record routing results: successful outputs, rework/retries, escalations, measured usage/cost when available, and elapsed time. Evaluate cost per successful outcome, not token price alone.
7. For delegated coding, create an execution audit using [the audit schema and gates](references/execution-audit.md). Record requested and observed models separately. Include reviewer usage, active review effort, corrections, re-verification, and audit overhead in successful-outcome cost.

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
- escalation triggers: ambiguity, missing access, conflicting requirements, unsafe operation, failed verification, or scope expansion;
- whether parallel work is allowed and what other work it must not overlap.

If any required item is unavailable, the worker should stop at the boundary and report the missing information rather than inventing it. A coding worker does not report completion until it has created or updated the contracted tests and run the contracted commands successfully. If execution is genuinely unavailable, it reports the exact blocker and leaves the task incomplete for lead classification; source inspection alone is not a passing test result.

## Suitability and routing gates

Delegate to a worker tier when the task is bounded, reversible, locally verifiable, and has low ambiguity. Keep it with the lead when it changes architecture, public contracts, security/privacy, data integrity, user-visible acceptance, or requires interpreting conflicting requirements. A research worker may gather sources and summarize them, but the lead verifies source quality and applies the conclusion.

Before starting, mark each item `Runnable` only if its write scope is disjoint from active items and its dependencies are satisfied. Otherwise mark it `Dependent` or serialize it. After completion, mark it `Verified` only when the stated evidence passes; a prose claim without evidence remains `In progress` until classified as `Dependent`, `Externally blocked`, or `Rejected with reason`.

## Escalation and review

Escalate to the lead tier when any of the following occurs:

- the worker reports ambiguity, a missing dependency, or a requirement conflict;
- verification fails after one focused correction, or the worker needs to broaden scope;
- the change touches a public API, persistence/data migration, authentication/authorization, security, or irreversible action;
- independent reviewers disagree on correctness or acceptance;
- rework is more expensive than reassignment based on observed effort.

At minimum, perform a lead review at decomposition, before integration of worker changes, and after final verification. The default unit after integration is an acceptance criterion or a closely related criterion group defined by the existing AC-to-task mapping, not each microtask. Check the approved AC and concern dispositions, attributable integrated diff/scope, executable real-path evidence, invariants and non-regression, audit/rework metrics, and open failures. Drill down to task/diff review only when evidence fails or conflicts, a frozen decision changed, scope/ownership is violated, risk-sensitive implementation lacks proof, independent evidence is missing, or retry/review burden exceeds its boundary. Resolve the cause, rerun the group evidence, and make the acceptance decision at group level. For high-risk work, add an independent review with no access to the worker's conclusion where practical.

## Outcome and cost measurement

Use observable gates for routing decisions:

- **Quality gate**: all acceptance criteria pass and required checks have evidence.
- **Scope gate**: only authorized files/systems changed; no secret, generated, or unrelated changes were introduced.
- **Rework gate**: count retries, escalations, and lead fixes attributable to the worker.
- **Efficiency gate**: compare elapsed time and measured usage/cost to the lead-only baseline when available. Track `cost per successful outcome = total measured delegated + review cost / outcomes passing quality and scope gates`; if cost data is unavailable, record that limitation and use effort/rework as a proxy.

Keep a worker tier only when it passes quality and scope gates with acceptable rework and improves outcome cost or throughput. Otherwise narrow the task, strengthen the prompt, serialize the work, or promote it to the lead/review tier. These are routing decisions, not permanent model rankings.

Count the full cost of a successful result: task/prompt preparation, worker usage, integration, acceptance-group review, conditional detail review, automated review, human active review time when supplied, retries, lead corrections, promotions, re-verification, and audit overhead. Keep unavailable units separate; do not invent currency conversion or labor rates. Compare routes only across similar task difficulty and attributable patches. If fragmentation makes this total worse, combine slices or promote the route. Fewer than five comparable successes may inform a note but may not change a persistent default.

Persistent routing, prompt, reasoning, or budget improvements require repeated comparable evidence, one material security/data/scope/false-completion failure, or at least five successful samples. Change one bounded factor, validate it, run an independent forward test, define an observation period and rollback condition, and record escaped defects. Generate recommendations autonomously, but do not expand approved scope or silently rewrite unrelated policy.

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
