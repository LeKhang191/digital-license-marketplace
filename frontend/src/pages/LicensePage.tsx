
export function LicensePage() {
  return (
    <div className="container">
      <h1>Quản lý kho License Key</h1>

      <p className="muted">
        License Coordinator Dashboard
      </p>

      <div className="grid admin-summary">
        <div className="card">
          <h2>Tổng License Key</h2>
          <strong>—</strong>
        </div>

        <div className="card">
          <h2>Key khả dụng</h2>
          <strong>—</strong>
        </div>

        <div className="card">
          <h2>Key đã cấp phát</h2>
          <strong>—</strong>
        </div>
      </div>

      <div className="card" style={{ marginTop: 24 }}>
        <h2>Quản lý kho mã Key</h2>

        <p className="muted">
          Chức năng sẽ được kết nối với
          License Vault Service.
        </p>

        <button className="btn" disabled>
          + Nhập License Key
        </button>

        <table
          className="admin-table"
          style={{ marginTop: 20 }}
        >
          <thead>
            <tr>
              <th>Sản phẩm</th>
              <th>Key khả dụng</th>
              <th>Key đã cấp phát</th>
              <th>Thao tác</th>
            </tr>
          </thead>

          <tbody>
            <tr>
              <td colSpan={4}>
                Chưa kết nối License Vault Service.
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  );
}
