# 移行エラーをサイト停止へ波及させず画面で確認する

- Status: Proposed
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-25
- Updated: 2026-10-25

## CURRENT

T1 source investigation verified; T2 concrete design below awaits explicit approval. No production edit/deploy/data operation. Reuse current supervisor/minimalhost; no server redundancy/service addition. Keep authenticated status/error page during migration and contained migration failures; preserve DB isolation and API error semantics. Existing journal reason/results/phase/time suffice, no schema migration. Parent20261024 recovery remains unfinished;24Oct StoppingApi/blocked503 is historical, not freshly rechecked. Next after approval: Luna lifecycle T3, serialized UI T4, exact native/process/browser verification, canonical deployment and parent operational recovery. Unrelated dirtyAGENTS/skills/routing/efficiency untouched.

## Context and hypotheses

Parent: [production failure recovery](../20261024_collection-failure-recovery/README.md). Related unapproved [pre-apply cancellation proposal](../20261019_pre-apply-cancel/README.md); that proposal is not implicitly approved by this request. Current known source catches supervisor failures into process exit and provides a minimal blocked host with limited status routes. T1 must establish exact error loss, restart/HTTP availability, and UI paths before freezing design. The actual24Oct handoff failure remains unknown; do not turn an untested exit-code/gate theory into an acceptance premise.

H1 verified in source: supervisor returns5 on apply/verify false and unexpected child exit, and outer catch returns5; container restart is currently relied on. Existing blocked host is journal-only and baseline tests prove no business-store creation. Reuse it as a read-only operator child during offline CLI and error containment; do not run normal business API concurrently. Source evidence: Supervisor.cs:98-155, RuntimeMode.cs:253+, RuntimeModeTests named blocked-host/source-isolation cases. Actual24Oct cause is still unknown; source mechanism is not a production root-cause claim.
H2 verified in source: journal already stores reason/updated_utc/results_json; public DTO exposes reason/results/phase/time. Minimal HTML drops most fields and preparation failure may leave AwaitingActions/None. Add narrow transactional failure recording and append-only typed public-safe error codes, preserving existing results and Succeeded/Partial evidence. No new SQLite column/table/index or arbitrary exception string. Existing bounded failure reserve retained; corrupt journal is explicitly unavailable, not repaired by fabricated evidence.

## Decisions and boundaries

1. Reuse one existing LinuxPID1 supervisor and existing journal-only operator host. After normal API fully exits, keep this minimal host serving HTTP while backup/census/CLI/verification runs. It must not acquire business-source runtime leases or map business writes. While CLI/verification is active, do not expose existing verification mutation actions. After the CLI and operator child exit/reap safely, normal API starts only on existing verified/cleared-gate conditions. Brief process/port handoff may reconnect; no promise of uninterrupted sockets or server-outage HA.
2. Contain migration-controlled exceptions, failed return values and child exits rather than exiting PID1 and relying on container restart. Stop/reap owned writers before keeping/starting the operator host. SIGTERM/container shutdown still means shutdown, not automatic restart. Child startup crash loops are bounded/backed off; no hidden apply replay or normal-API restart over uncertain data.
3. Record bounded categorized failure in the existing operation reason/results and phase/time fields, under the journal transaction and ownership guard. Do not replace earlier results, promote Partial to success, downgrade verified data to failed data on later host error, erase pending intent or infer an unobserved phase. Display a safe category (preparation/backup/inspection/apply/verification/child failure), not raw SQL/paths/stack traces/key. Existing old status/results remain readable; new enum values append without renumbering existing values.
4. Both normal migration page and minimal page show the same safe status, failed/last-confirmed stage, safe reason, occurrence/last-confirmed time and operation reference. Clearly distinguish unknown cause and status-unavailable from success/pending. No request executes on view/reload; refresh is manual.
5. Authenticated ordinary HTML navigation (including root/races/jobs/settings) reaches a clear maintenance/error view instead of raw503 when the business API is isolated. Resolve active operation first, otherwise read one latest journal operation with a bounded internal query (no new listing endpoint). No journal/operation means a clear unavailable/empty view. API callers retain existing401/404/409/503 semantics; no HTML200 masquerading as successful API or readiness. Login/auth and no-store/privacy boundaries remain.
6. Safe ordinary pages continue when normal mode is available. During offline migration or partial/unknown data, DB-backed race/job operations remain restricted and receive the operator view. This is application-level error isolation, not continuous full business functionality or infrastructure redundancy. No new cancel/abort/reset/automatic retry/rollback is included;19Oct cancel proposal remains Proposed.

