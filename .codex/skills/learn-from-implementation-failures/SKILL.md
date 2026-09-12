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

### Review-to-execution closure gate

When the agent produces a numbered review or gap report and the user subsequently authorizes implementation, every reported finding becomes an explicit closure item before code changes begin.

- Copy each finding into the durable execution plan with one of: `Runnable`, `Dependent`, `Externally blocked`, or `Rejected with reason`. Do not silently reinterpret “implement the proposal” as permission to implement only the highest-priority subset.
- Give every item observable completion evidence. If a finding is intentionally combined with another task, retain a mapping from both original findings to the shared evidence.
- After each checkpoint, reconcile the original review list against commits, tests, runtime verification, and the acceptance matrix. A passing build or a polished primary screen does not close findings about pagination, large-data behavior, secondary workflows, operational cutover, or browser validation.
- Before a completion response, run a zero-open-item check. If any authorized item is still runnable or dependent on another local item, continue. If an item requires external access or destructive production authority, report it as an explicit external blocker and do not describe the whole proposal as completed.
- When a new self-review finds additional defects within the approved outcome, add them to the same closure ledger before fixing them; do not leave them only in commentary or the final response.

The observable gate is a one-to-one ledger from reported findings to completion evidence or a genuine external blocker, with no untracked “remaining work” introduced only after completion was claimed.

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

## CI parity and push closure

When a pushed change fails continuous integration after local verification, treat the mismatch between the local gate and the repository workflow as the demonstrated delivery failure.

- Read the failing workflow step and its raw log before changing code or the skill. Record the exact command, runner OS, failure location, and whether the local verification omitted or changed any of them.
- Before a later push, inspect the repository workflows and run their required formatting, build, and test commands in workflow order, including configuration, filters, restore behavior, and generated-file checks. A broader-looking command is not a substitute when its flags or prerequisites differ. After source or test edits, never use `--no-build` until the workflow-equivalent build has regenerated the binaries under test.
- Treat every formatter output as a code change: run all workflow steps that follow that formatter again. Contract tests for formatted configuration must assert parsed meaning or whitespace-tolerant structure, not formatter-controlled column alignment.
- If the failure touches paths, casing, line endings, locale, time zones, process behavior, or other platform-sensitive APIs, add an OS-neutral invariant test. Use framework path APIs instead of string normalization assumptions. When a matching runner or container is available, execute the affected test there; otherwise state that cross-OS parity remains unverified.
- Treat a successful `git push` as dispatch, not completion, when the requested outcome includes a healthy GitHub Actions run. Watch the new commit's required workflows to a terminal state, inspect every failure, fix it, rerun the exact local gates, and push again until they pass or an external blocker is established.
- Add the repository's exact pre-push commands to the durable change record or contributor guidance when they were previously absent, so the next agent does not have to rediscover the CI contract from a failed run.

The observable correction is that the same commit passes the locally reproduced workflow commands and the remote required workflows, with any platform-sensitive fix covered by a test that executes on the CI runner OS.

## External adapter fidelity gate

When a production failure occurs because an external page, payload, or navigation graph differs from a test fixture, treat fixture fidelity and terminal validation as separate gates.

- Capture the smallest production-shaped evidence that distinguishes the failure: candidate count and ordering, labels/attributes, resolved URL, identified page kind, and requested resource identity. Do not reduce a multi-candidate production structure to a single ideal candidate in the regression fixture.
- For selection logic, include at least one ambiguous fixture where a plausible wrong candidate appears before the correct candidate. Assert the semantic selection criterion and the terminal outcome (for example, a RaceCard request produces a validated RaceCard page for the requested race), not merely that some URL was extracted or navigation returned HTTP 200.
- Inventory whether live-site tests are excluded by the normal CI filter. If they are, record and run a bounded pre-release smoke test for the changed adapter against a current, non-destructive target, or explicitly record that live compatibility remains unverified. A passing fixture suite must not be reported as proof of current external-site compatibility.
- When production logs reveal a richer structure than the stored fixture, add or refresh a sanitized fixture and its regression test as part of the fix. Keep volatile tokens and URLs out of identity assertions; validate stable semantics and page identification instead.
- Verify failure classification as well as success: a resolved page of the wrong kind or identity must become `UnexpectedPage`/validation failure with the observed URL and page kind, rather than a generic exception that hides the selection defect.

The observable correction is a regression test that fails with the production-shaped ambiguity, passes only when the correct semantic candidate is chosen, and proves the requested resource type and identity at the workflow boundary.

## Behavior-preservation gate for infrastructure migrations

When an existing capability stops working after moving it onto a new queue, scheduler, collection platform, or execution path, do not describe an internal shortcut as an intentional specification change unless the approved change record explicitly changed that user-visible behavior.

- Inventory the old production path's observable guarantees before replacing it: semantic target selection, fallbacks, terminal validation, persistence side effects, and operator-visible diagnostics. Map each guarantee to the new path or record an approved removal.
- For optimizations such as direct URLs, caches, batching, or fewer browser navigations, test them as alternate routes to the same terminal contract. The optimization is invalid if it can return a different resource kind or identity, even when it is faster and HTTP succeeds.
- Add a migration regression test that enters through the new production handler and proves the same terminal domain result as the old path. Unit tests for intermediate URL capture, queue publication, or parser invocation do not satisfy this gate alone.
- During review, classify a changed behavior as one of `approved change`, `preserved`, or `regression`. Any unapproved change in a previously working flow is a regression and must be fixed before declaring the migration complete.
- When an implementation introduces an assumption that the old path did not require (for example, “the first anchor is the desired resource”), require production-shaped evidence for that invariant. Without evidence, retain semantic selection or validation from the old path.

The observable correction is a behavior map plus an end-to-end regression test demonstrating that infrastructure replacement preserved each approved user-visible capability.

## Boundaries

- Do not rewrite a skill solely to rationalize an isolated typo or unpredictable external failure.
- Do not weaken acceptance criteria to make prior work appear complete.
- Do not mark the underlying implementation fixed unless it was separately corrected and verified.
- Keep failure-specific narrative in the relevant change record and reusable decision rules in skills.
