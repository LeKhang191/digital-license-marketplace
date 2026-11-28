# Digital License Marketplace — Bàn giao Infrastructure

## 1. Phạm vi

Nhánh bàn giao: `feat/customer-license-delivery` (xác nhận commit mới nhất với người phát triển).

- Thành viên infrastructure: homeserver, domain, Cloudflare Tunnel, Nginx, HTTPS, cấu hình tích hợp, callback/webhook, backup và kiểm thử triển khai.
- Thành viên ứng dụng: UI, nghiệp vụ Catalog/Order/Wallet/License/Support, Supplier Management và các patch tiếp theo.
- Không tự đổi stack, schema nghiệp vụ, hay merge đè code của nhau. Thống nhất trước khi sửa file Compose, gateway, cấu hình auth hoặc migration.

## 2. Tình trạng bàn giao

| Thành phần | Tình trạng |
|---|---|
| Docker Compose, PostgreSQL, RabbitMQ, Keycloak, Ocelot | Đã chạy local |
| Catalog, Order, Wallet, License Vault, Support | Đã có; regression trước các patch Supplier đạt 302 PASS |
| Storefront, quản lý nhà cung cấp, nhập file CSV/TXT | Đã phát triển; cần kiểm tra lại trên server |
| Kinguin Mock, mapping, auto-import demo | Có patch; auto-import mới nhất cần xác nhận build/E2E |
| Jira REST | Đã test thành công trước đây; cần cấu hình lại trên server |
| Jira webhook | Có endpoint và proxy mẫu; public callback chưa xác nhận |
| VNPay Sandbox | Có luồng tích hợp; public IPN và E2E còn thiếu |
| Gmail SMTP, Google OAuth | Đã cấu hình local; cần cấu hình lại và test trên hostname mới |
| Steam metadata | Đã chạy local; kiểm tra DNS/egress server |
| IGDB/Twitch | Tùy chọn, không chặn demo |
| Kinguin ESA / Yard | Chờ quyền; không giao dịch tiền thật |
| Named Tunnel | Connector laptop từng chạy HTTP/2 IPv4; chưa có hostname public hoạt động |

## 3. Checklist triển khai theo thứ tự

### A. Chuẩn bị trước khi thay đổi hệ thống

- [ ] Xác nhận branch/commit, quyền GitHub và quyền SSH homeserver.
- [ ] Kiểm tra Docker Engine, Compose, OpenSSL, Python 3, Node/npm (theo yêu cầu frontend), dung lượng, RAM, DNS và HTTPS egress.
- [ ] Xác nhận tài khoản Cloudflare nào có zone domain, quyền DNS/Tunnel và hostname khả dụng **trước khi tạo tunnel**. Không đổi nameserver hay DNS của ChitNChat đang hoạt động.
- [ ] Chốt môi trường demo riêng, domain frontend, domain auth, hostname callback; không bật giao dịch tiền thật.
- [ ] Chốt dùng DB demo mới hay chuyển DB laptop. Ưu tiên DB demo mới nếu không cần giữ lịch sử. Không chạy hai hệ thống DB độc lập dưới cùng hostname.
- [ ] Nếu chuyển dữ liệu, backup PostgreSQL, Keycloak realm/volume và cấu hình bí mật đồng bộ. Bảo quản `LICENSE_ENCRYPTION_KEY`, `LICENSE_FINGERPRINT_KEY` cùng DB license; không tự sinh lại hai khóa khi restore. Giữ nhất quán `PAYMENT_INTERNAL_KEY` giữa các service.

### B. Chạy hệ thống nội bộ

```bash
git clone -b feat/customer-license-delivery https://github.com/LeKhang191/digital-license-marketplace.git
cd digital-license-marketplace
git rev-parse --short HEAD
docker compose version
```

Trên bản cài mới, chạy `bash scripts/install.sh` sau khi kiểm tra điều kiện. Script tự tạo `.env` và các secret nội bộ nếu thiếu, thực hiện migration và build backend; không chạy lại tùy tiện trên server đang phục vụ vì script dừng các service ứng dụng.

```bash
docker compose config --quiet
docker compose ps
bash scripts/configure-auth.sh
```

`configure-auth.sh` cấu hình role, SMTP và Google OAuth qua Keycloak; chỉ bật các lựa chọn đã có credentials. Tạo tài khoản demo bằng `scripts/create-test-users.sh` (đọc script trước khi chạy). Dataset demo: `scripts/recover-demo-data.sh` và các script seed trong `scripts/`; kiểm tra không seed trùng lên DB đã restore.

Frontend: kiểm tra `frontend/.env.example`, cấu hình `VITE_API_URL=/api`, `VITE_KEYCLOAK_URL` theo hostname auth, realm `marketplace`, client `marketplace-web`. Chạy `npm ci` nếu có lockfile tương thích, rồi `npm run build` trong `frontend/`. Dùng Nginx phục vụ `dist/` và proxy `/api` đến gateway; Vite dev server chỉ dành cho test local. Không đưa secret vào biến `VITE_*`.

