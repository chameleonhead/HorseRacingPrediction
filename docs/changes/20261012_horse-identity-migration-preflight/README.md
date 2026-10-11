# Saved-data migration preflight

- Status: Proposed
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-12
- Updated: 2026-10-12

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | This record proposes bounded read-only acquisition only. No implementation or migration is authorized here. |
| Verification | Not started | Proposal validators are required; implementation tests and target evidence belong to later approved work. |
| Deployment/operation | Not started | No production read has been performed for this proposal. After separate approval, acceptance includes the normal CI/CD path and a bounded read-only production check; migration, resume, and reset remain excluded. |

## Context and goal

The parent [recurring identity-conflict record](../20261011_recurring-horse-identity-conflict/README.md) requires an all-data migration assessment, not a name-based fix for the two observed horses. M2 found 47 source-defined application tables across EventStore, CollectionPlatform, PredictionSchedule, and optional LocalCollectionQueue, plus migration metadata, repair sidecars, external queue/DLQ, configured files, and other storage surfaces requiring separate reconciliation. These are source mappings, not a census of the deployed target.

The existing [single-horse diagnostic](../20261011_horse-identity-diagnostics/README.md) establishes a useful read-only connection and evidence pattern. It cannot stand in for a bounded batched inventory of horse identities and typed references. HorseRegistered persists names and profile fields, but not the source or identity-generation input; missing provenance must remain Unverifiable, not be inferred from a name.

This proposal covers a read-only, paged preflight that gathers bounded evidence from explicitly configured SQLite surfaces and makes gaps visible. Its supported row scans are deliberately limited to the allowlist below; other known tables are schema-only, not row-inspected. Completing all pages means only that the listed supported pages were read at their separate observation times. It is not a fixed migration manifest, point-in-time cross-store snapshot, proof of all production storage, or permission to apply a migration.

## Goals

- Reconcile physical SQLite objects observed at runtime against the allowlisted source model and show supported, unsupported, unknown, and unavailable coverage explicitly.
- Enumerate horse roots and known typed identity/reference surfaces in bounded keyset pages, with batched reads rather than one request or query per horse.
- Present only the approved domain evidence needed to decide what further investigation is required. Never promote a calculated or matching ID into identity proof.
- Keep every read on a dedicated read-only/query-only connection with bounded request duration, cancellation, row/payload limits, and immediate admission rejection when busy.
- Keep the authenticated admin user informed about page progress, observation times, partial failure, restart/expiry, and the non-atomic nature of separate-store reads.
- Preserve the parent’s unresolved apply, coherent-generation, external-delivery, runner, cutover, and recovery decisions for later design and authorization.

## Non-goals and frozen boundaries

- No domain, collection, schema, queue, prediction, or user-data writes; no migration, apply, resume, reset, retry, or repair operation.
- No new durable report, operation journal, background poller, short polling loop, generic migration framework, arbitrary SQL/command/path input, or privileged runner.
- No raw event JSON, metadata JSON, source URL, database path, SQL, actor/caller log, secret, malformed identifier, or free-form exception in the response.
- No name-only matching, arbitrary legacy-ID adoption, source guessing, or automatic Keep/Migrate decision.
- No claim that the 47 source-defined table list equals the production schema/data or that a completed supported-surface scan covers SQS/DLQ, external files, sidecars, model storage, or unknown sources.
- No fixed apply manifest: store pages are separate reads and cannot establish one coherent generation or rule out same-count changes between pages.
- No migration implementation or cutover plan. The parent remains Proposed, and its AC2–AC5 decisions remain Lead-owned.

## Proposed interaction

An authenticated administrator opens a dedicated preflight page from Settings, starts a read-only scan, and advances one bounded page at a time. The page never offers Apply, Resume, Retry-job, or Reset. It does not poll. A browser refresh or expired continuation starts a fresh observation and clearly replaces the previous run as non-current evidence.

The first response identifies the supported configured source surfaces, observed schema compatibility, and the first bounded page. Later responses continue using an opaque server-protected cursor. Each page records the observation time for the store it read. Separate EventStore and CollectionPlatform observations are explicitly shown as non-atomic. A completed page sequence is labelled “Supported configured sources scanned”; it is never labelled “migration ready” or “all data verified.”

The UI distinguishes:

- Supported page read and the bounded counts/items returned.
- Unsupported schema or event shape.
- Unknown physical objects or noncanonical IDs.
- Unavailable/NotInspected stores and external sources.
- Truncated or oversized evidence.
- Busy, timeout, cancellation, invalid/expired cursor, and incomplete sequence.
- Unverifiable identity provenance, conflicting evidence, unknown shapes, truncation, and incomplete coverage.

## Fixed source and row-coverage ledger

