import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import { listProducts } from "../api/products";
import type { Product } from "../api/types";
import { ProductCard } from "../components/ProductCard";
import { RefreshButton } from "../components/RefreshButton";
import { useLanguage } from "../i18n/LanguageContext";
import { filterProducts, pageNumbers, readPrice, PAGE_SIZE, SORT_OPTIONS, type StoreSort } from "./storefrontFilters";

export function HomePage() {
  const { t } = useLanguage();
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
    {home && <section className="featured-section"><h2 className="section-heading">{t("Khám phá sản phẩm", "Featured Games")}</h2><div className="featured-rail">{products ? products.slice(0,7).map(p => <ProductCard key={p.id} product={p} featured/>) : loading ? Array.from({length:7}, (_,i) => <div className="featured-placeholder skeleton" key={i}/>) : null}</div></section>}
    <section className="catalog-section">
      <div className="catalog-head"><h1 className="section-heading">{search ? `${t("Kết quả", "Results")}: ${search}` : category === "Game" ? t("Kho game", "Game Library") : category === "Software" ? t("Tạp hóa", "Store & Software") : t("Tất cả sản phẩm", "All Products")}</h1>
        <RefreshButton loading={loading} onClick={() => setRetry(n => n + 1)} />
      </div>
      <div className="store-filters">
        <label>{t("Thể loại game", "Genre")}<select value={genre} onChange={e => update({genre:e.target.value})}><option value="">{t("Tất cả", "All")}</option>{genre && !genres.includes(genre) && <option value={genre}>{genre}</option>}{genres.map(g => <option key={g}>{g}</option>)}</select></label>
        <label>{t("Sắp xếp", "Sort by")}<select value={sort} onChange={e => update({sort:e.target.value === "default" ? "" : e.target.value})}><option value="default">{t("Mặc định", "Default")}</option><option value="price-asc">{t("Giá thấp đến cao", "Price: Low to High")}</option><option value="price-desc">{t("Giá cao đến thấp", "Price: High to Low")}</option><option value="name-asc">{t("Tên A–Z", "Name A–Z")}</option><option value="name-desc">{t("Tên Z–A", "Name Z–A")}</option><option value="newest">{t("Mới thêm", "Newest")}</option></select></label>
        <form className="price-filter" onSubmit={applyPrices}><label>{t("Giá từ (₫)", "Min price (₫)")}<input type="number" min={0} step={1} value={minDraft} onChange={e => setMinDraft(e.target.value)}/></label><label>{t("Đến (₫)", "Max price (₫)")}<input type="number" min={0} step={1} value={maxDraft} onChange={e => setMaxDraft(e.target.value)}/></label><button className="btn btn-secondary" type="submit">{t("Áp dụng", "Apply")}</button></form>
        <button type="button" className="btn btn-secondary" onClick={() => {setMinDraft("");setMaxDraft("");setPriceError("");update({genre:"",min:"",max:"",sort:""});}}>{t("Xóa bộ lọc", "Clear filters")}</button>
      </div>
      {priceError && <p role="alert" className="alert">{priceError}</p>}
      <p className="catalog-status muted" role="status">{loading ? t("Đang tải danh sách…", "Loading catalog…") : products ? `${filtered.length} ${t("sản phẩm", "products")}` : t("Không có dữ liệu", "No data")}</p>
      {error && <div className="alert" role="alert">{t("Không tải được danh sách:", "Failed to load catalog:")} {error}{products && t(" (đang hiển thị dữ liệu lần tải trước)", " (showing previous data)")} <button className="btn btn-secondary" onClick={() => setRetry(n => n+1)}>{t("Thử lại", "Retry")}</button></div>}
      <div className={`catalog-grid${(!products && loading) || filtered.length > PAGE_SIZE ? " catalog-stable" : ""}`} aria-busy={loading}>
        {!products && loading ? Array.from({length:PAGE_SIZE}, (_,i) => <div className="skeleton" key={i}/>) : items.map(p => <ProductCard key={p.id} product={p}/>)}
        {products && !items.length && <p className="muted empty-state">{t("Không có sản phẩm phù hợp.", "No matching products found.")}</p>}
      </div>
      {products && filtered.length > 0 && <nav className="pagination" aria-label={t("Phân trang sản phẩm", "Product pagination")}><button className="btn btn-secondary" disabled={page <= 1} onClick={() => go(page-1)}>{t("Trước", "Prev")}</button>{pageNumbers(page,lastPage).map(n => typeof n === "number" ? <button key={n} className={`btn btn-secondary${n === page ? " page-current" : ""}`} aria-current={n === page ? "page" : undefined} onClick={() => go(n)}>{n}</button> : <span key={n}>…</span>)}<button className="btn btn-secondary" disabled={page >= lastPage} onClick={() => go(page+1)}>{t("Sau", "Next")}</button></nav>}
    </section>
  </main>;
}
