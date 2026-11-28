# Digital License Marketplace — Development Handover

> Snapshot: 28/11/2026. Tài liệu tiếp quản công việc cho thành viên nhóm và AI coding agent. Đây là ghi nhận tiến độ từ các phiên phát triển, không phải chứng nhận toàn bộ code mới đã PASS. Luôn đối chiếu HEAD và kết quả test thực tế trước khi sửa.

## 1. Mục tiêu và nguyên tắc

DLM là đồ án SOA bán license game: quản lý catalog, nhập kho đa nguồn, bán hàng, thanh toán, cấp phát license, hỗ trợ khách hàng và hoàn tiền. Môi trường demo chỉ dùng key giả lập, không kích hoạt game thật.

- Repo: `digital-license-marketplace`; nhánh ứng dụng: `feat/customer-license-delivery` (kiểm tra lại bằng `git branch --show-current`).
- Stack hiện tại: .NET 10, React/TypeScript/Vite, PostgreSQL 16, RabbitMQ, Keycloak, Ocelot, Docker Compose; Nginx/Cloudflare cho triển khai.
- KISS: không tự thêm thư viện, microservice, hạ tầng, branch hoặc đổi kiến trúc khi chưa được duyệt.
- Gom các thay đổi liên quan, kiểm tra `git diff --check`, build và test có mục tiêu. Không tự commit/push, không force push, không reset DB.
- Không đọc/in/gửi secret không cần thiết; không đưa `.env`, token, key thật, backup hay log nhạy cảm vào Git. Không đổi khóa mã hóa/fingerprint khi giữ DB cũ.
- README tổng thể viết cuối. Kết quả test lưu `.txt` tại project (không commit log).

## 2. Kiến trúc và luồng nghiệp vụ

| Thành phần | Nhiệm vụ |
|---|---|
| Catalog | Sản phẩm, metadata Steam, giá bán, hiển thị cửa hàng |
| Order | Đơn hàng, trạng thái, hoàn tiền |
| Payment | Wallet, VNPay Sandbox |
| License Vault | Nhập kho, chống trùng, mã hóa, cấp phát, nhà cung cấp |
| Support | Ticket khách hàng, Jira |
| Notification | Thông báo/email |
| Gateway | Định tuyến API và phân quyền |
| Keycloak | Đăng nhập, role, Google OAuth |
| PostgreSQL / RabbitMQ | Lưu dữ liệu / xử lý sự kiện bất đồng bộ |

Luồng bán: Customer chọn game → tạo đơn → thanh toán → sự kiện RabbitMQ → License Vault cấp key → Customer xem/reveal key. Thiếu key hoặc lỗi phải xử lý theo nghiệp vụ hiện có, không tự cấp trùng.

Luồng nhập thủ công: chọn nhà cung cấp + sản phẩm → nhập tay/CSV/TXT → kiểm tra → mã hóa → lô nhập + tồn kho. Luồng nhập API demo: chọn game Kinguin Mock → báo giá → đặt mua → nhận key giả lập → nhập kho demo và ghi lịch sử (cần bật đúng chế độ demo).

Roles: customer, catalog_manager, license_coordinator, transaction_auditor, admin. Staff không được sử dụng chức năng mua hàng của Customer.

## 3. Tiến độ và bằng chứng

Mốc regression trước các patch UI/Supplier gần đây: 302 PASS trên 13/15 nhóm (195 nội bộ + 107 tích hợp), gồm phân quyền, wallet, cấp phát/concurrency, ownership, support, Jira, Kinguin Mock, refund. Hai nhóm VNPay reliability/rollback chưa hoàn tất; không diễn giải mốc 302 là kết quả của HEAD hiện tại.

| Nhóm | Trạng thái |
|---|---|
| Catalog, Steam metadata, dataset game | Đã triển khai local; 59 game nhập thành công từ dataset 60 dòng |
| Wallet, order, cấp phát và refund | Regression cũ PASS |
| Storefront responsive, filter/sort/pagination, quantity | Patch đã triển khai; cần đối chiếu test HEAD |
| Staff navbar theo role, tách kho/nhập kho/nhà cung cấp | Đã phát triển |
| Nhập thủ công và CSV/TXT, preview, tải mẫu, kéo thả | Đã phát triển, người dùng xác nhận UI ổn |
| Lô nhập, giá vốn, lịch sử và nguồn hàng đa nguồn | Đã phát triển; build trước đây PASS, lỗi 502 do deployment dừng được người dùng xác nhận đã khắc phục |
| CRUD nhà cung cấp, kích hoạt/ngừng, selector, lịch sử | Đã có patch; chưa có báo cáo E2E đầy đủ |
| Supplier API framework, credentials mã hóa, mapping theo môi trường | Batch 1 đã có patch; Kinguin Mock hoạt động qua UI, chưa xác nhận toàn bộ test |
| Kinguin danh mục game, tự gợi ý mapping, 3 tab mua/lịch sử/cài đặt | Đã có patch, người dùng đã thấy đơn MOCK_FULFILLED trên UI trước Auto Import |
| Kinguin Mock Auto Import | Patch đã tạo; chưa có log full build/E2E xác nhận |
| Inventory Batch 3 | Patch đã tạo; 14 unit assertions và kiểm tra cú pháp được báo cáo PASS, full build/E2E còn chờ |
| Google OAuth, SMTP | Đã cấu hình local; cần test môi trường đích |
| Jira REST | Regression cũ PASS; webhook public cần triển khai lại |
| Kinguin/Yard Sandbox chính thức | Chờ cấp quyền; chưa xác nhận giao dịch thật |

