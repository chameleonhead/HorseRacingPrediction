#!/usr/bin/env python3
"""Validate compact agent audits in repository change records.

With no paths, changed change records are validated strictly. Explicit paths
are strict. ``--all`` scans historical records diagnostically unless combined
with ``--strict-history``.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

BASE_COLUMNS = {"id", "task", "owner", "model tier", "depends on", "write scope", "verification", "completion evidence", "state"}
AUDIT_COLUMNS = {"routing", "audit", "result metrics"}
ACTIVE_OR_COMPLETE = {"runnable", "in progress", "verified"}
ACTIVE = {"runnable", "in progress"}
VALID_STATES = {"proposed", "runnable", "in progress", "dependent", "externally blocked", "rejected with reason", "verified"}
NONE_VALUES = {"none", "n/a", "not applicable", "lead-only", "-"}
AVAILABILITY = {"complete", "partial", "unavailable"}
DECISIONS = {"accept", "revise", "promote", "reject"}


@dataclass(frozen=True)
class Table:
    heading: str
    headers: list[str]
    rows: list[dict[str, str]]


def _normal(value: str) -> str:
    value = re.sub(r"[`*_]", "", value).strip().lower()
    return re.sub(r"[^a-z0-9]+", " ", value).strip()


def _tables(text: str) -> list[Table]:
    lines, heading, found, index = text.splitlines(), "", [], 0
    while index < len(lines):
        match = re.match(r"^#{2,6}\s+(.+?)\s*$", lines[index])
        if match:
            heading = _normal(match.group(1))
        if lines[index].lstrip().startswith("|") and index + 1 < len(lines) and re.match(r"^\s*\|?\s*:?-{3,}", lines[index + 1]):
            split = lambda line: [cell.strip() for cell in line.strip().strip("|").split("|")]
            headers = [_normal(cell) for cell in split(lines[index])]
            index += 2
            rows = []
            while index < len(lines) and lines[index].lstrip().startswith("|"):
                cells = split(lines[index]) + [""] * len(headers)
                rows.append(dict(zip(headers, cells)))
                index += 1
            found.append(Table(heading, headers, rows))
            continue
        index += 1
    return found


def _nonempty(value: Any) -> bool:
    return isinstance(value, str) and bool(value.strip()) and _normal(value) not in {"pending", "tbd", "unknown"}


def _delegated(row: dict[str, str]) -> bool:
    role_signal = _normal(" ".join(row.get(key, "") for key in ("owner", "model tier")))
    if any(word in role_signal.split() for word in ("worker", "delegated", "delegate", "reviewer", "review")):
        return True
    routing = _normal(row.get("routing", ""))
    return (
        any(word in routing.split() for word in ("worker", "delegated", "delegate"))
        or routing == "reviewer"
        or routing.startswith("reviewer ")
    )


def _attempt_ids(value: str) -> list[str]:
    return [] if _normal(value) in NONE_VALUES else re.findall(r"\b[A-Za-z][A-Za-z0-9_-]*-A\d+\b", value)


def _scope_parts(value: str) -> set[str]:
    if _normal(value) in {"read only", *NONE_VALUES}:
        return set()
    parts = set()
    for part in re.split(r"<br\s*/?>|[,;]", value, flags=re.I):
        cleaned = re.sub(r"[`*]", "", part).strip().replace("\\", "/").lower().rstrip("/")
        if cleaned:
            parts.add(cleaned)
    return parts


def _has_wildcard(values: Iterable[str]) -> bool:
    return any(any(character in value for character in "*?[]") for value in values)


def _scopes_overlap(left: set[str], right: set[str]) -> set[str]:
    overlaps = set()
    for a in left:
        for b in right:
            if a == b or a.startswith(b + "/") or b.startswith(a + "/"):
                overlaps.add(a if len(a) <= len(b) else b)
    return overlaps


def _object(value: Any, path: str, issues: list[str]) -> dict[str, Any]:
    if not isinstance(value, dict):
        issues.append(f"{path} must be an object")
        return {}
    return value


def _nonnegative(value: Any, path: str, issues: list[str], nullable: bool = False) -> None:
    if value is None and nullable:
        return
    if isinstance(value, bool) or not isinstance(value, (int, float)) or value < 0:
        issues.append(f"{path} must be a non-negative number" + (" or null" if nullable else ""))


def validate_attempt(record: Any) -> list[str]:
    """Validate a compact delegated-attempt record."""
    issues: list[str] = []
    root = _object(record, "$", issues)
    if root.get("schemaVersion") != 1:
        issues.append("$.schemaVersion must equal 1")
    allowed = {"schemaVersion", "changeId", "taskId", "attemptId", "state", "taskDifficulty", "route", "usage", "elapsed", "scope", "startRevision", "endRevisionOrPatch", "review", "outcome"}
    if extras := sorted(set(root) - allowed):
        issues.append("unexpected top-level fields: " + ", ".join(extras))
    for key in ("changeId", "taskId", "attemptId", "startRevision"):
        if not _nonempty(root.get(key)):
            issues.append(f"$.{key} must be a non-empty string")
    attempt_state = root.get("state")
    if attempt_state not in {"active", "completed"}:
        issues.append("$.state must be active or completed")
    if not _nonempty(root.get("taskDifficulty")):
        issues.append("$.taskDifficulty must be a non-empty string")
    route = _object(root.get("route"), "$.route", issues)
    for key in ("tier", "requestedModel"):
        if not _nonempty(route.get(key)):
            issues.append(f"$.route.{key} must be a non-empty string")
    observed, source = route.get("observedModel"), route.get("observationSource")
    if (observed is None) != (source is None):
        issues.append("$.route observedModel and observationSource must both be set or both be null")
    if observed is not None and (not _nonempty(observed) or not _nonempty(source)):
        issues.append("$.route observedModel and observationSource must be non-empty when set")
    if observed is None and not _nonempty(route.get("modelTelemetryReason")):
        issues.append("$.route.modelTelemetryReason is required when model telemetry is unavailable")
    usage = _object(root.get("usage"), "$.usage", issues)
    availability = usage.get("availability")
    if availability not in AVAILABILITY:
        issues.append("$.usage.availability must be complete, partial, or unavailable")
    _nonnegative(usage.get("totalTokens"), "$.usage.totalTokens", issues, nullable=True)
    if availability == "complete" and usage.get("totalTokens") is None:
        issues.append("complete usage requires totalTokens")
    if availability == "unavailable":
        if usage.get("totalTokens") is not None:
            issues.append("unavailable usage must leave totalTokens null")
        if not _nonempty(usage.get("reason")):
            issues.append("unavailable usage requires reason")
    if availability == "partial" and not _nonempty(usage.get("reason")):
        issues.append("partial usage requires reason")
    elapsed = _object(root.get("elapsed"), "$.elapsed", issues)
    elapsed_availability = elapsed.get("availability")
    _nonnegative(elapsed.get("minutes"), "$.elapsed.minutes", issues, nullable=True)
    if attempt_state == "active":
        if any(elapsed.get(key) is not None for key in ("availability", "minutes", "reason")):
            issues.append("active attempt must leave elapsed telemetry null")
    else:
        if elapsed_availability not in AVAILABILITY:
            issues.append("$.elapsed.availability must be complete, partial, or unavailable")
        if elapsed_availability == "complete" and elapsed.get("minutes") is None:
            issues.append("complete elapsed telemetry requires minutes")
        if elapsed_availability == "unavailable" and elapsed.get("minutes") is not None:
            issues.append("unavailable elapsed telemetry must leave minutes null")
        if elapsed_availability in {"partial", "unavailable"} and not _nonempty(elapsed.get("reason")):
            issues.append(f"{elapsed_availability} elapsed telemetry requires reason")
    scope = root.get("scope")
    if not isinstance(scope, list) or not scope or not all(_nonempty(item) for item in scope):
        issues.append("$.scope must be a non-empty string array")
    elif _has_wildcard(scope):
        issues.append("$.scope must not contain wildcard paths")
    if attempt_state == "active" and root.get("endRevisionOrPatch") is not None:
        issues.append("active attempt must leave endRevisionOrPatch null")
    if attempt_state == "completed" and not _nonempty(root.get("endRevisionOrPatch")):
        issues.append("completed attempt requires endRevisionOrPatch")
    review = _object(root.get("review"), "$.review", issues)
    review_availability = review.get("usageAvailability")
    _nonnegative(review.get("totalTokens"), "$.review.totalTokens", issues, nullable=True)
    _nonnegative(review.get("activeMinutes"), "$.review.activeMinutes", issues, nullable=True)
    if attempt_state == "active":
        if any(review.get(key) is not None for key in ("usageAvailability", "totalTokens", "activeMinutes", "reason")):
            issues.append("active attempt must leave review telemetry null")
    else:
        if review_availability not in AVAILABILITY:
            issues.append("$.review.usageAvailability must be complete, partial, or unavailable")
        if review_availability == "complete" and review.get("totalTokens") is None:
            issues.append("complete review usage requires totalTokens")
        if review_availability == "unavailable" and review.get("totalTokens") is not None:
            issues.append("unavailable review usage must leave totalTokens null")
        if review_availability in {"partial", "unavailable"} and not _nonempty(review.get("reason")):
            issues.append(f"{review_availability} review usage requires reason")
    outcome = _object(root.get("outcome"), "$.outcome", issues)
    if attempt_state == "active":
        for key in ("verificationPassed", "qualityPassed", "scopePassed", "independentChallenge", "promoted", "escapedDefects", "retries", "leadCorrections", "reviewPasses", "escalations", "decision"):
            if outcome.get(key) is not None:
                issues.append(f"active attempt must leave $.outcome.{key} null")
    else:
        for key in ("verificationPassed", "qualityPassed", "scopePassed", "promoted"):
            if not isinstance(outcome.get(key), bool):
                issues.append(f"$.outcome.{key} must be boolean")
        if not _nonempty(outcome.get("independentChallenge")):
            issues.append("$.outcome.independentChallenge must be a non-empty string")
        for key in ("escapedDefects", "retries", "leadCorrections", "reviewPasses", "escalations"):
            _nonnegative(outcome.get(key), f"$.outcome.{key}", issues)
        if outcome.get("decision") not in DECISIONS:
            issues.append("$.outcome.decision must be accept, revise, promote, or reject")
    if outcome.get("decision") == "accept" and not all(outcome.get(key) is True for key in ("verificationPassed", "qualityPassed", "scopePassed")):
        issues.append("accept requires verificationPassed, qualityPassed, and scopePassed true")
    return issues


def validate_change_record(path: Path, repo: Path) -> list[str]:
    tables = _tables(path.read_text(encoding="utf-8"))
    plans = [table for table in tables if table.heading == "task plan"]
    if not plans:
        return ["missing ## Task plan table"]
    issues = ["multiple ## Task plan tables found"] if len(plans) > 1 else []
    plan = plans[0]
    if missing := sorted((BASE_COLUMNS | AUDIT_COLUMNS) - set(plan.headers)):
        issues.append("Task plan is missing columns: " + ", ".join(missing))
        return issues
    audit_by_attempt: dict[str, tuple[Path, dict[str, Any]]] = {}
    audit_dir = path.parent / "agent-audits"
    if audit_dir.exists():
        for audit_path in audit_dir.glob("*.json"):
            try:
                record = json.loads(audit_path.read_text(encoding="utf-8"))
                if not isinstance(record, dict):
                    issues.append(f"{audit_path.relative_to(repo)}: delegated audit must be a JSON object")
                    continue
                attempt_id = record.get("attemptId")
                if not isinstance(attempt_id, str) or not attempt_id.strip():
                    issues.append(f"{audit_path.relative_to(repo)}: attemptId must be a non-empty string")
                elif attempt_id in audit_by_attempt:
                    issues.append(f"duplicate delegated attempt ID {attempt_id}")
                else:
                    audit_by_attempt[attempt_id] = (audit_path, record)
            except (OSError, json.JSONDecodeError):
                issues.append(f"{audit_path.relative_to(repo)} is not valid JSON")
    task_ids, active_scopes, rows_by_id, referenced_attempts = set(), [], {}, set()
    for number, row in enumerate(plan.rows, start=1):
        task_id = row["id"].strip()
        label = task_id or f"row {number}"
        if not task_id:
            issues.append(f"{label}: ID is required")
        elif task_id in task_ids:
            issues.append(f"{label}: duplicate task ID")
        task_ids.add(task_id)
        rows_by_id[task_id] = row
        for field in BASE_COLUMNS | AUDIT_COLUMNS:
            if not _nonempty(row[field]):
                issues.append(f"{label}: {field} is required")
        metrics = _normal(row["result metrics"])
        if not any(metrics.startswith(value) for value in AVAILABILITY):
            issues.append(f"{label}: result metrics must start with usage availability")
        state = _normal(row["state"])
        if state not in VALID_STATES:
            issues.append(f"{label}: invalid state {row['state']!r}")
        task_scope = _scope_parts(row["write scope"])
        if _has_wildcard(task_scope):
            issues.append(f"{label}: write scope must not contain wildcard paths")
        if state in ACTIVE:
            active_scopes.append((label, task_scope))
        attempts, delegated = _attempt_ids(row["audit"]), _delegated(row)
        if len(attempts) != len(set(attempts)):
            issues.append(f"{label}: duplicate attempt ID in Audit")
        delegated = delegated or bool(attempts)
        if delegated and state in ACTIVE_OR_COMPLETE:
            for metric in ("retries", "corrections", "reviews"):
                if not re.search(rf"\b{metric}\s+\d+\b", metrics):
                    issues.append(f"{label}: delegated result metrics requires numeric {metric}")
        elif not delegated:
            for metric in ("retries", "corrections", "reviews"):
                if not re.search(rf"\b{metric}\s+(?:\d+|unavailable)\b", metrics):
                    issues.append(f"{label}: lead-only result metrics requires {metric} N or unavailable")
        referenced_attempts.update(attempts)
        if delegated and state in ACTIVE_OR_COMPLETE and not attempts:
            issues.append(f"{label}: delegated {state} task requires an attempt ID in Audit")
        if not delegated and attempts:
            issues.append(f"{label}: attempt IDs require delegated routing")
        completed_records: list[dict[str, Any]] = []
        for attempt_id in attempts:
            if attempt_id not in audit_by_attempt:
                issues.append(f"{label}: missing delegated audit for attempt {attempt_id}")
                continue
            audit_path, record = audit_by_attempt[attempt_id]
            issues.extend(f"{audit_path.relative_to(repo)}: {item}" for item in validate_attempt(record))
            if record.get("state") == "completed":
                completed_records.append(record)
            if record.get("taskId") != task_id:
                issues.append(f"{attempt_id}: identity.taskId must equal {task_id}")
            if record.get("changeId") != path.parent.name:
                issues.append(f"{attempt_id}: identity.changeId must equal {path.parent.name}")
            json_scope = _scope_parts(";".join(item for item in record.get("scope", []) if isinstance(item, str)))
            if json_scope != _scope_parts(row["write scope"]):
                issues.append(f"{attempt_id}: JSON writeScope must match Task plan")
            if state == "verified" and record.get("state") != "completed":
                issues.append(f"{attempt_id}: Verified task requires a completed attempt")
            if record.get("state") == "completed" and record.get("outcome", {}).get("verificationPassed") is True and record.get("outcome", {}).get("decision") == "accept":
                size = len(json.dumps(record, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
                if size >= 2500:
                    issues.append(f"{attempt_id}: normal successful delegated JSON must be below 2500 UTF-8 bytes")
        if state == "verified" and attempts and attempts[-1] in audit_by_attempt:
            final_record = audit_by_attempt[attempts[-1]][1]
            outcome = final_record.get("outcome", {})
            if final_record.get("state") != "completed" or outcome.get("decision") != "accept" or not all(outcome.get(key) is True for key in ("verificationPassed", "qualityPassed", "scopePassed")):
                issues.append(f"{attempts[-1]}: final attempt for Verified task must be completed and accepted with all gates true")
        if completed_records:
            availabilities = [record.get("usage", {}).get("availability") for record in completed_records]
            expected_availability = availabilities[0] if len(set(availabilities)) == 1 else "partial"
            expected_metrics = {
                "retries": sum(record.get("outcome", {}).get("retries", 0) for record in completed_records),
                "corrections": sum(record.get("outcome", {}).get("leadCorrections", 0) for record in completed_records),
                "reviews": sum(record.get("outcome", {}).get("reviewPasses", 0) for record in completed_records),
            }
            if not metrics.startswith(expected_availability):
                issues.append(f"{label}: Result metrics usage availability must equal delegated JSON ({expected_availability})")
            for metric, expected in expected_metrics.items():
                match = re.search(rf"\b{metric}\s+(\d+)\b", metrics)
                if match and int(match.group(1)) != expected:
                    issues.append(f"{label}: Result metrics {metric} must equal delegated JSON ({expected})")
    for attempt_id in sorted(set(audit_by_attempt) - referenced_attempts):
        issues.append(f"delegated audit {attempt_id} is not linked from the Task plan")
    for index, (left_id, left_scope) in enumerate(active_scopes):
        for right_id, right_scope in active_scopes[index + 1:]:
            if overlap := _scopes_overlap(left_scope, right_scope):
                issues.append(f"active write scopes overlap for {left_id} and {right_id}: {', '.join(sorted(overlap))}")
    for table in [item for item in tables if "verification failure ledger" in item.heading]:
        task_column = next((name for name in ("task", "task id", "linked task") if name in table.headers), None)
        state_column = next((name for name in ("state", "status") if name in table.headers), None)
        severity_column = next((name for name in ("severity", "materiality") if name in table.headers), None)
        if not task_column or not state_column:
            issues.append("verification failure ledger requires Task and State columns")
            continue
        for row in table.rows:
            linked = row.get(task_column, "").strip()
            closed = _normal(row.get(state_column, "")) in {"verified", "closed", "resolved", "rejected with reason"}
            material = severity_column is None or _normal(row.get(severity_column, "")) in {"material", "blocking", "high", "critical"}
            if linked in rows_by_id and not closed and material and _normal(rows_by_id[linked]["state"]) == "verified":
                issues.append(f"{linked}: Verified task has an open material verification failure")
    return issues


def _git_paths(repo: Path) -> list[Path]:
    patterns = ("docs/changes/*/README.md", "docs/changes/*/agent-audits/*.json")
    commands = (["git", "diff", "--name-only", "--diff-filter=ACMR", "HEAD", "--", *patterns], ["git", "ls-files", "--others", "--exclude-standard", "--", *patterns])
    names = set()
    for command in commands:
        result = subprocess.run(command, cwd=repo, text=True, capture_output=True, check=False)
        if result.returncode:
            raise RuntimeError(result.stderr.strip() or "git path discovery failed")
        names.update(line.strip() for line in result.stdout.splitlines() if line.strip())
    records = set()
    for name in names:
        path = Path(name)
        records.add(path if path.name == "README.md" else path.parent.parent / "README.md")
    return [repo / name for name in sorted(records)]


def _record_paths(paths: Iterable[Path]) -> list[Path]:
    result = []
    for path in paths:
        if path.is_dir():
            candidate = path / "README.md"
            result.extend([candidate] if candidate.exists() else path.glob("*/README.md"))
        else:
            result.append(path)
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="*", type=Path, help="change README or change directory (strict)")
    parser.add_argument("--repo", type=Path, default=Path.cwd(), help="repository root (default: cwd)")
    parser.add_argument("--changed", action="store_true", help="validate changed records (default with no paths)")
    parser.add_argument("--all", action="store_true", help="scan historical records diagnostically")
    parser.add_argument("--strict-history", action="store_true", help="make --all findings fail")
    args = parser.parse_args(argv)
    repo = args.repo.resolve()
    if args.paths and (args.changed or args.all):
        parser.error("paths cannot be combined with --changed or --all")
    if args.changed and args.all:
        parser.error("--changed and --all are mutually exclusive")
    try:
        paths = _record_paths((repo / p if not p.is_absolute() else p for p in args.paths)) if args.paths else (sorted((repo / "docs/changes").glob("*/README.md")) if args.all else _git_paths(repo))
    except RuntimeError as error:
        print(error, file=sys.stderr)
        return 2
    diagnostic, failed = args.all and not args.strict_history, False
    if not paths:
        print("No change records selected.")
        return 0
    for path in paths:
        try:
            issues = validate_change_record(path, repo)
        except (OSError, UnicodeError, RuntimeError) as error:
            issues = [str(error)]
        relative = path.relative_to(repo) if path.is_relative_to(repo) else path
        if issues:
            print(f"{relative}: {'diagnostic' if diagnostic else 'invalid'}")
            for issue in issues:
                print(f"  - {issue}")
            failed = failed or not diagnostic
        else:
            print(f"{relative}: valid")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
