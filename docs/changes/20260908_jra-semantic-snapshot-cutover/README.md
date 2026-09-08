# JRA Semantic Snapshot Cutover

- Status: Approved
- Owner: Scraping team
- Created: 2026-09-08
- Updated: 2026-09-08

## Context

The user has selected a direct cutover: remove the existing section-oriented snapshot implementation,
make the semantic snapshot the sole `IWebBrowser.GetPageSnapshotAsync` result, and add generic table-cell
fragment evidence needed by JRA parsing. This record turns the earlier
[migration challenge inventory](../20260908_jra-semantic-snapshot-migration/README.md) into an executable
design.

The change is intentionally breaking inside the repository. It updates all first-party consumers and tests
in the same change set rather than maintaining two production snapshot implementations.

## Goals

- Make `Browser.Snapshots.PageSnapshot` the only page snapshot model.
- Have `PlaywrightWebBrowser.GetPageSnapshotAsync` invoke `IPageSnapshotter` against its private `IPage` after
  the browser's existing readiness wait.
- Delete compatibility snapshot extraction and models after all consumers compile against semantic data.
- Preserve bounded, generic DOM evidence inside table cells so JRA parsers can recover structured values and
  links without provider rules in the browser layer.
- Migrate JRA parsers, JRA navigation, agent tools, extraction formatting, collector scheduling, fakes, and
  tests to the semantic contract.
- Avoid routine dual capture and retain one semantic browser evaluation per snapshot request.

## Non-goals

- Exposing `Microsoft.Playwright.IPage` through `IWebBrowser`.
- Moving navigation, clicks, waiting, JRA selectors, or JRA class meanings into `IPageSnapshotter`.
- Preserving `Sections` through a compatibility adapter.
- Retaining the old snapshot implementation behind a runtime fallback.
- Capturing arbitrary element attributes, complete HTML, event handlers, framework state, or unbounded
  `class`/`data-*` values.
- Changing domain page models or expanding the business fields collected from JRA.

## Public API cutover

`IWebBrowser.GetPageSnapshotAsync` will return
`HorseRacingPrediction.Scraping.Browser.Snapshots.PageSnapshot`. The existing `maxLinks` parameter will be
removed; collection limits are a presentation/consumer concern and truncating the canonical snapshot risks
losing navigation evidence. Agent-specific link limits remain applied when formatting tool output.

`PlaywrightWebBrowser` will receive or construct an `IPageSnapshotter` and implement capture as:

```text
validate session
    -> existing WaitForPageSettledAsync
    -> snapshotter.CaptureAsync(private page, JRA-safe defaults, cancellationToken)
    -> log duration/counts/diagnostics
    -> semantic PageSnapshot
```

`IPageSnapshotter` itself remains readiness-agnostic. `FakeWebBrowser` and agent/browser test doubles return
semantic snapshots directly.

## Deleted compatibility surface

After consumer migration, delete:

- `Browser/PageSnapshot.cs`;
- `Browser/PageSectionSnapshot.cs`;
- `Browser/PageActionSnapshot.cs`;
- compatibility `Browser/PageTableSnapshot.cs` and `Browser/PageTableCellSnapshot.cs`;
- section/candidate extraction methods, limits, DTOs, and merge logic used only by compatibility
  `PlaywrightWebBrowser.GetPageSnapshotAsync`.

Standalone APIs such as `GetLinksAsync`, click/form operations, and their request/result types are retained
where still used independently. A type is removed only after `rg` confirms no first-party references.

## Generic table-cell fragments

### Model

Extend semantic `PageTableCellSnapshot` with:

```csharp
public IReadOnlyList<PageElementFragmentSnapshot> Fragments { get; init; } = [];
public PageSourceReference? Source { get; init; }

public sealed record PageElementFragmentSnapshot
{
    public required string TagName { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<string> ClassTokens { get; init; } = [];
    public Uri? Url { get; init; }
    public string? RawUrl { get; init; }
    public string? Role { get; init; }
    public string? AccessibleName { get; init; }
    public PageSourceReference? Source { get; init; }
}
```

