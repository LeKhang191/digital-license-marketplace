import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams, useLocation, useNavigate } from "react-router-dom";
import { getMyWallet } from "../api/wallet";
import { useAuth } from "../auth/AuthProvider";
import { useCart } from "../cart/CartContext";
import { vnd } from "./format";

export function Navbar() {
  const { ready, authenticated, username, isCustomer, isCatalogManager, isLicenseCoordinator, isTransactionAuditor, isStaff, isAdmin, login, logout } = useAuth();
  const { totalCount } = useCart();
  const [params] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const [walletBalance, setWalletBalance] = useState<number | null>(null);
  useEffect(() => {
    if (!ready || !authenticated || !isCustomer) { setWalletBalance(null); return; }
    let active = true;
    getMyWallet().then(w => { if (active) setWalletBalance(w.balance); }).catch(() => {});
    return () => { active = false; };
  }, [ready, authenticated, isCustomer, location.key]);
  const [modal, setModal] = useState<"faq" | "guide" | null>(null);
  const account = useRef<HTMLDetailsElement>(null);
  useEffect(() => { if (account.current) account.current.open = false; }, [location.key, authenticated]);
  useEffect(() => {
    const close = (event: PointerEvent) => {
      if (account.current && !account.current.contains(event.target as Node)) account.current.open = false;
    };
    document.addEventListener("pointerdown", close);
    return () => document.removeEventListener("pointerdown", close);
  }, []);
  const dialog = useRef<HTMLDialogElement>(null);
  const [search, setSearch] = useState(params.get("q") ?? "");
  useEffect(() => { setSearch(params.get("q") ?? ""); }, [params]);
  useEffect(() => { if (modal && !dialog.current?.open) dialog.current?.showModal(); else if (!modal && dialog.current?.open) dialog.current.close(); }, [modal]);
  const staffView = authenticated && (isStaff || isAdmin);
  const staffLinks = [
    ...(isCatalogManager ? [["/admin", "Quản lý sản phẩm"]] : []),
    ...(isLicenseCoordinator ? [["/licenses", "Kho license"], ["/licenses/import", "Nhập kho"], ["/licenses/suppliers", "Nhà cung cấp"], ["/licenses/history", "Lịch sử"]] : []),
    ...(isTransactionAuditor ? [["/transactions", "Giao dịch"]] : []),
    ...(isStaff ? [["/support", "Hỗ trợ"]] : []),
  ];
  const tab = location.pathname === "/" ? params.get("category") ?? "" : null;
  return <><nav className={staffView ? "navbar staff-navbar" : "navbar"}><div className="nav-inner">
    <Link to="/" className="brand"><span className="brand-mark">D</span> DIGITAL KEY</Link>
    {staffView ? <div className="nav-tabs" aria-label="Quản lý">{staffLinks.map(([path,label]) => <Link key={path} to={path} className={location.pathname === path ? "active" : ""} aria-current={location.pathname === path ? "page" : undefined}>{label}</Link>)}</div> : <div className="nav-tabs" aria-label="Danh mục">{[["", "Trang chủ"], ["Game", "Game"], ["Software", "Tạp hóa"]].map(([value,label]) => <Link key={value} className={tab === value ? "active" : ""} aria-current={tab === value ? "page" : undefined} to={value ? `/?category=${value}` : "/"}>{label}</Link>)}</div>}
    <div className="nav-actions">{ready && !staffView && <><form className="nav-search" onSubmit={e => { e.preventDefault(); const q = new URLSearchParams(); if (tab) q.set("category",tab); if(search.trim()) q.set("q",search.trim()); navigate(`/?${q}`); }}><input aria-label="Tìm sản phẩm" placeholder="⌕ Tìm game..." value={search} onChange={e=>setSearch(e.target.value)} /></form>
    <button className="pill faq" onClick={()=>setModal("faq")}>FAQ</button><button className="pill guide" onClick={()=>setModal("guide")}>Hướng dẫn mua key</button>{authenticated && isCustomer && <Link to="/support" className="pill">Hỗ trợ</Link>}<Link to="/cart" className="pill">Giỏ hàng{totalCount > 0 ? ` (${totalCount})` : ""}</Link></>}
    <div className="nav-account-slot">
      {!ready ? <span className="muted nav-loading" role="status">Đang tải…</span> : authenticated ? (
        <details className="nav-account" ref={account} onKeyDown={e => {
          if (e.key === "Escape" && account.current?.open) {
            account.current.open = false;
            account.current.querySelector("summary")?.focus();
          }
        }}>
          <summary className="pill"><span className="account-name">{username || "Tài khoản"}</span><span aria-hidden="true">▾</span></summary>
          <div className="account-links">
            {isCustomer && <><Link to="/account">Ví &amp; Cài đặt{walletBalance !== null ? ` (${vnd(walletBalance)})` : ""}</Link><Link to="/orders">Đơn hàng</Link><Link to="/my-licenses">License của tôi</Link><Link to="/support">Hỗ trợ &amp; Hoàn tiền</Link></>}
            {!isCustomer && <Link to="/account">Cài đặt tài khoản</Link>}
            {isCatalogManager && <Link to="/admin">Quản lý sản phẩm</Link>}
            {isLicenseCoordinator && <><Link to="/licenses">Kho license</Link><Link to="/licenses/import">Nhập kho</Link><Link to="/licenses/suppliers">Nhà cung cấp</Link><Link to="/licenses/history">Lịch sử</Link></>}
            {isTransactionAuditor && <Link to="/transactions">Giao dịch</Link>}
            {isStaff && <Link to="/support">Hộp thư Hỗ trợ</Link>}
            <button type="button" onClick={logout}>Đăng xuất</button>
          </div>
        </details>
      ) : <button className="pill" onClick={login}>Đăng nhập</button>}
    </div></div>
  </div></nav><dialog ref={dialog} className="help-dialog" onClose={()=>setModal(null)} onClick={e=>{if(e.target===e.currentTarget)setModal(null);}}><div className="dialog-content"><button className="dialog-close" aria-label="Đóng" onClick={()=>setModal(null)}>×</button><h2>{modal === "faq" ? "Câu hỏi thường gặp" : "Hướng dẫn mua game key"}</h2>{modal === "faq" ? <>{[["Key là gì?","Mã dùng để kích hoạt sản phẩm trên nền tảng tương ứng."],["Thanh toán thế nào?","Đăng nhập, chọn sản phẩm và thanh toán qua VNPay."],["Xem trạng thái ở đâu?","Mở Đơn hàng trong tài khoản để theo dõi giao dịch."]].map(([q,a])=><details key={q}><summary>{q}</summary><p>{a}</p></details>)}</> : <ol>{["Chọn sản phẩm và đọc kỹ thông tin kích hoạt.","Đăng nhập, chọn số lượng và thanh toán qua VNPay.","Theo dõi trạng thái trong mục Đơn hàng.","Khi nhận key, kích hoạt theo hướng dẫn của nền tảng tương ứng."].map(s=><li key={s}>{s}</li>)}</ol>}<button className="btn dialog-ok" onClick={()=>setModal(null)}>Đã hiểu</button></div></dialog></>;
}
