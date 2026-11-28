import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getProduct, getProductAvailability } from "../api/products";
import { createOrder } from "../api/orders";
import type { Product } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { ProductArtwork } from "../components/ProductCard";
import { vnd } from "../components/format";

interface ParsedRequirement {
  notes: string[];
  rows: Array<{ label: string; value: string }>;
}

const SPEC_LABELS: Record<string, string> = {
  os: "Hệ điều hành",
  "os *": "Hệ điều hành",
  processor: "Vi xử lý (CPU)",
  cpu: "Vi xử lý (CPU)",
  memory: "Bộ nhớ (RAM)",
  ram: "Bộ nhớ (RAM)",
  graphics: "Card đồ họa (GPU)",
  gpu: "Card đồ họa (GPU)",
  video: "Card đồ họa (GPU)",
  directx: "DirectX",
  storage: "Dung lượng trống",
  "hard drive": "Dung lượng trống",
  network: "Mạng",
  "sound card": "Âm thanh",
  "additional notes": "Ghi chú thêm",
};

function parseRequirements(raw: string): ParsedRequirement {
  const notes: string[] = [];
  const rows: Array<{ label: string; value: string }> = [];
  if (!raw.trim()) return { notes, rows };

  for (const rawLine of raw.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line) continue;
    if (/^(minimum|recommended):?$/i.test(line)) continue;
    const match = line.match(/^([^:]{2,32}):\s*(.+)$/);
    if (match) {
      const key = match[1].trim().toLowerCase();
      const value = match[2].trim();
      if (value) {
        rows.push({
          label: SPEC_LABELS[key] ?? match[1].trim(),
          value,
        });
        continue;
      }
    }
    notes.push(line);
  }
  return { notes, rows };
}