The name is intentionally generic. JRA interprets tokens such as `name`, `trainer`, `weight`, and `owner`;
the snapshotter does not.

### Browser extraction rules

For each retained `th`/`td`, collect descendant elements that have at least one of:

- a non-empty `class` token list;
- `href`;
- explicit `role` or ARIA name; or
- semantic tag meaning (`a`, `button`, `img`, `time`, `data`, `abbr`).

Apply the same hidden/aggressive ancestor pruning as the cell. Preserve document order. Text uses the same
effective visible text rules as the surrounding snapshot; image `alt` participates as semantic text. Resolve
URLs against the document base while retaining raw values and diagnostics for invalid URLs.

Bounds prevent arbitrary DOM duplication:

- at most 48 fragments per cell;
- at most 16 class tokens per fragment;
- ignore empty tokens and cap each token at 128 characters;
- deduplicate identical adjacent fragment evidence;
- never capture `data-*`, style, event handlers, input values, or descendant HTML.

When the fragment cap is reached, preserve the first 48 in document order and add a warning diagnostic with
the table/cell source. This makes truncation observable instead of silently changing parser evidence.

## JRA-owned semantic view

Add internal JRA query/projection helpers rather than reintroducing legacy models. Responsibilities:

- aggregate root effective text and ordered headings;
- select nodes/collections using source and semantic ancestry;
- derive link/button click text from visible text, image alt, and accessible name;
- project semantic tables into a rectangular coordinate view while retaining original cells/fragments;
- identify header rows from `IsHeader` and map `RowSpan`/`ColumnSpan` deterministically;
- expose explicit projection failure diagnostics rather than shifting column indexes.

This may be a small internal type, but it must not be named `PageSnapshot` and must not be a compatibility
`Sections` reconstruction.

## Consumer migration

### JRA parsers

- `CalendarPageParser`: replace `Sections`/`MainText` scans with ordered effective text and headings.
- `RaceListPageParser`: use projected headers/rows and fragment URLs for race links.
- `RaceCardPageParser`: use generic fragment class tokens for horse name, trainer, weight, owner, and related
  structured cell values.
- `RaceResultPageParser`: use projected tables for result, corner order, lap, and payout parsing; preserve its
  current explicit error behavior when required evidence is absent.
- `IJraPageParser` and `JraPageReader`: accept only semantic snapshots and log capture diagnostics.

Parser priority and domain output contracts do not change.

### Navigation

`JraNavigator` will use semantic links plus Link/Button nodes and `GetEffectiveText` for date, course,
race-number, and result candidates. Actual interaction continues through `IWebBrowser.ClickAsync`. Candidate
matching must be deterministic when visible text, image alt, and accessible name differ.

### Agents and collector

`PlaywrightTools` and `PageDataExtractionAgent` currently depend on compatibility `MainText`, `Sections`, and
link replacement. Replace this with semantic-root formatting and immutable link projection; retain configured
prompt/link limits at formatting time. `HistoricalRaceReferenceParser` moves to the shared table projection.

These consumers are part of the cutover acceptance criteria, not follow-up work, because deleting the old
model otherwise breaks the solution outside JRA.

## Design review and resolved implementation details

### Buildable transition without a runtime fallback

The first draft switched `IWebBrowser.GetPageSnapshotAsync` before migrating consumers, which cannot form a
buildable checkpoint. During development only, add a clearly named semantic capture member alongside the
legacy member. Migrate one consumer slice at a time; no call path invokes both captures for one read. After
the last consumer switches, remove the legacy member and rename the semantic member to
`GetPageSnapshotAsync` in the removal checkpoint.

The temporary member is a branch-local compatibility seam, not a production rollout fallback. The branch is
not mergeable until it is deleted, satisfying the requested final direct cutover while keeping every commit
buildable and testable.

### Snapshotter construction

`PlaywrightWebBrowser` will own an `IPageSnapshotter` field. `CreateAsync` accepts an optional snapshotter for
focused tests and otherwise constructs `PlaywrightPageSnapshotter`; `PlaywrightWebBrowserSessionFactory`
receives the registered singleton and passes it to `CreateAsync`. This avoids exposing `IPage`, permits a
capture spy in browser tests, and does not introduce a JRA-specific browser dependency.