## 4. Chuỗi patch gần đây (không apply lại nếu đã commit)

1. `dlm-phase1-storefront.patch`
2. `dlm-phase1.1-staff-ui.patch`
3. `dlm-multi-supplier-import.patch`
4. `dlm-license-file-preview.patch`
5. `dlm-license-upload-ui-fix.patch`
6. `dlm-supplier-management.patch`
7. `dlm-supplier-batch1.patch`
8. `dlm-kinguin-mapping-ui-fix.patch`
9. `dlm-kinguin-game-catalog.patch`
10. `dlm-kinguin-workspace-ui.patch`
11. `dlm-kinguin-auto-import.patch`
12. `dlm-license-batch3.patch`

Source ZIP gần nhất đã cung cấp là `digital-license-marketplace-feat-customer-license-delivery (5).zip`, trước Batch 1. Các patch sau ZIP không mặc nhiên có trong HEAD: kiểm tra trạng thái Git và source thực tế, không tái tạo source bằng suy đoán.

## 5. Chi tiết License/Supplier

- Kho: thống kê available/allocated/disabled, tìm kiếm và lọc, xem chi tiết, vô hiệu hóa key chưa cấp; Batch 3 bổ sung ngưỡng tồn kho thấp, hết hàng, nhập nhanh, truy xuất lô/nguồn và lịch sử đổi key.
- Nhập kho: chọn sản phẩm Catalog, nhà cung cấp hoạt động, mã lô, giá vốn; manual/CSV/TXT; preview chỉ kiểm tra file, backend kiểm tra cả DB. Không hiển thị plaintext key trong danh sách quản lý.
- Danh mục nhà cung cấp: thêm/sửa/ngừng/kích hoạt, thông tin liên hệ, số lô/key/giá vốn, lịch sử; bảo toàn tên nguồn tại thời điểm nhập. Không xóa lịch sử.
- Bảng liên quan: `suppliers`, `license_import_batches`, `license_keys`, `supplier_api_configurations`, `supplier_product_mappings`. Kiểm tra migration và schema thực tế trước khi thao tác.
- Kinguin: Mock và Sandbox tách mapping; ba mã `demo-product-001/002/003` giữ tương thích regression, ẩn khỏi luồng demo nhập kho mới; dùng `mock-steam-*` theo game. Giá/tồn kho Mock không phải dữ liệu Kinguin thật.
- Auto Import: chỉ dùng DB demo riêng, `KINGUIN_MODE=Mock` và `KINGUIN_DEMO_AUTO_IMPORT=true` nếu patch đã áp dụng. Lô `MOCK_API`, nguồn Kinguin Mock (DEMO), tỷ giá giả định 30.000 VND/EUR. Đơn cũ không tự nhập lại; Request ID phải chống nhập trùng. Không trộn với hàng thương mại, không bật trên production.
- Batch 3: biên gộp dự kiến từ giá bán hiện tại và giá vốn đã biết, không phải lợi nhuận thực tế. Key chưa có giá vốn không được giả định giá vốn bằng 0.

## 6. Chạy và kiểm tra local

Từ root repo (môi trường đã được cài):

```bash
git status --short
git branch --show-current
docker compose ps
(cd frontend && npm run build)
```

Mở frontend ở terminal riêng:

```bash
cd frontend
npm run dev -- --host 127.0.0.1 --strictPort
```

Frontend `http://localhost:5173`, Gateway `http://localhost:8000`, Keycloak local `http://localhost:8081` (đối chiếu compose). Nếu thay đổi License Vault, build rồi recreate riêng service:

```bash
docker compose build license-vault && docker compose up -d --no-deps license-vault
docker compose ps license-vault
```

