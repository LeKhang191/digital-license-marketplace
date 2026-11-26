#!/usr/bin/env bash

set -euo pipefail

cd "$(dirname "$0")/.."

suites=(
    test-vnpay-rollback.py
    test-vnpay-reliability.py
    test-wallet-core.py
    test-wallet-payment.py
    test-refund-integration.py
    test-license-allocation.py
    test-customer-ownership.py
    test-license-replacement.py
)

for suite in "${suites[@]}"; do
    echo
    echo "========================================"
    echo "RUNNING: $suite"
    echo "========================================"

    python3 -u "scripts/$suite"

    echo "[SUITE COMPLETED] $suite"
done

echo
echo "[COMPLETE] All regression scripts exited successfully."
