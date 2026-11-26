
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { listOrders, payOrder } from "../api/orders";
import type { Order } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { StatusBadge } from "../components/StatusBadge";
import { vnd } from "../components/format";

export function OrdersPage() {
  const {
    ready,
    authenticated,
    isCustomer,
    login,
  } = useAuth();

  const [orders, setOrders] = useState<Order[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [payingId, setPayingId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    setOrders(null);
    setError(null);

    if (!ready || !authenticated || !isCustomer)
      return;

    listOrders()
      .then((data) => {
        if (!cancelled) setOrders(data);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      });

    return () => {
      cancelled = true;
    };
  }, [ready, authenticated, isCustomer]);

  async function repay(id: string) {
    if (!ready || !authenticated || !isCustomer || payingId)
      return;

    setPayingId(id);
    setError(null);

    try {
      const order = await payOrder(id);

      if (!order.paymentUrl) {
        throw new Error(
          "Không nhận được liên kết thanh toán"
        );
      }

      window.location.href = order.paymentUrl;
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "Không thể thanh toán đơn hàng."
      );
    } finally {
      setPayingId(null);
    }
  }

  if (!ready) {
    return (
      <div className="container">
        <p className="muted">Đang tải...</p>
      </div>
    );
  }

  if (!authenticated) {
    return (
      <div className="container">
        <p>
          Vui lòng đăng nhập để xem đơn hàng.
        </p>

        <button className="btn" onClick={login}>
          Đăng nhập
        </button>
      </div>
    );
  }

  if (!isCustomer) {
    return (
      <div className="container">
        <div className="alert">
          Bạn không có quyền truy cập trang này.
        </div>

        <Link
          to="/"
          className="btn btn-secondary"
          style={{ marginTop: 16 }}
        >
          ← Quay về cửa hàng
        </Link>
      </div>
    );
  }

  return (
    <div className="container">
      <div className="product-back">
        <Link to="/" className="btn btn-secondary">
          ← Quay về cửa hàng
        </Link>
      </div>

      <h1>Đơn hàng của tôi</h1>

      {error && (
        <div className="alert">{error}</div>
      )}

      {!orders && !error && (
        <p className="muted">Đang tải...</p>
      )}

      {orders && orders.length === 0 && (
        <p className="muted">
          Bạn chưa có đơn hàng nào.
        </p>
      )}

      <div
        className="grid"
        style={{ gridTemplateColumns: "1fr" }}
      >
        {orders?.map((o) => (
          <div className="card" key={o.id}>
            <div className="row">
              <strong>
                Đơn {o.id.slice(0, 8)}
              </strong>

              <StatusBadge status={o.status} />

              <span className="spacer" />

              <span className="price">
                {vnd(o.totalAmount)}
              </span>
            </div>

            <div className="muted">
              {o.items.length} sản phẩm ·{" "}
              {new Date(o.createdAtUtc)
                .toLocaleString("vi-VN")}
            </div>

            {o.status === "PENDING" && (
              <button
                className="btn"
                style={{ marginTop: 12 }}
                disabled={payingId !== null}
                onClick={() => repay(o.id)}
              >
                {payingId === o.id
                  ? "Đang tạo liên kết..."
                  : "Thanh toán lại"}
              </button>
            )}

            {o.status === "COMPLETED" && (
              <p>
                <Link
                  className="btn"
                  to={`/my-licenses?orderId=${o.id}`}
                >
                  Xem License Key
                </Link>
              </p>
            )}

            {o.status === "PAID" && (
              <p className="muted">
                Đã thanh toán. Đơn hàng đang được xử lý.
                Vui lòng không thanh toán lại.
              </p>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