The preflight inspects physical schema on these four configured SQLite surfaces: EventStore, CollectionPlatform, PredictionSchedule, and LocalCollectionQueue when configured. A missing optional queue is `NotConfigured`; an inaccessible configured store is `Unavailable`. The 47 source-defined application tables in the parent inventory are not a promise that production has exactly those objects. Schema inspection compares physical objects with the known source mappings without returning raw table names; unknown objects and versions are represented by counts/status.

Only these known tables receive bounded row inspection in this child:

| Store | Physical tables read | Evidence purpose |
| --- | --- | --- |
| EventStore | `Horses`; `EventEntity` rows for Horse aggregates, parsing the supported registration event and counting unknown/malformed event types or shapes as `UnknownShape`; `JraSubjectProfileReadModel`; `HorseRaceHistories`; `HorseWeightHistories` | Horse root identity, initial registration evidence, typed profile identity, and bounded typed history references. |
| CollectionPlatform | `collection_resources` restricted to the exact horse resource type/provider | Existing collection resource identity and typed resource reference evidence; no raw attributes/metadata. |

All other known EventStore and CollectionPlatform application tables, all `prediction_candidates`, all `local_collection_messages`, migration-history tables, SQLite internals, and sidecars are classified `SchemaOnly`/`NotInspected` by this child. They are not row-counted or represented as empty. External SQS/DLQ, files, model storage, backups, and host state are `NotInspected`. This bounded scope does not satisfy the parent’s full migration inventory by itself.

The coverage vocabulary describes observations only: `Observed`, `SchemaOnly`, `Unsupported`, `UnknownShape`, `Truncated`, `Unavailable`, and `NotInspected`. Row evidence may additionally be `Unverifiable` or `Conflict`. It contains no Keep/Migrate decision and no calculated candidate ID. Exact source identity is exposed only when it passes the canonical JRA allowlist; otherwise the response reports a safe status without echoing the value.

## Finite route ledger

| Method and path | Proposed use | Query/body | Success and failure behavior | Consumer |
| --- | --- | --- | --- | --- |
| GET /api/v2/admin/identity-migration-preflight | Start a new bounded observation when cursor is absent; continue one page when cursor is supplied. Registered under the existing `/api` prefix and API-key-protected `/v2/admin` group. | Optional single opaque cursor query; no body. Cursor is server-protected, at most 4 KiB, expires within 30 minutes, and binds the fixed scan policy, configured surface selection, schema assumptions, and keyset position. It contains no caller-controlled path, SQL, or free-form scan instruction. | 200 typed page, including explicit partial/unknown coverage; 400 invalid/expired/restart-required cursor; 401 unauthenticated; 429 if the single global admission slot is occupied; 503 for an unavailable store or bounded deadline. Unexpected schema is reported as incomplete typed evidence, not raw schema/SQL. Cache-Control: no-store. | Dedicated authorized admin page through server-side AdminApiClient only. |

This finite route is the only route in this child. Do not add separate per-horse routes, an execution route, a durable status route, or a second authentication model. A cursor does not preserve a database snapshot or make repeated requests idempotent against changing stores. After restart, the token may be invalidated and the user starts again.

## Response boundary

The typed response contains:

- A scan/page state, allowlisted source-surface code, page/keyset continuation state, and a store-specific UTC observation timestamp where available.
- Bounded counts for rows inspected and each observation state, reconciled against the fixed row allowlist above.
- Bounded domain evidence only: canonical HorseId; current and initial registered/normalized names; optional birth date; validated canonical JRA identity and its observation status; bounded related RaceId/HorseNumber evidence; and explicit coverage/truncation state.
- A protected continuation cursor or an explicit end-of-supported-pages marker.

Source URL, raw event or metadata payload, SQL, filesystem path, actor, caller log, exception message, secret, calculated candidate ID, and unrecognized identifier values are not serialized. Unsupported types, malformed JSON, oversized values, duplicate registration events, noncanonical IDs, missing references, and unconfigured/external surfaces become observation counts/statuses; their raw values are not echoed.

The scan uses a fixed maximum of 128 root identities per page and batched typed reference reads, not N+1 queries. Each HTTP request has a 30-second upper bound, cancellation propagation, and one global immediate-admission slot; a busy request returns 429 without waiting. Payload-size, JSON-depth, response-size, and driver timeout limits are server-owned and must be no looser than the existing single-horse diagnostic limits. Oversized or unparseable records are counted as incomplete rather than skipped.

