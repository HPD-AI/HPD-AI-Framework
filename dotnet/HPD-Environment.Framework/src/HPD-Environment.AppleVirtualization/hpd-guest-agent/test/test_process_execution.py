import base64
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

    def test_status_and_cursor_output_observe_independent_process_exit(self):
        start = self.agent.process_start({
            "RequestId": "start-real",
            "SequenceNumber": 1,
            "ProcessStartRequest": {
                "ProcessId": "process-real",
                "UnitId": "unit-1",
                "Command": {
                    "FileName": "/bin/sh",
                    "Arguments": ["-c", "printf first; sleep 0.05; printf second"],
                },
                "Io": {"MergeStandardError": False},
            },
        })
        self.assertEqual(3, start["ProcessStatusResponse"]["ProcessPhase"])

        chunks = []
        cursor = 0
        deadline = MODULE.time.monotonic() + 2.0
        while MODULE.time.monotonic() < deadline:
            response = self.agent.process_read_output({
                "RequestId": "read-real",
                "SequenceNumber": 2,
                "ProcessLifecycleRequest": {
                    "ProcessId": "process-real",
                    "AfterOutputSequence": cursor,
                },
            })
            event = response.get("ProcessOutputEvent")
            if event is not None:
                chunks.append(base64.b64decode(event["Bytes"]))
                cursor = event["Sequence"]
                if event["Flags"] & 1:
                    break
            MODULE.time.sleep(0.01)

        status = self.agent.process_status({
            "RequestId": "status-real",
            "SequenceNumber": 3,
            "ProcessStatusRequest": {
                "ProcessId": "process-real",
                "IncludeResult": True,
            },
        })

        self.assertEqual(b"firstsecond", b"".join(chunks))
        self.assertEqual(6, status["ProcessStatusResponse"]["ProcessPhase"])
        self.assertEqual(0, status["ProcessStatusResponse"]["Result"]["ExitCode"])


if __name__ == "__main__":
    unittest.main()
