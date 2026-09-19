# Change-record format

Use the following sections when they are relevant; omit empty boilerplate.

Store each record at `docs/changes/yyyyMMdd_<change-name>/README.md`. `yyyyMMdd` is the change-set creation date and `<change-name>` is short kebab-case. Keep the original directory name after creation even when the record is updated on later dates.

```markdown
# <change title>

- Status: Proposed | Approved | Implemented | Superseded
- Owner: <person or team>
- Created: YYYY-MM-DD
- Updated: YYYY-MM-DD

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | <evidence or next action> |
| Verification | Not started | <evidence or next action> |
| Deployment/operation | Not started | <evidence, next action, or Not applicable> |

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

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | <observable outcome> | T1 | <evidence> | Not started |
## Delivery plan
## Task plan
For multi-task or multi-agent changes, use this table (a short single task may use equivalent bullets):

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | <task> | Main or Worker | High capability or Cost efficient | - | <paths or read-only> | <command or observable check> | <artifact or result> | Proposed |

For delegated coding tasks, link `agent-audits/<task-id>.json` from Completion evidence or the Verification record. Summarize requested/observed model, model verification, usage availability, quality verdict, independent challenge, review passes and effort, rework/promotion, total successful-outcome cost when measurable, and the routing recommendation. Never infer observed model or token usage from the requested configuration.

Link each task to its acceptance criteria and each acceptance criterion to its task(s) and verification. Use observable evidence and keep state current. The canonical states are `Proposed`, `Runnable`, `In progress`, `Dependent`, `Externally blocked`, `Rejected with reason`, and `Verified`. `Rejected with reason` may be used only for work explicitly outside the approved scope; an approved-scope task cannot be rejected to bypass completion. `Externally blocked` may remain at completion only when it is an explicitly excluded follow-up that does not prevent an approved acceptance criterion.

## Review gates
For multi-task or delegated changes, record the reviewer, inputs, decision, and follow-up for each gate:

- **Design and task-split review** — before approval; confirms settled decisions, AC/task/verification coverage, valid dependencies, non-overlapping write scopes, and suitable model tier.
- **Pre-implementation review** — after approval and before code changes; classifies every task and confirms the runnable frontier and worker contracts.
- **Checkpoint review** — at each substantial checkpoint; compares worker evidence, full diff, tests, and the acceptance-criterion matrix, recording fixes or escalation.
- **Final review** — before `Implemented`; confirms every approved task and AC is `Verified`, with no approved-scope rejected, runnable, dependent, in-progress, or acceptance-blocking external work left. A genuine external blocker keeps the record `Approved` unless it is an explicitly excluded follow-up that does not prevent an AC. Reconcile the task/finding ledger when the work has multiple tasks, delegation, or material review findings; a short non-delegated task may record the same conclusion concisely. Record the next action if work is interrupted.

For delegated coding, Final review also reconciles each task audit: attributable patch, quality/scope gates, independent evidence, requested versus observed model, token telemetry availability, automated and human review effort, correction/re-verification, escaped defects, comparable baseline, and guarded routing recommendation.

## Verification record
## Deviations and follow-up
```

Status meanings:

- `Proposed`: The document is reviewable, but production implementation is blocked.
- `Approved`: The user explicitly confirmed the documented design; implementation may begin.
- `Implemented`: Approved scope is implemented and its verification record is complete.
- `Superseded`: Another linked record replaces this proposal.

Use these four values exactly. Do not add parenthetical qualifiers such as `Implemented (deployment pending)`. When code, verification, and deployment or operations differ, keep the whole-record Status at the least complete acceptance-blocking stage and describe the partial states in `Completion summary`.

Acceptance-criterion states are `Not started`, `Connected`, and `Verified`. Every AC table includes a State column. `Implemented` requires every approved AC to be `Verified`; prose, a passing build, or a later change record does not replace updating the originating AC row and verification record.

For UI work, store mocks in `mocks/`. A text wireframe is appropriate for information hierarchy and responsive behavior. Use an image or runnable prototype only when spatial or visual details cannot be reviewed reliably in text.

`Documentation updates` lists every non-change-record document added or changed by the change set. For each item, include its path, a concise description of the change, and its relationship to the canonical source. If none were needed, state that outcome and the documents inspected.
