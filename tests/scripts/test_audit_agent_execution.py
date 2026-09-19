import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/audit_agent_execution.py"
SPEC = importlib.util.spec_from_file_location("repository_audit_validator", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)

HEADERS = "ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics"
LEAD = "T1 | Lead work | Main | Lead | Approval | src/a.cs | unit test | test passed | Verified | Lead: public contract | none | unavailable; retries 0; corrections 0; reviews 1"
WORKER = "T2 | Worker work | audit worker | Worker | T1 | src/b.cs | unit test | test passed | Verified | delegated bounded code | T2-A1 | unavailable; retries 0; corrections 0; reviews 1"


def markdown(rows, failures=""):
    separator = " | ".join(["---"] * len(HEADERS.split(" | ")))
    return f"""# Fixture

- Status: Approved

## Task plan

| {HEADERS} |
| {separator} |
{''.join(f'| {row} |\n' for row in rows)}
{failures}"""


def markdown_v2(rows, criteria=None, failures=""):
    criteria = criteria or [("AC1", "T1"), ("AC2", "T2")]
    ac_rows = "".join(f"| {ac_id} | observable outcome | {tasks} | command evidence | Verified |\n" for ac_id, tasks in criteria)
    return markdown(rows, failures).replace(
        "- Status: Approved",
        "- Status: Approved\n- Orchestration schema: 2\n\n## Acceptance criteria\n\n"
        "| ID | Observable criterion | Tasks | Verification | State |\n"
        "| --- | --- | --- | --- | --- |\n" + ac_rows,
    )


def attempt():
    return {
        "schemaVersion": 1,
        "changeId": "20260920_fixture",
        "taskId": "T2",
        "attemptId": "T2-A1",
        "state": "completed",
        "taskDifficulty": "bounded low-risk code",
        "route": {"tier": "worker", "requestedModel": "worker-model", "observedModel": None, "observationSource": None, "modelTelemetryReason": "runtime did not expose model metadata"},
        "usage": {"availability": "unavailable", "totalTokens": None, "reason": "runtime did not expose per-worker usage"},
        "elapsed": {"availability": "unavailable", "minutes": None, "reason": "runtime did not expose worker elapsed time"},
        "scope": ["src/b.cs"],
        "startRevision": "abc",
        "endRevisionOrPatch": "working-tree:T2",
        "review": {"usageAvailability": "unavailable", "totalTokens": None, "activeMinutes": None, "reason": "review telemetry was not exposed"},
        "outcome": {"verificationPassed": True, "qualityPassed": True, "scopePassed": True, "independentChallenge": "validator tests", "promoted": False, "escapedDefects": 0, "retries": 0, "leadCorrections": 0, "reviewPasses": 1, "escalations": 0, "decision": "accept"},
    }


def attempt_v2():
    record = attempt()
    record["schemaVersion"] = 2
    record["acceptanceCriteria"] = ["AC2"]
    record["route"]["requestedModel"] = "gpt-5.6-luna"
    record["overhead"] = {"availability": "unavailable", "preparationMinutes": None, "integrationMinutes": None, "auditMinutes": None, "reason": "active effort telemetry was not captured"}
    record["outcome"]["reviewMode"] = "ac-group"
    record["outcome"]["detailReviewReason"] = None
    return record


class CompactAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.repo = Path(self.temp.name)
        self.change = self.repo / "docs/changes/20260920_fixture"
        self.change.mkdir(parents=True)

    def tearDown(self):
        self.temp.cleanup()

    def write(self, text, audit=None):
        path = self.change / "README.md"
        path.write_text(text, encoding="utf-8")
        if audit is not None:
            records = audit if isinstance(audit, list) else [audit]
            for record in records:
                attempt_id = record.get("attemptId") if isinstance(record, dict) and isinstance(record.get("attemptId"), str) else "T2-A1"
                audit_path = self.change / f"agent-audits/{attempt_id}.json"
                audit_path.parent.mkdir(exist_ok=True)
                audit_path.write_text(json.dumps(record, separators=(",", ":")), encoding="utf-8")
        return path

    def issues(self, text, audit=None):
        return MODULE.validate_change_record(self.write(text, audit), self.repo)

    def test_lead_only_uses_existing_task_plan_without_json(self):
        self.assertEqual([], self.issues(markdown([LEAD])))

    def test_lead_routing_may_describe_final_review_without_delegation(self):
        row = LEAD.replace("Lead: public contract", "Lead: final review")
        self.assertIn("Lead: final review", row)
        self.assertEqual([], self.issues(markdown([row])))

    def test_historical_lead_metrics_may_be_explicitly_unavailable(self):
        row = LEAD.replace("retries 0; corrections 0; reviews 1",
                           "retries unavailable; corrections unavailable; reviews unavailable")
        self.assertEqual([], self.issues(markdown([row])))

    def test_only_three_audit_columns_are_added_and_fixture_is_small(self):
        text = markdown([LEAD, WORKER])
        self.assertLess(sum(1 for line in text.splitlines() if line.strip()), 20)
        self.assertEqual(MODULE.BASE_COLUMNS | MODULE.AUDIT_COLUMNS, set(MODULE._tables(text)[0].headers))

    def test_missing_audit_column_is_rejected(self):
        text = markdown([LEAD]).replace(" | Result metrics", "")
        self.assertTrue(any("missing columns" in issue for issue in self.issues(text)))

    def test_delegated_verified_task_requires_attempt(self):
        row = WORKER.replace("T2-A1", "none")
        self.assertTrue(any("requires an attempt" in issue for issue in self.issues(markdown([row]))))

    def test_valid_compact_attempt_is_accepted_and_below_size_budget(self):
        record = attempt()
        self.assertLess(len(json.dumps(record, separators=(",", ":")).encode("utf-8")), 2500)
        self.assertEqual([], MODULE.validate_attempt(record))
        self.assertEqual([], self.issues(markdown([LEAD, WORKER]), record))

    def test_schema_two_accepts_concrete_model_and_ac_group_review(self):
        record = attempt_v2()
        self.assertLess(len(json.dumps(record, separators=(",", ":")).encode("utf-8")), 2500)
        self.assertEqual([], self.issues(markdown_v2([LEAD, WORKER]), record))

    def test_schema_two_rejects_abstract_requested_model(self):
        for model in ("runtime-default", "cost-sensitive-coding-worker"):
            with self.subTest(model=model):
                record = attempt_v2()
                record["route"]["requestedModel"] = model
                self.assertTrue(any("concrete model ID" in issue for issue in self.issues(markdown_v2([LEAD, WORKER]), record)))

    def test_schema_two_requires_audit_ac_membership_to_match_task_mapping(self):
        record = attempt_v2()
        record["acceptanceCriteria"] = ["AC1"]
        self.assertTrue(any("Task-to-AC mapping" in issue for issue in self.issues(markdown_v2([LEAD, WORKER]), record)))

    def test_schema_two_requires_specific_lead_non_delegation_reason(self):
        vague = LEAD.replace("Lead: public contract", "Lead: keep with lead")
        issues = self.issues(markdown_v2([vague], [("AC1", "T1")]))
        self.assertTrue(any("concrete non-delegation reason" in issue for issue in issues))
        self.assertEqual([], self.issues(markdown_v2([LEAD], [("AC1", "T1")])))

    def test_detailed_review_requires_trigger_reason(self):
        record = attempt_v2()
        record["outcome"]["reviewMode"] = "detailed"
        self.assertTrue(any("detailReviewReason" in issue for issue in MODULE.validate_attempt(record)))
        record["outcome"]["detailReviewReason"] = "scope ownership mismatch"
        self.assertEqual([], MODULE.validate_attempt(record))

    def test_schema_two_tracks_preparation_integration_and_audit_overhead(self):
        record = attempt_v2()
        record["overhead"] = {"availability": "complete", "preparationMinutes": 4, "integrationMinutes": 3, "auditMinutes": 1, "reason": None}
        self.assertEqual([], MODULE.validate_attempt(record))
        record["overhead"]["integrationMinutes"] = None
        self.assertTrue(any("complete overhead" in issue for issue in MODULE.validate_attempt(record)))

    def test_ac_group_review_does_not_require_detail_reason(self):
        record = attempt_v2()
        self.assertEqual([], MODULE.validate_attempt(record))
        record["outcome"]["detailReviewReason"] = "routine microtask inspection"
        self.assertTrue(any("must leave" in issue for issue in MODULE.validate_attempt(record)))

    def test_unavailable_usage_requires_reason(self):
        record = attempt()
        record["usage"]["reason"] = ""
        issues = MODULE.validate_attempt(record)
        self.assertTrue(any("unavailable usage requires" in issue for issue in issues))

    def test_unavailable_model_and_review_usage_require_reasons(self):
        record = attempt()
        record["route"]["modelTelemetryReason"] = ""
        record["review"]["reason"] = ""
        issues = MODULE.validate_attempt(record)
        self.assertTrue(any("modelTelemetryReason" in issue for issue in issues))
        self.assertTrue(any("review usage requires" in issue for issue in issues))

    def test_partial_telemetry_requires_reasons(self):
        record = attempt()
        record["usage"].update({"availability": "partial", "totalTokens": 10, "reason": ""})
        record["elapsed"].update({"availability": "partial", "minutes": 2, "reason": ""})
        record["review"].update({"usageAvailability": "partial", "totalTokens": 5, "reason": ""})
        issues = MODULE.validate_attempt(record)
        self.assertTrue(any("partial usage" in issue for issue in issues))
        self.assertTrue(any("partial elapsed" in issue for issue in issues))
        self.assertTrue(any("partial review" in issue for issue in issues))

    def test_active_attempt_leaves_review_elapsed_and_outcome_null(self):
        record = attempt()
        record["state"] = "active"
        record["endRevisionOrPatch"] = None
        record["elapsed"] = {"availability": None, "minutes": None, "reason": None}
        record["review"] = {"usageAvailability": None, "totalTokens": None, "activeMinutes": None, "reason": None}
        record["outcome"] = {key: None for key in record["outcome"]}
        self.assertEqual([], MODULE.validate_attempt(record))
        record["elapsed"]["availability"] = "unavailable"
        record["review"]["usageAvailability"] = "unavailable"
        issues = MODULE.validate_attempt(record)
        self.assertTrue(any("elapsed telemetry null" in issue for issue in issues))
        self.assertTrue(any("review telemetry null" in issue for issue in issues))

    def test_failed_first_attempt_and_successful_final_attempt_are_valid(self):
        first = attempt()
        first["outcome"].update({"verificationPassed": False, "qualityPassed": False, "scopePassed": True, "retries": 1, "leadCorrections": 1, "reviewPasses": 1, "decision": "revise"})
        second = attempt()
        second["attemptId"] = "T2-A2"
        row = WORKER.replace("T2-A1", "T2-A1, T2-A2").replace("retries 0", "retries 1").replace("corrections 0", "corrections 1").replace("reviews 1", "reviews 2")
        self.assertEqual([], self.issues(markdown([row]), [first, second]))

    def test_failed_final_attempt_cannot_verify_task(self):
        record = attempt()
        record["outcome"].update({"verificationPassed": False, "qualityPassed": False, "decision": "revise"})
        self.assertTrue(any("final attempt" in issue for issue in self.issues(markdown([WORKER]), record)))

    def test_result_metrics_must_match_completed_attempt(self):
        row = WORKER.replace("retries 0", "retries 2").replace("corrections 0", "corrections 3").replace("reviews 1", "reviews 4")
        issues = self.issues(markdown([row]), attempt())
        self.assertTrue(any("retries must equal" in issue for issue in issues))
        self.assertTrue(any("corrections must equal" in issue for issue in issues))
        self.assertTrue(any("reviews must equal" in issue for issue in issues))

    def test_attempt_identity_and_scope_must_match_task_plan(self):
        record = attempt()
        record["taskId"] = "T9"
        record["scope"] = ["src/other.cs"]
        issues = self.issues(markdown([WORKER]), record)
        self.assertTrue(any("identity.taskId" in issue for issue in issues))
        self.assertTrue(any("writeScope must match" in issue for issue in issues))

    def test_delegated_read_only_scope_matches_compact_attempt(self):
        row = WORKER.replace("src/b.cs", "read-only")
        record = attempt()
        record["scope"] = ["read-only"]
        self.assertEqual([], self.issues(markdown([row]), record))

    def test_unlinked_attempt_json_is_rejected(self):
        self.assertTrue(any("not linked" in issue for issue in self.issues(markdown([LEAD]), attempt())))

    def test_duplicate_and_non_string_attempt_ids_are_rejected(self):
        duplicate_row = WORKER.replace("T2-A1", "T2-A1, T2-A1")
        self.assertTrue(any("duplicate attempt" in issue for issue in self.issues(markdown([duplicate_row]), attempt())))
        record = attempt()
        record["attemptId"] = 7
        self.assertTrue(any("attemptId must be" in issue for issue in self.issues(markdown([WORKER]), record)))

    def test_malformed_non_object_audit_is_rejected(self):
        self.assertTrue(any("JSON object" in issue for issue in self.issues(markdown([WORKER]), ["not", "an", "object"])))

    def test_wildcard_scope_is_rejected(self):
        row = WORKER.replace("src/b.cs", "src/*.cs")
        record = attempt()
        record["scope"] = ["src/*.cs"]
        issues = self.issues(markdown([row]), record)
        self.assertTrue(any("wildcard" in issue for issue in issues))

    def test_explicit_attempt_establishes_delegation_for_reviewer_route(self):
        row = WORKER.replace("audit worker", "independent reviewer").replace("Worker", "Review").replace("delegated bounded code", "Reviewer: independent challenge")
        self.assertEqual([], self.issues(markdown([row]), attempt()))

    def test_overlapping_active_scopes_are_rejected(self):
        left = LEAD.replace("Verified", "In progress").replace("src/a.cs", "src")
        right = LEAD.replace("T1", "T3", 1).replace("Verified", "Runnable")
        self.assertTrue(any("write scopes overlap" in issue for issue in self.issues(markdown([left, right]))))

    def test_open_material_failure_blocks_verified_task(self):
        failures = """
## Verification failure ledger

| ID | Linked task | Severity | Status |
| --- | --- | --- | --- |
| VF1 | T1 | Material | Open |
"""
        self.assertTrue(any("open material" in issue for issue in self.issues(markdown([LEAD], failures))))

    def test_nonmaterial_or_closed_failure_does_not_block(self):
        failures = """
## Verification failure ledger

| ID | Task | Severity | State |
| --- | --- | --- | --- |
| VF1 | T1 | Minor | Open |
| VF2 | T1 | Material | Verified |
"""
        self.assertEqual([], self.issues(markdown([LEAD], failures)))

    def test_all_is_diagnostic_while_explicit_path_is_strict(self):
        path = self.write("# Historical\n")
        self.assertEqual(0, MODULE.main(["--repo", str(self.repo), "--all"]))
        self.assertEqual(1, MODULE.main(["--repo", str(self.repo), str(path)]))

    def test_changed_audit_json_maps_to_parent_readme(self):
        outputs = [
            SimpleNamespace(returncode=0, stdout="docs/changes/20260920_fixture/agent-audits/T2-A1.json\n", stderr=""),
            SimpleNamespace(returncode=0, stdout="", stderr=""),
        ]
        with mock.patch.object(MODULE.subprocess, "run", side_effect=outputs):
            self.assertEqual([self.change / "README.md"], MODULE._git_paths(self.repo))


if __name__ == "__main__":
    unittest.main()
