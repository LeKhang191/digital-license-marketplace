import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { previewSteam, importSteam, listProducts } from "../api/products";
import {
  completeAdminRefund,
  getVaultStock,
  importVaultKeys,
  listAdminRefunds,
  type VaultStockSummary,
} from "../api/orders";
import type { SteamMetadata, Product, Order } from "../api/types";
import { StatusBadge } from "../components/StatusBadge";
import { vnd } from "../components/format";

export function AdminPage() {
  const { ready, isAdmin } = useAuth();
  const [app, setApp] = useState("");
  const [price, setPrice] = useState("50000");
  const [preview, setPreview] = useState<SteamMetadata | null>(null);
  const [created, setCreated] = useState<Product | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const [products, setProducts] = useState<Product[]>([]);
  const [stock, setStock] = useState<VaultStockSummary[]>([]);
  const [selectedProductId, setSelectedProductId] = useState("");
  const [keysInput, setKeysInput] = useState("");
  const [vaultBusy, setVaultBusy] = useState(false);
  const [vaultMessage, setVaultMessage] = useState("");
  const [vaultError, setVaultError] = useState("");

  const [refunds, setRefunds] = useState<Order[]>([]);
  const [refundBusyId, setRefundBusyId] = useState<string | null>(null);
  const [refundError, setRefundError] = useState("");
  const [refreshTick, setRefreshTick] = useState(0);

  useEffect(() => {
    if (!ready || !isAdmin) return;
    let active = true;
    void listProducts({ page: 1, pageSize: 100 })
      .then((res) => {
        if (!active) return;
        setProducts(res.items);
        setSelectedProductId((prev) => prev || res.items[0]?.id || "");
      })
      .catch(() => {});
    void getVaultStock()
      .then((rows) => { if (active) setStock(rows); })
      .catch((e: Error) => { if (active) setVaultError(e.message); });
    void listAdminRefunds()
      .then((rows) => { if (active) setRefunds(rows); })
      .catch((e: Error) => { if (active) setRefundError(e.message); });
    return () => { active = false; };
  }, [ready, isAdmin, refreshTick]);

  if (!ready) return <div className="container"><p className="muted">Đang tải...</p></div>;
  if (!isAdmin) return <div className="container"><div className="alert">Bạn không có quyền truy cập trang này.</div></div>;

  async function load() {
    setBusy(true); setError(""); setPreview(null); setCreated(null);
    try { setPreview(await previewSteam(app)); } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }

  async function save() {
    if (!preview) return;
    const parsedPrice = Number(price);
    if (!Number.isInteger(parsedPrice) || parsedPrice < 5000) {
      setError("Giá bán phải là số nguyên tối thiểu 5.000 VNĐ.");
      return;
    }
    setBusy(true); setError("");
    try {
      const product = await importSteam(String(preview.appId), parsedPrice);
      setCreated(product);
      setPreview(null);
      setSelectedProductId(product.id);
      setRefreshTick((n) => n + 1);
    }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }

  async function submitKeys() {
    const lines = keysInput.split(/\r?\n/).map((s) => s.trim()).filter(Boolean);
    if (!selectedProductId) {
      setVaultError("Vui lòng chọn sản phẩm cần nhập key.");
      return;
    }
    if (lines.length === 0) {
      setVaultError("Vui lòng nhập ít nhất 1 key (mỗi dòng 1 mã).");
      return;
    }
    setVaultBusy(true); setVaultError(""); setVaultMessage("");
    try {
      const res = await importVaultKeys(selectedProductId, lines);
      setKeysInput("");
      setVaultMessage(`Đã nhập ${res.added} key mới (${res.duplicates} key trùng đã bỏ qua).`);
      setRefreshTick((n) => n + 1);
    } catch (e) {
      setVaultError((e as Error).message);
    } finally {
      setVaultBusy(false);
    }
  }

  async function handleRefund(orderId: string) {
    setRefundBusyId(orderId); setRefundError("");
    try {
      await completeAdminRefund(orderId);
      setRefreshTick((n) => n + 1);
    } catch (e) {
      setRefundError((e as Error).message);
    } finally {
      setRefundBusyId(null);
    }
  }

  const productNames = new Map(products.map((p) => [p.id, p.name]));

  return (
    <div className="container">
      <h1>Quản trị</h1>
      <section className="card steam-import">
        <h2>Import game từ Steam</h2>
        <p className="muted">Metadata từ Steam/IGDB, giá bán do shop thiết lập (tối thiểu 5.000 đ). Key nhập ở mục Kho Key bên dưới.</p>
        <form onSubmit={(e) => { e.preventDefault(); void load(); }}>
          <label htmlFor="steam-app">Steam App ID hoặc URL</label>
          <div className="row">
            <input
              id="steam-app"
              value={app}
              required
              maxLength={500}
              disabled={busy}
              onChange={(e) => { setApp(e.target.value); setPreview(null); setCreated(null); }}
              placeholder="1091500 hoặc https://store.steampowered.com/app/1091500/"
            />
            <button className="btn" disabled={busy}>Xem trước</button>
          </div>
        </form>
        {preview && (
          <div className="steam-preview">
            <img src={preview.headerImage || undefined} alt={preview.name} />
            <h3>{preview.name}</h3>
            <p>{preview.description}</p>
            <p className="muted">{preview.genres.join(" · ")} · {preview.releaseDate}</p>
            <form onSubmit={(e) => { e.preventDefault(); void save(); }}>
              <label htmlFor="steam-price">Giá bán (VNĐ)</label>
              <div className="row">
                <input
                  id="steam-price"
                  type="number"
                  min="5000"
                  max="9999999999"
                  step="1"
                  required
                  value={price}
                  disabled={busy}
                  onChange={(e) => setPrice(e.target.value)}
                />
                <button className="btn" disabled={busy}>Xuất bản sản phẩm</button>
              </div>
            </form>
          </div>
        )}
        {busy && <p role="status">Đang lấy dữ liệu game...</p>}
        {error && <div className="alert" role="alert">{error}</div>}
        {created && <p role="status">Đã tạo {created.name}. <Link to={`/products/${created.id}`}>Xem sản phẩm →</Link></p>}
      </section>

      <div className="grid" style={{ gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))" }}>
        <section className="card">
          <div className="row">
            <h2>Kho Key</h2>
            <span className="spacer" />
            <button className="btn btn-secondary" type="button" onClick={() => setRefreshTick((n) => n + 1)}>
              Làm mới
            </button>
          </div>
          <p className="muted">Nhập mã kích hoạt (mỗi dòng 1 key). Key được mã hóa AES-GCM trong License Vault.</p>
          <form onSubmit={(e) => { e.preventDefault(); void submitKeys(); }}>
            <div style={{ marginBottom: 12 }}>
              <label htmlFor="vault-product">Sản phẩm</label>
              <select
                id="vault-product"
                value={selectedProductId}
                disabled={vaultBusy}
                onChange={(e) => setSelectedProductId(e.target.value)}
                style={{ width: "100%", marginTop: 6 }}
              >
                <option value="">-- Chọn sản phẩm --</option>
                {products.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.id.slice(0, 8)})
                  </option>
                ))}
              </select>
            </div>
            <div style={{ marginBottom: 12 }}>
              <label htmlFor="vault-keys">Danh sách key (mỗi dòng 1 mã)</label>
              <textarea
                id="vault-keys"
                rows={4}
                value={keysInput}
                disabled={vaultBusy}
                onChange={(e) => setKeysInput(e.target.value)}
                placeholder={"XXXXX-YYYYY-ZZZZZ\nAAAAA-BBBBB-CCCCC"}
                style={{ width: "100%", marginTop: 6 }}
              />
            </div>
            <button className="btn" disabled={vaultBusy}>
              {vaultBusy ? "Đang nhập..." : "Nhập kho key"}
            </button>
          </form>
          {vaultMessage && <p role="status" className="muted" style={{ marginTop: 12 }}>{vaultMessage}</p>}
          {vaultError && <div className="alert" role="alert" style={{ marginTop: 12 }}>{vaultError}</div>}

          <div className="order-key-panel">
            <strong>Tồn kho hiện tại</strong>
            {stock.length === 0 ? (
              <p className="muted">Chưa có key nào trong kho.</p>
            ) : (
              stock.map((s) => (
                <div className="order-key-row" key={s.productId}>
                  <span>{productNames.get(s.productId) ?? s.productId.slice(0, 8)}</span>
                  <span className="spacer" />
                  <span className="muted">Còn: <strong>{s.available}</strong> · Đã cấp: {s.allocated}</span>
                </div>
              ))
            )}
          </div>
        </section>

        <section className="card">
          <div className="row">
            <h2>Hoàn tiền</h2>
            <span className="spacer" />
            <button className="btn btn-secondary" type="button" onClick={() => setRefreshTick((n) => n + 1)}>
              Làm mới
            </button>
          </div>
          <p className="muted">Các đơn hết key sau khi thanh toán sẽ tự chuyển sang Đang hoàn tiền để đối soát và hoàn trả.</p>
          {refundError && <div className="alert" role="alert" style={{ marginBottom: 12 }}>{refundError}</div>}
          {refunds.length === 0 ? (
            <p className="muted">Không có đơn hàng nào cần hoàn tiền.</p>
          ) : (
            refunds.map((o) => (
              <div className="order-key-panel" key={o.id}>
                <div className="row">
                  <strong>Đơn #{o.id.slice(0, 8)}</strong>
                  <StatusBadge status={o.status} />
                  <span className="spacer" />
                  <span className="price">{vnd(o.totalAmount)}</span>
                </div>
                <div className="muted" style={{ marginTop: 4 }}>
                  {o.items.length} sản phẩm · {new Date(o.createdAtUtc).toLocaleString("vi-VN")}
                </div>
                {o.status === "REFUNDING" && (
                  <button
                    className="btn"
                    style={{ marginTop: 12 }}
                    disabled={refundBusyId === o.id}
                    onClick={() => void handleRefund(o.id)}
                  >
                    {refundBusyId === o.id ? "Đang xác nhận..." : "Xác nhận đã hoàn tiền"}
                  </button>
                )}
              </div>
            ))
          )}
        </section>
      </div>
    </div>
  );
}
