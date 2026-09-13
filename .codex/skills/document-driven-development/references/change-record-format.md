# Change-record format

Use the following sections when they are relevant; omit empty boilerplate.

Store each record at `docs/changes/yyyyMMdd_<change-name>/README.md`. `yyyyMMdd` is the change-set creation date and `<change-name>` is short kebab-case. Keep the original directory name after creation even when the record is updated on later dates.

```markdown
# <change title>

- Status: Proposed | Approved | Implemented | Superseded
- Owner: <person or team>
- Created: YYYY-MM-DD
- Updated: YYYY-MM-DD

## Context
## Goals
## Non-goals
## Experience and interaction design
## Navigation and relationships
## Mocks
## Documentation updates
## Technical impact
## Decisions
## Acceptance criteria
## Delivery plan
## Task plan
For multi-task or multi-agent changes, use this table (a short single task may use equivalent bullets):

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | <task> | Main or Worker | High capability or Cost efficient | - | <paths or read-only> | <command or observable check> | <artifact or result> | Proposed |

Link each task to its acceptance criteria and each acceptance criterion to its task(s) and verification. Use observable evidence and keep state current. Recommended states are `Proposed`, `Runnable`, `In progress`, `Dependent`, `Externally blocked`, `Rejected with reason`, and `Verified`.

## Review gates
For multi-task or delegated changes, record the reviewer, inputs, decision, and follow-up for each gate:

- **Design and task-split review** — before approval; confirms settled decisions, AC/task/verification coverage, valid dependencies, non-overlapping write scopes, and suitable model tier.
- **Pre-implementation review** — after approval and before code changes; classifies every task and confirms the runnable frontier and worker contracts.
- **Checkpoint review** — at each substantial checkpoint; compares worker evidence, full diff, tests, and the acceptance-criterion matrix, recording fixes or escalation.
- **Final review** — before `Implemented`; confirms every approved task and AC is `Verified`, with no runnable, dependent, in-progress, or acceptance-blocking external work left. A genuine external blocker keeps the record `Approved` unless it is an explicitly excluded follow-up that does not prevent an AC.

## Verification record
## Deviations and follow-up
```

Status meanings:

- `Proposed`: The document is reviewable, but production implementation is blocked.
- `Approved`: The user explicitly confirmed the documented design; implementation may begin.
- `Implemented`: Approved scope is implemented and its verification record is complete.
- `Superseded`: Another linked record replaces this proposal.

For UI work, store mocks in `mocks/`. A text wireframe is appropriate for information hierarchy and responsive behavior. Use an image or runnable prototype only when spatial or visual details cannot be reviewed reliably in text.

`Documentation updates` lists every non-change-record document added or changed by the change set. For each item, include its path, a concise description of the change, and its relationship to the canonical source. If none were needed, state that outcome and the documents inspected.
