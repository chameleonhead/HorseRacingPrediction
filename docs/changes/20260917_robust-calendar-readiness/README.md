# Robust JRA calendar readiness

- Status: Implemented
- Owner: HorseRacingPrediction team
- Created: 2026-09-17
- Updated: 2026-09-17

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | Visible calendar readiness, one bounded GET retry, timeout propagation, and requested-month cache validation are implemented. |
| Verification | Complete | Focused tests, classifier tests, three live repetitions, Release build, and all non-external tests passed. |
| Deployment/operation | Out of scope | No deployment, production resume, or existing-job retry was performed. |

## Context

Production race discovery intermittently failed on `https://www.jra.go.jp/keiba/calendar/` with
`JraPageParseException: 対象年月を取得できませんでした。` and paused the collection pipeline. The page
first exposes a calendar shell and fills the visible year, month, dates, and racecourses later.

The current browser path waits up to three seconds for calendar year/month text and any numeric table cell,
swallows timeout, and immediately takes the one no-wait semantic Snapshot. This has two defects:

1. visible calendar content that takes longer than three seconds reaches the parser as an incomplete page;
   and
2. the calendar shell can create year/month and numeric day cells before racecourse information is visible,
   so the current condition can also complete too early.

The resulting parse exception is structural/permanent from the collection platform's point of view, whereas
a calendar data-readiness timeout is an intermittent transport/rendering failure and must remain retryable.

The performance contract in
[Playwright collection efficiency](../20260915_playwright-collection-efficiency/README.md) remains binding:
typed JRA operations use one page-specific post-action readiness barrier, no discarded legacy full-page text,
and one semantic Snapshot per parsed terminal page; they do not wait for images, fonts, trackers, `Load`, or
`NetworkIdle`. Link extraction stays batched and production stays on one sequential browser page.

## Goals

- Prove from the rendered page that the requested calendar has enough visible year, month, date, and
  racecourse information to parse before taking the semantic Snapshot.
- Recover once inside the operation when an idempotent calendar GET has a transient rendering failure.
- Report final readiness exhaustion as a cancellation-aware transient timeout rather than an incomplete-page
  parse error.
- Validate the parsed month before returning or caching it.
- Preserve the September 15 browser-efficiency invariants on every normal successful path.

## Non-goals

- Generic retries for clicks, forms, POST/session-bound actions, or non-calendar pages.
- Fixed sleeps, global timeout increases, `Load`/`NetworkIdle` waiting, or repeated semantic Snapshot polling.
- Observing, intercepting, or parsing JRA's internal JSON/API requests.
- Resource blocking, lossy Snapshot projection, child pages, or parallel navigation.
- Sharing Chromium's disk/HTTP cache across Lambda execution environments.
- Changing permanent parser/markup failures into transient failures after the readiness contract has succeeded.
- Automatically resuming production, retrying existing production jobs, or deploying this change.

## Technical impact

### Calendar readiness contract

Calendar readiness is determined only from rendered, user-visible page content. The implementation does not
observe or depend on JRA's internal JSON/API URLs, response shapes, JavaScript functions, or callback timing.
A successful attempt requires all of the following:

1. navigation reaches `DOMContentLoaded`;
2. the rendered page exposes a year/month heading or caption;
3. the rendered calendar contains valid date cells; and
4. at least one date cell exposes a recognized JRA racecourse name, proving that the useful schedule content
   rather than only the empty calendar shell is visible.

The condition is checked in the existing single page-specific readiness barrier and exits immediately when
the visible contract is satisfied. Images, fonts, trackers, global network idleness, full document load, and
internal request completion are irrelevant. The attempt deadline is 10 seconds and is always bounded by the
caller's cancellation token.

If the first attempt does not meet the visible contract, the browser repeats exactly one idempotent calendar
GET using the resolved calendar URL. There is no retry on an already successful attempt and no fixed backoff.
After the second failed attempt, the operation throws a calendar-readiness `TimeoutException` carrying the URL
and missing visible condition. It does not take a semantic Snapshot or invoke `CalendarPageParser` for that
incomplete page.

