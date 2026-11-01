#!/usr/bin/env python3
"""Executed on the VM over SSH; reads JSON from stdin and never prints Secret values."""
import json
import re
import secrets
import smtplib
import ssl
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

REQUIRED_KEYS = ("otp-secret", "smtp-username", "smtp-password", "smtp-from-address")
GMAIL_KEYS = ("gmail-client-id", "gmail-client-secret", "gmail-refresh-token", "gmail-from-address")
GMAIL_SCOPE = "https://www.googleapis.com/auth/gmail.send"


class ConfigurationError(Exception):
    pass


def check_gmail_oauth(credentials, opener=urllib.request.urlopen):
    """Refresh a token from this host without sending mail or returning token values."""
    request = urllib.request.Request("https://oauth2.googleapis.com/token", method="POST",
        data=urllib.parse.urlencode({"grant_type": "refresh_token", "client_id": credentials["gmail-client-id"],
             "client_secret": credentials["gmail-client-secret"], "refresh_token": credentials["gmail-refresh-token"]}).encode(),
        headers={"Content-Type": "application/x-www-form-urlencoded"})
    try:
        with opener(request, timeout=15) as response:
            token = json.loads(response.read(1_048_576))
        if not isinstance(token, dict) or not isinstance(token.get("scope", ""), str):
            return {"ok": False, "kind": "oauth_protocol_error"}
        if not token.get("access_token") or GMAIL_SCOPE not in token.get("scope", "").split():
            return {"ok": False, "kind": "gmail_scope_missing"}
        return {"ok": True, "kind": "accepted"}
    except urllib.error.HTTPError as error:
        try:
            reason = json.loads(error.read(65_536)).get("error")
        except (ValueError, AttributeError):
            reason = None
        safe = reason if reason in ("invalid_grant", "invalid_client", "access_denied", "invalid_scope") else "oauth_rejected"
        return {"ok": False, "kind": safe, "status": error.code}
    except (OSError, ValueError):
        return {"ok": False, "kind": "oauth_connection_error"}


def check_smtp(email, password, smtp_factory=None):
    """One AUTH attempt over verified TLS. Never sends mail or returns raw server diagnostics."""
    client = None
    stage = "connect"
    try:
        client = (smtp_factory or smtplib.SMTP)("smtp.gmail.com", 587, timeout=10)
        client.ehlo()
        stage = "tls"
        client.starttls(context=ssl.create_default_context())
        client.ehlo()
        stage = "authenticate"
        status, _ = client.auth("PLAIN", lambda challenge=None: "\0" + email + "\0" + password)
        return {"ok": status == 235, "stage": stage, "status": status, "kind": "accepted" if status == 235 else "auth_rejected"}
    except smtplib.SMTPResponseException as error:
        response = error.smtp_error.decode(errors="replace") if isinstance(error.smtp_error, bytes) else str(error.smtp_error)
        codes = re.findall(r"\b[45]\.[0-9]\.[0-9]{1,3}\b", response)
        return {"ok": False, "stage": stage, "status": error.smtp_code,
                "enhanced": codes[0] if codes else None,
                "kind": "auth_rejected" if stage == "authenticate" and error.smtp_code in (534, 535) else "smtp_rejected"}
    except ssl.SSLError:
        return {"ok": False, "stage": stage, "kind": "tls_error"}
    except TimeoutError:
        return {"ok": False, "stage": stage, "kind": "timeout"}
    except smtplib.SMTPException:
        return {"ok": False, "stage": stage, "kind": "protocol_error"}
    except OSError:
        return {"ok": False, "stage": stage, "kind": "connection_error"}
    finally:
        if client:
            client.close()