## Finite public-contract ledger

| Existing method/path | Disposition | Invariant/consumer |
| --- | --- | --- |
| POST /api/v2/admin/horse-id-migration-previews | Unchanged | Bounded target/full review; admin client unchanged |
| GET /api/v2/admin/horse-id-migration-schema-diagnostics | Unchanged | No raw rows/path/SQL; existing client |
| POST /api/v2/admin/horse-id-migration-operations | Unchanged | PendingV2/BoundV1 and max16/idempotency intact |
| POST /api/v2/admin/horse-id-migration-operations/{operationId}/execute | Existing lifecycle extended, payload unchanged | Same persisted intent; failure containment is not replay permission |
| GET /api/v2/admin/horse-id-migration-operations/{operationId} | Extend safe reason/result enum meanings, same DTO shape | API-key auth; shared normal/minimal/client representation |
| GET /api/v2/admin/horse-id-migration-requests/{requestKey} | Same safe enum extension, unchanged replay | Same immutable request binding and privacy |
| POST /api/v2/admin/horse-id-migration-operations/{operationId}/verify | Unchanged eligibility, unavailable during active writer/control phase | Existing cookie/CSRF and succeeded-data-only verification; no preapply escape hatch |
| GET /settings/horse-id-migration | Extend minimal and normal error UI; allow authenticated landing without query | Single active/latest operation, safe empty/unavailable states; manual refresh only |
| POST /settings/horse-id-migration/{operationId:guid}/verify | Unchanged eligibility/action semantics; hidden/disabled during writer | Existing cookie/CSRF; no duplicate verification |
| GET /health | Unchanged liveness | Not proof of ordinary business readiness; deployment Swagger readiness unchanged |
| Existing minimal-host fallback /{**path} | Split HTML navigation from API/non-HTML requests | Authenticated GET HTML gets operator view; /api and non-GET/non-HTML retain proper unavailable status; tested root/races/jobs/settings |

11 discovered surfaces = unchanged6 + existing lifecycle/enum/UI extensions4 + fallback replacement1; new public routes0/deletions0. Login endpoints and ordinary application routes are reused, not modified contracts. T3/T4 must reconcile actual outbound client reads with this ledger and leave other contracts unchanged.

## Experience and mock

Use existing Detail Page hierarchy, no new dashboard or server. Primary action is「状態を更新」. Japanese summary precedes technical reference; normal Blazor page retains Fluent components. Minimal host intentionally uses semantic server-rendered HTML without bootstrapping normal Blazor/DB services; existing pattern reused, not a replacement Fluent framework. Auth redirects preserve only validated local return URL. Desktop and390px same information, long IDs wrap, keyboard focus/heading/state text explicit. [Wireframe](mocks/error-status.md) covers pending/running/failure/unknown/journal-unavailable, retained on refresh.

## Concern and agreement ledger

