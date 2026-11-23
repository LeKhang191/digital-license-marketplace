-- MỘT LẦN: đưa order_db cũ (tạo bởi EnsureCreated trước khi có cột VNPay) về đúng model hiện tại.
-- Không mất dữ liệu, chạy lại nhiều lần vẫn an toàn (idempotent).
-- Sẽ được thay thế bởi EF Core migrations (xem README, mục "Schema").
--
--   docker compose exec -T postgres psql -U app -d order_db -v ON_ERROR_STOP=1 \
--     < infra/postgres/repair-order-db-schema.sql
--
-- Kiểu cột khớp OrderDbContext: MaxLength -> varchar(n), DateTime? -> timestamptz NULL.
BEGIN;

ALTER TABLE orders ADD COLUMN IF NOT EXISTS "PaymentRef"         varchar(64);
ALTER TABLE orders ADD COLUMN IF NOT EXISTS "VnpTransactionNo"   varchar(32);
ALTER TABLE orders ADD COLUMN IF NOT EXISTS "VnpPayDate"         varchar(14);
ALTER TABLE orders ADD COLUMN IF NOT EXISTS "PaidAtUtc"          timestamptz;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS "PaidEventSentAtUtc" timestamptz;

-- Tên index theo quy ước EF (IX_<bảng>_<cột>) để migration sau này không tạo trùng.
CREATE INDEX IF NOT EXISTS "IX_orders_PaymentRef" ON orders ("PaymentRef");

COMMIT;
