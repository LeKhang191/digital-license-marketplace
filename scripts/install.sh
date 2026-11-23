
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

# Kiểm tra Docker Compose
echo "Validating Docker Compose..."
docker compose config --quiet

# Build và chạy container
echo "Building and starting backend..."
docker compose up -d --build

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
