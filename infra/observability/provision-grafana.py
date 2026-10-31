#!/usr/bin/env python3
"""Provision the existing TrueNAS Grafana over SSH; no Grafana password is needed."""

import argparse
import ipaddress
import json
from pathlib import Path
import shlex
import subprocess
import urllib.parse


def data_sources(address, prometheus_port, loki_port, tempo_port):
    address = str(ipaddress.IPv4Address(address))
    return {
        "apiVersion": 1,
        "datasources": [
            {
                "uid": "bikehub-prometheus", "name": "BikeHub Prometheus", "type": "prometheus",
                "access": "proxy", "url": f"http://{address}:{prometheus_port}", "editable": False,
                "jsonData": {"httpMethod": "POST", "timeInterval": "15s", "exemplarTraceIdDestinations": [
                    {"name": "trace_id", "datasourceUid": "bikehub-tempo"}
                ]},
            },
            {
                "uid": "bikehub-loki", "name": "BikeHub Loki", "type": "loki",
                "access": "proxy", "url": f"http://{address}:{loki_port}", "editable": False,
                "jsonData": {"derivedFields": [{
                    "name": "TraceID", "matcherRegex": "trace_id", "matcherType": "label",
                    "datasourceUid": "bikehub-tempo", "url": "$${__value.raw}",
                    "urlDisplayLabel": "View trace",
                }]},
            },
            {
                "uid": "bikehub-tempo", "name": "BikeHub Tempo", "type": "tempo",
                "access": "proxy", "url": f"http://{address}:{tempo_port}", "editable": False,
                "jsonData": {
                    "tracesToLogsV2": {
                        "datasourceUid": "bikehub-loki", "spanStartTimeShift": "-1m",
                        "spanEndTimeShift": "1m", "tags": [{"key": "service.name", "value": "service_name"}],
                        "customQuery": True,
                        "query": '{$$__tags} | trace_id = "$${__trace.traceId}"',
                    },
                    "nodeGraph": {"enabled": True},
                },
            },
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ssh-target", default="truenas_admin@truenas.local")
    parser.add_argument("--ssh-identity", type=Path, default=Path.home() / ".ssh/truenas_admin")
    parser.add_argument("--truenas-url", default="https://truenas.local")
    parser.add_argument("--ca-certificate", type=Path, default=Path.home() / ".config/truenas/pki/bikehub-ca.crt")
    parser.add_argument("--grafana-app", default="grafana")
    parser.add_argument("--certificate-id", type=int, default=2)
    parser.add_argument("--vm-address", required=True)
    parser.add_argument("--prometheus-port", type=int, default=30900)
    parser.add_argument("--loki-port", type=int, default=31000)
    parser.add_argument("--tempo-port", type=int, default=32000)
    args = parser.parse_args()
    api_url = urllib.parse.urlsplit(args.truenas_url)
    if api_url.scheme != "https" or not api_url.hostname or api_url.username or api_url.query or api_url.fragment or api_url.path not in ("", "/"):
        parser.error("--truenas-url must be an HTTPS origin without credentials, path, query or fragment")
    if not all(1 <= value <= 65535 for value in (args.prometheus_port, args.loki_port, args.tempo_port)):
        parser.error("Datasource ports must be in the range 1..65535")
    directory = Path(__file__).resolve().parent
    provider = {"apiVersion": 1, "providers": [{
        "name": "BikeHub", "orgId": 1, "folder": "BikeHub", "folderUid": "bikehub",
        "type": "file", "disableDeletion": True, "allowUiUpdates": False,
        "updateIntervalSeconds": 10,
        "options": {"path": "/var/lib/grafana/data/bikehub-provisioning/dashboards-json"},
    }]}
    encode = lambda value: json.dumps(value, ensure_ascii=False, indent=2) + "\n"
    payload = {
        "app": args.grafana_app,
        "certificate_id": args.certificate_id,
        "api_url": args.truenas_url,
        "ca_certificate": args.ca_certificate.expanduser().read_text(),
        "files": {
            "datasources/bikehub.yml": encode(data_sources(args.vm_address, args.prometheus_port, args.loki_port, args.tempo_port)),
            "dashboards/bikehub.yml": encode(provider),
            "dashboards-json/bikehub-api.json": (directory / "dashboards/bikehub-api.json").read_text(),
        },
    }
    command = ["ssh", "-T", "-o", "BatchMode=yes", "-o", "ConnectTimeout=10",
               "-i", str(args.ssh_identity.expanduser()), args.ssh_target,
               "python3 -c " + shlex.quote((directory / "truenas-provision.py").read_text())]
    result = subprocess.run(command, input=json.dumps(payload), text=True)
    if result.returncode:
        raise SystemExit(result.returncode)


if __name__ == "__main__":
    main()
