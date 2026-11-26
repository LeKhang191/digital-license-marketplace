#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

echo "=== RabbitMQ Provisioning ==="

READY=false

for ((attempt=1; attempt<=30; attempt++)); do
    if docker compose exec -T rabbitmq \
        rabbitmq-diagnostics -q check_running \
        >/dev/null 2>&1; then
        READY=true
        break
    fi

    echo "Waiting for RabbitMQ ($attempt/30)..."
    sleep 3
done

if [ "$READY" != "true" ]; then
    echo "[ERROR] RabbitMQ is not ready."
    exit 1
fi

echo "[OK] RabbitMQ ready."

docker compose exec -T rabbitmq \
    rabbitmqctl set_policy \
    -p / \
    --apply-to quorum_queues \
    license-allocation-retry \
    '^license-vault\.order-paid$' \
    '{"delivery-limit":5,"dead-letter-exchange":"marketplace.events.dead","dead-letter-routing-key":"license-vault.order-paid.dead","dead-letter-strategy":"at-least-once","overflow":"reject-publish"}'

docker compose exec -T rabbitmq \
    rabbitmqctl set_policy \
    -p / \
    --apply-to quorum_queues \
    order-license-events-dlx \
    '^order\.license-events$' \
    '{"delivery-limit":5,"dead-letter-exchange":"marketplace.events.dead","dead-letter-routing-key":"order.license-events.dead","dead-letter-strategy":"at-least-once","overflow":"reject-publish"}'


docker compose exec -T rabbitmq \
    rabbitmqctl set_policy \
    -p / \
    --apply-to quorum_queues \
    order-payment-events-dlx \
    '^order\.payment-events$' \
    '{"delivery-limit":5,"dead-letter-exchange":"marketplace.events.dead","dead-letter-routing-key":"order.payment-events.dead","dead-letter-strategy":"at-least-once","overflow":"reject-publish"}'

docker compose exec -T rabbitmq \
    rabbitmqctl set_policy \
    -p / \
    --apply-to quorum_queues \
    notification-events-dlx \
    '^notification\.order-events$' \
    '{"delivery-limit":5,"dead-letter-exchange":"marketplace.events.dead","dead-letter-routing-key":"notification.order-events.dead","dead-letter-strategy":"at-least-once","overflow":"reject-publish"}'

echo "[OK] Retry policies configured."

docker compose exec -T rabbitmq \
    rabbitmqctl list_policies -p /

echo "[OK] RabbitMQ provisioning completed."
