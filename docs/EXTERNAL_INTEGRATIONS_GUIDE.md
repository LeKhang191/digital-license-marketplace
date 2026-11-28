# Hướng dẫn Cấu hình Tích hợp Ngoài (Google OAuth + Keycloak, Jira Service Management, VNPay, SMTP)

> Hướng dẫn thực hành trên hạ tầng **VPS trực tiếp bằng Nginx + Let's Encrypt (không dùng Cloudflare)**, gắn với 2 tài nguyên Đức Anh đã cấp quyền:
> 1. **Google Cloud Project**: `Digital License Marketplace` (Project ID: `digital-license-marketplace`, Project Number: `958142757296`) — tích hợp đăng nhập Google qua Keycloak.
> 2. **Atlassian Customer Service Management (Jira)**: Space `Marketplace Support` (Project Key: `DLM`) — quản lý & đồng bộ 2 chiều phiếu hỗ trợ.

---

## 0. Điều kiện tiên quyết về Domain & HTTPS (Nginx trực tiếp)

Trước khi cấu hình các dịch vụ ngoài, cần chuẩn bị 2 domain (hoặc 2 subdomain) trỏ DNS `A` (và `AAAA` nếu có IPv6) trực tiếp về IP public của VPS:

| Biến | Ví dụ | Dùng cho |
|---|---|---|
| `APP_DOMAIN` | `marketplace.example.com` | Frontend SPA, API `/api/*`, VNPay IPN, Jira Webhook |
| `AUTH_DOMAIN` | `auth.example.com` | Keycloak OIDC & Google OAuth Broker (chặn `/admin` từ Internet) |

Trình tự dựng HTTPS lần đầu trên VPS:
1. Sao chép các biến từ `infra/nginx/vps/env.vps.example` vào `.env` (điền domain thật và email Certbot; không commit `.env`).
2. Build Frontend với `VITE_API_URL=/api` và `VITE_KEYCLOAK_URL=https://<AUTH_DOMAIN>`:
   ```bash
   cd frontend
   cat > .env.production <<EOF
   VITE_API_URL=/api
   VITE_KEYCLOAK_URL=https://<AUTH_DOMAIN>
   VITE_KEYCLOAK_REALM=marketplace
   VITE_KEYCLOAK_CLIENT_ID=marketplace-web
   EOF
   npm ci && npm run build
   ```
3. Khởi động stack & xin chứng chỉ Let's Encrypt:
   ```bash
   bash scripts/init-vps-tls.sh
   ```
4. Cập nhật lại biến `KEYCLOAK_PUBLIC_URL`, `FRONTEND_PUBLIC_URL`, `VNPAY_RETURN_URL` trong `.env` và recreate các service đọc các biến này:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.vps.yml up -d
   ```

---

## 1. Tích hợp Đăng nhập Google với Keycloak (Google Cloud Console)

### 1.1. Dự án Google Cloud bạn đã được mời
- **Project name:** `Digital License Marketplace`
- **Project ID:** `digital-license-marketplace`
- **Project number:** `958142757296`

### 1.2. Cách hoạt động
- Người dùng **không** đăng nhập thẳng từ React vào Google.
- React (`marketplace-web`) chuyển hướng sang Keycloak (`https://<AUTH_DOMAIN>/realms/marketplace/...`).
- Keycloak đóng vai trò **OAuth2 Client** nói chuyện với Google Cloud bằng `Client ID` và `Client Secret`.
- Khi Google xác thực xong, Google gọi ngược về **Redirect URI của Keycloak**, Keycloak cấp JWT chuẩn của realm `marketplace` (tự gán role `customer`) trả về cho React.

