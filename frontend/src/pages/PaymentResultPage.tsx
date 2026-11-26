import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { getOrder } from "../api/orders";
import type { Order } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { OrderKeys } from "../components/OrderKeys";
import { StatusBadge } from "../components/StatusBadge";
import { vnd } from "../components/format";
import { nextPollDelayMs, shouldMarkGaveUp } from "./paymentPolling";

export function PaymentResultPage() {
  const [params] = useSearchParams();
  const { ready, authenticated, login } = useAuth();
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [gaveUp, setGaveUp] = useState(false);
  const [pollRun, setPollRun] = useState(0);

  const ref = params.get("vnp_TxnRef") ?? "";
  const hex = ref.slice(0, 32);
  const orderId = /^[0-9a-f]{32}$/i.test(hex)
    ? hex.replace(/(.{8})(.{4})(.{4})(.{4})(.{12})/, "$1-$2-$3-$4-$5")
    : null;

  const responseCode = params.get("vnp_ResponseCode");
  const isVnpCanceled = responseCode === "24";
  const isVnpFailed =
    responseCode !== null &&
    responseCode !== "" &&
    responseCode !== "00" &&
    responseCode !== "24";

  useEffect(() => {
    if (!ready || !authenticated || !orderId) return;
    let stop = false;
    let attempt = 0;
    let timer: ReturnType<typeof setTimeout> | undefined;

    setGaveUp(false);
    setError(null);

    async function poll() {
      try {
        const o = await getOrder(orderId!);
        if (stop) return;
        attempt += 1;
        setOrder(o);
        const delay = nextPollDelayMs(o.status, attempt, responseCode);
        if (delay !== null) timer = setTimeout(poll, delay);
        else if (shouldMarkGaveUp(o.status, responseCode)) setGaveUp(true);
      } catch (e) {
        if (!stop) setError((e as Error).message);
      }
    }
    void poll();
    return () => {
      stop = true;
      clearTimeout(timer);
    };
  }, [ready, authenticated, orderId, responseCode, pollRun]);

  if (!orderId)
    return (
      <div className="container">
        <div className="card">
          <h1>Không tìm thấy mã đơn hàng</h1>
          <div className="alert">
            Đường dẫn thiếu mã tham chiếu giao dịch hợp lệ (vnp_TxnRef).
          </div>
          <div className="row" style={{ marginTop: 24 }}>
            <Link className="btn" to="/orders">Xem đơn hàng của tôi</Link>
            <Link className="btn btn-secondary" to="/">Về cửa hàng</Link>
          </div>
        </div>
      </div>
    );

  if (ready && !authenticated)
    return (
      <div className="container">
        <div className="card">
          <h1>Kết quả thanh toán</h1>
          <p className="muted">
            Vui lòng đăng nhập để đối chiếu trạng thái đơn hàng #{orderId.slice(0, 8)} và nhận game key.
          </p>
          <div className="row" style={{ marginTop: 16 }}>
            <button className="btn" onClick={login}>Đăng nhập</button>
            <Link className="btn btn-secondary" to="/">Về cửa hàng</Link>
          </div>
        </div>
      </div>
    );

  const isCanceledView =
    order?.status === "CANCELED" ||
    (order?.status === "PENDING" && isVnpCanceled);

  const isFailedView =
    order?.status === "REFUNDING" ||
    order?.status === "REFUNDED" ||
    (order?.status === "PENDING" && isVnpFailed);

  function resolveHeading(): string {
    if (!order) return error ? "Không thể kiểm tra đơn hàng" : "Đang xác nhận thanh toán";
    if (order.status === "COMPLETED") return "Thanh toán và cấp key hoàn tất";
    if (order.status === "PAID") {
      return gaveUp
        ? "Đã nhận thanh toán — Chưa hoàn tất cấp key"
        : "Đã nhận thanh toán — Đang cấp key";
    }
    if (order.status === "CANCELED" || (order.status === "PENDING" && isVnpCanceled)) {
      return "Giao dịch đã hủy";
    }
    if (order.status === "REFUNDING" || order.status === "REFUNDED") {
      return "Không thể cấp key — Đơn hoàn tiền";
    }
    if (order.status === "PENDING" && isVnpFailed) {
      return "Thanh toán không thành công tại VNPay";
    }
    if (gaveUp) return "Chưa nhận được kết quả cuối cùng";
    return "Đang chờ VNPay xác nhận";
  }

  return (
    <div className="container">
      <div className="card">
        <div className="row" style={{ marginBottom: 12 }}>
          {order ? (
            <StatusBadge status={order.status} />
          ) : (
            <span className="muted">Đang đồng bộ</span>
          )}
          <span className="spacer" />
          <span className="muted">Mã đơn #{orderId.slice(0, 8)}</span>
        </div>

        <h1>{resolveHeading()}</h1>

        {error && (
          <div className="alert" style={{ marginBottom: 16 }}>
            {error}
          </div>
        )}

        {!order && !error && (
          <>
            <p className="muted">
              Đang kiểm tra kết quả giao dịch với hệ thống. Vui lòng giữ trang này trong giây lát...
            </p>
            <div className="skeleton" aria-busy="true" aria-label="Đang tải thông tin đơn hàng" />
          </>
        )}

        {order && (
          <>
            <div className="order-key-panel" style={{ marginTop: 0, marginBottom: 16 }}>
              <div className="row">
                <strong>Đơn hàng #{order.id.slice(0, 8)}</strong>
                <span className="spacer" />
                <span className="price">{vnd(order.totalAmount)}</span>
              </div>
              <div className="muted" style={{ marginTop: 4 }}>
                {order.items.length} sản phẩm · {new Date(order.createdAtUtc).toLocaleString("vi-VN")}
              </div>
            </div>

            {order.status === "COMPLETED" && (
              <>
                <p>
                  Giao dịch đã hoàn tất. Bạn có thể hiển thị và sao chép mã kích hoạt ngay bên dưới.
                </p>
                <OrderKeys orderId={order.id} />
              </>
            )}

            {order.status === "PAID" && !gaveUp && (
              <p className="muted">
                Thanh toán đã ghi nhận. Hệ thống đang trích xuất mã kích hoạt cho bạn (tự động cập nhật sau mỗi 2 giây)...
              </p>
            )}

            {order.status === "PAID" && gaveUp && (
              <p className="muted">
                Thanh toán đã ghi nhận nhưng quá trình cấp key mất nhiều thời gian hơn dự kiến. Vui lòng bấm kiểm tra lại hoặc mở mục Đơn hàng của tôi. Không thanh toán lại.
              </p>
            )}

            {order.status === "CANCELED" && (
              <p>
                Đơn hàng đã hủy do giao dịch không hoàn tất. Bạn chưa bị trừ tiền và có thể đặt đơn mới từ trang sản phẩm.
              </p>
            )}

            {order.status === "PENDING" && isVnpCanceled && (
              <p>
                Bạn đã hủy thao tác trên cổng VNPay. Đơn hàng đang giữ trạng thái chờ đối soát; nếu tài khoản chưa bị trừ tiền, bạn có thể đặt đơn mới.
              </p>
            )}

            {order.status === "PENDING" && isVnpFailed && (
              <div className="alert">
                Cổng VNPay báo giao dịch không thành công (Mã phản hồi: {responseCode}). Nếu tài khoản đã bị trừ tiền, vui lòng không thanh toán lại và kiểm tra mục Đơn hàng của tôi.
              </div>
            )}

            {order.status === "PENDING" && !isVnpCanceled && !isVnpFailed && !gaveUp && (
              <p className="muted">
                Đang đồng bộ kết quả đối soát từ VNPay. Tuyệt đối không thanh toán lại để tránh trùng giao dịch.
              </p>
            )}

            {order.status === "PENDING" && !isVnpCanceled && !isVnpFailed && gaveUp && (
              <p className="muted">
                Hệ thống chưa nhận được phản hồi chốt đơn từ VNPay sau 60 giây. Nếu bạn đã chuyển tiền, tuyệt đối không thanh toán lại; hãy bấm kiểm tra lại hoặc theo dõi tại mục Đơn hàng của tôi.
              </p>
            )}

            {order.status === "REFUNDING" && (
              <div className="alert">
                Giao dịch đã ghi nhận nhưng kho tạm hết key. Đơn hàng đang được xử lý hoàn tiền, chưa xác nhận tiền đã hoàn về tài khoản.
              </div>
            )}

            {order.status === "REFUNDED" && (
              <p>
                Đơn hàng đã được hoàn tiền về phương thức thanh toán ban đầu. Rất tiếc về sự bất tiện này.
              </p>
            )}
          </>
        )}

        <div className="row" style={{ marginTop: 24 }}>
          {(gaveUp || error) && (
            <button className="btn" onClick={() => setPollRun((n) => n + 1)}>
              Kiểm tra lại ngay
            </button>
          )}
          {isCanceledView ? (
            <>
              <Link className="btn" to="/">Quay lại cửa hàng</Link>
              <Link className="btn btn-secondary" to="/orders">Kiểm tra đơn hàng</Link>
            </>
          ) : (
            <>
              <Link className={gaveUp || error ? "btn btn-secondary" : "btn"} to="/orders">
                Xem đơn hàng của tôi
              </Link>
              <Link className="btn btn-secondary" to="/">
                {isFailedView ? "Về trang chủ" : "Tiếp tục mua sắm"}
              </Link>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
