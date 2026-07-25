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


class IncompleteReader:
    def join(self, timeout=None):
        del timeout


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

    def test_capture_and_replay_are_bounded_with_cursor_gap(self):
        self.agent.process_start({
            "RequestId": "start-bounded",
            "SequenceNumber": 1,
            "ProcessStartRequest": {
                "ProcessId": "process-bounded",
                "UnitId": "unit-1",
                "Command": {
                    "FileName": "/bin/sh",
                    "Arguments": ["-c", "head -c 20000 /dev/zero"],
                },
                "Io": {
                    "StandardOutput": {"Capture": True, "MaxCapturedBytes": 32},
                    "StandardError": {"Capture": False},
                    "LogPolicy": {"MaxRetainedBytesPerStream": 4096},
                },
            },
        })
        wait = self.agent.process_wait({
            "RequestId": "wait-bounded",
            "SequenceNumber": 2,
            "ProcessLifecycleRequest": {"ProcessId": "process-bounded"},
        })
        output = wait["ProcessStatusResponse"]["Result"]["Output"]["Stdout"]
        state = self.agent.processes["process-bounded"]

        self.assertEqual(20000, output["BytesObserved"])
        self.assertEqual(32, output["BytesCaptured"])
        self.assertEqual(19968, output["BytesDiscarded"])
        self.assertTrue(output["Truncated"])
        self.assertLessEqual(state["output_replay_bytes"][0], 4096)

        read = self.agent.process_read_output({
            "RequestId": "read-gap",
            "SequenceNumber": 3,
            "ProcessLifecycleRequest": {
                "ProcessId": "process-bounded",
                "AfterOutputSequence": 0,
            },
        })
        self.assertTrue(read["ProcessOutputEvent"]["Flags"] & 2)

    def test_incomplete_reader_reports_drain_timeout_without_final_chunk(self):
        process = FakeProcess()
        state = {
            "popen": process,
            "started_at": self.agent.timestamp(),
            "merged_standard_error": False,
            "output_lock": MODULE.threading.Lock(),
            "output_chunks": [{
                "ProcessId": "",
                "Stream": 0,
                "Sequence": 1,
                "ObservedAt": self.agent.timestamp(),
                "_bytes": b"x",
                "Flags": 0,
            }],
            "output_accounting": {
                0: {"capture": True, "limit": 8, "captured": bytearray(b"x"), "observed": 1},
                1: {"capture": True, "limit": 8, "captured": bytearray(), "observed": 0},
            },
            "output_readers": [IncompleteReader()],
            "output_readers_complete": False,
            "output_drain_timeout_seconds": 0.0,
            "output_drain_timed_out": False,
        }

        result = self.agent.finalize_process_output("process-timeout", state)

        self.assertTrue(result["Output"]["OutputDrainTimedOut"])
        self.assertEqual(0, state["output_chunks"][-1]["Flags"] & 1)


if __name__ == "__main__":
    unittest.main()
