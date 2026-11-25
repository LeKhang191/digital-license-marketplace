import { useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { previewSteam, importSteam } from "../api/products";
import type { SteamMetadata, Product } from "../api/types";

export function AdminPage() {
  const { ready, isAdmin } = useAuth();
  const [app, setApp] = useState("");
  const [price, setPrice] = useState("50000");
  const [preview, setPreview] = useState<SteamMetadata | null>(null);
  const [created, setCreated] = useState<Product | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  if (!ready) return <div className="container"><p className="muted">Đang tải...</p></div>;
  if (!isAdmin) return <div className="container"><div className="alert">Bạn không có quyền truy cập trang này.</div></div>;
  async function load() {
    setBusy(true); setError(""); setPreview(null); setCreated(null);
    try { setPreview(await previewSteam(app)); } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  async function save() {
    if (!preview) return;
    setBusy(true); setError("");
    try { setCreated(await importSteam(String(preview.appId), Number(price))); setPreview(null); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  return <div className="container"><h1>Quản trị</h1><section className="card steam-import">
    <h2>Import game từ Steam</h2><p className="muted">Metadata từ Steam, giá bán do shop thiết lập. Key nhập riêng qua License Vault.</p>
    <form onSubmit={e=>{e.preventDefault(); void load();}}><label htmlFor="steam-app">Steam App ID hoặc URL</label><div className="row"><input id="steam-app" value={app} required maxLength={500} disabled={busy} onChange={e=>{setApp(e.target.value);setPreview(null);setCreated(null);}} placeholder="1091500 hoặc https://store.steampowered.com/app/1091500/"/><button className="btn" disabled={busy}>Xem trước</button></div></form>
    {preview && <div className="steam-preview"><img src={preview.headerImage || undefined} alt={preview.name}/><h3>{preview.name}</h3><p>{preview.description}</p><p className="muted">{preview.genres.join(" · ")} · {preview.releaseDate}</p><form onSubmit={e=>{e.preventDefault();void save();}}><label htmlFor="steam-price">Giá bán (VNĐ)</label><div className="row"><input id="steam-price" type="number" min="5000" max="9999999999" step="1" required value={price} disabled={busy} onChange={e=>setPrice(e.target.value)}/><button className="btn" disabled={busy}>Xuất bản sản phẩm</button></div></form></div>}
    {busy && <p role="status">Đang lấy dữ liệu Steam...</p>}{error && <div className="alert" role="alert">{error}</div>}{created && <p role="status">Đã tạo {created.name}. <Link to={`/products/${created.id}`}>Xem sản phẩm →</Link></p>}
    </section><div className="grid"><div className="card"><h2>Kho Key</h2><p className="muted">Nhập kho qua License Vault CLI.</p></div><div className="card"><h2>Hoàn tiền</h2><p className="muted">Chưa triển khai.</p></div></div></div>;
}
