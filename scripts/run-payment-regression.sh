#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

echo "Development environment only."
echo "Tests may modify test data and stop services."

read -rp "Type TEST to continue: " confirm
[[ "$confirm" == "TEST" ]] || exit 2

[[ "${VNPAY_TMN_CODE:-}" == "TEST0001" ]] || {
    echo "[STOP] Synthetic merchant required."
    exit 2
}

[[ -n "${VNPAY_HASH_SECRET:-}" ]] || {
    echo "[STOP] Synthetic secret required."
    exit 2
}

docker compose exec -T payment sh -c \
    'test "$VnPay__TmnCode" = TEST0001' || {
    echo "[STOP] Payment runtime is not synthetic."
    exit 2
}

passed=0

for suite in scripts/test-*.py scripts/test-*.sh; do

    case "$(basename "$suite")" in
        test-vnpay-sandbox.py)
            continue
            ;;
    esac

    echo
    echo "===== RUNNING: $suite ====="

    if [[ "$suite" == *.py ]]; then
        python3 -u "$suite"
    else
        bash "$suite"
    fi

    passed=$((passed + 1))

    echo "[SUITE EXIT OK] $suite"
done

echo
echo "===== REGRESSION SUMMARY ====="
echo "SUCCESSFUL SUITE EXITS: $passed"
