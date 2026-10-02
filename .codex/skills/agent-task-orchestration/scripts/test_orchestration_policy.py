import re
import unittest
from pathlib import Path


SKILL = Path(__file__).parents[1] / "SKILL.md"


def skill_parts():
    text = SKILL.read_text(encoding="utf-8")
    match = re.match(r"---\s*\n(?P<header>.*?)\n---\s*\n(?P<body>.*)", text, re.DOTALL)
    if not match:
        raise AssertionError("SKILL.md must have YAML frontmatter")
    return match.group("header"), match.group("body")


class OrchestrationPolicyTests(unittest.TestCase):
    def test_discovery_covers_serialized_repository_work(self):
        header, body = skill_parts()
        description = re.search(r"^description:\s*(.+)$", header, re.MULTILINE)
        self.assertIsNotNone(description)
        declared_scope = description.group(1).casefold()
        self.assertIn("repository development", declared_scope)
        self.assertIn("investigation", declared_scope)
        self.assertIn("serialized", declared_scope)
        self.assertIn("do not use", body.casefold())
        self.assertIn("needs neither repository investigation nor a repository change", body.casefold())

    def test_first_write_gate_requires_readiness_evidence(self):
        _, body = skill_parts()
        gate = re.search(r"## First implementation-write gate\s+(.*?)(?=\n## |\Z)", body, re.DOTALL)
        self.assertIsNotNone(gate)
        policy = gate.group(1).casefold()
        requirements = (
            "before the first implementation edit",
            "loaded in the current turn",
            "purpose",
            "investigation findings",
            "files to change",
            "ordered implementation steps",
            "unresolved specification questions",
            "verification commands",
            "requested model",
            "requested model/reasoning",
            "executor accepted",
            "executor availability",
            "task state",
            "observed model",
            "otherwise retain null with a reason",
            "canonical audit command",
            "python scripts/audit_agent_execution.py <change-record-path>",
            "from the repository root",
            "other validators apply only when their governed artifact and current state support pre-edit validation",
            "record why a check does not apply",
            "ddd approval remains a separate",
            "externally blocked",
            "implementation files remain untouched",
            "approval alone",
            "lead-owned planning",
            "read-only repository investigation",
        )
        for requirement in requirements:
            with self.subTest(requirement=requirement):
                self.assertIn(requirement, policy)


if __name__ == "__main__":
    unittest.main()
