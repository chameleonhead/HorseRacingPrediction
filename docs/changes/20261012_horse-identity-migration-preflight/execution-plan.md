# M5 preflight execution plan

This is a Proposed child plan. No implementation, target read, deployment, migration, resume, or reset has been performed. After separate approval, P5 includes the ordinary CI/CD gate and a bounded read-only production acceptance request; data-changing operations remain excluded.

## Repository-informed findings

- M2’s [persistence inventory](../20261011_recurring-horse-identity-conflict/persistence-inventory.md) reconciles 47 source-defined application-table mappings across EventStore, CollectionPlatform, PredictionSchedule, and optional LocalCollectionQueue. It explicitly does not establish the mounted production schema, rows, or external queue/file contents.
- The current single-horse GET is /api/v2/admin/identity-diagnostics/horses/{horseId}. CodeGraph shows the endpoint is a GET on the v2 admin group, adds Cache-Control no-store, maps bounded validation/admission/store errors, and returns one typed diagnostic wrapper. Its service reads EventStore and CollectionPlatform separately through dedicated read-only/query-only connections. This is a safety precedent, not a whole-dataset enumeration API.
- AdminApiClient adds the API key in a server-side HTTP header. Existing Settings pages are authorized. Preserve the authenticated UI → server-side client → API-key route; do not add a browser-held API key or infer a new role.
- Store constructors can invoke schema migrators. A future preflight must not construct a migration-owning store; it should use dedicated read-only connections derived from configured server options and reject writes.
- HorseRegistered contains names/profile values but no source URL, producer marker, or identity-generation input. Missing source remains Unverifiable. M2’s old-to-canonical candidates do not become valid because names match.
- Existing admin migration-command design separates the eventual migration runner/journal/cutover from UI lifetime. Those remain parent decisions and are not part of this read-only page scan.

## Proposed bounded acquisition contract

1. Start with one GET and advance with the same GET plus one opaque cursor query. No body, user-supplied path, SQL, table name, or horse-ID scan instruction.
2. Cursor is server-protected, at most 4 KiB, and expires within 30 minutes. It binds a policy version, server-selected configured surfaces/schema assumptions, and keyset position. It has no durable report/journal. Invalid/expired/restart tokens require a fresh scan; the UI does not promise resume.
3. Inspect physical schema using allowlisted source definitions; return recognized surface codes and coverage/count status rather than raw SQL/path/connection data. Unknown tables/columns/versions are Unknown/Unsupported, never silently omitted.
4. Read rows only from the fixed six-table allowlist: EventStore `Horses`, `EventEntity` rows for Horse aggregates (parse the supported registration event; count unknown/malformed event types or shapes as `UnknownShape`), `JraSubjectProfileReadModel`, `HorseRaceHistories`, and `HorseWeightHistories`; CollectionPlatform `collection_resources` (exact horse resource type/provider only). Use batches of at most 128 root identities and batched typed references. Do not call the single-horse endpoint N times or load the database into memory. Other known application tables are SchemaOnly/NotInspected, not row-counted. PredictionSchedule, optional LocalCollectionQueue, migration metadata, sidecars, external queues/files/model storage are also NotInspected for rows.
5. Use only approved wire evidence: canonical HorseId, current/initial names, optional birth date, validated canonical JRA identity/status, bounded related RaceId/HorseNumber, coverage/truncation, counters, and per-store observation time where available. Do not return calculated candidate IDs, raw URL/event/metadata, path, actor/caller log, raw malformed identifiers, or exception text.
6. One global immediate admission slot, no wait queue; busy returns 429. Per-request upper bound 30 seconds, cancellation, query-only connection and deferred read transaction. No polling loop. Payload-size, JSON-depth, response-size, and driver timeout limits are server-owned and no looser than the existing single-horse diagnostic limits.
7. Each response and the page retains the non-atomic warning. Pages may span separate read times; a scan count reconciliation is not a stable snapshot and cannot create a fixed apply manifest. Coverage of unsupported/external/not-configured sources is explicit.

## Finite route and response ledger

| Method/path | Inputs | Typed output | Status behavior | Notes |
| --- | --- | --- | --- | --- |
| GET /api/v2/admin/identity-migration-preflight | Optional single protected cursor query; no body | One bounded IdentityMigrationPreflightPage wrapper with phase/source-surface status, counts, allowlisted evidence items, observation timestamp, coverage/truncation state, and next cursor or end marker | 200 page, including typed incomplete/unknown; 400 invalid/expired cursor; 401 unauthenticated; 429 no-wait admission busy; 503 store unavailable/deadline | Exact proposed route under the existing `/api` prefix and API-key-protected `/v2/admin` group. No apply/status route in this child. |

The UI page is dedicated and authorized, linked from Settings. It starts a scan, shows an explicit next-page action, and re-runs after a cursor is lost/expired. It does not poll and exposes no apply/resume/retry/reset operation.

