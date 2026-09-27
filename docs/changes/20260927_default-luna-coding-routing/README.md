# Default Luna execution routing

- Status: Implemented
- Change record schema: 2
- Owner: Main agent / user
- Created: 2026-09-27
- Updated: 2026-09-27

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not applicable | Repository workflow policy only |
| Verification | Complete | Luna/high applied and verified the policy; Astra/Lead accepted AC1-AC5 and conflict counterexamples |
| Deployment/operation | Complete | Repository-level routing policy is active through `AGENTS.md` |

## Context

The repository currently says that a locally scoped, independently verifiable coding task should first consider the available `low_cost_coding_worker`. The user has explicitly requested that future coding and investigation work should instead default to `gpt-6-luna` with reasoning effort `high`, while keeping rigorous lead review. When a delegated result has a problem, the first response should normally remain on Luna through focused correction, narrower decomposition, or another Luna investigation rather than immediately moving execution to Astra.

The immediately preceding endpoint refactor provides one successful but not fully clean observation: the requested route was `gpt-6-luna` / `high`, the final quality and scope gates passed, one test-detected route-registration retry and one lead-requested import cleanup were required, and independent model/token telemetry was unavailable. This is sufficient to honor a user-directed routing preference, but not sufficient to claim a proven cost or quality optimization.

## Goals

- Make `gpt-6-luna` with reasoning effort `high` the user-directed default for eligible delegated coding, investigation, exploration, test-authoring, and verification work in this repository.
- Prefer total cost efficiency over elapsed-time optimization when choosing task size, parallelism, retries, and escalation.
- Keep Astra/Lead tier centered on non-coding specification, requirements, architecture, public contracts, task decomposition, risk decisions, retry/promotion decisions, integration acceptance, and final achievement judgment; delegate direct execution work to Luna as far as practical, including review-phase commands and verification work.
- When Luna encounters a problem, first use a focused Luna correction, narrower Luna task, or Luna re-investigation whenever the safety and scope gates permit it.
- Preserve focused correction, promotion, independent verification, and agent-audit gates.
- Observe the first five comparable eligible tasks before making any evidence-based efficiency claim.

## Non-goals

- Absolutely prohibit Astra from running code, tests, or diagnostic commands when a short direct independent check is necessary for trustworthy review, safety, or lower total cost.
- Force delegation when a coding, investigation, or verification task is too small for delegation overhead to be worthwhile.
- Route specification, architecture, security/risk decisions, migration strategy, destructive decisions, ambiguous requirements, task decomposition, retry/promotion decisions, or final acceptance to Luna merely because implementation or research is involved.
- Claim that Luna is cheaper or better based on one sample.
- Change `.codex/skills/agent-task-orchestration/SKILL.md`; this repository preference is recorded in `AGENTS.md` and remains subordinate to safety and suitability gates.

## Documentation updates

- `AGENTS.md`: after approval, replace the generic cost-sensitive coding-worker default with the explicit user-directed `gpt-6-luna` / `high` preference for coding, investigation, and review-phase execution; Luna-first recovery; cost-efficiency priority; Astra/Lead decision boundary; five-task observation period; and rollback/reconsideration conditions. `AGENTS.md` is the canonical repository workflow policy.
- No other document requires an update; the orchestration skill remains generic and should not encode a repository-specific permanent model ID.

## Decisions

### D1 Default route

For a delegated coding, investigation, exploration, test-authoring, or verification slice that is frozen, locally scoped, reversible/read-only as applicable, and independently verifiable, request `gpt-6-luna` with reasoning effort `high` by default.

### D2 Lead-owned exceptions

Astra/Lead tier is used as narrowly as practical for non-coding specification, requirement interpretation, architecture/public-contract decisions, persistence/migration strategy, concurrency and security/privacy decisions, destructive actions, unresolved ambiguity, task decomposition, integration acceptance, retry/rework/promotion judgment, and final achievement judgment.

