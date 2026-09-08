# Playwright Page Snapshot API

- Status: Implemented
- Owner: Scraping team
- Created: 2026-09-07
- Updated: 2026-09-08

## Context

The scraping project already exposes a `PageSnapshot` assembled by `IWebBrowser` / `PlaywrightWebBrowser`. That model is a mutable, section-oriented compatibility model, and its extraction path performs multiple locator calls for structural blocks and their links, tables, forms, and images. This is useful to existing JRA parsers and agent workflows, but it does not provide the information-preserving semantic tree, metadata, source references, or direct `IPage` capture contract required for a reusable rendered-DOM snapshot.

This change introduces a generic semantic capture boundary:

```text
Microsoft.Playwright.IPage
    -> IPageSnapshotter.CaptureAsync
    -> immutable PageSnapshot
    -> provider/domain parser
```

The snapshotter observes the DOM exactly when called. Navigation, authentication, interaction, and readiness waits remain caller responsibilities.

## Goals

- Capture the currently rendered DOM from an `IPage` into an information-preserving semantic tree.
- Keep generic web concepts such as headings, links, tables, definition lists, forms, images, metadata, ARIA hints, and source references.
- Remove implementation noise and meaningless `div` / `span` nesting without repeating descendant text on ancestors.
- Extract the complete browser-side DTO in one `EvaluateAsync` round trip under normal operation.
- Make snapshot data immutable and straightforward for deterministic provider-specific parsers to consume.
- Honor cancellation before, during, and after the Playwright evaluation.

## Non-goals

- Wrapping or replacing Playwright navigation, locator, script execution, form interaction, login, or waiting APIs.
- Recognizing JRA races, horses, products, prices, companies, weather, or any other provider/domain concept.
- Article extraction, summarization, Markdown conversion, OCR, screenshot analysis, visual AI, API discovery, pagination, CAPTCHA handling, or login automation.
- Inferring key/value pairs from arbitrary visual `div` layouts or interpreting `data-*` attributes.
- Adding speculative filter or classifier interfaces in the initial release.

## Public API

The API will live in `HorseRacingPrediction.Scraping.Browser.Snapshots`, separating it from the existing section-based compatibility types during migration:

```csharp
public interface IPageSnapshotter
{
    Task<PageSnapshot> CaptureAsync(
        IPage page,
        PageSnapshotOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class PlaywrightPageSnapshotter : IPageSnapshotter
{
    public Task<PageSnapshot> CaptureAsync(
        IPage page,
        PageSnapshotOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

The namespace choice avoids a source-breaking replacement of `HorseRacingPrediction.Scraping.Browser.PageSnapshot`. Callers opt into the semantic API explicitly. A later, separately approved migration may adapt `IWebBrowser` and existing JRA/agent consumers; this change does not expose or wrap the private `IPage` owned by `PlaywrightWebBrowser`.

## Snapshot model

The immutable model consists of:

- `PageSnapshot`: absolute page URL, optional title, semantic root, metadata, key/value pairs, tables, links, images, forms, and diagnostics.
- `PageContentNode`: `PageContentKind`, own text, optional heading level, role, accessible name, location, source reference, and read-only children.
- `PageMetadataSnapshot`: description, canonical URL, language, meta-name/property map, and valid JSON-LD values.
- Dedicated records for table rows/cells, links, images, forms/controls, definition-list key/value pairs, locations, source references, JSON-LD, and diagnostics.
- `PageSnapshotOptions` and `PageSnapshotPruningLevel` (`None`, `Safe`, `Aggressive`).

Collections returned by the public model will be immutable snapshots rather than writable `List<T>` instances. JSON-LD values will be cloned `JsonElement` values so they do not depend on a disposed `JsonDocument`.

`PageContentKind` initially includes `Document`, `Section`, `Navigation`, `Article`, `Aside`, `Heading`, `Paragraph`, `Text`, `List`, `ListItem`, `DefinitionList`, `Link`, `Image`, `Quote`, `Code`, and `Button`. Tables and forms remain represented in their dedicated collections; a section-level semantic node may retain surrounding context without recreating every HTML table/form descendant.

## DOM to semantic tree rules

1. Traverse the live `document` in browser context; do not fetch and reparse raw HTML.
2. Build text only from an element's immediate text-node children. Normalize whitespace only when requested, preserving punctuation and displayed values.
3. Map semantic HTML and explicit ARIA roles to the small semantic-kind set rather than mapping every tag.
4. Treat semantically empty `div` and `span` elements as transparent and splice their meaningful children into the parent. A leaf wrapper containing only own text becomes one `Text` node.
5. Preserve grouping for landmarks/sections, headings, semantic containers, and elements with a meaningful explicit role or accessible name.
6. Exclude `script`, `style`, `noscript`, `template`, and `canvas` from the content tree. JSON-LD scripts are read only by structured-data extraction.
7. Exclude ordinary SVG descendants, but preserve an SVG as an image-like node when it supplies a non-empty accessible name through `aria-label`, `aria-labelledby`, `title`, or an applicable role.
8. Safe hidden detection excludes the `hidden` attribute and computed `display: none` / `visibility: hidden`; it does not exclude solely because of zero dimensions, off-viewport position, or opacity.
9. Accessible name uses a bounded DOM-based approximation: `aria-label`, referenced `aria-labelledby` text, associated label text for controls, `alt` for images, and appropriate visible/own text fallback. It does not depend on Playwright ARIA YAML.
10. Source references contain lowercase tag name, non-empty element ID, and a cheap locator hint (`#id` when safely escapable, otherwise tag plus a stable name/type attribute where available). They do not build full ancestor selectors.

