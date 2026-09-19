import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("audit_agent_execution.py")
SPEC = importlib.util.spec_from_file_location("audit_agent_execution", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


def valid_record():
    return {
        "schemaVersion": 2,
        "identity": {"changeId": "c", "taskId": "T1", "attemptId": "A1", "taskClass": "bounded-code", "risk": "low", "startedAt": "s", "completedAt": "e"},
        "difficulty": {"ambiguity": "low", "executionPaths": 1, "publicContract": False, "persistenceOrMigration": False, "concurrency": False, "securityOrPrivacy": False, "externalDependency": False, "existingTestCoverage": "strong", "expectedWriteScope": ["x"]},
        "routing": {"expectedTier": "low-cost-coding", "requestedModel": "gpt-5.6-luna", "requestedReasoningEffort": "medium", "observedModel": None, "observationSource": None, "verificationState": "unavailable", "unavailableReason": "not exposed"},
        "usage": {"availability": "unavailable", "inputTokens": None, "cachedInputTokens": None, "outputTokens": None, "reasoningTokens": None, "totalTokens": None, "configuredBudget": None, "truncated": False, "unavailableReason": "not exposed"},
        "instruction": {"contractFingerprint": "sha256:x", "objective": True, "scope": True, "dependencies": True, "acceptance": True, "evidence": True, "escalation": True, "outputFormat": True},
        "reproducibility": {"startRevision": "a", "endRevisionOrPatch": "b", "skills": ["agent-task-orchestration"], "toolConfigProfile": "default", "providerModelVersion": None},
        "attribution": {"startStatusCaptured": True, "workerPatchIdentified": True, "parallelOwnersRecorded": True, "unattributedChanges": False},
        "quality": {"acceptancePassed": True, "verificationPassed": True, "scopePassed": True, "blockingFindings": 0, "nonBlockingFindings": 0, "workerRetries": 0, "leadCorrectionFiles": 0, "leadCorrectionLines": 0, "promoted": False, "independentChallenge": "existing suite", "regressionPassed": True},
        "reviewEffort": {"reviewerTier": "lead", "reviewerModel": None, "reviewUsageTokens": None, "reviewPasses": 1, "elapsedReviewMinutes": 3, "pureReviewMinutes": 1, "correctionMinutes": 0, "reverificationMinutes": 1, "auditOverheadMinutes": 1, "totalActiveMinutes": 3, "availability": "partial", "source": "log"},
        "cost": {"currency": None, "priceSource": None, "priceObservedAt": None, "worker": None, "automatedReview": None, "humanReview": None, "rework": None, "auditOverhead": None, "totalSuccessfulOutcome": None, "reviewBurdenRatio": None, "humanHourlyRate": None},
        "baseline": {"comparable": False, "auditIds": [], "reason": "none"},
        "escapedDefects": [],
        "outcome": {"verdict": "pass-with-telemetry-gap", "leadDecision": "accept", "recommendation": "collect"},
    }


class AuditTests(unittest.TestCase):
    def test_valid_telemetry_gap(self):
        self.assertEqual([], MODULE.validate_record(valid_record()))

    def test_requested_model_is_not_observed_model(self):
        record = valid_record()
        record["routing"]["observedModel"] = record["routing"]["requestedModel"]
        self.assertTrue(any("must not assert observedModel" in x for x in MODULE.validate_record(record)))

    def test_model_mismatch_requires_fail(self):
        record = valid_record()
        record["routing"].update({"observedModel": "gpt-5.6-terra", "observationSource": "runtime", "verificationState": "mismatch"})
        record["outcome"]["verdict"] = "fail"
        self.assertEqual([], MODULE.validate_record(record))

    def test_scope_failure_cannot_pass(self):
        record = valid_record()
        record["quality"]["scopePassed"] = False
        self.assertTrue(any("verdict must be fail" in x for x in MODULE.validate_record(record)))

    def test_forbidden_prompt_and_secret_fields(self):
        record = valid_record()
        record["promptText"] = "do work"
        record["nested"] = {"secret": "value"}
        issues = MODULE.validate_record(record)
        self.assertEqual(2, len([x for x in issues if "is forbidden" in x]))

    def test_human_cost_requires_explicit_rate_and_price_source(self):
        record = valid_record()
        record["cost"]["humanReview"] = 10
        issues = MODULE.validate_record(record)
        self.assertTrue(any("explicit humanHourlyRate" in x for x in issues))
        self.assertTrue(any("priceSource" in x for x in issues))

    def test_total_cost_and_review_burden_are_reconciled(self):
        record = valid_record()
        record["cost"].update({
            "currency": "USD", "priceSource": "contract", "priceObservedAt": "2026-09-19",
            "worker": 4, "automatedReview": 2, "humanReview": 0, "rework": 1,
            "auditOverhead": 1, "totalSuccessfulOutcome": 8, "reviewBurdenRatio": 0.5
        })
        self.assertEqual([], MODULE.validate_record(record))
        record["cost"]["reviewBurdenRatio"] = 0.25
        self.assertTrue(any("reviewBurdenRatio must equal" in x for x in MODULE.validate_record(record)))

    def test_unattributed_work_fails_quality_gate(self):
        record = valid_record()
        record["attribution"]["unattributedChanges"] = True
        self.assertTrue(any("verdict must be fail" in x for x in MODULE.validate_record(record)))

    def test_review_time_components_must_be_exclusive_and_reconcile(self):
        record = valid_record()
        record["reviewEffort"]["totalActiveMinutes"] = 2
        self.assertTrue(any("exclusive review-time components" in x for x in MODULE.validate_record(record)))

    def test_summary_requires_five_comparable_samples_in_one_group(self):
        four = [copy.deepcopy(valid_record()) for _ in range(4)]
        for record in four:
            record["baseline"] = {"comparable": True, "auditIds": ["baseline"], "reason": None}
        group = MODULE.summarize(four)["groups"][0]
        self.assertEqual("collect-more-comparable-successful-samples", group["recommendation"])
        fifth = copy.deepcopy(four[0])
        group = MODULE.summarize(four + [fifth])["groups"][0]
        self.assertEqual("eligible-for-one-step-reviewed-adjustment", group["recommendation"])

    def test_non_comparable_fifth_sample_does_not_open_gate(self):
        records = [copy.deepcopy(valid_record()) for _ in range(5)]
        for record in records[:4]:
            record["baseline"] = {"comparable": True, "auditIds": ["baseline"], "reason": None}
        group = MODULE.summarize(records)["groups"][0]
        self.assertEqual(4, group["eligibleComparableSuccessful"])
        self.assertNotEqual("eligible-for-one-step-reviewed-adjustment", group["recommendation"])

    def test_mixed_profiles_are_never_pooled(self):
        records = [copy.deepcopy(valid_record()) for _ in range(5)]
        for record in records:
            record["baseline"] = {"comparable": True, "auditIds": ["baseline"], "reason": None}
        records[-1]["identity"]["risk"] = "medium"
        summary = MODULE.summarize(records)
        self.assertEqual(2, summary["comparisonGroups"])
        self.assertFalse(any(g["recommendation"] == "eligible-for-one-step-reviewed-adjustment" for g in summary["groups"]))

    def test_escaped_defect_suspends_route_recommendation(self):
        records = [copy.deepcopy(valid_record()) for _ in range(5)]
        records[0]["escapedDefects"] = [{"severity": "material", "reference": "incident-1"}]
        self.assertEqual("review-or-suspend-affected-route", MODULE.summarize(records)["groups"][0]["recommendation"])

    def test_cli_validates_and_summarizes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "audit.json"
            path.write_text(json.dumps(valid_record()), encoding="utf-8")
            self.assertEqual(0, MODULE.main(["--summary", str(path)]))


if __name__ == "__main__":
    unittest.main()
