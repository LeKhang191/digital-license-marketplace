import { useEffect, useState } from "react";
import { api } from "../api/client";
import { RefreshButton } from "../components/RefreshButton";
import { vnd } from "../components/format";

type Transaction = {
  id: string; keycloakUserId: string; customerEmail: string | null;
  totalAmount: number; paymentMethod: string; paymentRef: string | null;
  status: string; paidAtUtc: string;
};
type AuditPage = { total: number; refunded: number; page: number; pageSize: number; items: Transaction[] };
const labels: Record<string, string> = { PAID: "Đã thanh toán", COMPLETED: "Hoàn thành", REFUNDING: "Đang hoàn tiền", REFUNDED: "Đã hoàn tiền", CANCELED: "Đã hủy" };

export function TransactionsPage() {
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [data, setData] = useState<AuditPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    setLoading(true); setError(null); setData(null);
    api<AuditPage>(`/transactions?page=${page}&pageSize=25`, { auth: true })
      .then(result => { if (!cancelled) setData(result); })
      .catch((e: Error) => { if (!cancelled) setError(e.message); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [page, revision]);

  return <div className="container">
    <h1>Quản lý giao dịch</h1>
    <p className="muted">Đơn hàng đã ghi nhận thanh toán (Ví DLM và VNPay).</p>
    <div className="grid admin-summary">
      <div className="card"><h2>Đơn đã thanh toán</h2><strong>{data?.total ?? "—"}</strong></div>
      <div className="card"><h2>Đã hoàn tiền</h2><strong>{data?.refunded ?? "—"}</strong></div>
    </div>
    <div className="card" style={{ marginTop: 24 }}>
      <div className="row" style={{ justifyContent: "space-between", alignItems: "center" }}>
        <h2 style={{ margin: 0 }}>Danh sách giao dịch</h2>
        <RefreshButton loading={loading} onClick={() => setRevision(n => n + 1)} />
      </div>
      {error && <p className="alert" role="alert">{error}</p>}
      {loading && <p role="status">Đang tải...</p>}
      {data && <>
        <div className="admin-table-container"><table className="admin-table">
          <thead><tr><th>Mã đơn hàng</th><th>Khách hàng</th><th>Số tiền</th><th>Phương thức</th><th>Trạng thái đơn</th><th>Thanh toán lúc</th></tr></thead>
          <tbody>{data.items.map(t => <tr key={t.id}>
            <td>{t.id}<div className="muted">{t.paymentRef ?? "—"}</div></td><td>{t.customerEmail || t.keycloakUserId}</td>
            <td>{vnd(t.totalAmount)}</td><td>{t.paymentMethod === "WALLET" ? "Ví DLM" : t.paymentMethod}</td>
            <td>{labels[t.status] ?? t.status}</td><td>{new Date(t.paidAtUtc).toLocaleString("vi-VN")}</td>
          </tr>)}{!data.items.length && <tr><td colSpan={6}>Không có giao dịch trên trang này.</td></tr>}</tbody>
        </table></div>
        <div className="row" style={{ marginTop: 16 }}>
          <button className="btn btn-secondary" disabled={page <= 1} onClick={() => setPage(n => n - 1)}>Trước</button>
          <span>Trang {page} / {Math.max(1, Math.ceil(data.total / data.pageSize))}</span>
          <button className="btn btn-secondary" disabled={page * data.pageSize >= data.total} onClick={() => setPage(n => n + 1)}>Sau</button>
        </div>
      </>}
    </div>
  </div>;
}
