---
name: document-driven-development
description: Plan and deliver repository changes through an approved change record, including requirements, decisions, acceptance criteria, and planning-stage mocks. Use for feature, UI/UX, data-model, integration, or operational workflow changes in this repository.
---

# Document-Driven Development

Treat the document as the durable record of intent and the implementation as its verified realization.

## Workflow

1. Inspect related code, existing documents, and uncommitted changes without modifying production code.
2. Create or update `docs/changes/yyyyMMdd_<change-name>/README.md` using [the change-record format](references/change-record-format.md). Use the change-set creation date as eight digits and a short, stable kebab-case name; for example, `20260829_seamless-admin-object-navigation`.
3. Identify every current architecture, design, operational, or user-facing document affected by the change. Update those documents in the same change set before requesting approval; do not defer necessary corrections, supersession notices, or canonical-document links until implementation.
4. Add a `## Documentation updates` section to the change record. For every updated or newly created non-change-record document, state its path, the precise change, and why it is now the canonical source or how it relates to the change. State explicitly when inspection found no document update necessary.
5. Store planning artifacts under the same directory. Use `mocks/` for wireframes or visual mocks and `decisions/` only when a decision needs substantial standalone rationale. Link every artifact from the change record.
6. Set the document status to `Proposed` while decisions remain open. Present the document to the user and request explicit confirmation. The approval request itself must summarize the proposed outcome, material boundaries or irreversible decisions, and the acceptance criteria in observable user-facing terms. Include every acceptance-criterion ID, grouping closely related criteria when useful, so the user can judge what approval means without opening the change record. Do not substitute a document link, task list, or implementation plan for this acceptance summary. Call out any open decision separately and state that approval authorizes the summarized criteria, not unspecified adjacent work.
7. Change the status to `Approved` only after the user confirms the documented design. Do not change production code before this gate.
8. For a multi-task or multi-agent change, complete the four review gates below. Record the reviewer, inputs, decision, and follow-up in the change record before proceeding past each gate.
9. Implement only the approved scope. If implementation reveals a material design change, update the document and every affected canonical document, return the change record to `Proposed`, and obtain approval again before continuing.
10. Verify the acceptance criteria and relevant regressions. Record commands, results, intentional deviations, remaining work, and documentation updates in the change record.
11. Before the final response, reconcile approved tasks, acceptance criteria, and material review findings against observable evidence. For delegated, multi-task, or review-driven work, include the focused self-audit from `agent-task-orchestration`; a short non-delegated task may use a concise final comparison. If the audit demonstrates a reusable missing or ineffective repository skill rule, apply `learn-from-implementation-failures` and record the evidence-based correction and re-verification.
12. Set the status to `Implemented` only when the approved scope, required documentation updates, and required verification are complete and no task or acceptance criterion remains unfinished. In particular, no approved-scope task may remain `Rejected with reason`, `In progress`, `Runnable`, `Dependent`, or otherwise incomplete, and no acceptance-blocking `Externally blocked` task may remain. An `Externally blocked` item may remain only as an explicitly excluded follow-up that does not prevent any approved acceptance criterion.

## Task plan and review gates

For changes with multiple tasks or agents, include a task plan in the record (or a linked `execution-plan.md`) with `ID`, `Task`, `Owner`, `Model tier`, `Depends on`, `Write scope`, `Verification`, `Completion evidence`, and `State`. A short single task may compress the same fields into bullets. Link every task to at least one acceptance criterion and at least one verification; link every acceptance criterion back to one or more tasks. Use observable evidence, not an agent's completion claim.

Before approval, perform a **Design and task-split review**: the main owner confirms the design decisions are settled, all acceptance criteria have task and verification coverage, boundaries and dependencies are valid, parallel write scopes do not overlap, and delegation is suitable for the selected model tier. Do not delegate unresolved design decisions.

Immediately after approval and before code changes, perform a **Pre-implementation review**: classify every task as `Runnable`, `Dependent`, `Externally blocked`, or `Rejected with reason`; confirm the current frontier has non-overlapping write scopes; and record the exact worker inputs, expected evidence, and escalation conditions. Use the canonical task-state vocabulary defined below.

At each substantial checkpoint, perform a **Checkpoint review**: the main owner reads the delegated diff or primary evidence, compares it with the approved design and acceptance-criterion matrix, checks tests and scope, and records fixes, re-sequencing, or model escalation. A checkpoint may contain unfinished work; it is not a completion claim.

Before `Implemented`, perform a **Final review**: every task needed by the approved scope and every acceptance criterion is `Verified`, and no approved-scope `Rejected with reason`, `Runnable`, `In progress`, `Dependent`, or otherwise incomplete task remains. No acceptance-blocking `Externally blocked` task may remain; a non-blocking external blocker is allowed only as an explicitly excluded follow-up. Otherwise keep the record `Approved` and report the blocker. Record final diff/status checks and relevant tests. Never mark a record complete from isolated tests or scaffolding that is not connected to the documented path.

### Canonical task states and completion gate

