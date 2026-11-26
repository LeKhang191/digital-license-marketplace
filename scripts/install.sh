
#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/.."

echo "================================="
echo " Digital License Marketplace"
echo " One-click Backend Installer"
echo "================================="

# Kiểm tra Docker
if ! command -v docker >/dev/null 2>&1; then
    echo "ERROR: Docker is not installed."
    exit 1
fi

# Kiểm tra Docker Compose
if ! docker compose version >/dev/null 2>&1; then
    echo "ERROR: Docker Compose is not available."
    exit 1
fi

# Kiểm tra OpenSSL
if ! command -v openssl >/dev/null 2>&1; then
    echo "ERROR: OpenSSL is required."
    exit 1
fi

# Tạo .env nếu chưa tồn tại
if [ ! -f .env ]; then
    echo "Creating .env..."

    umask 077

    cat > .env <<EOF
DB_PASSWORD=$(openssl rand -hex 20)
RABBITMQ_USER=marketplace
RABBITMQ_PASSWORD=$(openssl rand -hex 20)
KC_PASSWORD=$(openssl rand -hex 20)
KEYCLOAK_VERSION=26.8.2
EOF

    echo ".env created successfully."
else
    echo ".env already exists. Keeping existing configuration."
fi


# Initialize License Vault secrets
umask 077

if ! grep -q '^LICENSE_ENCRYPTION_KEY=' .env; then
    printf '\nLICENSE_ENCRYPTION_KEY=%s\n' \
        "$(openssl rand -base64 32)" >> .env
fi

if ! grep -q '^LICENSE_FINGERPRINT_KEY=' .env; then
    printf '\nLICENSE_FINGERPRINT_KEY=%s\n' \
        "$(openssl rand -base64 32)" >> .env
fi


# Initialize internal service authentication
if ! grep -q '^PAYMENT_INTERNAL_KEY=' .env; then
    printf '\nPAYMENT_INTERNAL_KEY=%s\n' \
        "$(openssl rand -hex 32)" >> .env
fi

chmod 600 .env

echo "License Vault secrets are configured."


# Kiểm tra Docker Compose
echo "Validating Docker Compose..."
docker compose config --quiet


# Khởi động infrastructure
echo "[1/4] Starting infrastructure..."

docker compose up -d --wait postgres
docker compose up -d rabbitmq keycloak

# Configure RabbitMQ
bash scripts/configure-rabbitmq.sh

# Dừng backend nếu đang chạy
echo "[2/4] Stopping application services..."

docker compose stop gateway order catalog license-vault payment notification

# Thực hiện database migration
echo "[3/4] Applying database migrations..."

bash scripts/migrate.sh

# Build và khởi động backend
echo "[4/4] Building and starting backend..."

docker compose up -d --build --wait --wait-timeout 120 notification

docker compose up -d --build catalog order license-vault payment gateway

# Hiển thị trạng thái
echo ""
echo "================================="
echo " Docker Containers"
echo "================================="

docker compose ps

echo ""
echo "Installation command completed."
echo "API Gateway: http://localhost:8000"
echo "Catalog API: http://localhost:8000/api/catalog/products"
echo ""
echo "Check logs: docker compose logs -f"
