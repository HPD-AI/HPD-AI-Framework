import importlib.util
import os
import pathlib
import socket
import subprocess
import tempfile
import unittest
from unittest import mock


AGENT_PATH = pathlib.Path(__file__).parents[1] / "src" / "hpd_guest_agent.py"
SPEC = importlib.util.spec_from_file_location("hpd_guest_agent", AGENT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class FakeUnixSocket:
    def __init__(self, response=None, error=None):
        self.responses = list(response) if isinstance(response, (list, tuple)) else [response]
        self.error = error

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False

    def settimeout(self, _):
        pass

    def connect(self, _):
        if self.error:
            raise self.error

    def sendall(self, _):
        pass

    def recv(self, _):
        if self.error:
            raise self.error
        return self.responses.pop(0) if self.responses else b""


class EngineProbeTests(unittest.TestCase):
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

    def test_each_supported_api_ready(self):
        cases = {
            0: b"HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK",
            1: b"HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK",
        }
        for api, response in cases.items():
            with self.subTest(api=api), \
                    mock.patch("os.path.exists", return_value=True), \
                    mock.patch.object(MODULE.socket, "socket", return_value=FakeUnixSocket(response)):
                self.assertEqual("ready", self.agent.probe_engine(api, "/run/engine.sock")["state"])

        for api, output in ((2, "Client: 1.7\nServer: 1.7"), (4, "BuildKit: v0.20")):
            completed = subprocess.CompletedProcess([], 0, stdout=output, stderr="")
            with self.subTest(api=api), \
                    mock.patch("os.path.exists", return_value=True), \
                    mock.patch.object(MODULE.subprocess, "run", return_value=completed):
                self.assertEqual("ready", self.agent.probe_engine(api, "/run/engine.sock")["state"])

    def test_http_headers_and_body_may_arrive_in_separate_reads(self):
        response = [
            b"HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\n",
            b"OK",
        ]
        with mock.patch("os.path.exists", return_value=True), \
                mock.patch.object(MODULE.socket, "socket", return_value=FakeUnixSocket(response)):
            self.assertEqual("ready", self.agent.probe_engine(0, "/run/docker.sock")["state"])

    def test_http_truncated_content_length_is_malformed(self):
        response = b"HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\nOK"
        with mock.patch("os.path.exists", return_value=True), \
                mock.patch.object(MODULE.socket, "socket", return_value=FakeUnixSocket(response)):
            self.assertEqual("malformed", self.agent.probe_engine(0, "/run/docker.sock")["state"])

    def test_http_oversized_content_length_is_malformed(self):
        response = b"HTTP/1.1 200 OK\r\nContent-Length: 65537\r\n\r\nOK"
        with mock.patch("os.path.exists", return_value=True), \
                mock.patch.object(MODULE.socket, "socket", return_value=FakeUnixSocket(response)):
            self.assertEqual("malformed", self.agent.probe_engine(0, "/run/docker.sock")["state"])

    def test_each_supported_api_unavailable(self):
        for api in (0, 1):
            with self.subTest(api=api), \
                    mock.patch("os.path.exists", return_value=True), \
                    mock.patch.object(
                        MODULE.socket,
                        "socket",
                        return_value=FakeUnixSocket(error=ConnectionRefusedError("refused")),
                    ):
                self.assertEqual("unavailable", self.agent.probe_engine(api, "/run/engine.sock")["state"])

        for api in (2, 4):
            completed = subprocess.CompletedProcess([], 1, stdout="", stderr="unavailable")
            with self.subTest(api=api), \
                    mock.patch("os.path.exists", return_value=True), \
                    mock.patch.object(MODULE.subprocess, "run", return_value=completed):
                self.assertEqual("unavailable", self.agent.probe_engine(api, "/run/engine.sock")["state"])

    def test_each_supported_api_malformed_response(self):
        for api in (0, 1):
            with self.subTest(api=api), \
                    mock.patch("os.path.exists", return_value=True), \
                    mock.patch.object(
                        MODULE.socket,
                        "socket",
                        return_value=FakeUnixSocket(b"HTTP/1.1 200 OK\r\n\r\nwrong"),
                    ):
                self.assertEqual("malformed", self.agent.probe_engine(api, "/run/engine.sock")["state"])

        for api in (2, 4):
            completed = subprocess.CompletedProcess([], 0, stdout="", stderr="")
            with self.subTest(api=api), \
                    mock.patch("os.path.exists", return_value=True), \
                    mock.patch.object(MODULE.subprocess, "run", return_value=completed):
                self.assertEqual("malformed", self.agent.probe_engine(api, "/run/engine.sock")["state"])

    def test_each_supported_api_timeout(self):
        for api in (0, 1):
            with self.subTest(api=api), \
                    mock.patch("os.path.exists", return_value=True), \
                    mock.patch.object(
                        MODULE.socket,
                        "socket",
                        return_value=FakeUnixSocket(error=socket.timeout()),
                    ):
                self.assertEqual("timeout", self.agent.probe_engine(api, "/run/engine.sock")["state"])

        for api in (2, 4):
            with self.subTest(api=api), \
                    mock.patch("os.path.exists", return_value=True), \
                    mock.patch.object(
                        MODULE.subprocess,
                        "run",
                        side_effect=subprocess.TimeoutExpired(["probe"], 2),
                    ):
                self.assertEqual("timeout", self.agent.probe_engine(api, "/run/engine.sock")["state"])

    def test_unsupported_api_is_structured_unsupported(self):
        with mock.patch("os.path.exists", return_value=True):
            result = self.agent.probe_engine(5, "/run/provider.sock")
        self.assertEqual("unsupported", result["state"])
        self.assertIn("API 5", result["message"])

    def test_provider_generation_is_preserved_and_engine_generation_is_stable_across_readiness(self):
        request = {
            "Operation": 47,
            "RequestId": "engine-1",
            "ProviderGeneration": 9,
            "HostId": "host-b",
            "EngineStatusRequest": {
                "HostId": "host-b",
                "EngineId": "docker",
                "Api": 0,
                "ProviderGeneration": 9,
                "HostStartGeneration": 3,
            },
        }
        stat = mock.Mock(st_dev=1, st_ino=2, st_ctime_ns=3)
        with mock.patch.object(
                self.agent,
                "probe_engine",
                return_value={
                    "state": "ready",
                    "socket_exists": True,
                    "message": "Docker-compatible API is ready.",
                }), mock.patch("os.stat", return_value=stat), \
                mock.patch.object(self.agent, "_save_generation_state"):
            first = self.agent.engine_status(request)
        with mock.patch.object(
                self.agent,
                "probe_engine",
                return_value={
                    "state": "unavailable",
                    "socket_exists": True,
                    "message": "Docker-compatible API is unavailable.",
                }), mock.patch("os.stat", return_value=stat), \
                mock.patch.object(self.agent, "_save_generation_state"):
            second = self.agent.engine_status(request)

        first_generation = first["EngineStatusResponse"]["GuestEngineStatus"]["Generation"]
        second_generation = second["EngineStatusResponse"]["GuestEngineStatus"]["Generation"]
        self.assertEqual(9, first_generation["ProviderGeneration"])
        self.assertEqual(3, first_generation["HostStartGeneration"])
        self.assertEqual(first_generation["EngineGeneration"], second_generation["EngineGeneration"])


if __name__ == "__main__":
    unittest.main()
