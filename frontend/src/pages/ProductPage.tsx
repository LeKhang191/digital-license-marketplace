import { useEffect, useState } from "react";
import {Link, useParams } from "react-router-dom";
import { getProduct } from "../api/products";
import { createOrder } from "../api/orders";
import type { Product } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { vnd } from "../components/format";

export function ProductPage() {
  const { id } = useParams();
  const { authenticated, login } = useAuth();
  const [product, setProduct] = useState<Product | null>(null);
  const [quantity, setQuantity] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!id) return;
    getProduct(id).then(setProduct).catch((e: Error) => setError(e.message));
  }, [id]);

  async function buy() {
    if (!product) return;
    if (!authenticated) return login();

    setBusy(true);
    setError(null);
    try {
      const order = await createOrder(product.id, quantity);
      // Chuyển sang cổng thanh toán VNPay
      if (order.paymentUrl) window.location.href = order.paymentUrl;
      else setError("Không nhận được link thanh toán.");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  if (error && !product) return <div className="container"><div className="alert">{error}</div></div>;
  if (!product) return <div className="container"><p className="muted">Đang tải...</p></div>;
    
  const total = product.price * quantity;

  return (
    <div className="container">
      <div className="product-back">
        <Link to="/" className="btn btn-secondary">
          ← Quay về cửa hàng
        </Link>
      </div>
      <div className="card">
        <div className="muted">{product.category}</div>
        <h1>{product.name}</h1>
        <p>{product.description || "Chưa có mô tả."}</p>
        <p className="price" style={{ fontSize: 24 }}>{vnd(product.price)}</p>

        <div className="row">
          <label>
            Số lượng{" "}
            <input type="number" min={1} max={10} value={quantity}
              onChange={(e) =>
                setQuantity(
                  Math.min(10, Math.max(1, Math.trunc(Number(e.target.value) || 1)))
                )
              }
              style={{ width: 72 }} />
          </label>
          <button className="btn" onClick={buy} disabled={busy}>
            {busy ? "Đang tạo đơn..." : authenticated ? "Mua và thanh toán" : "Đăng nhập để mua"}
          </button>
        </div>

        {error && <div className="alert" style={{ marginTop: 16 }}>{error}</div>}
        <div style={{ marginTop: 20 }}>
          <strong>Tổng thanh toán: </strong>
          <span className="price" style={{ fontSize: 24 }}>
            {vnd(total)}
          </span>
        </div>
      </div>
    </div>
  );
}