### 1.3. Các bước cấu hình trên Google Cloud Console
1. Mở [Google Cloud Console](https://console.cloud.google.com/) và chọn đúng project **`Digital License Marketplace`** (`digital-license-marketplace`).
2. Vào menu **APIs & Services → OAuth consent screen** (hoặc **Google Auth Platform → Branding / Audience**):
   - Kiểm tra trạng thái Publishing status: nếu đang ở **Testing**, chỉ những email nằm trong danh sách **Test users** mới đăng nhập Google được (thêm email demo/thành viên nhóm vào đây).
   - Kiểm tra **Authorized domains** đã chứa domain gốc của `AUTH_DOMAIN` (ví dụ `example.com`).
3. Vào **APIs & Services → Credentials → OAuth 2.0 Client IDs**, mở OAuth Web Client của đồ án (hoặc tạo mới loại *Web application* nếu chưa có):
   - **Authorized JavaScript origins**:
     ```text
     https://<AUTH_DOMAIN>
     ```
     *(Có thể giữ `http://localhost:8081` nếu vẫn cần test local).*
   - **Authorized redirect URIs**:
     ```text
     https://<AUTH_DOMAIN>/realms/marketplace/broker/google/endpoint
     ```
     *(Gỡ bỏ các URL tạm của Codespaces/Cloudflare cũ không còn dùng; giữ `http://localhost:8081/realms/marketplace/broker/google/endpoint` nếu vẫn test local).*
   - Lưu lại **Client ID** và **Client Secret** (không gửi vào nhóm chat công khai, không commit vào Git).

### 1.4. Các bước cấu hình trên Keycloak (VPS)
1. Chạy script cấu hình auth tương tác để nhập Google Client ID & Secret (và tùy chọn SMTP cho Keycloak):
   ```bash
   bash scripts/configure-auth.sh
   ```
   Script sẽ tạo/cập nhật Identity Provider alias `google` trong realm `marketplace` với:
   - `providerId: "google"`, `defaultScope: "openid email profile"`
   - `trustEmail: false` (tránh chiếm đoạt tài khoản qua email chưa xác minh), `storeToken: false`, `syncMode: "IMPORT"`
   - `firstBrokerLoginFlowAlias: "first broker login"`
2. Chạy script cập nhật Redirect URIs & Web Origins của `marketplace-web` cho domain VPS:
   ```bash
   bash scripts/configure-vps-auth.sh
   ```
   Script sẽ in ra đúng `Google Redirect URI` để đối chiếu lại với Google Cloud Console.

### 1.5. Kiểm thử & Khắc phục lỗi thường gặp

| Hiện tượng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| Google báo `Error 400: redirect_uri_mismatch` | Redirect URI gửi từ Keycloak khác với URI khai báo trên Google Cloud (sai HTTP/HTTPS, sai domain, hoặc dư dấu `/`). | Đối chiếu `KC_HOSTNAME` trong `.env` (`https://<AUTH_DOMAIN>`), kiểm tra Nginx đang gửi `X-Forwarded-Proto https` và cập nhật đúng chuỗi `/realms/marketplace/broker/google/endpoint` trên Google Cloud. |
| Đăng nhập xong nhưng mọi API trả `401 Unauthorized` | `Keycloak__Issuer` trong các backend container khác với `iss` trong JWT (ví dụ backend vẫn giữ `http://localhost:8081`). | Kiểm tra `KEYCLOAK_PUBLIC_URL=https://<AUTH_DOMAIN>` trong `.env` và `docker compose up -d` để nạp lại env cho `catalog`, `order`, `payment`, `license-vault`, `support`. |
| Mở `https://<AUTH_DOMAIN>/admin` bị `403 Forbidden` | **Đây là hành vi bảo mật đúng thiết kế** của `infra/nginx/vps/site-https.conf.template`. | Khi cần vào Admin Console, tạo SSH tunnel: `ssh -L 8081:127.0.0.1:8081 user@vps` rồi mở `http://localhost:8081/admin` trên máy cá nhân. |
| Lỗi `502 Bad Gateway` khi callback từ Google | Header/Cookie OAuth lớn vượt buffer mặc định của Nginx. | Đã xử lý sẵn trong `site-https.conf.template` bằng `proxy_buffer_size 128k; proxy_buffers 4 256k;`. |

---

## 2. Tích hợp Phiếu Hỗ trợ — Atlassian Jira Service Management

### 2.1. Không gian Atlassian bạn đã được mời
- **Sản phẩm:** Atlassian Customer Service Management (Jira Service Management)
- **Space / Project:** `Marketplace Support` (Project Key: `DLM`, các ticket mẫu: `DLM-1` đến `DLM-5`)
- **Quy ước tiêu đề do backend sinh:** `[DLM:<ticket-guid>] <CATEGORY>` (ví dụ `[DLM:8af80b2f-8963-487c-ae7d-28d45cc4e...]`). Marker này được `JiraClient.Matches()` dùng để xác minh khi thực hiện `reconcile` (đối soát thủ công nếu lần tạo đầu bị đứt mạng).

### 2.2. Hai chiều giao tiếp giữa DLM (`support-service`) và Jira

1. **Chiều đi (DLM → Jira REST API — `JiraClient.cs`):**
   - Khi `license_coordinator` hoặc `transaction_auditor` bấm Escalate ticket đang `IN_PROGRESS`, `SupportExternalController` lưu `ExternalSupportLink (PENDING)` trước rồi gọi `POST https://<site>.atlassian.net/rest/servicedeskapi/request` bằng **Basic Auth** (`JIRA_EMAIL:JIRA_API_TOKEN`).
   - Lưu ý điều kiện kiểm tra nghiêm ngặt trong `JiraClient.Ready`:
     - `JIRA_BASE_URL` **bắt buộc** dạng `https://<your-site>.atlassian.net` (HTTPS, đuôi `.atlassian.net`, không có path phụ, không có query/fragment).
     - `JIRA_SERVICE_DESK_ID` và `JIRA_REQUEST_TYPE_ID` phải là số nguyên dương.
2. **Chiều về (Jira Automation → DLM Webhook — `JiraWebhookController.cs`):**
   - Jira Automation gửi `POST https://<APP_DOMAIN>/api/support/webhooks/jira` kèm:
     - Header: `X-DLM-Webhook-Secret: <secret >= 32 ký tự>`
     - Header: `Content-Type: application/json`
     - Body: `{"issueId":"{{issue.id}}"}`
   - Nginx giới hạn phương thức `POST`, tối đa `16KB`, rate-limit `5 req/s`, tắt access log.
   - `JiraWebhookController` kiểm tra `X-DLM-Webhook-Secret` bằng SHA-256 constant-time, xác nhận `issueId` chỉ gồm chữ số và tồn tại bản ghi `LINKED`, sau đó **gọi ngược Jira REST API (`RefreshLink`)** để đồng bộ trạng thái và các bình luận công khai (`public=true&internal=false`) — không tin dữ liệu trong payload webhook.

### 2.3. Các bước cấu hình chi tiết

#### Bước 1: Tạo API Token cá nhân (không dùng mật khẩu)
1. Truy cập [Atlassian Account Security → API tokens](https://id.atlassian.com/manage-profile/security/api-tokens) bằng chính tài khoản đã được Đức Anh mời vào `Marketplace Support`.
2. Chọn **Create API token**, đặt nhãn (ví dụ `dlm-vps-support`), sao chép token.

#### Bước 2: Tra cứu `JIRA_SERVICE_DESK_ID` và `JIRA_REQUEST_TYPE_ID`
Chạy lệnh sau trên máy có quyền gọi HTTPS (thay `<site>`, `<your-email>`, `<your-token>`):

```bash
# 1. Lấy danh sách Service Desk (tìm projectKey = "DLM" để lấy "id")
curl -sS -u "<your-email>:<your-token>" \
  -H "Accept: application/json" \
  "https://<site>.atlassian.net/rest/servicedeskapi/servicedesk" | python3 -m json.tool

# 2. Lấy danh sách Request Type của Service Desk đó (thay <SD_ID>)
curl -sS -u "<your-email>:<your-token>" \
  -H "Accept: application/json" \
  "https://<site>.atlassian.net/rest/servicedeskapi/servicedesk/<SD_ID>/requesttype" | python3 -m json.tool
```

#### Bước 3: Sinh Webhook Secret và điền vào `.env` trên VPS
Sinh secret ngẫu nhiên tối thiểu 32 ký tự:
```bash
openssl rand -hex 32
```
Cập nhật vào `.env` (không commit):
```dotenv
JIRA_ENABLED=true
JIRA_BASE_URL=https://<site>.atlassian.net
JIRA_EMAIL=<email-chủ-API-token>
JIRA_API_TOKEN=<api-token-vừa-tạo>
JIRA_SERVICE_DESK_ID=<id-số-nguyên>
JIRA_REQUEST_TYPE_ID=<id-số-nguyên>
JIRA_WEBHOOK_SECRET=<chuỗi-64-ký-tự-hex-vừa-sinh>
```
Sau đó khởi động lại riêng service `support`:
```bash
docker compose -f docker-compose.yml -f docker-compose.vps.yml up -d --no-deps support
```

#### Bước 4: Cấu hình Jira Automation Rule trên `Marketplace Support`
1. Trong giao diện Atlassian → Space **Marketplace Support** → **Project settings / Space settings → Automation → Create rule**.
2. Tạo rule cho sự kiện **Issue transitioned** (và tạo rule thứ hai hoặc dùng chung trigger **Issue commented** cho bình luận công khai):
   - **Condition (khuyến nghị):** Issue fields condition → `Summary` contains `[DLM:` (chỉ bắn webhook cho ticket sinh từ DLM).
   - **Action:** **Send web request**:
     - **Web request URL:** `https://<APP_DOMAIN>/api/support/webhooks/jira`
     - **HTTP method:** `POST`
     - **Web request body:** `Custom data`
       ```json
       {"issueId":"{{issue.id}}"}
       ```
     - **Headers:**
       - `Content-Type`: `application/json`
       - `X-DLM-Webhook-Secret`: `<giá-trị-khớp-JIRA_WEBHOOK_SECRET>` (đánh dấu *Hidden* để ẩn trong log Jira).
3. Lưu & bật Rule (tắt hoặc sửa URL của các rule cũ còn trỏ về Codespaces/laptop/Cloudflare).

#### Bước 5: Kiểm thử 2 chiều
1. Kiểm thử chiều DLM → Jira:
   ```bash
   python3 -u scripts/test-jira-integration.py 2>&1 | tee dlm-jira-test.txt
   ```
2. Kiểm thử bảo mật chiều Webhook (phải trả `401` khi sai secret):
   ```bash
   curl -i -X POST "https://<APP_DOMAIN>/api/support/webhooks/jira" \
     -H "Content-Type: application/json" \
     -H "X-DLM-Webhook-Secret: wrong-secret-value-for-negative-test" \
     -d '{"issueId":"10001"}'
   ```
3. Kiểm thử E2E thật: đổi trạng thái hoặc thêm **Public comment** (Reply to customer) trên ticket `DLM-*` vừa tạo trong Jira -> kiểm tra API `GET /api/support/tickets/{id}/external` thấy `externalStatus` và `comments` tự động cập nhật.

---

## 3. Tích hợp VNPay Sandbox (không dùng Cloudflare)

1. Điền biến vào `.env`:
   ```dotenv
   VNPAY_TMN_CODE=<mã-merchant-sandbox>
   VNPAY_HASH_SECRET=<hash-secret-sandbox>
   VNPAY_RETURN_URL=https://<APP_DOMAIN>/payment/result
   ```
2. Đăng ký (hoặc nhờ chủ merchant cập nhật) **IPN URL** trên cổng VNPay Sandbox:
   ```text
   https://<APP_DOMAIN>/api/orders/vnpay-ipn
   ```
3. Nginx (`site-https.conf.template`) đã cấu hình riêng cho `location = /api/orders/vnpay-ipn`:
   - Chỉ nhận `GET` (phương thức khác trả `405`).
   - Giữ nguyên toàn bộ query string `$is_args$args` để không làm sai chữ ký HMAC-SHA512.
   - Tắt `access_log` để không ghi `vnp_SecureHash` ra file log.
4. Chạy kiểm thử trên Sandbox:
   ```bash
   export SANDBOX_PUBLIC_IPN="https://<APP_DOMAIN>/api/orders/vnpay-ipn"
   python3 -u scripts/test-vnpay-sandbox.py 2>&1 | tee dlm-vnpay-sandbox-result.txt
   ```

---

## 4. Tích hợp Gmail SMTP (2 nơi cấu hình độc lập)

Hệ thống có **2 nơi gửi email tách biệt** (không tự đồng bộ với nhau):

1. **Notification Service** (gửi email đơn hoàn tất & hoàn tiền):
   - Cấu hình trong `.env` bằng `SMTP_ENABLED=true`, `SMTP_HOST=smtp.gmail.com`, `SMTP_PORT=587`, `SMTP_ENABLE_SSL=true`, `SMTP_USERNAME`, `SMTP_PASSWORD` (dùng **Google App Password 16 ký tự** của mailbox dự án, không dùng mật khẩu đăng nhập), `SMTP_FROM_EMAIL`, `SMTP_FROM_NAME`.
   - Áp dụng: `docker compose -f docker-compose.yml -f docker-compose.vps.yml up -d --no-deps notification`.
2. **Keycloak** (gửi email xác minh tài khoản / khôi phục mật khẩu nếu bật):
   - Cấu hình riêng bằng `bash scripts/configure-auth.sh` (chọn `y` ở mục `Configure Gmail SMTP?`).
   - Kiểm thử gửi email thành công trước khi bật bắt buộc xác minh email (`verifyEmail`).
