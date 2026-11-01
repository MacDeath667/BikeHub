#!/usr/bin/env python3
"""Executed on the VM over SSH; reads JSON from stdin and never prints Secret values."""
import json
import re
import secrets
import subprocess
import sys

REQUIRED_KEYS = ("otp-secret", "smtp-username", "smtp-password", "smtp-from-address")


class ConfigurationError(Exception):
    pass


def configure(payload, run=subprocess.run):
    namespace = payload["namespace"]
    name = payload["name"]
    for value in (namespace, name):
        if not isinstance(value, str) or not re.fullmatch(r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?", value):
            raise ConfigurationError("Invalid namespace or Secret name.")
    action = payload.get("action")
    if action not in ("check", "configure"):
        raise ConfigurationError("Unsupported operation.")

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

    email = payload.get("email", "").strip().lower()
    password = payload.get("password", "").replace(" ", "")
    if not re.fullmatch(r"[^\s@]+@(gmail|googlemail)\.com", email) or not re.fullmatch(r"[A-Za-z]{16}", password):
        raise ConfigurationError("Expected a personal Gmail address and a 16-character app password.")
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
    return {**summary, "present": True, "ready": True, "otpGenerated": generated, "updated": True}


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