### C. Nginx và Cloudflare

- [ ] Gateway hiện map host port 8000; Keycloak host port 8081. Xác nhận bind thực tế bằng `docker compose ps` và `ss -lnt`.
- [ ] Tận dụng `docker-compose.auth-proxy.yml`, `infra/nginx/keycloak.conf.template`, `infra/nginx/jira-webhook.conf` làm tham chiếu. Overlay hiện có phục vụ auth và Jira callback trên loopback 8002; **chưa có cấu hình full public frontend/VNPay callback hoàn chỉnh**.
- [ ] Thiết lập Nginx frontend + `/api`, SPA fallback và auth hostname phù hợp; cập nhật issuer, redirect URI, Web Origins, CORS. Không expose cổng DB, RabbitMQ management, Keycloak admin hoặc gateway thô ra Internet.
- [ ] Tạo/reuse Named Tunnel trong account có zone phù hợp. Nếu dùng domain ChitNChat, cần chủ zone cấp quyền hoặc tạo tunnel đúng account; không sử dụng domain tùy ý trong account trống.
- [ ] Chạy connector trên homeserver; nếu chuyển tunnel laptop, dừng connector cũ khi cutover. Giữ token bí mật. Nếu QUIC lỗi, có thể thử HTTP/2 IPv4 như lần local trước.
- [ ] Kiểm tra DNS public, HTTPS, frontend, login, API và route callback. Không áp Cloudflare Access challenge lên callback máy-máy; vẫn bắt buộc xác thực ở backend.

### D. VNPay Sandbox

- [ ] Merchant sandbox hợp lệ, `VNPAY_TMN_CODE`, `VNPAY_HASH_SECRET` và `VNPAY_RETURN_URL` (frontend HTTPS + `/payment/result`).
- [ ] IPN public HTTPS: `/api/orders/vnpay-ipn` qua Nginx đến gateway. Giới hạn chính xác route và method theo controller hiện tại; giữ query string, không log thông tin nhạy cảm. Không public toàn gateway chỉ để nhận IPN.
- [ ] Cập nhật IPN trong merchant portal nếu được hỗ trợ; nếu không có quyền thì nhờ chủ merchant thao tác.
- [ ] Chạy trên Sandbox, không tạo IPN giả để báo đã test dịch vụ thật:

```bash
export SANDBOX_PUBLIC_IPN='https://HOSTNAME-CALLBACK/api/orders/vnpay-ipn'
python3 -u scripts/test-vnpay-sandbox.py 2>&1 | tee dlm-vnpay-sandbox-result.txt
```

Thay HOSTNAME-CALLBACK bằng hostname đã kiểm tra. Kiểm tra thanh toán thành công/thất bại, chữ ký IPN, đơn hàng, cấp license và không xử lý thanh toán lặp. Hai script reliability/rollback synthetic có điều kiện môi trường riêng; không đổi merchant thật sang TEST0001 để chạy chung. Báo riêng phạm vi test còn thiếu.

### E. Jira Service Management

- [ ] Chủ Jira mời bạn vào site/project với quyền tối thiểu phù hợp để quản lý ticket và Automation; không chia sẻ mật khẩu Gmail/Atlassian.
- [ ] Dùng tài khoản tích hợp riêng hoặc API token do tài khoản được cấp quyền tạo. `JIRA_EMAIL` phải tương ứng chủ token. Cấu hình `JIRA_ENABLED`, `JIRA_BASE_URL`, `JIRA_EMAIL`, `JIRA_API_TOKEN`, `JIRA_SERVICE_DESK_ID`, `JIRA_REQUEST_TYPE_ID`, `JIRA_WEBHOOK_SECRET`.
- [ ] Kiểm tra ID service desk và request type trên site thực tế, không mặc định ID cũ dùng được nếu đổi site.
- [ ] Test `python3 -u scripts/test-jira-integration.py` trên project demo (tạo ticket thử).
- [ ] Jira Automation gửi POST đến `https://HOSTNAME-CALLBACK/api/support/webhooks/jira`, header `X-DLM-Webhook-Secret` khớp secret backend. Giữ payload tương thích controller hiện tại. Cập nhật URL cũ nếu còn trỏ Codespaces/laptop.
- [ ] Kiểm tra ticket DLM → Jira, thay đổi trạng thái/comment Jira → DLM, callback sai secret bị từ chối. Không log secret.

### F. Email và Google OAuth

