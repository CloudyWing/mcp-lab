"""Minimal HTTP server for integration tests: an OpenAPI spec plus OIDC discovery and JWKS documents."""

import json
from http.server import BaseHTTPRequestHandler, HTTPServer

BASE = "http://test-api:8000"
SPEC = {
    "openapi": "3.0.0",
    "info": {"title": "MCP Lab test API", "version": "1.0.0"},
    "servers": [{"url": BASE}],
    "paths": {
        "/items": {"get": {"responses": {"200": {"description": "Items"}}}},
        "/redirect": {"get": {"responses": {"302": {"description": "Redirect"}}}},
    },
}
DISCOVERY = {"issuer": BASE, "jwks_uri": f"{BASE}/jwks"}
JWKS = {"keys": [{"kty": "RSA", "kid": "test-key", "use": "sig", "e": "AQAB",
                  "n": "sXchDaQebHnPiGvyDOAT4saGEUetSyo9MKLOoWFsueri23bOdgWp4Dy1WlUzewbgBHod5pcM9H95GQRV3JDXboIRROSBigeC5yjU1hGzHHyXss8UDprecbAYxknTcQkhslANGRUZmdTOQ5qTRsLAt6BTYuyvVRdhS8exSZEy_c4gs_7svlJJQ4H9_NxsiIoLwAEk7-Q3UXERGYw_75IDrGA84-lA_-Ct4eTlXHBIY2EaV7t7LjJaynVJCpkv4LKjTTAumiGUIuQhrNhZLuF_RJLqHpM2kgWFLU7-VTdL1VbC2tejvcI2BlMkEpk1BzBZI0KQB0GaDWFLN-aEAw3vRw"}]}
ROUTES = {
    "/openapi.json": SPEC,
    "/.well-known/openid-configuration": DISCOVERY,
    "/jwks": JWKS,
    "/items": [{"id": 1}],
}


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/redirect":
            self.send_response(302)
            self.send_header("Location", f"{BASE}/items")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return

        payload = ROUTES.get(self.path)
        body = json.dumps(payload if payload is not None else {"error": "not found"}).encode()
        self.send_response(200 if payload is not None else 404)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


HTTPServer(("0.0.0.0", 8000), Handler).serve_forever()