Direct execution is not forbidden for Astra, but should be exceptional. Code edits, repository investigation, build/test/lint/format execution, verification scripts, reproduction steps, and corrective implementation should be delegated to Luna/high whenever they can be bounded and independently assessed. During review, Astra should define the challenge and judge evidence; a Luna worker should normally execute it. Astra may directly run a short read-only or verification command when needed to establish independent evidence, when delegation would materially increase total cost, when Luna is unavailable, or when immediate safety/diagnostic evidence is required. Astra should not take over implementation merely because it found the defect.

### D3 Luna-first recovery

When a Luna result fails a gate or exposes ambiguity inside the approved boundary, prefer one focused correction, narrowed task, or re-investigation using Luna/high. Continue using Luna when the failure is mechanical, local, reversible, and independently testable. Astra/Lead decides the new contract and whether a retry is acceptable; Astra does not take over implementation by default. Promote execution away from Luna only when the issue crosses into an Astra-owned decision, Luna is unavailable, scope can no longer be isolated, or repeated Luna attempts make total successful-outcome cost worse.

### D4 Cost objective, observation, and rollback

The optimization objective is total cost per successful outcome, not minimum elapsed time. Do not parallelize merely to finish sooner when serial Luna work is expected to cost less overall. This is a user preference, not an evidence-based model ranking. Record the first five comparable eligible outcomes using the existing audit fields. Reconsider or roll back the default if a material security/data/scope/false-completion defect occurs, or if repeated retries, lead corrections, failed gates, or review burden make successful-outcome cost worse. Do not infer observed model or usage when telemetry is absent.

## Hypothesis ledger

| ID | Claim | Fact / inference | Evidence and falsification | Result | Disposition |
| --- | --- | --- | --- | --- | --- |
| H1 | The current repository default does not guarantee Luna/high for coding or investigation | Fact | `AGENTS.md` names an abstract cost-sensitive coding worker and allows explorer routing, but not `gpt-6-luna`/`high` as the cross-task default | User request requires a policy change | Design premise |
| H2 | Luna/high is proven more efficient | Unverified inference | One endpoint-refactor sample passed but required retry/correction and lacks model/token telemetry | Not proven | Do not make this claim; observe five comparable tasks |

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Hard-coding a model can become stale or unavailable | Future task dispatch could fail or be inefficient | Treat Luna/high as the requested default, retain availability/suitability fallback and record actual requested/observed routing | AC1, AC2 / T1 | Accept with fallback | User requested Luna/high | Resolved in design |
| C2 | One successful sample cannot justify a persistent efficiency conclusion | Routing could be presented as evidence-based when it is not | Label it user-directed; require five comparable observations before an efficiency recommendation | AC3 / T1 | Required qualification | Included in approval scope | Resolved in design |
| C3 | “Coding work” can include high-risk architectural or security decisions | Blind delegation could violate ownership gates | Keep explicit lead-owned exceptions and final review | AC2 / T1 | Required boundary | Included in approval scope | Resolved in design |
| C4 | Luna-first retries can become more expensive than an early escalation | Repeated failed attempts can worsen total cost | Astra/Lead owns retry judgment; permit one focused retry by default and continue only while measured rework remains acceptable | AC2, AC4 / T1 | Luna-first, not Luna-only | User prioritizes cost efficiency | Resolved in design |
| C5 | Parallel Luna work can reduce elapsed time but increase total prompt, integration, and review cost | Conflicts with the stated objective | Prefer the smallest sufficient number of Luna tasks and serialize when parallelism does not improve successful-outcome cost | AC4 / T1 | Cost before speed | User explicitly prioritized cost efficiency | Resolved in design |
| C6 | Delegating every review command can make the reviewer depend only on worker-produced evidence or add disproportionate overhead | Review independence can weaken, or total cost can rise | Luna normally executes review work; Astra may run minimal independent challenges and short diagnostics, but avoids implementation takeover | AC2, AC5 / T1 | Delegate execution, retain independent judgment | User permits direct review execution but wants it minimized | Resolved in design |

