#!/usr/bin/env python3
"""E2E test for multi-item cart checkout using Marketplace Wallet."""
import base64
import importlib.util
import json
import time
import uuid
from decimal import Decimal
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name("test-license-allocation.py"),
)
t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

check = t.check


def subject(token: str) -> str:
    part = token.split(".")[1]
    part += "=" * (-len(part) % 4)
    return json.loads(base64.urlsafe_b64decode(part))["sub"]


def main():
    customer = t.login("test-user1")
    cid = subject(customer)

    p1 = t.create_product("cart-1", 1)
    p2 = t.create_product("cart-2", 2)

    t.sql(
        "payment_db",
        f"""
        BEGIN;
        INSERT INTO wallets ("Id", "CustomerId", "Balance", "CreatedAtUtc", "UpdatedAtUtc")
        VALUES ({t.literal(str(uuid.uuid4()))}, {t.literal(cid)}, 0, now(), now())
        ON CONFLICT ("CustomerId") DO NOTHING;
        UPDATE wallets SET "Balance" = "Balance" + 500000, "UpdatedAtUtc" = now()
        WHERE "CustomerId" = {t.literal(cid)};
        COMMIT;
        """,
    )
    before = Decimal(str(t.request("GET", "/wallet/me", customer)["balance"]))

    order = t.request(
        "POST",
        "/orders",
        customer,
        {
            "paymentMethod": "WALLET",
            "items": [
                {"productId": p1, "quantity": 1},
                {"productId": p2, "quantity": 2},
            ],
        },
    )
    check("Multi-item order created", order["status"] == "PENDING" and len(order["items"]) == 3)
    check("Total amount matches sum of items", Decimal(str(order["totalAmount"])) == Decimal("300000"))

    t.request("POST", f"/orders/{order['id']}/pay", customer)

    final = None
    for _ in range(30):
        cur = t.request("GET", f"/orders/{order['id']}", customer)
        if cur["status"] in ("COMPLETED", "REFUNDED", "CANCELED"):
            final = cur
            break
        time.sleep(0.5)

    check("Multi-item wallet order completed", final is not None and final["status"] == "COMPLETED")
    after = Decimal(str(t.request("GET", "/wallet/me", customer)["balance"]))
    check("Wallet deducted exact 300000 VND", before - after == Decimal("300000"))

    licenses = t.request("GET", f"/licenses/my?orderId={order['id']}", customer)
    check("Delivered 3 licenses for multi-item order", len(licenses) == 3)
    print("PASS: Multi-item cart checkout & wallet delivery verified.")


if __name__ == "__main__":
    main()
