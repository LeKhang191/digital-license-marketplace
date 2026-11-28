
import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { getProduct } from "../api/products";
import type { Product } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { useCart } from "../cart/CartContext";
import { ProductArtwork } from "../components/ProductCard";
import { vnd } from "../components/format";
import { useLanguage } from "../i18n/LanguageContext";

export function ProductPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { addItem } = useCart();
  const { t } = useLanguage();

  const {
    ready,
    authenticated,
    isCustomer,
  } = useAuth();

  const [product, setProduct] = useState<Product | null>(null);
  const [quantity, setQuantity] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [addedNotice, setAddedNotice] = useState(false);

  const noticeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    setAddedNotice(false);
    return () => {
      if (noticeTimer.current !== null) clearTimeout(noticeTimer.current);
      noticeTimer.current = null;
    };
  }, [id]);

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

    if (authenticated && !isCustomer) {
      setError("Chỉ khách hàng mới có thể mua sản phẩm.");
      return;
    }

    navigate(`/checkout?productId=${encodeURIComponent(product.id)}&qty=${quantity}`);
  }

  function addToCart() {
    if (!product || !ready || (authenticated && !isCustomer)) return;
    addItem(product, quantity);
    setAddedNotice(true);
    if (noticeTimer.current !== null) clearTimeout(noticeTimer.current);
    noticeTimer.current = setTimeout(() => {
      setAddedNotice(false);
      noticeTimer.current = null;
    }, 5000);
    setError(null);
  }

  const canPurchase =
    ready && (!authenticated || isCustomer);

  const total = product ? product.price * quantity : 0;

  return (
    <div className="container">
      <div className="product-back">
        <Link to="/" className="btn btn-secondary">
          {t("← Quay về cửa hàng", "← Back to Store")}
        </Link>
      </div>

      {error && !product ? (
        <div className="alert">{error}</div>
      ) : !product ? (
        <p className="muted">{t("Đang tải...", "Loading...")}</p>
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
            {product.description || t("Chưa có mô tả.", "No description available.")}
          </p>

          <p className="price" style={{ fontSize: 24 }}>
            {vnd(product.price)}
          </p>

          {!ready ? (
            <p className="muted">
              {t("Đang kiểm tra quyền truy cập...", "Checking permissions...")}
            </p>
          ) : canPurchase ? (
            <>
              <div className="purchase-actions">
                <div className="quantity-control">
                  <label htmlFor="purchase-quantity">{t("Số lượng", "Quantity")}</label>
                  <div className="quantity-stepper">
                    <button type="button" aria-label={t("Giảm số lượng", "Decrease quantity")} disabled={quantity <= 1}
                      onClick={() => setQuantity(n => Math.max(1, n - 1))}>−</button>
                    <input id="purchase-quantity" type="number" min={1} max={10} step={1}
                      value={quantity}
                      onChange={e => setQuantity(Math.min(10, Math.max(1, Math.trunc(Number(e.target.value) || 1))))} />
                    <button type="button" aria-label={t("Tăng số lượng", "Increase quantity")} disabled={quantity >= 10}
                      onClick={() => setQuantity(n => Math.min(10, n + 1))}>+</button>
                  </div>
                </div>

                <button
                  type="button"
                  className="btn purchase-btn"
                  onClick={buyNow}
                >
                  {t("Mua ngay", "Buy Now")}
                </button>

                <button
                  type="button"
                  className="btn btn-secondary purchase-btn"
                  onClick={addToCart}
                >
                  {t("Thêm vào giỏ", "Add to Cart")}
                </button>
              </div>

              <div className="cart-added-slot" role="status">
                <div className="cart-added-inline" hidden={!addedNotice}>
                  {t("Đã thêm vào giỏ hàng.", "Added to cart.")}{" "}
                  <Link to="/cart">{t("Xem giỏ hàng →", "View cart →")}</Link>
                </div>
              </div>

              <div className="purchase-total" aria-live="polite">
                <strong>{t("Tổng thanh toán:", "Total:")} </strong>

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
              {t(
                "Tài khoản nhân viên chỉ được xem thông tin sản phẩm.",
                "Staff accounts have view-only access to products.",
              )}
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