Approval was received on 2026-09-27, and no `Open decision` remains.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | `AGENTS.md` says eligible delegated coding and investigation work defaults to requested model `gpt-6-luna` with reasoning effort `high` | T1 | exact policy-text inspection | Verified |
| AC2 | The same rule limits Astra/Lead primarily to specification, task split, risk/architecture decisions, retry/promotion judgment, integration acceptance, and final achievement judgment while preserving safety and independent review | T1 | surrounding-rule review and counterexample inspection | Verified |
| AC3 | The policy labels the choice user-directed, defines a five-comparable-task observation period, and gives rollback/reconsideration conditions without claiming measured superiority | T1 | policy-text and change-record review | Verified |
| AC4 | The policy explicitly prioritizes total cost efficiency over elapsed time and uses Luna-first focused correction/re-investigation without making Luna mandatory after repeated or risk-sensitive failure | T1 | policy-text and failure-path review | Verified |
| AC5 | The policy directs coding, investigation, correction, and review-phase command execution to Luna where practical, while allowing narrowly justified Astra direct checks and keeping acceptance judgment independent | T1 | direct-execution and review-independence counterexamples | Verified |

## Task plan

- **T1 — Update and verify repository routing policy**
  - Owner: gpt-6-luna execution worker; Main agent accepts
  - Model tier: Luna/high — direct policy-file execution follows the approved routing preference; Lead retains wording acceptance
  - Depends on: explicit approval (satisfied 2026-09-27)
  - Write scope: `AGENTS.md`, this change record
  - Verification: `rg` policy inspection, `git diff --check`, `git status`, focused lead review
  - Completion evidence: [T1-A1](agent-audits/T1-A1.json), AC1-AC5 mapped to the final diff
  - State: Verified
  - Routing: Luna/high for direct edit and verification; Astra/Lead for policy acceptance
  - Audit: T1-A1
  - Result metrics: usage unavailable; retries 0; corrections 0; reviews 1

## Review gates

- **Design and task-split review (2026-09-27, Main):** single short policy edit; delegation would add overhead and delegate the routing decision itself, so T1 remains lead-owned. AC1-AC5 cover coding, investigation, review execution, Luna-first recovery, Astra boundaries, cost objective, independent evidence, and risk gates.
- **Concern and agreement review (2026-09-27, Main):** model staleness/availability, insufficient efficiency evidence, risky coding/research categories, fallback, audit, retry cost, parallelism cost, direct review execution, independent evidence, and review burden were inspected. C1-C6 are resolved in the proposed design; no open objection remains.
- **Pre-implementation review (2026-09-27, Main):** T1 is Runnable/In progress with one exclusive write owner for `AGENTS.md`. The worker receives the approved D1-D4, AC1-AC5, C1-C6, exact non-overwrite scope, text-inspection and diff verification, and escalation boundaries. Main owns only the change record, audit reconciliation, conflict review, and final acceptance.
- **Checkpoint review (2026-09-27, Main):** Luna/high changed only the intended Agent task orchestration bullets in `AGENTS.md`, preserved safety/escalation/audit rules, and passed diff/audit validation. A status-only CRLF change appeared on an unrelated endpoint with no content or staged diff; attribution is unknown and it is excluded from this change.
- **Final review (2026-09-27, Main):** AC1-AC5 are Verified. The integrated wording makes Luna/high the eligible execution default, keeps Astra/Lead decision ownership, permits only narrow Astra direct execution, prioritizes total successful-outcome cost, defines Luna-first bounded recovery, five comparable observations, rollback triggers, and telemetry separation. T1 quality/scope verdict is accept; requested model was gpt-6-luna, observed model and token usage remain unavailable.

## Verification record

- Inspected current `AGENTS.md` coding-route and persistent-improvement rules.
- Inspected the repository orchestration skill; its generic guidance must remain model-agnostic.
- No workflow policy was changed before approval.
- Luna/high updated `AGENTS.md` within the exclusive scope and reported `git diff --check` success plus a valid active audit.
- Astra/Lead independently reviewed the exact diff against AC1-AC5 and C1-C6. No conflict with security, destructive-action, evidence, audit, or final-acceptance gates remains.
- `python .codex/skills/agent-task-orchestration/scripts/audit_agent_execution.py docs/changes/20260927_default-luna-coding-routing/README.md` validates the completed delegated-attempt record.

## Deviations and follow-up

- An unrelated endpoint appeared modified because its working-tree line ending was CRLF while the index is LF. `git diff`, cached diff, and numstat showed no content change. It is intentionally excluded from this policy commit and not treated as T1 output.
