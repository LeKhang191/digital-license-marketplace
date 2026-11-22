import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";

export function Navbar() {
  const { ready, authenticated, username, isAdmin, login, logout } = useAuth();

  return (
    <nav className="navbar">
      <div className="container">
        <Link to="/" className="brand">Digital License Marketplace</Link>
        {authenticated && <Link to="/orders">Đơn hàng của tôi</Link>}
        {isAdmin && <Link to="/admin">Quản trị</Link>}
        <span className="spacer" />
        {!ready ? null : authenticated ? (
          <>
            <span className="muted">{username}</span>
            <button className="btn btn-secondary" onClick={logout}>Đăng xuất</button>
          </>
        ) : (
          <button className="btn" onClick={login}>Đăng nhập</button>
        )}
      </div>
    </nav>
  );
}
