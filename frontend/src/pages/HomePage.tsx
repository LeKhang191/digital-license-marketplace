import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import { listProducts } from "../api/products";
import type { Product } from "../api/types";
import { ProductCard } from "../components/ProductCard";
import { filterProducts, pageNumbers, readPrice, PAGE_SIZE, SORT_OPTIONS, type StoreSort } from "./storefrontFilters";

export function HomePage() {
  const [params, setParams] = useSearchParams();
  const search = params.get("q") ?? "";
  const category = params.get("category") ?? "";
  const genre = params.get("genre") ?? "";
  const minParam = params.get("min") ?? "";
  const maxParam = params.get("max") ?? "";
  const min = readPrice(minParam);
  const max = readPrice(maxParam);
  const rawSort = params.get("sort") as StoreSort | null;
  const sort = rawSort && SORT_OPTIONS.includes(rawSort) ? rawSort : "default";
  const requestedPage = Number(params.get("page")) || 1;
  const [products, setProducts] = useState<Product[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);
  const [minDraft, setMinDraft] = useState(minParam);
  const [maxDraft, setMaxDraft] = useState(maxParam);
  const [priceError, setPriceError] = useState("");
  useEffect(() => {
    setMinDraft(minParam);
    setMaxDraft(maxParam);
    setPriceError("");
  }, [minParam, maxParam]);

  // Load every public page once per visit, not just the visible 12 products.
  // Keep the last successful dataset during retry; never publish a partial load.
  useEffect(() => {
    const controller = new AbortController();
    const signal = controller.signal;
    setLoading(true);
    setError(null);
    async function load() {
      const first = await listProducts({ page: 1, signal });
      const all = new Map(first.items.map(p => [p.id, p]));
      if (first.pageSize <= 0) throw new Error("Phân trang không hợp lệ");
      const last = Math.ceil(first.total / first.pageSize);
      for (let page = 2; page <= last; page++) {
        const result = await listProducts({ page, signal });
        result.items.forEach(p => all.set(p.id, p));
      }
      if (!signal.aborted) setProducts([...all.values()].filter(p => p.isActive));
    }
    void load().catch((e: unknown) => {
      if (!signal.aborted) setError(e instanceof Error ? e.message : "Không tải được sản phẩm");
    }).finally(() => { if (!signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [retry]);

  const genres = useMemo(() => [...new Set((products ?? []).flatMap(p => p.steam?.genres ?? []))].filter(Boolean).sort((a,b) => a.localeCompare(b, "vi")), [products]);
  const filtered = useMemo(() => filterProducts(products ?? [], { search, category, genre, min, max, sort }), [products, search, category, genre, min, max, sort]);
  const lastPage = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const page = Math.min(lastPage, Math.max(1, Math.trunc(requestedPage)));
  const items = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);
  const update = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params);
    next.delete("page");
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value); else next.delete(key);
    }
    setParams(next);
  };
  const go = (number: number) => {
    const next = new URLSearchParams(params);
    if (number === 1) next.delete("page"); else next.set("page", String(number));
    setParams(next);
  };
  const home = !category && !search && !genre && min === undefined && max === undefined && sort === "default";
  const applyPrices = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const low = readPrice(minDraft), high = readPrice(maxDraft);
    if ((minDraft !== "" && low === undefined) || (maxDraft !== "" && high === undefined) || (low !== undefined && high !== undefined && low > high)) {
      setPriceError("Khoảng giá không hợp lệ."); return;
    }
    setPriceError(""); update({ min: minDraft, max: maxDraft });
  };
  return <main className="storefront">
    {home && <section className="featured-section"><h2 className="section-heading">Khám phá sản phẩm</h2><div className="featured-rail">{products ? products.slice(0,7).map(p => <ProductCard key={p.id} product={p} featured/>) : loading ? Array.from({length:7}, (_,i) => <div className="featured-placeholder skeleton" key={i}/>) : null}</div></section>}
    <section className="catalog-section">
      <div className="catalog-head"><h1 className="section-heading">{search ? `Kết quả: ${search}` : category === "Game" ? "Kho game" : category === "Software" ? "Tạp hóa" : "Tất cả sản phẩm"}</h1>
        <button type="button" className="btn btn-secondary" disabled={loading} onClick={() => setRetry(n => n+1)}>Làm mới</button>
      </div>
      <div className="store-filters">
        <label>Thể loại game<select value={genre} onChange={e => update({genre:e.target.value})}><option value="">Tất cả</option>{genre && !genres.includes(genre) && <option value={genre}>{genre}</option>}{genres.map(g => <option key={g}>{g}</option>)}</select></label>
        <label>Sắp xếp<select value={sort} onChange={e => update({sort:e.target.value === "default" ? "" : e.target.value})}><option value="default">Mặc định</option><option value="price-asc">Giá thấp đến cao</option><option value="price-desc">Giá cao đến thấp</option><option value="name-asc">Tên A–Z</option><option value="name-desc">Tên Z–A</option><option value="newest">Mới thêm</option></select></label>
        <form className="price-filter" onSubmit={applyPrices}><label>Giá từ (₫)<input type="number" min={0} step={1} value={minDraft} onChange={e => setMinDraft(e.target.value)}/></label><label>Đến (₫)<input type="number" min={0} step={1} value={maxDraft} onChange={e => setMaxDraft(e.target.value)}/></label><button className="btn btn-secondary" type="submit">Áp dụng</button></form>
        <button type="button" className="btn btn-secondary" onClick={() => {setMinDraft("");setMaxDraft("");setPriceError("");update({genre:"",min:"",max:"",sort:""});}}>Xóa bộ lọc</button>
      </div>
      {priceError && <p role="alert" className="alert">{priceError}</p>}
      <p className="catalog-status muted" role="status">{loading ? "Đang tải danh sách…" : products ? `${filtered.length} sản phẩm` : "Không có dữ liệu"}</p>
      {error && <div className="alert" role="alert">Không tải được danh sách: {error}{products && " (đang hiển thị dữ liệu lần tải trước)"} <button className="btn btn-secondary" onClick={() => setRetry(n => n+1)}>Thử lại</button></div>}
      <div className={`catalog-grid${(!products && loading) || filtered.length > PAGE_SIZE ? " catalog-stable" : ""}`} aria-busy={loading}>
        {!products && loading ? Array.from({length:PAGE_SIZE}, (_,i) => <div className="skeleton" key={i}/>) : items.map(p => <ProductCard key={p.id} product={p}/>)}
        {products && !items.length && <p className="muted empty-state">Không có sản phẩm phù hợp.</p>}
      </div>
      {products && filtered.length > 0 && <nav className="pagination" aria-label="Phân trang sản phẩm"><button className="btn btn-secondary" disabled={page <= 1} onClick={() => go(page-1)}>Trước</button>{pageNumbers(page,lastPage).map(n => typeof n === "number" ? <button key={n} className={`btn btn-secondary${n === page ? " page-current" : ""}`} aria-current={n === page ? "page" : undefined} onClick={() => go(n)}>{n}</button> : <span key={n}>…</span>)}<button className="btn btn-secondary" disabled={page >= lastPage} onClick={() => go(page+1)}>Sau</button></nav>}
    </section>
  </main>;
}
