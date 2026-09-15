# Playwright collection efficiency baseline

## Purpose

This baseline freezes a reproducible local measurement seam before Playwright, snapshot, navigation,
view, or parser optimizations. It changes no collection behavior and makes no production performance
claim.

## Runnable benchmark

Run the focused diagnostic tests:

```powershell
dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --filter FullyQualifiedName~PerformanceProbeTests
```

`LocalPipelineBenchmark_ReportsReadyTextCaptureViewParserNavigationRepositoryAndAll` uses an in-memory
`data:` page and reports elapsed time plus process-wide approximate managed allocations for:

- `navigation`: Chromium navigation through `DOMContentLoaded`;
- `ready`: the subsequent `Load` readiness barrier;
- `text`: one rendered body-text extraction;
- `capture`: one production `PlaywrightPageSnapshotter` capture;
- `view`: production `JraSnapshotView` projection;
- `parser`: production `RaceListPageParser` parsing;
- `repository-call`: an isolated counted-call seam, not a database benchmark;
- `all`: total measured pipeline wall time.

The test asserts semantics and non-negative counters, not machine-specific timing thresholds. Use at
least five warm measured iterations and record environment details when comparing an optimization.

## Static source-backed protocol counts

`BrowserProtocolBaseline` freezes formulas derived from the current locator loops, excluding readiness
and the final click:

| Scenario | Current estimate |
| --- | ---: |
| Extract 100 links, normal text path | 701 calls |
| Extract 100 links, worst text fallback | 1,301 calls |
| Click the last of 100 links | 304 calls |
| Search 100 clickable candidates with 5 text matches | 311 calls |
| Extract 2 forms / 20 fields / 2 selects / 24 options | 279 calls |

These counts are source models, not intercepted Playwright wire traces. A later optimization must add
an operation-counting adapter at the Playwright boundary if exact protocol counts are required.

## Environment record

Record the following with every comparison:

```powershell
dotnet --info
$PSVersionTable.PSVersion
git rev-parse HEAD
```

Also record OS, CPU, available memory, Playwright package/browser version, Debug or Release build,
warm-up count, measured iteration count, and whether other test processes were active.

### Initial seam run — 2026-09-15

- Revision: `e6df0d9c0fde7a44d017d87f7b51369708d32b54`
- Windows 11 Pro 10.0.26200; PowerShell 7.6.5; .NET SDK 10.0.202
- Intel Core i7-12700, 12 cores / 20 logical processors; approximately 63.7 GiB visible memory
- Debug build; Microsoft.Playwright 1.62.0; one warm-up plus five measured iterations; other-process load not controlled

| Stage | Mean ms | p50 ms | p95 ms | Mean approximate bytes | p95 approximate bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| navigation | 5.296 | 5.320 | 6.217 | 94,865 | 112,744 |
| ready | 2.028 | 0.495 | 7.281 | 154,433 | 180,944 |
| text | 15.272 | 12.028 | 23.857 | 57,304 | 57,856 |
| capture | 5.705 | 5.468 | 6.331 | 462,961 | 622,856 |
| view | 0.117 | 0.101 | 0.198 | 36,728 | 44,496 |
| parser | 2.795 | 1.634 | 7.190 | 984,550 | 1,000,984 |
| repository-call seam | 0.079 | 0.004 | 0.259 | 0 | 0 |
| all | 31.463 | 24.865 | 44.432 | 1,797,403 | 1,982,144 |

This local fixture run proves reporting, not a production performance threshold. The `all` sample
includes nested measurement overhead, and process-wide allocation deltas can overlap between nested
samples. The parser allocation includes construction of the parsed page model; it is not a retained-size
measurement.

## Limitations and follow-up seam

- A `data:` fixture removes DNS, TLS, Internet, JRA server, and Lambda variability. It is suitable for
  regression comparisons of local browser/snapshot/parser work only.
- Async allocation uses `GC.GetTotalAllocatedBytes`; concurrent managed work can inflate a sample.
- `repository-call` proves the counter/measurement seam only. Measuring real repository calls requires
  wiring a counter at the owning API or persistence boundary, which is outside this isolated P1 scope.
- Readiness is measured using public Playwright APIs rather than private
  `PlaywrightWebBrowser.WaitForPageSettledAsync`; connecting that stage requires an approved shared-file
  instrumentation change.
- Legacy navigation repository-call counts are currently static source models. Exact counts need an
  injectable browser protocol adapter or Playwright tracing parser before behavior-changing work.
- No live JRA page, AWS service, production database, or production collector is contacted.
