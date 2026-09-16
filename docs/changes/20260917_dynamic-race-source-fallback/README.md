# Dynamic race source fallback

- Status: Proposed
- Owner: HorseRacingPrediction team
- Created: 2026-09-17
- Updated: 2026-09-17

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | Approval is required before production-code changes. |
| Verification | Not started | Handler, Navigator, lifecycle, live-route, and solution regressions remain. |
| Deployment/operation | Not started | Deployment, pipeline resume, and failed-task recovery are outside this proposal. |

## Context

Production stopped on `race-discovery` for `2026-09-12 Nakayama`. On 2026-09-17 the date was exactly
`DefaultRaceCardLookupPeriodDays` (five days) old, so discovery selected the RaceCard route. JRA had already
removed that meeting button from its RaceCard selection screen. `JraNavigationException` with
`OutOfDisplayedRange` escaped the handler, became `PermanentFailure`, and triggered the platform-wide safety
pause. The same resource had previously been treated as publication waiting while its surrounding discovery
window was still being retried.

The five-day value is a local optimization and data-enrichment preference, not a guarantee of JRA screen
retention. JRA's visible RaceCard range can end before that boundary. Conversely, race results remain
discoverable through the existing Current/Recent/Historical result routes.

## Goals

- Use RaceCard first when useful, but treat a visible `OutOfDisplayedRange` response as a route-selection
  signal rather than an unexpected system failure.
- Fall back to the existing RaceResult discovery route for past/current races when RaceCard has disappeared.
- Apply the same lifecycle rule to discovery, individual race detail, and result-list navigation so one
  adjacent entry point cannot reproduce the global pause.
- Preserve strict identity, parse, HTTP, and unexpected-page failures; do not hide genuine defects.
- Preserve operator visibility and the global safety pause if both supported routes genuinely fail.

## Non-goals

- Changing JRA's five-day configuration to another guessed fixed number.
- Making every `JraNavigationException` retryable or disabling the global safety pause.
- Treating future unpublished pages as historical results.
- Reconstructing unavailable RaceCard data from RaceResult pages.
- Depending on private JRA URLs, JSON, or JavaScript implementation details.
- Deploying, resuming the production pipeline, or retrying existing failures.

## Impact assessment

| Surface | Current behavior | Decision |
| --- | --- | --- |
| `race-discovery` | Exact five-day boundary chooses RaceCard; `OutOfDisplayedRange` escapes and pauses all collection. | Must change. Fall back to `ToRaceResultListAsync` and persist result-derived work. |
| `race-detail` | Uses the same inclusive five-day check; missing RaceCard can escape as a permanent failure before result collection. | Must change. Stop card enrichment and continue the existing result workflow on `OutOfDisplayedRange`. |
| direct RaceCard workflow | Its production use is through `race-detail` refresh; no independent active collection handler was found. | No separate change; handle the exception at the owning `race-detail` boundary. |
| `race-odds` | Uses RaceCard internally, but normal scheduling stops after race start and does not share the five-day source-selection decision. | No change in this scope; retain strict active-window behavior. |
| RaceResult navigation | Recent already falls to Historical on `OutOfDisplayedRange`, but Current does not. | Must change for past dates only; Current and Recent both use Historical fallback when the visible selection has expired. Today/future remain strict. |
| Calendar readiness | Independent of RaceCard retention. | No change; retain the approved visible-content readiness and one-Snapshot performance contract. |
| Admin failure screens | Correctly display the permanent failure and global pause caused by the unhandled exception. | No UI change; prevent the expected lifecycle exception at the handler boundary rather than hiding it. |
| Pipeline pause policy | Every terminal result except `ResourceNotFound` pauses globally. | No broad change. Expected lifecycle states must be handled by their domain handlers; structural terminal failures still pause. |

## Proposed behavior

### Discovery

