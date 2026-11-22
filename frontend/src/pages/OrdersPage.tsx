import { useEffect, useState } from "react";
import { listOrders, payOrder } from "../api/orders";
import type { Order } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { StatusBadge } from "../components/StatusBadge";
import { vnd } from "../components/format";

export function OrdersPage() {
  const { ready, authenticated, login } = useAuth();
  const [orders, setOrders] = useState<Order[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!authenticated) return;
    listOrders().then(setOrders).catch((e: Error) => setError(e.message));
  }, [authenticated]);

  async function repay(id: string) {
    try {
      const order = await payOrder(id);
      if (order.paymentUrl) window.location.href = order.paymentUrl;
    } catch (e) {
      setError((e as Error).message);
    }
  }

  if (!ready) return <div className="container"><p className="muted">Đang tải...</p></div>;
  if (!authenticated)
    return (
      <div className="container">
        <p>Vui lòng đăng nhập để xem đơn hàng.</p>
        <button className="btn" onClick={login}>Đăng nhập</button>
      </div>
    );

  return (
    <div className="container">
      <h1>Đơn hàng của tôi</h1>
      {error && <div className="alert">{error}</div>}
      {!orders && !error && <p className="muted">Đang tải...</p>}
      {orders && orders.length === 0 && <p className="muted">Bạn chưa có đơn hàng nào.</p>}

      <div className="grid" style={{ gridTemplateColumns: "1fr" }}>
        {orders?.map((o) => (
          <div className="card" key={o.id}>
            <div className="row">
              <strong>Đơn {o.id.slice(0, 8)}</strong>
              <StatusBadge status={o.status} />
              <span className="spacer" />
              <span className="price">{vnd(o.totalAmount)}</span>
            </div>
            <div className="muted">
              {o.items.length} sản phẩm · {new Date(o.createdAtUtc).toLocaleString("vi-VN")}
            </div>
            {o.status === "PENDING" && (
              <button className="btn" style={{ marginTop: 12 }} onClick={() => repay(o.id)}>
                Thanh toán lại
              </button>
            )}
            {/* TODO: hiển thị Key khi License Vault sẵn sàng (đơn COMPLETED) */}
          </div>
        ))}
      </div>
    </div>
  );
}
