# Dynamic race source fallback

- Status: Implemented
- Owner: HorseRacingPrediction team
- Created: 2026-09-17
- Updated: 2026-09-17

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | Discovery, race-detail, past-date fallback, terminal page/identity validation, and readiness cancellation are implemented. |
| Verification | Complete | Focused regressions, repeated cancellation checks, Release, full non-external, formatting, graph, and record gates passed. |
| Deployment/operation | Out of scope | No deployment, pipeline resume, or failed-task recovery was performed. |

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
| AC1 | At the exact configured RaceCard age boundary, a missing meeting button with `OutOfDisplayedRange` falls back to result discovery and creates the correct race-detail requests without pausing the pipeline. | T2, T5 | Handler test plus completion contract | Verified |
| AC2 | A still-visible RaceCard uses the existing fast path and produces the same card URL, odds requests, attributes, priority, and lane as before. | T2, T5 | Existing and new discovery regressions | Verified |
| AC3 | Discovery fallback validates date/course/race identity and persists result URLs; it does not create odds work or pretend RaceCard metadata exists. | T2 | Route-aware handler tests | Verified |
| AC4 | A race-detail task whose card retires at execution time continues to result collection; missing card data does not enqueue subject or prediction work, and successful result collection completes normally. | T3 | Detail-handler boundary test | Verified |
| AC5 | Future `NotYetPublished`, HTTP failures, parse failures, unexpected pages, and identity mismatches retain their existing distinct behavior and do not use the retirement fallback. | T2, T3, T5 | Negative-path regressions | Verified |
| AC6 | Current and Recent result routes fall back to Historical only for past `OutOfDisplayedRange` dates; today/future, parse, HTTP, and identity failures remain strict. | T4, T5 | Navigator route matrix tests | Verified |
| AC7 | Calendar, odds, subject/profile collection, admin presentation, and the platform-wide terminal-failure safety policy remain unchanged. | T5 | Cross-surface regression and production caller inventory | Verified |
| AC8 | Calendar readiness, one semantic Snapshot, non-calendar navigation, result Current/Recent/Historical behavior, and existing performance tests do not regress. | T5 | Focused scraping/collector/API tests and efficiency gates | Verified |
| AC9 | Three bounded live checks cover visible card success and retired-card result fallback where the official site exposes suitable dates; no production state changes. | T5 | Live read-only verification | Verified |
| AC10 | Release build, all non-external tests, formatting, CodeGraph, record validation, and diff/status checks pass. | T5, T6 | CI-equivalent verification | Verified |
| AC11 | If Historical navigation lands directly on a different race, it must not return that race as the requested result; it either reaches the requested race or reports an explicit terminal identity failure. | T7 | Navigator wrong-race regression | Verified |
| AC12 | Result-route discovery must reject unsupported page kinds instead of succeeding with zero requests, while retaining the supported result-list and single-result cases. | T8 | Discovery unexpected-page regression | Verified |
| AC13 | Calendar readiness cancellation must interrupt every Playwright readiness wait instead of being delayed by an uncancellable three-second sub-wait. | T9 | Repeated cancellation test and original full-suite command | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Audit production failure and every fixed-period/route consumer. Covers AC1-AC10 design. | Main with read-only audit worker | Lead/review tier | - | Read-only plus this record/canonical docs | Source, production UI, tests | Reviewed inventory in this record | Verified |
| T2 | Make discovery route-aware with precise `OutOfDisplayedRange` result fallback. Covers AC1-AC3, AC5. | Main | Lead tier | Approval | discovery handler/tests | Focused handler and identity tests | Result route, URL, lane, priority, and no-odds assertions passed | Verified |
| T3 | Align race-detail fallback behavior without changing card enrichment. Covers AC4-AC5. | Main | Lead tier | T2 contract | detail handler/tests | Boundary and store tests | Boundary result continuation passed | Verified |
| T4 | Extend past-date Current result fallback while preserving today/future strictness. Covers AC6. | Test worker with Main integration | Worker/review tier | T2 route contract | Navigator/tests | Route matrix tests | Four new route tests and 50 Navigator tests passed | Verified |
| T5 | Run cross-surface, pause-policy, performance, live, and non-external regressions. Covers AC1-AC10. | Main | Lead tier | T2-T4 | Read-only except ignored outputs | Recorded commands/results | Focused, live, Release, and 1,050 non-external tests passed | Verified |
| T6 | Synchronize CodeGraph, update records, audit scope/secrets, and commit. Covers AC10. | Main | Lead tier | T5 | docs and derived graph | Validator/diff/status | Final gates passed | Verified |
| T7 | Close review finding 1 by validating a direct Historical result's identity and testing the mismatched-race path. Covers AC11. | Main | Lead tier | User authorization | Navigator and Navigator tests | Focused navigation tests | Requested-race recovery and explicit mismatch tests passed | Verified |
| T8 | Close review finding 2 by rejecting unsupported discovery result pages and testing that failure. Covers AC12. | Main | Lead tier | T7 contract | Discovery handler and tests | Focused handler tests | Unsupported page returns `UnexpectedPage`; focused tests passed | Verified |
| T9 | Close the repeated full-suite cancellation failure by propagating the token through all readiness waits. Covers AC13. | Main | Lead tier | Observed verification failure | Browser implementation/tests | Repeated focused test plus original CI-equivalent suite | 11 focused runs and the original full suite passed | Verified |

