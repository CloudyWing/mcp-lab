"""Minimal HTTP server for integration tests: an OpenAPI spec plus OIDC discovery and JWKS documents.

GET /stats returns how often discovery and JWKS were fetched, so the smoke test can check caching.
GET /big returns a JSON array of about 2 MiB, so the smoke test can check response size limits.
"""

import json
from collections import Counter
from http.server import BaseHTTPRequestHandler, HTTPServer

BASE = "http://test-api:8000"
SPEC = {
    "openapi": "3.0.0",
    "info": {"title": "MCP Lab test API", "version": "1.0.0"},
    "servers": [{"url": BASE}],
    "paths": {
        "/items": {"get": {"responses": {"200": {"description": "Items"}}}},
        "/redirect": {"get": {"responses": {"302": {"description": "Redirect"}}}},
        "/big": {"get": {"responses": {"200": {"description": "Large JSON array"}}}},
    },
}
DISCOVERY = {"issuer": BASE, "jwks_uri": f"{BASE}/jwks"}
with open("/app/oidc-test-key.json", encoding="utf-8") as key_file:
    KEY = json.load(key_file)
JWKS = {"keys": [{"kty": "RSA", "kid": KEY["kid"], "use": "sig", "alg": "RS256", "e": KEY["e"], "n": KEY["n"]}]}
HITS = Counter()
# 2100 items of about 1 KiB each serialize to roughly 2.1 MiB.
BIG = [{"id": index, "pad": "x" * 1000} for index in range(2100)]
ROUTES = {
    "/openapi.json": SPEC,
    "/.well-known/openid-configuration": DISCOVERY,
    "/jwks": JWKS,
    "/items": [{"id": 1}],
    "/big": BIG,
}


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/redirect":
            self.send_response(302)
            self.send_header("Location", f"{BASE}/items")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return

        HITS[self.path] += 1
        payload = dict(HITS) if self.path == "/stats" else ROUTES.get(self.path)
        body = json.dumps(payload if payload is not None else {"error": "not found"}).encode()
        self.send_response(200 if payload is not None else 404)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


HTTPServer(("0.0.0.0", 8000), Handler).serve_forever()
