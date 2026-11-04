import importlib.util
import os
import pathlib
import tempfile
import unittest
from unittest import mock


AGENT_PATH = pathlib.Path(__file__).parents[1] / "src" / "hpd_guest_agent.py"
SPEC = importlib.util.spec_from_file_location(
    "hpd_guest_agent_authority", AGENT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class AuthorityTests(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.TemporaryDirectory()
        self.state = tempfile.TemporaryDirectory()
        root = pathlib.Path(self.root.name)
        self.source = root / "docker.sock"
        self.target = root / "authority" / "docker.sock"
        self.source.touch()
        self.target.parent.mkdir()
        self.target.symlink_to(self.source)
        self.environment = mock.patch.dict(
            os.environ,
            {
                "HPD_GUEST_AGENT_STATE_DIR": self.state.name,
                "HPD_GUEST_AGENT_ENGINE_SOCKET": str(self.source),
            },
        )
        self.environment.start()
        self.agent = MODULE.GuestAgent("test", "1.0", "boot-test")

    def tearDown(self):
        self.agent.shutdown()
        self.environment.stop()
        self.state.cleanup()
        self.root.cleanup()

    def test_revoke_reports_only_post_revocation_evidence(self):
        response = self.agent.authority_binding(
            {
                "RequestId": "authority-revoke-1",
                "SequenceNumber": 1,
                "AuthorityBindingRequest": {
                    "BindingId": "binding-1",
                    "Source": {},
                    "Target": {},
                    "Projection": {
                        "TargetSocketPath": {"Value": str(self.target)},
                    },
                },
            },
            46,
        )

        authority = response["AuthorityBindingResponse"]
        self.assertEqual(5, authority["BindingPhase"])
        self.assertEqual(2, authority["RevocationStatus"])
        self.assertFalse(self.target.exists())
        self.assertEqual(
            [3],
            [item["Kind"] for item in authority["RevocationEvidence"]],
        )
        self.assertTrue(authority["RevocationEvidence"][0]["Observed"])


if __name__ == "__main__":
    unittest.main()
