# JRA Semantic Snapshot Cutover

- Status: Proposed
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

## Approval required

This record is `Proposed`. Approval authorizes the breaking in-repository cutover, generic bounded cell
fragments, removal of compatibility types and extraction, and migration of every listed first-party consumer.
No production code changes are made before that approval.

The design review found no remaining technical blocker to starting the cutover. The intentionally breaking
scope, temporary build-only seam, fragment bounds, span projection failure behavior, and agent prompt
projection are now explicit parts of the approval request.