For each past or current date/course, discovery may try RaceCard first while it is within the configured
preference window. If and only if this attempt raises `OutOfDisplayedRange`, it immediately calls
`ToRaceResultListAsync`. The handler records the route actually used rather than continuing to infer source
type from age. Result-route pages generate `race-detail` requests with validated result URLs and without odds
requests or RaceCard-only metadata. A future `NotYetPublished` result remains publication waiting.

The supported terminal page types remain `JraRaceListPage` and the existing single `JraRaceResultPage` case.
Identity validation for date, course, and race number is unchanged. Parse errors, HTTP failures, unknown pages,
and identity mismatches do not trigger this fallback.

### Individual race detail

Within the preference window, saved RaceCard URLs and normal RaceCard navigation are still attempted first.
When normal navigation reports `OutOfDisplayedRange`, the handler marks card enrichment unavailable for this
attempt and continues to the existing RaceResult workflow. It does not request referenced subjects or enqueue
prediction work from a missing card. Result collection, confirmation checks, and retry timing remain unchanged.

### Result navigation boundary

`ToRaceResultAsync` and `ToRaceResultListAsync` apply the existing Historical fallback when either Current or
Recent meeting selection reports `OutOfDisplayedRange` for a date before today. Today and future dates never
use historical fallback, so a rendering defect or unpublished future page is not hidden. The general platform
pause rule and admin presentation remain intact for genuine terminal failures.

## Alternatives considered

### Change five days to four days

Rejected. It fixes the observed date but replaces one assumed JRA retention period with another.

### Classify every `OutOfDisplayedRange` as transient

Rejected. A retired RaceCard will not reappear through retries, and the repository already has a supported
RaceResult route. Blind retries would waste Lambda time and may still pause after exhaustion.

### Disable global pause for all permanent failures

Rejected. The safety pause is appropriate for real schema, identity, and unsupported-route defects. The
expected lifecycle condition should be handled before it reaches that boundary.

### Always use RaceResult for past dates

Rejected. It would lose RaceCard-only fields during the period when the official card is still available and
would undo the integrated detail workflow's enrichment behavior.

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: clarifies that `RaceCardLookupPeriod` is a preference/optimization, not
  proof of availability, and links this record as the canonical correction for dynamic fallback.
- `docs/22-collector-design.md`: clarifies the Collector's source-selection and expected-retirement behavior.
- `docs/26-collection-platform-design.md`: clarifies that lifecycle outcomes are handled before the unchanged
  platform-wide terminal-failure safety pause.
