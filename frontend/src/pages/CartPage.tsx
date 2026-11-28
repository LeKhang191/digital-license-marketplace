import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { useCart } from "../cart/CartContext";
import { vnd } from "../components/format";
import { useLanguage } from "../i18n/LanguageContext";

export function CartPage() {
  const navigate = useNavigate();
  const { t } = useLanguage();
  const { ready, authenticated, isCustomer } = useAuth();
  const { items, totalCount, totalAmount, updateQuantity, removeItem, clearCart } = useCart();

  const overLimit = totalCount > 20;
  if (!ready) return <div className="container"><p role="status">Đang kiểm tra tài khoản...</p></div>;
  if (authenticated && !isCustomer) return <div className="container"><div className="alert">Tài khoản nhân viên không thể sử dụng giỏ hàng.</div><Link to="/">Quay về cửa hàng</Link></div>;

  return (
    <div className="container">
      <div className="product-back">
        <Link to="/" className="btn btn-secondary">
          {t("← Tiếp tục mua sắm", "← Continue Shopping")}
        </Link>
      </div>

      <h1>{t("Giỏ hàng", "Shopping Cart")}</h1>

      {items.length === 0 ? (
        <section className="card">
          <p className="muted">Giỏ hàng của bạn đang trống.</p>
          <p style={{ marginTop: 12 }}>
            <Link to="/" className="btn">
              Khám phá cửa hàng
            </Link>
          </p>
        </section>
      ) : (
        <div className="checkout-layout">
          <div className="checkout-items">
            {items.map((item) => (
              <div className="card cart-item" key={item.productId}>
                {item.image && (
                  <img src={item.image} alt="" className="cart-thumb" loading="lazy" />
                )}
                <div className="cart-info">
                  <Link to={`/products/${item.productId}`}>
                    <strong>{item.name}</strong>
                  </Link>
                  <div className="muted">{vnd(item.price)} / key</div>
                </div>
                <div className="quantity-stepper">
                  <button
                    type="button"
                    aria-label={`Giảm số lượng ${item.name}`}
                    disabled={item.quantity <= 1}
                    onClick={() => updateQuantity(item.productId, item.quantity - 1)}
                  >
                    −
                  </button>
                  <input
                    type="number"
                    min={1}
                    max={10}
                    value={item.quantity}
                    aria-label={`Số lượng ${item.name}`}
                    onChange={(e) =>
                      updateQuantity(item.productId, Number(e.target.value) || 1)
                    }
                  />
                  <button
                    type="button"
                    aria-label={`Tăng số lượng ${item.name}`}
                    disabled={item.quantity >= 10}
                    onClick={() => updateQuantity(item.productId, item.quantity + 1)}
                  >
                    +
                  </button>
                </div>
                <div className="cart-subtotal">
                  <strong>{vnd(item.price * item.quantity)}</strong>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => removeItem(item.productId)}
                  >
                    Xóa
                  </button>
                </div>
              </div>
            ))}
          </div>

          <aside className="card checkout-summary">
            <h2>Tóm tắt giỏ hàng</h2>
            <div className="receipt-lines">
              <div className="receipt-line">
                <span className="muted">Tổng số lượng key</span>
                <strong>{totalCount}</strong>
              </div>
              <div className="receipt-line">
                <span className="muted">Tạm tính</span>
                <strong>{vnd(totalAmount)}</strong>
              </div>
            </div>

            {overLimit && (
              <div className="alert" style={{ marginTop: 12 }}>
                Mỗi đơn hàng chỉ hỗ trợ tối đa 20 key. Vui lòng giảm bớt số lượng.
              </div>
            )}

            {!ready ? (
              <p className="muted" style={{ marginTop: 14 }}>Đang kiểm tra tài khoản...</p>
            ) : !authenticated ? (
              <button
                type="button"
                className="btn"
                style={{ width: "100%", marginTop: 16 }}
                disabled={overLimit}
                onClick={() => navigate("/checkout")}
              >
                Đăng nhập để thanh toán
              </button>
            ) : !isCustomer ? (
              <p className="muted" style={{ marginTop: 14 }}>
                Tài khoản nhân viên không thể thực hiện mua hàng.
              </p>
            ) : (
              <button
                type="button"
                className="btn"
                style={{ width: "100%", marginTop: 16 }}
                disabled={overLimit}
                onClick={() => navigate("/checkout")}
              >
                Tiến hành thanh toán
              </button>
            )}

            <button
              type="button"
              className="btn btn-secondary"
              style={{ width: "100%", marginTop: 10 }}
              onClick={clearCart}
            >
              Xóa toàn bộ giỏ hàng
            </button>
          </aside>
        </div>
      )}
    </div>
  );
}
