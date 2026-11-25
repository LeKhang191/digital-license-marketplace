
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";

export function Navbar() {
  const {
    ready,
    authenticated,
    username,
    isCustomer,
    isCatalogManager,
    isLicenseCoordinator,
    isTransactionAuditor,
    login,
    logout,
  } = useAuth();

  const roleLabels = [
    isCatalogManager ? "Catalog Manager" : null,

    isLicenseCoordinator
      ? "License Coordinator"
      : null,

    isTransactionAuditor
      ? "Transaction Auditor"
      : null,

    isCustomer ? "Customer" : null,
  ].filter(Boolean);

  return (
    <nav className="navbar">
      <div className="container">
        <Link to="/" className="brand">
          Digital License Marketplace
        </Link>

        {ready && authenticated && (
          <>
            {isCustomer && (
              <Link to="/orders">
                Đơn hàng của tôi
              </Link>
            )}

            {isCatalogManager && (
              <Link to="/admin">
                Quản lý sản phẩm
              </Link>
            )}

            {isLicenseCoordinator && (
              <Link to="/licenses">
                Kho License Key
              </Link>
            )}

            {isTransactionAuditor && (
              <Link to="/transactions">
                Quản lý giao dịch
              </Link>
            )}
          </>
        )}

        <span className="spacer" />

        {!ready ? null : authenticated ? (
          <>
            <div>
              <span className="muted">
                {username}
              </span>

              <div
                className="muted"
                style={{ fontSize: 12 }}
              >
                {roleLabels.join(" / ")}
              </div>
            </div>

            <button
              className="btn btn-secondary"
              onClick={logout}
            >
              Đăng xuất
            </button>
          </>
        ) : (
          <button
            className="btn"
            onClick={login}
          >
            Đăng nhập
          </button>
        )}
      </div>
    </nav>
  );
}
