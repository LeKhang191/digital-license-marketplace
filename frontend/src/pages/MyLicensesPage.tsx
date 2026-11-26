import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { listMyLicenses, revealMyLicense } from "../api/licenses";
import { getProduct } from "../api/products";
import type { CustomerLicense } from "../api/types";
import { useAuth } from "../auth/AuthProvider";

function LicenseCard({
  item,
  name,
}: {
  item: CustomerLicense;
  name: string;
}) {
  const [key, setKey] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  async function reveal() {
    if (busy) return;

    if (key !== null) {
      setKey(null);
      setMessage("");
      return;
    }

    setBusy(true);
    setMessage("");

    try {
      const result = await revealMyLicense(item.id);
      setKey(result.licenseKey);
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
      <h2>{name}</h2>

      <p className="muted">
        Đơn hàng: {item.orderId.slice(0, 8)}
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
          onClick={() => void reveal()}
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
      </div>

      {message && <p role="status">{message}</p>}

      <p className="muted">
        Nếu Key gặp vấn đề, hãy lưu mã đơn hàng
        để liên hệ hỗ trợ.
      </p>
    </div>
  );
}

export function MyLicensesPage() {
  const { username } = useAuth();
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
      <h1>License của tôi</h1>

      <div className="row">
        <Link
          to="/orders"
          className="btn btn-secondary"
        >
          Đơn hàng của tôi
        </Link>

        <button
          className="btn btn-secondary"
          onClick={() => {
            setItems(null);
            setVersion(v => v + 1);
          }}
        >
          Làm mới
        </button>

        {orderId && (
          <Link to="/my-licenses">
            Xem tất cả License
          </Link>
        )}
      </div>

      {error && (
        <p className="alert">
          {error} — Vui lòng thử lại.
        </p>
      )}

      {!items && !error && (
        <p>Đang tải License...</p>
      )}

      {items?.length === 0 && (
        <p>
          Chưa có License sẵn sàng.
          Vui lòng kiểm tra trạng thái đơn hàng.
        </p>
      )}

      {items && (
        <p>Tổng License: {items.length}</p>
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
