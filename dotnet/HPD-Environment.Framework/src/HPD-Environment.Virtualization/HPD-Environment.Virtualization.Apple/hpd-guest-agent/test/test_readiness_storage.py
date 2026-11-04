import importlib.util
import pathlib
import tempfile
import unittest
from unittest import mock


AGENT_PATH = pathlib.Path(__file__).parents[1] / "src" / "hpd_guest_agent.py"
SPEC = importlib.util.spec_from_file_location(
    "hpd_guest_agent_readiness_storage",
    AGENT_PATH,
)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ReadinessStorageTests(unittest.TestCase):
    def setUp(self):
        self.state_directory = tempfile.TemporaryDirectory()
        self.environment = mock.patch.dict(
            MODULE.os.environ,
            {"HPD_GUEST_AGENT_STATE_DIR": self.state_directory.name},
        )
        self.environment.start()
        self.agent = MODULE.GuestAgent("test", "1.0", "boot-test")

    def tearDown(self):
        self.environment.stop()
        self.state_directory.cleanup()

    @staticmethod
    def request():
        return {
            "ProtocolVersion": "1.0",
            "MessageType": 0,
            "Operation": 2,
            "RequestId": "storage-readiness-test",
            "SequenceNumber": 1,
        }

    def test_readiness_waits_for_live_storage_verification(self):
        with mock.patch.object(
            self.agent,
            "verified_storage_identity",
            side_effect=OSError("not mounted"),
        ):
            response = self.agent.handle(self.request())

        self.assertEqual(0, response["ResponseStatus"])
        self.assertFalse(response["Ready"]["IsReady"])
        self.assertIsNone(response["Ready"]["RuntimeFilesystemUuid"])
        self.assertIsNone(response["Ready"]["AppDataFilesystemUuid"])

    def test_readiness_publishes_only_live_verified_identities(self):
        with mock.patch.object(
            self.agent,
            "verified_storage_identity",
            side_effect=["runtime-uuid", "app-data-uuid"],
        ):
            response = self.agent.handle(self.request())

        self.assertEqual(0, response["ResponseStatus"])
        self.assertTrue(response["Ready"]["IsReady"])
        self.assertEqual(
            "runtime-uuid",
            response["Ready"]["RuntimeFilesystemUuid"],
        )
        self.assertEqual(
            "app-data-uuid",
            response["Ready"]["AppDataFilesystemUuid"],
        )

    def test_mountinfo_uses_exact_ext4_superblock_options(self):
        source, filesystem_type, options = self.agent.storage_mount_identity(
            "/var/lib/hpdos/app-data",
            "39 29 253:32 / /var/lib/hpdos/app-data rw,relatime "
            "- ext4 /dev/vdc rw,prjquota\n",
        )

        self.assertEqual("/dev/vdc", source)
        self.assertEqual("ext4", filesystem_type)
        self.assertEqual({"rw", "prjquota"}, options)

    def test_mountinfo_decodes_paths_and_rejects_parent_mounts(self):
        source, filesystem_type, options = self.agent.storage_mount_identity(
            "/var/lib/hpdos/app data",
            "38 29 253:32 / /var/lib/hpdos rw - ext4 /dev/vdc rw\n"
            "39 38 253:32 / /var/lib/hpdos/app\\040data rw "
            "- ext4 /dev/vdc rw,prjquota\n",
        )

        self.assertEqual("/dev/vdc", source)
        self.assertEqual("ext4", filesystem_type)
        self.assertIn("prjquota", options)

    def test_runtime_ext4_identity_does_not_claim_project_quotas(self):
        with mock.patch.object(
            MODULE.GuestAgent,
            "storage_mount_identity",
            return_value=("/dev/vdb", "ext4", {"rw"}),
        ), mock.patch.object(
            MODULE.subprocess,
            "run",
            return_value=mock.Mock(
                stdout="01234567-89ab-cdef-0123-456789abcdef\n"),
        ):
            identity = self.agent.storage_filesystem_identity(
                "/var/lib/hpdos/runtime",
                "ext4",
            )

        self.assertEqual(
            "guest-runtime:01234567-89ab-cdef-0123-456789abcdef",
            identity,
        )

    def test_durable_ext4_identity_still_requires_project_quotas(self):
        with mock.patch.object(
            MODULE.GuestAgent,
            "storage_mount_identity",
            return_value=("/dev/vdc", "ext4", {"rw"}),
        ):
            with self.assertRaisesRegex(OSError, "project quotas"):
                self.agent.storage_filesystem_identity(
                    "/var/lib/hpdos/app-data",
                    "ext4-project",
                )


if __name__ == "__main__":
    unittest.main()
