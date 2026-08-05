import importlib.util
import io
import json
import pathlib
import unittest
from unittest import mock


AGENT_PATH = pathlib.Path(__file__).parents[1] / "src" / "hpd_guest_agent.py"
SPEC = importlib.util.spec_from_file_location("hpd_guest_agent_shutdown", AGENT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class HostShutdownTests(unittest.TestCase):
    def request(self):
        return {
            "ProtocolVersion": "1.0",
            "MessageType": 0,
            "Operation": 52,
            "RequestId": "shutdown-test",
            "SequenceNumber": 1,
            "HostId": "host-a",
            "ProviderGeneration": 7,
            "HostStartGeneration": 3,
            "HostShutdownRequest": {
                "HostId": "host-a",
                "ProviderGeneration": 7,
                "HostStartGeneration": 3,
                "Reason": "test",
            },
        }

    def test_flushes_acceptance_before_executing_identity_bound_shutdown(self):
        writer = io.BytesIO()
        observations = []

        def execute():
            observations.append(writer.getvalue())

        agent = MODULE.GuestAgent(
            "0.1.0",
            "1.0",
            guest_boot_id="boot-a",
            host_shutdown_executor=execute,
        )
        reader = io.BytesIO(
            json.dumps(self.request()).encode("utf-8") + b"\n")

        MODULE.serve_stream(agent, reader, writer)

        response = json.loads(writer.getvalue())
        self.assertEqual(1, response["ResponseStatus"])
        self.assertTrue(response["HostShutdownResponse"]["Accepted"])
        self.assertEqual("host-a", response["HostId"])
        self.assertEqual([writer.getvalue()], observations)

    def test_rejects_mismatched_identity_without_poweroff(self):
        executed = []
        agent = MODULE.GuestAgent(
            "0.1.0",
            "1.0",
            guest_boot_id="boot-a",
            host_shutdown_executor=lambda: executed.append(True),
        )
        request = self.request()
        request["HostShutdownRequest"]["HostStartGeneration"] = 4

        response = agent.handle(request)

        self.assertEqual(2, response["ResponseStatus"])
        self.assertEqual(
            "AppleVirtualization.GuestAgentHostShutdownIdentityInvalid",
            response["Error"]["Code"],
        )
        self.assertEqual([], executed)

    def test_rejects_missing_payload_without_poweroff(self):
        executed = []
        agent = MODULE.GuestAgent(
            "0.1.0",
            "1.0",
            guest_boot_id="boot-a",
            host_shutdown_executor=lambda: executed.append(True),
        )
        request = self.request()
        del request["HostShutdownRequest"]

        response = agent.handle(request)

        self.assertEqual(2, response["ResponseStatus"])
        self.assertEqual(
            "AppleVirtualization.GuestAgentHostShutdownRequestMissing",
            response["Error"]["Code"],
        )
        self.assertEqual([], executed)

    def test_executor_failure_occurs_only_after_acceptance_is_flushed(self):
        writer = io.BytesIO()

        def fail():
            raise OSError("worker unavailable")

        agent = MODULE.GuestAgent(
            "0.1.0",
            "1.0",
            guest_boot_id="boot-a",
            host_shutdown_executor=fail,
        )

        reader = io.BytesIO(
            json.dumps(self.request()).encode("utf-8") + b"\n")

        with self.assertRaisesRegex(OSError, "worker unavailable"):
            MODULE.serve_stream(agent, reader, writer)

        response = json.loads(writer.getvalue())
        self.assertEqual(1, response["ResponseStatus"])
        self.assertTrue(response["HostShutdownResponse"]["Accepted"])

    @mock.patch.object(MODULE.subprocess, "Popen")
    @mock.patch.object(MODULE.os, "sync")
    @mock.patch.object(MODULE.GuestAgent, "_write_shutdown_diagnostic")
    def test_starts_openrc_shutdown_outside_the_service_cgroup(
            self, diagnostic, sync, popen):
        process = object()
        popen.return_value = process

        result = MODULE.GuestAgent._request_poweroff()

        diagnostic.assert_called_once_with(
            "HPDOS_GUEST_SHUTDOWN: root-cgroup OpenRC shutdown requested")
        sync.assert_called_once_with()
        self.assertIs(process, result)
        popen.assert_called_once_with(
            [
                "/bin/sh",
                "-c",
                "printf '%s\\n' \"$$\" > /sys/fs/cgroup/cgroup.procs; "
                "exec /sbin/openrc shutdown",
            ],
            stdin=MODULE.subprocess.DEVNULL,
            stdout=MODULE.subprocess.DEVNULL,
            stderr=MODULE.subprocess.DEVNULL,
            close_fds=True,
            start_new_session=True,
        )


if __name__ == "__main__":
    unittest.main()
