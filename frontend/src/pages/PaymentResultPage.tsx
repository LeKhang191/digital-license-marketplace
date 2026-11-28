import { useEffect, useState } from "react";
import { Link, useLocation, useSearchParams } from "react-router-dom";
import { getOrder } from "../api/orders";
import { listProducts } from "../api/products";
import {
  confirmVnPayReturn,
  getWalletTopUp,
  type WalletTopUpStatus,
} from "../api/wallet";
import type { Order, Product } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { ReceiptDialog, buildOrderReceipt, type ReceiptData } from "../components/ReceiptDialog";
import { StatusBadge } from "../components/StatusBadge";
import { vnd } from "../components/format";
import { isFinalStatus, nextPollDelayMs } from "./paymentPolling";

// Khách quay về từ VNPay (mua đơn hàng hoặc nạp Ví DLM).
// Chữ ký HMAC-SHA512 được xác thực idempotent trên backend trước khi chốt trạng thái.
export function PaymentResultPage() {
  const [params] = useSearchParams();
  const location = useLocation();
  const { ready, authenticated, login } = useAuth();
  const [order, setOrder] = useState<Order | null>(null);
  const [topUp, setTopUp] = useState<WalletTopUpStatus | null>(null);
  const [products, setProducts] = useState<Product[]>([]);
  const [receipt, setReceipt] = useState<ReceiptData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [gaveUp, setGaveUp] = useState(false);

  const rawOrderId = params.get("orderId") ?? "";
  const ref = params.get("vnp_TxnRef") ?? "";
  const isWalletTopUp = /^TOPUP[0-9A-F]{8,}$/i.test(ref);

  const hex = ref.slice(0, 32);
  const orderId = !isWalletTopUp
    ? /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(rawOrderId)
      ? rawOrderId
      : /^[0-9a-f]{32}$/i.test(hex)
        ? hex.replace(/(.{8})(.{4})(.{4})(.{4})(.{12})/, "$1-$2-$3-$4-$5")
        : null
    : null;

  useEffect(() => {
    if (!isWalletTopUp) {
      listProducts({ page: 1 }).then((p) => setProducts(p.items)).catch(() => {});
    }
  }, [isWalletTopUp]);

  const productName = (id: string) =>
    products.find((p) => p.id === id)?.name ?? `Sản phẩm ${id.slice(0, 8)}`;

  useEffect(() => {
    if (!ready || !authenticated || (!orderId && !isWalletTopUp)) return;
    let stop = false;
    let attempt = 0;
    let timer: ReturnType<typeof setTimeout> | undefined;

    async function start() {
      // Nếu VNPay chuyển hướng kèm vnp_SecureHash, xác thực chữ ký idempotent với backend.
      if (location.search.includes("vnp_SecureHash=")) {
        await confirmVnPayReturn(location.search);
        if (stop) return;
      }

      if (isWalletTopUp) {
        async function pollTopUp() {
          try {
            const res = await getWalletTopUp(ref);
            if (stop) return;
            attempt += 1;
            setTopUp(res);
            if (res.status === "PENDING") {
              if (attempt < 15) {
                timer = setTimeout(pollTopUp, 2000);
              } else {
                setGaveUp(true);
              }
            }
          } catch (e) {
            if (!stop) setError((e as Error).message);
          }
        }
        void pollTopUp();
        return;
      }

      async function pollOrder() {
        try {
          const o = await getOrder(orderId!);
          if (stop) return;
          attempt += 1;
          setOrder(o);
          const delay = nextPollDelayMs(o.status, attempt);
          if (delay !== null) timer = setTimeout(pollOrder, delay);
          else if (!isFinalStatus(o.status)) setGaveUp(true);
        } catch (e) {
          if (!stop) setError((e as Error).message);
        }
      }
      void pollOrder();
    }

    void start();

    return () => {
      stop = true;
      clearTimeout(timer);
    };
  }, [ready, authenticated, orderId, isWalletTopUp, ref, location.search]);

  if (!orderId && !isWalletTopUp) {
    return (
      <div className="container">
        <div className="alert">Không tìm thấy mã giao dịch trong đường dẫn.</div>
      </div>
    );
  }

  if (ready && !authenticated) {
    return (
      <div className="container">
        <p>Vui lòng đăng nhập để xem kết quả thanh toán.</p>
        <button className="btn" onClick={login}>Đăng nhập</button>
      </div>
    );
  }

  if (isWalletTopUp) {
    return (
      <div className="container">
        <div className="card">
          <h1>Kết quả nạp tiền Ví DLM</h1>
          {error && <div className="alert">{error}</div>}
          {!topUp && !error && <p className="muted">Đang xác nhận giao dịch VNPay...</p>}
          {topUp && (
            <>
              <p>
                <span
                  className="badge"
                  style={{
                    background:
                      topUp.status === "SUCCEEDED"
                        ? "var(--color-success)"
                        : topUp.status === "FAILED"
                          ? "var(--color-danger)"
                          : "var(--color-warning)",
                  }}
                >
                  {topUp.status === "SUCCEEDED"
                    ? "Nạp ví thành công"
                    : topUp.status === "FAILED"
                      ? "Giao dịch thất bại"
                      : "Đang chờ xác nhận VNPay"}
                </span>
              </p>

              <p style={{ marginTop: 12 }}>
                Số tiền nạp: <strong>{vnd(topUp.amount)}</strong>
              </p>
              <p className="muted">Mã giao dịch: <code>{topUp.paymentRef}</code></p>
              {topUp.vnpTransactionNo && (
                <p className="muted">Mã VNPay: <code>{topUp.vnpTransactionNo}</code></p>
              )}

              {topUp.status === "SUCCEEDED" && (
                <div className="alert-success" style={{ marginTop: 14 }}>
                  Số dư Ví DLM hiện tại của bạn là{" "}
                  <strong>{vnd(topUp.walletBalance)}</strong>.
                </div>
              )}

              {topUp.status === "PENDING" && !gaveUp && (
                <p className="muted" style={{ marginTop: 10 }}>
                  Đang chờ VNPay xác nhận giao dịch. Vui lòng không đóng trang.
                </p>
              )}

              {topUp.status === "PENDING" && gaveUp && (
                <p className="muted" style={{ marginTop: 10 }}>
                  Giao dịch vẫn đang chờ xác nhận từ VNPay. Nếu tiền đã bị trừ, bạn có thể kiểm tra lại ở trang Ví hoặc gửi phiếu hỗ trợ nạp ví.
                </p>
              )}

              {topUp.status === "FAILED" && (
                <p className="muted" style={{ marginTop: 10 }}>
                  Giao dịch nạp tiền không thành công hoặc đã bị hủy trên VNPay. Số dư ví không thay đổi.
                </p>
              )}
            </>
          )}

          <div className="row" style={{ marginTop: 22 }}>
            <Link className="btn" to="/account">
              Về Ví DLM &amp; Lịch sử giao dịch
            </Link>
            <Link className="btn btn-secondary" to="/">
              Tiếp tục mua game
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="container">
      <div className="card">
        <h1>Kết quả thanh toán</h1>
        {error && <div className="alert">{error}</div>}
        {!order && !error && <p className="muted">Đang xác nhận thanh toán...</p>}
        {order && (
          <>
            <p><StatusBadge status={order.status} /></p>
            {order.status === "PENDING" && <p className="muted">Đang chờ VNPay xác nhận. Vui lòng không đóng trang.</p>}
            {order.status === "PAID" && !gaveUp && <p className="muted">Thanh toán thành công. Hệ thống đang cấp Key cho bạn...</p>}
            {gaveUp && <p className="muted">Hệ thống vẫn đang xử lý đơn hàng. Bạn có thể đóng trang này, Key sẽ được gửi qua email khi sẵn sàng. Trạng thái luôn cập nhật ở trang đơn hàng.</p>}
            {order.status === "COMPLETED" && (
              <>
                <p>Key đã sẵn sàng trong tài khoản và thông báo đã được gửi qua email của bạn.</p>
                <p style={{ marginTop: 12 }}>
                  <Link className="btn" to={`/my-licenses?orderId=${order.id}`}>
                    Xem License Key ngay
                  </Link>
                </p>
              </>
            )}
            {order.status === "REFUNDING" && <p>Sản phẩm tạm hết Key. Chúng tôi đang hoàn tiền cho bạn.</p>}
            {order.status === "REFUNDED" && <p>Đã hoàn tiền. Rất tiếc về sự bất tiện này.</p>}
            {order.status === "CANCELED" && <p>Giao dịch không thành công nên đơn hàng đã bị hủy. Bạn có thể đặt đơn mới từ trang sản phẩm.</p>}
            {["PAID", "COMPLETED", "REFUNDING", "REFUNDED"].includes(order.status) && (
              <p style={{ marginTop: 12 }}>
                <button type="button" className="btn btn-secondary" onClick={() => setReceipt(buildOrderReceipt(order, productName))}>
                  Xem chứng từ giao dịch
                </button>
              </p>
            )}
          </>
        )}
        <p style={{ marginTop: 24 }}><Link to="/orders">Xem đơn hàng của tôi</Link></p>
      </div>
      <ReceiptDialog receipt={receipt} onClose={() => setReceipt(null)} />
    </div>
  );
}
