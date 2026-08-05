import argparse
import importlib.machinery
import pathlib
import stat
import subprocess
import unittest
from unittest import mock


RUNNER_PATH = pathlib.Path(__file__).parents[1] / "packaging" / "container-run"
LOADER = importlib.machinery.SourceFileLoader("hpd_container_run", str(RUNNER_PATH))
MODULE = LOADER.load_module()


class ContainerRunTests(unittest.TestCase):
    def arguments(self):
        return argparse.Namespace(
            image="alpine:3.20",
            engine_socket="/run/hpd/engine/docker.sock",
            timeout_ms=2500,
            env=["MESSAGE=hello world", "EMPTY="],
            command=["sh", "-c", 'printf "%s\\n" "$MESSAGE"'],
        )

    def test_builds_argument_safe_docker_invocation(self):
        completed = subprocess.CompletedProcess([], 7)
        with mock.patch.object(MODULE, "parse_arguments", return_value=self.arguments()), \
                mock.patch.object(MODULE.os, "stat", return_value=mock.Mock(st_mode=stat.S_IFSOCK)), \
                mock.patch.object(MODULE.uuid, "uuid4", return_value=mock.Mock(hex="abc123")), \
                mock.patch.object(MODULE.signal, "signal"), \
                mock.patch.object(MODULE.subprocess, "run", return_value=completed) as run:
            exit_code = MODULE.main()

        self.assertEqual(7, exit_code)
        command = run.call_args.args[0]
        self.assertEqual(
            [
                "docker", "run", "--name", "hpd-run-abc123", "--rm",
                "--env", "MESSAGE=hello world",
                "--env", "EMPTY=",
                "alpine:3.20",
                "sh", "-c", 'printf "%s\\n" "$MESSAGE"',
            ],
            command)
        self.assertEqual(
            "unix:///run/hpd/engine/docker.sock",
            run.call_args.kwargs["env"]["DOCKER_HOST"])
        self.assertEqual(2.5, run.call_args.kwargs["timeout"])

    def test_timeout_forces_container_cleanup(self):
        timeout = subprocess.TimeoutExpired(["docker"], 2.5)
        cleanup = subprocess.CompletedProcess([], 0)
        with mock.patch.object(MODULE, "parse_arguments", return_value=self.arguments()), \
                mock.patch.object(MODULE.os, "stat", return_value=mock.Mock(st_mode=stat.S_IFSOCK)), \
                mock.patch.object(MODULE.uuid, "uuid4", return_value=mock.Mock(hex="abc123")), \
                mock.patch.object(MODULE.signal, "signal"), \
                mock.patch.object(MODULE.subprocess, "run", side_effect=[timeout, cleanup]) as run:
            exit_code = MODULE.main()

        self.assertEqual(124, exit_code)
        self.assertEqual(2, run.call_count)
        self.assertEqual(
            ["docker", "rm", "-f", "hpd-run-abc123"],
            run.call_args_list[1].args[0])


if __name__ == "__main__":
    unittest.main()
