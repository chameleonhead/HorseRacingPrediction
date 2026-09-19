#!/usr/bin/env python3
"""Validate and summarize version 1 delegated-agent audit records."""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any


REQUIRED_INSTRUCTION_FIELDS = (
    "objective", "scope", "dependencies", "acceptance", "evidence", "escalation", "outputFormat"
)
QUALITY_GATES = ("acceptancePassed", "verificationPassed", "scopePassed", "regressionPassed")
VALID_VERIFICATION_STATES = {"verified", "mismatch", "unavailable"}
VALID_AVAILABILITY = {"complete", "partial", "unavailable"}
VALID_VERDICTS = {"pass", "pass-with-telemetry-gap", "fail"}
FORBIDDEN_KEYS = {"prompt", "promptText", "sourceText", "credential", "credentials", "secret", "apiKey"}


def _mapping(value: Any, path: str, issues: list[str]) -> dict[str, Any]:
    if not isinstance(value, dict):
        issues.append(f"{path} must be an object")
        return {}
    return value


def _number_or_none(value: Any, path: str, issues: list[str]) -> None:
    if value is not None and (isinstance(value, bool) or not isinstance(value, (int, float)) or value < 0):
        issues.append(f"{path} must be a non-negative number or null")


def _find_forbidden(value: Any, path: str, issues: list[str]) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            if key in FORBIDDEN_KEYS:
                issues.append(f"{path}.{key} is forbidden; store a fingerprint or reference instead")
            _find_forbidden(child, f"{path}.{key}", issues)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            _find_forbidden(child, f"{path}[{index}]", issues)


