# Robust JRA horse-search form readiness

- Status: Approved
- Owner: HorseRacingPrediction team
- Created: 2026-09-17
- Updated: 2026-09-17

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | Missing-only visible-field readiness, bounded cancellation, final structural failure, and diagnostics are implemented. |
| Verification | Complete | Focused browser/collection tests, three live checks, Release build, 1,058 non-external tests, formatting, migration, vulnerability, and graph gates passed. |
| Deployment/operation | Not started | Deployment, pipeline resume, and failed-task recovery follow verified implementation. |

## Context

Production collection is stopped by a horse-profile task with
`InvalidOperationException: フィールド 'iv_h_name' が見つかりませんでした。`. The failed resource is
`horse-b280e889-ec56-5fff-b0f9-62d1d8890656`. Attempts 1-5 reached the existing publication-waiting outcome,
whereas attempt 6 failed after 8.9 seconds while opening the horse-search form. This history proves that the
resource identity itself is not the immediate cause and makes a permanent removal of the search path less
likely.

`JraNavigator.OpenHorseSearchAsync` opens the public `競走馬検索` link and immediately asks
`PlaywrightWebBrowser` to fill `iv_h_name`. The browser's generic post-click readiness accepts any non-empty
rendered body containing common elements. It therefore can finish on the search-page shell before the
horse-name input is rendered. `SetFieldValueCoreAsync` performs only an immediate lookup and raises an
`InvalidOperationException` when the input is not present at that instant. The collection-wide exception
classifier correctly treats that unclassified exception as permanent and pauses the pipeline.

A current read-only external regression for the same public horse-search flow passed in 10 seconds. Together
with the prior five non-structural outcomes, this supports an intermittent operation-readiness race rather
than a reproducible removed field. It does not by itself prove that every production run is safe.

The existing performance contract remains binding: typed operations do not read and discard full page text,
do not wait for `Load` or `NetworkIdle`, take one semantic Snapshot for the terminal parsed page, and batch
large candidate extraction.

## Goals

- Make form input wait for the specific requested visible field, not generic page activity.
- Preserve fast exit when the field is already present and bound only the slow path.
- Keep a genuinely missing or renamed field structural so the global safety pause still protects collection.
- Add enough diagnostics to distinguish a readiness timeout from a completed page with an absent field.
- Deploy and recover the stopped production pipeline only after the focused and regression gates pass.

## Non-goals

- Waiting for network idleness, all assets, fonts, analytics, or full-document load.
- Inspecting or depending on JRA's private JSON/API calls, JavaScript callbacks, or internal timing.
- Retrying the non-idempotent search-form submission or directly constructing a session-bound result URL.
- Weakening the collection platform's permanent-failure pause policy.
- Changing horse identity matching, candidate bounds, profile parsing, or persistence behavior.
- Sharing browser caches across Lambda execution environments.

## Technical impact

### Field-specific readiness

`SetFieldValueCoreAsync` will retain its immediate field lookup. Only when that lookup misses will it wait, for
at most 10 seconds and under the caller's cancellation token, for a rendered fillable element matching the
existing label/name/placeholder rules. The wait exits as soon as the element is visible, fills that same
resolved element, and continues through the existing form-specific submission path.

The field check is the visible interaction contract of the requested operation. It does not infer readiness
from global network state or from JRA's internal implementation. No extra wait occurs after a successful
field resolution, and no semantic Snapshot is captured merely to poll readiness.

If the bounded wait expires, the browser re-runs the immediate lookup once to close the boundary race. If the
field is still absent, it throws a structural missing-field exception containing the field name and final URL;
it does not turn the defect into an indefinitely retryable timeout. Thus a renamed/removed field still reaches
the existing permanent-failure safety pause. Cancellation remains cancellation and is not reclassified.

### Affected surfaces

Repository search shows that production `SetFieldValueForSnapshotAsync` currently has one caller: JRA horse
search. The shared browser primitive is still tested generically so a later caller inherits the same visible-
field contract. Jockey/trainer directories, calendar, race discovery/detail, odds, result navigation, profile
parsing, admin UI, and persistence do not use this form-input path and require regression checks rather than
behavior changes.

### Production recovery

After deployment health succeeds, retry only the affected horse failure and resume the pipeline. Confirm that
the horse task either completes or returns its pre-existing domain outcome without another `iv_h_name` error.
Then confirm `discovery:2026091306` is allowed to run to its terminal successful state and that the original
Nakayama navigation exception does not recur. A different structural failure remains a stop condition and is
not automatically bypassed.