If JRA later introduces an explicit visible empty-calendar state, that state can be added as an allowed screen
condition with a fixture. In the absence of such a visible signal, a calendar containing no racecourse is not
silently accepted as an empty month because it cannot be distinguished from an unfinished screen without
depending on internal implementation details.

### Parse and cache validation

After the single successful semantic Snapshot is parsed, `JraNavigator.ToCalendarAsync` requires the returned
`JraCalendarPage.Month` to equal the requested month. Only a matching page enters `_lastCalendarPage`. A
mismatch is an explicit navigation/readiness failure and never contaminates the same-month cache.

The parser remains strict. If readiness succeeded but the completed page has a genuine structural change,
`JraPageParseException` keeps its existing non-transient meaning rather than being hidden by retries.

### Performance invariants carried forward

- Normal success: one calendar GET, one page-specific readiness barrier, one semantic Snapshot, one typed
  parse, zero legacy full-page text reads.
- Recovery success: at most two calendar GETs, but still only one semantic Snapshot and one typed parse.
- Non-calendar readiness and navigation behavior are unchanged.
- No repeated Snapshot polling and no locator-per-element extraction are introduced.
- The existing slow-image and 10/100/500-link performance tests remain required gates.
- A successful same-session same-month lookup remains a cache hit with no navigation or capture.

## Alternatives considered

### Observe the monthly JSON/API request

Rejected. Although this would identify the current asynchronous source precisely, it would couple the
collector to JRA's private URL layout, response schema, and client-side implementation. The supported contract
for this scraper is what the rendered official page exposes.

### Only increase the three-second timeout

Rejected. It would fast-exit on normal pages, but the current DOM predicate can become true before racecourse
content is visible and its swallowed timeout still becomes a misleading permanent parse failure.

### Retry inside `JraPageReader` or every parser

Rejected. It would repeat expensive semantic captures, broaden retries to real markup regressions, and weaken
the single-readiness/single-Snapshot ownership boundary.

### Wait for `Load`, `NetworkIdle`, or a fixed delay

Rejected. Those conditions couple latency to unrelated images, fonts, analytics, and long-lived traffic and
would undo the measured browser-efficiency improvement.

### Parse JRA's internal JSON directly as the domain source

Rejected for this change. It replaces the established browser/Snapshot/parser contract and needs a separate
integration design and adoption evidence.

### Share browser cache across Lambda execution environments

Rejected for this change. A compatible collection envelope already shares one browser session and its normal
in-context HTTP cache. Separate Lambda environments cannot share their local cache directly; adding EFS, a
proxy, or another external cache would introduce serialization, invalidation, concurrent-writer, stale-content,
and network-latency costs. It would not prove that the current page finished rendering and therefore would not
fix this readiness failure. No shared cache will be added without separate phase measurements showing that
repeat static-resource transfer, rather than browser startup, navigation, rendering, or parsing, is material.

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: links this proposed readiness refinement from the canonical navigation
  and performance policy and now records the implemented readiness contract.
- `docs/changes/20260917_robust-calendar-readiness/README.md`: owns the change-specific design, acceptance
  criteria, task ledger, and verification evidence.
- `docs/changes/20260915_playwright-collection-efficiency/README.md` and its `baseline.md` were inspected and
  remain historical evidence; they are not rewritten.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | A calendar whose visible year/month/date/racecourse content completes after more than three seconds but within the attempt deadline returns the correct month and race dates. | T2, T3 | Deterministic delayed-render Playwright fixtures | Verified |
