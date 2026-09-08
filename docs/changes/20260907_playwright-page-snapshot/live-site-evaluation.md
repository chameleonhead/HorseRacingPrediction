# Live-site evaluation

Evaluated: 2026-09-08

## Target and method

The implementation was exercised against the public Wikipedia article
`https://en.wikipedia.org/wiki/Horse_racing_in_Japan`. This page was selected because it is relevant to
the repository while containing ordinary article content, site navigation, metadata, JSON-LD, links,
images, forms, and a table. It is not a substitute for saved JRA fixtures and may change independently.

Playwright Chromium 151 loaded the page to `DOMContentLoaded`, then waited for `#firstHeading`. Browser
startup and navigation were excluded from capture timing. After one warm-up, each option set was captured
seven times in the same page. Reported allocation is the process-wide .NET allocation counter delta around
`CaptureAsync`; it is useful for magnitude, but includes concurrent runtime allocation and excludes the
Chromium process.

The observed page returned HTTP 200, contained 1,379 DOM elements, and `ContentAsync()` serialized to
166,904 UTF-8 bytes.

## Results

| Options | Median | Slowest sample | Managed allocation median | Snapshot JSON | Nodes | Links | Tables | Forms | Images |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Defaults | 253.2 ms | 557.6 ms | 2,962,256 B | 322,405 B | 1,060 | 331 | 1 | 4 | 6 |
| Content-only collections disabled | 116.1 ms | 154.9 ms | 1,758,960 B | 209,032 B | 1,060 | 0 | 0 | 0 | 0 |
| Defaults plus locations | 189.6 ms | 206.6 ms | 3,438,792 B | 378,784 B | 1,060 | 331 | 1 | 4 | 6 |

The locations timing is lower than the default median in this small, non-isolated sample and must not be
interpreted as locations improving performance. The deterministic observations are that locations added
56,379 bytes (17.5%) to public JSON and approximately 477 KB to the median managed allocation.

## Functional observations

- The final URL and title were correct. Thirteen headings, 331 resolved links, one table, four forms, six
  images, ten metadata values, and one JSON-LD object were available through typed properties.
- Heading order reflected the rendered document, beginning with `Contents`, `History`, `Japan Racing
  Association`, and `National Association of Racing`. One retained heading had no direct `Text`; consumers
  cannot assume every `Heading` has a non-null `Text` and may need descendant text or accessible-name logic.
- Relative and fragment links were resolved correctly. Some image-only links legitimately had empty `Text`
  and required `AccessibleName`, confirming that parser code should not select links by `Text` alone.
- The table was preserved but had no caption because the source table had none. Exact caption lookup is
  therefore insufficient as the only table discovery mechanism on real pages.
- One `duplicate-meta` warning was produced without failing capture, demonstrating the intended partial
  failure behavior.
- With dedicated collections disabled, the semantic tree remained unchanged at 1,060 nodes. These options
  reduce collection work and payload but are not a subtree/content-selection mechanism.

## Implementation findings

### Sound decisions

- One `EvaluateAsync` returns the complete browser DTO, so there is no protocol call per element.
- Direct text-node traversal preserved document order and avoided parent `textContent` duplication.
- Safe pruning retained the article and site chrome rather than silently applying reader-mode behavior.
- Dedicated collections made metadata, links, forms, and tables immediately inspectable without tree
  reconstruction.

### Performance costs visible in the code

- `isHidden` walks from each visited element through all ancestors and calls `getComputedStyle` for each.
  `isPruned` can then walk the ancestors again for aggressive checks. Deep pages therefore pay more than a
  simple linear visit and repeat style queries.
- The tree walk and the dedicated `querySelectorAll` passes independently revisit links, images, tables,
  forms, controls, and definition lists. The rich default capture took 2.18 times the median duration of
  the content-only run on this page.
- `innerText` is computed repeatedly for rendered text, accessible names, labels, links, and table cells.
  Because `innerText` is layout-aware, these calls can be materially more expensive than text-node reads.
- Every semantic node carries a source object; links and images then repeat source and text-related evidence
  in their dedicated collections. On this page, public JSON was 1.93 times the serialized HTML size even
  after structural pruning.
- Browser data is first materialized as an object graph, then `JSON.stringify` creates a large string,
  Playwright transfers it, `System.Text.Json` creates DTOs, and conversion creates the final record graph.
  The roughly 3 MB observed .NET allocation for a 322 KB result is consistent with multiple intermediate
  representations.

## Usability assessment

For a caller that already owns `IPage`, the capture call and typed collections are straightforward. For a
domain parser, the API is evidence-rich but still low-level. A practical parser needs conventions for:

- obtaining a node's effective text from `Text`, descendants, and `AccessibleName`;
- selecting unc captioned tables using headings, source references, or content;
- selecting links when visible text is empty;
- handling warnings and optional/invalid URLs; and
- choosing a smaller option profile when forms, images, or metadata are unnecessary.

These conventions should live in provider/parser helpers, not in the generic snapshotter. Existing JRA
code also cannot call this API directly because it receives the compatibility snapshot rather than the
private `IPage`.

## Conclusion

The live-page run confirms that the implementation captures useful rendered information and remains
operational on a non-trivial real page. It also confirms that the current default is not a compact
representation in the general case: capture cost was material, allocation was about 2.96 MB, and JSON was
almost twice the page HTML. Before JRA migration, optimize or consciously accept repeated traversal and
representation, add effective-text/table/link query conventions, and measure saved JRA pages with explicit
latency, allocation, and payload budgets.
