# Hướng dẫn Triển khai & Nhận Partner Webhook trên `khenserver` (Homeserver Arch Linux)

> Tài liệu bổ sung cho [`docs/INFRASTRUCTURE_HANDOVER.md`](INFRASTRUCTURE_HANDOVER.md), tối ưu riêng cho máy chủ **`khenserver`** (`HP EliteDesk 800 G3 DM`, Arch Linux) — **không dùng Cloudflare Tunnel**.

---

## 1. Đánh giá năng lực phần cứng `khenserver`

| Tài nguyên | Hiện trạng `khenserver` | Nhu cầu dự kiến của DLM (full stack) | Đánh giá & Khuyến nghị |
|---|---|---|---|
| **CPU** | Intel Core i5-7500T (4 cores @ 3.30 GHz) | ~0.5–1.5 cores lúc tải bình thường, cao hơn khi `docker compose build` | **Đủ tốt** (build từng service khi cần thay vì rebuild tất cả cùng lúc). |
| **RAM** | 16 GiB (đang dùng ~2.6 GiB cho ChitNChat, Nextcloud, Pi-hole, Dockge → **còn ~12 GiB trống**) + 4 GiB zram | ~2.5–3.5 GiB (Keycloak ~700MB, Postgres ~300MB, RabbitMQ ~300MB, 6 .NET services + Gateway ~1.2GB, Nginx ~30MB) | **Rất dư dả** (vẫn còn > 8 GiB trống sau khi chạy DLM). |
| **Ổ `/` (`nvme0n1p2`)** | 46 GiB — **chỉ còn 27 GiB trống** | Docker image + build layer `.NET 10 SDK` + `postgres`/`keycloak` có thể tốn 6–12 GiB | **CẢNH BÁO QUAN TRỌNG:** Không lưu mã nguồn hay backup trên `/`. Dọn build cache định kỳ (`docker builder prune`) hoặc cân nhắc chuyển Docker `data-root` sang `/home/docker` hoặc `/mnt/storage/docker`. |
| **Ổ `/home` (`nvme0n1p3`)** | 421 GiB NVMe — **còn 397 GiB trống** | Clone repo + build `frontend/dist` (~1–2 GiB) | **Nơi đặt repo chính:** `/home/ldkhen/digital-license-marketplace`. |
| **Ổ `/mnt/storage` (`sda1`)** | 916 GiB HDD — **còn 870 GiB trống** | Backup `pg_dump`, backup volume Keycloak, log test | **Nơi đặt backup:** `/mnt/storage/dlm-backups/`. |
| **Mạng** | `wlan0` (`192.168.1.9/24` + IPv6 `2001:ee0:4f8e:eca0::/64`), `eno1` đang `DOWN`, **`tailscale0` (`100.104.128.121`)** | Cần mạng ổn định cho RabbitMQ, VNPay IPN, Jira Webhook, Partner Webhook | Khuyến nghị **cắm dây LAN `eno1`** để tránh rớt Wi-Fi khi demo; tận dụng Tailscale cho SSH/quản trị nội bộ (Keycloak Admin `127.0.0.1:8081`). |

---

## 2. Sống chung an toàn với các container đang chạy trên `khenserver`

Hiện `khenserver` đang chạy các stack:
- **ChitNChat:** `discord_nginx`, `discord_server_1`, `discord_server_2`, `chitnchat_notification_service`, `discord_mongo`, `discord_redis`
- **Homelab:** `khen-homelab-nextcloud-1`, `khen-homelab-pihole-1`, `khen-homelab-redis-1`, `khen-homelab-db-1`, `khen-homelab-cron-1`, `dockge-dockge-1`

**Bắt buộc kiểm tra cổng trước khi bật DLM Nginx:**
```bash
ss -tulpn | grep -E ':(80|443|8000|8081|15672)\b'
```

- **Trường hợp 1 — Cổng `80`/`443` chưa bị chiếm (hoặc bạn định tuyến qua một Reverse Proxy chung):**
  - Chạy thẳng `docker-compose.vps.yml` mặc định (`80:80`, `443:443`).
- **Trường hợp 2 — `discord_nginx` (ChitNChat) đã chiếm cổng `80`/`443`:**
  - **Không tắt hoặc sửa DNS của ChitNChat.**
  - Đặt trong `.env` của DLM:
    ```dotenv
    NGINX_HTTP_BIND=127.0.0.1:8088
    NGINX_HTTPS_BIND=127.0.0.1:8443
    ```
  - Sau đó thêm `server {}` block cho `APP_DOMAIN` và `AUTH_DOMAIN` trên reverse proxy mặt tiền (hoặc dùng **Tailscale Funnel** / router port-forwarding trực tiếp) để chuyển lưu lượng vào DLM Nginx mà không đụng ChitNChat!

