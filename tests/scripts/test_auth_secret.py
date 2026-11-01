import importlib.util
import json
from pathlib import Path
import subprocess
import smtplib
import ssl
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
    @staticmethod
    def configure(payload, **kwargs):
        return secret.configure(payload, probe_smtp=lambda email, password: {"ok": True, "status": 235, "kind": "accepted"}, **kwargs)

    payload = {"namespace": "bikehub", "name": "bikehub-auth", "action": "configure",
               "email": "rider@gmail.com", "password": "abcdefghijklmnop"}

    def test_first_setup_generates_otp_and_sends_credentials_only_in_stdin(self):
        runner = FakeKubectl()
        result = self.configure(self.payload, run=runner)
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
        result = self.configure(self.payload, run=runner)
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
                result = self.configure({**self.payload, "action": "check"}, run=runner)
                self.assertEqual(1, len(runner.calls))
                self.assertIn("get", runner.calls[0][0])
                self.assertEqual(bool(state), result["ready"])

    def test_missing_otp_is_initialized_without_reading_old_values(self):
        runner = FakeKubectl({"revision": "42", "keys": {key: key != "otp-secret" for key in secret.REQUIRED_KEYS}})
        result = self.configure(self.payload, run=runner)
        self.assertTrue(result["otpGenerated"])
        self.assertIn("otp-secret", json.loads(runner.calls[-1][1]["input"])["stringData"])

    def test_kubernetes_write_error_does_not_repeat_credential(self):
        with self.assertRaises(secret.ConfigurationError) as error:
            self.configure(self.payload, run=FakeKubectl(fail_write=True))
        self.assertNotIn(self.payload["password"], str(error.exception))
        self.assertNotIn("private diagnostic", str(error.exception))

    def test_invalid_target_is_rejected_before_kubectl(self):
        runner = FakeKubectl()
        with self.assertRaises(secret.ConfigurationError):
            self.configure({**self.payload, "namespace": "bikehub; malicious"}, run=runner)
        self.assertEqual([], runner.calls)

    def test_invalid_password_never_writes(self):
        runner = FakeKubectl()
        with self.assertRaises(secret.ConfigurationError):
            self.configure({**self.payload, "password": "wrong"}, run=runner)
        self.assertEqual(1, len(runner.calls))

    def test_failed_smtp_validation_never_changes_the_secret(self):
        runner = FakeKubectl({"revision": "42", "keys": {key: True for key in secret.REQUIRED_KEYS}})
        result = secret.configure(self.payload, run=runner, probe_smtp=lambda email, password:
                                  {"ok": False, "kind": "auth_rejected", "status": 535, "enhanced": "5.7.8"})
        self.assertFalse(result["updated"])
        self.assertFalse(result["ready"])
        self.assertEqual(1, len(runner.calls))
        self.assertIn("get", runner.calls[0][0])
        self.assertEqual("auth_rejected", result["smtp"]["kind"])

    def test_smtp_diagnostic_with_missing_configuration_never_authenticates(self):
        def forbidden_probe(email, password):
            self.fail("Must not authenticate with missing configuration")
        runner = FakeKubectl()
        result = secret.configure({**self.payload, "action": "test-smtp"}, run=runner, probe_smtp=forbidden_probe)
        self.assertFalse(result["smtp"]["ok"])
        self.assertEqual("missing_configuration", result["smtp"]["kind"])

    def test_fresh_input_diagnostic_never_reads_or_writes_kubernetes(self):
        def forbidden_kubectl(*args, **kwargs):
            self.fail("Fresh input diagnostic must not use Kubernetes")
        observed = []
        def probe(email, password):
            observed.append((email, password))
            return {"ok": False, "status": 535, "kind": "auth_rejected"}
        result = secret.configure({**self.payload, "action": "test-input"}, run=forbidden_kubectl, probe_smtp=probe)
        self.assertEqual([(self.payload["email"], self.payload["password"])], observed)
        self.assertFalse(result["updated"])
        self.assertFalse(result["smtp"]["ok"])


class FakeSMTP:
    def __init__(self, failure=None):
        self.failure = failure
        self.closed = False
        self.calls = []

    def ehlo(self):
        self.calls.append("ehlo")
        return 250, b"ok"

    def starttls(self, context):
        self.calls.append("tls")
        assert context.check_hostname and context.verify_mode == ssl.CERT_REQUIRED
        return 220, b"ready"

    def auth(self, mechanism, callback):
        self.calls.append("auth:" + mechanism)
        assert callback() == "\0rider@gmail.com\0abcdefghijklmnop"
        if self.failure:
            raise self.failure
        return 235, b"authenticated"

    def close(self):
        self.closed = True


class SmtpProbeTests(unittest.TestCase):
    def test_probe_verifies_tls_authenticates_once_and_does_not_send(self):
        client = FakeSMTP()
        result = secret.check_smtp("rider@gmail.com", "abcdefghijklmnop", smtp_factory=lambda *args, **kwargs: client)
        self.assertTrue(result["ok"])
        self.assertEqual(["ehlo", "tls", "ehlo", "auth:PLAIN"], client.calls)
        self.assertTrue(client.closed)

    def test_probe_redacts_google_error_details(self):
        failure = smtplib.SMTPAuthenticationError(535, b"5.7.8 BadCredentials rider@gmail.com abcdefghijklmnop")
        client = FakeSMTP(failure)
        result = secret.check_smtp("rider@gmail.com", "abcdefghijklmnop", smtp_factory=lambda *args, **kwargs: client)
        self.assertFalse(result["ok"])
        self.assertEqual(535, result["status"])
        self.assertEqual("5.7.8", result["enhanced"])
        self.assertEqual("auth_rejected", result["kind"])
        self.assertNotIn("rider@gmail.com", json.dumps(result))
        self.assertNotIn("abcdefghijklmnop", json.dumps(result))
        self.assertTrue(client.closed)

    def test_tls_failure_never_attempts_authentication(self):
        client = FakeSMTP()
        def rejected_tls(context):
            raise ssl.SSLCertVerificationError("private diagnostic")
        client.starttls = rejected_tls
        result = secret.check_smtp("rider@gmail.com", "abcdefghijklmnop", smtp_factory=lambda *args, **kwargs: client)
        self.assertEqual("tls_error", result["kind"])
        self.assertNotIn("auth:PLAIN", client.calls)
        self.assertTrue(client.closed)


if __name__ == "__main__":
    unittest.main()
