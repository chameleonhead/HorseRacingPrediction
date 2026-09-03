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
6. Set the document status to `Proposed` while decisions remain open. Present the document to the user and request explicit confirmation.
7. Change the status to `Approved` only after the user confirms the documented design. Do not change production code before this gate.
8. Implement only the approved scope. If implementation reveals a material design change, update the document and every affected canonical document, return the change record to `Proposed`, and obtain approval again before continuing.
9. Verify the acceptance criteria and relevant regressions. Record commands, results, intentional deviations, remaining work, and documentation updates in the change record.
10. Set the status to `Implemented` only when the approved scope, required documentation updates, and required verification are complete.

## Commit Checkpoints

- For large or long-running changes, commit at verified checkpoints instead of waiting for the entire change set to finish. Good checkpoint boundaries include document/design updates, API or state-model changes, UI slices, tests, and final documentation synchronization.
- Before each checkpoint commit, update the change record or working notes with the completed scope, remaining work, and verification result that justifies the commit.
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

Tiny corrections that cannot change behavior—such as spelling fixes or comment-only clarification—do not require a change record. When uncertain whether a change affects behavior or review decisions, create one.
