import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import {
  getMyWallet,
  listWalletTransactions,
  type WalletBalance,
  type WalletTransaction,
} from "../api/wallet";
import { useAuth } from "../auth/AuthProvider";
import { keycloak } from "../auth/keycloak";
import { vnd } from "../components/format";
import { config } from "../config";

const TX_LABEL: Record<string, string> = {
  TOPUP: "Nạp tiền vào ví",
  DEPOSIT: "Nạp tiền vào ví",
  PURCHASE: "Thanh toán đơn hàng",
  DEBIT: "Thanh toán đơn hàng",
  REFUND: "Hoàn tiền đơn hàng",
};

export function AccountPage() {
  const { ready, authenticated, isCustomer, username, login } = useAuth();
  const [wallet, setWallet] = useState<WalletBalance | null>(null);
  const [txs, setTxs] = useState<WalletTransaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [version, setVersion] = useState(0);

  useEffect(() => {
    if (!ready || !authenticated || !isCustomer) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError("");
    Promise.all([getMyWallet(), listWalletTransactions(1, 30)])
      .then(([w, list]) => {
        if (!cancelled) {
          setWallet(w);
          setTxs(list);
        }
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message || "Không tải được thông tin ví.");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [ready, authenticated, isCustomer, version]);

  if (!ready) {
    return (
      <div className="container">
        <p className="muted">Đang tải...</p>
      </div>
    );
  }

  if (!authenticated) {
    return (
      <div className="container">
        <div className="card">
          <h1>Ví &amp; Cài đặt tài khoản</h1>
          <p>Vui lòng đăng nhập để xem số dư ví và cài đặt tài khoản.</p>
          <button type="button" className="btn" style={{ marginTop: 12 }} onClick={login}>
            Đăng nhập
          </button>
        </div>
      </div>
    );
  }

  const parsed = keycloak.tokenParsed as Record<string, unknown> | undefined;
  const email = typeof parsed?.email === "string" ? parsed.email : "Chưa có email";
  const emailVerified = parsed?.email_verified === true;
  const sub = typeof parsed?.sub === "string" ? parsed.sub : "";
  const accountConsoleUrl = `${config.keycloakUrl}/realms/${config.realm}/account`;

  return (
    <div className="container">
      <div className="product-back">
        <Link to="/" className="btn btn-secondary">
          ← Quay về cửa hàng
        </Link>
      </div>

      <h1>Ví &amp; Cài đặt tài khoản</h1>

      <div className="checkout-layout">
        <div className="checkout-items">
          {isCustomer && (
            <section className="card">
              <div className="supplier-heading">
                <h2>Ví DLM (Marketplace Balance)</h2>
                <button
                  type="button"
                  className="btn btn-secondary"
                  disabled={loading}
                  onClick={() => setVersion((v) => v + 1)}
                >
                  Làm mới
                </button>
              </div>

              {error && (
                <div className="alert" style={{ marginTop: 12 }}>
                  {error}
                </div>
              )}

              <div className="wallet-balance-box">
                <span className="muted">Số dư khả dụng</span>
                <strong className="price" style={{ fontSize: 30 }}>
                  {loading ? "Đang tải..." : wallet ? vnd(wallet.balance) : "—"}
                </strong>
                <p className="muted">
                  Dùng để thanh toán nhanh đơn hàng hoặc nhận tiền hoàn tự động khi thiếu key.
                </p>
              </div>

              <h3 style={{ marginTop: 20 }}>Lịch sử giao dịch ví</h3>
              {loading ? (
                <p className="muted">Đang tải lịch sử...</p>
              ) : txs.length === 0 ? (
                <p className="muted">Chưa có giao dịch ví nào.</p>
              ) : (
                <div style={{ overflowX: "auto", marginTop: 10 }}>
                  <table className="admin-table">
                    <thead>
                      <tr>
                        <th>Thời gian</th>
                        <th>Loại giao dịch</th>
                        <th>Số tiền</th>
                        <th>Số dư sau GD</th>
                        <th>Mã tham chiếu</th>
                      </tr>
                    </thead>
                    <tbody>
                      {txs.map((tx) => (
                        <tr key={tx.id}>
                          <td>{new Date(tx.createdAtUtc).toLocaleString("vi-VN")}</td>
                          <td>{TX_LABEL[tx.type.toUpperCase()] ?? tx.type}</td>
                          <td>{vnd(tx.amount)}</td>
                          <td>{vnd(tx.balanceAfter)}</td>
                          <td>
                            <code title={tx.referenceId}>{tx.referenceId.slice(0, 8)}</code>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          )}
        </div>

        <aside className="card checkout-summary">
          <h2>Cài đặt tài khoản</h2>
          <div className="receipt-lines" style={{ marginTop: 12 }}>
            <div className="receipt-line">
              <span className="muted">Tên đăng nhập</span>
              <strong>{username || "—"}</strong>
            </div>
            <div className="receipt-line">
              <span className="muted">Email</span>
              <strong>{email}</strong>
            </div>
            <div className="receipt-line">
              <span className="muted">Xác thực email</span>
              <span className={emailVerified ? "license-available" : "license-disabled"}>
                {emailVerified ? "Đã xác thực" : "Chưa xác thực"}
              </span>
            </div>
            {sub && (
              <div className="receipt-line">
                <span className="muted">User ID</span>
                <code title={sub}>{sub.slice(0, 12)}…</code>
              </div>
            )}
          </div>

          <a
            className="btn"
            style={{ width: "100%", marginTop: 16, textAlign: "center", display: "block" }}
            href={accountConsoleUrl}
            target="_blank"
            rel="noopener noreferrer"
          >
            Quản lý bảo mật &amp; Đổi mật khẩu ↗
          </a>
          <p className="muted" style={{ marginTop: 10, fontSize: ".85rem" }}>
            Mở trang quản lý tài khoản Keycloak để đổi mật khẩu hoặc cập nhật thông tin bảo mật.
          </p>
        </aside>
      </div>
    </div>
  );
}
