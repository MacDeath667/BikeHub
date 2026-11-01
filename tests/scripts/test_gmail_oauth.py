import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import types
import unittest
import urllib.error
import urllib.parse

ROOT = Path(__file__).resolve().parents[2]
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

client = load("gmail_oauth_client", "scripts/configure-gmail-oauth.py")
server = load("gmail_oauth_server", "infra/ansible/scripts/configure-auth-secret.py")

CLIENT = {"installed": {"client_id": "test.apps.googleusercontent.com", "client_secret": "private-client-secret",
                       "auth_uri": "https://accounts.google.com/o/oauth2/auth", "token_uri": "https://oauth2.googleapis.com/token"}}
CREDENTIALS = {"gmail-client-id": CLIENT["installed"]["client_id"], "gmail-client-secret": "private-client-secret",
               "gmail-refresh-token": "private-refresh-token", "gmail-from-address": "sender@gmail.com"}


class GmailOAuthClientTests(unittest.TestCase):
    def write_client(self, root, config):
        path = Path(root) / "google-client.json"
        path.write_text(json.dumps(config))
        return path

    def test_desktop_json_is_accepted_but_web_and_non_google_endpoints_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(CLIENT, client.load_client(self.write_client(directory, CLIENT)))
            for config in ({"web": CLIENT["installed"]}, {"installed": {**CLIENT["installed"], "token_uri": "https://attacker.example/token"}}):
                with self.assertRaises(client.SetupError):
                    client.load_client(self.write_client(directory, config))

    def test_account_scope_nonce_and_offline_token_are_required(self):
        token = types.SimpleNamespace(granted_scopes=client.SCOPES, scopes=client.SCOPES, refresh_token="private-refresh-token")
        claims = {"email": "sender@gmail.com", "email_verified": True, "nonce": "nonce"}
        result = client.validated_credentials(CLIENT, token, claims, "sen.der@gmail.com", "nonce")
        self.assertEqual(CREDENTIALS, result)
        for changed in ({**claims, "email": "other@gmail.com"}, {**claims, "email_verified": False}, {**claims, "nonce": "wrong"}):
            with self.assertRaises(client.SetupError):
                client.validated_credentials(CLIENT, token, changed, "sender@gmail.com", "nonce")
        with self.assertRaises(client.SetupError):
            client.validated_credentials(CLIENT, types.SimpleNamespace(granted_scopes=["openid"], scopes=[], refresh_token="private-refresh-token"), claims, "sender@gmail.com", "nonce")
        with self.assertRaises(client.SetupError):
            client.validated_credentials(CLIENT, types.SimpleNamespace(granted_scopes=client.SCOPES, scopes=[], refresh_token=None), claims, "sender@gmail.com", "nonce")


class GmailOAuthServerTests(unittest.TestCase):
    def test_refresh_uses_fixed_https_endpoint_and_does_not_return_tokens(self):
        def request(req, timeout):
            self.assertEqual("https://oauth2.googleapis.com/token", req.full_url)
            fields = urllib.parse.parse_qs(req.data.decode())
            self.assertEqual(["private-refresh-token"], fields["refresh_token"])
            self.assertNotIn("private-", req.full_url)
            return io.BytesIO(json.dumps({"access_token": "private-access-token", "scope": server.GMAIL_SCOPE}).encode())
        result = server.check_gmail_oauth(CREDENTIALS, opener=request)
        self.assertTrue(result["ok"])
        self.assertNotIn("private-", json.dumps(result))

    def test_revoked_and_wrong_scope_tokens_fail_without_disclosing_error_description(self):
        def request(req, timeout):
            raise urllib.error.HTTPError(req.full_url, 400, "Bad Request", {},
                io.BytesIO(json.dumps({"error": "invalid_grant", "error_description": "private-refresh-token sender@gmail.com"}).encode()))
        result = server.check_gmail_oauth(CREDENTIALS, opener=request)
        self.assertEqual("invalid_grant", result["kind"])
        self.assertNotIn("private-", json.dumps(result))
        self.assertNotIn("sender@gmail.com", json.dumps(result))
        for body in ({"access_token": "private-access-token", "scope": "openid"}, []):
            result = server.check_gmail_oauth(CREDENTIALS, opener=lambda req, timeout: io.BytesIO(json.dumps(body).encode()))
            self.assertFalse(result["ok"])

    def test_gmail_setup_preserves_otp_and_selects_transport_only_after_validation(self):
        for ok in (True, False):
            with self.subTest(oauth_valid=ok):
                calls = []
                def kubectl(command, **kwargs):
                    calls.append((command, kwargs))
                    state = {"revision": "42", "keys": {key: True for key in server.REQUIRED_KEYS}}
                    return subprocess.CompletedProcess(command, 0, json.dumps(state) if "get" in command else "", "")
                result = server.configure({"namespace": "bikehub", "name": "bikehub-auth", "action": "configure-gmail", "credentials": CREDENTIALS},
                                          run=kubectl, probe_gmail=lambda values: {"ok": ok, "kind": "accepted" if ok else "invalid_grant"})
                self.assertEqual(ok, result["updated"])
                self.assertEqual(2 if ok else 1, len(calls))
                if ok:
                    body = json.loads(calls[-1][1]["input"])
                    self.assertNotIn("otp-secret", body["stringData"])
                    self.assertEqual("GmailApi", body["metadata"]["annotations"]["bikehub.io/email-provider"])
                    self.assertEqual("42", body["metadata"]["resourceVersion"])
                    self.assertNotIn("private-", str(calls[-1][0]))


if __name__ == "__main__":
    unittest.main()
