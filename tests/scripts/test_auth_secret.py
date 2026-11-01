import importlib.util
import json
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("configure_auth_secret", ROOT / "infra/ansible/scripts/configure-auth-secret.py")
secret = importlib.util.module_from_spec(spec)
spec.loader.exec_module(secret)


class FakeKubectl:
    def __init__(self, state=None, fail_write=False):
        self.state = state
        self.fail_write = fail_write
        self.calls = []

    def __call__(self, command, **kwargs):
        self.calls.append((command, kwargs))
        if "get" in command:
            return subprocess.CompletedProcess(command, 0, json.dumps(self.state) if self.state else "", "")
        return subprocess.CompletedProcess(command, 1 if self.fail_write else 0, "", "private diagnostic abcdefghijklmnop")


class AuthenticationSecretTests(unittest.TestCase):
    payload = {"namespace": "bikehub", "name": "bikehub-auth", "action": "configure",
               "email": "rider@gmail.com", "password": "abcdefghijklmnop"}

    def test_first_setup_generates_otp_and_sends_credentials_only_in_stdin(self):
        runner = FakeKubectl()
        result = secret.configure(self.payload, run=runner)
        command, options = runner.calls[-1]
        self.assertIn("create", command)
        self.assertTrue(result["otpGenerated"])
        body = json.loads(options["input"])
        self.assertRegex(body["stringData"]["otp-secret"], r"^[0-9a-f]{64}$")
        self.assertEqual(self.payload["password"], body["stringData"]["smtp-password"])
        self.assertNotIn(self.payload["password"], " ".join(command))
        self.assertNotIn(self.payload["email"], " ".join(command))
        self.assertNotIn(self.payload["password"], json.dumps(result))

    def test_update_preserves_existing_otp_and_uses_optimistic_concurrency(self):
        state = {"revision": "42", "keys": {key: True for key in secret.REQUIRED_KEYS}}
        runner = FakeKubectl(state)
        result = secret.configure(self.payload, run=runner)
        command, options = runner.calls[-1]
        self.assertIn("patch", command)
        self.assertIn("--patch-file=/dev/stdin", command)
        body = json.loads(options["input"])
        self.assertEqual("42", body["metadata"]["resourceVersion"])
        self.assertNotIn("otp-secret", body["stringData"])
        self.assertFalse(result["otpGenerated"])

    def test_check_only_reads_key_presence(self):
        for state in (None, {"revision": "42", "keys": {key: True for key in secret.REQUIRED_KEYS}}):
            with self.subTest(state=bool(state)):
                runner = FakeKubectl(state)
                result = secret.configure({**self.payload, "action": "check"}, run=runner)
                self.assertEqual(1, len(runner.calls))
                self.assertIn("get", runner.calls[0][0])
                self.assertEqual(bool(state), result["ready"])

    def test_missing_otp_is_initialized_without_reading_old_values(self):
        runner = FakeKubectl({"revision": "42", "keys": {key: key != "otp-secret" for key in secret.REQUIRED_KEYS}})
        result = secret.configure(self.payload, run=runner)
        self.assertTrue(result["otpGenerated"])
        self.assertIn("otp-secret", json.loads(runner.calls[-1][1]["input"])["stringData"])

    def test_kubernetes_write_error_does_not_repeat_credential(self):
        with self.assertRaises(secret.ConfigurationError) as error:
            secret.configure(self.payload, run=FakeKubectl(fail_write=True))
        self.assertNotIn(self.payload["password"], str(error.exception))
        self.assertNotIn("private diagnostic", str(error.exception))

    def test_invalid_target_is_rejected_before_kubectl(self):
        runner = FakeKubectl()
        with self.assertRaises(secret.ConfigurationError):
            secret.configure({**self.payload, "namespace": "bikehub; malicious"}, run=runner)
        self.assertEqual([], runner.calls)

    def test_invalid_password_never_writes(self):
        runner = FakeKubectl()
        with self.assertRaises(secret.ConfigurationError):
            secret.configure({**self.payload, "password": "wrong"}, run=runner)
        self.assertEqual(1, len(runner.calls))


if __name__ == "__main__":
    unittest.main()
