#!/usr/bin/env python3
"""Executed on the VM over SSH; reads JSON from stdin and never prints Secret values."""
import json
import re
import secrets
import smtplib
import ssl
import subprocess
import sys

REQUIRED_KEYS = ("otp-secret", "smtp-username", "smtp-password", "smtp-from-address")


class ConfigurationError(Exception):
    pass


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


def configure(payload, run=subprocess.run, probe_smtp=check_smtp):
    namespace = payload["namespace"]
    name = payload["name"]
    for value in (namespace, name):
        if not isinstance(value, str) or not re.fullmatch(r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?", value):
            raise ConfigurationError("Invalid namespace or Secret name.")
    action = payload.get("action")
    if action not in ("check", "test-smtp", "test-input", "configure"):
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
    fields = [f'"{key}":{{{{if .data}}}}{{{{if index .data "{key}"}}}}true{{{{else}}}}false{{{{end}}}}{{{{else}}}}false{{{{end}}}}' for key in REQUIRED_KEYS]
    template = '{"revision":"{{.metadata.resourceVersion}}","keys":{' + ','.join(fields) + '}}'
    raw = kubectl(["get", "secret", name, "--ignore-not-found", "-o", "go-template=" + template])
    state = json.loads(raw) if raw else None
    summary = {"namespace": namespace, "name": name, "present": state is not None}
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
                {"metadata": {"resourceVersion": state["revision"]}, "stringData": data})
    else:
        kubectl(["create", "-f", "-", "-o", "name"], {
            "apiVersion": "v1", "kind": "Secret", "type": "Opaque",
            "metadata": {"name": name, "namespace": namespace}, "stringData": data})
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
