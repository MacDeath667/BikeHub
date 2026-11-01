import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import types
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("configure_auth_client", ROOT / "scripts/configure-auth-server.py")
client = importlib.util.module_from_spec(spec)
spec.loader.exec_module(client)


class SmtpDiagnosticClientTests(unittest.TestCase):
    def test_same_fresh_credentials_are_used_for_both_origins_without_leaking_them(self):
        email = "fresh-input@gmail.com"
        password = "abcdefghijklmnop"
        for local_ok, remote_ok in ((True, True), (True, False), (False, True), (False, False)):
            with self.subTest(local=local_ok, remote=remote_ok), tempfile.TemporaryDirectory() as directory:
                key = Path(directory) / "unused-key"
                key.touch()
                local = Mock(return_value={"ok": local_ok, "stage": "authenticate", "status": 235 if local_ok else 535})
                remote = {"ready": remote_ok, "updated": False, "smtp": {"ok": remote_ok, "stage": "authenticate", "status": 235 if remote_ok else 535}}
                ssh = Mock(return_value=subprocess.CompletedProcess([], 0, json.dumps(remote), ""))
                output = io.StringIO()
                with patch.object(client.sys, "argv", ["configure-auth-server.py", "--diagnose-smtp", "--ssh-key", str(key)]), \
                     patch.object(client.sys, "stdin", types.SimpleNamespace(isatty=lambda: True)), \
                     patch("builtins.input", return_value=email), \
                     patch.object(client.getpass, "getpass", return_value="abcd efgh ijkl mnop") as getpass, \
                     patch.object(client, "load_smtp_checker", return_value=local), \
                     patch.object(client.subprocess, "run", ssh), contextlib.redirect_stdout(output):
                    code = client.main()
                self.assertEqual(0 if local_ok and remote_ok else 1, code)
                getpass.assert_called_once()
                local.assert_called_once_with(email, password)
                payload = json.loads(ssh.call_args.kwargs["input"])
                self.assertEqual("test-input", payload["action"])
                self.assertEqual(email, payload["email"])
                self.assertEqual(password, payload["password"])
                self.assertNotIn(email, str(ssh.call_args.args[0]))
                self.assertNotIn(password, str(ssh.call_args.args[0]))
                self.assertNotIn(email, output.getvalue())
                self.assertNotIn(password, output.getvalue())
                self.assertIn("Mac:", output.getvalue())
                self.assertIn("VM:", output.getvalue())


if __name__ == "__main__":
    unittest.main()
