import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { listMyLicenses, revealMyLicense } from "../api/licenses";
import { getProduct } from "../api/products";
import type { CustomerLicense } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { RefreshButton } from "../components/RefreshButton";
import { useLanguage } from "../i18n/LanguageContext";

function LicenseCard({
  item,
  name,
}: {
  item: CustomerLicense;
  name: string;
}) {
  const [key, setKey] = useState<string | null>(null);
  const [revealedAt, setRevealedAt] = useState<string | null>(
    item.revealedAtUtc ?? null,
  );
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [confirmOpen, setConfirmOpen] = useState(false);
  const confirmDialog = useRef<HTMLDialogElement>(null);

  const isRevealed = Boolean(revealedAt || item.isRevealed || key !== null);

  useEffect(() => {
    const d = confirmDialog.current;
    if (!d) return;
    if (confirmOpen && !d.open) d.showModal();
    else if (!confirmOpen && d.open) d.close();
  }, [confirmOpen]);

  async function doReveal() {
    if (busy) return;
    setConfirmOpen(false);
    setBusy(true);
    setMessage("");

    try {
      const result = await revealMyLicense(item.id);
      setKey(result.licenseKey);
      setRevealedAt(result.revealedAtUtc ?? new Date().toISOString());
    } catch (e) {
      setMessage(
        e instanceof Error
          ? e.message
          : "Không thể tải License Key."
      );
    } finally {
      setBusy(false);
    }
  }

  function handleToggleReveal() {
    if (busy) return;
    if (key !== null) {
      setKey(null);
      setMessage("");
      return;
    }
    setConfirmOpen(true);
  }

  async function copy() {
    if (!key) return;

    try {
      await navigator.clipboard.writeText(key);
      setMessage("Đã sao chép License Key.");
    } catch {
      setMessage("Vui lòng sao chép Key thủ công.");
    }
  }

  return (
    <div className="card">
      <div className="row" style={{ justifyContent: "space-between", alignItems: "center" }}>
        <h2 style={{ margin: 0 }}>{name}</h2>
        <span
          className={
            isRevealed
              ? "license-status assigned"
              : "license-status available"
          }
        >
          {isRevealed ? "Đã xem key" : "Chưa mở key · Có thể yêu cầu hoàn tiền"}
        </span>
      </div>

      <p className="muted" style={{ marginTop: 8 }}>
        Đơn hàng: <code>{item.orderId.slice(0, 8)}</code>
        {revealedAt && (
          <>
            {" "}· Xem key lúc:{" "}
            {new Date(revealedAt).toLocaleString("vi-VN")}
          </>
        )}
      </p>

      <p className="muted">
        Ngày cấp:{" "}
        {new Date(item.assignedAtUtc).toLocaleString("vi-VN")}
      </p>

      <p>
        <code style={{ overflowWrap: "anywhere" }}>
          {key ?? "•••••-•••••-•••••"}
        </code>
      </p>

      <div className="row">
        <button
          className="btn"
          disabled={busy}
          onClick={handleToggleReveal}
        >
          {busy ? "Đang tải..." : key ? "Ẩn Key" : "Hiện Key"}
        </button>

        <button
          className="btn btn-secondary"
          disabled={!key}
          onClick={() => void copy()}
        >
          Sao chép
        </button>

        {isRevealed ? (
          <Link
            className="btn btn-secondary"
            to={`/support?category=INVALID_KEY&orderId=${encodeURIComponent(item.orderId)}&licenseId=${encodeURIComponent(item.id)}`}
          >
            Báo key lỗi / Hoàn tiền
          </Link>
        ) : (
          <Link
            className="btn btn-secondary"
            to={`/support?category=REFUND_REQUEST&orderId=${encodeURIComponent(item.orderId)}`}
          >
            Yêu cầu hoàn tiền (chưa mở key)
          </Link>
        )}
      </div>

      {message && <p role="status">{message}</p>}

      <p className="muted">
        Nếu Key gặp vấn đề, hãy lưu mã đơn hàng
        để liên hệ hỗ trợ.
      </p>

      <dialog
        ref={confirmDialog}
        className="help-dialog"
        onCancel={() => setConfirmOpen(false)}
        onClick={(e) => {
          if (e.target === e.currentTarget) setConfirmOpen(false);
        }}
      >
        <div className="dialog-content kinguin-order">
          <button
            type="button"
            className="dialog-close"
            aria-label="Đóng"
            onClick={() => setConfirmOpen(false)}
          >
            ×
          </button>
          <h2>Xác nhận hiển thị License Key</h2>
          <p>
            Bạn sắp hiển thị key cho <strong>{name}</strong> (Đơn{" "}
            <code>{item.orderId.slice(0, 8)}</code>).
          </p>
          <p className="muted">
            Chính sách hoàn tiền: Sau khi hiển thị key, giao dịch được xem là đã bàn giao mã kích hoạt. Key lỗi sẽ được hỗ trợ kiểm tra/đổi key mới hoặc hoàn tiền nếu kho hết key thay thế.
          </p>
          <div className="supplier-dialog-actions">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => setConfirmOpen(false)}
            >
              Hủy
            </button>
            <button
              type="button"
              className="btn"
              onClick={() => void doReveal()}
            >
              Tôi hiểu, hiện Key
            </button>
          </div>
        </div>
      </dialog>
    </div>
  );
}