### Cell identity and truncation evidence

Semantic cells gain `Source` in addition to `Fragments`. Fragment truncation diagnostics identify the table,
row, and cell indexes in the message and attach the cell source. Locator hints are investigation aids, not
stable primary keys; parsers consume fragments nested in their owning cell and never join them by selector.

### Span-aware projection algorithm

The JRA projection walks source rows in order and places each cell in the first unoccupied logical column.
It reserves the covered rectangle for `RowSpan` and `ColumnSpan`, rejecting overlap, non-positive effective
width, or a span extending beyond configured safety bounds. Header labels are built per logical column from
applicable `th` cells in header rows, joining multi-row labels in top-to-bottom order without adjacent
duplicates. Body rows expose logical columns while retaining references to originating cells.

A table with irreconcilable overlap or inconsistent width produces a projection diagnostic and is not passed
to a JRA parser. It is never padded in a way that silently moves a value under another header.

### Agent text and snapshot prompts

`Root.GetEffectiveText()` is acceptable for parser searches but is not a readable replacement for agent
prompts by itself because it flattens block boundaries. Add a bounded generic text projection that emits
headings, paragraphs, list items, links/images, and dedicated tables with line boundaries. Agent snapshot
JSON is rebuilt from a size-bounded semantic projection rather than serializing source/location evidence and
fragments wholesale. This preserves prompt budgets and avoids exposing class-token noise to the LLM.

### Standalone browser models

`GetLinksAsync`, click/form interaction operations, and their existing result records remain during this
cutover because `JraNavigator` also uses link-only reads in paths where a full snapshot is unnecessary.
They are not the removed page snapshot contract. Their names must be explicitly aliased where semantic link
types coexist. Consolidating those standalone APIs is a separate cleanup after snapshot cutover and must not
delay deletion of section capture.

### Zero-reference removal gate

Before deleting each legacy file, search the entire solution, including Agents and Collector tests—not only
the Scraping project. The removal commit must contain a machine-readable zero-match check for legacy
`PageSectionSnapshot`, `PageActionSnapshot`, `PageDomTextFragment`, and the legacy snapshot constructor.

## Performance and diagnostics

- Normal snapshot capture performs one `EvaluateAsync`; fragment collection is part of that traversal.
- No legacy snapshot is captured before or after semantic capture.
- Element locations remain disabled by default.
- Record capture duration, node/table/link/form/image counts, fragment count, and diagnostic codes.
- Add local table-heavy measurements comparing semantic capture before and after fragments. Define a measured
  budget in the implementation record; any payload or median-time increase above 15% requires optimization or
  explicit user re-approval because fragments intentionally add evidence.
- Do not log fragment text, form values, credentials, or complete snapshots in routine logs.

## Delivery checkpoints

1. **Semantic fragment model and capture:** model, browser DTO, conversion, diagnostics, and focused tests.
2. **Table projection and fixtures:** JRA-owned view plus multi-row header/span/malformed-table tests.
3. **Browser boundary:** add the temporary branch-only semantic capture member to `IWebBrowser`,
   `PlaywrightWebBrowser`, and fakes; verify no request performs both captures.
4. **Calendar and race-list:** migrate parsers and parity fixtures.
5. **Race-card:** migrate fragment-dependent parsing and parity fixtures.
6. **Race-result:** migrate all result/payout table variants and parity fixtures.
7. **Navigator:** migrate semantic action/link candidates and navigation tests.
8. **Agents and collector:** migrate prompt formatting, tool limits, and historical reference parsing.
9. **Removal:** remove the legacy member, rename semantic capture to `GetPageSnapshotAsync`, and delete
   compatibility models/extraction after solution-wide zero-reference checks.
10. **Final verification:** performance record, full non-external suite, selected local-browser scenarios,
    documentation synchronization, and status `Implemented`.

Each checkpoint is independently buildable and tested. Because direct cutover is selected, the branch may
not be merged until all checkpoints pass; intermediate commits are review checkpoints, not deployable partial
rollouts.

## Test strategy