## Alternatives considered

### Increase the generic page-settled delay

Rejected. It delays unrelated pages and still does not prove that the requested control exists.

### Wait for `NetworkIdle`

Rejected. Images, analytics, and long-lived requests make it slower and less deterministic without proving
that the horse-name field is usable.

### Retry the whole navigation or form submission

Rejected. The search route is session-bound and submission is not treated as idempotent. Replaying it can
create ambiguous navigation and would undo the existing safe-action boundary.

### Treat every missing field as transient

Rejected. A real JRA markup change would retry indefinitely and weaken the production safety pause.

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: adds the proposed visible-field readiness rule and links this record as
  the change-specific design. The existing calendar and performance contracts remain canonical.
- `docs/changes/20260917_horse-search-form-readiness/README.md`: owns incident evidence, scope, acceptance
  criteria, implementation plan, and eventual production-recovery evidence.
- `docs/22-collector-design.md` and `docs/26-collection-platform-design.md` were inspected. No update is
  necessary because retry/pause policy is intentionally unchanged.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | When the public horse-search page renders `iv_h_name` after the generic page shell, collection waits for the visible field and proceeds to the correct horse result/profile. | T2, T3 | Deterministic delayed-form fixture and Navigator integration test | Verified |
| AC2 | When the field is already visible, the operation performs no fixed delay, no `NetworkIdle` wait, no discarded full-page text read, and no additional semantic Snapshot. | T2, T4 | Instrumented fast fixture and existing efficiency tests | Verified |
| AC3 | A field that remains absent after 10 seconds fails structurally with field name and final URL and retains the existing global safety-stop behavior. | T2, T3 | Missing-field fixture and collection-boundary classification test | Verified |
| AC4 | Cancellation interrupts the field wait promptly and remains a cancellation/transient outcome rather than a structural missing-field failure. | T2, T3 | Repeated bounded cancellation test and classifier assertion | Verified |
| AC5 | Horse form submission remains scoped to the form containing `iv_h_name`; no URL synthesis or whole-navigation/submission replay is introduced. | T3, T4 | Form-selection regression and source/diff review | Verified |
| AC6 | Horse identity, pagination, candidate evidence bounds, profile validation, jockey/trainer navigation, calendar, race, odds, and result behavior do not regress. | T4 | Focused scraping/collector suites and existing performance gates | Verified |
| AC7 | The live horse-search path succeeds in three bounded sequential checks without changing production data. | T4 | External E2E repetitions with elapsed evidence | Verified |
| AC8 | Formatting, Release build, all non-external tests, CodeGraph sync/re-query, record validation, diff/status, and secret checks pass. | T4, T5 | CI-equivalent and repository gates | Verified |
| AC9 | After deployment, the affected horse failure is recovered without another `iv_h_name` error, the pipeline is running, and `discovery:2026091306` reaches a successful terminal state without the original Nakayama error. | T6 | Deployment run, production job/attempt screens, and pipeline status | Not started |

## Delivery plan

1. Obtain explicit approval for AC1-AC9 and the stated scope.
2. Add field-specific, cancellation-aware readiness on the missing-only path.
3. Add delayed, absent, fast, and cancellation fixtures plus Navigator/form-selection coverage.
4. Run focused, efficiency, live, Release, and non-external verification.
5. Synchronize CodeGraph and documentation, self-review the final diff, and push one purpose-specific commit.
6. Verify deployment, recover the affected task, resume collection, and observe the original discovery target.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Trace production evidence, caller impact, prior performance constraints, and alternatives. Covers AC1-AC9 design. | Main | Lead tier | - | Read-only plus design documents | Production UI, source, history, live check | Evidence recorded in this proposal | Verified |
| T2 | Implement visible-field readiness with immediate fast path, 10-second bound, cancellation, and final structural miss. Covers AC1-AC4. | Main | Lead tier | Approval | `PlaywrightWebBrowser.cs` | Deterministic browser fixtures | Four browser readiness tests passed | Verified |
| T3 | Prove horse-search navigation, form selection, failure classification, and safety-stop semantics. Covers AC1, AC3-AC5. | Main | Lead tier | T2 | Navigator/browser/collection tests | Focused integration and boundary tests | Browser/Navigator 15/15 and classifier 10/10 passed | Verified |
| T4 | Run cross-surface, efficiency, three live repetitions, Release, and non-external regressions. Covers AC2, AC5-AC8. | Main | Lead/review tier | T2-T3 | Read-only except ignored test outputs | Recorded commands and results | Live 3/3, Release build, and 1,058 tests passed | Verified |
| T5 | Update canonical docs and record, sync CodeGraph, audit diff/status/secrets, self-review, commit, and push. Covers AC8. | Main | Lead/review tier | T4 | docs and derived graph | Validator and repository gates | Final review evidence | In progress |
| T6 | Verify deployment and perform bounded production recovery/monitoring. Covers AC9. | Main | Lead tier | T5 and successful deployment | Production operations explicitly listed in AC9 | Production UI and deployment evidence | Dependent |

