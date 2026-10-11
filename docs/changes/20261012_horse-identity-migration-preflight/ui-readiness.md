# UI readiness — bounded identity migration preflight

Status: planning-only. This note is the P4R source-informed readiness gate for AC5. It does not implement the page, client method, endpoint, or tests, and it does not authorize a migration, apply, resume, reset, job retry, production read, or browser run.

## Purpose and frozen UI contract

The authenticated administrator gets a dedicated read-only page linked from Settings. The page starts one bounded GET and advances with one explicit continuation GET at a time. It keeps only the current page (at most 128 evidence items) in browser component state; it must not concatenate all pages or expose a browser-held API key. A successful page replaces the current page. A failed continuation keeps the previous page visible and labels it stale. A bad, expired, or restart-invalid cursor discards continuation and requires a fresh scan.

The primary action is exactly one of `Start new inspection` (before the first request) or `Read next page` (when `NextCursor` is present). A fresh-scan action is secondary. There is no Apply, Resume, Reset, job-retry, polling/background journal, arbitrary SQL/table/path/ID input, migration-ready/all-target-complete claim, or raw event/metadata/URL/path/exception output. Wire evidence is limited to the committed P1 DTO: canonical HorseId, current/initial names, optional birth dates, canonical JRA identity/status, bounded RaceId/HorseNumber references, counts, coverage/truncation, and observation timestamps.

## Repository findings

- `src/HorseRacingPrediction.Api/Web/Components/Pages/HorseIdentityDiagnostics.razor` and `.razor.css` are the closest dedicated read-only page. It uses `@page`, `[Authorize]`, `@inject AdminApiClient Api`, `RaceOpsPageHeader`, `UiState`, `RaceOpsAlert`, `FluentDataGrid`, a desktop grid plus narrow-screen card rendering, accessible headings/labels, status tones, and preserves the prior result while a refresh is pending or fails. Reuse this structure and wording style; do not reuse its single-horse input or candidate-ID display.
- `src/HorseRacingPrediction.Api/Web/Components/Pages/Settings.razor` is `@page "/settings"`, `[Authorize]`, and already links the read-only horse diagnostic at line 14. Add the preflight link beside that link; do not put the scan controls into the large settings form. `Layout/NavMenu.razor` already exposes `/settings` under 運用, and `Routes.razor` uses `AuthorizeRouteView`/`RedirectToLogin`.
- Fluent UI is registered by `builder.Services.AddFluentUIComponents()` in `src/HorseRacingPrediction.Api/Program.cs`; the API project uses `Microsoft.FluentUI.AspNetCore.Components` 4.14.4 and the existing `_Imports.razor` imports it. Use `FluentButton`, `FluentDataGrid`, `FluentCard`/`UiState`, `FluentProgressRing` through existing wrappers, and Fluent appearance/tokens; no bespoke control or visual restyling.
- `AdminApiClient` is registered with `AddHttpClient<AdminApiClient>()`. Its constructor injects the configured server-side `ApiKeyOptions` header; the browser never receives that key. Existing partial `AdminApiClient.IdentityDiagnostics.cs` calls the private `GetJsonAsync` helper. P3 may add `AdminApiClient.IdentityMigrationPreflight.cs` with a proposed `GetIdentityMigrationPreflightAsync(string? cursor, CancellationToken)` method that escapes the one opaque cursor and returns `IdentityMigrationPreflightPage`; P3/Lead must settle the exact wrapper/error behavior before P4 source edits.
- API authentication is split correctly: UI pages use the cookie `[Authorize]`/`/login` flow in `AdminAuthenticationExtensions`, while `/api/v2/admin` is protected by the existing `ApiKeyEndpointFilter` on `adminReadGroup`. The UI must call only `AdminApiClient`; it must not add a second auth model or direct `HttpClient`/API-key handling in the component.
- P1 committed `src/HorseRacingPrediction.Contracts/Identity/IdentityMigrationPreflightPage.cs` supplies `IdentityMigrationPreflightPage`, `IdentityMigrationPreflightSurfaceObservation`, `IdentityMigrationPreflightHorseEvidence`, `IdentityMigrationPreflightRaceReference`, counts, page state, phase, surface, and explicit statuses (`Unsupported`, `UnknownShape`, `Truncated`, `Unavailable`, `NotConfigured`, `NotInspected`, `Unverifiable`, `Conflict`, etc.). Repeated source/phase observations must remain visible; do not collapse evidence by HorseId.
- The API test project is MSTest/net10.0 with bUnit 2.11.3, Fluent UI test registration through `context.Services.AddFluentUIComponents()`, and the existing `TestApplicationFactory` + `AdminApiClient(HttpClient, AdminApiBaseAddressResolver, Options.Create(ApiKeyOptions))` helper pattern in `HorseIdentityDiagnosticComponentTests`. Existing API browser tests use Playwright and a local process/login harness; this readiness pass does not execute that harness.