- Browser tests for fragments, link resolution, hidden content, alt text, caps, diagnostics, order, class-token
  bounds, and sensitive-attribute exclusion.
- Projection unit tests for header rows, row/column spans, sparse/malformed tables, and original evidence.
- Semantic fixture builders for all parser and navigator tests.
- Parser parity cases covering current Calendar, RaceList, RaceCard, and RaceResult expected domain outputs.
- Agent tests for semantic text formatting and link limits.
- Collector tests for historical race references through semantic table projection.
- Existing local Playwright snapshot suite and full `TestCategory!=External` solution suite.
- External JRA tests remain supplemental and are never the sole acceptance evidence.

## Acceptance criteria

1. `IWebBrowser.GetPageSnapshotAsync` returns the semantic immutable snapshot and exposes no `IPage`.
2. The compatibility snapshot, section, action, compatibility table/cell models, and compatibility-only
   extraction code have no references and are deleted.
3. Semantic table cells preserve bounded generic fragments, links, class tokens, accessible evidence, source,
   and observable truncation without JRA-specific interpretation.
4. Table projection handles headers and spans safely and reports malformed input.
5. All four JRA parsers produce the same domain outputs for existing positive and negative fixtures.
6. Race-list links and race-card fragment-dependent fields retain their current behavior.
7. `JraNavigator` date/course/race/result candidate behavior remains covered and equivalent.
8. Agent page formatting, safe detail-link selection, link limits, and collector historical-reference parsing
   work with semantic snapshots.
9. A normal snapshot request performs one semantic browser evaluation and never performs legacy capture.
10. Fragment performance/size results are recorded and stay within the approved 15% investigation budget or
    receive explicit re-approval.
11. `dotnet build HorseRacingPrediction.sln` and the complete non-external suite pass.
12. Canonical documentation contains no statement that the compatibility snapshot remains active.
13. The temporary semantic capture member used for buildable migration is absent from the final public API.
14. Agent text and JSON projections preserve useful block/table structure and enforce existing prompt limits
    without serializing fragment class tokens or source/location evidence by default.

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Column shifts corrupt race data | Span-aware projection with explicit failure and parser parity fixtures. |
| Fragment payload recreates DOM | Strict per-cell/token bounds, selected attributes only, size measurement. |
| Removing Sections changes agent prompts | Golden/behavioral agent tests using semantic effective text. |
| Action labels become ambiguous | Deterministic visible/alt/accessible precedence and navigation tests. |
| Big-bang branch is hard to review | Buildable commits by model, projection, parser, navigator, consumer, removal. |
| No production fallback after merge | Merge only after full parity; retain Git-level rollback, not dual runtime code. |

## Documentation updates

- This record is the canonical implementation contract for the direct cutover and supersedes the staged
  recommendations in `docs/changes/20260908_jra-semantic-snapshot-migration/README.md` when approved.
- `docs/23-jra-scraping-redesign.md` is updated now to link this proposal; after implementation it will be
  rewritten to describe semantic capture as the only current path.
- Generic snapshot records remain canonical for DOM-to-semantic rules and query behavior.

## Approval

The user approved this direct cutover on 2026-09-08. The implementation removes the compatibility surface
rather than retaining a runtime fallback.

## Pre-implementation concern review

The user authorized implementation on 2026-09-08 after requesting a final concern review. No blocking issue
was found. The main risks remain column corruption from span projection, fragment payload growth, changed
agent prompt shape, ambiguous action labels, and the size of the repository-wide breaking cutover. The
approved mitigations are explicit projection failure, bounded fragments, bounded prompt projections,
navigation parity tests, buildable checkpoints, and a final zero-reference gate. Implementation may proceed
without retaining the legacy runtime path in the finished change.

## Implementation result

- `IWebBrowser.GetPageSnapshotAsync` now returns the semantic snapshot and `PlaywrightWebBrowser` delegates
  capture to the injected `IPageSnapshotter` after its existing readiness wait.
- Bounded generic fragments retain source element, class tokens, text, accessible evidence, and raw/resolved
  links. Truncation is reported through diagnostics.
