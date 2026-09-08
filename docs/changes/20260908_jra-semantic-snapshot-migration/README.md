# JRA Semantic Snapshot Migration

- Status: Proposed
- Owner: Scraping team
- Created: 2026-09-08
- Updated: 2026-09-08

## Context

The generic semantic `PageSnapshot` can now capture rendered DOM data with effective text and image
alternatives. The JRA implementation still consumes the section-oriented
`HorseRacingPrediction.Scraping.Browser.PageSnapshot`. This record inventories the work and risks required
to migrate JRA without conflating browser capture, parser rewrites, and navigation changes.

This is a planning record. Production migration starts only after its open decisions are resolved and the
user explicitly approves the resulting design.

## Current execution path

```text
JraNavigator / workflow
    -> JraPageReader.ReadAsync
    -> IWebBrowser.GetPageSnapshotAsync
    -> PlaywrightWebBrowser section extraction
    -> compatibility Browser.PageSnapshot
    -> IJraPageParser.CanParse / Parse
    -> JraCalendarPage | JraRaceListPage | JraRaceCardPage | JraRaceResultPage
```

Four parsers implement `IJraPageParser`: calendar, race list, race card, and race result. `JraNavigator`
also reads compatibility snapshots directly to find date, course, and race-number click candidates.

## Migration blockers

### 1. Capture boundary

`PlaywrightPageSnapshotter` accepts `IPage`, but `PlaywrightWebBrowser` owns its page privately and
`IWebBrowser` exposes only compatibility capture. `JraPageReader` therefore cannot request a semantic
snapshot today.

Recommended direction: add an additive semantic-capture capability implemented inside
`PlaywrightWebBrowser`, where its private `IPage` is available. Do not expose `IPage` through `IWebBrowser`
and do not make the snapshotter navigate or wait. Test doubles need a deterministic semantic path during
transition.

Open decision: use an additive `IWebBrowser` method or a narrow companion interface implemented by browser
sessions. The companion keeps the existing abstraction smaller; an additive method simplifies the reader.

### 2. Two `PageSnapshot` types

The compatibility and semantic models have the same simple name in different namespaces. Transitional code
must use explicit aliases such as `LegacyPageSnapshot` and `SemanticPageSnapshot`. Renaming a public type is
not necessary for staged migration and would widen the breaking-change surface.

### 3. Section context has no direct replacement

JRA code uses `Sections` and per-section `Title`, `MainText`, `Headings`, `Links`, `Actions`, `Tables`,
`Forms`, and `Images`. The semantic model has a tree and page-level collections, but does not pre-associate
a table or link with a compatibility section.

A JRA-owned adapter/query layer must centralize effective text, nearest-heading lookup, table context, and
source matching. Reconstructing legacy `Sections` is not recommended: it would retain old assumptions and
could silently invent grouping that is not present in the semantic tree.

### 4. Table shape differs materially

Compatibility tables expose separate `Headers` and body `Rows`; semantic tables preserve all rows/cells
with `IsHeader`, `RowSpan`, and `ColumnSpan`. Current parsers assume rectangular body rows and locate columns
by header index. Direct substitution would treat header rows as data and mishandle spans.

The JRA adapter needs a tested projection that identifies header rows, maps row/column spans, retains the
original cells as evidence, and fails explicitly when safe projection is impossible rather than shifting
horse or result columns.

### 5. Cell fragments are a hard information gap

Compatibility table cells retain generic class-token and link fragments. `RaceCardPageParser` reads class
tokens such as `name`, `trainer`, `weight`, and `owner`; `RaceListPageParser` reads cell links. Semantic table
cells currently contain only text/header/span values.

Before those parsers migrate, generic capture must preserve bounded cell fragments/links or a stable source
relationship from which equivalent evidence can be obtained. JRA class meanings must not enter the generic
snapshotter. Preserving generic class tokens requires a separate payload/privacy measurement because the
current semantic model intentionally omits arbitrary classes.

### 6. Actions and navigation candidates

`JraNavigator` chooses click text from compatibility `Links.Title` and `Actions.Text`. The semantic model has
links and Button nodes but no page-level action collection or interaction handle.

Navigation should continue through `IWebBrowser.ClickAsync`. A JRA query can derive candidates from semantic
Links, Buttons, roles, image alt, and effective text, but matching and ambiguity rules need regression tests
for dates, courses, race numbers, and result navigation.

### 7. Page detection and aggregate text

All four parsers use compatibility `Title`, `Headings`, `MainText`, or tables in `CanParse` and `Parse`.
Semantic `Root.GetEffectiveText()` is ordered, but Safe mode includes site chrome and is not identical to
the compatibility section aggregate.