def configure(payload, run=subprocess.run, probe_smtp=check_smtp, probe_gmail=check_gmail_oauth):
    namespace = payload["namespace"]
    name = payload["name"]
    for value in (namespace, name):
        if not isinstance(value, str) or not re.fullmatch(r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?", value):
            raise ConfigurationError("Invalid namespace or Secret name.")
    action = payload.get("action")
    if action not in ("check", "test-smtp", "test-input", "configure", "configure-gmail", "check-gmail"):
        raise ConfigurationError("Unsupported operation.")
    if action == "test-input":
        email = payload.get("email", "").strip().lower()
        password = payload.get("password", "").replace(" ", "")
        if not re.fullmatch(r"[^\s@]+@(gmail|googlemail)\.com", email) or not re.fullmatch(r"[A-Za-z]{16}", password):
            raise ConfigurationError("Expected a personal Gmail address and a 16-character app password.")
        smtp = probe_smtp(email, password)
        return {"namespace": namespace, "name": name, "ready": smtp["ok"], "updated": False, "smtp": smtp}

    def kubectl(arguments, body=None):
        result = run(["k3s", "kubectl", "--request-timeout=20s", "-n", namespace, *arguments],
                     input=json.dumps(body) if body is not None else None,
                     text=True, capture_output=True, timeout=30)
        if result.returncode:
            # API validation errors can repeat submitted values. Keep both output streams private.
            raise ConfigurationError(f"Kubernetes operation failed (exit {result.returncode}). Check SSH/sudo, namespace and Secret permissions; then retry.")
        return result.stdout.strip()

    # Read only key presence and metadata; never return existing credential values.
    fields = [f'"{key}":{{{{if .data}}}}{{{{if index .data "{key}"}}}}true{{{{else}}}}false{{{{end}}}}{{{{else}}}}false{{{{end}}}}' for key in REQUIRED_KEYS + GMAIL_KEYS]
    template = '{"revision":"{{.metadata.resourceVersion}}","keys":{' + ','.join(fields) + '}}'
    raw = kubectl(["get", "secret", name, "--ignore-not-found", "-o", "go-template=" + template])
    state = json.loads(raw) if raw else None
    summary = {"namespace": namespace, "name": name, "present": state is not None}
    if action in ("configure-gmail", "check-gmail"):
        import base64
        if action == "check-gmail":
            if not state or not all(state["keys"].get(key) for key in GMAIL_KEYS + ("otp-secret",)):
                return {**summary, "ready": False, "oauth": {"ok": False, "kind": "missing_configuration"}}
            saved = json.loads(kubectl(["get", "secret", name, "-o", "json"]))["data"]
            credentials = {key: base64.b64decode(saved[key]).decode() for key in GMAIL_KEYS}
        else:
            credentials = payload.get("credentials", {})
            if any(not isinstance(credentials.get(key), str) or not 1 <= len(credentials[key]) <= 8192 or
                   any(c in credentials[key] for c in "\r\n\0") for key in GMAIL_KEYS):
                raise ConfigurationError("Google OAuth credentials are missing or malformed.")
            if not credentials["gmail-client-id"].endswith(".apps.googleusercontent.com") or not re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+", credentials["gmail-from-address"]):
                raise ConfigurationError("Expected a Google OAuth client and verified sender address.")
        oauth = probe_gmail(credentials)
        if action == "check-gmail" or not oauth["ok"]:
            return {**summary, "ready": oauth["ok"], "updated": False, "oauth": oauth}
        data = {key: credentials[key] for key in GMAIL_KEYS}
        generated = not state or not state["keys"].get("otp-secret")
        if generated:
            data["otp-secret"] = secrets.token_hex(32)
        metadata = {"annotations": {"bikehub.io/email-provider": "GmailApi"}}
        if state:
            metadata["resourceVersion"] = state["revision"]
            kubectl(["patch", "secret", name, "--type=merge", "--patch-file=/dev/stdin", "-o", "name"],
                    {"metadata": metadata, "stringData": data})
        else:
            metadata.update(name=name, namespace=namespace)
            kubectl(["create", "-f", "-", "-o", "name"], {
                "apiVersion": "v1", "kind": "Secret", "type": "Opaque", "metadata": metadata, "stringData": data})
        return {**summary, "present": True, "ready": True, "updated": True, "otpGenerated": generated, "oauth": oauth}
    if action == "check":
        return {**summary, "ready": bool(state and all(state["keys"].get(k) for k in REQUIRED_KEYS)),
                "keys": state["keys"] if state else {key: False for key in REQUIRED_KEYS}}
    if action == "test-smtp":
        if not state or not all(state["keys"].get(key) for key in ("smtp-username", "smtp-password")):
            return {**summary, "ready": False, "smtp": {"ok": False, "kind": "missing_configuration"}}
        import base64
        # Values stay inside this process on the VM; stdout contains only a sanitized result.
        saved = json.loads(kubectl(["get", "secret", name, "-o", "json"]))["data"]
        email = base64.b64decode(saved["smtp-username"]).decode()
        password = base64.b64decode(saved["smtp-password"]).decode()
        return {**summary, "ready": all(state["keys"].get(key) for key in REQUIRED_KEYS), "smtp": probe_smtp(email, password)}

    email = payload.get("email", "").strip().lower()
    password = payload.get("password", "").replace(" ", "")
    if not re.fullmatch(r"[^\s@]+@(gmail|googlemail)\.com", email) or not re.fullmatch(r"[A-Za-z]{16}", password):
        raise ConfigurationError("Expected a personal Gmail address and a 16-character app password.")
    smtp = probe_smtp(email, password)
    if not smtp["ok"]:
        return {**summary, "ready": False, "updated": False, "smtp": smtp}
    data = {"smtp-username": email, "smtp-password": password, "smtp-from-address": email}
    generated = not state or not state["keys"].get("otp-secret")
    if generated:
        data["otp-secret"] = secrets.token_hex(32)

    if state:
        # A merge patch keeps unrelated keys and the existing OTP secret. RV prevents a lost update.
        kubectl(["patch", "secret", name, "--type=merge", "--patch-file=/dev/stdin", "-o", "name"],
                {"metadata": {"resourceVersion": state["revision"], "annotations": {"bikehub.io/email-provider": "Smtp"}}, "stringData": data})
    else:
        kubectl(["create", "-f", "-", "-o", "name"], {
            "apiVersion": "v1", "kind": "Secret", "type": "Opaque",
            "metadata": {"name": name, "namespace": namespace, "annotations": {"bikehub.io/email-provider": "Smtp"}}, "stringData": data})
    return {**summary, "present": True, "ready": True, "otpGenerated": generated, "updated": True, "smtp": smtp}


def main():
    try:
        payload = json.load(sys.stdin)
        result = configure(payload)
    except ConfigurationError as error:
        print(str(error), file=sys.stderr)
        return 1
    except Exception:
        # JSON, subprocess and validation exceptions can include input; do not echo them.
        print("Could not configure authentication Secret. Credentials were not printed.", file=sys.stderr)
        return 1
    print(json.dumps(result))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