## Review gates

- **Design and task-split review — 2026-09-17, reviewer: Main.** Inputs: production job/failure/attempt
  screens, CodeGraph call paths, fixed-period literal inventory, handler/store code, existing Recent fallback tests,
  and the read-only `adjacent_surface_audit`. Decision: mandatory production impact is limited to discovery,
  race-detail, and past-date Current result fallback. Calendar, odds, subjects/profiles, admin UI, and global
  pause policy do not share the faulty source-selection boundary and stay unchanged. AC1-AC10 cover success,
  strict negative paths, performance, live evidence, and final gates. No unresolved design choice blocks
  approval.
- **Pre-implementation review — 2026-09-17, reviewer: Main.** The user explicitly approved AC1-AC10.
  T2 is `In progress`; T3-T6 are `Dependent`. Main owns the shared discovery/detail route contract and
  production integration. A test-only worker may cover the disjoint Navigator route matrix after the contract
  is frozen. Escalate if fallback requires private JRA implementation details, changes today/future semantics,
  creates duplicate race-detail work, or weakens the terminal-failure safety pause.
- **Checkpoint review — 2026-09-17, reviewer: Main.** Main reviewed the production diff and delegated
  Navigator tests against AC1-AC8. Discovery switches its route-state only after the precise exception and
  therefore emits a result URL, Background lane, lower priority, and no odds request. Race detail skips only
  missing-card enrichment and continues its existing result workflow. Four delegated route tests passed and
  did not change production code; focused Collector tests passed. One parallel Playwright cancellation timing
  assertion was slow once and passed immediately in isolated rerun, with no related source change.
- **Final review — 2026-09-17, reviewer: Main.** AC1-AC10 trace to production handlers/Navigator and passing
  focused, live, and solution evidence. Cross-surface inventory confirms no behavior change to calendar, odds,
  subjects/profiles, admin presentation, or global pause policy. No approved task, review finding, or blocker
  remains. Deployment and production recovery remain explicitly excluded.
- **Post-implementation review reopening — 2026-09-17, reviewer: Main.** A later explicit review found two
  material terminal-validation gaps: Historical direct-result navigation could return a different race, and
  discovery could treat an unsupported result-route page as an empty successful result. The user authorized
  both fixes. AC11/T7 and AC12/T8 are now closure items, so the record returns to `Approved` until their
  regression tests and final gates pass.
- **Post-review final review — 2026-09-17, reviewer: Main.** The original two findings map one-to-one to AC11
  and AC12 and now have production-boundary regressions. The repeated cancellation failure maps to AC13 and
  has verified cause, product fix, repeated focused evidence, and a passing rerun of the original full-suite
  command. AC1-AC13 and T1-T9 are all `Verified`; no authorized review item or observed verification failure
  remains open. No reusable skill gap was found because the existing external-adapter fidelity gate already
  required terminal kind/identity validation; the demonstrated failure was noncompliance with that gate.