- This record owns the incident evidence, affected-surface inventory, acceptance criteria, and execution plan.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | At the exact configured RaceCard age boundary, a missing meeting button with `OutOfDisplayedRange` falls back to result discovery and creates the correct race-detail requests without pausing the pipeline. | T2, T5 | Handler test plus store/pause integration | Not started |
| AC2 | A still-visible RaceCard uses the existing fast path and produces the same card URL, odds requests, attributes, priority, and lane as before. | T2, T5 | Existing and new discovery regressions | Not started |
| AC3 | Discovery fallback validates date/course/race identity and persists result URLs; it does not create odds work or pretend RaceCard metadata exists. | T2 | Route-aware handler tests | Not started |
| AC4 | A race-detail task whose card retires at execution time continues to result collection; missing card data does not enqueue subject or prediction work, and successful result collection completes normally. | T3 | Detail-handler boundary test | Not started |
| AC5 | Future `NotYetPublished`, HTTP failures, parse failures, unexpected pages, and identity mismatches retain their existing distinct behavior and do not use the retirement fallback. | T2, T3, T5 | Negative-path regressions | Not started |
| AC6 | Current and Recent result routes fall back to Historical only for past `OutOfDisplayedRange` dates; today/future, parse, HTTP, and identity failures remain strict. | T4, T5 | Navigator route matrix tests | Not started |
| AC7 | Calendar, odds, subject/profile collection, admin presentation, and the platform-wide terminal-failure safety policy remain unchanged. | T5 | Cross-surface regression and production caller inventory | Not started |
| AC8 | Calendar readiness, one semantic Snapshot, non-calendar navigation, result Current/Recent/Historical behavior, and existing performance tests do not regress. | T5 | Focused scraping/collector/API tests and efficiency gates | Not started |
| AC9 | Three bounded live checks cover visible card success and retired-card result fallback where the official site exposes suitable dates; no production state changes. | T5 | Live read-only verification | Not started |
| AC10 | Release build, all non-external tests, formatting, CodeGraph, record validation, and diff/status checks pass. | T5, T6 | CI-equivalent verification | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Audit production failure and every fixed-period/route consumer. Covers AC1-AC10 design. | Main with read-only audit worker | Lead/review tier | - | Read-only plus this record/canonical docs | Source, production UI, tests | Reviewed inventory in this record | Verified |
| T2 | Make discovery route-aware with precise `OutOfDisplayedRange` result fallback. Covers AC1-AC3, AC5. | Main | Lead tier | Approval | discovery handler/tests | Focused handler and identity tests | Pending | Dependent |
| T3 | Align race-detail fallback behavior without changing card enrichment. Covers AC4-AC5. | Main | Lead tier | T2 contract | detail handler/tests | Boundary and store tests | Pending | Dependent |
| T4 | Extend past-date Current result fallback while preserving today/future strictness. Covers AC6. | Main | Lead tier | T2 route contract | Navigator/tests | Route matrix tests | Pending | Dependent |
| T5 | Run cross-surface, pause-policy, performance, live, and non-external regressions. Covers AC1-AC10. | Main | Lead tier | T2-T4 | Read-only except ignored outputs | Recorded commands/results | Pending | Dependent |
| T6 | Synchronize CodeGraph, update records, audit scope/secrets, and commit. Covers AC10. | Main | Lead tier | T5 | docs and derived graph | Validator/diff/status | Pending | Dependent |

## Review gates

- **Design and task-split review — 2026-09-17, reviewer: Main.** Inputs: production job/failure/attempt
  screens, CodeGraph call paths, fixed-period literal inventory, handler/store code, existing Recent fallback tests,
  and the read-only `adjacent_surface_audit`. Decision: mandatory production impact is limited to discovery,
  race-detail, and past-date Current result fallback. Calendar, odds, subjects/profiles, admin UI, and global
  pause policy do not share the faulty source-selection boundary and stay unchanged. AC1-AC10 cover success,
  strict negative paths, performance, live evidence, and final gates. No unresolved design choice blocks
  approval.
- **Pre-implementation review** — blocked on explicit approval.
- **Checkpoint review** — pending implementation.
- **Final review** — pending implementation and verification.

## Verification record

- 2026-09-17: production UI showed `discovery:2026091306` failing after 39.6 seconds with
  `JraNavigationException` for `2026-09-12 Nakayama`; the terminal result was permanent and paused collection.
- 2026-09-17: source trace found inclusive five-day checks in discovery and race detail, while RaceResult
  navigation already supports Recent-to-Historical fallback on `OutOfDisplayedRange`.
- 2026-09-17: store trace confirmed that terminal outcomes other than `ResourceNotFound` pause the pipeline;
  this proposal intentionally handles expected lifecycle outcomes in domain handlers instead of weakening that
  global safety rule.
- 2026-09-17: delegated read-only audit confirmed the same inclusive boundary in `race-detail`, an existing
  Recent-only result fallback, and no shared fixed-period decision in calendar, odds scheduling, subjects, or
  profiles. Main verified the cited production callers. Rework: the initial proposal was narrowed to exclude
  odds and UI changes; usage/cost telemetry was unavailable.

## Deviations and follow-up

- No production code has changed while this record is `Proposed`.
- Deployment, pipeline resume, and recovery of current failed tasks remain separate operational actions.