Migrate page-detection predicates before extraction logic and compare both results. False-positive
`CanParse` is especially dangerous because parser priority selects the winner. Unknown pages and semantic
diagnostics must remain observable.

### 8. Performance and repeated capture

`JraNavigator` captures repeatedly during route selection and already logs slow compatibility capture. The
generic live-page evaluation measured material capture time and allocation. Running both full captures on
every production read would duplicate DOM work and must not become the steady state.

Dual capture is acceptable only as sampled diagnostics. Saved JRA pages need budgets for median/p95 time,
payload, managed allocation, and session stability. Element locations remain disabled unless demonstrated.

### 9. Tests are coupled to legacy constructors

Parser and navigator tests construct compatibility snapshots, sections, tables, and cell fragments directly.
Replacing every fixture at once would obscure behavioral differences and remove the legacy comparison oracle.

Add semantic fixture builders first. For each parser, apply equivalent legacy and semantic evidence to the
same domain expectations, then retire compatibility fixtures after the migrated path is stable. Live-site
tests remain supplemental because the site and network are non-deterministic.

### 10. Diagnostics, rollout, and fallback

Semantic capture can succeed with warnings. JRA must decide which diagnostics are acceptable and which make
parsing unsafe. Logs should include page kind, URL, selected table/source, diagnostics, capture duration, and
fallback reason without credentials or sensitive form values.

Rollout needs a configuration-controlled fallback until every page kind and navigation path has semantic
coverage. Fallback must be observable; silently mixing models would hide parser defects.

## Proposed architecture

```text
private IPage
    -> IPageSnapshotter
    -> SemanticPageSnapshot
    -> JRA-owned snapshot view / projections
    -> semantic IJraPageParser implementations
    -> existing domain page models
```

The JRA-owned view is a conceptual boundary, not an approved interface name. It owns provider table
selection/projection, text normalization, navigation candidates, and evidence diagnostics. It does not
navigate or move JRA knowledge into the generic snapshotter.

## Recommended delivery sequence

1. Save sanitized rendered fixtures for calendar, race list, race card, race result, payout-heavy result,
   and navigation-selection pages; measure both captures.
2. Add the selected browser capture boundary and semantic fixture builders without switching production.
3. Add bounded generic cell evidence only if fixture analysis confirms it is required; measure separately.
4. Implement the JRA snapshot view: page text, headings, link/action candidates, table projection, diagnostics.
5. Migrate calendar parsing first because it is least table-dependent.
6. Migrate race-list parsing and race-link extraction.
7. Migrate race-card parsing and its class-fragment-dependent fields.
8. Migrate race-result parsing last because it has the broadest table and payout surface.
9. Switch navigator candidates only after destination parsers are stable.
10. Shadow/sample, compare domain results and timings, enable semantic capture by default, then remove legacy
    capture in a separate cleanup.

Each parser migration must be independently buildable and revertible. Do not combine all parser rewrites or
compatibility removal into one checkpoint.

## Acceptance criteria for the future migration

1. The browser abstraction does not expose its private Playwright `IPage`.
2. Semantic fixtures for all four page kinds produce equivalent domain objects, including negative cases.
3. Table projection covers multiple header rows, spans, malformed tables, and explicit failure.
4. Horse/trainer/weight/owner, race links, payout details, and all fragment-dependent evidence remain intact.
5. Navigation candidate selection has deterministic ambiguity tests.
6. Diagnostics and fallback reasons are logged without sensitive values.
7. Production does not routinely perform both snapshot implementations for one read.
8. Saved-fixture median/p95 time, allocation, and payload budgets are defined and met.
9. Existing non-external tests and semantic parity tests pass at every parser checkpoint.
10. Legacy capture is removed only after semantic mode is the verified default and rollback is unnecessary.

## Decisions required before approval

1. Additive `IWebBrowser` method versus narrow companion semantic-capture interface.
2. Bounded generic representation for table-cell fragments/source relationships.
3. Rollout configuration and duration of legacy fallback support.
4. Permission to store sanitized JRA HTML/DOM fixtures after checking for credentials or personal data.
5. Initial performance budgets derived from those fixtures, not synthetic or Wikipedia results.

## Documentation updates

- This record is the canonical challenge inventory and proposed delivery sequence for the migration.
- `docs/23-jra-scraping-redesign.md` links here while remaining canonical for current runtime behavior.
- Generic snapshot change records remain canonical for capture semantics and consumer queries.

## Verification

Documentation-only investigation. Source inspection confirmed four parser implementations, direct snapshot
reads in `JraPageReader` and `JraNavigator`, legacy header/body assumptions, and current fragment consumers.
No production behavior changed.
