import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams, useLocation, useNavigate } from "react-router-dom";
import { listProducts } from "../api/products";
import type { Product } from "../api/types";
import { getMyWallet } from "../api/wallet";
import { useAuth } from "../auth/AuthProvider";
import { useCart } from "../cart/CartContext";
import { useLanguage } from "../i18n/LanguageContext";
import { vnd } from "./format";

function HomeIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M3 10.5 12 3l9 7.5" />
      <path d="M5 9.5V20a1 1 0 0 0 1 1h4.5v-6h3v6H18a1 1 0 0 0 1-1V9.5" />
    </svg>
  );
}

function ControllerIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="2" y="6" width="20" height="12" rx="5" />
      <path d="M6 12h4" />
      <path d="M8 10v4" />
      <circle cx="15.5" cy="11" r="0.9" fill="currentColor" stroke="none" />
      <circle cx="17.8" cy="13" r="0.9" fill="currentColor" stroke="none" />
    </svg>
  );
}

function StoreBagIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M5 8h14l-1.2 12.2a1 1 0 0 1-1 .8H7.2a1 1 0 0 1-1-.8L5 8Z" />
      <path d="M9 8V6a3 3 0 0 1 6 0v2" />
    </svg>
  );
}

export function Navbar() {
  const { ready, authenticated, username, isCustomer, isCatalogManager, isLicenseCoordinator, isTransactionAuditor, isStaff, isAdmin, login, logout } = useAuth();
  const { totalCount } = useCart();
  const { t } = useLanguage();
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
  const searchWrapRef = useRef<HTMLDivElement>(null);
  const dialog = useRef<HTMLDialogElement>(null);

  const [search, setSearch] = useState(params.get("q") ?? "");
  const [searchOpen, setSearchOpen] = useState(false);
  const [searchLoading, setSearchLoading] = useState(false);
  const [searchResults, setSearchResults] = useState<Product[]>([]);
  const [searchTotal, setSearchTotal] = useState(0);

  useEffect(() => {
    setSearch(params.get("q") ?? "");
  }, [params]);

  useEffect(() => {
    if (account.current) account.current.open = false;
    setSearchOpen(false);
  }, [location.key, authenticated]);

  useEffect(() => {
    const close = (event: PointerEvent) => {
      const target = event.target as Node;
      if (account.current && !account.current.contains(target)) {
        account.current.open = false;
      }
      if (searchWrapRef.current && !searchWrapRef.current.contains(target)) {
        setSearchOpen(false);
      }
    };
    document.addEventListener("pointerdown", close);
    return () => document.removeEventListener("pointerdown", close);
  }, []);

  useEffect(() => {
    if (modal && !dialog.current?.open) dialog.current?.showModal();
    else if (!modal && dialog.current?.open) dialog.current.close();
  }, [modal]);

  // Live search mini-window below search input
  useEffect(() => {
    const term = search.trim();
    if (!term || !searchOpen) {
      setSearchLoading(false);
      if (!term) {
        setSearchResults([]);
        setSearchTotal(0);
      }
      return;
    }

    const controller = new AbortController();
    setSearchLoading(true);
    const timer = window.setTimeout(() => {
      listProducts({ search: term, page: 1, signal: controller.signal })
        .then((res) => {
          if (controller.signal.aborted) return;
          setSearchResults(res.items.filter((p) => p.isActive).slice(0, 6));
          setSearchTotal(res.total);
        })
        .catch(() => {
          if (!controller.signal.aborted) {
            setSearchResults([]);
            setSearchTotal(0);
          }
        })
        .finally(() => {
          if (!controller.signal.aborted) setSearchLoading(false);
        });
    }, 180);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [search, searchOpen]);

  const staffView = authenticated && (isStaff || isAdmin);
  const staffLinks: [string, string][] = [
    ...(isCatalogManager ? [["/admin", t("Quản lý sản phẩm", "Catalog")] as [string, string]] : []),
    ...(isLicenseCoordinator
      ? [
          ["/licenses", t("Kho license", "Inventory")] as [string, string],
          ["/licenses/import", t("Nhập kho", "Import")] as [string, string],
          ["/licenses/suppliers", t("Nhà cung cấp", "Suppliers")] as [string, string],
          ["/licenses/history", t("Lịch sử", "History")] as [string, string],
        ]
      : []),
    ...(isTransactionAuditor ? [["/transactions", t("Giao dịch", "Transactions")] as [string, string]] : []),
    ...(isStaff ? [["/support", t("Hỗ trợ", "Support")] as [string, string]] : []),
  ];

  const tab = location.pathname === "/" ? params.get("category") ?? "" : null;

  const storeTabs = [
    { value: "", label: t("Trang chủ", "Home"), icon: <HomeIcon /> },
    { value: "Game", label: t("Game", "Games"), icon: <ControllerIcon /> },
    { value: "Software", label: t("Tạp hóa", "Store"), icon: <StoreBagIcon /> },
  ];

  function submitSearch() {
    const q = new URLSearchParams();
    if (tab) q.set("category", tab);
    if (search.trim()) q.set("q", search.trim());
    setSearchOpen(false);
    navigate(`/?${q}`);
  }

  return (
    <>
      <nav className={staffView ? "navbar staff-navbar" : "navbar"}>
        <div className="nav-inner">
          <Link to="/" className="brand">
            <span className="brand-mark">D</span> DIGITAL KEY
          </Link>

          {staffView ? (
            <div className="nav-tabs" aria-label={t("Quản lý", "Management")}>
              {staffLinks.map(([path, label]) => (
                <Link
                  key={path}
                  to={path}
                  className={location.pathname === path ? "active" : ""}
                  aria-current={location.pathname === path ? "page" : undefined}
                >
                  {label}
                </Link>
              ))}
            </div>
          ) : (
            <div className="nav-tabs" aria-label={t("Danh mục", "Categories")}>
              {storeTabs.map((item) => (
                <Link
                  key={item.value}
                  className={`nav-tab-link${tab === item.value ? " active" : ""}`}
                  aria-current={tab === item.value ? "page" : undefined}
                  to={item.value ? `/?category=${item.value}` : "/"}
                >
                  {item.icon}
                  <span>{item.label}</span>
                </Link>
              ))}
            </div>
          )}

          <div className="nav-actions">
            {ready && !staffView && (
              <>
                <div className="nav-search-wrap" ref={searchWrapRef}>
                  <form
                    className="nav-search"
                    onSubmit={(e) => {
                      e.preventDefault();
                      submitSearch();
                    }}
                  >
                    <input
                      aria-label={t("Tìm sản phẩm", "Search products")}
                      aria-expanded={searchOpen && search.trim().length > 0}
                      placeholder={t("⌕ Tìm game...", "⌕ Search games...")}
                      value={search}
                      onFocus={() => {
                        if (search.trim()) setSearchOpen(true);
                      }}
                      onKeyDown={(e) => {
                        if (e.key === "Escape") setSearchOpen(false);
                      }}
                      onChange={(e) => {
                        setSearch(e.target.value);
                        setSearchOpen(Boolean(e.target.value.trim()));
                      }}
                    />
                  </form>

                  {searchOpen && search.trim().length > 0 && (
                    <div
                      className="nav-search-popover"
                      role="listbox"
                      aria-label={t("Kết quả tìm kiếm nhanh", "Quick search results")}
                    >
                      {searchLoading ? (
                        <div className="nav-search-empty muted">
                          {t("Đang tìm game...", "Searching games...")}
                        </div>
                      ) : searchResults.length === 0 ? (
                        <div className="nav-search-empty muted">
                          {t(
                            `Không tìm thấy game cho "${search.trim()}"`,
                            `No games found for "${search.trim()}"`,
                          )}
                        </div>
                      ) : (
                        <>
                          <div className="nav-search-list">
                            {searchResults.map((p) => {
                              const thumb = p.steam?.headerImage || p.steam?.posterImage;
                              return (
                                <Link
                                  key={p.id}
                                  to={`/products/${p.id}`}
                                  className="nav-search-item"
                                  role="option"
                                  onClick={() => setSearchOpen(false)}
                                >
                                  {thumb ? (
                                    <img
                                      src={thumb}
                                      alt=""
                                      className="nav-search-thumb"
                                      loading="lazy"
                                    />
                                  ) : (
                                    <span className="nav-search-thumb fallback">
                                      {p.name.slice(0, 2).toUpperCase()}
                                    </span>
                                  )}
                                  <span className="nav-search-info">
                                    <strong>{p.name}</strong>
                                    <small className="muted">
                                      {p.steam?.genres?.slice(0, 2).join(" · ") || p.category}
                                    </small>
                                  </span>
                                  <span className="nav-search-price">{vnd(p.price)}</span>
                                </Link>
                              );
                            })}
                          </div>
                          <button
                            type="button"
                            className="nav-search-all"
                            onClick={submitSearch}
                          >
                            {t(
                              `Xem tất cả ${searchTotal} kết quả cho "${search.trim()}" →`,
                              `View all ${searchTotal} results for "${search.trim()}" →`,
                            )}
                          </button>
                        </>
                      )}
                    </div>
                  )}
                </div>

                <button className="pill faq" onClick={() => setModal("faq")}>
                  FAQ
                </button>
                <button className="pill guide" onClick={() => setModal("guide")}>
                  {t("Hướng dẫn mua key", "Buying Guide")}
                </button>
                {authenticated && isCustomer && (
                  <Link to="/support" className="pill">
                    {t("Hỗ trợ", "Support")}
                  </Link>
                )}
                <Link to="/cart" className="pill">
                  {t("Giỏ hàng", "Cart")}
                  {totalCount > 0 ? ` (${totalCount})` : ""}
                </Link>
              </>
            )}

            <div className="nav-account-slot">
              {!ready ? (
                <span className="muted nav-loading" role="status">
                  {t("Đang tải…", "Loading…")}
                </span>
              ) : authenticated ? (
                <details
                  className="nav-account"
                  ref={account}
                  onKeyDown={(e) => {
                    if (e.key === "Escape" && account.current?.open) {
                      account.current.open = false;
                      account.current.querySelector("summary")?.focus();
                    }
                  }}
                >
                  <summary className="pill">
                    <span className="account-name">
                      {username || t("Tài khoản", "Account")}
                    </span>
                    <span aria-hidden="true">▾</span>
                  </summary>
                  <div className="account-links">
                    {isCustomer && (
                      <>
                        <Link to="/account">
                          {t("Ví & Cài đặt", "Wallet & Settings")}
                          {walletBalance !== null ? ` (${vnd(walletBalance)})` : ""}
                        </Link>
                        <Link to="/orders">{t("Đơn hàng", "Orders")}</Link>
                        <Link to="/my-licenses">{t("License của tôi", "My Licenses")}</Link>
                        <Link to="/support">{t("Hỗ trợ & Hoàn tiền", "Support & Refunds")}</Link>
                      </>
                    )}
                    {!isCustomer && (
                      <Link to="/account">{t("Cài đặt tài khoản", "Account Settings")}</Link>
                    )}
                    {isCatalogManager && (
                      <Link to="/admin">{t("Quản lý sản phẩm", "Catalog")}</Link>
                    )}
                    {isLicenseCoordinator && (
                      <>
                        <Link to="/licenses">{t("Kho license", "Inventory")}</Link>
                        <Link to="/licenses/import">{t("Nhập kho", "Import")}</Link>
                        <Link to="/licenses/suppliers">{t("Nhà cung cấp", "Suppliers")}</Link>
                        <Link to="/licenses/history">{t("Lịch sử", "History")}</Link>
                      </>
                    )}
                    {isTransactionAuditor && (
                      <Link to="/transactions">{t("Giao dịch", "Transactions")}</Link>
                    )}
                    {isStaff && (
                      <Link to="/support">{t("Hộp thư Hỗ trợ", "Support Inbox")}</Link>
                    )}
                    <button type="button" onClick={logout}>
                      {t("Đăng xuất", "Sign out")}
                    </button>
                  </div>
                </details>
              ) : (
                <button className="pill" onClick={login}>
                  {t("Đăng nhập", "Sign in")}
                </button>
              )}
            </div>
          </div>
        </div>
      </nav>

      <dialog
        ref={dialog}
        className="help-dialog"
        onClose={() => setModal(null)}
        onClick={(e) => {
          if (e.target === e.currentTarget) setModal(null);
        }}
      >
        <div className="dialog-content">
          <button
            className="dialog-close"
            aria-label={t("Đóng", "Close")}
            onClick={() => setModal(null)}
          >
            ×
          </button>
          <h2>
            {modal === "faq"
              ? t("Câu hỏi thường gặp", "Frequently Asked Questions")
              : t("Hướng dẫn mua game key", "How to Buy Game Keys")}
          </h2>
          {modal === "faq" ? (
            <>
              {[
                [
                  t("Key là gì?", "What is a license key?"),
                  t(
                    "Mã dùng để kích hoạt sản phẩm trên nền tảng tương ứng (Steam, ...).",
                    "A digital activation code used to redeem your game or software on its platform (Steam, etc.).",
                  ),
                ],
                [
                  t("Thanh toán thế nào?", "How do I pay?"),
                  t(
                    "Đăng nhập, chọn sản phẩm và thanh toán bằng Ví DLM hoặc VNPay.",
                    "Sign in, choose your product, and pay via DLM Wallet or VNPay.",
                  ),
                ],
                [
                  t("Xem trạng thái ở đâu?", "Where can I track my order?"),
                  t(
                    "Mở mục Đơn hàng hoặc License của tôi trong menu tài khoản.",
                    "Open Orders or My Licenses from the account menu.",
                  ),
                ],
              ].map(([q, a]) => (
                <details key={q}>
                  <summary>{q}</summary>
                  <p>{a}</p>
                </details>
              ))}
            </>
          ) : (
            <ol>
              {[
                t(
                  "Chọn sản phẩm và đọc kỹ thông tin kích hoạt.",
                  "Choose a product and review its activation details.",
                ),
                t(
                  "Đăng nhập, chọn số lượng và thanh toán qua Ví DLM hoặc VNPay.",
                  "Sign in, choose quantity, and pay with DLM Wallet or VNPay.",
                ),
                t(
                  "Theo dõi trạng thái trong mục Đơn hàng.",
                  "Track delivery status in My Orders.",
                ),
                t(
                  "Khi nhận key, kích hoạt theo hướng dẫn của nền tảng tương ứng.",
                  "Reveal your key and activate it on the corresponding platform.",
                ),
              ].map((s) => (
                <li key={s}>{s}</li>
              ))}
            </ol>
          )}
          <button className="btn dialog-ok" onClick={() => setModal(null)}>
            {t("Đã hiểu", "Got it")}
          </button>
        </div>
      </dialog>
    </>
  );
}