| ID | Concern/evidence | Impact | Proposed disposition and alternative | AC/task/counterexample | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Normal API cannot safely access DBs during offline CLI/partial state; existing leases enforce this | Serving every ordinary function would violate isolation | Keep operator/error view reachable, restrict unsafe business routes. Alternative concurrent normal API rejected, not server redundancy | AC1/AC3,T3/T5; partial writer still alive | Support minimal host reuse, no gate bypass | Explicit scope summary awaiting approval | Resolved in design |
| C2 | Current preparation can remain AwaitingActions/None; raw exceptions may contain secrets | Misleading status or leaked sensitive data | Transactional existing-journal bounded safe codes; preserve Succeeded/Partial and existing results; unknown evidence labelled unknown. Alternative raw log/exception display rejected | AC2/AC4,T3/T4; corrupt journal, later startup error | Support typed diagnostics without schema migration | Awaiting approval | Resolved in design |
| C3 | Root/latest query has no timestamp index and journal capped64MiB; current phase actual cause unknown | Repeated unbounded work or false cause claim | Active-index lookup then single bounded latest query after auth only, manual refresh/no polling; status unavailable on deadline. No new index/schema. Test boundaries; actual24Octcause remains parent T7 | AC1/AC2/AC4,T3/T5; empty/corrupt journal, repeated requests | Support bounded existing storage | Awaiting approval | Resolved in design |
| C4 | Restart/child failure and post-success verification share process control | Duplicate apply, writer coexistence, early gate clearance | Keep one controller; operator child read-only while active; reap before normal host; no automatic replay/cancel/reset, no stronger infrastructure availability promise | AC1/AC3,T3/T5; duplicate/reconnect/SIGTERM/process exit | Support realPID1 fault proof, not mock-only acceptance | Awaiting approval | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Migration-controlled exceptions and child failures do not leave the site connectionless or raw503-only; authenticated operator/error page remains reachable without added HA infrastructure | T1,T2,T3,T5 | Real LinuxPID1/process failure through HTTP, normal unaffected path | Not started |
| AC2 | Screen shows migration status, failed stage, safe reason, timestamp and operation reference, survives reload/restart; unknown evidence is explicitly unknown | T1,T2,T3,T4,T5 | Producer→journal→DTO→normal/minimal UI, injection/restart/browser counterexamples | Not started |
| AC3 | Partial/unknown data never enables unsafe ordinary writes, clears ownership gates, or starts duplicate apply; no reset or new automatic retry | T1,T2,T3,T5 | Collision/unknown/apply failure/process ownership/idempotency regressions | Not started |
| AC4 | Authenticated desktop/narrow error page is discoverable from ordinary URLs, keyboard usable, no API key/path/stack trace leakage; API errors retain proper HTTP semantics | T1,T2,T4,T5 | Auth/CSRF/XSS/HTML-vs-API endpoint tests, bUnit and real browser390px/reload | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Trace current lifecycle/error/UI and existing test oracles, bounded cheap plan | pending_planner | Luna/high | none | Read-only migration runtime, journal, public contracts, operator components/tests and docs | CodeGraph first; exact current source paths and executable test commands; no prod calls/code edits | Source report independently challenged by exact Supervisor/RuntimeMode/normal UI path reads | Verified | Worker — bounded repository investigation | T1-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | Freeze smallest concrete design and concern dispositions, obtain approval | Lead | Lead | T1 | This record, mock and relevant canonical docs | Hypothesis/contract/AC review; no material unresolved design premise | Concrete proposal/ledger/mocks ready; explicit approval pending | Proposed | Lead — architecture/public contract/security/approval | none | unavailable; retries 0; corrections 0; reviews 0 |
| T3 | Implement lifecycle/error-persistence containment after approval | Planned Luna lifecycle executor | Luna/high | T2 approval | Api/IdentityMigration runtime, journal, mapper/minimal HTML and corresponding Identity policy/result enum, Contracts/Identity operation contracts only if required; corresponding API migration tests | Real failure red/green, focused then related native regression/build/format | No code edit before approval; production logic source freeze precedes T4 | Dependent | Worker — exclusive frozen lifecycle slice | none | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | Implement normal operator error UI and integrated browser proof after contract freeze | Planned Luna UI executor | Luna/high | T2 approval,T3 contract freeze | HorseIdentityMigrationAdmin.razor, existing AdminApiClient.HorseIdentityMigration only if necessary, relevant component/browser tests; minimal renderer changes return to T3 owner | bUnit/HTTP/browser desktop390px auth/reload/error/long strings | No overlap with runtime owner; no new public body fields planned | Dependent | Worker — serialized UI, no overlapping lifecycle owner | none | unavailable; retries 0; corrections 0; reviews 0 |
| T5 | Integrate, deploy approved change and verify actual path | Lead | Lead | T3,T4 | Reviewed integration/docs and separately approved canonical deployment/production checks | Exact CI parity, real HTTP/process proof and live normal/operator availability; parent recovery kept owned | Not started | Dependent | Lead — operational authority/integration/final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |

