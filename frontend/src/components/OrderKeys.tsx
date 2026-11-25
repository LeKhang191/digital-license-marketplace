import { useEffect, useRef, useState } from "react";
import { getOrderKeys, type PurchasedLicense } from "../api/orders";

export function OrderKeys({ orderId }: { orderId: string }) {
  const [keys, setKeys] = useState<PurchasedLicense[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const pending = useRef<AbortController | null>(null);
  useEffect(() => () => pending.current?.abort(), []);

  async function reveal() {
    const controller = new AbortController();
    pending.current?.abort();
    pending.current = controller;
    setBusy(true);
    setMessage("");
    try {
      const result = await getOrderKeys(orderId, controller.signal);
      if (!controller.signal.aborted) setKeys(result);
    } catch {
      if (!controller.signal.aborted) setMessage("Không thể tải key. Vui lòng thử lại hoặc liên hệ hỗ trợ.");
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  return <section className="order-key-panel">
    <p><strong>Game key đã mua</strong></p>
    <p className="muted">Không chia sẻ mã kích hoạt với người khác.</p>
    <button className="btn" disabled={busy} onClick={() => {
      if (keys) { setKeys(null); setMessage(""); } else void reveal();
    }}>{busy ? "Đang tải..." : keys ? "Ẩn key" : "Hiện game key"}</button>
    {keys?.map((k, i) => <div className="order-key-row" key={k.itemId}>
      <span className="muted">Key {i + 1}</span>
      <code>{k.key}</code>
      <button className="btn" onClick={async () => {
        try { await navigator.clipboard.writeText(k.key); setMessage("Đã sao chép key."); }
        catch { setMessage("Không thể sao chép tự động; hãy chọn mã và sao chép thủ công."); }
      }}>Copy</button>
    </div>)}
    {message && <p role="status" className="muted">{message}</p>}
    <p><a href="https://help.steampowered.com/en/faqs/view/2A12-9D79-C3D7-F870" target="_blank" rel="noreferrer">Hướng dẫn kích hoạt trên Steam (nếu là key Steam)</a></p>
  </section>;
}