All implementation and shared verification remain serialized under Main because the browser primitive,
Navigator behavior, classifier evidence, deployment, and production recovery form one safety-sensitive path.

## Review gates

- **Design and task-split review — 2026-09-17, reviewer: Main.** Inputs: production failure group and six
  attempts, `JraNavigator.Subjects`, `PlaywrightWebBrowser`, collection exception classification, the existing
  semantic-snapshot/performance records, caller inventory, and one current live horse-search check. Decision:
  the smallest robust fix is a missing-only wait for the requested visible field. AC1-AC9 cover delayed and
  fast success, structural absence, cancellation, safe form semantics, adjacent regressions, live evidence,
  repository gates, and production closure. No unresolved design decision blocks approval. Delegation is not
  useful because the safety-sensitive write and verification scopes are sequential and overlapping.
- **Pre-implementation review — 2026-09-17, reviewer: Main.** The user explicitly approved AC1-AC9.
  T2 is `In progress`; T3-T6 are `Dependent`. Main owns the serialized browser, navigation, classifier,
  verification, deployment, and production-recovery path. The immediate fast path, missing-only visible-field
  wait, 10-second bound, final structural miss, no replay, and unchanged global pause policy are frozen.
  Escalate only if the live form has no stable visible field contract, implementation requires private JRA
  internals or action replay, or an approved performance/safety invariant must change.
- **Checkpoint review — 2026-09-17, reviewer: Main.** The diff was compared with AC1-AC8. The existing
  immediate resolver remains the fast path; only an initial miss enters the visible-control wait. Timeout is
  followed by one resolver pass and the existing `InvalidOperationException`, so structural absence remains
  permanent. No navigation or form submission is replayed, no semantic Snapshot or legacy text read was
  added, and cancellation reaches the Playwright wait. Focused, live, Release, and full non-external evidence
  passed. T2-T4 are `Verified`; T5 is `In progress`; T6 remains deployment-dependent.
- **Final review:** pending verification and production closure.

## Verification record

- 2026-09-17: production showed one open horse-profile failure for
  `horse-b280e889-ec56-5fff-b0f9-62d1d8890656`, attempt 6, after 8.9 seconds, with no requested/final URL
  recorded and `iv_h_name` absent at input time. Attempts 1-5 had reached `ResourceNotYetAvailable`.
- 2026-09-17: source trace found that generic page readiness accepts a non-empty shell before
  `SetFieldValueCoreAsync` performs a one-shot lookup. The only production caller is horse search.
- 2026-09-17: the existing live `HorseProfileSearch_DaiyuVenti` regression passed in 10.873 seconds.
- 2026-09-17: no production code was changed during investigation.
- 2026-09-17: the user explicitly approved AC1-AC9; implementation is authorized.
- 2026-09-17: four deterministic field-readiness tests passed for delayed display, immediate display, final
  structural absence with URL, and prompt cancellation. Focused browser/Navigator tests passed 15/15 and the
  collection contract suite passed 10/10.
- 2026-09-17: the live `HorseProfileSearch_DaiyuVenti` path passed three sequential runs in approximately
  four, three, and three seconds.
- 2026-09-17: formatting verification and Release build passed with zero warnings/errors. All non-external
  tests passed: 1,058 passed and one existing skip. No pending EF model change or vulnerable package was found.
- 2026-09-17: CodeGraph synchronized and re-query confirmed the one production field-input path and expected
  browser helper call graph. Diff review found no action replay, `NetworkIdle`, extra Snapshot, or pause-policy
  change.

## Deviations and follow-up

- None at implementation start.