def validate_record(record: Any) -> list[str]:
    issues: list[str] = []
    root = _mapping(record, "$", issues)
    if root.get("schemaVersion") != 1:
        issues.append("$.schemaVersion must equal 1")
    _find_forbidden(root, "$", issues)

    required_sections = (
        "identity", "difficulty", "routing", "usage", "instruction", "reproducibility",
        "attribution", "quality", "reviewEffort", "cost", "baseline", "escapedDefects", "outcome"
    )
    sections = {name: _mapping(root.get(name), f"$.{name}", issues) for name in required_sections if name != "escapedDefects"}
    if not isinstance(root.get("escapedDefects"), list):
        issues.append("$.escapedDefects must be an array")

    identity = sections["identity"]
    for key in ("changeId", "taskId", "attemptId", "taskClass", "risk", "startedAt", "completedAt"):
        if not isinstance(identity.get(key), str) or not identity[key].strip():
            issues.append(f"$.identity.{key} must be a non-empty string")

    difficulty = sections["difficulty"]
    for key in ("ambiguity", "existingTestCoverage"):
        if not isinstance(difficulty.get(key), str) or not difficulty[key].strip():
            issues.append(f"$.difficulty.{key} must be a non-empty string")
    _number_or_none(difficulty.get("executionPaths"), "$.difficulty.executionPaths", issues)
    for key in ("publicContract", "persistenceOrMigration", "concurrency", "securityOrPrivacy", "externalDependency"):
        if not isinstance(difficulty.get(key), bool):
            issues.append(f"$.difficulty.{key} must be boolean")
    if not isinstance(difficulty.get("expectedWriteScope"), list) or not difficulty["expectedWriteScope"]:
        issues.append("$.difficulty.expectedWriteScope must be a non-empty array")

    routing = sections["routing"]
    for key in ("expectedTier", "requestedModel", "requestedReasoningEffort", "verificationState"):
        if not isinstance(routing.get(key), str) or not routing[key].strip():
            issues.append(f"$.routing.{key} must be a non-empty string")
    verification_state = routing.get("verificationState")
    if verification_state not in VALID_VERIFICATION_STATES:
        issues.append("$.routing.verificationState must be verified, mismatch, or unavailable")
    if verification_state == "verified":
        if not routing.get("observedModel") or not routing.get("observationSource"):
            issues.append("verified routing requires observedModel and observationSource")
        if routing.get("observedModel") != routing.get("requestedModel"):
            issues.append("verified routing requires observedModel to match requestedModel")
    if verification_state == "mismatch":
        if not routing.get("observedModel") or not routing.get("observationSource"):
            issues.append("mismatched routing requires observedModel and observationSource")
        if routing.get("observedModel") == routing.get("requestedModel"):
            issues.append("mismatched routing requires different requested and observed models")
    if verification_state == "unavailable":
        if routing.get("observedModel") is not None:
            issues.append("unavailable routing must not assert observedModel")
        if not routing.get("unavailableReason"):
            issues.append("unavailable routing requires unavailableReason")

    usage = sections["usage"]
    if usage.get("availability") not in VALID_AVAILABILITY:
        issues.append("$.usage.availability must be complete, partial, or unavailable")
    token_keys = ("inputTokens", "cachedInputTokens", "outputTokens", "reasoningTokens", "totalTokens", "configuredBudget")
    for key in token_keys:
        _number_or_none(usage.get(key), f"$.usage.{key}", issues)
    if usage.get("availability") == "complete" and any(usage.get(key) is None for key in token_keys[:5]):
        issues.append("complete usage requires every token field")
    if usage.get("availability") == "unavailable":
        if any(usage.get(key) is not None for key in token_keys[:5]):
            issues.append("unavailable usage must leave token fields null")
        if not usage.get("unavailableReason"):
            issues.append("unavailable usage requires unavailableReason")
    if not isinstance(usage.get("truncated"), bool):
        issues.append("$.usage.truncated must be boolean")

    instruction = sections["instruction"]
    if not isinstance(instruction.get("contractFingerprint"), str) or not instruction["contractFingerprint"].strip():
        issues.append("$.instruction.contractFingerprint must be a non-empty string")
    for key in REQUIRED_INSTRUCTION_FIELDS:
        if not isinstance(instruction.get(key), bool):
            issues.append(f"$.instruction.{key} must be boolean")

    reproducibility = sections["reproducibility"]
    for key in ("startRevision", "endRevisionOrPatch", "toolConfigProfile"):
        if not isinstance(reproducibility.get(key), str) or not reproducibility[key].strip():
            issues.append(f"$.reproducibility.{key} must be a non-empty string")
    if not isinstance(reproducibility.get("skills"), list):
        issues.append("$.reproducibility.skills must be an array")

    attribution = sections["attribution"]
    for key in ("startStatusCaptured", "workerPatchIdentified", "parallelOwnersRecorded", "unattributedChanges"):
        if not isinstance(attribution.get(key), bool):
            issues.append(f"$.attribution.{key} must be boolean")

    quality = sections["quality"]
    for key in QUALITY_GATES + ("promoted",):
        if not isinstance(quality.get(key), bool):
            issues.append(f"$.quality.{key} must be boolean")
    for key in ("blockingFindings", "nonBlockingFindings", "workerRetries", "leadCorrectionFiles", "leadCorrectionLines"):
        _number_or_none(quality.get(key), f"$.quality.{key}", issues)
    if not isinstance(quality.get("independentChallenge"), str) or not quality["independentChallenge"].strip():
        issues.append("$.quality.independentChallenge must be a non-empty string")

    review = sections["reviewEffort"]
    for key in ("reviewPasses", "reviewUsageTokens", "elapsedReviewMinutes", "humanActiveMinutes", "correctionMinutes", "reverificationMinutes", "auditOverheadMinutes"):
        _number_or_none(review.get(key), f"$.reviewEffort.{key}", issues)
    if review.get("availability") not in VALID_AVAILABILITY:
        issues.append("$.reviewEffort.availability must be complete, partial, or unavailable")
    if not review.get("source"):
        issues.append("$.reviewEffort.source is required")

    cost = sections["cost"]
    component_keys = ("worker", "automatedReview", "humanReview", "rework", "auditOverhead")
    monetary_keys = component_keys + ("totalSuccessfulOutcome",)
    for key in monetary_keys + ("reviewBurdenRatio", "humanHourlyRate"):
        _number_or_none(cost.get(key), f"$.cost.{key}", issues)
    has_money = any(cost.get(key) is not None for key in monetary_keys)
    if has_money and not (cost.get("currency") and cost.get("priceSource") and cost.get("priceObservedAt")):
        issues.append("monetary cost requires currency, priceSource, and priceObservedAt")
    if cost.get("humanReview") not in (None, 0) and cost.get("humanHourlyRate") is None:
        issues.append("humanReview cost requires an explicit humanHourlyRate")
    if cost.get("reviewBurdenRatio") is not None and cost.get("totalSuccessfulOutcome") in (None, 0):
        issues.append("reviewBurdenRatio requires a positive totalSuccessfulOutcome")
    if cost.get("totalSuccessfulOutcome") is not None:
        if any(cost.get(key) is None for key in component_keys):
            issues.append("totalSuccessfulOutcome requires every worker/review/rework/audit component")
        else:
            expected_total = sum(float(cost[key]) for key in component_keys)
            if not math.isclose(float(cost["totalSuccessfulOutcome"]), expected_total, rel_tol=1e-9, abs_tol=1e-9):
                issues.append("totalSuccessfulOutcome must equal worker plus review, rework, and audit components")
            expected_ratio = 0 if expected_total == 0 else sum(float(cost[key]) for key in component_keys[1:]) / expected_total
            if cost.get("reviewBurdenRatio") is None or not math.isclose(float(cost["reviewBurdenRatio"]), expected_ratio, rel_tol=1e-9, abs_tol=1e-9):
                issues.append("reviewBurdenRatio must equal review, rework, and audit cost divided by total")

    baseline = sections["baseline"]
    if not isinstance(baseline.get("comparable"), bool):
        issues.append("$.baseline.comparable must be boolean")
    if not isinstance(baseline.get("auditIds"), list):
        issues.append("$.baseline.auditIds must be an array")
    if baseline.get("comparable") and not baseline.get("auditIds"):
        issues.append("comparable baseline requires at least one audit ID")
    if not baseline.get("comparable") and not baseline.get("reason"):
        issues.append("non-comparable baseline requires a reason")

    outcome = sections["outcome"]
    if outcome.get("verdict") not in VALID_VERDICTS:
        issues.append("$.outcome.verdict must be pass, pass-with-telemetry-gap, or fail")
    if not outcome.get("leadDecision") or not outcome.get("recommendation"):
        issues.append("$.outcome requires leadDecision and recommendation")

    quality_pass = (
        all(quality.get(key) is True for key in QUALITY_GATES)
        and quality.get("blockingFindings") == 0
        and attribution.get("unattributedChanges") is False
    )
    telemetry_gap = verification_state == "unavailable" or usage.get("availability") != "complete"
    model_mismatch = verification_state == "mismatch"
    expected_verdict = "fail" if (not quality_pass or model_mismatch) else ("pass-with-telemetry-gap" if telemetry_gap else "pass")
    if outcome.get("verdict") != expected_verdict:
        issues.append(f"$.outcome.verdict must be {expected_verdict} for the recorded gates")

    return issues


