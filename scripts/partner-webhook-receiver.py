#!/usr/bin/env python3
"""
Standalone Partner Outbound Webhook Receiver (chạy trực tiếp trên khenserver hoặc máy đối tác).

- Xác thực chữ ký HMAC-SHA256 trong header X-DLM-Signature theo chuỗi "<X-DLM-Timestamp>.<raw_body>".
- Kiểm tra cửa sổ thời gian X-DLM-Timestamp (mặc định <= 300 giây) chống replay attack.
- Không có plaintext key trong webhook; tùy chọn tự động gọi /api/partner/v1/orders/{id}/licenses/claim
  khi truyền đủ --api-base-url, --token-url, --client-id, --client-secret.

Ví dụ chạy trên khenserver:
  python3 -u scripts/partner-webhook-receiver.py \
    --host 0.0.0.0 --port 9090 \
    --secret "<chuỗi-secret-tối-thiểu-32-ký-tự>"
"""

import argparse
import hashlib
import hmac
import json
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlencode
from urllib.request import Request, urlopen


def verify_signature(secret: str, timestamp_raw: str, signature: str, body: bytes, max_skew: int) -> tuple[bool, str]:
    try:
        ts = int(timestamp_raw)
    except (TypeError, ValueError):
        return False, "invalid timestamp"

    if abs(int(time.time()) - ts) > max_skew:
        return False, "timestamp outside allowed replay window"

    if not signature or not signature.startswith("sha256="):
        return False, "missing sha256 signature prefix"

    signed_payload = f"{ts}.".encode("utf-8") + body
    expected = "sha256=" + hmac.new(secret.encode("utf-8"), signed_payload, hashlib.sha256).hexdigest()

    if not hmac.compare_digest(expected, signature):
        return False, "signature mismatch"

    return True, "ok"


def fetch_partner_token(token_url: str, client_id: str, client_secret: str) -> str:
    data = urlencode({
        "grant_type": "client_credentials",
        "client_id": client_id,
        "client_secret": client_secret,
    }).encode("utf-8")
    req = Request(token_url, data=data, headers={"Content-Type": "application/x-www-form-urlencoded"})
    with urlopen(req, timeout=10) as resp:
        return json.loads(resp.read().decode("utf-8"))["access_token"]


def claim_licenses(api_base_url: str, claim_path: str, token: str) -> dict:
    url = api_base_url.rstrip("/") + claim_path
    req = Request(
        url,
        data=b"",
        method="POST",
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/json",
        },
    )
    with urlopen(req, timeout=15) as resp:
        return json.loads(resp.read().decode("utf-8"))


def make_handler(args):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, fmt, *fmt_args):
            print(f"[HTTP] {self.address_string()} - " + (fmt % fmt_args), flush=True)

        def do_GET(self):
            if self.path == "/health":
                self._send_json(200, {"status": "ok"})
            else:
                self._send_json(404, {"error": "not found"})

        def do_POST(self):
            length = int(self.headers.get("Content-Length", "0"))
            if length <= 0 or length > 65536:
                self._send_json(400, {"error": "invalid content length"})
                return

            raw = self.rfile.read(length)
            event_type = self.headers.get("X-DLM-Event", "")
            delivery_id = self.headers.get("X-DLM-Delivery-Id", "")
            timestamp = self.headers.get("X-DLM-Timestamp", "")
            signature = self.headers.get("X-DLM-Signature", "")

            ok, reason = verify_signature(args.secret, timestamp, signature, raw, args.max_skew)
            if not ok:
                print(f"[REJECT] delivery={delivery_id} event={event_type} reason={reason}", flush=True)
                self._send_json(401, {"error": reason})
                return

            try:
                payload = json.loads(raw.decode("utf-8"))
            except json.JSONDecodeError:
                self._send_json(400, {"error": "invalid json"})
                return

            print(
                f"[VERIFIED] delivery={delivery_id} event={event_type} "
                f"partner={payload.get('partnerId')} order={payload.get('orderId')} "
                f"status={payload.get('status')} externalRef={payload.get('externalReference')}",
                flush=True,
            )

            if (
                args.auto_claim
                and event_type == "partner.order.completed"
                and payload.get("claimPath")
            ):
                try:
                    token = fetch_partner_token(args.token_url, args.client_id, args.client_secret)
                    claimed = claim_licenses(args.api_base_url, payload["claimPath"], token)
                    print(
                        f"[CLAIMED] order={payload.get('orderId')} count={len(claimed.get('licenses', []))}",
                        flush=True,
                    )
                except Exception as exc:
                    print(f"[CLAIM_ERROR] order={payload.get('orderId')} error={exc}", flush=True)

            self._send_json(200, {"received": True, "deliveryId": delivery_id})

        def _send_json(self, code: int, obj: dict):
            body = json.dumps(obj).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    return Handler


def main():
    parser = argparse.ArgumentParser(description="DLM Partner Outbound Webhook Receiver")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=9090)
    parser.add_argument("--secret", required=True, help="Shared HMAC-SHA256 signing secret (>= 32 chars)")
    parser.add_argument("--max-skew", type=int, default=300, help="Replay protection window in seconds")
    parser.add_argument("--auto-claim", action="store_true", help="Auto-call claim API on partner.order.completed")
    parser.add_argument("--api-base-url", default="", help="Gateway/Nginx base URL (e.g. https://marketplace.example.com)")
    parser.add_argument("--token-url", default="", help="Keycloak token URL")
    parser.add_argument("--client-id", default="", help="Partner Keycloak client_id")
    parser.add_argument("--client-secret", default="", help="Partner Keycloak client_secret")
    args = parser.parse_args()

    if len(args.secret) < 32:
        sys.exit("[ERROR] --secret must be at least 32 characters")

    if args.auto_claim and not all([args.api_base_url, args.token_url, args.client_id, args.client_secret]):
        sys.exit("[ERROR] --auto-claim requires --api-base-url, --token-url, --client-id, --client-secret")

    server = ThreadingHTTPServer((args.host, args.port), make_handler(args))
    print(f"[LISTEN] Partner webhook receiver listening on http://{args.host}:{args.port}/webhook", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[STOP] Shutting down receiver", flush=True)
        server.server_close()


if __name__ == "__main__":
    main()
