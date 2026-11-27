import { useState } from "react";
import { previewSteam, importSteam } from "../api/products";
import type { SteamMetadata } from "../api/types";
export function GameImport({ onImported }: { onImported: () => void }) {
  const [app, setApp] = useState("");
  const [price, setPrice] = useState("250000");
  const [preview, setPreview] = useState<SteamMetadata | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  async function action(save: boolean) {
    setBusy(true); setMessage("");
    try {
      if (save) { await importSteam(app, Number(price)); setPreview(null); setMessage("Đã nhập sản phẩm ở trạng thái ẩn."); onImported(); }
      else setPreview(await previewSteam(app));
    } catch (e) { setMessage(e instanceof Error ? e.message : "Không thể nhập metadata"); }
    finally { setBusy(false); }
  }
  return <section className="card steam-import"><h2>Nhập game từ Steam / IGDB</h2><p className="muted">Chỉ nhập metadata, không mua key. Sản phẩm mới mặc định ẩn.</p>
    <form className="row" onSubmit={e => { e.preventDefault(); void action(false); }}><input required value={app} onChange={e => { setApp(e.target.value); setPreview(null); }} placeholder="Steam App ID hoặc URL" disabled={busy}/><button className="btn" disabled={busy}>Xem trước</button></form>
    {preview && <div className="steam-preview"><h3>{preview.name}</h3><p>Nguồn: {preview.source ?? "Steam"}</p>{preview.headerImage && <img src={preview.headerImage} alt={preview.name}/>}<p>{preview.description}</p><div className="row"><label>Giá bán VND <input type="number" min={5000} max={9999999999} step={1} value={price} onChange={e => setPrice(e.target.value)}/></label><button className="btn" disabled={busy || !Number.isInteger(Number(price)) || Number(price)<5000 || Number(price)>9999999999} onClick={() => void action(true)}>Nhập sản phẩm</button></div></div>}
    {message && <p role="status">{message}</p>}</section>;
}
