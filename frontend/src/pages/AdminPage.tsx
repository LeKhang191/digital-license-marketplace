import { useAuth } from "../auth/AuthProvider";

// Khung trang quản trị. Tiếp theo: bảng sản phẩm (CRUD), nhập kho Key, xử lý hoàn tiền.
export function AdminPage() {
  const { ready, isAdmin } = useAuth();

  if (!ready) return <div className="container"><p className="muted">Đang tải...</p></div>;
  if (!isAdmin) return <div className="container"><div className="alert">Bạn không có quyền truy cập trang này.</div></div>;

  return (
    <div className="container">
      <h1>Quản trị</h1>
      <div className="grid">
        <div className="card"><h2>Sản phẩm</h2><p className="muted">Thêm, sửa, xóa sản phẩm.</p></div>
        <div className="card"><h2>Kho Key</h2><p className="muted">Nhập kho Key mới.</p></div>
        <div className="card"><h2>Hoàn tiền</h2><p className="muted">Xử lý đơn hoàn tiền.</p></div>
      </div>
    </div>
  );
}