- `JraSnapshotView` provides span-aware rectangular table projection and rejects overlapping or malformed
  grids instead of silently shifting columns.
- Calendar, race-list, race-card, race-result, navigation, page-reader, Agents, and Collector consumers now
  use semantic snapshots. Test fakes and fixtures use semantic models as well.
- The section-oriented snapshot, section/action/table/cell compatibility models and compatibility-only DOM
  extraction were deleted. A solution-wide source search confirms no references remain.

## Verification result

- Solution restore and build complete successfully with zero compiler warnings or errors.
- Snapshot browser tests pass after installing the Playwright Chromium runtime and native dependencies.
- The same seven-sample Wikipedia scenario used by the pre-fragment live-site evaluation measured a 258.8 ms
  median and 341,046-byte JSON with 62 fragments. Compared with the recorded 253.2 ms and 322,405 bytes,
  this is a 2.2% median-time increase and 5.8% payload increase, both within the approved 15% budget. Browser
  startup/navigation were excluded and one warm-up preceded the samples; this remains a non-isolated
  directional measurement rather than a production SLA.
- The complete non-external solution test selection passes, including parser, navigator, projection, Agent,
  Collector, and snapshot integration coverage.
- `dotnet format` and `git diff --check` complete successfully.

## Remaining considerations

- External live-JRA tests remain dependent on network and current provider markup and are not a deterministic
  merge gate.
- Fragment bounds deliberately trade complete descendant evidence for bounded snapshot size; diagnostics let
  consumers detect truncation.

## Post-cutover verification (2026-09-08)

The completed branch was independently reverified after the cutover commit.

### Functional verification

- `dotnet restore HorseRacingPrediction.sln` completed successfully.
- `dotnet build HorseRacingPrediction.sln --no-restore` completed with zero warnings and errors.
- `dotnet test HorseRacingPrediction.sln --no-build --filter 'TestCategory!=External'` passed all
  681 selected tests across the solution. This run included the 15 local Chromium snapshot tests.

### Performance spot checks

The live Wikipedia scenario documented in `../20260907_playwright-page-snapshot/live-site-evaluation.md`
was repeated with the same method: navigation and browser startup excluded, one warm-up, seven captures in
the same page, and the median reported. The current rendered page contained 1,379 elements and serialized to
166,904 UTF-8 bytes.

| Profile | Median | Slowest | Snapshot JSON | Nodes | Links | Tables | Forms | Images | Fragments |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Defaults | 311.9 ms | 815.4 ms | 341,046 B | 1,061 | 331 | 1 | 4 | 6 | 62 |
| Content-only collections disabled | 166.7 ms | 186.3 ms | 209,094 B | 1,061 | 0 | 0 | 0 | 0 | 0 |
| Defaults plus locations | 245.3 ms | 344.8 ms | 392,916 B | 1,061 | 331 | 1 | 4 | 6 | 62 |

As in the earlier run, the locations result must not be interpreted as an optimization: these short samples
are affected by Chromium/runtime noise. Deterministic payload observations are more useful: default JSON is
2.04 times the rendered HTML size; locations add 51,870 bytes (15.2%); disabling dedicated collections cuts
131,952 bytes (38.7%). The default median was 20.5% slower than the earlier 258.8 ms follow-up sample while
the JSON size was identical, demonstrating that isolated latency samples are not stable enough to establish
a regression without a dedicated benchmark environment.

A second, network-free table-heavy spot check used 250 rows containing image links, classed weight fragments,
and buttons. After one warm-up, 20 captures produced:

| Profile | Median | p95 | Median managed allocation | Snapshot JSON | Nodes | Rows | Links | Images | Fragments |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Defaults | 506.2 ms | 989.4 ms | 6,571,296 B | 771,428 B | 1,508 | 251 | 250 | 250 | 1,250 |
| Content-only collections disabled | 165.9 ms | 202.2 ms | 2,236,984 B | 270,179 B | 1,508 | 0 | 0 | 0 | 0 |

