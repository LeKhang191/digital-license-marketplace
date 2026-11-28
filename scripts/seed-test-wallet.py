#!/usr/bin/env python3
"""Local DEV/TEST only: seed VND balance into a customer's Marketplace Wallet."""
import argparse
import base64
import importlib.util
import json
import uuid
from decimal import Decimal
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name("test-license-allocation.py"),
)
t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)


def subject(token: str) -> str:
    part = token.split(".")[1]
    part += "=" * (-len(part) % 4)
    return json.loads(base64.urlsafe_b64decode(part))["sub"]


def main():
    parser = argparse.ArgumentParser(description="Seed local test wallet balance.")
    parser.add_argument("--user", default="test-user1", help="Keycloak username (default: test-user1)")
    parser.add_argument("--sub", default="", help="Keycloak user sub UUID (skips login if set)")
    parser.add_argument("--amount", default="1000000000", help="Amount in VND (default: 1000000000)")
    args = parser.parse_args()

    amount = Decimal(args.amount)
    if amount <= 0:
        raise SystemExit("Amount must be positive.")

    customer_id = args.sub.strip() or subject(t.login(args.user))
    wid = str(uuid.uuid4())
    txid = str(uuid.uuid4())
    refid = str(uuid.uuid4())

    t.sql(
        "payment_db",
        f"""
        BEGIN;
        INSERT INTO wallets ("Id", "CustomerId", "Balance", "CreatedAtUtc", "UpdatedAtUtc")
        VALUES ({t.literal(wid)}, {t.literal(customer_id)}, 0, now(), now())
        ON CONFLICT ("CustomerId") DO NOTHING;

        UPDATE wallets
        SET "Balance" = "Balance" + {amount}, "UpdatedAtUtc" = now()
        WHERE "CustomerId" = {t.literal(customer_id)};

        INSERT INTO wallet_transactions ("Id", "WalletId", "Type", "Amount", "BalanceAfter", "ReferenceId", "CreatedAtUtc")
        SELECT {t.literal(txid)}, "Id", 'REFUND', {amount}, "Balance", {t.literal(refid)}, now()
        FROM wallets WHERE "CustomerId" = {t.literal(customer_id)};
        COMMIT;
        """,
    )
    new_balance = t.sql(
        "payment_db",
        f'SELECT "Balance" FROM wallets WHERE "CustomerId" = {t.literal(customer_id)};',
    )
    print(f"Seeded {amount:,.0f} VND for customer {customer_id}. Current balance: {new_balance} VND")


if __name__ == "__main__":
    main()