- [ ] Ưu tiên Gmail riêng của đồ án (không phải Gmail cá nhân). Nếu dùng Gmail SMTP, chủ mailbox tạo App Password khi tài khoản đủ điều kiện; không chia sẻ mật khẩu đăng nhập hay mã 2FA. Nếu chưa dùng mailbox chung, chủ tài khoản có thể tự nhập secret trên server.
- [ ] SMTP notification service: `SMTP_ENABLED`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_ENABLE_SSL`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM_EMAIL`, `SMTP_FROM_NAME`.
- [ ] SMTP Keycloak cấu hình riêng qua `scripts/configure-auth.sh`; cấu hình notification service không tự đồng bộ sang Keycloak.
- [ ] Google OAuth: cấp quyền dự án Google Cloud cho thành viên hoặc chủ dự án tự cập nhật OAuth client, redirect URI theo hostname Keycloak (đọc URI do script in ra). Không chia sẻ Google password.
- [ ] Test email thông báo, xác minh email nếu bật, đăng nhập Google. Forgot/Reset Password thuộc roadmap ứng dụng, không tự mở rộng nghiệp vụ khi chưa thống nhất.

### G. Kinguin / Yard / Steam

- [ ] Kinguin giữ `KINGUIN_MODE=Mock` trong demo. Patch auto-import bổ sung `KINGUIN_DEMO_AUTO_IMPORT=true` chỉ cho DB demo riêng, tuyệt đối không bật trên kho thương mại.
- [ ] Kinguin ESA và Yard chưa có quyền đã xác minh; không chặn deployment. Chỉ chuyển Sandbox sau khi người phụ trách tích hợp kiểm tra credentials, quyền và endpoint. Không mua hàng thật.
- [ ] Kiểm tra Steam metadata từ server, DNS và outbound HTTPS. IGDB tùy chọn (`IGDB_CLIENT_ID`, `IGDB_CLIENT_SECRET`), không yêu cầu nếu Steam hoạt động.

## 4. Bàn giao credentials

| Loại | Cách bàn giao |
|---|---|
| GitHub | Mời collaborator, không chia sẻ PAT cá nhân |
| SSH | Public key riêng của thành viên; không chia sẻ private key |
| Cloudflare | Mời đúng account/zone, quyền tối thiểu; token tunnel chỉ đặt trên server |
| Jira | Mời user vào project, token riêng hoặc tài khoản tích hợp |
| Gmail SMTP | Mailbox dự án và App Password riêng; không mượn mật khẩu Gmail cá nhân |
| Google OAuth / IGDB | Quyền project hoặc chủ tài khoản cấu hình secret trực tiếp |
| VNPay | Chủ merchant cấp quyền nếu có; chuyển thông tin sandbox qua kênh bảo mật hoặc tự cấu hình |
| DB, RabbitMQ, Keycloak | Tạo secret riêng khi cài mới; nếu restore phải đồng bộ credentials với dữ liệu |
| License encryption / fingerprint | Bảo quản bí mật cùng backup; không thay khóa tùy ý |

`.env`, token, backup, private key, license thật không commit, không gửi nhóm chat công khai. Chỉ chia sẻ tên biến và trạng thái trong log. Secret đã lộ cần thu hồi/thay thế phù hợp; không tự xoay khóa mã hóa license đang sử dụng.

## 5. Tiêu chí nghiệm thu

- [ ] Server chạy đủ service, khởi động lại có kiểm soát và có backup/restore test trên môi trường riêng.
- [ ] HTTPS frontend/auth hoạt động, login đúng role, API không lỗi 502/CORS/issuer.
- [ ] Customer mua bằng Wallet và nhận license demo; nhập kho file và Kinguin Mock auto-import được kiểm tra nếu patch đã áp dụng.
- [ ] VNPay Sandbox IPN và Jira webhook hoạt động qua hostname cố định.
- [ ] SMTP và Google OAuth kiểm tra bằng tài khoản demo.
- [ ] Không lộ secret, không mở cổng quản trị công khai, không phát sinh giao dịch tiền thật.
- [ ] Gửi lại hostname, commit SHA, danh sách service, checklist PASS/FAIL, log đã lọc secret, hướng dẫn vận hành và rollback. Không gửi `.env` hay backup qua chat.

## 6. File cần đọc

- `docker-compose.yml`, `docker-compose.auth-proxy.yml`, `.env.example`
- `infra/nginx/`, `infra/keycloak/`, `infra/migrations/`
- `scripts/install.sh`, `scripts/configure-auth.sh`, `scripts/configure-roles.sh`, `scripts/migrate.sh`
- `gateway/ocelot.json`, `frontend/.env.example`, `frontend/vite.config.ts`
- `scripts/test-vnpay-sandbox.py`, `scripts/test-jira-integration.py`, `scripts/test-kinguin-mock-workflow.py`
- `scripts/test-kinguin-auto-import.py` (nếu patch mới nhất đã áp dụng)
- `docs/DATABASE_MIGRATIONS.md`

Tài liệu này dựa trên ZIP source gần nhất được cung cấp và các patch sau ZIP. Không phải xác nhận server đã triển khai hoặc các patch mới đã PASS. Domain, quyền tài khoản và commit cuối cần hai thành viên xác nhận trước khi cutover.