def _percentile(values: list[float], percentile: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    rank = (len(ordered) - 1) * percentile
    lower = math.floor(rank)
    upper = math.ceil(rank)
    if lower == upper:
        return ordered[lower]
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (rank - lower)


def summarize(records: list[dict[str, Any]]) -> dict[str, Any]:
    valid_quality = [r for r in records if r["outcome"]["verdict"] in {"pass", "pass-with-telemetry-gap"}]
    attributable = [r for r in valid_quality if not r["attribution"]["unattributedChanges"]]
    measured_tokens = [float(r["usage"]["totalTokens"]) for r in attributable if r["usage"].get("totalTokens") is not None]
    measured_costs = [float(r["cost"]["totalSuccessfulOutcome"]) for r in attributable if r["cost"].get("totalSuccessfulOutcome") is not None]
    review_minutes = [
        sum(float(r["reviewEffort"].get(key) or 0) for key in ("humanActiveMinutes", "correctionMinutes", "reverificationMinutes", "auditOverheadMinutes"))
        for r in attributable
    ]
    escaped = sum(len(r.get("escapedDefects", [])) for r in records)
    comparable = sum(1 for r in attributable if r["baseline"].get("comparable"))
    recommendation = "collect-more-successful-samples"
    if len(attributable) >= 5:
        recommendation = "eligible-for-one-step-reviewed-adjustment"
    if any(r.get("escapedDefects") for r in records):
        recommendation = "review-or-suspend-affected-route"
    return {
        "records": len(records),
        "successful": len(valid_quality),
        "attributableSuccessful": len(attributable),
        "comparableSuccessful": comparable,
        "escapedDefects": escaped,
        "tokens": {"samples": len(measured_tokens), "p50": _percentile(measured_tokens, .5), "p90": _percentile(measured_tokens, .9)},
        "successfulOutcomeCost": {"samples": len(measured_costs), "p50": _percentile(measured_costs, .5), "p90": _percentile(measured_costs, .9)},
        "reviewEffortMinutes": {"samples": len(review_minutes), "p50": _percentile(review_minutes, .5), "p90": _percentile(review_minutes, .9)},
        "recommendation": recommendation,
    }


def _load(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("paths", nargs="+", type=Path)
    parser.add_argument("--summary", action="store_true")
    args = parser.parse_args(argv)
    records: list[dict[str, Any]] = []
    failed = False
    for path in args.paths:
        try:
            record = _load(path)
        except (OSError, json.JSONDecodeError) as error:
            print(f"{path}: {error}", file=sys.stderr)
            failed = True
            continue
        issues = validate_record(record)
        if issues:
            failed = True
            for issue in issues:
                print(f"{path}: {issue}", file=sys.stderr)
        else:
            records.append(record)
            print(f"{path}: valid")
    if args.summary and records:
        print(json.dumps(summarize(records), ensure_ascii=False, indent=2, sort_keys=True))
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