## Structured extraction

- **Tables:** preserve DOM row order, cell order, `th`/`td`, caption, and normalized positive `rowspan`/`colspan` (default 1). No dictionary conversion or expanded grid inference occurs.
- **Key/value pairs:** pair explicit `dt` elements with their following `dd` siblings until the next `dt`; multiple values for one term are joined in displayed order without domain interpretation.
- **Links:** preserve displayed text, resolved `href` where URI parsing succeeds, raw `href`, relation, title, accessible name, and source reference. Absolute, relative, fragment, `mailto:`, and `javascript:` values are not silently dropped. `Url` is nullable for invalid values, while `RawHref` and a diagnostic preserve evidence.
- **Images:** preserve resolved source where valid, raw source, alt, title, accessible name, and source reference; no binary is fetched.
- **Metadata:** preserve title, language, canonical URL when valid, and non-empty meta `name`/`property` values. Duplicate meta keys use the first value and emit a diagnostic for conflicting later values.
- **JSON-LD:** parse each non-empty `application/ld+json` script independently. Invalid JSON is skipped and produces a warning diagnostic without failing capture.
- **Forms:** preserve name, resolved/raw action, normalized method, source, and controls for input/select/textarea/button. Controls include name, type, label, value, placeholder, accessible name, selected option display values where applicable, disabled, and required state. Password control values are always `null`; file values are also omitted to avoid leaking local paths.

## Pruning and options

- `None` retains visible and hidden semantic content and transparent wrapper groups where useful for fidelity, while still omitting executable/style implementation noise. `IncludeHiddenContent = false` still applies hidden filtering; setting it to `true` is the explicit override.
- `Safe` is the default and applies hidden filtering, empty-node removal, direct-text deduplication, and meaningless wrapper flattening. It never removes content merely because it is in `header`, `footer`, `nav`, or `aside`.
- `Aggressive` additionally drops clearly identified navigation/footer landmarks and nodes whose explicit role or conventional identifiers indicate cookie banners, advertising, social links, or related-content chrome. Documentation and XML comments will warn that this mode can lose information. The initial heuristics remain intentionally narrow.
- `IncludeMetadata`, `IncludeStructuredData`, `IncludeLinks`, `IncludeForms`, and a corresponding `IncludeImages` option gate dedicated collections. Semantic link/image/button nodes remain available unless pruning removes their content; collection options do not erase the content tree.
- `IncludeElementLocations` calls `getBoundingClientRect()` during the same traversal and is off by default.
- `NormalizeWhitespace` defaults to true.

