# Digital License Marketplace

Microservices .NET 10 (Catalog, Order, Gateway/Ocelot) + React/Vite, xác thực Keycloak, thanh toán VNPay, sự kiện qua RabbitMQ.

## Chạy local

```bash
bash scripts/install.sh  
cd frontend && cp .env.example .env.local && npm ci && npm run dev
```

`.env` bắt buộc có `DB_PASSWORD`, `KC_PASSWORD`, `RABBITMQ_USER`, `RABBITMQ_PASSWORD`, `KEYCLOAK_VERSION`
(thiếu biến nào, `docker compose` dừng ngay với tên biến đó). Điền thêm `VNPAY_TMN_CODE`, `VNPAY_HASH_SECRET` để bật thanh toán.

## Schema

```bash
docker compose exec -T postgres psql -U app -d order_db -v ON_ERROR_STOP=1 \
  < infra/postgres/repair-order-db-schema.sql
```

Sửa tận gốc: chuyển sang EF Core migrations (làm trên máy có NuGet):

```bash
dotnet tool install --global dotnet-ef
dotnet add services/order-service package Microsoft.EntityFrameworkCore.Design
dotnet ef migrations add InitialCreate --project services/order-service \
  --output-dir Infrastructure/Persistence/Migrations
# Program.cs: thay EnsureCreatedAsync() bằng MigrateAsync()   (làm tương tự cho Catalog)
```

## Kiểm tra

```bash
cd frontend && npm test && npx tsc -b      
bash scripts/test-auth.sh      
bash scripts/test-orders.sh
bash scripts/test-vnpay.sh
```
## Audit và kế hoạch sửa lỗi

Đọc [docs/REFACTOR_AUDIT.md](docs/REFACTOR_AUDIT.md) trước khi cập nhật database hoặc triển khai thanh toán.
