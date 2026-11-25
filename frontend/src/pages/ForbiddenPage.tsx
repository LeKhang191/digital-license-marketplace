
import { Link } from "react-router-dom";

export function ForbiddenPage() {
  return (
    <div className="container">
      <div
        className="card"
        style={{
          textAlign: "center",
          padding: 40,
        }}
      >
        <h1 style={{ fontSize: 64, margin: 0 }}>
          403
        </h1>

        <h2>Không có quyền truy cập</h2>

        <p className="muted">
          Tài khoản của bạn không có quyền truy cập
          chức năng này.
        </p>

        <Link to="/" className="btn">
          Quay về cửa hàng
        </Link>
      </div>
    </div>
  );
}
