import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { createMultiOrder, payOrder, type PaymentMethod } from "../api/orders";
import { getProduct } from "../api/products";
import { getMyWallet, type WalletBalance } from "../api/wallet";
import { useAuth } from "../auth/AuthProvider";
import { useCart, type CartItem } from "../cart/CartContext";
import { vnd } from "../components/format";

export function CheckoutPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const { ready, authenticated, isCustomer, login } = useAuth();
  const { items: cartItems, clearCart } = useCart();

  const buyNowId = params.get("productId") || "";
  const buyNowQty = Math.min(10, Math.max(1, Math.trunc(Number(params.get("qty")) || 1)));
  const isBuyNow = Boolean(buyNowId);

  const [buyNowItem, setBuyNowItem] = useState<CartItem | null>(null);
  const [loadingProduct, setLoadingProduct] = useState(isBuyNow);
  const [wallet, setWallet] = useState<WalletBalance | null>(null);
  const [walletLoading, setWalletLoading] = useState(true);
  const [method, setMethod] = useState<PaymentMethod>("WALLET");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const submittingRef = useRef(false);

  useEffect(() => {
    if (!buyNowId) {
      setLoadingProduct(false);
      setBuyNowItem(null);
      return;
    }
    let cancelled = false;
    setLoadingProduct(true);
    setError("");
    getProduct(buyNowId)
      .then((p) => {
        if (!cancelled) {
          setBuyNowItem({
            productId: p.id,
            name: p.name,
            price: p.price,
            image: p.steam?.posterImage || p.steam?.headerImage || undefined,
            quantity: buyNowQty,
          });
        }
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message || "Không tải được thông tin sản phẩm.");
      })
      .finally(() => {
        if (!cancelled) setLoadingProduct(false);
      });
    return () => {
      cancelled = true;
    };
  }, [buyNowId, buyNowQty]);

  useEffect(() => {
    if (!ready || !authenticated || !isCustomer) {
      setWalletLoading(false);
      return;
    }
    let cancelled = false;
    setWalletLoading(true);
    getMyWallet()
      .then((w) => {
        if (!cancelled) setWallet(w);
      })
      .catch(() => {
        if (!cancelled) setWallet(null);
      })
      .finally(() => {
        if (!cancelled) setWalletLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [ready, authenticated, isCustomer]);

  const items: CartItem[] = isBuyNow ? (buyNowItem ? [buyNowItem] : []) : cartItems;
  const totalCount = items.reduce((sum, i) => sum + i.quantity, 0);
  const totalAmount = items.reduce((sum, i) => sum + i.price * i.quantity, 0);
  const insufficientWallet = method === "WALLET" && wallet !== null && wallet.balance < totalAmount;

  async function submitCheckout() {
    if (busy || submittingRef.current || !ready || !authenticated || !isCustomer || items.length === 0) return;
    if (totalCount > 20) {
      setError("Mỗi đơn hàng tối đa 20 key.");
      return;
    }

    submittingRef.current = true;
    setBusy(true);
    setError("");

    try {
      const payload = items.map((i) => ({ productId: i.productId, quantity: i.quantity }));
      const order = await createMultiOrder(payload, method);

      if (method === "WALLET") {
        const paid = await payOrder(order.id);
        if (!isBuyNow) clearCart();
        navigate(`/payment/result?orderId=${encodeURIComponent(paid.id)}`);
        return;
      }

      if (order.paymentUrl) {
        if (!isBuyNow) clearCart();
        window.location.href = order.paymentUrl;
        return;
      }
      setError("Không nhận được liên kết thanh toán VNPay.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không thể hoàn tất thanh toán.");
    }
    submittingRef.current = false;
    setBusy(false);
  }

  if (!ready || loadingProduct) {
    return (
      <div className="container">
        <p className="muted">Đang tải thông tin thanh toán...</p>
      </div>
    );
  }

  if (!authenticated) {
    return (
      <div className="container">
        <div className="card">
          <h1>Thanh toán đơn hàng</h1>
          <p>Vui lòng đăng nhập bằng tài khoản khách hàng để tiếp tục thanh toán.</p>
          <button type="button" className="btn" style={{ marginTop: 12 }} onClick={login}>
            Đăng nhập
          </button>
        </div>
      </div>
    );
  }

  if (!isCustomer) {
    return (
      <div className="container">
        <div className="alert">Tài khoản nhân viên không thể mua hàng.</div>
        <Link to="/" className="btn btn-secondary" style={{ marginTop: 12 }}>
          ← Quay về cửa hàng
        </Link>
      </div>
    );
  }

  return (
    <div className="container">
      <div className="product-back">
        <Link to={isBuyNow ? `/products/${buyNowId}` : "/cart"} className="btn btn-secondary">
          ← {isBuyNow ? "Quay lại sản phẩm" : "Quay lại giỏ hàng"}
        </Link>
      </div>

      <h1>Xác nhận &amp; Thanh toán</h1>

      {error && (
        <div className="alert" style={{ marginBottom: 16 }}>
          {error}
        </div>
      )}

      {items.length === 0 ? (
        <section className="card">
          <p className="muted">Không có sản phẩm nào để thanh toán.</p>
          <Link to="/" className="btn" style={{ marginTop: 12 }}>
            Về cửa hàng
          </Link>
        </section>
      ) : (
        <div className="checkout-layout">
          <div className="checkout-items">
            <section className="card">
              <h2>Thông tin đơn hàng</h2>
              <div className="receipt-lines" style={{ marginTop: 12 }}>
                {items.map((item) => (
                  <div className="receipt-line" key={item.productId}>
                    <div>
                      <strong>{item.name}</strong>
                      <div className="muted">
                        {item.quantity} × {vnd(item.price)}
                      </div>
                    </div>
                    <strong>{vnd(item.price * item.quantity)}</strong>
                  </div>
                ))}
              </div>
            </section>

            <section className="card" style={{ marginTop: 16 }}>
              <h2>Chọn phương thức thanh toán</h2>
              <div className="payment-methods">
                <label className={`payment-option${method === "WALLET" ? " selected" : ""}`}>
                  <input
                    type="radio"
                    name="paymentMethod"
                    value="WALLET"
                    checked={method === "WALLET"}
                    disabled={busy}
                    onChange={() => setMethod("WALLET")}
                  />
                  <div>
                    <strong>Ví DLM (Marketplace Balance)</strong>
                    <div className="muted">
                      Số dư khả dụng:{" "}
                      {walletLoading
                        ? "Đang tải..."
                        : wallet
                          ? vnd(wallet.balance)
                          : "Không tải được"}
                    </div>
                    {insufficientWallet && (
                      <div className="license-disabled">
                        Số dư ví không đủ.{" "}
                        <Link to="/account">Kiểm tra Ví &amp; Cài đặt</Link>
                      </div>
                    )}
                  </div>
                </label>

                <label className={`payment-option${method === "VNPAY" ? " selected" : ""}`}>
                  <input
                    type="radio"
                    name="paymentMethod"
                    value="VNPAY"
                    checked={method === "VNPAY"}
                    disabled={busy}
                    onChange={() => setMethod("VNPAY")}
                  />
                  <div>
                    <strong>VNPay Sandbox</strong>
                    <div className="muted">
                      Chuyển sang cổng thanh toán VNPay để xác nhận giao dịch.
                    </div>
                  </div>
                </label>
              </div>
            </section>
          </div>

          <aside className="card checkout-summary">
            <h2>Tổng kết thanh toán</h2>
            <div className="receipt-lines" style={{ marginTop: 10 }}>
              <div className="receipt-line">
                <span className="muted">Số lượng key</span>
                <strong>{totalCount}</strong>
              </div>
              <div className="receipt-line">
                <span className="muted">Phương thức</span>
                <strong>{method === "WALLET" ? "Ví DLM" : "VNPay Sandbox"}</strong>
              </div>
              {method === "WALLET" && wallet && (
                <div className="receipt-line">
                  <span className="muted">Số dư sau thanh toán</span>
                  <strong>{vnd(wallet.balance - totalAmount)}</strong>
                </div>
              )}
            </div>

            <div className="receipt-total">
              <span>Tổng thanh toán</span>
              <strong className="price">{vnd(totalAmount)}</strong>
            </div>

            <button
              type="button"
              className="btn"
              style={{ width: "100%", marginTop: 16 }}
              disabled={busy || totalCount > 20 || insufficientWallet}
              onClick={() => void submitCheckout()}
            >
              {busy
                ? "Đang xử lý thanh toán..."
                : method === "WALLET"
                  ? "Thanh toán bằng Ví DLM"
                  : "Tiếp tục tới VNPay"}
            </button>
          </aside>
        </div>
      )}
    </div>
  );
}
