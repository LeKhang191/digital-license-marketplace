
export function TransactionsPage() {
  return (
    <div className="container">
      <h1>Quản lý giao dịch</h1>

      <p className="muted">
        Transaction Auditor Dashboard
      </p>

      <div className="grid admin-summary">
        <div className="card">
          <h2>Tổng giao dịch</h2>
          <strong>—</strong>
        </div>

        <div className="card">
          <h2>Thanh toán thành công</h2>
          <strong>—</strong>
        </div>

        <div className="card">
          <h2>Đã hoàn tiền</h2>
          <strong>—</strong>
        </div>
      </div>

      <div className="card" style={{ marginTop: 24 }}>
        <h2>Danh sách giao dịch</h2>

        <p className="muted">
          Chức năng tra cứu giao dịch và hoàn tiền
          đang được phát triển.
        </p>

        <table className="admin-table">
          <thead>
            <tr>
              <th>Mã đơn hàng</th>
              <th>Khách hàng</th>
              <th>Số tiền</th>
              <th>Trạng thái</th>
              <th>Thao tác</th>
            </tr>
          </thead>

          <tbody>
            <tr>
              <td colSpan={5}>
                Chưa có API tra cứu giao dịch.
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  );
}