## Exact later implementation write scope

P4 owns only these files after P3 is connected and Lead re-confirms the source gate:

1. `src/HorseRacingPrediction.Api/Web/Components/Pages/IdentityMigrationPreflight.razor` — dedicated route (proposed `/settings/identity-migration-preflight`), `[Authorize]`, state machine, one-request-per-action behavior, warning/coverage/count/evidence rendering, and no-op/disabled duplicate-click handling.
2. `src/HorseRacingPrediction.Api/Web/Components/Pages/IdentityMigrationPreflight.razor.css` — layout/reflow only, following the diagnostic page’s grid-to-card pattern and avoiding Fluent visual overrides.
3. `src/HorseRacingPrediction.Api/Web/Components/Pages/Settings.razor` — one read-only link to the dedicated page beside the existing identity diagnostic link; no scan logic.
4. `src/HorseRacingPrediction.Api/Web/ApiBrowsing/AdminApiClient.IdentityMigrationPreflight.cs` — P3-owned transport dependency; P4 must consume the final typed method and must not change endpoint/client contract independently.
5. `tests/HorseRacingPrediction.Api.Tests/IdentityMigrationPreflightComponentTests.cs` — bUnit behavior tests and an in-memory HTTP handler for typed page/error responses.
6. `tests/HorseRacingPrediction.Api.Tests/IdentityMigrationPreflightBrowserTests.cs` — Playwright desktop/390px, login/navigation, keyboard/focus, continuation, stale/error, and expired/restart checks, if Lead confirms this existing API-test browser harness as the integration location.

P4 must not edit contracts, endpoint mapping, backend readers, schema, migrations, authentication middleware, settings navigation outside the single link, or unrelated pages/styles. The P3 client file is listed for contract reconciliation only; P3 owns its implementation.

## Ordered implementation steps

1. P3/Lead freezes the typed client method, response wrapper (if any), error/status mapping, and exact API route. Confirm the page can distinguish 400 cursor expiry/restart from 429 busy and 503 unavailable without exposing problem details.
2. Add the dedicated authorized page and route. Render the initial non-scanning state with the non-atomic/read-only warning and one accent Start action; do not request on component initialization or on navigation alone.
3. Implement the bounded state machine: initial, loading-first, page-loaded, loading-next with current evidence retained, complete/no-next-cursor, partial/unknown/truncated, busy/unavailable/error with stale evidence, and expired/restarted-cursor requiring fresh scan. Every request is cancellation-aware, guarded by one busy flag, and issues at most one GET per click.
4. Render surface coverage and observed timestamps, scan accounting, and the current evidence page. Show explicit status text for unknown/not-inspected/not-configured/unavailable/unsupported/truncated/unverifiable/conflict; do not render an absent value as success or zero. Keep separate phases for repeated HorseId observations and show only allowlisted fields.
5. Use `FluentDataGrid` for desktop evidence with priority columns (HorseId/name, source phase/status, JRA status, bounded references) and a responsive card view at the existing 760px pattern. Preserve heading hierarchy, visible focus, button names, `aria-label`s, and no page-level horizontal overflow at 390px.
6. Add the Settings link and CSS. Verify the page remains a standalone inspection page rather than a dialog or a write-oriented Settings section.
7. Add bUnit tests, then browser tests after the real P3 route/client is connected. Keep prior evidence only for the current page and mark it stale after a failed continuation; assert that page 2 replaces page 1 after a successful continuation.

## Required UI test scenarios and exact files

All component scenarios belong in `IdentityMigrationPreflightComponentTests.cs` and should use the real component/client registration pattern, not a mocked component implementation:

- initial page renders the read-only/non-atomic explanation and exactly one enabled Start primary action; no request occurs until clicked;
- Start sends `GET /api/v2/admin/identity-migration-preflight` without a body or user input, renders one bounded page, surface statuses/timestamps, counts, repeated phase evidence, and a single Read next primary action when `NextCursor` exists;
- loading first/next page disables duplicate clicks; continuation leaves the current evidence visible with a polite loading status;
- successful next page sends exactly one escaped opaque cursor query, replaces page 1 rather than accumulating it, and renders `State=Complete`/no next action without claiming migration readiness or all-target completeness;
- empty current page explains that no evidence was observed in this bounded page and retains explicit coverage/status rows;
- `Unsupported`, `UnknownShape`, `Truncated`, `Unverifiable`, `Conflict`, `Unavailable`, `NotConfigured`, and `NotInspected` remain visible as labels/statuses and are not converted to success/zero;
- 429 busy and 503 unavailable show safe recovery guidance; a failed continuation preserves the old page and visibly labels it stale; no raw status body, URL, path, SQL, event JSON, or exception appears;
- 400 expired/restarted cursor clears the continuation and shows a fresh-scan action; no Resume/Reset/apply/job-retry control is rendered;
- allowlisted evidence fields are rendered (names, birth, canonical JRA identity/status, bounded RaceId/HorseNumber) while no calculated candidate ID/raw identifier is displayed;
- the Settings link points to the dedicated page and the component has an `h1`; keyboard activation/focusable Fluent buttons and controls have accessible names;
- narrow layout renders cards, stacks actions, preserves primary action/status/evidence, and does not require horizontal page scrolling;
- no timer, periodic refresh, background request, or browser-side API key is introduced (assert request count remains unchanged while idle and inspect component markup/state for forbidden controls).

The browser file, if retained by Lead, should cover the real local authenticated route at 1280px and 390px, keyboard navigation/focus, one continuation, transient failure with stale prior evidence, and cursor expiry/restart. It must use the existing sanitized local test harness and must not be treated as production or application-test evidence during this planning phase.

## Verification commands

Planning-only checks completed for this readiness artifact:

```powershell
python scripts/audit_agent_execution.py docs/changes/20261012_horse-identity-migration-preflight
python .codex/skills/document-driven-development/scripts/validate_change_records.py docs/changes/20261012_horse-identity-migration-preflight
git diff --check
```

Observed result: all three exited 0. No application/component/browser test was run or claimed.

After P4 implementation, the smallest focused component command is:

```powershell
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~IdentityMigrationPreflightComponentTests"
```

The smallest browser command (after the browser harness is confirmed) is:

```powershell
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~IdentityMigrationPreflightBrowserTests"
```

The required broader UI/API regression is:

```powershell
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~HorseIdentityDiagnostic"
```

Before integrated handoff, Lead must also run the approved workflow gates (restore, format verify, Release build, EF pending-model check, isolated database update, and full non-External solution tests) and the P3 focused endpoint/client tests. Those gates are not satisfied by this planning note.

## Remaining material unknowns and handoff

- P3/Lead must decide the final `AdminApiClient` return shape and how `GetJsonAsync`-style `HttpRequestException.StatusCode` maps 400/429/503 while retaining the typed 200 wrapper. The UI must not invent a second transport contract.
- P3/Lead must confirm whether the approved browser scenario belongs in `IdentityMigrationPreflightBrowserTests.cs` under API.Tests or an existing browser suite; existing API browser tests prove the harness can authenticate locally but no browser run is evidence here.
- The proposed UI route `/settings/identity-migration-preflight`, Japanese action labels, and whether a transient 429/503 gets a secondary `Read current page again` affordance are implementation details to freeze at the P4 pre-implementation review. A cursor-expiry 400 always requires a fresh scan and never promises resume.
- Backend P2/P3 may return a complete state with explicit unsupported/external/not-inspected coverage. UI completion must display those gaps and must never infer a coherent manifest or target-wide completeness.

Handoff to Lead: exact UI page/client/link/CSS/test files are listed above; P4 remains `Dependent` on P3 and this P4R note. Requested route was `gpt-5.6-luna/high` because the current spawn list did not expose `gpt-6-luna`; observed model and usage telemetry are unavailable and intentionally not inferred. Material risks are raw-data disclosure, accidental write/poll behavior, page-memory growth, false complete/manifest language, stale-evidence loss, and cursor-expiry mislabeling; the ordered steps and negative component scenarios are the validators. No retry or promotion was needed for this read-only planning slice.
