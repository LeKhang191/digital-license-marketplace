import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { getOrder } from "../api/orders";
import { listProducts } from "../api/products";
import type { Order, Product } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { ReceiptDialog, buildOrderReceipt, type ReceiptData } from "../components/ReceiptDialog";
import { StatusBadge } from "../components/StatusBadge";
import { isFinalStatus, nextPollDelayMs } from "./paymentPolling";

// Khách quay về từ VNPay. IPN (server-to-server) mới chốt đơn, nên trang này
// chỉ hỏi lại trạng thái đơn theo chu kỳ cho đến khi có kết quả cuối cùng.
export function PaymentResultPage() {
  const [params] = useSearchParams();
  const { ready, authenticated, login } = useAuth();
  const [order, setOrder] = useState<Order | null>(null);
  const [products, setProducts] = useState<Product[]>([]);
  const [receipt, setReceipt] = useState<ReceiptData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [gaveUp, setGaveUp] = useState(false);

  useEffect(() => {
    listProducts({ page: 1 }).then((p) => setProducts(p.items)).catch(() => {});
  }, []);
  const productName = (id: string) => products.find((p) => p.id === id)?.name ?? `Sản phẩm ${id.slice(0, 8)}`;

  // Hỗ trợ cả ?orderId=<uuid> (thanh toán Ví) và ?vnp_TxnRef=<32hex> (VNPay)
  const rawOrderId = params.get("orderId") ?? "";
  const ref = params.get("vnp_TxnRef") ?? "";
  const hex = ref.slice(0, 32);
  const orderId = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(rawOrderId)
    ? rawOrderId
    : /^[0-9a-f]{32}$/i.test(hex)
      ? hex.replace(/(.{8})(.{4})(.{4})(.{4})(.{12})/, "$1-$2-$3-$4-$5")
      : null;

  useEffect(() => {
    if (!ready || !authenticated || !orderId) return;
    let stop = false;
    let attempt = 0;
    let timer: ReturnType<typeof setTimeout> | undefined;

    async function poll() {
      try {
        const o = await getOrder(orderId!);
        if (stop) return;
        attempt += 1;
        setOrder(o);
        const delay = nextPollDelayMs(o.status, attempt);
        if (delay !== null) timer = setTimeout(poll, delay);
        else if (!isFinalStatus(o.status)) setGaveUp(true);
      } catch (e) {
        if (!stop) setError((e as Error).message);
      }
    }
    poll();
    return () => {
      stop = true;
      clearTimeout(timer);
    };
  }, [ready, authenticated, orderId]);

  if (!orderId)
    return <div className="container"><div className="alert">Không tìm thấy mã đơn hàng trong đường dẫn.</div></div>;

  if (ready && !authenticated)
    return (
      <div className="container">
        <p>Vui lòng đăng nhập để xem kết quả thanh toán.</p>
        <button className="btn" onClick={login}>Đăng nhập</button>
      </div>
    );

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
