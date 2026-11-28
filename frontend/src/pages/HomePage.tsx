import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { getVaultAvailability, listProducts } from "../api/products";
import type { PagedProducts } from "../api/types";
import { ProductCard } from "../components/ProductCard";

export function HomePage() {
  const [params, setParams] = useSearchParams();
  const search = params.get("q") ?? "";
  const category = params.get("category") ?? "";
  const page = Math.max(1, Number(params.get("page")) || 1);
  const [sort, setSort] = useState("");
  const [data, setData] = useState<PagedProducts | null>(null);
  const [availability, setAvailability] = useState<Record<string, number> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    let active = true;
    setData(null); setError(null);
    listProducts({ search, category, page })
      .then((result) => { if (active) setData(result); })
      .catch((e: Error) => { if (active) setError(e.message); });
    getVaultAvailability()
      .then((rows) => {
        if (!active) return;
        const map: Record<string, number> = {};
        for (const r of rows) map[r.productId] = r.available;
        setAvailability(map);
      })
      .catch(() => { /* storefront remains usable even if vault is unavailable */ });
    return () => { active = false; };
  }, [search, category, page, retry]);

  const lastPage = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const items = [...(data?.items ?? [])];
  if (sort) items.sort((a, b) => sort === "asc" ? a.price - b.price : b.price - a.price);
  const home = !category && !search && page === 1;
  const go = (p: number) => { const next = new URLSearchParams(params); next.set("page", String(p)); setParams(next); };
  const stockOf = (id: string) => (availability ? (availability[id] ?? 0) : undefined);

  return <main className="storefront" key={category + search}>
    {home && data && items.length > 0 && (
      <section className="featured-section">
        <h1 className="section-heading">Sản phẩm nổi bật</h1>
        <div className="featured-rail">
          {data.items.slice(0, 7).map((p) => (
            <ProductCard key={p.id} product={p} featured availableKeys={stockOf(p.id)} />
          ))}
        </div>
      </section>
    )}
    <section className="catalog-section">
      <div className="catalog-head">
        <h1 className="section-heading">{search ? `Kết quả: ${search}` : category === "Game" ? "Kho game" : category === "Software" ? "Tạp hóa" : "Tất cả sản phẩm"}</h1>
        <details className="filter-menu"><summary>☷ Bộ lọc</summary><div className="filter-content"><label>Giá (trang hiện tại)<select value={sort} onChange={e => setSort(e.target.value)}><option value="">Mặc định</option><option value="asc">Thấp đến cao</option><option value="desc">Cao đến thấp</option></select></label></div></details>
      </div>
      {error && <div className="alert">Không tải được danh sách: {error} <button className="btn btn-secondary" onClick={() => setRetry(n => n + 1)}>Thử lại</button></div>}
      {!data && !error && <div className="catalog-grid" aria-busy="true" aria-label="Đang tải sản phẩm">{Array.from({ length: 9 }, (_, i) => <div key={i} className="skeleton" />)}</div>}
      {data && !items.length && <p className="muted empty-state">Không có sản phẩm phù hợp.</p>}
      {data && items.length > 0 && (
        <>
          <div className="catalog-grid">
            {items.map((p) => (
              <ProductCard key={p.id} product={p} availableKeys={stockOf(p.id)} />
            ))}
          </div>
          <div className="pagination">
            <button className="btn btn-secondary" disabled={page <= 1} onClick={() => go(page - 1)}>Trước</button>
            <span className="muted">Trang {page} / {lastPage}</span>
            <button className="btn btn-secondary" disabled={page >= lastPage} onClick={() => go(page + 1)}>Sau</button>
          </div>
        </>
      )}
    </section>
  </main>;
}
