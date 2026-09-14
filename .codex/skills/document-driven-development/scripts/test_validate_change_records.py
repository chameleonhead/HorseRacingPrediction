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


if __name__ == "__main__":
    unittest.main()
