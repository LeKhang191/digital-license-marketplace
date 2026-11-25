import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { getOrder } from "../api/orders";
import type { Order } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { StatusBadge } from "../components/StatusBadge";
import { isFinalStatus, nextPollDelayMs } from "./paymentPolling";

// Khách quay về từ VNPay. IPN (server-to-server) mới chốt đơn, nên trang này
// chỉ hỏi lại trạng thái đơn theo chu kỳ cho đến khi có kết quả cuối cùng.
export function PaymentResultPage() {
  const [params] = useSearchParams();
  const { ready, authenticated, login } = useAuth();
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [gaveUp, setGaveUp] = useState(false);

  // vnp_TxnRef: 32 ký tự đầu là Id đơn (dạng N, không có dấu gạch)
  const ref = params.get("vnp_TxnRef") ?? "";
  const hex = ref.slice(0, 32);
  const orderId = /^[0-9a-f]{32}$/i.test(hex)
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
            {order.status === "PENDING" && <p className="muted">Đang chờ VNPay xác nhận. Không thanh toán lại khi chưa đối soát.</p>}
            {order.status === "PAID" && !gaveUp && <p className="muted">Thanh toán thành công. Hệ thống đang cấp Key cho bạn...</p>}
            {gaveUp && <p className="muted">Hệ thống chưa có kết quả cuối cùng. Kiểm tra trang đơn hàng hoặc liên hệ hỗ trợ nếu đã chuyển tiền. Không thanh toán lại.</p>}
            {order.status === "COMPLETED" && <p>Đơn hàng đã hoàn tất. Kiểm tra thông tin giao hàng hoặc liên hệ hỗ trợ.</p>}
            {order.status === "REFUNDING" && <p>Đơn hàng cần được xử lý hoàn tiền. Chưa xác nhận tiền đã được hoàn.</p>}
            {order.status === "REFUNDED" && <p>Đã hoàn tiền. Rất tiếc về sự bất tiện này.</p>}
            {order.status === "CANCELED" && <p>Giao dịch không thành công nên đơn hàng đã bị hủy. Bạn có thể đặt đơn mới từ trang sản phẩm.</p>}
          </>
        )}
        <p style={{ marginTop: 24 }}><Link to="/orders">Xem đơn hàng của tôi</Link></p>
      </div>
    </div>
  );
}