Each configured SQLite file is opened through a dedicated read-only connection with query_only enabled and a deferred read transaction. Do not instantiate a store whose constructor invokes a migrator. A page may read one configured store at a time; the continuation never claims a transaction across stores or HTTP requests. The complete scan remains a sequence of observations, not a coherent apply input.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | M2’s 47 tables are source-defined mappings; target physical objects, rows, and external stores are not reconciled by that count. | A source-complete page run could be mistaken for whole-target coverage. | Inspect schema on the four configured SQLite surfaces, row-read only the fixed six-table allowlist above, label all other known tables SchemaOnly/NotInspected, and keep external/unavailable/unknown surfaces explicit. This child makes no whole-target or migration-ready claim. | AC1–AC3; P1/P2; unknown table/store counterexample | Do not infer target coverage from source code. | Proposed for this child; parent target census stays Lead-owned. | Resolved in design |
| C2 | Event registrations do not persist the source/generator input; observed Tokyo/Kyoto names and known URLs do not reproduce the stored IDs. | Name-based classification could silently conflate distinct horses. | Keep missing origin Unverifiable; classify same-name/different-source and noncanonical/unknown IDs without automatic mapping. | AC2; P2; same-name distinct-source and missing-origin tests | A differing or matching calculated ID is not identity proof. | Frozen parent safety boundary. | Resolved in design |
| C3 | Event, collection, and schedule stores are separate; each paged read occurs at a separate time. | A multi-page scan cannot freeze one generation or guarantee no same-count mutation between pages. | Show per-store observation time and non-atomic warning; no fixed manifest or apply eligibility. | AC1, AC3, AC4; P1/P2/P3; source-change-between-pages counterexample | Keep coherent-generation decision in parent. | Parent Open decision remains owned by Lead and is outside this child’s ACs. | Resolved in design |
| C4 | A cursor could expose row keys or accept altered scan instructions, and browser/API restart can invalidate continuation. | Disclosure, path injection, or false resume claims. | Server-protect a bounded cursor (≤4 KiB, ≤30 minutes), validate policy/surface/schema assumptions, accept no arbitrary query instructions, and restart fresh after invalidation. Check existing protection-key setup before implementation; no new secret/config contract. | AC4, AC5; P2/P3; tampered/oversized/expired cursor tests | No persistence/resume promise. | Frozen child boundary; exact internal protection API remains implementation detail. | Resolved in design |
| C5 | Existing default store constructors may run migrations, while response evidence is user/business data. | An ostensibly diagnostic route could write schema or disclose raw payloads. | Use the tested dedicated read-only/query-only connection path, field allowlist, SQL-side size bounds, no raw payloads, and before/after no-write tests. | AC4; P2/P4; constructor migration and malformed/oversized payload tests | Never reuse a migration-owning constructor. | Frozen child boundary. | Resolved in design |
| C6 | Host-side migration runner, external delivery fence, coherent backups, apply authorization, and restart recovery remain unresolved in the parent. | A read-only preflight must not be presented as the requested eventual migration command or recovery completion. | Exclude execution/status journal/runner/cutover from this child; parent Lead resolves before T4/U1/T5. | AC5; P5; no-apply-button/restart test | This child is an evidence prerequisite only. | Parent AC2–AC5 remain Proposed/Not started. | Excluded follow-up |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | The page distinguishes source-defined mappings from actual observed schema on the four configured SQLite surfaces, row-inspects only the fixed allowlist, records per-store observation times, and never equates all pages scanned with target-wide completeness or a fixed manifest. | P1, P2, P4 | Source/target unknown-surface fixtures; schema-only versus row-inspected assertions; separate-store timestamp and between-page change tests; manual review against M2 inventory. | Not started |
| AC2 | Bounded batched evidence reports observation-only `Observed`/`Unverifiable`/`Conflict`/`UnknownShape`/`Truncated`/`Unavailable`/`NotInspected` states; same-name/different-source and missing-source evidence never auto-merge or establish migration eligibility. No calculated candidate ID is returned. | P1, P2 | Pure classification tests plus real SQLite event/profile/reference fixtures and actual reader path. | Not started |
| AC3 | The six allowlisted tables are read in pages of at most 128 root identities with batched queries, count reconciliation, explicit truncation, and no N+1 or full-memory load; remaining known tables are not row-counted. Unknown/malformed Horse event shapes count as `UnknownShape`, not an absent registration. | P2 | Query-count/row-cap/continuation-boundary tests, duplicate/missing page counterexamples, and measured query plans on seeded SQLite. | Not started |
| AC4 | Every read uses dedicated read-only/query-only access with bounded cancellation and immediate single-slot admission; API-key protection, cursor validation, and response allowlisting hold through the actual HTTP adapter. | P2, P3 | Before/after rows/schema/control snapshots; attempted write rejection; lock/timeout/cancel/admission tests; HTTP 401/400/429/503 tests; raw payload/URL/exception redaction tests. | Not started |
| AC5 | An authorized responsive admin page advances pages explicitly, preserves or labels stale/incomplete results after errors, restarts clearly after cursor expiry/restart, and offers no apply/resume/reset action. Following separate approval, acceptance uses the normal CI/CD process and a bounded read-only production request; migration, resume, and reset remain outside this child. | P4, P5 | Component tests for loading/empty/partial/error/expired and duplicate actions; browser desktop/390px/revisit/keyboard checks; normal deployment gate and sanitized read-only production response evidence. | Not started |