When metadata or a structured collection is disabled, its public property remains a non-null empty snapshot/collection. This keeps consumers null-safe and makes option behavior deterministic.

## Error handling and cancellation

- A failure to evaluate the rendered DOM is a capture failure and propagates.
- Recoverable item failures (invalid URL, invalid JSON-LD, malformed span values, inaccessible ARIA reference) add `PageSnapshotDiagnostic` entries and preserve other data.
- `CaptureAsync` validates `page`, checks cancellation before evaluation, awaits the single Playwright evaluation with `WaitAsync(cancellationToken)`, and checks cancellation before conversion returns. Cancellation cannot stop JavaScript already executing in the browser, but the caller stops awaiting it promptly.

## Performance policy

- One page-level `EvaluateAsync` returns a simple JSON-serializable browser DTO containing the tree and enabled structured collections.
- No locator is created per DOM node, and C# does not retrieve or parse the full HTML.
- Computed style is requested only when hidden filtering is active; bounding rectangles are requested only when locations are enabled.
- Accessible-name and locator-hint logic is bounded and does not recursively generate selectors.
- DTO-to-domain conversion and JSON-LD parsing occur in .NET after the single browser round trip.
- A deterministic large nested local page test records capture duration and compares raw HTML versus serialized snapshot size as a non-flaky diagnostic; it does not impose a machine-specific timing threshold.

## Query helpers

The initial API adds only:

```csharp
IEnumerable<PageContentNode> Descendants(this PageContentNode node)
IEnumerable<PageContentNode> FindHeadings(this PageSnapshot snapshot)
PageTableSnapshot? FindTableByCaption(this PageSnapshot snapshot, string caption)
```

Traversal order is depth-first document order. Caption matching is ordinal, with an overload or comparer deferred until a demonstrated need.

## Compatibility and delivery

The current section snapshot and `IWebBrowser.GetPageSnapshotAsync` stay unchanged in this change. This prevents unrelated source breaks in JRA parsers, agent prompts, and test fakes. The new direct-`IPage` API is additive and can be adopted incrementally. No dependency-injection registration is required because `PlaywrightPageSnapshotter` is stateless and directly constructible; DI registration may be added later when a production consumer exists.

Implementation checkpoints after approval:

1. Add immutable models, options, diagnostics, query helpers, and model-only unit tests.
2. Add the one-evaluation snapshotter and browser DTO conversion.
3. Add local Playwright integration tests for semantic traversal and all structured collections.
4. Add cancellation/options/aggressive-pruning and large-DOM size/performance coverage.
5. Update this record and canonical scraping documentation with verification and final status.

## Pre-implementation concern review

The user authorized implementation on 2026-09-07 after requesting a final concern review. The review found no blocking issue. The following non-blocking risks are controlled by the approved design:

- The repository already has a `Browser.PageSnapshot`; the semantic API uses the nested `Browser.Snapshots` namespace and leaves existing consumers unchanged.
- Browser-evaluated JavaScript cannot be forcibly interrupted after dispatch; `.WaitAsync(cancellationToken)` gives the public operation prompt cooperative cancellation without pretending to terminate browser work.
- DOM-based accessible names cannot fully reproduce the browser accessibility tree; the bounded fallback order is documented and covered by representative tests.
- `javascript:` and malformed links cannot always be represented as a usable `Uri`; raw values and diagnostics prevent silent information loss.
- Aggressive pruning is inherently lossy, so it remains opt-in with narrow, testable heuristics.
- Playwright integration tests require an installed Chromium binary; the repository's existing Playwright package and local-page pattern will be used, and any unavailable-browser limitation will be recorded separately from unit-test results.

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: links the canonical description of the existing browser snapshot to the implemented additive semantic API and records that both contracts coexist without a breaking replacement.
- [`live-site-evaluation.md`](live-site-evaluation.md): records a real public-page execution, functional observations, performance measurements, and implementation-level cost analysis.
- No user-facing operations document changes are needed: the API does not alter collection workflows, navigation, deployment, authentication, or persisted data.

