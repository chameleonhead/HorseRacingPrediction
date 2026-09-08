# Semantic Snapshot Consumer Usability

- Status: Implemented
- Owner: Scraping team
- Created: 2026-09-08
- Updated: 2026-09-08

## Context

The [live-site evaluation](../20260907_playwright-page-snapshot/live-site-evaluation.md) confirmed that the
semantic snapshot captures a useful real page, but also exposed consumer friction:

- an image-only link can have empty visible text even though its image has useful `alt` text;
- a semantic heading or other container can have no direct `Text` while its descendants carry the label;
- callers must repeatedly combine direct text, descendant text, and accessible-name fallbacks;
- caption-only table lookup is insufficient for unc captioned real-world tables; and
- the relationship between `Text` and `AccessibleName` is not obvious from the public API.

This change improves the generic snapshot's text semantics and small query surface before any JRA migration.

## Goals

- Make image `alt` text available as ordinary semantic-node text as well as image metadata.
- Give image-only links a useful text fallback without losing the distinction between visible text and an
  accessible name in the semantic tree.
- Provide one canonical effective-text helper for parser code.
- Add focused generic queries needed by real pages without creating a query DSL.
- Document precedence and deduplication rules so parser behavior is deterministic.

## Non-goals

- Adding JRA-specific concepts, selectors, or parsing rules.
- Replacing the browser's accessibility tree or claiming a complete Accessible Name computation.
- Inferring text from arbitrary image filenames, URLs, CSS backgrounds, OCR, or nearby layout.
- Treating decorative `alt=""` images as content.
- Migrating existing JRA parsers in this change.
- Renaming either of the two existing `PageSnapshot` types.

## Public behavior

### Image nodes

For `<img>` semantic nodes:

1. `Text` is the normalized non-empty `alt` attribute.
2. An absent or explicitly empty `alt` produces `Text = null`; `title` is not promoted to visible text.
3. `AccessibleName` retains the current bounded accessibility fallback and may therefore come from
   `aria-label`, `aria-labelledby`, `alt`, or `title`.
4. `PageImageSnapshot.AltText` continues to preserve normalized non-empty `alt` independently.

This makes meaningful image alternatives participate in ordinary semantic traversal while respecting
`alt=""` as an author signal that an image is decorative.

### Link collection text

`PageLinkSnapshot.Text` remains non-null and uses this precedence:

1. normalized rendered text;
2. normalized, non-empty descendant image `alt` values in document order;
3. the link's accessible-name approximation;
4. empty string.

The semantic `Link` node itself does not copy all descendant text into `Text`; its image child carries the
`alt` text. This preserves tree ownership and avoids parent/child text duplication. `AccessibleName` remains
separate evidence.

### Effective text helper

Add `GetEffectiveText(this PageContentNode node)` with deterministic precedence:

1. node `Text`, when non-empty;
2. effective text of descendants in document order, joined with one space;
3. node `AccessibleName`, when non-empty;
4. `null`.

Duplicate adjacent contributions with the same normalized value are emitted once. The helper does not
mutate the snapshot and does not perform domain normalization. This allows headings, links, buttons, and
image-backed controls to be read consistently.

### Focused queries

Add:

- `SelfAndDescendants()` for depth-first document-order traversal including the current node;
- `FindByKind(PageContentKind kind)` on `PageSnapshot`;
- a `FindTables` predicate helper so unc captioned tables can be selected by typed contents without a new DSL.

Existing `Descendants`, `FindHeadings`, and `FindTableByCaption` behavior remains compatible.

## DOM and performance policy

The browser traversal must reuse already-computed image alternative text where practical. It must not add
Playwright round trips, locator-per-node calls, network requests, layout measurements, or a second DOM walk
solely for text fallback. The default real-page snapshot size and median capture time must not regress by
more than 5% across three same-process comparison runs; any noise or deviation will be recorded.

## Alternatives considered

### Put `AccessibleName` into every node's `Text`

Rejected because an accessible name is not necessarily visible content and doing so would erase useful
provenance. Only image `alt` is promoted because HTML defines it as the image's textual alternative.

