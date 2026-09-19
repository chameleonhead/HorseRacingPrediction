#!/usr/bin/env python3
"""Diagnose change-record Status and acceptance-state inconsistencies."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

STATUSES = {"Proposed", "Approved", "Implemented", "Superseded"}
AC_STATES = {"Not started", "Connected", "Verified"}
CONCERN_STATES = {"Resolved in design", "Accepted risk", "Excluded follow-up", "Open decision"}
JRA_CONTRACT_MARKER = re.compile(r"(?im)^- JRA site contract impact:\s*(Updated|None)\b")
JRA_CHANGE_SURFACE = re.compile(
    r"HorseRacingPrediction\.Scraping[/\\]Jra|JraNavigator|RaceCardPageParser|RaceResultPageParser|"
    r"race-entry-owners|owner migration|馬主.*マイグレーション",
    re.IGNORECASE,
)


def markdown_cells(line: str) -> list[str]:
    return [cell.strip() for cell in line.strip().strip("|").split("|")]


def inspect(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()
    issues: list[str] = []
    match = re.search(r"(?m)^- Status:\s*(.+?)\s*$", text)
    if not match:
        issues.append("missing Status")
        status = None
    else:
        status = match.group(1)
        if status not in STATUSES:
            issues.append(f"non-canonical Status: {status}")

    schema_match = re.search(r"(?m)^- Change record schema:\s*(\d+)\s*$", text)
    schema_version = int(schema_match.group(1)) if schema_match else 1
    concern_heading = next((i for i, line in enumerate(lines) if line.strip() == "## Concern and agreement ledger"), None)
    no_material_concern = re.search(r"(?im)^- Concern review:\s*No material concern\b", text)
    if schema_version >= 2 and concern_heading is None and not no_material_concern:
        issues.append("schema 2 record requires a concern ledger or a reviewed no-material-concern declaration")
    if concern_heading is not None:
        concern_table = next((i for i in range(concern_heading + 1, len(lines)) if lines[i].lstrip().startswith("|")), None)
        if concern_table is None:
            issues.append("Concern and agreement ledger is not a state table")
        else:
            concern_headers = markdown_cells(lines[concern_table])
            concern_state_index = next((i for i, value in enumerate(concern_headers) if value.lower() in {"state", "状態"}), None)
            agent_position_index = next((i for i, value in enumerate(concern_headers) if value.lower() == "agent position"), None)
            user_disposition_index = next((i for i, value in enumerate(concern_headers) if value.lower() == "user disposition"), None)
            if concern_state_index is None:
                issues.append("Concern and agreement ledger has no State column")
            else:
                if schema_version >= 2 and (agent_position_index is None or user_disposition_index is None):
                    issues.append("schema 2 concern ledger requires Agent position and User disposition columns")
                concern_states: list[str] = []
                for line in lines[concern_table + 2 :]:
                    if line.startswith("## "):
                        break
                    if not line.lstrip().startswith("|"):
                        continue
                    cells = markdown_cells(line)
                    if len(cells) <= concern_state_index or not re.fullmatch(r"C\d+", cells[0], re.IGNORECASE):
                        continue
                    concern_state = cells[concern_state_index]
                    concern_states.append(concern_state)
                    if concern_state not in CONCERN_STATES:
                        issues.append(f"{cells[0]} has non-canonical concern state: {concern_state}")
                    if status in {"Approved", "Implemented"}:
                        if agent_position_index is not None and len(cells) > agent_position_index and re.search(r"(?i)objection", cells[agent_position_index]):
                            issues.append(f"{status} record contains an unresolved agent objection in {cells[0]}")
                        if user_disposition_index is not None and len(cells) > user_disposition_index and re.search(r"(?i)pending|未確認|未合意", cells[user_disposition_index]):
                            issues.append(f"{status} record contains a pending user disposition in {cells[0]}")
                if not concern_states:
                    issues.append("Concern and agreement ledger has no concern rows")
                elif status in {"Approved", "Implemented"} and "Open decision" in concern_states:
                    issues.append(f"{status} record contains an Open decision")

    if JRA_CHANGE_SURFACE.search(text) and not JRA_CONTRACT_MARKER.search(text):
        issues.append("JRA collection change has no 'JRA site contract impact: Updated|None' declaration")

    heading = next((i for i, line in enumerate(lines) if line.strip() == "## Acceptance criteria"), None)
    if heading is None:
        return issues
    table_start = next((i for i in range(heading + 1, len(lines)) if lines[i].lstrip().startswith("|")), None)
    if table_start is None:
        issues.append("Acceptance criteria is not a state table")
        return issues
    headers = markdown_cells(lines[table_start])
    state_index = next((i for i, value in enumerate(headers) if value.lower() in {"state", "状態"}), None)
    if state_index is None:
        issues.append("Acceptance criteria table has no State column")
        return issues
    states: list[str] = []
    for line in lines[table_start + 2 :]:
        if line.startswith("## "):
            break
        if not line.lstrip().startswith("|"):
            continue
        cells = markdown_cells(line)
        if len(cells) <= state_index or "AC" not in cells[0].upper():
            continue
        state = cells[state_index]
        states.append(state)
        if state not in AC_STATES:
            issues.append(f"{cells[0]} has non-canonical AC state: {state}")
    if not states:
        issues.append("Acceptance criteria table has no AC rows")
    elif status == "Implemented" and any(state != "Verified" for state in states):
        issues.append("Implemented record contains an AC that is not Verified")
    elif status == "Approved" and all(state == "Verified" for state in states):
        has_explicit_remaining = "## Completion summary" in text or re.search(
            r"(?i)(remaining work|remaining approved work|残作業|未完了)", text
        )
        if not has_explicit_remaining:
            issues.append("Approved record has all ACs Verified and no explicit remaining-work ledger")
    return issues


def discover(inputs: list[Path]) -> list[Path]:
    found: set[Path] = set()
    for item in inputs:
        if item.is_file():
            found.add(item)
        elif item.is_dir():
            found.update(item.rglob("README.md"))
        else:
            raise FileNotFoundError(item)
    return sorted(found)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("paths", nargs="+", type=Path)
    args = parser.parse_args()
    issue_count = 0
    for path in discover(args.paths):
        for issue in inspect(path):
            issue_count += 1
            print(f"{path}: {issue}")
    print(f"Checked change records; issues={issue_count}")
    return 1 if issue_count else 0


if __name__ == "__main__":
    sys.exit(main())