| AC2 | A fast calendar succeeds with one GET, one readiness barrier, one semantic Snapshot, one typed parse, and zero discarded full-page text reads. | T2, T3 | Instrumented fast fixture | Verified |
| AC3 | A first screen that never becomes visibly parseable is recovered by exactly one calendar GET retry; the recovered operation still takes only one final Snapshot and parse. | T2, T3 | First-incomplete/second-success fixture with counters | Verified |
| AC4 | Two incomplete attempts end in a cancellation-aware calendar-readiness `TimeoutException`, take no incomplete Snapshot, and are classified as `TransientFailure` without pausing the pipeline. | T2, T4 | Browser timeout/cancellation tests plus collection-classifier test | Verified |
| AC5 | Successful readiness followed by a genuine malformed calendar remains a structural parse failure and is not retried as transient readiness. | T3, T4 | Completed-but-malformed classification assertion | Verified |
| AC6 | A parsed month must equal the requested month before return/cache; a mismatch is not cached, while a second successful same-month call remains a zero-navigation cache hit. | T3 | Navigator trace and cache tests | Verified |
| AC7 | A calendar shell containing only year/month and day numbers is not accepted; readiness requires a visible recognized racecourse or a separately verified explicit empty-state message. | T2, T3 | Shell-only and delayed-racecourse fixtures | Verified |
| AC8 | Non-calendar navigation, slow image/font/tracker behavior, batched link extraction, one sequential page, full safe-pruning Snapshot, and cancellation behavior do not regress. | T2, T5 | Existing efficiency tests, 10/100/500 candidates, 100-link benchmark, focused regression suite | Verified |
| AC9 | The current-month live calendar path succeeds in at least three bounded sequential repetitions with elapsed/readiness evidence and correct requested-month identity; no production data is changed. | T5 | External calendar E2E repeated three times | Verified |
| AC10 | Formatting, Release build, all non-external tests, CodeGraph synchronization, graph re-query, diff/status/secret checks, and change-record validation pass. | T5, T6 | CI-equivalent commands and final review record | Verified |

## Delivery plan

1. Approve this record; approval authorizes only AC1-AC10 and the listed code/test/document scopes.
2. Implement and test the visible calendar readiness primitive without changing parser/domain contracts.
3. Integrate the one-retry GET policy and requested-month cache validation.
4. Verify transient timeout classification through the collection boundary.
5. Run focused, performance, live, and CI-equivalent verification; update this record and canonical policy.
6. Commit the verified implementation as one purpose-specific checkpoint. Deployment and production recovery
   remain separate unless explicitly requested.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Trace the production failure, prior performance contract, and robust alternatives. Covers AC1-AC10 design. | Main with research workers | Lead/review tier | - | Read-only | Source/history/change-record evidence | Reviewed findings in this record | Verified |
| T2 | Implement visible-content-aware, bounded and cancellation-aware calendar GET readiness with one safe retry. Covers AC1-AC5, AC7, AC8. | Main | Lead tier | Approval | `PlaywrightWebBrowser*`, browser tests | Deterministic Playwright fixtures and efficiency tests | 64 focused scraping tests passed | Verified |
| T3 | Enforce requested-month identity, cache safety, and terminal single-capture behavior. Covers AC1-AC3, AC5-AC7. | Main | Lead tier | T2 | `JraNavigator*`, navigation/parser tests | Navigation traces and parser fixtures | Mismatch/cache regression passed | Verified |
| T4 | Prove readiness timeout remains retryable while completed structural parse failure does not. Covers AC4, AC5. | Main | Lead tier | T2 | Collection classifier tests only | Focused collection tests | 9 contract tests passed | Verified |
| T5 | Run focused regressions, performance gates, three bounded live repetitions, Release build, and non-external suite. Covers AC8-AC10. | Main | Lead tier | T2-T4 | Read-only except test outputs ignored by git | Recorded commands/results | All verification gates passed | Verified |
| T6 | Synchronize CodeGraph, update canonical documentation and this record, audit scope/secrets/status, and commit. Covers AC10. | Main | Lead tier | T5 | `.codegraph`, `docs/23-jra-scraping-redesign.md`, this record | Graph re-query, validator, diff/status checks | Final checks passed | Verified |

T2 and T3 are serialized because they share navigation/readiness behavior. T4 may run in parallel only after
the timeout contract is frozen and has a disjoint test-only write scope. Shared builds, formatting, CodeGraph,
documentation, and commits are owned by Main.

## Review gates

