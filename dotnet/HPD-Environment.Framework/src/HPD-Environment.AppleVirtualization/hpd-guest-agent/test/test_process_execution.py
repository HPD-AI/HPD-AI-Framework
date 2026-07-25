import importlib.util
import os
import pathlib
import subprocess
import tempfile
import unittest
from unittest import mock


AGENT_PATH = pathlib.Path(__file__).parents[1] / "src" / "hpd_guest_agent.py"
SPEC = importlib.util.spec_from_file_location("hpd_guest_agent_process", AGENT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class FakeProcess:
    def __init__(self):
        self.pid = 42
        self.returncode = 0

    def communicate(self, timeout=None):
        del timeout
        return b"out-err-out", None


class ProcessExecutionTests(unittest.TestCase):
    def setUp(self):
        self.state_directory = tempfile.TemporaryDirectory()
        self.environment = mock.patch.dict(
            os.environ,
            {"HPD_GUEST_AGENT_STATE_DIR": self.state_directory.name},
        )
        self.environment.start()
        self.agent = MODULE.GuestAgent("test", "1.0", "boot-test")

    def tearDown(self):
        self.environment.stop()
        self.state_directory.cleanup()

    def test_merged_stderr_launch_and_result_use_stdout_only(self):
        process = FakeProcess()
        start_request = {
            "RequestId": "start-1",
            "SequenceNumber": 1,
            "ProcessStartRequest": {
                "ProcessId": "process-1",
                "UnitId": "unit-1",
                "Command": {
                    "FileName": "/bin/echo",
                    "Arguments": ["hello"],
                },
                "Io": {
                    "MergeStandardError": True,
                },
            },
        }

        with mock.patch.object(MODULE.subprocess, "Popen", return_value=process) as popen:
            self.agent.process_start(start_request)

        self.assertIs(subprocess.STDOUT, popen.call_args.kwargs["stderr"])
        self.assertTrue(self.agent.processes["process-1"]["merged_standard_error"])

        wait_response = self.agent.process_wait({
            "RequestId": "wait-1",
            "SequenceNumber": 2,
            "ProcessLifecycleRequest": {
                "ProcessId": "process-1",
            },
        })
        output = wait_response["ProcessStatusResponse"]["Result"]["Output"]

        self.assertTrue(output["MergedStandardError"])
        self.assertEqual("b3V0LWVyci1vdXQ=", output["Stdout"]["CapturedBytes"])
        self.assertEqual(11, output["Stdout"]["BytesObserved"])
        self.assertEqual("", output["Stderr"]["CapturedBytes"])
        self.assertEqual(0, output["Stderr"]["BytesObserved"])
        self.assertEqual(0, output["Stderr"]["BytesCaptured"])
        self.assertEqual(0, output["Stderr"]["BytesDiscarded"])


if __name__ == "__main__":
    unittest.main()
