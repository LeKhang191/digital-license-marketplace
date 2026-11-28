import { useEffect, useState } from "react";
import { listReplacementHistory, type ReplacementHistoryItem } from "../api/licenses";

export function InventoryReplacementHistory({ productId, refreshVersion }: { productId: string; refreshVersion: number }) {
  const [rows, setRows] = useState<ReplacementHistoryItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    setRows([]); setError(""); setLoading(true);
    listReplacementHistory(productId).then(data => { if (active) setRows(data); })
      .catch(() => { if (active) setError("Không tải được lịch sử đổi key."); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [productId, refreshVersion]);
  return <section className="card admin-table-container">
    <h2>Lịch sử đổi key</h2>
    <p className="muted">Tối đa 200 lần đổi gần nhất của sản phẩm đã chọn. Chỉ hiển thị các lần đổi đã thực hiện.</p>
    {loading ? <p>Đang tải...</p> : error ? <p role="alert">{error}</p> : rows.length === 0 ? <p>Chưa có lần đổi key nào.</p> :
      <table className="admin-table"><thead><tr><th>Ticket</th><th>Key cũ (ID)</th><th>Key mới (ID)</th><th>Thời gian</th></tr></thead>
      <tbody>{rows.map(row => <tr key={row.ticketId}><td><code title={row.ticketId}>{row.ticketId.slice(0,8)}</code></td>
        <td><code title={row.oldLicenseId}>{row.oldLicenseId.slice(0,8)}</code></td><td><code title={row.newLicenseId}>{row.newLicenseId.slice(0,8)}</code></td>
        <td>{new Date(row.createdAtUtc).toLocaleString("vi-VN")}</td></tr>)}</tbody></table>}
  </section>;
}
