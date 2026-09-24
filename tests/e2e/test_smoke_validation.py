import unittest
from smoke_test import check_system


class SmokeValidationTests(unittest.TestCase):
    def test_rejects_degraded_even_when_api_is_online(self):
        body = {"status": "degraded", "api": {"status": "online"}, "python": {"status": "unavailable"}}
        with self.assertRaises(AssertionError):
            check_system(body, "online")
        check_system(body, "unavailable")

    def test_rejects_python_offline_even_with_wrong_global_online(self):
        body = {"status": "online", "api": {"status": "online"}, "python": {"status": "unavailable"}}
        with self.assertRaises(AssertionError):
            check_system(body, "online")

    def test_accepts_complete_online_contract(self):
        check_system({"status": "online", "api": {"status": "online"}, "python": {
            "status": "online", "service": "engine", "version": "1"}}, "online")


if __name__ == "__main__":
    unittest.main()