## Verification record

- 2026-09-17: the first post-review focused test run launched Scraping and Collector test builds in parallel.
  The Scraping command failed before test execution with `CS2012` because both processes attempted to write
  `HorseRacingPrediction.Contracts.dll` in the same `obj` directory; the concurrent Collector command passed
  14/14. Classification: deterministic local command concurrency conflict, unrelated to product behavior.
  Disposition remains open until the original Scraping selection is rerun sequentially and passes.
- 2026-09-17: the first sequential Scraping rerun reached compilation and exposed that the new test asserted
  non-existent convenience properties on `JraRaceIdentityMismatchException`. Classification: deterministic
  test implementation defect. The assertion was corrected to the exception's existing `Message` and
  `RawValue` contract; closure requires the same focused command to pass.
- 2026-09-17: the CI-equivalent suite reproduced the earlier calendar cancellation failure at 2.28 seconds
  against a two-second bound. Source inspection established the cause: the cancellation token reached the
  calendar-specific wait but not the preceding load-state and generic-readiness Playwright waits, each of
  which could block for three seconds. Classification: deterministic load-sensitive product cancellation
  defect exposed by suite concurrency. The token is now propagated through every readiness wait; closure
  requires repeated focused execution and a successful rerun of the original full-suite command.
- 2026-09-17: the first focused compile after changing unsupported pages to an explicit `UnexpectedPage`
  completion found the new assertion used `Message` instead of the record's existing `ErrorMessage` member.
  Classification: deterministic test implementation defect; corrected before rerunning the focused command.
- 2026-09-17: post-review focused verification passed: Navigator 52/52, discovery/detail handlers 31/31,
  and the combined final selection 53/53. The calendar cancellation test passed once after build and ten
  additional no-build repetitions, closing the load-sensitive timing failure with cause evidence rather than
  an isolated rerun.
- 2026-09-17: the original CI-equivalent sequence passed after the final edits: formatting verification;
  Release build with zero warnings/errors; and 1,053 non-external tests passed with one existing skip. This
  closes the prior file-lock, test-compilation, and cancellation entries. CodeGraph synchronized and confirmed
  the updated navigation, discovery, and readiness-wait call paths.
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
- 2026-09-17: the user explicitly approved AC1-AC10 and requested implementation.
- 2026-09-17: focused Collector tests passed 39/39; Navigator plus browser-efficiency selection passed after
  one unrelated timing-only cancellation test succeeded on immediate isolated rerun. Navigator tests passed
  50/50, including four delegated Current-route boundary tests.
- 2026-09-17: read-only live checks passed for current calendar (about five seconds), completed RaceResult
  (about nine seconds), retired RaceCard range detection (about seven seconds), and Historical RaceResultList
  fallback (about eight seconds). The current-week RaceCard list check had no suitable published meeting and
  skipped; deterministic normal-path tests verify unchanged fast-path behavior.
- 2026-09-17: Release solution build passed with zero warnings and errors. All non-external solution tests
  passed: 1,050 passed and one existing skip.
- 2026-09-17: CodeGraph synchronized and re-queried the changed handlers/Navigator; formatting verification,
  change-record validation, and final diff/status checks passed.

## Deviations and follow-up

- Review finding analysis: AC5 already required strict unexpected-page and identity behavior, and the existing
  `learn-from-implementation-failures` external-adapter gate already requires terminal kind/identity tests.
  The omission was therefore not a missing skill rule. The implementation review verified the successful
  fallback route but did not enumerate every terminal page shape on the newly reachable Historical branch;
  tests likewise used only the ideal requested-race/list fixtures. The corrective gate is AC11-AC12 with
  production-boundary regressions for a plausible wrong race and an unsupported page kind. No skill file is
  changed because duplicating an existing enforceable rule would not change future decisions.
- The post-review implementation now matches the expanded AC11-AC13 contract; no behavioral deviation or
  open local follow-up remains.
- Deployment, pipeline resume, and recovery of current failed tasks remain separate operational actions.
