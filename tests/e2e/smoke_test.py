"""Health smoke checks against running services (HTTP only, not browser UI)."""
import argparse
import json
import os
from urllib.request import Request, urlopen


def read_json(url, origin=None):
    headers = {"Origin": origin} if origin else {}
    with urlopen(Request(url, headers=headers), timeout=15) as response:
        assert response.status == 200, f"{url}: HTTP {response.status}"
        if origin:
            assert response.headers.get("Access-Control-Allow-Origin") == origin, "CORS missing"
        return json.load(response)


def check_system(body, expected):
    assert body["api"]["status"] == "online", body
    assert body["status"] == ("online" if expected == "online" else "degraded"), body
    assert body["python"]["status"] == expected, body
    if expected == "online":
        assert body["python"]["service"], body
        assert body["python"]["version"], body


def verify(backend, python, expected="online", origin="http://localhost:5173"):
    assert read_json(backend + "/health")["status"] == "ok"
    body = read_json(backend + "/api/v1/system/health", origin)
    check_system(body, expected)
    if expected == "online":
        health = read_json(python + "/health")
        assert health["status"] == "ok"
        for key in ("service", "version"):
            assert body["python"][key] == health[key], (body, health)
    print(f"OK: API online, Python {expected}, contrato y CORS verificados.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-python", choices=["online", "unavailable", "timeout", "invalid_response", "error"], default="online")
    args = parser.parse_args()
    verify(os.getenv("BACKEND_URL", "http://localhost:5080").rstrip("/"),
           os.getenv("PYTHON_URL", "http://localhost:8001").rstrip("/"),
           args.expected_python, os.getenv("FRONTEND_ORIGIN", "http://localhost:5173"))