Không dừng service trước khi build/backup. Nếu migration thực sự cần thiết: backup đã kiểm tra trước, dùng migrator của repo; nếu thất bại giữ DB, chẩn đoán log và khôi phục service tương thích, không chạy `down -v`. Tránh `docker compose exec -T` đọc stdin của Bash heredoc; dùng script file hoặc `</dev/null` khi không cần stdin.

Test có mục tiêu nếu script tồn tại trong HEAD:

```bash
python3 -u scripts/test-kinguin-auto-import.py 2>&1 | tee dlm-kinguin-auto-import-test.txt
python3 -u scripts/test-inventory-management.py 2>&1 | tee dlm-license-batch3-test.txt
```

Test auto-import tạo dữ liệu demo, chạy trên DB demo; inventory test dự kiến chỉ đọc. Kiểm tra script trước khi chạy. Kiểm thử thêm: nhập file đúng/trùng/sai, nhà cung cấp ngừng hoạt động, mua Mock → kho tăng đúng → lịch sử → Customer wallet mua → kho giảm → reveal; retry không trùng; hết hàng; RBAC. Không chạy lại mọi regression nếu không liên quan, nhưng cần final regression trước merge cuối.

## 7. Phân công tránh conflict

| Phạm vi | Người phụ trách |
|---|---|
| frontend, catalog, license-vault, order | Application (chủ nhánh hiện tại) |
| infra, domain/DNS, tunnel, Nginx, triển khai, backup | Thành viên infrastructure (trừ migration nghiệp vụ) |
| docker-compose.yml, gateway/ocelot.json, .env.example, Keycloak, payment/support/notification | Trao đổi trước khi chỉnh |
| scripts/docs | Chia theo nghiệp vụ; README làm cuối |

Nhánh infra đề xuất `feat/infrastructure-deployment` từ commit bàn giao, chỉ tạo nếu nhóm đồng ý. PR về nhánh ứng dụng, review file dùng chung và test trước merge. Không force push hoặc tự ghi đè cấu hình người khác.

Tài liệu triển khai chi tiết: `docs/INFRASTRUCTURE_HANDOVER.md` nếu đã được đưa vào repo. Atlassian đã mời thành viên theo trao đổi; Google Cloud đang cấp quyền, chưa xác nhận thành công. Không chia sẻ mật khẩu Gmail; ưu tiên cấp quyền phù hợp, token riêng hoặc chủ tài khoản cập nhật redirect. Bạn infra phụ trách VNPay IPN, Jira webhook, SMTP/OAuth trên homeserver. Domain phải thuộc tài khoản Cloudflare phù hợp với tunnel; kiểm tra trước khi triển khai. Chưa coi Kinguin/Yard có Sandbox access khi chưa nhận credentials và test thành công.

## 8. Roadmap tiếp theo (ưu tiên tránh conflict)

1. Chốt Auto Import và Batch 3: xác nhận build, kiểm thử UI/E2E, sửa lỗi trước khi thêm chức năng.
2. Checkout & License Delivery: xác nhận thanh toán/chống double submit, Payment Result với trạng thái rõ ràng, chứng từ giao dịch (không gọi là hóa đơn thuế), xem/in PDF bằng trình duyệt, xác nhận trước reveal và chính sách hoàn tiền. Dùng Wallet để test độc lập VNPay.
3. Hoàn thiện account: xác thực email, Google Login, email thông báo không chứa key plaintext; Forgot/Reset Password cần thống nhất lại trước khi làm (trước đây từng hoãn).
4. Còn thiếu trong Supplier: nhiều Adapter hoạt động thực sự, so sánh báo giá, số dư ảo, mua tự động khi hết kho, khiếu nại nhà cung cấp, lợi nhuận thực tế sau refund. Không coi đây là chức năng đã xong.
5. Partner API: duyệt thiết kế trước; Keycloak/Gateway là vùng dùng chung, làm sau khi thống nhất infra.
6. Merge infra, test VNPay Sandbox/IPN và Jira webhook, security hardening, final regression, README/kiến trúc/demo.

## 9. Prompt khởi đầu cho agent

Đọc file này và `docs/INFRASTRUCTURE_HANDOVER.md` nếu có. Kiểm tra HEAD, git status, code và test hiện tại; báo lại những gì đã xác minh và những gì chỉ là kế hoạch. Ưu tiên chốt Auto Import và Inventory Batch 3 trước Checkout. Không sửa vùng infrastructure dùng chung, không thêm stack, không chạy lệnh phá hủy dữ liệu, không lộ secrets. Đề xuất phạm vi file trước khi sửa. Xuất diff/patch, danh sách test và kết quả thực tế; không tuyên bố PASS khi chưa chạy. Không commit/push nếu chưa được yêu cầu.
