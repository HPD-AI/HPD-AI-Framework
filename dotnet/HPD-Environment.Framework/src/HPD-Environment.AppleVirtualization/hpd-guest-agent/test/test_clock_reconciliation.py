import importlib.util
import os
import pathlib
import tempfile
import unittest
from unittest import mock


AGENT_PATH = pathlib.Path(__file__).parents[1] / "src" / "hpd_guest_agent.py"
SPEC = importlib.util.spec_from_file_location("hpd_guest_agent_clock", AGENT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ClockReconciliationTests(unittest.TestCase):
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

    @staticmethod
    def request(host_utc_ms):
        return {
            "ProtocolVersion": "1.0",
            "MessageType": 0,
            "Operation": 2,
            "RequestId": "clock-test",
            "SequenceNumber": 1,
            "ClockReconciliationRequest": {
                "HostUtcUnixMilliseconds": host_utc_ms,
                "MaximumClockSkewMilliseconds": 5000,
                "CorrectGuestClock": True,
            },
        }

    def test_skewed_clock_is_corrected_and_verified(self):
        host_utc_ms = 2_000_000
        with mock.patch.object(
            self.agent,
            "verified_storage_identity",
            side_effect=["runtime-uuid", "app-data-uuid"],
        ), mock.patch.object(
            MODULE.time,
            "time",
            side_effect=[1000.0, 2000.0],
        ), mock.patch.object(MODULE.time, "clock_settime") as clock_settime:
            response = self.agent.handle(self.request(host_utc_ms))

        self.assertEqual(0, response["ResponseStatus"])
        evidence = response["Ready"]["ClockReconciliation"]
        self.assertTrue(evidence["Corrected"])
        self.assertTrue(evidence["Verified"])
        clock_settime.assert_called_once_with(MODULE.time.CLOCK_REALTIME, 2000.0)

    def test_failed_clock_correction_fails_closed(self):
        with mock.patch.object(
                self.agent,
                "verified_storage_identity",
                side_effect=["runtime-uuid", "app-data-uuid"],
        ), mock.patch.object(MODULE.time, "time", return_value=1000.0), \
                mock.patch.object(
                    MODULE.time,
                    "clock_settime",
                    side_effect=OSError("denied"),
                ):
            response = self.agent.handle(self.request(2_000_000))

        self.assertEqual(2, response["ResponseStatus"])
        self.assertEqual(
            "Environment.Lifecycle.GuestClockCorrectionFailed",
            response["Error"]["Code"],
        )


if __name__ == "__main__":
    unittest.main()
