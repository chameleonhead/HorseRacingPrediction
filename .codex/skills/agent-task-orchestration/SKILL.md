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

Do not hard-code model IDs, pricing, or provider assumptions in plans or artifacts. Record the selected tier, measured usage if available, elapsed time, retries, and delegated result so routing can be tuned later.

## Required lead workflow

1. Define the outcome, constraints, acceptance criteria, and risk level before delegation.
2. Split only work with a clear boundary. For each work item record: `id`, objective, owner, tier, dependencies, read scope, write scope, deliverable, verification command or evidence, and completion state.
3. Decide whether items can run in parallel. Parallelize only when they have disjoint write scopes and no ordering dependency. Serialize shared-file edits, schema/API changes, migrations, and integration work. Treat generated files, migration snapshots, shared contracts, and formatters that rewrite common files as shared write scopes even when workers edit different source files. Assign them one owner and record when a reviewed contract is frozen before dependent workers begin.
4. Send each worker a complete prompt using the contract below. A worker must not infer missing authority from repository access.
5. Review the returned evidence against acceptance criteria before merging or forwarding it. The lead resolves conflicts and owns the final integrated change.
6. Record routing results: successful outputs, rework/retries, escalations, measured usage/cost when available, and elapsed time. Evaluate cost per successful outcome, not token price alone.

## Worker prompt contract

Every delegated prompt must state:

- task objective and why it matters;
- in-scope files/systems and explicit write scope (or `read-only`);
- out-of-scope actions, especially destructive operations and external side effects;
- dependencies, starting revision/state, and assumptions;
- acceptance criteria and required completion evidence (tests, commands, links, or cited sources);
- expected output format, including changed files and unresolved risks;
- escalation triggers: ambiguity, missing access, conflicting requirements, unsafe operation, failed verification, or scope expansion;
- whether parallel work is allowed and what other work it must not overlap.

If any required item is unavailable, the worker should stop at the boundary and report the missing information rather than inventing it.

## Suitability and routing gates

Delegate to a worker tier when the task is bounded, reversible, locally verifiable, and has low ambiguity. Keep it with the lead when it changes architecture, public contracts, security/privacy, data integrity, user-visible acceptance, or requires interpreting conflicting requirements. A research worker may gather sources and summarize them, but the lead verifies source quality and applies the conclusion.

Before starting, mark each item `ready` only if its write scope is disjoint from active items and its dependencies are satisfied. Otherwise mark it `blocked` or serialize it. After completion, mark it `verified` only when the stated evidence passes; a prose claim without evidence is `incomplete`.

## Escalation and review

Escalate to the lead tier when any of the following occurs:

- the worker reports ambiguity, a missing dependency, or a requirement conflict;
- verification fails after one focused correction, or the worker needs to broaden scope;
- the change touches a public API, persistence/data migration, authentication/authorization, security, or irreversible action;
- independent reviewers disagree on correctness or acceptance;
- rework is more expensive than reassignment based on observed effort.

At minimum, perform a lead review at decomposition, before integration of worker changes, and after final verification. The review must check scope compliance, acceptance evidence, tests, unintended changes, and unresolved risks. For high-risk work, add an independent review with no access to the worker's conclusion where practical.

## Outcome and cost measurement

Use observable gates for routing decisions:

- **Quality gate**: all acceptance criteria pass and required checks have evidence.
- **Scope gate**: only authorized files/systems changed; no secret, generated, or unrelated changes were introduced.
- **Rework gate**: count retries, escalations, and lead fixes attributable to the worker.
- **Efficiency gate**: compare elapsed time and measured usage/cost to the lead-only baseline when available. Track `cost per successful outcome = total measured delegated + review cost / outcomes passing quality and scope gates`; if cost data is unavailable, record that limitation and use effort/rework as a proxy.

Keep a worker tier only when it passes quality and scope gates with acceptable rework and improves outcome cost or throughput. Otherwise narrow the task, strengthen the prompt, serialize the work, or promote it to the lead/review tier. These are routing decisions, not permanent model rankings.

## Handoff record

For each task, retain a compact record with:

```text
Task: <id and objective>
Owner/tier: <role and capability tier>
Dependencies: <ids or none>
Read scope: <paths/systems>
Write scope: <paths/systems or read-only>
Acceptance/evidence: <checks and results>
State: ready | running | blocked | incomplete | verified
Usage/effort: <measured values or unavailable>
Rework/escalation: <count and reason>
Lead decision: accept | revise | promote | reject
```

Keep records in the governing change record or task artifact, not in worker prompts alone. If the workflow is repository work, follow the repository's change-record and approval rules before editing production code.