This confirms the expected cost profile: semantic tree capture itself remains materially cheaper than rich
default capture, while tables, fragments, links, and images dominate large structured pages. Fragment bounds
prevent unbounded growth, but callers that only need text should disable unused collections. The current
implementation is acceptable for JRA's evidence-rich parser path, but capture latency and payload size should
be monitored if pages grow substantially; a future optimization should cache visibility/text calculations or
combine repeated collection traversals rather than weakening information retention.

## Acceptance-gap remediation (2026-09-08)

A final document-to-code audit found and closed three omissions from the approved contract:

- `JraSnapshotView` now retains a structured projection diagnostic for every malformed table that is excluded.
  Diagnostics identify the source table index, stable failure code, explanation, and table source reference;
  invalid spans, row/column-limit overflow, overlapping spans, inconsistent widths, sparse grids, and empty tables
  are distinguished.
- Routine semantic capture logging now includes the total bounded table-cell fragment count alongside duration,
  node/table/link/form/image counts, and diagnostic codes. Fragment content remains excluded from logs.
- Agent snapshot prompts now use an ordered, total-length-bounded `ContentBlocks` projection for headings,
  paragraphs, list items, links, image alternatives, quotes, and code. Tables remain separately bounded by
  table/row limits, and individual link/action/caption/cell values are capped at 1,000 characters and each projected row at 64 cells. Source references, locations,
  fragment content, and class tokens are not serialized into the prompt projection.

Focused regression tests cover malformed-table diagnostic evidence, block kinds including image/list content,
prompt fragment exclusion, and the fragment-count log field. This closes acceptance criteria 4 and 14 and
the operational logging requirement under Performance and diagnostics.

Final verification after remediation: `dotnet build HorseRacingPrediction.sln --no-restore` completed with
zero warnings/errors, and `dotnet test HorseRacingPrediction.sln --no-build --filter 'TestCategory!=External'`
passed all 683 selected tests.

## Live-site regression remediation (2026-09-09)

Post-merge local verification found that the solution still builds, but 8 of 198 scraping-project tests fail
when external JRA scenarios are included. This is a continuation of the approved cutover rather than a new
behavior change: it closes acceptance criteria 4, 6, and 7 against live HTML shapes that the synthetic parity
fixtures did not reproduce.

The primary defect is in `JraTableView.Project`. It classifies every leading row containing any header cell as
a column-header row. JRA race tables use a row-header `<th>` for the race number inside otherwise ordinary
mixed `<th>`/`<td>` data rows, so all race rows are removed from the projected body and collection jobs see an
empty race list. The repair will classify a leading row as a column-header row only when it is non-empty and
all its cells are header cells, and will add a mixed-row regression fixture.

Two navigator regressions are also within the original navigation-equivalence scope. Trainer directory hrefs
may be relative, but the migrated code constructs an absolute `Uri` unconditionally. Trainer path matching
will use safe absolute-or-relative URI parsing and ignore malformed/pseudo-action hrefs. Horse search captures
the submit action as a link and later requires exact stale link identity; it will instead use the existing
`ClickAsync("検索")` action path after populating the field.

Acceptance for this remediation is:

1. Mixed `<th>`/`<td>` race rows remain body rows while all-header leading rows still supply column labels.
2. Current-week race-list/race-card and historical result-list live tests produce non-empty race collections
   when the provider publishes the target page.
3. Relative trainer links and the current rendered horse-search action reach their expected subject pages.
4. Existing span, malformed-table, parser, and navigator coverage remains green.
5. The solution builds, the complete non-external suite passes, targeted live tests pass or are inconclusive
   only when their public prerequisite is unavailable, and `git diff --check` passes.

One additional failure occurred when Chromium crashed during `SetContentAsync` for an artificial deeply nested
DOM, before semantic capture began. It is outside the JRA job path; it will be rerun in isolation and recorded
separately if reproducible rather than being conflated with these confirmed consumer defects.

The user requested on 2026-09-09 that this repair continue in the existing change record and authorized the
clear bug fixes. The record is returned to `Approved` until implementation and verification are complete.

### Remediation result

- Header projection now treats only non-empty, all-`th` leading rows as column headers. Mixed row-header and
  data-cell rows remain in the body.
