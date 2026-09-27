#!/usr/bin/env python3
"""Reconcile the governing route ledger with the source inventory and cutover gates."""

from __future__ import annotations

import re
from collections import defaultdict
from pathlib import Path
from urllib.parse import urlsplit


ROOT = Path(__file__).resolve().parent
ROUTE_ID = re.compile(r"^(CP|SC|CM|RO|ER|PS)-\d{2}$")
ROUTE = re.compile(r"^(GET|POST|PUT|PATCH|DELETE) `(/[^`]+)`$")
MERGE = re.compile(r"^Merge \(((?:CP|SC|CM|RO|ER|PS)-\d{2})/((?:CP|SC|CM|RO|ER|PS)-\d{2})\)$")


def route_rows(path: Path) -> dict[str, list[str]]:
    rows: dict[str, list[str]] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.startswith("| "):
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if cells and ROUTE_ID.fullmatch(cells[0]):
            route_id = cells[0]
            if route_id in rows:
                raise SystemExit(f"duplicate route ID {route_id} in {path.name}")
            rows[route_id] = cells
    return rows


def parse_route(cell: str, route_id: str, column: str) -> tuple[str, str]:
    match = ROUTE.fullmatch(cell.strip())
    if not match:
        raise SystemExit(f"{route_id}: invalid {column} method/path: {cell!r}")
    return match.group(1), match.group(2)


source = route_rows(ROOT / "route-map.md")
readme = route_rows(ROOT / "README.md")
if len(source) != 69 or len(readme) != 69:
    raise SystemExit(f"expected 69 route IDs in both maps; source={len(source)}, README={len(readme)}")
if source.keys() != readme.keys():
    raise SystemExit(
        f"route ID mismatch; missing={sorted(source.keys() - readme.keys())}; "
        f"extra={sorted(readme.keys() - source.keys())}"
    )

controller_ids = {key for key in source if key.startswith(("CP-", "SC-", "CM-", "RO-", "ER-"))}
schedule_ids = {key for key in source if key.startswith("PS-")}
if (len(controller_ids), len(schedule_ids)) != (65, 4):
    raise SystemExit(f"expected 65 CollectionController + 4 PredictionScheduling; found {len(controller_ids)} + {len(schedule_ids)}")

legacy_exact: set[tuple[str, str]] = set()
canonical_routes: dict[tuple[str, str], list[tuple[str, str, str]]] = defaultdict(list)
groups: dict[str, list[str]] = defaultdict(list)
replacements = 0
merges = 0
deletions = 0
for row in readme.values():
    method, uri = parse_route(row[1], row[0], "current")
    legacy_exact.add((method, urlsplit(uri).path))

for route_id, src in source.items():
    row = readme[route_id]
    if len(src) < 9 or len(row) < 7:
        raise SystemExit(f"{route_id}: malformed route row")
    if src[1] != row[1]:
        raise SystemExit(f"{route_id}: old method/path differs; route-map={src[1]!r}; README={row[1]!r}")

    current_method, current_uri = parse_route(row[1], route_id, "current")
    for label, value in (("disposition", row[2]), ("behavior/invariant", row[4]), ("endpoint file", row[5]), ("consumer/test evidence", row[6])):
        if not value.strip():
            raise SystemExit(f"{route_id}: missing required {label}")

    if row[2] == "Delete":
        deletions += 1
        if row[3] != "— (no canonical target or handler)" or row[5] != "None — delete route registration; no replacement endpoint file.":
            raise SystemExit(f"{route_id}: Delete must have no canonical target or replacement handler")
        if route_id != "CP-24":
            raise SystemExit(f"{route_id}: only the evidenced unused CP-24 route is currently approved for deletion")
        canonical_method = canonical_uri = None
    else:
        canonical_method, canonical_uri = parse_route(row[3], route_id, "canonical")
        if not canonical_uri.startswith("/api/v2/"):
            raise SystemExit(f"{route_id}: canonical URI must begin /api/v2/: {canonical_uri}")

    if row[2] == "Replace":
        replacements += 1
    elif row[2] != "Delete":
        match = MERGE.fullmatch(row[2])
        if not match:
            raise SystemExit(f"{route_id}: disposition must be Replace or a named merge pair")
        left, right = match.groups()
        if left == right or route_id not in (left, right):
            raise SystemExit(f"{route_id}: invalid merge-pair membership in {row[2]!r}")
        groups[row[2]].append(route_id)
        merges += 1

    if row[2] != "Delete":
        route_path = urlsplit(canonical_uri).path
        if (canonical_method, route_path) in legacy_exact:
            raise SystemExit(f"{route_id}: canonical exact method/path collides with legacy route {canonical_method} {route_path}")
        canonical_routes[(canonical_method, route_path)].append((route_id, row[2], row[5]))

    if src[2] != row[3]:
        raise SystemExit(f"{route_id}: route-map target differs from README: {src[2]!r} vs {row[3]!r}")
    if src[7] != row[5]:
        raise SystemExit(f"{route_id}: route-map endpoint target differs from README: {src[7]!r} vs {row[5]!r}")

