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

## Quality bar

A correction must change a future decision and have an observable completion condition. Prefer gates that trace a requirement through its production entry point and terminal side effect, exercise the real adapter or dispatcher, prove removed infrastructure has zero runtime callers, distinguish checkpoints from completion, or verify destructive cutover ordering.

Do not add reminders such as "be careful" or "test thoroughly." Replace them with a specific artifact, query, test, or invariant.

## Boundaries

- Do not rewrite a skill solely to rationalize an isolated typo or unpredictable external failure.
- Do not weaken acceptance criteria to make prior work appear complete.
- Do not mark the underlying implementation fixed unless it was separately corrected and verified.
- Keep failure-specific narrative in the relevant change record and reusable decision rules in skills.
