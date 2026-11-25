
import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getProduct } from "../api/products";
import { createOrder } from "../api/orders";
import type { Product } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { vnd } from "../components/format";

export function ProductPage() {
  const { id } = useParams();

  const {
    ready,
    authenticated,
    isCustomer,
    login,
  } = useAuth();

  const [product, setProduct] = useState<Product | null>(null);
  const [quantity, setQuantity] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;

    setProduct(null);
    setError(null);
    setQuantity(1);

    if (!id) return;

    getProduct(id)
      .then((data) => {
        if (!cancelled) setProduct(data);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      });

    return () => {
      cancelled = true;
    };
  }, [id]);

  async function buy() {
    if (!product || !ready || busy) return;

    if (!authenticated) {
      login();
      return;
    }

    if (!isCustomer) {
      setError("Chỉ khách hàng mới có thể mua sản phẩm.");
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const order = await createOrder(product.id, quantity);

      if (order.paymentUrl) {
        window.location.href = order.paymentUrl;
      } else {
        setError("Không nhận được link thanh toán.");
      }
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "Không thể tạo đơn hàng."
      );
    } finally {
      setBusy(false);
    }
  }

  const canPurchase =
    ready && (!authenticated || isCustomer);

  const total = product ? product.price * quantity : 0;

  return (
    <div className="container">
      <div className="product-back">
        <Link to="/" className="btn btn-secondary">
          ← Quay về cửa hàng
        </Link>
      </div>

      {error && !product ? (
        <div className="alert">{error}</div>
      ) : !product ? (
        <p className="muted">Đang tải...</p>
      ) : (
        <div className="card">
          <div className="muted">
            {product.category}
          </div>

          <h1>{product.name}</h1>

          <p>
            {product.description || "Chưa có mô tả."}
          </p>

          <p className="price" style={{ fontSize: 24 }}>
            {vnd(product.price)}
          </p>

          {!ready ? (
            <p className="muted">
              Đang kiểm tra quyền truy cập...
            </p>
          ) : canPurchase ? (
            <>
              <div className="row">
                <label>
                  Số lượng{" "}
                  <input
                    type="number"
                    min={1}
                    max={10}
                    step={1}
                    value={quantity}
                    disabled={busy}
                    onChange={(e) =>
                      setQuantity(
                        Math.min(
                          10,
                          Math.max(
                            1,
                            Math.trunc(
                              Number(e.target.value) || 1
                            )
                          )
                        )
                      )
                    }
                    style={{ width: 72 }}
                  />
                </label>

                <button
                  className="btn"
                  onClick={buy}
                  disabled={busy}
                >
                  {busy
                    ? "Đang tạo đơn..."
                    : authenticated
                      ? "Mua và thanh toán"
                      : "Đăng nhập để mua"}
                </button>
              </div>

              <div style={{ marginTop: 20 }}>
                <strong>Tổng thanh toán: </strong>

                <span
                  className="price"
                  style={{ fontSize: 24 }}
                >
                  {vnd(total)}
                </span>
              </div>
            </>
          ) : (
            <p className="muted">
              Tài khoản nhân viên chỉ được xem
              thông tin sản phẩm.
            </p>
          )}

          {error && (
            <div
              className="alert"
              style={{ marginTop: 16 }}
            >
              {error}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