required_contract_evidence = {
    "CP-13": ("LegacyRaceDetailMergeReport", "zero-work report", "no operation ID/idempotency key"),
    "CP-15": ("RaceEntryOwnerMigrationProgress", "migration:race-entry-owners:v2", "409"),
    "CP-17": ('{"cancellationRequested":true}', "204", "409"),
    "CP-24": ("zero first-party call site", "no canonical target", "replacement HTTP operation", "old exact", "404/405", "internal dispatcher", "calls the store directly", "restart-retry", "no behavior or data-model migration"),
    "CP-46": ("leaseToken", "after Completed returns 204", "stale token"),
    "CM-04": ("CollectionKnownRecoveryExecution", "no operation/idempotency key", "never blindly replay"),
}
for route_id, required_values in required_contract_evidence.items():
    contract = readme[route_id][4].casefold()
    missing_values = [value for value in required_values if value.casefold() not in contract]
    if missing_values:
        raise SystemExit(f"{route_id}: behavior/invariant contract lacks source-derived evidence: {missing_values}")

if (replacements, merges, deletions) != (48, 20, 1):
    raise SystemExit(f"expected 48 Replace + 20 Merge + 1 Delete; found {replacements} + {merges} + {deletions}")
if len(groups) != 10:
    raise SystemExit(f"expected ten merge groups, found {len(groups)}")
for label, members in groups.items():
    expected = list(MERGE.fullmatch(label).groups())
    if len(members) != 2 or sorted(members) != sorted(expected):
        raise SystemExit(f"{label}: merge group must contain exactly its two declared rows, found {members}")
    contracts = []
    for member_id in members:
        method, uri = parse_route(readme[member_id][3], member_id, "canonical")
        contracts.append((urlsplit(uri).path, readme[member_id][5]))
    if len({path for path, _ in contracts}) != 1 or len({target for _, target in contracts}) != 1:
        raise SystemExit(f"{label}: merge pair must converge on one URI path and one endpoint file")

# A shared URI/path is legal only as one declared pair, implemented by one endpoint file.
for route, members in canonical_routes.items():
    if len(members) == 1:
        continue
    member_ids = {item[0] for item in members}
    member_groups = {item[1] for item in members}
    endpoint_files = {item[2] for item in members}
    if len(members) != 2 or len(member_groups) != 1 or next(iter(member_groups)) == "Replace" or len(endpoint_files) != 1:
        raise SystemExit(f"canonical route collision {route}: rows must be one declared merge pair with one endpoint contract; rows={members}")

print("PASS: 69 IDs/old pairs; 65 + 4 scope; 48 Replace + 20 Merge/10 pairs + CP-24 Delete; required fields/contracts, /api/v2 namespace, no legacy collisions, CP-24 zero-callsite/deletion proof, merge targets and route-map synchronization verified.")
