---
name: learn-from-implementation-failures
description: Analyze a demonstrated implementation or delivery failure, distinguish immediate and systemic causes, and make the smallest evidence-based updates to relevant repository skills. Use when the user asks why work went wrong, requests lessons learned, or asks Codex to improve its development process after a mistake.
---

# Learn from Implementation Failures

Turn observed failures into narrow, testable improvements to future decisions. Do not use hypothetical risks alone as justification for adding rules.

## Workflow

1. Reconstruct the intended outcome from the approved requirements, acceptance criteria, and user corrections.
2. Establish what actually happened from code, tests, tool output, commits, and review findings. Separate verified facts from inference.
3. For each material failure, identify the incorrect outcome, immediate technical cause, workflow failure that allowed it, missing check, and an observable future gate.
4. Group repeated symptoms under one systemic cause. Avoid creating one rule per bug.
5. Update the narrowest existing skill whose decisions should change. Create a specialized skill only when the capability is independently reusable.
6. Preserve authorization and approved design. A retrospective does not grant permission to change unrelated code, external systems, or user data.
7. Validate every modified skill with the skill-creator validator. Inspect the final diff for generic platitudes, duplicated policy, unfinished placeholders, and unverifiable rules.
8. Report the causes, changed skills, and the concrete behavior that will differ next time.

## Repeated interruption and autonomy review

When a multi-step implementation repeatedly stops at checkpoints while safe approved work remains, treat the interruption pattern itself as a delivery failure and analyze it before the next checkpoint.

- Build a dependency graph for all remaining acceptance criteria. Mark tasks as runnable, blocked, or dependent; do not use a flat list that encourages stopping after the first item.
- Execute the entire runnable frontier. When delegation is authorized and tasks have non-overlapping write scopes, assign independent frontier tasks in parallel while retaining integration, skill interpretation, and final verification in the main agent.
- In a shared worktree, non-overlapping ownership also means preserving a buildable integration surface. Add required types before their callers, avoid leaving uncompilable intermediate edits across waits, run a fast build immediately after cross-project contract changes, and notify other owners as soon as the shared build is restored. Treat repeated cross-agent compile blocking as evidence that task boundaries or edit order must be tightened.
- A commit, passing focused test, context compaction, or completed subtask is a checkpoint, not a stopping condition. Immediately select the next runnable task after recording it.
- Maintain a durable execution plan containing task owner, dependency, write scope, verification command, and completion evidence so work can resume without rediscovery after compaction.
- Before yielding, check whether any approved task is runnable with current authority and tools. If so, continue. Yield only when the requested outcome is complete or a permitted blocker genuinely requires user/external input.
- If a task is too large for one slice, split it by independently verifiable production paths, not by layers that leave disconnected scaffolding.

The observable correction is sustained progress across multiple dependent checkpoints, with the execution plan and acceptance matrix showing why work continued or why it was genuinely blocked.

### Resume protocol after an interruption

When work resumes after a user interjection, context compaction, tool failure, process restart, or another interruption, do not reconstruct the task from memory or restart completed work.

1. Read the latest user request together with the active approved change record and durable execution plan.
2. Inspect repository evidence (`git status`, the latest relevant commits, and focused diffs), runtime evidence (health/process state when applicable), and the most recent verification results.
3. Classify each planned item as complete with evidence, currently modified, runnable next, or genuinely blocked. Preserve unrelated user changes.
4. Continue from the first runnable unmet acceptance criterion. Re-run only the smallest verification needed to establish whether previously completed work is still valid.
5. Before another possible interruption, record the exact next action, verification command, runtime identifiers, and files intentionally left uncommitted in the durable plan or change record.

For open-ended quality requests such as “iterate until it looks good,” define an observable loop before editing: inspect the rendered result, name the largest remaining usability or visual defect, make one coherent adjustment, and render again. Stop only after a full pass finds no material defect in hierarchy, density, alignment, responsive behavior, interaction feedback, or accessibility—not merely after the first successful build.

The recovery gate is that a new agent or compacted continuation can identify the next command and remaining acceptance gap from repository artifacts and current state in under one inspection pass.

## Quality bar

A correction must change a future decision and have an observable completion condition. Prefer gates that trace a requirement through its production entry point and terminal side effect, exercise the real adapter or dispatcher, prove removed infrastructure has zero runtime callers, distinguish checkpoints from completion, or verify destructive cutover ordering.

Do not add reminders such as "be careful" or "test thoroughly." Replace them with a specific artifact, query, test, or invariant.

## Boundaries

- Do not rewrite a skill solely to rationalize an isolated typo or unpredictable external failure.
- Do not weaken acceptance criteria to make prior work appear complete.
- Do not mark the underlying implementation fixed unless it was separately corrected and verified.
- Keep failure-specific narrative in the relevant change record and reusable decision rules in skills.
