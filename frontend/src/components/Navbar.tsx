import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";

export function Navbar() {
  const { ready, authenticated, username, isAdmin, login, logout } = useAuth();
  const [params] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const [modal, setModal] = useState<"faq" | "guide" | null>(null);
  const dialog = useRef<HTMLDialogElement>(null);
  const [search, setSearch] = useState(params.get("q") ?? "");
  useEffect(() => { setSearch(params.get("q") ?? ""); }, [params]);
  useEffect(() => { if (modal && !dialog.current?.open) dialog.current?.showModal(); else if (!modal && dialog.current?.open) dialog.current.close(); }, [modal]);
  const tab = location.pathname === "/" ? params.get("category") ?? "" : null;
  return <><nav className="navbar"><div className="nav-inner">
    <Link to="/" className="brand"><span className="brand-mark">D</span> DIGITAL KEY</Link>
    <div className="nav-tabs" aria-label="Danh mục">{[["", "Trang chủ"], ["Game", "Game"], ["Software", "Tạp hóa"]].map(([value,label]) => <Link key={value} className={tab === value ? "active" : ""} aria-current={tab === value ? "page" : undefined} to={value ? `/?category=${value}` : "/"}>{label}</Link>)}</div>
    <div className="nav-actions"><form className="nav-search" onSubmit={e => { e.preventDefault(); const q = new URLSearchParams(); if (tab) q.set("category",tab); if(search.trim()) q.set("q",search.trim()); navigate(`/?${q}`); }}><input aria-label="Tìm sản phẩm" placeholder="⌕ Tìm game..." value={search} onChange={e=>setSearch(e.target.value)} /></form>
    <button className="pill faq" onClick={()=>setModal("faq")}>FAQ</button><button className="pill guide" onClick={()=>setModal("guide")}>Hướng dẫn mua key</button>
    {authenticated && <Link className="pill" to="/orders">Đơn hàng</Link>}{isAdmin && <Link className="pill" to="/admin">Quản trị</Link>}
    {ready && (authenticated ? <button className="pill" title={username ?? undefined} onClick={logout}>Đăng xuất</button> : <button className="pill" onClick={login}>Đăng nhập</button>)}</div>
  </div></nav><dialog ref={dialog} className="help-dialog" onClose={()=>setModal(null)} onClick={e=>{if(e.target===e.currentTarget)setModal(null);}}><div className="dialog-content"><button className="dialog-close" aria-label="Đóng" onClick={()=>setModal(null)}>×</button><h2>{modal === "faq" ? "Câu hỏi thường gặp" : "Hướng dẫn mua game key"}</h2>{modal === "faq" ? <>{[["Key là gì?","Mã dùng để kích hoạt sản phẩm trên nền tảng tương ứng."],["Thanh toán thế nào?","Đăng nhập, chọn sản phẩm và thanh toán qua VNPay."],["Xem trạng thái ở đâu?","Mở Đơn hàng trong tài khoản để theo dõi giao dịch."]].map(([q,a])=><details key={q}><summary>{q}</summary><p>{a}</p></details>)}</> : <ol>{["Chọn sản phẩm và đọc kỹ thông tin kích hoạt.","Đăng nhập, chọn số lượng và thanh toán qua VNPay.","Theo dõi trạng thái trong mục Đơn hàng.","Khi nhận key, kích hoạt theo hướng dẫn của nền tảng tương ứng."].map(s=><li key={s}>{s}</li>)}</ol>}<button className="btn dialog-ok" onClick={()=>setModal(null)}>Đã hiểu</button></div></dialog></>;
}
