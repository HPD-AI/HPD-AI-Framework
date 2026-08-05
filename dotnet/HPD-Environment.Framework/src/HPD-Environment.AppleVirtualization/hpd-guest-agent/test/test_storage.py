import importlib.util
import base64
import json
import os
import pathlib
import tempfile
import unittest
from unittest import mock


AGENT_PATH = pathlib.Path(__file__).parents[1] / "src" / "hpd_guest_agent.py"
SPEC = importlib.util.spec_from_file_location("hpd_guest_agent_storage", AGENT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class StorageTests(unittest.TestCase):
    def setUp(self):
        self.storage_directory = tempfile.TemporaryDirectory()
        self.state_directory = tempfile.TemporaryDirectory()
        self.engine_directory = tempfile.TemporaryDirectory()
        self.environment = mock.patch.dict(
            os.environ,
            {
                "HPD_GUEST_APP_DATA_ROOT": self.storage_directory.name,
                "HPD_GUEST_RUNTIME_ROOT": self.engine_directory.name,
                "HPD_GUEST_AGENT_STATE_DIR": self.state_directory.name,
                "HPD_GUEST_OPERATION_TEMP_ROOT":
                    str(pathlib.Path(self.state_directory.name) / "operations"),
                "HPD_GUEST_STORAGE_QUOTA_MODE": "ext4-project",
                "HPD_GUEST_APP_DATA_FILESYSTEM_ID":
                    "guest-app-data:test-filesystem",
            },
        )
        self.environment.start()
        self.agent = MODULE.GuestAgent("test", "1.0", "boot-test")
        self.filesystem_identity = mock.patch.object(
            self.agent,
            "storage_filesystem_identity",
            return_value="guest-app-data:test-filesystem",
        )
        self.quota_command = mock.patch.object(
            self.agent,
            "run_quota_command",
        )
        self.project_identity = mock.patch.object(
            self.agent,
            "verify_project_identity",
        )
        self.filesystem_identity.start()
        self.run_quota_command = self.quota_command.start()
        self.verify_project_identity = self.project_identity.start()

    def tearDown(self):
        self.agent.shutdown()
        self.project_identity.stop()
        self.quota_command.stop()
        self.filesystem_identity.stop()
        self.environment.stop()
        self.state_directory.cleanup()
        self.storage_directory.cleanup()
        self.engine_directory.cleanup()

    def test_pool_measurement_selects_exact_storage_class_root(self):
        app_data = self.agent.handle(self.request(0, None))
        runtime_request = self.request(0, None)
        runtime_request["StorageRequest"]["StorageClass"] = \
            "runtime-disposable"
        runtime = self.agent.handle(runtime_request)

        self.assertNotIn("Error", app_data)
        self.assertNotIn("Error", runtime)
        invalid = self.request(0, None)
        invalid["StorageRequest"]["StorageClass"] = \
            "operation-temporary"
        self.assertEqual(
            "AppleVirtualization.StorageClassInvalid",
            self.agent.handle(invalid)["Error"]["Code"])

    def test_volume_ensure_observe_detach_and_erase_are_generation_bound(self):
        ensured = self.agent.handle(self.request(1, "volume-a"))
        ensured_storage = ensured["StorageResponse"]
        self.assertTrue(ensured_storage["Exists"])
        self.assertTrue(ensured_storage["Attached"])
        self.assertEqual("host-a", ensured_storage["HostId"])
        self.assertEqual(7, ensured_storage["ProviderGeneration"])
        self.assertEqual(3, ensured_storage["HostStartGeneration"])
        volume_path = pathlib.Path(
            ensured_storage["EffectiveRuntimePath"])
        self.assertTrue(volume_path.is_dir())
        self.assertRegex(
            ensured_storage["FilesystemIdentity"],
            r"^guest-app-data:test-filesystem:project:[1-9][0-9]+$")

        (volume_path / "content.bin").write_bytes(b"persistent")
        observed = self.agent.handle(self.request(2, "volume-a"))
        self.assertEqual(
            len(b"persistent"),
            observed["StorageResponse"]["UsedBytes"]["Value"])
        detached = self.agent.handle(self.request(3, "volume-a"))
        self.assertTrue(detached["StorageResponse"]["Exists"])
        self.assertFalse(detached["StorageResponse"]["Attached"])
        self.assertEqual(
            b"persistent",
            (volume_path / "content.bin").read_bytes())

        erased = self.agent.handle(self.request(4, "volume-a"))
        self.assertFalse(erased["StorageResponse"]["Exists"])
        self.assertFalse(volume_path.exists())
        self.assertEqual(
            [],
            list((pathlib.Path(self.storage_directory.name) / "volumes")
                 .glob(".erase-*")))

    def test_invalid_identity_is_rejected_before_filesystem_mutation(self):
        for logical_id in (
                "../escape",
                ".",
                "..",
                "",
                "slash/name",
                "x" * 129):
            with self.subTest(logical_id=logical_id):
                response = self.agent.handle(
                    self.request(1, logical_id))
                self.assertEqual(
                    "AppleVirtualization.StorageIdentityInvalid",
                    response["Error"]["Code"])
        self.assertFalse(
            (pathlib.Path(self.storage_directory.name).parent / "escape")
            .exists())

    def test_symlink_volume_and_symlink_content_are_rejected(self):
        volumes = pathlib.Path(self.storage_directory.name) / "volumes"
        volumes.mkdir(mode=0o700)
        external = pathlib.Path(self.storage_directory.name) / "external"
        external.mkdir()
        (volumes / "linked-volume").symlink_to(
            external,
            target_is_directory=True)

        linked_volume = self.agent.handle(
            self.request(2, "linked-volume"))
        self.assertEqual(
            "AppleVirtualization.StorageOperationFailed",
            linked_volume["Error"]["Code"])

        ensured = self.agent.handle(self.request(1, "safe-volume"))
        safe_path = pathlib.Path(
            ensured["StorageResponse"]["EffectiveRuntimePath"])
        (safe_path / "linked-content").symlink_to(
            external,
            target_is_directory=True)
        linked_content = self.agent.handle(
            self.request(2, "safe-volume"))
        self.assertEqual(
            "AppleVirtualization.StorageOperationFailed",
            linked_content["Error"]["Code"])

    def test_missing_generation_and_invalid_action_fail_closed(self):
        missing_generation = self.request(0, None)
        missing_generation["StorageRequest"]["ProviderGeneration"] = 0
        response = self.agent.handle(missing_generation)
        self.assertEqual(
            "AppleVirtualization.StorageGenerationMissing",
            response["Error"]["Code"])

        invalid_action = self.request(99, "volume")
        response = self.agent.handle(invalid_action)
        self.assertEqual(
            "AppleVirtualization.StorageActionInvalid",
            response["Error"]["Code"])

    def test_quota_metadata_is_stable_and_overage_fails_closed(self):
        ensured = self.agent.handle(self.request(1, "volume-quota"))
        self.assertNotIn("Error", ensured)
        volume_path = pathlib.Path(
            ensured["StorageResponse"]["EffectiveRuntimePath"])
        quota_path = (
            pathlib.Path(self.storage_directory.name) /
            "volumes" /
            ".hpd-volume-quotas-v1.json"
        )
        first_state = quota_path.read_bytes()

        repeated = self.agent.handle(self.request(1, "volume-quota"))
        self.assertNotIn("Error", repeated)
        self.assertEqual(first_state, quota_path.read_bytes())
        self.assertEqual(
            ensured["StorageResponse"]["FilesystemIdentity"],
            repeated["StorageResponse"]["FilesystemIdentity"])

        request = self.request(2, "volume-quota")
        request["StorageRequest"]["MaximumBytes"] = {"Value": 4}
        conflict = self.agent.handle(request)
        self.assertEqual(
            "AppleVirtualization.StorageOperationFailed",
            conflict["Error"]["Code"])

        (volume_path / "large.bin").write_bytes(b"x" * 1048577)
        overage = self.agent.handle(self.request(2, "volume-quota"))
        self.assertEqual(
            "AppleVirtualization.StorageOperationFailed",
            overage["Error"]["Code"])

    def test_volume_ownership_and_generation_are_exact(self):
        ensured = self.agent.handle(self.request(1, "owned-volume"))
        self.assertNotIn("Error", ensured)

        changed_workload = self.request(2, "owned-volume")
        changed_workload["StorageRequest"]["OwnerResourceId"] = "workload-b"
        response = self.agent.handle(changed_workload)
        self.assertEqual(
            "AppleVirtualization.StorageOperationFailed",
            response["Error"]["Code"])

        changed_generation = self.request(4, "owned-volume")
        changed_generation["StorageRequest"]["VolumeGeneration"] = 12
        response = self.agent.handle(changed_generation)
        self.assertEqual(
            "AppleVirtualization.StorageOperationFailed",
            response["Error"]["Code"])

        missing_owner = self.request(2, "owned-volume")
        missing_owner["StorageRequest"]["OwnerScopeId"] = None
        response = self.agent.handle(missing_owner)
        self.assertEqual(
            "AppleVirtualization.StorageOwnershipInvalid",
            response["Error"]["Code"])

    def test_streamed_backup_and_restore_are_durable_and_generation_bound(self):
        ensured = self.agent.handle(self.request(1, "portable-volume"))
        volume_path = pathlib.Path(
            ensured["StorageResponse"]["EffectiveRuntimePath"])
        (volume_path / "empty").mkdir()
        (volume_path / "nested").mkdir()
        (volume_path / "nested" / "data.txt").write_text(
            "original-durable-data",
            encoding="utf-8")

        begin = self.transfer_request(5, "portable-volume", "backup-a")
        prepared = self.agent.handle(begin)["StorageResponse"]
        self.assertEqual(len(b"original-durable-data"), prepared["LogicalBytes"])
        self.assertEqual(3, prepared["EntryCount"])
        encoded = bytearray()
        while len(encoded) < prepared["EncodedPayloadBytes"]:
            read = self.transfer_request(6, "portable-volume", "backup-a")
            read["StorageRequest"]["Offset"] = len(encoded)
            read["StorageRequest"]["MaximumChunkBytes"] = 17
            response = self.agent.handle(read)["StorageResponse"]
            encoded.extend(base64.b64decode(response["ChunkBase64"], validate=True))
        self.assertEqual(prepared["EncodedPayloadBytes"], len(encoded))
        self.assertNotIn(
            "Error",
            self.agent.handle(
                self.transfer_request(7, "portable-volume", "backup-a")))

        self.assertNotIn(
            "Error",
            self.agent.handle(
                self.transfer_request(7, "portable-volume", "backup-a")))

        (volume_path / "nested" / "data.txt").write_text(
            "mutated",
            encoding="utf-8")
        begin_restore = self.transfer_request(
            8, "portable-volume", "restore-a")
        begin_restore["StorageRequest"].update({
            "ExpectedContentSha256": prepared["ContentSha256"],
            "ExpectedLogicalBytes": prepared["LogicalBytes"],
        })
        self.assertNotIn("Error", self.agent.handle(begin_restore))
        for offset in range(0, len(encoded), 19):
            chunk = bytes(encoded[offset:offset + 19])
            write = self.transfer_request(
                9, "portable-volume", "restore-a")
            write["StorageRequest"].update({
                "Offset": offset,
                "ChunkBase64": base64.b64encode(chunk).decode("ascii"),
            })
            self.assertNotIn("Error", self.agent.handle(write))

        commit = self.transfer_request(10, "portable-volume", "restore-a")
        commit["StorageRequest"].update({
            "ExpectedContentSha256": prepared["ContentSha256"],
            "ExpectedEncodedPayloadBytes": len(encoded),
            "ExpectedLogicalBytes": prepared["LogicalBytes"],
            "ExpectedEntryCount": prepared["EntryCount"],
        })
        committed = self.agent.handle(commit)["StorageResponse"]
        self.assertTrue(committed["Completed"])
        self.assertEqual(prepared["ContentSha256"], committed["ContentSha256"])
        self.assertEqual(12, committed["VolumeGeneration"])
        self.assertEqual(
            "original-durable-data",
            (volume_path / "nested" / "data.txt").read_text(encoding="utf-8"))
        self.assertTrue((volume_path / "empty").is_dir())
        staging_identity = str(
            pathlib.Path(self.storage_directory.name) /
            "volumes" /
            ".restore-restore-a")
        project_id = self.agent.volume_project_id("portable-volume")
        self.assertIn(
            mock.call([
                "setproject", "-P", str(project_id), staging_identity,
            ]),
            self.run_quota_command.mock_calls)
        self.assertIn(
            mock.call(str(volume_path), project_id),
            self.verify_project_identity.mock_calls)
        recovered_with_old_authority = self.agent.handle(
            self.request(2, "portable-volume"))
        self.assertNotIn("Error", recovered_with_old_authority)
        self.assertEqual(
            12,
            recovered_with_old_authority["StorageResponse"]["VolumeGeneration"])

        finalized = self.agent.handle(
            self.transfer_request(11, "portable-volume", "restore-a"))
        self.assertNotIn("Error", finalized)
        observed = self.request(2, "portable-volume")
        observed["StorageRequest"]["VolumeGeneration"] = 12
        self.assertNotIn("Error", self.agent.handle(observed))

    def test_maximum_backup_chunk_fits_the_helper_json_frame(self):
        ensured = self.agent.handle(self.request(1, "bounded-volume"))
        volume_path = pathlib.Path(
            ensured["StorageResponse"]["EffectiveRuntimePath"])
        (volume_path / "payload.bin").write_bytes(b"x" * 50000)

        begin = self.transfer_request(5, "bounded-volume", "backup-frame")
        self.agent.handle(begin)["StorageResponse"]
        read = self.transfer_request(6, "bounded-volume", "backup-frame")
        read["StorageRequest"]["Offset"] = 0
        read["StorageRequest"]["MaximumChunkBytes"] = 43008
        response = self.agent.handle(read)

        frame = json.dumps(
            response,
            ensure_ascii=True,
            separators=(",", ":"),
        ).encode("utf-8")
        self.assertLessEqual(len(frame), 65536)
        self.assertEqual(
            43008,
            len(base64.b64decode(
                response["StorageResponse"]["ChunkBase64"],
                validate=True)))

    def test_restore_rejects_nonsequential_and_noncanonical_chunks(self):
        self.assertNotIn(
            "Error",
            self.agent.handle(self.request(1, "restore-bounds")))
        begin = self.transfer_request(8, "restore-bounds", "restore-b")
        begin["StorageRequest"].update({
            "ExpectedContentSha256": "0" * 64,
            "ExpectedLogicalBytes": 0,
        })
        self.assertNotIn("Error", self.agent.handle(begin))

        skipped = self.transfer_request(9, "restore-bounds", "restore-b")
        skipped["StorageRequest"].update({
            "Offset": 1,
            "ChunkBase64": "YQ==",
        })
        self.assertEqual(
            "AppleVirtualization.StorageOperationFailed",
            self.agent.handle(skipped)["Error"]["Code"])
        malformed = self.transfer_request(9, "restore-bounds", "restore-b")
        malformed["StorageRequest"].update({
            "Offset": 0,
            "ChunkBase64": "YQ",
        })
        self.assertEqual(
            "AppleVirtualization.StorageOperationFailed",
            self.agent.handle(malformed)["Error"]["Code"])
        self.assertNotIn(
            "Error",
            self.agent.handle(
                self.transfer_request(11, "restore-bounds", "restore-b")))

    def test_backup_rejects_hard_link_confusion(self):
        ensured = self.agent.handle(self.request(1, "hard-linked"))
        volume_path = pathlib.Path(
            ensured["StorageResponse"]["EffectiveRuntimePath"])
        source = volume_path / "source"
        source.write_bytes(b"same-inode")
        os.link(source, volume_path / "alias")

        response = self.agent.handle(
            self.transfer_request(5, "hard-linked", "backup-linked"))

        self.assertEqual(
            "AppleVirtualization.StorageOperationFailed",
            response["Error"]["Code"])

    @classmethod
    def transfer_request(cls, action, logical_id, operation_id):
        request = cls.request(action, logical_id)
        request["StorageRequest"]["OperationId"] = operation_id
        return request

    @staticmethod
    def request(action, logical_id):
        return {
            "ProtocolVersion": "1.0",
            "Operation": 50,
            "RequestId": "storage-test",
            "SequenceNumber": 1,
            "StorageRequest": {
                "HostId": "host-a",
                "ProviderGeneration": 7,
                "HostStartGeneration": 3,
                "Action": action,
                "StorageClass": "app-durable",
                "LogicalVolumeId": logical_id,
                "MaximumBytes": {"Value": 1048576},
                "OwnerScopeId": "app-installation-a",
                "OwnerResourceId": "workload-a",
                "DeclarationId": "data",
                "CompatibilityDomain": "test-data-v1",
                "VolumeGeneration": 11,
            },
        }


if __name__ == "__main__":
    unittest.main()
