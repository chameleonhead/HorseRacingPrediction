# Agent execution audit

Use this reference for delegated coding, model-routing audits, usage/cost comparisons, or persistent routing improvements. Keep the compact conclusion in the governing change record and store one JSON artifact per delegated task under `docs/changes/<change>/agent-audits/<task-id>.json`.

## Required record

The validator accepts schema version `1`. Unknown fields are allowed so provider telemetry can evolve, but the required decision fields below must remain explicit.

```json
{
  "schemaVersion": 1,
  "identity": {
    "changeId": "20260919_agent-execution-audit",
    "taskId": "T2",
    "attemptId": "T2-A1",
    "taskClass": "bounded-code",
    "risk": "low",
    "startedAt": "2026-09-19T10:00:00+09:00",
    "completedAt": "2026-09-19T10:12:00+09:00"
  },
  "difficulty": {
    "ambiguity": "low",
    "executionPaths": 1,
    "publicContract": false,
    "persistenceOrMigration": false,
    "concurrency": false,
    "securityOrPrivacy": false,
    "externalDependency": false,
    "existingTestCoverage": "strong",
    "expectedWriteScope": ["path/to/file"]
  },
  "routing": {
    "expectedTier": "low-cost-coding",
    "requestedModel": "gpt-5.6-luna",
    "requestedReasoningEffort": "medium",
    "observedModel": null,
    "observationSource": null,
    "verificationState": "unavailable",
    "unavailableReason": "runtime did not expose worker model metadata"
  },
  "usage": {
    "availability": "unavailable",
    "inputTokens": null,
    "cachedInputTokens": null,
    "outputTokens": null,
    "reasoningTokens": null,
    "totalTokens": null,
    "configuredBudget": null,
    "truncated": false,
    "unavailableReason": "runtime did not expose per-worker usage"
  },
  "instruction": {
    "contractFingerprint": "sha256:...",
    "objective": true,
    "scope": true,
    "dependencies": true,
    "acceptance": true,
    "evidence": true,
    "escalation": true,
    "outputFormat": true
  },
  "reproducibility": {
    "startRevision": "commit-or-tree-id",
    "endRevisionOrPatch": "commit-or-patch-id",
    "skills": ["agent-task-orchestration"],
    "toolConfigProfile": "repository-default",
    "providerModelVersion": null
  },
  "attribution": {
    "startStatusCaptured": true,
    "workerPatchIdentified": true,
    "parallelOwnersRecorded": true,
    "unattributedChanges": false
  },
  "quality": {
    "acceptancePassed": true,
    "verificationPassed": true,
    "scopePassed": true,
    "blockingFindings": 0,
    "nonBlockingFindings": 0,
    "workerRetries": 0,
    "leadCorrectionFiles": 0,
    "leadCorrectionLines": 0,
    "promoted": false,
    "independentChallenge": "existing-regression-suite",
    "regressionPassed": true
  },
  "reviewEffort": {
    "reviewerTier": "lead",
    "reviewerModel": null,
    "reviewUsageTokens": null,
    "reviewPasses": 1,
    "elapsedReviewMinutes": 4.0,
    "humanActiveMinutes": null,
    "correctionMinutes": 0.0,
    "reverificationMinutes": 2.0,
    "auditOverheadMinutes": 1.0,
    "availability": "partial",
    "source": "lead task log"
  },
  "cost": {
    "currency": null,
    "priceSource": null,
    "priceObservedAt": null,
    "worker": null,
    "automatedReview": null,
    "humanReview": null,
    "rework": null,
    "auditOverhead": null,
    "totalSuccessfulOutcome": null,
    "reviewBurdenRatio": null,
    "humanHourlyRate": null
  },
  "baseline": {
    "comparable": false,
    "auditIds": [],
    "reason": "no comparable successful sample yet"
  },
  "escapedDefects": [],
  "outcome": {
    "verdict": "pass-with-telemetry-gap",
    "leadDecision": "accept",
    "recommendation": "retain-and-collect-samples"
  }
}
```

## Invariants

- `requestedModel` is configuration intent. Never copy it into `observedModel` without runtime evidence.
- `verificationState` is `verified`, `mismatch`, or `unavailable`. `verified` requires an observation source. `unavailable` requires a reason.
- Usage availability is `complete`, `partial`, or `unavailable`. Missing values remain `null`; do not estimate provider telemetry.
- A pass requires acceptance, verification, scope, regression, zero blocking findings, and attributable work. A telemetry gap changes the verdict to `pass-with-telemetry-gap`, not `fail`.
- A model mismatch is a failed model-verification gate even when code quality passes.
- Important worker-authored code and tests require an independent challenge. An existing regression suite is acceptable only for a low-risk mechanical change and the audit states that rationale.
- Human time is active minutes, not unattended wall-clock time. Convert it to currency only when the user or organization supplied the hourly rate.
- Currency aggregation requires a price source, observation date, and one currency. Otherwise report tokens, minutes, findings, and corrections separately.
- `reviewBurdenRatio` uses automated review, human review, rework, and audit overhead cost divided by total successful-outcome cost. Do not compute it when required monetary components are unavailable.
- Unattributed shared-worktree changes exclude the run from comparative routing statistics.
- Initial passes and escaped-defect-adjusted passes are separate measures.

## Routing and promotion

- Start `bounded-code` tasks with `gpt-5.6-luna` at low or medium reasoning when the contract is frozen, the write scope is narrow, verification is independent, and the task has no architecture, public-contract, persistence/migration, concurrency, security/privacy, or destructive decision.
- Use `gpt-5.6-terra` for bounded multi-file work that still has settled acceptance and independently verifiable output.
- Keep ambiguous requirements, architecture, public contracts, persistence/migrations, concurrency correctness, security/privacy, destructive operations, integration, and final acceptance with the lead tier.
- After one focused correction still fails a gate, or scope/ambiguity expands, promote one tier. Record the failed attempt and its cost instead of overwriting it.

## Budget recommendations

Group only attributable, quality-passing samples with comparable task class, risk, model, reasoning effort, and difficulty profile.

- Fewer than five successful samples: show observations; do not change a persistent default.
- Five or more: report P50, P90, maximum successful usage, retry usage, truncation, and escaped defects.
- Recommend only one bounded change at a time: model tier, reasoning effort, token budget, or tighter task boundary.
- Attach an observation window and rollback condition. A security/data-integrity/scope/false-completion failure immediately recommends suspending the affected low-cost route.

## Review-cost accounting

Report these components separately before any total:

1. worker model tokens/credits/currency;
2. automated reviewer model tokens/credits/currency;
3. human active review minutes and optional user-supplied labor rate;
4. retry, correction, promotion, and re-verification cost;
5. audit overhead.

Do not describe a worker as cheaper merely because its own token use is lower. Compare successful-outcome cost and review burden against a similar baseline. When price or time data is missing, state the gap and retain the raw available units.

## Persistent improvement gate

Persistent skill or configuration changes require repeated comparable evidence, one material security/data/scope/false-completion failure, or at least five successful samples supporting a cheaper route. Apply the smallest change, validate the skill/configuration, run an independent forward test, define the rollback condition, and record the observation period. A recommendation may be generated automatically; changes beyond the approved scope remain proposals.