## Alternatives considered

### Replace the existing `Browser.PageSnapshot` in place

Rejected for the initial release. Existing parsers, agents, fake browsers, and tests depend on its section/list shape. Replacing it would combine semantic capture with a broad consumer migration and make independent review difficult.

### Add semantic members to the existing mutable model

Rejected. It would create one type with two extraction semantics, optional partially populated properties, and conflicting immutability expectations.

### Capture `page.ContentAsync()` and parse in .NET

Rejected because it transfers the full DOM serialization, misses the intended rendered-state computations, and prevents efficient computed visibility/location handling.

### Use the Playwright ARIA snapshot as the primary representation

Rejected because href/source/form/table DOM attributes would be lost and the implementation would couple to the ARIA YAML representation.

## Acceptance criteria

1. `IPageSnapshotter.CaptureAsync` accepts `Microsoft.Playwright.IPage` directly and performs no navigation or readiness wait.
2. Basic `main/h1/p` markup produces a document/section tree with one heading and one paragraph in document order.
3. Nested meaningless `div/span/span` markup around `Hello` produces a single meaningful text node, not three wrapper levels.
4. Parent elements do not repeat text owned by heading, paragraph, link, or other descendants.
5. Safe mode excludes `hidden`, `display:none`, and `visibility:hidden` content but retains zero-size, off-viewport, and opacity-zero content; `IncludeHiddenContent` includes the former.
6. Safe mode retains meaningful header, footer, navigation, and aside content. Aggressive mode removes only documented narrow boilerplate heuristics.
7. Tables preserve caption, row/cell order, headers, and row/column spans.
8. Definition lists yield explicit key/value pairs without arbitrary-layout inference.
9. Links resolve relative URLs against the document base URL and preserve fragments, non-HTTP schemes, invalid raw hrefs, accessibility/title/relation, and source evidence.
10. Metadata includes title, language, description/canonical/meta values, OpenGraph/Twitter entries, and valid JSON-LD; malformed JSON-LD does not fail capture and emits a diagnostic.
11. Forms preserve required fields and labels while password and file values are never captured.
12. Images preserve URL/alt/title/accessibility/source information without fetching binary content.
13. Role, accessible-name approximation, source reference, and optional element location are available on applicable semantic nodes.
14. Every include/normalization/pruning option has an observable integration test.
15. A DOM element injected with JavaScript immediately before capture appears in the snapshot.
16. Cancellation before capture and while awaiting evaluation yields `OperationCanceledException` without being ignored.
17. The normal capture path uses one browser evaluation and does not use per-node locator calls or `ContentAsync()`.
18. A generated large nested page test demonstrates wrapper reduction and reports serialized raw HTML and snapshot sizes without relying on an external website.
19. The scraping project builds, all new snapshot tests pass, and existing scraping tests remain green.
20. Public XML documentation identifies Aggressive pruning's information-loss risk and the snapshotter's non-responsibility for navigation/waits.

## Verification record

Implementation completed on 2026-09-07:

- Added the additive immutable semantic model and query helpers under `Browser/Snapshots`.
- Added `PlaywrightPageSnapshotter`, using one page-level `EvaluateAsync` followed by .NET DTO conversion and JSON-LD parsing.
- Added twelve local Chromium integration tests covering semantic compression, direct-text ownership/order, visibility, all pruning levels, structured collections, metadata, invalid JSON-LD, sensitive form values, locations, options, cancellation, dynamic DOM, and large nested DOM reduction.
- `dotnet build src/HorseRacingPrediction.Scraping/HorseRacingPrediction.Scraping.csproj --no-restore` — passed with zero warnings and errors.
- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --no-restore --filter "FullyQualifiedName~PlaywrightPageSnapshotterTests"` — passed 12/12.
- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --no-build --filter "TestCategory!=External"` — passed 158/158.
- The initial unfiltered test invocation was stopped because it included the explicitly external JRA site suites; the repository-documented non-external filter was then used for deterministic regression verification.

