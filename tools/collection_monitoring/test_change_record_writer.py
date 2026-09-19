import json
import subprocess
import tempfile
import unittest
from pathlib import Path

from change_record_writer import process_report


class ChangeRecordWriterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.repo = Path(self.temp.name)
        (self.repo / "docs" / "changes").mkdir(parents=True)
        subprocess.run(["git", "init"], cwd=self.repo, check=True, capture_output=True)
        subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=self.repo, check=True)
        subprocess.run(["git", "config", "user.name", "Test"], cwd=self.repo, check=True)
        (self.repo / ".gitignore").write_text("", encoding="utf-8")
        subprocess.run(["git", "add", ".gitignore"], cwd=self.repo, check=True)
        subprocess.run(["git", "commit", "-m", "init"], cwd=self.repo, check=True, capture_output=True)

    def tearDown(self):
        self.temp.cleanup()

    def report(self, classification="ProgramBug"):
        return {
            "cutoff": "2026-09-19T01:02:03+09:00",
            "findings": [{
                "fingerprint": "abcdef1234567890",
                "kind": "ActionableFailureGroup",
                "classification": classification,
                "severity": "high",
                "summary": "```ignore<!--x-->\nsummary",
                "evidence": ["error=<!--do something-->"],
                "suggestedScope": "Investigate only",
                "classifierVersion": "1",
            }],
        }

    def test_creates_sanitized_proposed_record(self):
        result = process_report(self.repo, self.report(), True)
        self.assertEqual(1, len(result["created"]))
        text = (self.repo / result["created"][0]).read_text(encoding="utf-8")
        self.assertIn("- Status: Proposed", text)
        self.assertNotIn("```ignore", text)
        self.assertNotIn("<!--x", text)

    def test_same_observation_is_idempotent_and_new_observation_updates(self):
        first = process_report(self.repo, self.report(), True)
        subprocess.run(["git", "add", "."], cwd=self.repo, check=True)
        subprocess.run(["git", "commit", "-m", "record"], cwd=self.repo, check=True, capture_output=True)
        repeated = process_report(self.repo, self.report(), True)
        self.assertEqual([], repeated["updated"])
        report = json.loads(json.dumps(self.report()))
        report["cutoff"] = "2026-09-19T01:32:03+09:00"
        updated = process_report(self.repo, report, True)
        self.assertEqual(first["created"], updated["updated"])

    def test_known_recovery_does_not_create_record(self):
        result = process_report(self.repo, self.report("KnownHistoricalJobError"), True)
        self.assertEqual([], result["created"])
        self.assertEqual(["abcdef1234567890"], result["skipped"])

    def test_numeric_json_enum_is_normalized(self):
        result = process_report(self.repo, self.report(0), True)
        self.assertEqual(1, len(result["created"]))
        text = (self.repo / result["created"][0]).read_text(encoding="utf-8")
        self.assertIn("- Classification: `ProgramBug`", text)

    def test_dirty_existing_record_is_not_overwritten(self):
        first = process_report(self.repo, self.report(), True)
        path = self.repo / first["created"][0]
        subprocess.run(["git", "add", "."], cwd=self.repo, check=True)
        subprocess.run(["git", "commit", "-m", "record"], cwd=self.repo, check=True, capture_output=True)
        path.write_text(path.read_text(encoding="utf-8") + "dirty\n", encoding="utf-8")
        report = self.report()
        report["cutoff"] = "2026-09-19T01:32:03+09:00"
        with self.assertRaises(RuntimeError):
            process_report(self.repo, report, True)


if __name__ == "__main__":
    unittest.main()