export function MyLicensesPage() {
  const { username } = useAuth();
  const { t } = useLanguage();
  const [params] = useSearchParams();
  const orderId = params.get("orderId") || undefined;

  const [items, setItems] =
    useState<CustomerLicense[] | null>(null);

  const [names, setNames] =
    useState<Record<string, string>>({});

  const [error, setError] = useState("");
  const [version, setVersion] = useState(0);

  useEffect(() => {
    let cancelled = false;

    setItems(null);
    setNames({});
    setError("");

    async function load() {
      try {
        const data = await listMyLicenses(orderId);

        if (cancelled) return;

        setItems(data);

        const ids = [...new Set(
          data.map(item => item.productId)
        )];

        const entries = await Promise.all(
          ids.map(async id => {
            try {
              const product = await getProduct(id);
              return [id, product.name] as const;
            } catch {
              return [
                id,
                `Sản phẩm ${id.slice(0, 8)}`
              ] as const;
            }
          })
        );

        if (!cancelled)
          setNames(Object.fromEntries(entries));

      } catch (e) {
        if (!cancelled)
          setError(
            e instanceof Error
              ? e.message
              : "Không thể tải License."
          );
      }
    }

    void load();

    return () => {
      cancelled = true;
    };
  }, [orderId, version, username]);

  return (
    <div className="container">
      <h1>{t("License của tôi", "My Licenses")}</h1>

      <div className="row" style={{ alignItems: "center" }}>
        <Link
          to="/orders"
          className="btn btn-secondary"
        >
          {t("Đơn hàng của tôi", "My Orders")}
        </Link>

        <RefreshButton
          loading={!items && !error}
          onClick={() => {
            setItems(null);
            setVersion(v => v + 1);
          }}
        />

        {orderId && (
          <Link to="/my-licenses">
            {t("Xem tất cả License", "View all Licenses")}
          </Link>
        )}
      </div>

      {error && (
        <p className="alert">
          {error} — {t("Vui lòng thử lại.", "Please try again.")}
        </p>
      )}

      {!items && !error && (
        <p>{t("Đang tải License...", "Loading licenses...")}</p>
      )}

      {items?.length === 0 && (
        <p>
          {t(
            "Chưa có License sẵn sàng. Vui lòng kiểm tra trạng thái đơn hàng.",
            "No licenses ready yet. Please check your order status.",
          )}
        </p>
      )}

      {items && (
        <p style={{ marginBottom: 14 }}>
          {t("Tổng License:", "Total Licenses:")} {items.length}
        </p>
      )}

      <div
        className="grid"
        style={{ gridTemplateColumns: "1fr" }}
      >
        {items?.map(item => (
          <LicenseCard
            key={`${username}-${orderId}-${version}-${item.id}`}
            item={item}
            name={
              names[item.productId] ??
              `Sản phẩm ${item.productId.slice(0, 8)}`
            }
          />
        ))}
      </div>
    </div>
  );
}
