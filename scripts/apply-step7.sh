#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/.."

O=services/order-service

echo "1/4 Adding package RabbitMQ.Client and reference Contracts for Order..."
dotnet add $O package RabbitMQ.Client --version 7.1.2
dotnet add $O reference shared/Contracts/Contracts.csproj

echo "2/4 Adding VNPay variable in .env and .env.example (if not exists)..."
for f in .env .env.example; do
  [ -f "$f" ] || continue
  grep -q '^VNPAY_TMN_CODE=' "$f"    || printf '\nVNPAY_TMN_CODE=\n' >> "$f"
  grep -q '^VNPAY_HASH_SECRET=' "$f" || printf 'VNPAY_HASH_SECRET=\n' >> "$f"
  grep -q '^VNPAY_RETURN_URL=' "$f"  || printf 'VNPAY_RETURN_URL=http://localhost:5173/payment/result\n' >> "$f"
done

echo "3/4 Build..."
dotnet build shared/Contracts
dotnet build $O

echo "4/4 Done."
cat <<'MSG'

Order table have new column. EnsureCreated cannot add column dynamically, need to drop and create database again. If you have data, backup it first:

  docker compose stop order
  docker compose exec postgres psql -U app -d postgres -c "DROP DATABASE order_db"
  docker compose exec postgres psql -U app -d postgres -c "CREATE DATABASE order_db"

And fill VNPAY_TMN_CODE and VNPAY_HASH_SECRET ando .env, after that run:

  docker compose up -d --build
  bash scripts/test-vnpay.sh
MSG