### Set parent link node `Text` from all descendants

Rejected because it recreates the parent/child duplication that the semantic tree intentionally avoids.
The flattened `PageLinkSnapshot.Text` may use a fallback for convenient link selection; the tree preserves
text ownership.

### Leave the model unchanged and add only helpers

Rejected because serialized or non-.NET consumers would still see image `alt` only in a separate collection,
and an image node would remain textless despite having an explicit textual alternative.

## Testing strategy

- Add local Playwright cases for standalone images, image-only links, mixed text/image links, decorative
  images, `aria-label` versus `alt`, whitespace normalization, and text-duplication prevention.
- Add model-only tests for effective-text precedence, descendant order, adjacent deduplication,
  `SelfAndDescendants`, kind lookup, and predicate table lookup.
- Re-run the live Wikipedia probe and record heading/link behavior plus before/after timing and size.
- Run the complete non-external solution suite.

## Acceptance criteria

1. `<img alt="Finish photo">` yields an Image node with `Text == "Finish photo"` and a matching
   `PageImageSnapshot.AltText`.
2. `<img alt="">` and `<img>` do not create semantic text from URL, filename, or title.
3. `<a href="/result"><img alt="Race result"></a>` yields a flattened link with
   `Text == "Race result"`, while the semantic Link node owns no duplicate text and its Image child does.
4. Visible link text takes precedence over descendant image alt in `PageLinkSnapshot.Text`.
5. ARIA evidence remains in `AccessibleName` and is not indiscriminately copied into semantic `Text`.
6. `GetEffectiveText` returns direct text first, otherwise ordered descendant text, otherwise accessible
   name, and does not repeat identical adjacent contributions.
7. The three new generic queries are covered by focused tests and existing helpers remain compatible.
8. Capture still uses one browser evaluation and adds no per-node Playwright calls.
9. The real-page median time and serialized size do not regress by more than 5%, subject to the documented
   repeated-run method.
10. All snapshot tests and the full non-external solution suite pass.

## Documentation updates

- This change record is the proposed contract for semantic text usability.
- On implementation, update `docs/changes/20260907_playwright-page-snapshot/README.md` and its live-site
  evaluation with the finalized text rules and verification results.
- No operational or end-user documentation changes are required because this affects a developer API only.

## Implementation result

Implemented on 2026-09-08:

- Image semantic nodes now expose normalized non-empty `alt` through `Text`; decorative empty alt and
  missing alt remain textless even when a title exists.
- Flattened links use visible text, descendant image alt, accessible name, then empty string. The semantic
  Link node still owns no copied descendant text.
- Added `GetEffectiveText`, `SelfAndDescendants`, `FindByKind`, and predicate-based `FindTables` helpers.
- Added browser integration coverage for meaningful/decorative images, image-only and mixed links, and
  text ownership, plus model-only coverage for effective-text precedence/deduplication and all new queries.

There was no material deviation from the approved design. The implementation uses descendant image lookup
only while constructing the already-enabled dedicated Link collection and adds no Playwright round trip.

## Verification

- `dotnet restore HorseRacingPrediction.sln` — passed.
- `dotnet format` for the scraping and scraping-test projects — passed.
- `dotnet build src/HorseRacingPrediction.Scraping/HorseRacingPrediction.Scraping.csproj --no-restore` —
  passed with zero warnings and errors.
- Filtered `PlaywrightPageSnapshotterTests` — passed 14/14.
- The live Wikipedia page retained 1,060 semantic nodes and 331 links. Four Image nodes now had semantic
  text and only two flattened links remained empty. Three repeated seven-sample runs measured medians of
  213.4 ms, 213.9 ms, and 198.4 ms versus the earlier 253.2 ms reference; serialized JSON changed from
  322,405 to 322,538 bytes (+0.04%). This satisfies the approved 5% non-regression budget, while remaining
  a non-isolated engineering probe rather than a production benchmark.

## Remaining work

- The generic improvements do not provide the `IPage` integration boundary required by existing JRA code.
- JRA adoption still requires saved provider fixtures, parser-oriented adapters, and explicit production
  latency/allocation budgets in a separate approved migration change.
