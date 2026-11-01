import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class RedeployAuthenticationTests(unittest.TestCase):
    def run_deploy(self, *arguments, current="true"):
        with tempfile.TemporaryDirectory(prefix="bikehub-redeploy-test-") as directory:
            path = Path(directory)
            calls = path / "calls.jsonl"
            stub = f"#!{sys.executable}\n" + '''import json, os, sys
with open(os.environ["FAKE_CALLS"], "a") as log:
    log.write(json.dumps([sys.argv[0], *sys.argv[1:]]) + "\\n")
if "jsonpath=" in sys.argv[-1]:
    print(os.environ["FAKE_AUTH_ENABLED"], end="")
'''
            for executable in ("ssh", "ansible-playbook"):
                script = path / executable
                script.write_text(stub)
                script.chmod(0o700)
            key = path / "unused-key"
            key.touch()
            environment = {**os.environ, "PATH": str(path) + os.pathsep + os.environ["PATH"],
                           "BIKEHUB_SSH_KEY": str(key), "BIKEHUB_VM_IP": "127.0.0.1",
                           "FAKE_CALLS": str(calls), "FAKE_AUTH_ENABLED": current}
            result = subprocess.run(["bash", str(ROOT / "scripts/redeploy.sh"), *arguments],
                                    env=environment, text=True, capture_output=True, timeout=10)
            entries = [json.loads(line) for line in calls.read_text().splitlines()] if calls.exists() else []
            return result, entries

    def server_arguments(self, entries):
        return next(call for call in entries if any(argument.endswith("playbooks/server.yml") for argument in call))

    def test_default_preserves_enabled_authentication(self):
        result, entries = self.run_deploy()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("bikehub_auth_enabled=true", self.server_arguments(entries))

    def test_legacy_deployment_remains_disabled_until_explicitly_enabled(self):
        result, entries = self.run_deploy(current="")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("bikehub_auth_enabled=false", self.server_arguments(entries))

    def test_explicit_enable_and_disable_override_deployed_state(self):
        for option, value in (("--enable-auth", "true"), ("--disable-auth", "false")):
            with self.subTest(option=option):
                result, entries = self.run_deploy(option)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertIn("bikehub_auth_enabled=" + value, self.server_arguments(entries))
                self.assertFalse(any("jsonpath=" in call[-1] for call in entries))

    def test_unknown_deployed_value_aborts_before_ansible(self):
        result, entries = self.run_deploy(current="unexpected")
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(any(Path(call[0]).name == "ansible-playbook" for call in entries))

    def test_logs_only_never_deploys_or_reads_authentication_state(self):
        result, entries = self.run_deploy("--logs-only")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, len(entries))
        self.assertIn("kubectl logs", entries[0][-1])

    def test_conflicting_options_abort_without_external_commands(self):
        for arguments in (("--enable-auth", "--disable-auth"), ("--enable-auth", "--logs-only")):
            with self.subTest(arguments=arguments):
                result, entries = self.run_deploy(*arguments)
                self.assertEqual(2, result.returncode)
                self.assertEqual([], entries)


if __name__ == "__main__":
    unittest.main()