Use these states consistently for tracked tasks and material findings: `Proposed`, `Runnable`, `In progress`, `Dependent`, `Externally blocked`, `Rejected with reason`, and `Verified`. `Rejected with reason` is valid only for work explicitly outside the approved scope; an approved-scope task cannot be rejected to bypass completion. `Externally blocked` is completion-compatible only when it is explicitly excluded and does not block an approved acceptance criterion. Before completion, reconcile tracked items to `Verified` or an explicitly documented non-blocking external follow-up, and record the next action before any interruption while runnable work remains. Do not invent additional incomplete states.

The `agent-task-orchestration` skill is required when delegation, model-tier routing, parallel workers, or worker-result adoption is part of the change. It is not required for a single short task with no delegation. The change-record gates and traceability requirements still apply whenever a change record is required.

## Implementation Traceability Gate

Before describing a slice as complete, trace each acceptance criterion through the real production path: trigger, persistence, dispatch, worker entry, domain side effect, failure recovery, and operator visibility as applicable. A model, policy, or adapter tested in isolation does not prove the feature is connected.

- Maintain an acceptance-criterion matrix with `Not started`, `Connected`, and `Verified` states. A checkpoint may contain unfinished items; completion may not.
- For replacement work, inventory old runtime registrations, entry points, callers, persisted data, configuration, infrastructure, UI, and tests. Completion requires evidence that prohibited legacy symbols have zero production callers and are not registered at runtime.
- Test at least one end-to-end happy path and the critical failure/restart path through the actual transport and persistence boundary.
- Verify operational invariants at the layer that enforces them. If priority is enforced by a dispatcher, test the dispatcher; if URL fallback is required, test the locator plus handler path.
- Treat new code calling a component that creates legacy work as continued legacy usage, even when wrapped by a new handler.

## Cutover Gate

For destructive replacement, document and verify the executable order separately from the steady-state architecture: create replacement resources; connect new producers and consumers; prevent mixed messages and dual execution; initialize state and smoke test; preserve rollback until checks pass; then delete only the approved legacy targets.

A resource rename or declarative replacement plan is not evidence of this order unless the deployment mechanism guarantees and tests every gate.

## Review Before Handoff

At each substantial checkpoint, review the diff against the acceptance-criterion matrix before reporting progress. For cross-process workflows, explicitly inspect cancellation, timeout, retry, lease expiry, duplicate delivery, multi-instance concurrency, and partial failure. Record blocking findings as unfinished work; do not present scaffolding, disconnected components, or passing isolated tests as an implemented capability.

## CodeGraph Index Freshness

When the repository contains `.codegraph/`, treat its index as a derived verification artifact whose freshness must follow source changes.

- Use CodeGraph before text search when locating code, as required by the repository instructions.
- After a coherent edit slice that adds, removes, renames, or changes production symbols or call relationships, run `codegraph sync .` before relying on `explore`, `impact`, `callers`, `callees`, or `affected` for post-change conclusions.
- At minimum, sync after the final production-code edit and before each checkpoint commit or final handoff. Sync earlier when the next implementation decision depends on the changed graph. Documentation-only or non-code artifact changes do not require a sync.
- Prefer incremental `codegraph sync .`. Use `codegraph index .` only when sync fails to restore a usable index, the index is corrupt, or CodeGraph configuration/language coverage changed.
- After syncing, re-query the changed entry points and critical callers. For replacement work, verify that prohibited legacy symbols have zero production callers and are no longer runtime-registered; a pre-edit graph is not evidence.
- Record the sync command and relevant graph verification in the change record's verification record. If syncing fails, record the failure and do not claim graph-based traceability or replacement completion.

## Commit Checkpoints

- For large or long-running changes, commit at verified checkpoints instead of waiting for the entire change set to finish. Good checkpoint boundaries include document/design updates, API or state-model changes, UI slices, tests, and final documentation synchronization.
- Before each checkpoint commit, update the change record or working notes with the completed scope, remaining work, and verification result that justifies the commit.
- Before every commit containing source or test code, inspect the repository CI workflow and run its exact formatting verification command after the final edit (currently `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`). If it reports changes, apply the formatter, then rerun the verification and every workflow-equivalent build/test gate affected by the formatted files before committing. A successful build or `git diff --check` does not replace this gate.
- Do not mix unrelated objectives in one checkpoint commit. If a checkpoint reveals a separate fix, commit it separately or leave it unstaged until the relevant scope is verified.

## Document Rules

- Link to existing architecture or design documents instead of copying them. Record only the change-specific decisions and context needed to understand the work later.
- A change record is not a substitute for maintaining current documentation. When a change changes the truth of a canonical document, correct that document in the same change set and link it from `Documentation updates`. Keep one canonical source per topic; replace duplicated rules with a link and a short scope statement.
- Describe user-visible behavior in domain language. Keep IDs, enum names, payloads, and framework details in technical notes unless they are part of the interface contract.
- Acceptance criteria must be observable. Include responsive, accessibility, empty/loading/error, permissions, and migration behavior when relevant.
- Mocks are specifications of hierarchy and interaction, not promises of pixel-perfect styling. State viewport and UI state for each mock.
- Keep alternatives that materially influenced the decision, together with the reason they were rejected.
- Do not rewrite history after implementation. Append deviations and follow-up work explicitly.

## Scope Boundary

Tiny corrections that cannot change behavior—only spelling fixes or comment-only clarification—do not require a change record. When uncertain whether a change affects behavior or review decisions, create one.
