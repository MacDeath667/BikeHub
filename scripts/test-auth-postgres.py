#!/usr/bin/env python3
"""Run auth tests against disposable local PostgreSQL; never uses the application's database."""
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
name = "bikehub-auth-test-" + secrets.token_hex(6)
password = secrets.token_hex(32)

def docker(*args, **kwargs):
    return subprocess.run(["docker", *args], check=True, text=True, **kwargs)

try:
    with tempfile.TemporaryDirectory(prefix="bikehub-auth-") as directory:
        env_file = Path(directory) / "postgres.env"
        env_file.write_text(f"POSTGRES_USER=bikehub_test\nPOSTGRES_DB=bikehub_test\nPOSTGRES_PASSWORD={password}\n")
        env_file.chmod(0o600)
        docker("run", "--detach", "--rm", "--name", name, "--env-file", str(env_file),
               "--publish", "127.0.0.1::5432", "--tmpfs", "/var/lib/postgresql:rw,size=512m",
               "postgres:18.6", stdout=subprocess.DEVNULL)
    for attempt in range(60):
        ready = subprocess.run(["docker", "exec", name, "pg_isready", "-U", "bikehub_test"],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if ready.returncode == 0:
            break
        time.sleep(0.5)
    else:
        raise RuntimeError("Test PostgreSQL did not become ready")
    ports = json.loads(docker("inspect", "--format", "{{json .NetworkSettings.Ports}}", name,
                              capture_output=True).stdout)
    port = ports["5432/tcp"][0]["HostPort"]
    environment = os.environ.copy()
    environment["BIKEHUB_TEST_POSTGRES"] = (
        f"Host=127.0.0.1;Port={port};Database=bikehub_test;Username=bikehub_test;Password={password}")
    result = subprocess.run(["dotnet", "test", "tests/BikeHub.Api.Tests", "--disable-build-servers", "--nologo"],
                            cwd=ROOT, env=environment)
    raise SystemExit(result.returncode)
finally:
    subprocess.run(["docker", "rm", "--force", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
