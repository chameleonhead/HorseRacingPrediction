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
