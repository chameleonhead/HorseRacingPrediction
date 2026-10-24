# Authenticated API observation — 2026-10-24

Observed 20:51 JST. Authorized direct HTTPS GET with X-Api-Key held only in a secure interactive process. No browser workaround, TLS bypass, redirected request, production mutation, retry, dismissal, migration, reset or resume. Responses inspected in memory; credential and raw bodies are not saved.

At handoff, the SecureString was disposed and the child process12061 exited0. Sanitized diagnostic candidates and target/task receipts are retained separately to avoid repeating the inventory. Pipeline GET confirms isPaused=true, updated19:55:07+09, reason points to the Niigata8 DomainWriteRejected notification; this is an automatic incident pause, not evidence that jobs recovered.

## Scope and persisted evidence

| Group | Definition | Current actionable targets/tasks | Error |
| --- | --- | ---: | --- |
| 0FB0D54D5B4D2FAA | race-detail | 2 | DomainWriteRejected / HorseIdentityConflict |
| 3B0A3856144660E6 | race-detail | 39 | RaceCardWriteRejected / HorseIdentityConflict |
| E56D934CCA551769 | horse-profile | 4 | SubjectNotIdentified / multiple same-name candidates |
| FBF7553D84F38EFB | trainer-profile | 1 | JraCollectionException / missing retired-trainer public link |

Full group pages contain46 distinct resource+definition keys and46 distinct task IDs, not duplicate group membership. The earlier reported44 is an earlier observation; its exact timestamp/scope is unavailable, so the difference is not attributed to a specific cause. All46 resource histories read successfully:399 persisted attempts in total, no request/task/attempt total exceeds the requested100-page-size bound,46 unresolved notifications. Latest race attempt codes independently reconcile2+39. Forty-one race messages report41 distinct official JRA horse identities, not the original three approved migration inputs.

Current failures reference16 distinct execution batches; all16 batch GETs succeeded. Nine report a declared batch task count larger than the persisted task list returned. Missing sibling results cannot be reconstructed from this API; do not infer failed siblings from timing/order. Historical-attempt batches beyond these current16 were not fetched: this is an explicitly bounded current-failure investigation, not a complete lifetime batch audit. No failure item persists an HTTP status;44 have requested URLs and43 final URLs. Do not describe these errors as HTTP failures based on absent status.

Representative persisted failures:

- `20261024:Niigata:8`, task `01239d60-2979-4fb5-95c8-c2847da44aaa`, attempt1 finished19:55:07+09: HorseIdentityConflict, ビービーバザーク, `jra-horse:2022102397`; batch `a5ba56b0-626c-4a15-a55f-17d0fbf59b6c`. State/task6; unresolved.
- `20261017:Niigata:3`, task `895e5016-ca0e-4adb-a6d3-b7382c3b5cef`, attempt10 finished21Oct15:03:22+09: RaceCardWriteRejected/HorseIdentityConflict, ヴィヴァシャス, `jra-horse:2022104391`; batch `fe2cddba-d0ef-455b-bf7d-008787bd1789`. State/task6; unresolved.
- Ambiguous profile origins: ジャンダルム←ヒルノアラゴン, コントレイル←ポネンテ, ワールドプレミア←トパーズプレミア, クリソベリル←プラチナリーフ. Each persists two same-name candidate URLs and no candidate evidence. Origin is a related horse, not proof that a candidate is the correct ancestor. Four resource states7/latest tasks6.
- Trainer `trainer-ae6f6e74-ec5a-5954-8d2b-4e324b6cb0d1` originated from ダノンレジェンド. Historical failed task `f5a4057d-79a6-4ad8-833a-e8aaf83aaf35`, attempt51 finished6Oct01:05:20+09, batch `92591cbc-95a3-4245-a16f-01effc0149dc`: 引退調教師の公開リンクが見つかりません。 Current state/latest task1, so do not claim it remains the same terminal failed task.

## Cause boundary and corrective direction

Source trace: `src/HorseRacingPrediction.Api/CollectionIdentityResolver.cs:87-151` rejects mismatched source/name/birth information and unproven source-less legacy identity adoption. Race-card and result handlers surface this guard at different write stages; removing the guard would risk joining different horses. Error text alone does not distinguish all guard branches. Exact source versus old-ID diagnostics are required before proposing mappings.

All41 error horse names were searched through `/api/horses?registeredName=...&page=1&pageSize=100`. Each has one exact-name current result (41 rows, zero missing/multiple exact matches); name uniqueness is not identity proof. All41 per-ID diagnostic GETs then succeeded: all registration Supported, unchanged original/current name, no normalized source identity/source-URL identity, both initial-name-derived comparisons Differs, no blocker/truncation. All41 current birth dates are null. Representative ダズリングダンス old `horse-d2c380c2-ee3c-5820-8654-35867b7db47e`: profile source Missing and name-derived candidate `horse-c586e663-31d5-5bc9-af87-07d73d3539d0` differs. Combined with the source guard and current failures, this substantiates unproven source-less legacy-ID adoption across the41, not a transient network error. It does not prove each proposed old/new pair refers to the same real horse.

First identity preflight page: HorseRoots, RowsInspected128, State Incomplete, next cursor present. Counts observed1/unsupported1/unknownShape45/unverifiable84/notConfigured1 include surface and identifier observations, not a count of45 corrupt horse records. No full-corpus completion claim; no migration eligibility inferred from this page. Selected-root migration preview has separate fail-closed checks.

All four origin horse GETs succeeded and identify the ambiguous subject as their sire: ヒルノアラゴン born2024-03-09/sireジャンダルム; ポネンテ born2024-04-01/sireコントレイル; トパーズプレミア born2024-03-03/sireワールドプレミア; プラチナリーフ born2024-02-01/sireクリソベリル. This is useful pedigree evidence but not a fresh validation of the external candidate pages. Four jobs require candidate-bound identification, not name-only merge/retry. Trainer GET identifies 村山 明（栗東）; failed attempt has no requested/final URL, and current resource has no location. The exact failing external page is not persisted. Independent navigation/profile evidence is required; no claim that the currently deployed code still reproduces this historical failure. Proposed correction must keep these three cause classes separate and preserve failure history. Existing original3 approval alone does not authorize a41-pair manifest or weaken reference checks.

## Reproduction contracts

Read current groups, full group pages, each resource history, and current failure execution batches using the diagnostic reference. Canonical mapping investigation uses `/api/horses`, `/api/v2/admin/identity-diagnostics/horses/{horseId}` and, where needed, paged `/api/v2/admin/identity-migration-preflight?cursor=...`. API success is diagnostic evidence only, not recovery proof.
