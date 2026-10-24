# First supervised migration attempt

24Oct2026, user-approved direct API route; no production code change.

- Fresh21:17:16+09:00: groups2+39+4+1; paused since19:55:07. All three TargetReviewV2 manifests16/16/9 pass/no blockers.
- Request keys saved in migration-requests.json before staging. First16 use its batch1 key and the first16 ordered diagnostic-candidates.json entries with canonical `jra-horse:` input and hypothesisAccepted=true. Server derives destination IDs.
- Operation `f8f857b4-23c6-40d7-ac04-60676f42aa6d`, staged21:19:20.5124391+09: PendingV2/AwaitingActions/None.
- Bodyless explicit execute accepted21:19:36.7828855+09: IntentRecorded. At21:19:36.8735072+09 phaseStoppingApi.
- Followup status reads remain AwaitingActions/None/StoppingApi/results[] through21:23:23+09; API status endpoint remains200.
- /health200; ordinary runtime and pipeline GET503. Decode WebResponse Content byte[] with UTF8 before parsing; exact problem title `Application startup is blocked by migration state.` confirms blocked-host fallback (initial byte-array pipeline parsing did not establish a negative result).
- Source-reviewed preparation persistsStoppingApi before installing markers/stoppingAPI. Supervisor must observe private exit73 and validate markers before ApiExited; backup/census/apply occur later. Source does not overwrite exitcode in Program. Actual exit/gate/restart failure cause cannot be distinguished by the exposed API.
- Blocked host has no preapply execute/resume endpoint. Its verify POST is only for succeeded-data operations and requires cookie/CSRF; it is not an escape hatch for this phase and was not called. /health alone is not normal-service health.
- No second execute, later batch stage, retry, collection resume, marker deletion, reset, forced reclassification or DB write outside the approved journal admission/intent. Zero confirmed migrated; no ApiExited/BackedUp/Applying/Verified signal. Do not treat accepted HTTP or staged state as recovery.
- Next requires separately authorized server-side runtime evidence and safe continuation planning. Preserve this exact operation and idempotency key; do not regenerate a competing request. Do not blindly invoke existing startup recovery, whose eligibility does not establish safe migration-intent continuation.