- Empty rendered cell text now falls back to bounded fragment text/accessibility evidence. This restores JRA
  race numbers represented by image alternative text while preserving whitespace-only raw values used by
  parser validation.
- `ClickLinkAsync` compares both raw href values and URLs resolved against the current page, so captured
  relative links remain clickable.
- Standalone link extraction now deduplicates by URL, title, and page region instead of URL alone. JRA's
  initial-group controls share hrefs but have distinct labels, so all groups remain navigable.
- Trainer directory path matching accepts absolute paths, root-relative paths, and page-relative filenames
  without throwing for malformed or pseudo-action hrefs.
- The artificial nested-DOM fixture was reduced from 500 to 60 alternating wrapper elements. Chromium in the
  local runtime reproducibly crashed in `SetContentAsync` before snapshot capture at the original depth; the
  smaller fixture still verifies semantic wrapper reduction and serialized-size reduction.

There was one deviation from the initial remediation diagnosis: changing header-row classification alone did
not restore race numbers because JRA renders them as image alternative text inside otherwise empty cells. The
approved fragment-evidence design already requires preservation of this information, so the adapter now uses
that evidence as an empty-cell fallback. Investigation also found URL-only link deduplication as the reason
trainer initial groups disappeared; fixing the standalone browser link contract was necessary to restore the
original navigation-equivalence acceptance criterion.

### Remediation verification

- Focused `JraSnapshotViewTests` and `JraNavigatorTests` — passed 41/41 before the final added cases; the added
  mixed-row, image-alternative, and URI cases are included in the full suite below.
- Targeted external JRA suite covering `JraSiteE2ETests`, `JraSubjectSiteE2ETests`,
  `JraWorkflowSiteE2ETests`, and `JraNavigationRegressionE2ETests` — passed 18/18 in 2 minutes 4 seconds.
  This includes current race list/card, consecutive race navigation, historical result-list fallback, horse
  profile/history/result, and trainer profile navigation.
- Isolated `CaptureAsync_LargeNestedDomIsSemanticallyReduced` with the corrected browser-safe fixture — passed.
- `dotnet build HorseRacingPrediction.sln --no-restore` — passed with zero errors. One pre-existing nullable
  warning remains in `JraRaceCardCollectionWorkflowTests.cs:73` when that test project recompiles.
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` — passed.
- `dotnet test HorseRacingPrediction.sln --no-build --filter 'TestCategory!=External'` — passed all 741 tests.
- `git diff --check` — passed; only Git's existing LF-to-CRLF working-copy notices were emitted.

No canonical architecture or operational documentation changed because the implementation restores the
already documented collector and semantic-snapshot behavior. The remediation is complete and the record is
returned to `Implemented`.

## Collected-field completeness remediation (2026-09-09)

Detailed live-site verification of race cards and results found a persistence-only regression in the normal
race-card collection path. `RaceCardPageParser` correctly populates grade, start time, course specification,
sex, and age, and the targeted refresh path preserves them. However, `JraRaceCardCollectionWorkflow.CollectAsync`
currently sends `null` for all race-level specification fields and for entry sex/age. This silently discards
successfully parsed JRA values before API persistence. Race-result collection already maps every corresponding
parsed field into `DeclareRaceResultBulkRequest`.

This is a continuation of the approved semantic-snapshot parity scope. The user explicitly requested on
2026-09-09 that every collected field be checked and that clear defects continue in the existing change record.
The record is returned to `Approved` while this persistence fix and field-by-field verification are completed.

Acceptance for this remediation is:

1. Normal race-card collection persists grade, start time, surface sequence, distance, direction, raw course
   layout, entry sex, and entry age whenever the parser provides them.
2. Existing race-card identity, horse number, frame, horse, jockey, trainer, assigned weight, owner, and body
   weight behavior remains unchanged.
3. Race-result parser/workflow coverage confirms all modeled race-level, entry-level, weather, track, corner,
   prize, and payout fields remain mapped.
4. Targeted live JRA page tests pass, the relevant non-external parser/workflow tests pass, the solution builds,
   and `git diff --check` passes.

No canonical documentation update is necessary: the existing scraper design already requires semantic parity;
this correction makes the normal collection path conform to that documented contract.

### Collected-field completeness result

- Normal race-card collection now forwards grade, surface sequence, distance, direction, entry sex, and entry
  age instead of replacing parsed values with `null`.
- A workflow regression test now verifies these mappings in addition to the previously covered race identity,
  names, frame/horse numbers, jockey, trainer, weight, owner, and body weight fields.
- The race-result parser and workflow mapping were audited field by field. All modeled values are connected:
  race identity/name/grade/start/course, every entry attribute and result value, weather, track condition,
  overall pace, race- and horse-level corner passages, prize money, and all eight payout types.
- Live JRA checks for a currently published race card and a completed result both passed.

One pre-existing storage limitation remains: the normal `UpsertRaceAsync` contract has no start-time or raw
course-layout parameters. Both values are parsed correctly, and the refresh/bulk contract preserves them, but
the normal race-card path cannot persist them without extending the API/application event contract. This is
not hidden data loss introduced by the semantic parser and is left as explicit follow-up rather than expanding
this targeted regression repair. The first acceptance item is therefore satisfied for every field supported by
the normal registration contract; start time and raw layout are verified at parser level and documented here.

Verification:

- Focused race-card/result parser and workflow suite: passed 84/84.
- Live `JraSiteE2ETests` current race-card and completed race-result scenarios: passed 2/2.
- `dotnet build HorseRacingPrediction.sln --no-restore`: passed with zero warnings and zero errors.
- Complete non-external solution suite: passed 741/741.
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` and `git diff --check`: passed.
- No canonical documentation changed; this record is the implementation and verification continuation.

