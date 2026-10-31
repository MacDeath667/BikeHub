"""Runs on TrueNAS through provision-grafana.py, using supported middleware APIs."""

import json
from pathlib import Path
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid


def call(method, *args, job=False):
    command = ["midclt", "call"] + (["-j"] if job else []) + [method]
    command += [json.dumps(value) for value in args]
    result = subprocess.run(command, capture_output=True, text=True, timeout=180)
    if result.returncode:
        # App API responses can include certificate keys; never print the raw response.
        raise RuntimeError(f"TrueNAS {method} failed with exit code {result.returncode}")
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError:
        # midclt prints string results (including one-use tokens) without JSON quotes.
        return result.stdout.strip()


def upload(path, content, opener, api_url):
    # A short-lived, single-use token is only used on this NAS to upload this public file.
    # It is neither printed nor stored, and does not create a persistent API key.
    token = call("auth.generate_token", 30, {}, False, True)
    boundary = "BikeHub" + uuid.uuid4().hex
    metadata = json.dumps({"method": "filesystem.put", "params": [str(path), {"mode": 0o644}]})
    body = (
        f'--{boundary}\r\nContent-Disposition: form-data; name="data"\r\n'
        f'Content-Type: application/json\r\n\r\n{metadata}\r\n'
        f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{path.name}"\r\n'
        f'Content-Type: application/octet-stream\r\n\r\n{content}\r\n--{boundary}--\r\n'
    ).encode()
    request = urllib.request.Request(
        api_url.rstrip("/") + "/_upload", data=body,
        headers={"Authorization": "Token " + token,
                 "Content-Type": "multipart/form-data; boundary=" + boundary}, method="POST")
    with opener.open(request, timeout=30) as response:
        job_id = json.load(response)["job_id"]
    for _ in range(30):
        job = call("core.get_jobs", [["id", "=", job_id]], {"get": True, "select": ["state", "error"]})
        if job["state"] == "SUCCESS":
            return
        if job["state"] in ("FAILED", "ABORTED"):
            raise RuntimeError(f"Upload of {path.name} failed: {job['error']}")
        time.sleep(1)
    raise TimeoutError("TrueNAS public configuration upload timed out")


def main():
    payload = json.load(sys.stdin)
    config = call("app.config", payload["app"])
    rows = call("app.query", [["name", "=", payload["app"]]], {"select": ["active_workloads"]})
    mounts = rows[0]["active_workloads"]["volumes"]
    paths = {entry["source"] for entry in mounts if entry["destination"] == "/var/lib/grafana/data"}
    if len(paths) != 1:
        raise RuntimeError("Expected one persistent /var/lib/grafana/data mount")
    data_path = Path(paths.pop())
    if not data_path.is_absolute() or not str(data_path).startswith("/mnt/"):
        raise ValueError("Grafana data must reside on a mounted TrueNAS dataset")
    root = data_path / "bikehub-provisioning"
    allowed = {"datasources/bikehub.yml", "dashboards/bikehub.yml", "dashboards-json/bikehub-api.json"}
    if set(payload["files"]) != allowed:
        raise ValueError("Only the three managed BikeHub provisioning files may be written")
    grafana = config["grafana"]
    env = grafana["additional_envs"]
    target = "/var/lib/grafana/data/bikehub-provisioning"
    existing = [entry for entry in env if entry["name"] == "GF_PATHS_PROVISIONING"]
    if existing and existing[0]["value"] != target:
        raise RuntimeError("Grafana already uses a different provisioning directory; merge it explicitly")
    context = ssl.create_default_context(cadata=payload["ca_certificate"])
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context))
    changed = []
    for relative, content in payload["files"].items():
        path = root / relative
        for folder in (root, path.parent):
            if not folder.exists():
                call("filesystem.mkdir", {"path": str(folder), "options": {"mode": "755"}})
        if not path.exists() or path.read_text() != content:
            upload(path, content, opener, payload["api_url"])
            changed.append(relative)

    environment_changed = not existing
    if environment_changed:
        env.append({"name": "GF_PATHS_PROVISIONING", "value": target})
    network = config["network"]
    certificate_changed = network["certificate_id"] != payload["certificate_id"]
    network["certificate_id"] = payload["certificate_id"]
    if environment_changed or certificate_changed:
        call("app.update", payload["app"], {"values": {"grafana": grafana, "network": network}}, job=True)
    elif any(name in changed for name in ("datasources/bikehub.yml", "dashboards/bikehub.yml")):
        call("app.redeploy", payload["app"], job=True)

    port = network["web_port"]["port_number"]
    hostname = urllib.parse.urlsplit(payload["api_url"]).hostname
    health_url = f"https://{hostname}:{port}/api/health"
    for _ in range(60):
        try:
            with opener.open(health_url, timeout=5) as response:
                health = json.load(response)
                if health.get("database") == "ok":
                    break
        except (OSError, ValueError):
            pass
        time.sleep(1)
    else:
        raise RuntimeError("Grafana did not become healthy with verified TLS")
    print(json.dumps({"changed": bool(changed or environment_changed or certificate_changed),
                      "files_changed": changed, "grafana": health_url.removesuffix("/api/health"),
                      "database": health["database"], "tls_verified": True}))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Grafana provisioning: {type(error).__name__}: {error}", file=sys.stderr)
        raise SystemExit(1)
