import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { Product } from "../api/types";

type Status = { provider: string; mode: string; credentialSource: string; purchaseEnabled: boolean; demoAutoImport?: boolean };
type Mapping = { productId: string; supplierProductId: string | null; suggestedMockId?: string | null };
type Order = { requestId: string; productId: string; supplierProductId: string; quantity: number; totalCost: number; status: string; imported?: number; createdAtUtc: string };
const path = "/licenses/integrations";
const options = { auth: true, cache: "no-store" as const };
const post = (suffix: string, body?: object) => api<void>(path + suffix, { ...options, method: "POST", ...(body ? { body: JSON.stringify(body) } : {}) });
const message = (e: unknown) => e instanceof Error ? e.message : "Không thể thực hiện yêu cầu.";

export function SupplierIntegration({ products, product, locked, onBusy, onMappingSaved, orderVersion, tab }: {
  products: Product[]; product: string; locked: boolean;
  onBusy: (busy: boolean) => void; onMappingSaved: () => void; orderVersion: number; tab: "purchase" | "history" | "settings";
}) {
  const [status, setStatus] = useState<Status | null>(null);
  const [mappings, setMappings] = useState<Mapping[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [savedCode, setSavedCode] = useState("");
  const [suggestedCode, setSuggestedCode] = useState("");
  const [code, setCode] = useState("");
  const [mappingLoading, setMappingLoading] = useState(false);
  const [key, setKey] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [version, refresh] = useState(0);
  useEffect(() => {
    let live = true;
    Promise.all([api<Status>(path, options), api<Mapping[]>(path + "/mappings", options), api<Order[]>(path + "/orders", options)])
      .then(([s, m, o]) => { if (live) { setStatus(s); setMappings(m); setOrders(o); } })
      .catch(e => { if (live) setError(message(e)); });
    return () => { live = false; };
  }, [version, orderVersion, tab]);
  useEffect(() => {
    let live = true; setCode(""); setSavedCode(""); setSuggestedCode(""); setMappingLoading(!!product);
    if (product) api<Mapping>(path + "/mappings/" + encodeURIComponent(product), options)
      .then(m => { if (live) { setCode(m.supplierProductId ?? m.suggestedMockId ?? ""); setSavedCode(m.supplierProductId ?? ""); setSuggestedCode(m.suggestedMockId ?? ""); } }).catch(e => { if (live) setError(message(e)); }).finally(() => { if (live) setMappingLoading(false); });
    return () => { live = false; };
  }, [product]);
  async function run(action: () => Promise<string>) {
    if (busy || locked) return;
    onBusy(true); setBusy(true); setError(""); setNotice("");
    try { setNotice(await action()); refresh(v => v + 1); } catch (e) { setError(message(e)); }
    finally { setBusy(false); setKey(""); onBusy(false); }
  }
  const enabled = status?.mode === "Mock" || status?.mode === "Sandbox";
  const validCode = /^[a-zA-Z0-9_-]{1,128}$/.test(code);
  if (tab === "purchase") return null;
  return <section className="kinguin-integration">
    {tab === "settings" && <>
    <h2>Cấu hình tích hợp nhà cung cấp</h2>
    <p className="muted">Kinguin là Adapter duy nhất hiện được hỗ trợ. Yard và nhà cung cấp khác chưa kết nối. Production không được hỗ trợ.</p>
    <p>Phương thức: API · Kinguin · {status?.mode ?? "Đang tải"}</p>
    <p className="muted">Môi trường do cấu hình triển khai quyết định. Mock không dùng tiền thật; Sandbox chỉ tra cứu.</p>
    {status?.mode === "Mock" && <p className="muted">{status.demoAutoImport ? "Đã bật nhập kho DEMO tự động." : "Đơn mô phỏng cách ly; chưa bật nhập kho DEMO."}</p>}
    <p>Thông tin xác thực: {status?.credentialSource === "stored" ? "Đã lưu mã hóa" : status?.credentialSource === "environment" ? "Cấu hình môi trường" : status?.credentialSource === "missing" ? "Chưa có API Key" : "Không yêu cầu"}</p>
    {status?.mode === "Sandbox" && <><label>API Key Sandbox<input type="password" autoComplete="new-password" maxLength={2048} value={key} onChange={e => setKey(e.target.value)} disabled={locked || busy}/></label>
      <button className="btn btn-secondary" type="button" disabled={locked || busy || !key.trim()} onClick={() => void run(async () => { await post("/credentials", { apiKey: key }); return "Đã lưu khóa; hãy kiểm tra kết nối."; })}>Lưu / thay thế API Key</button>
      <button className="btn btn-secondary" type="button" disabled={locked || busy || status.credentialSource !== "stored"} onClick={() => { if (window.confirm("Xóa khóa lưu trong DLM? Khóa môi trường (nếu có) sẽ được sử dụng lại.")) void run(async () => { await post("/credentials/clear"); return "Đã xóa khóa lưu trong DLM."; }); }}>Xóa khóa lưu</button></>}
    <h3 id="kinguin-mapping" tabIndex={-1}>Liên kết sản phẩm</h3>
    <p> Sản phẩm DLM: <strong>{products.find(p => p.id === product)?.name ?? "Chưa chọn sản phẩm"}</strong></p>
    <p className="muted">Liên kết hiện tại: {mappingLoading ? "Đang tải..." : savedCode || "Chưa liên kết"}</p>
    {status?.mode === "Mock" ? <label>Mã sản phẩm mô phỏng<select value={code} disabled={locked || busy || mappingLoading || !product} onChange={e => setCode(e.target.value)}>
      <option value="">Chọn mã Mock</option>
      {suggestedCode && <option value={suggestedCode}>{products.find(p => p.id === product)?.name ?? "Game"} — Steam (Mock · {suggestedCode})</option>}
      {code && code !== suggestedCode && !["demo-product-001", "demo-product-002", "demo-product-003"].includes(code) && <option value={code}>{code}</option>}
    </select></label> : <label>Mã sản phẩm Kinguin<input value={code} disabled={locked || busy || mappingLoading || !enabled || !product} onChange={e => setCode(e.target.value)} maxLength={128} placeholder="Supplier Product ID" /></label>}
    {status?.mode === "Mock" && <p className="muted">Mỗi game trong dataset Steam có mã Mock riêng, giá và tồn kho giả lập. Không phải mã hàng thật của Kinguin; chưa xác nhận phiên bản hoặc vùng kích hoạt. Danh mục demo sử dụng mã riêng đúng game; các fixture cũ không còn là lựa chọn mua hàng.</p>}
    <button className="btn btn-secondary" type="button" disabled={locked || busy || mappingLoading || !enabled || !validCode} onClick={() => void run(async () => { const r = await api<{connected:boolean;message:string;simulated:boolean}>(path + "/check?supplierProductId=" + encodeURIComponent(code), options); return (r.simulated ? "Mock: " : "Sandbox: ") + r.message; })}>Kiểm tra tra cứu</button>
    <button className="btn btn-secondary" type="button" disabled={locked || busy || mappingLoading || !enabled || !product || !validCode} onClick={() => void run(async () => { await post("/mappings", { productId: product, supplierProductId: code }); setSavedCode(code); onMappingSaved(); return "Đã lưu liên kết. Mở tab Mua license để xem báo giá mới."; })}>Lưu liên kết</button>
    <p className="muted">Liên kết lưu trong DLM ưu tiên hơn cấu hình cũ. Kiểm tra đúng phiên bản, nền tảng và vùng kích hoạt trước khi lưu.</p>
    {mappings.filter(m => status?.mode !== "Mock" || !m.supplierProductId?.startsWith("demo-product-")).map(m => <p key={m.productId}>{products.find(p => p.id === m.productId)?.name ?? m.productId} → {m.supplierProductId}</p>)}
    </>}
    {error && <p role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    {tab === "history" && <>
    <h3>Lịch sử đơn mua mô phỏng của tôi</h3>
    <button className="btn btn-secondary" type="button" disabled={busy} onClick={() => refresh(v => v + 1)}>Làm mới lịch sử</button>
    <div style={{overflowX:"auto"}}><table className="admin-table"><thead><tr><th>Mã đơn / ngày</th><th>Sản phẩm</th><th>Số lượng</th><th>Chi phí ảo (EUR)</th><th>Trạng thái</th></tr></thead><tbody>{orders.map(o => <tr key={o.requestId}><td>{o.requestId}<br/>{new Date(o.createdAtUtc).toLocaleString("vi-VN")}</td><td>{products.find(p => p.id === o.productId)?.name ?? o.productId}<br/>{o.supplierProductId}</td><td>{o.quantity}</td><td>{o.totalCost.toFixed(2)}</td><td>{o.status === "MOCK_FULFILLED" ? (o.imported ? `Đã nhập ${o.imported} key` : "Hoàn thành (cách ly)") : o.status === "MOCK_PENDING" ? "Đang xử lý" : o.status === "MOCK_IMPORT_BLOCKED" ? "Nhập kho bị chặn" : "Hết hàng"}</td></tr>)}</tbody></table></div>
    {!orders.length && <p className="muted">Chưa có đơn mô phỏng.</p>}
    </>}
  </section>;
}
