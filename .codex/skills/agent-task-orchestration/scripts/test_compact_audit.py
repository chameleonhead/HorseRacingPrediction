import copy
import unittest
from audit_agent_execution import validate_record


class CompactAuditTests(unittest.TestCase):
    def setUp(self):
        self.record = dict(schemaVersion=2, changeId="change", taskId="T2", attemptId="T2-A1",
                           state="active", taskDifficulty="low", startRevision="sha",
                           acceptanceCriteria=["AC2"], scope=["file"],
                           route=dict(tier="worker", requestedModel="gpt-5.6-luna",
                                      observedModel=None, modelTelemetryReason="unavailable"),
                           usage=dict(availability="unavailable", totalTokens=None, reason="unavailable"),
                           outcome=None, endRevisionOrPatch=None)

    def test_active_template_is_valid_without_fabricated_results(self):
        self.assertEqual([], validate_record(self.record))

    def test_rejects_prefilled_completion(self):
        self.record["outcome"] = {"decision": "accept"}
        self.assertTrue(validate_record(self.record))

    def test_completed_requires_independent_success_and_patch(self):
        self.record.update(state="completed", endRevisionOrPatch="diff", review={}, overhead={}, elapsed={},
                           outcome=dict(verificationPassed=True, qualityPassed=True, scopePassed=True,
                                        promoted=False, retries=0, leadCorrections=0, reviewPasses=1,
                                        escapedDefects=0, escalations=0, independentChallenge="counterexample",
                                        reviewMode="ac-group", decision="accept"))
        self.assertEqual([], validate_record(self.record))
        broken = copy.deepcopy(self.record)
        broken["outcome"]["verificationPassed"] = False
        self.assertTrue(validate_record(broken))
        self.record["endRevisionOrPatch"] = None
        self.assertTrue(validate_record(self.record))

    def test_rejects_secret_and_inferred_model(self):
        self.record["apiKey"] = "fixture"
        self.record["route"]["observedModel"] = "unproven"
        self.assertGreaterEqual(len(validate_record(self.record)), 2)


if __name__ == "__main__":
    unittest.main()