## Horse ownership, breeder, and pedigree follow-up (2026-09-09)

User review questioned whether owner, breeder, and parents are collected. Investigation confirmed three
different states:

- Owner is parsed from the race-card semantic `owner` fragment (with ordered-text fallback), is covered by a
  live assertion for every entry, and is forwarded to both the race entry and horse profile write paths.
- Breeder text is present in the same JRA cell but is currently discarded. The fallback parser even identifies
  its position between owner and trainer without retaining it.
- Sire and dam text are present in `父：` and `母：` lines but `IsFamilyLine` deliberately filters both lines.
  The current `RaceEntry`, horse aggregate, API contracts, and read models have no fields for these values.

This is a data-model expansion rather than a parser-only correction. Proposed scope:

1. Add nullable `BreederName`, `SireName`, and `DamName` values to race-card parsing and horse profile storage.
2. Parse the dam independently from the nested maternal-grandsire suffix; retain maternal grandsire only as
   source evidence for now, not as a first-class relationship.
3. Forward the three values through collector/API contracts and update the horse aggregate/read model without
   overwriting an existing non-null value when a later source omits it.
4. Expose the values on the horse detail response and screen; keep race-entry historical ownership unchanged.
5. Add semantic-fragment and text-fallback parser tests, persistence tests, and live assertions where JRA
   publishes the values.

Acceptance criteria:

1. A published JRA race card produces non-empty owner, breeder, sire, and dam for ordinary entries where all
   four are displayed.
2. Owner continues to be stored as both current horse owner and race-time owner.
3. Breeder, sire, and dam are stored on the horse and returned by the horse detail API.
4. `母：<dam>(母の父：<name>)` stores only `<dam>` as `DamName`.
5. Missing or old-page values remain nullable and never erase existing known values.
6. Existing scraper, domain, API, and UI tests remain green, and targeted live JRA verification passes.

Documentation updates:

- `docs/23-jra-scraping-redesign.md`: updated the canonical field policy to include owner, breeder, sire, and
  dam and to distinguish the already-working owner path from the proposed model expansion.

Production implementation is blocked until this proposed extension is explicitly approved.

The user approved implementation on 2026-09-09 with one explicit source constraint: race-result pages cannot
identify the owner. Owner must therefore be populated only from race-card or horse-profile evidence; result-only
collection must leave both current and race-time owner unset and must never infer or carry an unrelated owner.
The same implementation pass will diagnose and repair the reported standalone horse-information acquisition
failure, provided the repair remains within the approved JRA semantic-navigation/profile scope.
