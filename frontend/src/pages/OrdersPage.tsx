import { useEffect, useState } from "react";
import { OrderKeys } from "../components/OrderKeys";
import { keycloak } from "../auth/keycloak";
import { listOrders } from "../api/orders";
import type { Order } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { StatusBadge } from "../components/StatusBadge";
import { vnd } from "../components/format";

export function OrdersPage() {
  const { authenticated } = useAuth();
  return <OrderList key={authenticated ? keycloak.tokenParsed?.sub ?? "unknown" : "guest"} />;
}

function OrderList() {
  const { ready, authenticated, login } = useAuth();
  const [orders, setOrders] = useState<Order[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const subject = keycloak.tokenParsed?.sub;
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let active = true;
    setOrders(null); setError(null);
    if (authenticated && subject) listOrders().then(v => { if (active) setOrders(v); })
      .catch((e: Error) => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [authenticated, subject, revision]);

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
      <button className="btn" onClick={() => setRevision(v => v + 1)}>Làm mới đơn hàng</button>
      {error && <div className="alert">{error}</div>}
      {!orders && !error && <p className="muted">Đang tải...</p>}
      {orders && orders.length === 0 && <p className="muted">Bạn chưa có đơn hàng nào.</p>}

      <div className="grid" style={{ gridTemplateColumns: "1fr" }}>
        {orders?.map((o) => (
          <div className="card" key={`${subject}:${o.id}`}>
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
              <p className="muted">Đang chờ đối soát. Nếu đã thanh toán, không chuyển tiền lần nữa. Liên hệ hỗ trợ nếu cần kiểm tra giao dịch.</p>
            )}
            {o.status === "PAID" && (
              <p className="muted">Đã nhận thanh toán. Hệ thống đang trích xuất mã kích hoạt; bấm Làm mới đơn hàng sau vài giây.</p>
            )}
            {o.status === "REFUNDING" && (
              <div className="alert" style={{ marginTop: 12 }}>
                Kho tạm hết key cho đơn này. Yêu cầu hoàn tiền đang được xử lý.
              </div>
            )}
            {o.status === "REFUNDED" && (
              <p className="muted">Đơn hàng đã được hoàn tiền về phương thức thanh toán ban đầu.</p>
            )}
            {o.status === "CANCELED" && (
              <p className="muted">Đơn hàng đã bị hủy do giao dịch không hoàn tất.</p>
            )}
            {o.status === "COMPLETED" && <OrderKeys key={`${subject}:${o.id}`} orderId={o.id} />}
          </div>
        ))}
      </div>
    </div>
  );
}
