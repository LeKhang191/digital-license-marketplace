
import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { getProduct } from "../api/products";
import type { Product } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { useCart } from "../cart/CartContext";
import { ProductArtwork } from "../components/ProductCard";
import { vnd } from "../components/format";

export function ProductPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { addItem } = useCart();

  const {
    ready,
    authenticated,
    isCustomer,
    login,
  } = useAuth();

  const [product, setProduct] = useState<Product | null>(null);
  const [quantity, setQuantity] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [addedNotice, setAddedNotice] = useState(false);

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

  function buyNow() {
    if (!product || !ready) return;

    if (!authenticated) {
      login();
      return;
    }

    if (!isCustomer) {
      setError("Chỉ khách hàng mới có thể mua sản phẩm.");
      return;
    }

    navigate(`/checkout?productId=${encodeURIComponent(product.id)}&qty=${quantity}`);
  }

  function addToCart() {
    if (!product || !ready) return;
    addItem(product, quantity);
    setAddedNotice(true);
    setError(null);
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
        <>
        <div className="detail-layout">
        <ProductArtwork product={product}/>
        <div className="card detail-card">
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
                <div className="quantity-control">
                  <label htmlFor="purchase-quantity">Số lượng</label>
                  <div className="quantity-stepper">
                    <button type="button" aria-label="Giảm số lượng" disabled={quantity <= 1}
                      onClick={() => setQuantity(n => Math.max(1, n - 1))}>−</button>
                    <input id="purchase-quantity" type="number" min={1} max={10} step={1}
                      value={quantity}
                      onChange={e => setQuantity(Math.min(10, Math.max(1, Math.trunc(Number(e.target.value) || 1))))} />
                    <button type="button" aria-label="Tăng số lượng" disabled={quantity >= 10}
                      onClick={() => setQuantity(n => Math.min(10, n + 1))}>+</button>
                  </div>
                </div>

                <button
                  type="button"
                  className="btn"
                  onClick={buyNow}
                >
                  {authenticated
                    ? "Mua ngay"
                    : "Đăng nhập để mua"}
                </button>

                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={addToCart}
                >
                  Thêm vào giỏ
                </button>
              </div>

              {addedNotice && (
                <p role="status" className="kinguin-ok" style={{ marginTop: 10 }}>
                  Đã thêm vào giỏ hàng. <Link to="/cart">Xem giỏ hàng →</Link>
                </p>
              )}

              <div className="purchase-total" aria-live="polite">
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
        </div>
      {product.steam && <section className="steam-details card">
        <h2>Thông tin game</h2><p>Nguồn metadata: {product.steam.source ?? "Steam"}</p>
        <p>{product.steam.genres.join(" · ")}</p>
        <p>Nhà phát triển: {product.steam.developers.join(", ") || "—"}</p>
        <p>Nhà phát hành: {product.steam.publishers.join(", ") || "—"}</p>
        <p>Ngày phát hành: {product.steam.releaseDate || "—"}</p>
        <a href={`https://store.steampowered.com/app/${product.steam.appId}/`} target="_blank" rel="noopener noreferrer">Steam Store ↗</a>
        {product.steam.trailerUrl && <video controls preload="none" src={product.steam.trailerUrl} />}
        {!!product.steam.screenshots.length && <><h2>Hình ảnh trong game</h2><div className="steam-gallery">{product.steam.screenshots.map((url,i)=><a key={i} href={url} target="_blank" rel="noopener noreferrer"><img src={url} alt={`Screenshot ${i+1}`} loading="lazy"/></a>)}</div></>}
        <h2>Cấu hình PC</h2><div className="steam-requirements"><div><h3>Tối thiểu</h3><p>{product.steam.minimumRequirements || "Chưa có thông tin"}</p></div><div><h3>Khuyến nghị</h3><p>{product.steam.recommendedRequirements || "Chưa có thông tin"}</p></div></div>
      </section>}

        </>
      )}
    </div>
  );
}
