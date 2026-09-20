# Playwright absolute link contract

- Status: Approved
- Change record schema: 2
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-20
- Updated: 2026-09-20

## Context

Playwright link extraction currently returns the raw `href`, so relative links can escape the browser boundary. `PlaywrightTools` already resolves relative links against the current page, but the producer contract is weaker and other consumers can receive relative URLs.

## Approved outcome

- `PlaywrightWebBrowser.GetLinksAsync` returns only absolute URLs, resolving relative `href` values against the current page URL.
- The receiving `PlaywrightTools` normalization remains in place and continues resolving relative URLs for compatibility with existing or alternate `IWebBrowser` implementations.
- Tests cover both the producer guarantee and the receiver compatibility behavior.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Removing receiver-side normalization after strengthening Playwright would break fake, legacy, or alternate browser implementations. | compatibility regression | Keep the existing receiving normalization as defense in depth. | AC2/T2 | Agree | User explicitly requested current relative inputs remain supported | Resolved in design |
| C2 | Root-relative paths can be interpreted as file URIs on some operating systems if classified as absolute before base resolution. | cross-platform wrong URL | Resolve against the current absolute page first; do not classify the raw root-relative value independently. | AC1/T1 | Agree | User explicitly requested absolute resolution | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Every link returned by Playwright has an absolute URL; document-relative links are resolved against the current page. | T1 | focused Playwright browser test | Verified |
| AC2 | A relative link returned by another `IWebBrowser` is resolved to an absolute URL before `PlaywrightTools` returns it. | T2 | existing and focused agent tests | Verified |
| AC3 | Related scraping and agent regressions pass, followed by Linux CI/CD verification. | T1-T3 | Release tests and GitHub Actions | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Resolve extracted Playwright href values at the browser boundary and add a producer regression. | Main | Lead | Approved scope | PlaywrightWebBrowser; scraping browser tests | focused test | absolute returned URL | Verified | Lead — small cross-platform contract fix | none | unavailable; retries 1; corrections 1; reviews 1 |
| T2 | Retain and verify receiver-side relative URL normalization. | Main | Lead | T1 | PlaywrightTools tests/docs | focused test | absolute tool output | Verified | Lead — same URL contract | none | unavailable; retries 0; corrections 0; reviews 1 |
| T3 | Run regressions, push, and verify terminal CI/CD runs and annotations. | Main | Lead | T1,T2 | this record | local and remote gates | successful clean runs | In progress | Lead — final integration | none | unavailable; retries 0; corrections 0; reviews 0 |

## Pre-implementation review

- The user explicitly approved the producer and receiver behavior in this task.
- No persistence, security boundary, public API payload, or destructive operation changes.
- The producer test is the counterexample: a relative DOM `href` must not leave `GetLinksAsync` unchanged.
- The receiver test protects compatibility independently of the concrete Playwright implementation.

## Checkpoint review

- Playwright resolves raw href values against `_page.Url` before constructing returned link snapshots. Root-relative values therefore cannot be misclassified as platform-specific file URIs.
- The focused producer regression passed for 10, 100, and 500 relative links and asserts every returned URL is absolute.
- The existing receiver regression passed and confirms `/home`, `./detail`, and `../contact` are resolved before tool output.
- Full related project regressions passed: scraping 263/263 and agents 106/106.
- Repository format verification passed.
