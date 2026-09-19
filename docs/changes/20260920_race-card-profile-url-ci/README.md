# Race card profile URL CI portability fix

- Status: Implemented
- Change record schema: 2
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-20
- Updated: 2026-09-20
- JRA site contract impact: None

## Context

Linux CI failed `RaceCardPageParserTests.Parse_DOM断片付きセル_Classに基づき馬主と馬体重を抽出する` because a root-relative JRA URL was accepted by `Uri.TryCreate(..., Absolute)` as a file URI before page-relative resolution. Windows resolved the same fixture as expected, hiding the portability defect locally.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Root-relative jockey/trainer URLs resolve against the HTTPS JRA page on Windows and Linux semantics. | T1 | existing DOM-fragment parser regression | Verified |
| AC2 | True absolute HTTP/HTTPS URLs remain accepted and non-web absolute URI interpretations are not returned as subject profile URLs. | T1 | focused parser tests and code inspection | Verified |
| AC3 | The affected scraping test project and repository format check pass. | T1 | Release scraping tests; dotnet format | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Restrict absolute URL acceptance to HTTP/HTTPS and preserve page-relative resolution. | Main | Lead | CI failure log | RaceCardPageParser.cs; this record | focused and project regression tests | passing Release test output | Verified | Lead — single short CI portability fix | none | unavailable; retries 0; corrections 0; reviews 1 |

## Concern and agreement review

- Concern review: No material concern — this preserves the documented JRA-host/path/query validation and only removes an OS-dependent file-URI interpretation.
- User explicitly requested repair of the failing CI on 2026-09-20. No external contract, persistence, security, or destructive-operation change is introduced.

## Verification record

- GitHub Actions runs `35461102969` and `35461102962` both failed on the same assertion: expected the JRA jockey profile URL but received `null`.
- The focused Release test passed locally before the fix on Windows, confirming the OS-dependent blind spot rather than disproving the Linux failure.
- The existing DOM-fragment regression is retained as the cross-platform executable test; CI supplies the independent Linux forward test.

## Final review

- The change is limited to URL classification in `FindSubjectProfileUrl`.
- AC1-AC3 and T1 are verified locally; final Linux confirmation is the next main CI run.