- **Design and task-split review — 2026-09-17, reviewer: Main.** Inputs: production `/jobs` evidence,
  `PlaywrightWebBrowser`, `JraNavigator`, `CalendarPageParser`, collection exception classification, commits
  `e2ff18e` and `1959594`, the 20260915 efficiency record/baseline, and two read-only delegated reviews.
  Decision: simple timeout extension is insufficient because the calendar shell can satisfy the old predicate
  before racecourse content is visible. Internal JSON/API observation was considered and rejected to avoid
  coupling to JRA private implementation. AC1-AC10 cover visible data, timeout/retry, parse distinction, cache
  identity, performance, live verification, and final gates. Write scopes are serialized where shared; T4 is
  suitable for bounded worker delegation after approval. No unresolved design choice blocks approval.
- **Pre-implementation review — 2026-09-17, reviewer: Main.** The user explicitly approved AC1-AC10 after
  confirming that readiness must use only rendered screen content and that cross-Lambda cache is excluded.
  T2 is `In progress`; T3-T6 are `Dependent`. The visible witness is matching year/month plus valid date cells
  and a recognized racecourse name, the attempt budget is two cancellation-aware 10-second calendar GET
  attempts, and incomplete attempts produce no Snapshot. T2/T3 shared browser/navigation work is serialized.
  T4 may start only after T2 freezes the timeout contract and may write collection tests only. Escalate if the
  live page has no visible racecourse witness, if retry requires a non-idempotent action, if the browser API
  cannot preserve one terminal Snapshot, or if an approved performance invariant must change.
- **Checkpoint review — 2026-09-17, reviewer: Main.** The focused scraping suite (64 tests) and collection
  contract suite (9 tests) passed. Review confirmed that only direct calendar GET navigation receives the
  single retry, an incomplete attempt cannot reach Snapshot capture, and requested-month mismatch cannot enter
  cache. The existing typed no-text path and batched link extraction remain unchanged.
- **Final review — 2026-09-17, reviewer: Main.** AC1-AC10 trace to passing deterministic, classifier, live,
  and solution-wide evidence. CodeGraph re-query showed the expected `ToCalendarAsync` caller blast radius;
  all callers are covered by the non-external suite. No unresolved approved task, rejected item, external
  blocker, secret, or unintended file remains. Deployment and production recovery remain explicitly excluded.

## Verification record

- 2026-09-16/17: production UI showed two calendar failures with the same missing-year/month parse reason.
- 2026-09-16: the existing live current-month E2E passed when the page finished rendering, supporting an
  intermittent readiness race rather than a persistent schema/data absence.
- 2026-09-17: user feedback established that readiness must depend on rendered screen content rather than
  JRA's internal JSON/API implementation; the proposed contract was revised accordingly before approval.
- 2026-09-17: cross-Lambda browser-cache sharing was evaluated and excluded. Existing compatible-envelope
  session reuse remains; no evidence currently justifies a new shared cache layer.
- 2026-09-17: source trace confirmed the current calendar timeout is swallowed before the no-wait Snapshot,
  and that `SelectCalendarMonthAsync` parses before month fallback and caches without a final requested-month
  equality guard.
- 2026-09-17: prior optimization evidence was preserved: typed no-text operations, single page-specific
  barrier, one no-wait Snapshot, batched candidates, single sequential page, and disabled resource/projection
  experiments remain in scope as regression gates.
- 2026-09-17: delegated research tasks `performance_history` and `robust_wait_design` were accepted after Main
  checked their cited source/history evidence. Rework and escalation count: zero. Usage/cost telemetry was
  unavailable; elapsed effort is not exposed by the orchestration interface.
- 2026-09-17: focused scraping regressions passed: 64/64. Collection classifier contracts passed: 9/9.
- 2026-09-17: live `現在月Calendar取得` passed three sequential runs, each completing in approximately five
  seconds with the requested month identity intact.
- 2026-09-17: Release solution build passed with zero warnings and zero errors. All non-external solution
  tests passed: 1,044 passed and 1 skipped.
- 2026-09-17: `dotnet format` completed, CodeGraph synchronized and re-queried, and final repository checks
  and change-record validation passed.

## Deviations and follow-up

- The approved implementation matches the design; no behavioral deviation remains.
- Deployment, resuming the stopped pipeline, and retrying production failures are excluded follow-up actions.
