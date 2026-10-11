# Consistency and migration-first simplicity

- Status: Implemented
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-11
- Updated: 2026-10-11

## Context and approval

The user explicitly requested a generic skill rule preventing mismatches and prioritizing simple implementation and consideration of data migration over accumulating compatibility logic. Their follow-up requires this rule not to be limited to identities or a particular incident. This instruction approves this narrow policy update only. Their willingness to reset a DB after failure is not authorization to reset any DB now.

Observed basis: historical horse IDs used caller-supplied NormalizedName, while current legacy recognition reads RegisteredName only. The synthetic mismatch is reproduced; the Tokyo production cause remains unconfirmed. This policy does not claim that the incident has been fixed.

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not applicable | No application code changes. |
| Verification | Verified | Skill validator passed; independent behavioral/scope review completed. |
| Deployment/operation | Not applicable | No DB migration/reset, production change, or push. |

## Decisions and acceptance criteria

One canonical section in document-driven-development governs design decisions; preserve unrelated orchestration/AGENTS user changes. The approved generic policy requires consistent specification/data/implementation contracts across producers, persistence and consumers; representative prior/current input path tests when changing rules; correction/migration-first comparison when compatibility branches would complicate runtime; bounded transitional compatibility; and independent approval of destructive actions. Data-free code defects do not require migration merely to satisfy a rule.

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Skill prevents mismatched specification/data/implementation rules using a canonical contract and representative prior/current path checks, not an incident-specific mandate. | T1,T2,T3 | Focused diff, changed data contract, and code-only inconsistency scenarios. | Verified |
| AC2 | When mismatches occur, prefer a simpler canonical runtime and assess data migration before permanent compatibility branches; no arbitrary merging or automatic reset permission. | T1,T2,T3 | Migration-vs-branches scenario and ambiguous-source/reset counterexample. | Verified |
| AC3 | Only the intended skill section and task documents change; skill validation passes and existing user edits remain untouched. | T2,T3 | quick_validate, diff check, exact scoped diff/status. | Verified |

## Concern and agreement ledger

- Complexity vs preservation: user explicitly prefers simplicity and consideration of migration. Apply this preference without assuming loss of every DB is acceptable; migration correctness and safeguards still apply. Resolved in design.
- Absolute no-mismatch guarantees cannot be established by wording. Require concrete consistency checks and historical/current producer-consumer test cases, not a promise of infallibility. Resolved in design.
- DB reset deletes data. This change records only the preference; concrete environment, targets, impact, backup/recovery decision and authorization remain required before execution. Resolved in design.

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Generic simplicity preference must not turn every correction into a migration. | Unnecessary operational burden. | Distinguish code-only defects from inconsistent stored data. | AC1,AC2/T1-T3 | Agree with scoped rule | Explicit generic policy request | Resolved in design |
| C2 | Willingness to reset after failure does not name a DB or authorize present deletion. | Data loss through expanded authority. | Keep target-specific reset approval and impact/recovery safeguards; no reset in this change. | AC2,AC3/T1-T3 | Agree; no destructive action dispatched | Policy request only | Resolved in design |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Freeze user-directed policy and authorization boundary. | Lead | Lead | - | This record | Requirement and safety review | Explicit user instruction; decisions above | Verified | Lead owns policy/security decision | none | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | Add narrow canonical skill section and run validation. | hold_impl | Luna | T1 | .codex/skills/document-driven-development/SKILL.md only | skill-creator quick_validate; diff check; bounded scenario review | Final scoped diff; validator valid; one Lead-requested corrective pass | Verified | Existing gpt-6-luna/high requested route, observed telemetry unavailable | T2-A1 | unavailable; retries 1; corrections 1; reviews 2 |
| T3 | Independently accept policy scope and forward scenarios. | Lead | Lead | T2 | This record | Independent behavioral/scope review; canonical audits | Independent final diff and scenario review below | Verified | Lead owns final acceptance and destructive boundary | none | unavailable; retries 0; corrections 0; reviews 2 |

## Pre-implementation readiness and review gates

Main loaded skill-creator, learn-from-implementation-failures, document-driven-development and agent-task-orchestration in this turn. Design/task-split: one canonical policy file, no parallel ownership, no user dirty overlap. Purpose is general specification/data/implementation consistency and simpler runtime decisions; no application/DB implementation. Worker may add one concise section near Implementation Traceability Gate and must not alter existing approval/data-protection rules.

Ordered steps: add policy; validate SKILL frontmatter/placeholders; review old/new ID, migration-vs-compatibility, unknown-source, authorized preference vs actual DB reset, and low-risk comment-only cases; Lead reviews diff and evidence; update completion states. No new scripts/tests are needed for prose-only policy: structural validator plus behavioral reasoning and independent Lead review cover this scope; no application build is relevant.

Canonical execution audit must pass before the first skill edit. No model observation or usage amount is inferred. Runtime telemetry unavailable does not block the existing requested Luna route.

Readiness evidence: `python scripts/audit_agent_execution.py docs/changes/20261011_identity-simplicity-policy` passed before dispatch to the existing `hold_impl` requested gpt-6-luna/high route. The final prompt incorporated the user's generic-scope clarification; exact write scope remains one clean skill file. No application, migration, or DB reset is dispatched.

## Documentation updates

`.codex/skills/document-driven-development/SKILL.md` is the canonical new rule. No changes to AGENTS, orchestration skill/tests, production incident safety rules, or application design documents are required by this preference-only update.

## Verification record

- Worker ran `python C:/Users/yuto.nagano/.codex/skills/.system/skill-creator/scripts/quick_validate.py .codex/skills/document-driven-development`: `Skill is valid!`; rerun passed after the single corrective pass. `git diff --check` passed. LF-to-CRLF notification is the existing Git checkout normalization notice, not a failed gate.
- Checkpoint review: Lead moved the subsection out of the existing traceability list and required preventive prior/current path checks. Worker applied this one correction and revalidated. Final diff is one generic section; no incident-specific details or unrelated rule changes.
- Independent forward review (reasoned policy scenarios, not application tests): a changed persisted status/format must compare migration with permanent decoding branches and verify old/new transport paths; an inconsistent code calculation with consistent stored data needs a shared code correction, not a migration; nonrecoverable/ambiguous records cannot be force-matched or reset by preference alone; a comment-only edit does not invent migration obligations.
- Final review: AC1-AC3 and T1-T3 Verified. User policy is preserved without granting destructive authority; independent diff confirms exact skill scope and unchanged surrounding rules. Canonical audit and DDD validator are rerun before handoff. No application code, data migration/reset, deployment, commit, or push is claimed.
- Historical handoff: this record/audit and skill patch were left uncommitted alongside pre-existing user files. The separate recurring-incident proposal remains Proposed; no compatibility-only or migration execution proposal is approved by this policy change.
- Continuation checkpoint2026-10-11: unchanged final policy patch independently re-inspected; quick_validate reports `Skill is valid!`, canonical execution audit passed, changed-record validator issues=0, diffcheck passed. Commit only this skill and policy record/audit as a documentation-only verified unit. Application build/test/graph are not applicable to this prose-only checkpoint. User-owned orchestration/AGENTS edits and ongoing diagnostic UI files remain unstaged. Continue diagnostic T2/T3/T4 after this checkpoint; no push or production operation is established by the commit.