function RequirementCard({ title, raw }: { title: string; raw: string }) {
  const parsed = parseRequirements(raw);
  const hasContent = parsed.rows.length > 0 || parsed.notes.length > 0;
  return (
    <div className="req-card">
      <h3>{title}</h3>
      {!hasContent ? (
        <p className="muted">Chưa có thông tin cấu hình.</p>
      ) : (
        <>
          {parsed.rows.length > 0 && (
            <dl className="req-list">
              {parsed.rows.map((r, idx) => (
                <div className="req-row" key={`${r.label}-${idx}`}>
                  <dt>{r.label}</dt>
                  <dd>{r.value}</dd>
                </div>
              ))}
            </dl>
          )}
          {parsed.notes.length > 0 && (
            <div className="req-notes">
              {parsed.notes.map((n, idx) => (
                <p key={idx} className="muted">{n}</p>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

export function ProductPage() {
  const { id } = useParams();
  const { authenticated, login } = useAuth();
  const [product, setProduct] = useState<Product | null>(null);
  const [availableKeys, setAvailableKeys] = useState<number | null>(null);
  const [quantity, setQuantity] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [lightboxUrl, setLightboxUrl] = useState<string | null>(null);
  const lightboxRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    if (!id) return;
    let active = true;
    setProduct(null);
    setAvailableKeys(null);
    setError(null);
    setQuantity(1);
    setLightboxUrl(null);

    getProduct(id)
      .then((p) => { if (active) setProduct(p); })
      .catch((e: Error) => { if (active) setError(e.message); });

    getProductAvailability(id)
      .then((res) => { if (active) setAvailableKeys(res.available); })
      .catch(() => { /* keep null if availability endpoint unreachable */ });

    return () => { active = false; };
  }, [id]);

  useEffect(() => {
    const dialog = lightboxRef.current;
    if (!dialog) return;
    if (lightboxUrl && !dialog.open) dialog.showModal();
    else if (!lightboxUrl && dialog.open) dialog.close();
  }, [lightboxUrl]);

  const maxOrderQty =
    availableKeys !== null && availableKeys > 0 ? Math.min(10, availableKeys) : 10;
  const outOfStock = availableKeys === 0;

  async function buy() {
    if (!product || outOfStock) return;
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

  if (error && !product) {
    return (
      <div className="container">
        <Link className="back-link" to="/">← Quay lại cửa hàng</Link>
        <div className="alert" style={{ marginTop: 16 }}>{error}</div>
      </div>
    );
  }
  if (!product) {
    return (
      <div className="container">
        <div className="skeleton" style={{ height: 320 }} aria-busy="true" aria-label="Đang tải sản phẩm" />
      </div>
    );
  }

  const steam = product.steam;
  const hasReqs = !!(steam?.minimumRequirements?.trim() || steam?.recommendedRequirements?.trim());
  const backdropUrl = steam?.headerImage || steam?.posterImage;

  return (
    <main className="container game-page">
      <Link className="back-link" to="/">← Cửa hàng</Link>

      <section className="card game-hero">
        {backdropUrl && (
          <div
            className="game-hero-backdrop"
            style={{ backgroundImage: `url(${backdropUrl})` }}
            aria-hidden="true"
          />
        )}
        <div className="game-hero-grid">
          <div className="game-hero-main">
            <div className="game-hero-media">
              <ProductArtwork product={product} />
            </div>
            <div className="game-hero-header">
              <h1>{product.name}</h1>
              <div className="game-tags">
                <span className="game-tag">{steam ? "Steam PC" : product.category}</span>
                {steam?.genres.slice(0, 5).map((g) => (
                  <span className="game-tag" key={g}>{g}</span>
                ))}
              </div>
              <p className="game-summary">
                {product.description || steam?.description || "Chưa có mô tả chi tiết cho sản phẩm này."}
              </p>
            </div>
          </div>

          <aside className="purchase-dock">
            <div className="purchase-price-row">
              <span className="muted">Giá bán</span>
              <strong className="purchase-price">{vnd(product.price)}</strong>
            </div>

            {availableKeys !== null && (
              <div className={`dock-stock ${outOfStock ? "out-of-stock" : "in-stock"}`}>
                <span className="stock-dot" aria-hidden="true" />
                {outOfStock
                  ? "Tạm hết key trong kho"
                  : `Còn ${availableKeys} key sẵn sàng giao ngay`}
              </div>
            )}

            <div className="dock-qty-row">
              <label htmlFor="buy-qty">Số lượng</label>
              <div className="qty-stepper">
                <button
                  type="button"
                  className="btn btn-secondary qty-btn"
                  disabled={busy || outOfStock || quantity <= 1}
                  onClick={() => setQuantity((q) => Math.max(1, q - 1))}
                  aria-label="Giảm số lượng"
                >
                  −
                </button>
                <input
                  id="buy-qty"
                  type="number"
                  min={1}
                  max={maxOrderQty}
                  value={quantity}
                  disabled={busy || outOfStock}
                  onChange={(e) =>
                    setQuantity(Math.min(maxOrderQty, Math.max(1, Number(e.target.value) || 1)))
                  }
                />
                <button
                  type="button"
                  className="btn btn-secondary qty-btn"
                  disabled={busy || outOfStock || quantity >= maxOrderQty}
                  onClick={() => setQuantity((q) => Math.min(maxOrderQty, q + 1))}
                  aria-label="Tăng số lượng"
                >
                  +
                </button>
              </div>
            </div>

            <div className="dock-total-row">
              <span className="muted">Tổng thanh toán</span>
              <strong>{vnd(product.price * quantity)}</strong>
            </div>

            <button
              className="btn dock-buy-btn"
              onClick={buy}
              disabled={busy || outOfStock}
            >
              {busy
                ? "Đang tạo đơn..."
                : outOfStock
                  ? "Tạm hết key"
                  : authenticated
                    ? "Mua và thanh toán qua VNPay"
                    : "Đăng nhập để mua"}
            </button>

            <ul className="dock-notes">
              <li>Giao mã kích hoạt tự động ngay sau khi thanh toán xác nhận</li>
              <li>Mã lưu trữ riêng trong mục Đơn hàng của tài khoản</li>
            </ul>

            {error && <div className="alert" role="alert" style={{ marginTop: 12 }}>{error}</div>}
          </aside>
        </div>
      </section>

      {steam && (
        <>
          <section className="card game-section">
            <h2 className="section-heading">Thông tin phát hành</h2>
            <dl className="game-meta-grid">
              <div>
                <dt>Nhà phát triển</dt>
                <dd>{steam.developers.join(", ") || "Chưa cập nhật"}</dd>
              </div>
              <div>
                <dt>Nhà phát hành</dt>
                <dd>{steam.publishers.join(", ") || "Chưa cập nhật"}</dd>
              </div>
              <div>
                <dt>Ngày phát hành</dt>
                <dd>{steam.releaseDate || "Chưa cập nhật"}</dd>
              </div>
              <div>
                <dt>Trang gốc</dt>
                <dd>
                  <a
                    href={`https://store.steampowered.com/app/${steam.appId}/`}
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    Steam Store (App #{steam.appId}) ↗
                  </a>
                </dd>
              </div>
            </dl>
          </section>

          {(steam.trailerUrl || steam.screenshots.length > 0) && (
            <section className="card game-section">
              <h2 className="section-heading">Hình ảnh & Trailer</h2>
              {steam.trailerUrl && (
                <video className="game-trailer" controls preload="none" src={steam.trailerUrl} />
              )}
              {steam.screenshots.length > 0 && (
                <div className="steam-gallery">
                  {steam.screenshots.map((url, i) => (
                    <button
                      type="button"
                      key={url}
                      className="gallery-thumb"
                      onClick={() => setLightboxUrl(url)}
                      aria-label={`Xem ảnh trong game ${i + 1}`}
                    >
                      <img src={url} alt={`${product.name} screenshot ${i + 1}`} loading="lazy" />
                    </button>
                  ))}
                </div>
              )}
            </section>
          )}

          <section className="card game-section">
            <h2 className="section-heading">Cấu hình PC yêu cầu</h2>
            {hasReqs ? (
              <div className="req-grid">
                <RequirementCard title="Cấu hình tối thiểu" raw={steam.minimumRequirements} />
                <RequirementCard title="Cấu hình khuyến nghị" raw={steam.recommendedRequirements} />
              </div>
            ) : (
              <p className="muted">
                Cấu hình PC đang chờ đồng bộ tự động (hãy cấu hình <code>RAWG_API_KEY</code> hoặc kết nối Steam Store API trong backend rồi tải lại trang).
              </p>
            )}
          </section>
        </>
      )}

      <dialog
        ref={lightboxRef}
        className="lightbox-dialog"
        onClose={() => setLightboxUrl(null)}
        onClick={(e) => { if (e.target === e.currentTarget) setLightboxUrl(null); }}
      >
        <button
          type="button"
          className="dialog-close"
          aria-label="Đóng ảnh"
          onClick={() => setLightboxUrl(null)}
        >
          ×
        </button>
        {lightboxUrl && <img src={lightboxUrl} alt={product.name} />}
      </dialog>
    </main>
  );
}