## Documentation updates

- This record and its linked mock/task plan are the canonical proposal for this bounded preflight child.
- docs/20-admin-ui-design.md receives only a clearly Proposed navigation/design supplement; it does not alter current runtime behavior. The parent whole-data-migration.md and admin-migration-command.md remain canonical for eventual all-data migration and execution boundaries.
- Existing M2 persistence inventory, M3 command analysis, and T1 diagnostic record are linked above rather than copied. No source/test or operational document is changed.

## Tasks and verification

The task plan is in [execution-plan.md](execution-plan.md); the desktop/mobile hierarchy and states are in [the preflight mock](mocks/preflight.md). All implementation tasks remain Dependent until the parent Lead reviews this proposal and separately freezes the public/data/security decisions. User approval of this child cannot approve parent apply, reset, resume, external delivery, runner privileges, or cutover.

## Review gates

- Design and task-split review (2026-10-12, Lead): the integrated proposal, six-table/four-store ledger, one-route contract, field allowlist, AC1–AC5 and serialized P1–P5 were reviewed. Bounded observation is useful evidence, not a full census or migration-ready manifest. Public/security/data boundaries are fixed; exact files and tests are assigned at the post-approval readiness gate. Implementation/test execution remains Luna-owned; Lead retains risk and final operational acceptance.
- Concern and agreement review (2026-10-12, Lead): C1–C5 are resolved by explicit observation/coverage/no-write/cursor boundaries. C6 remains parent-owned and excluded only from this child; parent recovery criteria are unchanged. No child Open decision remains. User disposition awaits explicit approval of this child and its summarized ACs/concern dispositions.
- Checkpoint review (2026-10-12, Lead): reviewed the four attributable proposal files against source-grounded parent inventory and the counterexample that six row-readable tables cannot prove all 47 source mappings or external storage complete. Corrected initial ambiguous Keep/candidate output, deployment boundary, supported-surface freeze and table count. Source/test/runtime files unchanged. One erroneous validator path was corrected and the original gate rerun successfully.
- Pre-implementation and final implementation review: pending approval, source changes, executable tests, deployment and live acceptance; no implementation completion claimed.

## Verification record

Luna handoff reports the canonical change-record validator exit0/issues0 and diff check exit0. Lead independently reruns the proposal/audit gates before the document checkpoint. Application tests do not apply to this documentation-only patch; deployed diagnostic child regression evidence is unchanged and is not evidence for this unimplemented feature.

Proposal-only checks: run `python .codex/skills/document-driven-development/scripts/validate_change_records.py docs/changes/20261012_horse-identity-migration-preflight docs/changes/20261011_recurring-horse-identity-conflict` and `git diff --check`. No application build or test is claimed for documentation-only work.

Future implementation evidence must include:

- Pure policy counterexamples for same-name/different-source, missing or ambiguous origin, unknown/noncanonical IDs, malformed/unknown event schema, and matching IDs without provenance; no candidate ID is exposed.
- Actual SQLite readers for all four configured surfaces: unknown physical objects/schema, the exact six-table row allowlist, schema-only treatment of other known tables, unknown/malformed Horse events, bounded page edges, duplicate/missing continuation, concurrent change between pages, oversized/malformed payload, and no-write before/after values/schema/controls.
- API/client transport proof for exact route and wrapper, API-key-only authorization, cursor tamper/expiry/restart, 429 no-wait, timeout/cancellation, 503 partial/unavailable behavior, and absence of raw JSON, source URL, paths, exception text, or secrets.
- Admin component/browser proof for loading, empty, incomplete, unknown, error/stale retention, explicit next-page behavior, no apply CTA, desktop and 390px reflow, keyboard/focus, and restart after cursor loss.
- Relevant API, CollectionOperations, API-client, and whole-solution regressions. After approval, normal CI/CD and a bounded read-only production acceptance request are required; no production read has yet been made. Full target migration, coherent backups/generation, external queue/file inventory, apply/cutover, resume/reset, and recovery require separate parent evidence and authorization.

## Deviations and follow-up

No implementation or target-data claim is made. The next action is Lead review of this Proposed child and its parent dependencies. If approved, implementation scope and source tests must be frozen before source edits. Any unknown schema, unsupported source, unavailable configured store, or unrecoverable provenance remains visible and blocks claiming full migration readiness.
