import importlib.util
import json
import os
import pathlib
import socket
import tempfile
import threading
import unittest
from unittest import mock


AGENT_PATH = pathlib.Path(__file__).parents[1] / "src" / "hpd_guest_agent.py"
SPEC = importlib.util.spec_from_file_location("hpd_guest_agent", AGENT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class TcpTunnelTests(unittest.TestCase):
    def setUp(self):
        self.state_directory = tempfile.TemporaryDirectory()
        self.environment = mock.patch.dict(
            os.environ,
            {"HPD_GUEST_AGENT_STATE_DIR": self.state_directory.name},
        )
        self.environment.start()
        self.agent = MODULE.GuestAgent("test", "1.0", "boot-test")

    def tearDown(self):
        self.agent.shutdown()
        self.environment.stop()
        self.state_directory.cleanup()

    def test_tunnel_streams_bidirectionally_after_ready_frame(self):
        listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        listener.bind(("127.0.0.1", 0))
        listener.listen(1)
        target_port = listener.getsockname()[1]

        def echo():
            with listener:
                connection, _ = listener.accept()
                with connection:
                    while True:
                        payload = connection.recv(65536)
                        if not payload:
                            return
                        connection.sendall(payload)

        echo_thread = threading.Thread(target=echo)
        echo_thread.start()
        client, server = socket.socketpair()
        reader = server.makefile("rb", buffering=0)
        writer = server.makefile("wb", buffering=0)
        tunnel_thread = threading.Thread(
            target=MODULE.serve_stream,
            args=(self.agent, reader, writer),
        )
        tunnel_thread.start()
        request = {
            "ProtocolVersion": "1.0",
            "MessageType": 0,
            "Operation": 51,
            "RequestId": "tunnel-test",
            "SequenceNumber": 1,
            "TcpTunnelRequest": {
                "TargetAddress": "127.0.0.1",
                "TargetPort": target_port,
            },
        }
        client.sendall(
            json.dumps(request, separators=(",", ":")).encode("utf-8") +
            b"\n")
        response = b""
        while not response.endswith(b"\n"):
            response += client.recv(4096)
        self.assertIn("TcpTunnelReady", json.loads(response))

        payload = b"websocket-and-http-stream"
        client.sendall(payload)
        self.assertEqual(payload, client.recv(len(payload)))
        client.shutdown(socket.SHUT_WR)
        tunnel_thread.join(timeout=3)
        self.assertFalse(tunnel_thread.is_alive())
        client.close()
        server.close()
        echo_thread.join(timeout=3)
        self.assertFalse(echo_thread.is_alive())


if __name__ == "__main__":
    unittest.main()
