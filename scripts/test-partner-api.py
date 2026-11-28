#!/usr/bin/env python3
"""
End-to-End Test Suite cho Partner ID & Partner API (Phương án A: Keycloak client_credentials).

Yêu cầu trước khi chạy:
  1. Stack backend đang chạy (gateway :8000, keycloak :8081) và đã chạy migration mới nhất.
  2. Đã provision 2 partner client (ví dụ prt_test_a, prt_test_b) bằng scripts/provision-partner.sh.
  3. Có ít nhất 1 sản phẩm đang active trong Catalog kèm >= 1 license key AVAILABLE trong kho.
  4. Truyền biến môi trường:
       PARTNER_A_ID, PARTNER_A_CLIENT_ID, PARTNER_A_CLIENT_SECRET
       PARTNER_B_ID, PARTNER_B_CLIENT_ID, PARTNER_B_CLIENT_SECRET (tùy chọn để test cách ly chéo)
       AUDITOR_USERNAME, AUDITOR_PASSWORD (tài khoản role transaction_auditor)
       CUSTOMER_USERNAME, CUSTOMER_PASSWORD (tùy chọn để test chặn quyền chéo)
       PARTNER_WEBHOOK_URL, PARTNER_WEBHOOK_SECRET (tùy chọn để test outbound webhook tới khenserver)
"""

import base64
import json
import os
import sys
import time
import uuid
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

API_BASE = os.getenv("API_BASE", "http://127.0.0.1:8000").rstrip("/")
KC_BASE = os.getenv("KEYCLOAK_URL", "http://127.0.0.1:8081").rstrip("/")
REALM = os.getenv("KEYCLOAK_REALM", "marketplace")
WEB_CLIENT_ID = os.getenv("KEYCLOAK_CLIENT_ID", "marketplace-web")

TOKEN_URL = f"{KC_BASE}/realms/{REALM}/protocol/openid-connect/token"

passed = 0
failed = 0


def check(name: str, condition: bool, detail: str = ""):
    global passed, failed
    if condition:
        passed += 1
        print(f"[PASS] {name}", flush=True)
    else:
        failed += 1
        suffix = f" ({detail})" if detail else ""
        print(f"[FAIL] {name}{suffix}", flush=True)


def decode_jwt_payload(token: str) -> dict:
    part = token.split(".")[1]
    padded = part + "=" * (-len(part) % 4)
    return json.loads(base64.urlsafe_b64decode(padded).decode("utf-8"))


def get_client_credentials_token(client_id: str, client_secret: str) -> str:
    body = urlencode({
        "grant_type": "client_credentials",
        "client_id": client_id,
        "client_secret": client_secret,
    }).encode("utf-8")
    req = Request(TOKEN_URL, data=body, headers={"Content-Type": "application/x-www-form-urlencoded"})
    try:
        with urlopen(req, timeout=15) as resp:
            return json.loads(resp.read().decode("utf-8"))["access_token"]
    except HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        sys.exit(f"[FAIL] Keycloak client_credentials failed for client_id='{client_id}' (HTTP {exc.code}): {detail}")


def get_password_token(username: str, password: str) -> str:
    body = urlencode({
        "grant_type": "password",
        "client_id": WEB_CLIENT_ID,
        "username": username,
        "password": password,
    }).encode("utf-8")
    req = Request(TOKEN_URL, data=body, headers={"Content-Type": "application/x-www-form-urlencoded"})
    try:
        with urlopen(req, timeout=15) as resp:
            return json.loads(resp.read().decode("utf-8"))["access_token"]
    except HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        sys.exit(f"[FAIL] Keycloak password login failed for username='{username}' (HTTP {exc.code}): {detail}")


def api(method: str, path: str, token: str | None = None, data: dict | None = None, headers: dict | None = None):
    url = API_BASE + path
    req_headers = {"Accept": "application/json"}
    if token:
        req_headers["Authorization"] = f"Bearer {token}"
    if headers:
        req_headers.update(headers)
    payload = None
    if data is not None:
        payload = json.dumps(data).encode("utf-8")
        req_headers["Content-Type"] = "application/json"

    req = Request(url, data=payload, method=method, headers=req_headers)
    try:
        with urlopen(req, timeout=20) as resp:
            raw = resp.read().decode("utf-8")
            return resp.status, (json.loads(raw) if raw else None)
    except HTTPError as exc:
        raw = exc.read().decode("utf-8")
        try:
            body = json.loads(raw) if raw else None
        except json.JSONDecodeError:
            body = {"raw": raw}
        return exc.code, body