---

## 3. Hai phương án đưa `khenserver` ra Internet (không dùng Cloudflare)

1. **Phương án 1 — Domain + DNS `A`/`AAAA` trực tiếp + Port-Forwarding Router + Let's Encrypt:**
   - Mở port-forwarding `80/tcp` và `443/tcp` trên router nhà về IP tĩnh LAN của `khenserver` (`192.168.1.9` — nên gán DHCP reservation theo MAC hoặc cắm `eno1`).
   - Trỏ bản ghi DNS `A` (IPv4 public, dùng DDNS nếu IP động) và/hoặc `AAAA` (IPv6 public của `khenserver`) cho `APP_DOMAIN` và `AUTH_DOMAIN`.
   - Chạy `bash scripts/init-vps-tls.sh` để Certbot cấp chứng chỉ HTTPS tự động.
2. **Phương án 2 — Tailscale Funnel (tận dụng `tailscale0` đã cài sẵn trên `khenserver`):**
   - Dùng nhanh cho môi trường demo/webhook mà không cần mở port router hoặc khi ISP cấp CGNAT:
     ```bash
     tailscale funnel --bg --https=443 http://127.0.0.1:8088
     ```
   - Tailscale cấp sẵn hostname HTTPS hợp lệ (`https://khenserver.<tailnet>.ts.net`) nhận được VNPay IPN, Jira Webhook và Partner Webhook. *(Lưu ý: cần cấu hình `APP_DOMAIN`/`AUTH_DOMAIN` khớp hostname thực tế).*

---

## 4. Dùng `khenserver` làm máy chủ nhận Partner Outbound Webhook

Khi triển khai **Partner API (`feat/partner-api`)**, DLM tự động đẩy webhook (`partner.order.completed`, `partner.order.refunded`) có chữ ký **HMAC-SHA256** sang server đối tác (ví dụ chạy trực tiếp trên `khenserver`):

1. **Chạy mẫu Webhook Receiver trên `khenserver`:**
   ```bash
   python3 -u scripts/partner-webhook-receiver.py \
     --host 0.0.0.0 \
     --port 9090 \
     --secret "<chuỗi-secret-tối-thiểu-32-ký-tự>"
   ```
   - Receiver kiểm tra chữ ký `X-DLM-Signature: sha256=<hex>` trên chuỗi `<X-DLM-Timestamp>.<raw_json_body>` và kiểm tra độ trễ timestamp trong vòng 300 giây (chống replay attack).
   - **Không có plaintext license key trong payload webhook** — sau khi nhận sự kiện `partner.order.completed`, server đối tác gọi `POST /api/partner/v1/orders/{orderId}/licenses/claim` (kèm Bearer JWT của Partner) để giải mã & nhận key an toàn.
2. **Nếu DLM và Partner Receiver giao tiếp qua mạng LAN/Tailscale (`100.104.128.121` hoặc `192.168.1.9`) trong môi trường demo:**
   - Mặc định `order-service` chặn IP nội bộ (SSRF protection) và yêu cầu `https://`.
   - Chỉ khi test nội bộ/Tailscale mới bật trong `.env`:
     ```dotenv
     PARTNER_WEBHOOK_ALLOW_PRIVATE_NETWORK=true
     PARTNER_WEBHOOK_ALLOW_INSECURE_HTTP=true   # chỉ bật nếu chưa gắn HTTPS cho receiver
     ```
   - Trên môi trường public thực tế, giữ cả hai biến là `false` và dùng URL `https://...` cho Partner Receiver.

---

## 5. Backup định kỳ lên `/mnt/storage`

Không lưu backup trên `/` (27 GiB trống). Tạo thư mục `/mnt/storage/dlm-backups/` và sao lưu đồng bộ PostgreSQL + ghi nhớ bảo quản riêng `LICENSE_ENCRYPTION_KEY`, `LICENSE_FINGERPRINT_KEY`, `PAYMENT_INTERNAL_KEY` (không commit):

```bash
mkdir -p /mnt/storage/dlm-backups
chmod 700 /mnt/storage/dlm-backups
STAMP=$(date +%Y%m%d-%H%M%S)
for db in catalog_db order_db license_db payment_db support_db notification_db; do
  docker compose exec -T postgres pg_dump -U app -Fc "$db" \
    > "/mnt/storage/dlm-backups/${db}-${STAMP}.dump"
done
```