## Files proposed for a later approved implementation

The exact source scope is not yet authorized. Candidate files only:

- A dedicated typed preflight contract under src/HorseRacingPrediction.Contracts/Identity/.
- A dedicated API read service and GET endpoint under src/HorseRacingPrediction.Api/Endpoints/Identity/, with route registration through existing API extension wiring.
- Store-specific read-only page readers using the established diagnostic connection pattern, placed in their owning API/CollectionOperations modules only after schema/query design is frozen.
- A focused server-side AdminApiClient partial and a dedicated Blazor page linked from Settings.
- Pure policy, API/reader, client transport, and component tests in existing API/CollectionOperations/API-client/API test projects.

Do not reuse CollectionPlatformStore constructors for a diagnostic because they run the schema migrator. Do not modify the existing Horse identity resolver, domain event, schema, queue, predictor, dispatcher, or runtime maintenance behavior for this preflight proposal.

## Ordered tasks and verification

The M5-A1 planner/audit provenance is recorded in the parent change record; the child task table below tracks only implementation work.

| ID | Task | Owner | Model tier | Depends on | Scope | Verification | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| P1 | Implement the fixed surface/schema ledger, row allowlist, bounded page policy, observation-state vocabulary, and cursor validation from this approved proposal. | Planned Luna executor; Lead accepts | Luna | Separate child approval | Contracts and pure policy files frozen by Lead | Fixed surface/row allowlist, bounds, observation-only outcomes, cursor tamper/expiry tests | Dependent |
| P2 | Implement dedicated batched read-only readers for the six allowlisted tables and schema-only inspection for the four configured surfaces. | Planned Luna executor; Lead accepts | Luna | P1 | Exact API/CollectionOperations files frozen after P1 | No-write/read-only proofs, bounded page/query tests, unknown schema/event shapes, cancellation/busy/timeout, reference closure | Dependent |
| P3 | Add the typed GET route and server-side AdminApiClient operation under the existing API-key flow. | Planned Luna executor; Lead accepts | Luna | P1–P2 | Exact endpoint/wiring/client files frozen after P1 | Real HTTP wrapper/status/auth behavior, invalid cursor, 429/503, response redaction | Dependent |
| P4 | Add a dedicated authorized page and Settings link, with responsive coverage/stale/error states and no apply affordance. | Planned Luna executor; Lead accepts | Luna | P1–P3 | Exact Settings/page/component-test files frozen after P1 | Component and desktop/390px browser; loading/error/restart/keyboard/focus/no-apply | Dependent |
| P5 | Verify the child through normal CI/CD and a bounded read-only production request, then reconcile canonical design records. | Lead | Lead | P2–P4 | Approved documentation and read-only operational verification only | CI/CD terminal success, sanitized response/status evidence, child/parent validators; no migration/resume/reset | Dependent |

Expected focused tests after implementation:

- Pure classifier tests for exact supported/missing/unknown/malformed/native event shapes, name-only and same-name/different-source non-equivalence, source-backed observations without automatic eligibility, and noncanonical IDs; no calculated candidate ID is emitted.
- SQLite page-reader tests using the configured providers and read-only connections: the four-surface schema ledger, exact six-table row allowlist, schema-only known tables, no schema/data/control change, unknown physical object, unknown/malformed Horse event shape, page boundary without duplicate/skip, tampered/oversized/expired/restart cursor, between-page mutation, separate-store timestamps, malformed/oversized JSON, and batched reference counts.
- API integration tests for exact GET and wrapper, API-key authorization, 400/429/503 behavior, cancellation/timeout, and absence of raw URL/event/metadata/path/exception/secret output.
- Client transport tests for exact path/query, typed response, status preservation, and response-wrapper decoding.
- Component tests for initial/loading/page transition, repeated-click lockout, partial/unknown/truncated/stale/error/expired states, and absence of Apply/Resume/Reset controls; browser checks on desktop and 390px, keyboard/focus, page continuation, and restart.
- Relevant API, Collector/CollectionOperations, API-client, and full non-External solution regressions. After separate approval, complete the normal CI/CD gate and a bounded read-only production request. No tests or production reads have been executed for this proposed child.

## Unresolved parent decisions (not child approval blockers)

- A coherent apply generation/manifest, complete target census, external queue/file fences, collision policy, backup/restore/cutover, and rollback-after-write remain Open in the parent and Lead-owned.
- Whether source-unrecoverable identities can ever be migrated, and under what evidence, remains Open. This preflight only reports facts and gaps.
- A protected cursor can expire or become invalid after restart; restarting is the specified behavior. This does not promise resumable history.

## Next action

Lead reviews this proposal and the parent-linked concerns. If the child is approved, freeze the final surface/route/wire contract before source edits, run the documented audit/readiness gate, and dispatch serialized Luna implementation. This plan itself does not approve code, target access, deployment, or data mutation.