def main():
    partner_a_id = os.environ.get("PARTNER_A_ID", "")
    partner_a_cid = os.environ.get("PARTNER_A_CLIENT_ID", "")
    partner_a_secret = os.environ.get("PARTNER_A_CLIENT_SECRET", "")

    auditor_user = os.environ.get("AUDITOR_USERNAME", "")
    auditor_pass = os.environ.get("AUDITOR_PASSWORD", "")

    if not all([partner_a_id, partner_a_cid, partner_a_secret, auditor_user, auditor_pass]):
        sys.exit(
            "[STOP] Required env vars: PARTNER_A_ID, PARTNER_A_CLIENT_ID, PARTNER_A_CLIENT_SECRET, "
            "AUDITOR_USERNAME, AUDITOR_PASSWORD"
        )

    print("===== 1. AUTH & CLAIMS (METHOD A: CLIENT CREDENTIALS) =====")
    token_a = get_client_credentials_token(partner_a_cid, partner_a_secret)
    claims_a = decode_jwt_payload(token_a)
    roles_a = claims_a.get("realm_access", {}).get("roles", [])

    check("Partner A JWT has expected partner_id claim", claims_a.get("partner_id") == partner_a_id, str(claims_a.get("partner_id")))
    check("Partner A JWT has partner_api role", "partner_api" in roles_a, str(roles_a))
    check("Partner A JWT does NOT have customer role", "customer" not in roles_a, str(roles_a))

    auditor_token = get_password_token(auditor_user, auditor_pass)

    print("\n===== 2. RBAC ISOLATION =====")
    code, _ = api("GET", "/api/orders", token_a)
    check("Partner token blocked on Customer /api/orders (403)", code == 403, f"got {code}")

    code, _ = api("GET", "/api/licenses/me", token_a)
    check("Partner token blocked on Customer /api/licenses/me (403)", code == 403, f"got {code}")

    code, _ = api("POST", f"/api/partner-admin/wallets/{partner_a_id}/topups", token_a, {"amount": 10000}, {"Idempotency-Key": f"self-{uuid.uuid4().hex[:12]}"})
    check("Partner token blocked on Auditor top-up endpoint (403)", code == 403, f"got {code}")

    code, _ = api("GET", "/api/partner/v1/catalog/products", auditor_token)
    check("Auditor token blocked on Partner API catalog (403)", code == 403, f"got {code}")

    cust_user = os.environ.get("CUSTOMER_USERNAME", "")
    cust_pass = os.environ.get("CUSTOMER_PASSWORD", "")
    if cust_user and cust_pass:
        cust_token = get_password_token(cust_user, cust_pass)
        code, _ = api("GET", "/api/partner/v1/catalog/products", cust_token)
        check("Customer token blocked on Partner API (403)", code == 403, f"got {code}")

    print("\n===== 3. CATALOG & LIVE STOCK =====")
    code, cat = api("GET", "/api/partner/v1/catalog/products?page=1&pageSize=50", token_a)
    check("Partner catalog list returns 200", code == 200, f"got {code}")
    in_stock = [p for p in (cat or {}).get("items", []) if p.get("availableStock", 0) > 0]
    check("Found at least one in-stock product", len(in_stock) > 0)
    if not in_stock:
        sys.exit("[STOP] No in-stock product available to run Partner order test")

    product = in_stock[0]
    price = float(product["price"])
    print(f"[INFO] Selected product: {product['name']} ({product['id']}) price={price} stock={product['availableStock']}")

    print("\n===== 4. AUDITOR TOP-UP & IDEMPOTENCY =====")
    topup_key = f"topup-{uuid.uuid4().hex[:16]}"
    topup_amount = max(price * 2, 100000)

    code1, top1 = api(
        "POST",
        f"/api/partner-admin/wallets/{partner_a_id}/topups",
        auditor_token,
        {"amount": topup_amount, "note": "E2E partner top-up"},
        {"Idempotency-Key": topup_key},
    )
    check("Auditor top-up succeeds (201)", code1 == 201, f"got {code1} {top1}")

    code2, top2 = api(
        "POST",
        f"/api/partner-admin/wallets/{partner_a_id}/topups",
        auditor_token,
        {"amount": topup_amount, "note": "E2E partner top-up"},
        {"Idempotency-Key": topup_key},
    )
    check(
        "Auditor top-up retry is idempotent (200, alreadyProcessed=true, same balance)",
        code2 == 200 and (top2 or {}).get("alreadyProcessed") is True and (top2 or {}).get("balanceAfter") == (top1 or {}).get("balanceAfter"),
        f"got {code2} {top2}",
    )

    code3, _ = api(
        "POST",
        f"/api/partner-admin/wallets/{partner_a_id}/topups",
        auditor_token,
        {"amount": topup_amount + 1000, "note": "E2E partner top-up"},
        {"Idempotency-Key": topup_key},
    )
    check("Auditor top-up mismatch returns 409 IDEMPOTENCY_CONFLICT", code3 == 409, f"got {code3}")

    code_bal, bal = api("GET", "/api/partner/v1/wallet/balance", token_a)
    check("Partner wallet balance reflects top-up", code_bal == 200 and float((bal or {}).get("balance", 0)) >= price, f"got {code_bal} {bal}")

    webhook_url = os.environ.get("PARTNER_WEBHOOK_URL", "")
    webhook_secret = os.environ.get("PARTNER_WEBHOOK_SECRET", "")
    if webhook_url and webhook_secret:
        print("\n===== 5. CONFIGURE OUTBOUND WEBHOOK =====")
        code_wh, wh = api(
            "PUT",
            "/api/partner/v1/webhook",
            token_a,
            {"callbackUrl": webhook_url, "signingSecret": webhook_secret, "isActive": True},
        )
        check("Partner webhook configured (200)", code_wh == 200 and (wh or {}).get("configured") is True, f"got {code_wh} {wh}")

    print("\n===== 6. CREATE PARTNER ORDER & IDEMPOTENCY =====")
    order_key = f"ord-{uuid.uuid4().hex[:16]}"
    ext_ref = f"EXT-{uuid.uuid4().hex[:8]}"

    code_o1, ord1 = api(
        "POST",
        "/api/partner/v1/orders",
        token_a,
        {"productId": product["id"], "quantity": 1, "externalReference": ext_ref},
        {"Idempotency-Key": order_key},
    )
    check("Partner order created & paid via wallet (201)", code_o1 == 201 and (ord1 or {}).get("orderId"), f"got {code_o1} {ord1}")
    order_id = ord1["orderId"]

    code_o2, ord2 = api(
        "POST",
        "/api/partner/v1/orders",
        token_a,
        {"productId": product["id"], "quantity": 1, "externalReference": ext_ref},
        {"Idempotency-Key": order_key},
    )
    check(
        "Partner order retry is idempotent (200, same orderId)",
        code_o2 == 200 and (ord2 or {}).get("orderId") == order_id and (ord2 or {}).get("alreadyProcessed") is True,
        f"got {code_o2} {ord2}",
    )

    code_o3, _ = api(
        "POST",
        "/api/partner/v1/orders",
        token_a,
        {"productId": product["id"], "quantity": 2, "externalReference": ext_ref},
        {"Idempotency-Key": order_key},
    )
    check("Partner order conflicting retry returns 409", code_o3 == 409, f"got {code_o3}")

    print("\n===== 7. WAIT FOR FULFILLMENT & CLAIM LICENSES =====")
    final_status = None
    for _ in range(30):
        _, polled = api("GET", f"/api/partner/v1/orders/{order_id}", token_a)
        final_status = (polled or {}).get("status")
        if final_status in ("COMPLETED", "REFUNDED", "CANCELED"):
            break
        time.sleep(1)

    check("Partner order reached COMPLETED", final_status == "COMPLETED", f"final status={final_status}")

    if final_status == "COMPLETED":
        code_c1, claim1 = api("POST", f"/api/partner/v1/orders/{order_id}/licenses/claim", token_a)
        licenses1 = (claim1 or {}).get("licenses", [])
        check("Partner A claims plaintext license key (200)", code_c1 == 200 and len(licenses1) == 1 and bool(licenses1[0].get("licenseKey")), f"got {code_c1}")

        code_c2, claim2 = api("POST", f"/api/partner/v1/orders/{order_id}/licenses/claim", token_a)
        licenses2 = (claim2 or {}).get("licenses", [])
        check(
            "Partner A claim retry returns identical key & revealedAtUtc",
            code_c2 == 200 and len(licenses2) == 1 and licenses2[0]["licenseKey"] == licenses1[0]["licenseKey"] and licenses2[0]["revealedAtUtc"] == licenses1[0]["revealedAtUtc"],
            f"got {code_c2}",
        )

    partner_b_cid = os.environ.get("PARTNER_B_CLIENT_ID", "")
    partner_b_secret = os.environ.get("PARTNER_B_CLIENT_SECRET", "")
    if partner_b_cid and partner_b_secret:
        print("\n===== 8. CROSS-PARTNER ISOLATION =====")
        token_b = get_client_credentials_token(partner_b_cid, partner_b_secret)
        code_b1, _ = api("GET", f"/api/partner/v1/orders/{order_id}", token_b)
        check("Partner B cannot read Partner A order (404)", code_b1 == 404, f"got {code_b1}")

        code_b2, _ = api("POST", f"/api/partner/v1/orders/{order_id}/licenses/claim", token_b)
        check("Partner B cannot claim Partner A licenses (404)", code_b2 == 404, f"got {code_b2}")

    if webhook_url and webhook_secret:
        print("\n===== 9. CHECK WEBHOOK DELIVERY STATUS =====")
        time.sleep(3)
        code_dlv, dlv = api("GET", "/api/partner/v1/webhook/deliveries?page=1&pageSize=10", token_a)
        matching = [d for d in (dlv or {}).get("items", []) if d.get("orderId") == order_id and d.get("eventType") == "partner.order.completed"]
        check(
            "Partner outbound webhook recorded & delivered",
            code_dlv == 200 and len(matching) == 1 and matching[0].get("status") == "DELIVERED",
            f"got {matching}",
        )

    print(f"\n===== SUMMARY: {passed} PASS / {failed} FAIL =====")
    sys.exit(0 if failed == 0 else 1)


if __name__ == "__main__":
    main()