## Documentation updates

Planning-only: docs/20-admin-ui-design.md links this proposed lifecycle/error-view contract and distinguishes it from the existing connection-loss design. Parent20261024 links this proposal without excluding restoration/41migration acceptance.19Oct cancellation remains unapproved, no silent replacement. No production behavior changed. Orchestration/DDD/incident skills read this turn; UI design/page-pattern/testing skills guide operator detail/error hierarchy, bounded human-facing messages and component/browser proof. No grid/form data-entry change.

## Review gates and verification plan

Lead design/task-split and concern review:11surface ledger, C1–C4, no additional server/DB schema, exclusive runtime/UI ownership and serialized contracts. No unresolved factual premise is used as the actual24Oct root cause. Implementation readiness is not released while Proposed. Ordinary implementation/tests to selectable Luna/high; Lead does architecture/security and accepts actual evidence. No model/token telemetry inferred.

After approval, test responsibility T3: new counterexamples in HorseIdentityMigrationSupervisorTests, HorseIdentityMigrationRuntimeModeTests and journal/endpoint tests; original controlled-failure path must fail oldsource and pass fix, minimal-child no-business-lease/no-write, preapply/backup/census/apply/postcheck/startup failures, cleanup/SIGTERM/duplicate/restart/corruptjournal. Focused Release command: `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --filter "FullyQualifiedName~HorseIdentityMigrationSupervisorTests|FullyQualifiedName~HorseIdentityMigrationRuntimeModeTests|FullyQualifiedName~HorseIdentityMigrationEndpointsTests|FullyQualifiedName~HorseIdentityMigrationJournalTests"`. Use trueLinuxPID1 INIT runner retained in parent for process invariants, not Windows-only mocks. T4 adds normal component states plus authenticated HTTP and actual browser root/races/status/reload/390px/long-message keyboard checks; malformed data XSS/cause text never executes. Full relevant native Release regression/build and exact workflow formatter `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` after finalcode; graph sync/requery changed callers, diff/status/secret/scope checks before commit. No such tests have run at planning time.

Final acceptance requires normal healthy behavior unchanged, contained fault produces safe error view, unknown data remains gated, and deployment/public route proof. This child does not itself approve new41data mappings or unsafe resumption; original operational approval/safety gates stay parent-owned. Any new material safety/publiccontract change returns to Proposed; ordinary local failures remain worker corrections.

Planning checkpoint25Oct: audit validator exit0 (initial numeric result-metric spacing corrected), skill-local record validator issues0, git diff --check0. Read-only T1 accepted, T2 awaits explicit summary approval; no implementation/native/browser test or fresh production observation claimed. Proposed scope is not completion of parent incident. Next after approval: dispatch planned Luna lifecycle executor with frozen contract/owned tests, then UI executor and actual process/HTTP/browser gates; no agent or production operation continues while awaiting this decision. UI skills influenced the detail-page hierarchy, human-safe error wording, manual action and390px/keyboard tests, not additional dashboard/design-system work.

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | Proposed, no write gate released |
| Verification | Not started | Bounded source investigation |
| Deployment/operation | Not started | No live action; parent incident still unfinished |
