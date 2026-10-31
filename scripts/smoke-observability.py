#!/usr/bin/env python3
"""Follow one API request through Loki, Tempo and Prometheus."""

import argparse
import base64
import ipaddress
import json
import secrets
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request


def walk(value):
    if isinstance(value, dict):
        yield value
        for child in value.values():
            yield from walk(child)
    elif isinstance(value, list):
        for child in value:
            yield from walk(child)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api-url", default="http://127.0.0.1:5179")
    parser.add_argument("--vm-address", default="192.168.0.49")
    parser.add_argument("--environment", default="Development")
    parser.add_argument("--ca-certificate")
    parser.add_argument("--timeout", type=int, default=90)
    args = parser.parse_args()
    address = str(ipaddress.IPv4Address(args.vm_address))
    context = ssl.create_default_context(cafile=args.ca_certificate)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context))

    def get(url):
        with opener.open(url, timeout=10) as response:
            return json.load(response)

    trace_id = secrets.token_hex(16)
    request = urllib.request.Request(args.api_url.rstrip("/") + "/api/feed?pageSize=1",
                                    headers={"traceparent": f"00-{trace_id}-{secrets.token_hex(8)}-01"})
    with opener.open(request, timeout=10) as response:
        assert response.status == 200 and len(json.load(response)["items"]) == 1
    print(f"API request: OK, trace_id={trace_id}", flush=True)
    complete = set()
    instance = None
    deadline = time.monotonic() + args.timeout
    while time.monotonic() < deadline:
        try:
            if "trace" not in complete:
                trace = get(f"http://{address}:32000/api/traces/{trace_id}")
                identifiers = {row["traceId"] for row in walk(trace) if "traceId" in row}
                expected = {trace_id, base64.b64encode(bytes.fromhex(trace_id)).decode()}
                assert expected & identifiers, "Tempo returned a different trace"
                instance = next(row["value"]["stringValue"] for row in walk(trace)
                                if row.get("key") == "service.instance.id")
                environment = next(row["value"]["stringValue"] for row in walk(trace)
                                   if row.get("key") == "deployment.environment.name")
                assert environment == args.environment, (environment, args.environment)
                complete.add("trace")
                print("Tempo: matching trace and environment found", flush=True)
            if "logs" not in complete:
                query = '{service_name="bikehub-api"} | trace_id="' + trace_id + '"'
                logs = get(f"http://{address}:31000/loki/api/v1/query_range?" + urllib.parse.urlencode({"query": query, "limit": 100}))
                streams = logs["data"]["result"]
                if any(row["stream"].get("scope_name") == "BikeHub.Api.Middleware.StatisticsMiddleware" and
                       row["stream"].get("service_instance_id") == instance for row in streams):
                    complete.add("logs")
                    print("Loki: HTTP request log linked by trace ID found", flush=True)
            if "metrics" not in complete:
                labels = 'service_name="bikehub-api",service_instance_id=' + json.dumps(instance)
                query = "sum(http_server_request_duration_seconds_count{" + labels + '})'
                metrics = get(f"http://{address}:30900/api/v1/query?" + urllib.parse.urlencode({"query": query}))
                rows = metrics["data"]["result"]
                if rows and float(rows[0]["value"][1]) >= 1:
                    complete.add("metrics")
                    print("Prometheus: HTTP metric from the same API instance found", flush=True)
        except (urllib.error.URLError, TimeoutError):
            pass
        if complete == {"trace", "logs", "metrics"}:
            print("OK: API -> OTLP Collector -> metrics, logs and traces", flush=True)
            return
        time.sleep(2)
    raise SystemExit("Missing telemetry: " + ", ".join(sorted({"trace", "logs", "metrics"} - complete)))


if __name__ == "__main__":
    main()
