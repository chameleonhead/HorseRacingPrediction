#!/usr/bin/env python3

import tempfile
import unittest
from pathlib import Path

from validate_change_records import inspect


class ValidateChangeRecordsTests(unittest.TestCase):
    def record(self, body: str) -> Path:
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        path = Path(directory.name) / "README.md"
        path.write_text(body, encoding="utf-8")
        return path

    def test_accepts_verified_implemented_record(self):
        path = self.record("""# Change

- Status: Implemented

## Acceptance criteria

| ID | Observable criterion | State |
| --- | --- | --- |
| AC1 | Works | Verified |
""")
        self.assertEqual([], inspect(path))

    def test_rejects_qualified_status_and_noncanonical_ac_state(self):
        path = self.record("""# Change

- Status: Implemented (production pending)

## Acceptance criteria

| ID | Observable criterion | State |
| --- | --- | --- |
| AC1 | Works | Ready; production pending |
""")
        issues = inspect(path)
        self.assertTrue(any("non-canonical Status" in issue for issue in issues))
        self.assertTrue(any("non-canonical AC state" in issue for issue in issues))

    def test_rejects_implemented_with_incomplete_ac(self):
        path = self.record("""# Change

- Status: Implemented

## Acceptance criteria

| ID | Observable criterion | State |
| --- | --- | --- |
| AC1 | Works | Connected |
""")
        self.assertIn("Implemented record contains an AC that is not Verified", inspect(path))

    def test_rejects_ac_list_without_state_table(self):
        path = self.record("""# Change

- Status: Approved

## Acceptance criteria

1. Works.
""")
        self.assertIn("Acceptance criteria is not a state table", inspect(path))

    def test_requires_jra_site_contract_impact_for_jra_collection_changes(self):
        path = self.record("""# Change

- Status: Proposed

RaceCardPageParserを変更する。
""")
        self.assertTrue(any("JRA site contract impact" in issue for issue in inspect(path)))

    def test_accepts_declared_jra_site_contract_impact(self):
        path = self.record("""# Change

- Status: Proposed
- JRA site contract impact: Updated — source matrixを更新した。

RaceResultPageParserを変更する。
""")
        self.assertEqual([], inspect(path))

    def test_schema_two_requires_concern_review(self):
        path = self.record("""# Change

- Status: Proposed
- Change record schema: 2

## Acceptance criteria

| ID | Observable criterion | State |
| --- | --- | --- |
| AC1 | Works | Not started |
""")
        self.assertTrue(any("requires a concern ledger" in issue for issue in inspect(path)))

    def test_approved_record_rejects_open_concern(self):
        path = self.record("""# Change

- Status: Approved
- Change record schema: 2

## Concern and agreement ledger

| ID | Concern | Agent position | User disposition | State |
| --- | --- | --- | --- | --- |
| C1 | Risk | Agree | Pending | Open decision |

## Acceptance criteria

| ID | Observable criterion | State |
| --- | --- | --- |
| AC1 | Works | Connected |
""")
        self.assertIn("Approved record contains an Open decision", inspect(path))

    def test_accepts_resolved_concern_or_reviewed_none(self):
        resolved = self.record("""# Change

- Status: Approved
- Change record schema: 2

## Concern and agreement ledger

| ID | Concern | Agent position | User disposition | State |
| --- | --- | --- | --- | --- |
| C1 | Risk | Agree | Approved | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | State |
| --- | --- | --- |
| AC1 | Works | Connected |
""")
        self.assertEqual([], inspect(resolved))
        reviewed_none = self.record("""# Change

- Status: Proposed
- Change record schema: 2
- Concern review: No material concern — low-risk documentation correction reviewed for scope and verification.
""")
        self.assertEqual([], inspect(reviewed_none))

    def test_approved_record_rejects_agent_objection_or_pending_user(self):
        path = self.record("""# Change

- Status: Approved
- Change record schema: 2

## Concern and agreement ledger

| ID | Concern | Agent position | User disposition | State |
| --- | --- | --- | --- | --- |
| C1 | Risk | Objection: unsafe | Pending | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | State |
| --- | --- | --- |
| AC1 | Works | Connected |
""")
        issues = inspect(path)
        self.assertTrue(any("unresolved agent objection" in issue for issue in issues))
        self.assertTrue(any("pending user disposition" in issue for issue in issues))


if __name__ == "__main__":
    unittest.main()
