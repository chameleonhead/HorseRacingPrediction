# JRA subject identification failure recovery

- Status: Implemented
- Owner: HorseRacingPrediction team
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

Production recorded two `SubjectNotIdentified` horse-profile failures. One task reached the current
JRA profile URL for the expected horse but failed the final name comparison. The other found no
public search candidate and referred to a domain horse that was not visible. Both became
`ResourceNotFound` after one attempt. The stored attempt omitted the requested subject name, parsed
name, candidate identities, and final browser URL, so the operator could not distinguish a transient
page-state problem from a genuinely unavailable subject.

## Goals

- Preserve enough structured subject-identification evidence to determine why matching failed.
- Expose identification failures through the existing actionable-failure workflow without changing
  retry behavior.
- Keep successful subject collection and explicit saved-location validation unchanged.
- Audit other collection errors and ensure every persisted attempt has at least definition/resource
  context and an HTTP status when an `HttpRequestException` provides one.

## Non-goals

- Automatically merge or rename domain horses.
- Accept a profile whose normalized name, birth date, or saved source identity contradicts the task.
- Add automatic retry for subject-identification failures.
- Redesign the collection-management pages or migrate existing failure history.
- Automatically rerun the two production failures as part of deployment.

## Experience and interaction design

An identification failure remains the current one-attempt `ResourceNotFound` outcome and appears
under **要対応**. The error detail is safe for the management UI and contains:

- expected subject type and name;
- failure reason: no candidate, multiple candidates, profile-name mismatch, birth-date mismatch, or
  saved-identity mismatch;
- normalized candidate names and public identity URLs, capped to five candidates;
- the parsed profile name when a profile page was reached;
- requested and final browser URLs when available.

No new operator action is introduced. The existing individual/group recovery action starts a new
task with a fresh three-attempt budget.

## Documentation updates

- `docs/22-collector-design.md`: defines subject-identification failure diagnostics and the diagnostic
  evidence retained by the canonical collector workflow documentation.
- No UI structure changes are required. `docs/20-admin-ui-design.md` already requires human-readable
  error detail, attempt history, and recovery actions, and the existing pages already render those
  fields.

## Technical impact

- Add an identification failure kind and diagnostic payload to the JRA subject navigation boundary.
  The exception must carry data, not require handlers to parse localized message text.
- Capture the expected identity, observed profile identity, candidate summaries, requested URL, and
  final URL at the point where they are known.
- In `JraSubjectProfileCollectionHandler`, preserve the current `ResourceNotFound` /
  `SubjectNotIdentified` result while copying structured diagnostic fields into the completion.
- At both local and Lambda execution boundaries, backfill missing `PageIdentification` with the
  definition and resource key. Preserve handler-provided page identification, URLs, and classifications.
- Persist the HTTP status from unhandled `HttpRequestException` instances. URLs remain null when the
  exception and handler do not expose a reliable URL; diagnostics must not invent one.
- Populate `RequestedUrl`, `FinalUrl`, and `PageIdentification` on the completion record. Keep the
  readable error message concise while including expected and observed names; candidate details are
  bounded to prevent oversized persisted messages.

## Decisions

1. Identification remains strict. A retry never permits saving a mismatched profile.
2. Automatic retry is intentionally unchanged. Operators decide whether to use the existing recovery
   action after reviewing the enriched evidence.
3. Structured failure data is introduced at the navigation boundary. Localized exception-message
   matching is retained only as a compatibility fallback until all subject navigation failures use
   the structured type.
4. Candidate evidence is capped at five and contains public JRA data only. No page HTML or credentials
   are persisted.

Alternatives rejected:

- Automatic bounded or unlimited retry: explicitly excluded by the user; the change is diagnostics-only.
- Relaxed name matching: risks writing another horse's profile.
- Logging only to CloudWatch: does not make the evidence available in the resource/attempt history.

## Acceptance criteria

| # | Criterion | State |
|---|---|---|
| AC1 | A zero-candidate, multiple-candidate, or identity-mismatch failure records its structured failure kind, expected identity, bounded candidates, observed identity, and available URLs. | Passed |
| AC2 | Every identification failure remains a first-attempt `ResourceNotFound`/`SubjectNotIdentified`, marks the resource unavailable, and creates the existing actionable failure notification without an automatic retry time. | Passed |
| AC3 | A successful collection saves only a profile that passes name, birth-date, and saved-source-identity validation. | Passed |
| AC4 | Existing saved-location fallback, subject projection 404 retry, cancellation, timeout, and unrelated collection result mappings remain unchanged. | Passed |
| AC5 | Resource and failure-group detail render the improved diagnostic text and recorded URLs using the existing UI, without exposing credentials or raw page HTML. | Passed (existing UI bindings inspected) |
| AC6 | Focused parser/navigation/handler and relevant API component tests pass; the solution builds with no new warnings and `git diff --check` passes. | Passed |
| AC7 | Every handler-returned or unhandled collection failure receives definition/resource context when it has no more specific page identification; an available HTTP exception status is persisted without fabricating URLs. | Passed |

## Delivery plan

1. Add structured subject-identification failure contracts and focused navigation/parser tests.
2. Implement diagnostic completion fields with zero-candidate, multiple-candidate, name-mismatch,
   birth-date-mismatch, and source-identity-mismatch tests.
3. Verify existing resource/failure detail rendering against the enriched stored attempt.
4. Audit all collection handlers and add execution-boundary fallback context for errors that cannot
   provide page-specific diagnostics.
5. Run focused tests, build the solution, run broader relevant suites, and record results here.
6. Do not modify or recover current production failure records automatically.

## Verification record

- Planning investigation: production failure group, resource details, execution batches, Lambda logs,
  current JRA profile page, and the existing live horse-profile navigation test were inspected.
- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj
  --filter FullyQualifiedName~HorseProfileSearch_DaiyuVenti` passed 1/1 against the current JRA site.
- Focused collector tests passed: 27/27.
- Full collector test suite passed: 155/155.
- Full scraping test suite passed: 231 passed, 1 environment-dependent test skipped, 0 failed.
- `dotnet build HorseRacingPrediction.sln --no-restore` succeeded with 0 warnings and 0 errors.
- Existing `JobDetail.razor` and `CollectionFailureGroupDetail.razor` bindings were inspected and already
  display error messages, requested/final URLs, HTTP status, and page identification.
- `git diff --check` passed for this change set.

## Deviations and follow-up

- 2026-09-13: After initial approval, the user explicitly removed automatic retry from scope. The
  record and canonical collector documentation were updated before implementation continued. The
  approved outcome is diagnostics-only and preserves the existing one-attempt failure behavior.
- 2026-09-13: The user requested an audit of other error types. The diagnostics-only scope now also
  guarantees definition/resource fallback context and available HTTP status for all collection
  failures; it does not change their result or retry classification.