## Post-implementation review

The implementation was reviewed again on 2026-09-08. Three correctness issues were found and corrected:

- Mixed text and inline elements were previously grouped as parent own-text followed by all child nodes, which could change `before <strong>middle</strong> after` into `before after middle`. Direct text nodes are now emitted at their original child-node positions whenever an element has element children.
- Aggressive and hidden pruning originally applied fully to the semantic tree but not to every dedicated collection. Definition lists, table rows/cells, links, images, forms, and controls now use the same ancestor-aware pruning decision.
- Metadata keys differing only by case could survive browser collection and then collide in the case-insensitive .NET dictionary. Browser extraction now deduplicates keys case-insensitively, retains the first value, and emits a conflict diagnostic.

The review also tightened `NormalizeWhitespace = false`: formatting is preserved for meaningful text, while whitespace-only DOM nodes remain excluded as empty nodes.

## Existing JRA scraper impact review

The existing JRA scraping path was reviewed on 2026-09-08 after implementation. The new API has no current runtime or source impact on that path:

- `JraPageReader` and `JraNavigator` still depend on `IWebBrowser.GetPageSnapshotAsync` and therefore continue to receive `HorseRacingPrediction.Scraping.Browser.PageSnapshot`, the existing section-oriented compatibility model.
- All JRA parsers still accept the compatibility `PageSnapshot`; no parser, navigator, session, collector workflow, or test fake imports `HorseRacingPrediction.Scraping.Browser.Snapshots` or constructs `PlaywrightPageSnapshotter`.
- `PlaywrightWebBrowser.GetPageSnapshotAsync` and its settling, section extraction, link-limit, and logging behavior were not modified. Consequently this addition does not change JRA page readiness, extraction output, parser selection, navigation decisions, or request volume.
- The scraping project already referenced `Microsoft.Playwright`; the additive implementation introduces no new package or project dependency for JRA consumers.
- The two `PageSnapshot` types have the same simple name but live in different namespaces. Existing source continues to compile because it imports only `HorseRacingPrediction.Scraping.Browser`. A future migration must use an alias or fully qualified name while both contracts coexist, rather than importing both namespaces and relying on the simple name.
- The new snapshotter requires an `IPage`, while `PlaywrightWebBrowser` intentionally keeps its page private and `IWebBrowser` does not expose it. Adopting the semantic snapshot in `JraPageReader` is therefore not a drop-in type substitution: it needs a separately approved integration boundary or adapter plus parser migration.
- The semantic model does not carry the compatibility model's `Sections`, `Actions`, table fragment metadata, or mutable aggregate lists. Existing JRA parsers rely on those shapes, including table fragments used to distinguish provider-specific cell content, so automatic model conversion would risk information loss. Migration should be parser-by-parser and retain regression fixtures for calendar, race-list, race-card, race-result, and navigation behavior.

Regression verification built the whole solution and ran the non-external suite excluding only `PlaywrightPageSnapshotterTests`, including the existing JRA parser, navigator, workflow, collector, and agent tests. All 665 selected tests passed, so no compatibility regression was observed. The semantic integration tests could not launch Chromium in this container because the host lacks `libatk-1.0.so.0`; this does not affect the existing-JRA-path result. External live-JRA tests remain intentionally excluded from deterministic verification because their result depends on the live site and network state.

## Deviations and follow-up

- No material design deviations were required.
- The browser argument is passed as a string-keyed dictionary because Playwright preserves dictionary keys but serializes CLR object property names without the lower-camel transformation expected by the JavaScript DTO.
- Migrating `IWebBrowser.GetPageSnapshotAsync` and existing consumers to this semantic API is intentionally deferred to a separately reviewable change.
- Before such a migration, define how an `IPage` capture capability is supplied without exposing Playwright through the existing `IWebBrowser` abstraction, and map every compatibility-only structure consumed by JRA parsers. The impact review found no reason to alter the existing JRA scraper in this change.
