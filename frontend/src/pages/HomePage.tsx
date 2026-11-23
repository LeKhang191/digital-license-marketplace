import { useEffect, useState } from "react";
import { listProducts } from "../api/products";
import type { PagedProducts } from "../api/types";
import { ProductCard } from "../components/ProductCard";

export function HomePage() {
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState("");
  const [page, setPage] = useState(1);
  const [data, setData] = useState<PagedProducts | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setError(null);
    // Chờ 300ms sau khi người dùng ngừng gõ rồi mới gọi API
    let active = true;
    const t = setTimeout(() => {
      listProducts({ search, category, page })
        .then((result) => { if (active) setData(result); })
        .catch((e: Error) => { if (active) setError(e.message); });
    }, 300);
    return () => {
      active = false;
      clearTimeout(t);
    };
  }, [search, category, page]);

  const lastPage = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <div className="container">
      <h1>Game và phần mềm bản quyền</h1>

      <div className="row" style={{ marginBottom: 24 }}>
        <input
          placeholder="Tìm kiếm sản phẩm..."
          value={search}
          onChange={(e) => { setSearch(e.target.value); setPage(1); }}
        />
        <select value={category} onChange={(e) => { setCategory(e.target.value); setPage(1); }}>
          <option value="">Tất cả danh mục</option>
          <option value="Game">Game</option>
          <option value="Software">Phần mềm</option>
        </select>
      </div>

      {error && <div className="alert">Không tải được danh sách: {error}</div>}
      {!data && !error && <p className="muted">Đang tải...</p>}
      {data && data.items.length === 0 && <p className="muted">Không có sản phẩm phù hợp.</p>}

      {data && (
        <>
          <div className="grid">
            {data.items.map((p) => <ProductCard key={p.id} product={p} />)}
          </div>
          <div className="row" style={{ marginTop: 24 }}>
            <button className="btn btn-secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Trước</button>
            <span className="muted">Trang {page} / {lastPage}</span>
            <button className="btn btn-secondary" disabled={page >= lastPage} onClick={() => setPage(page + 1)}>Sau</button>
          </div>
        </>
      )}
    </div>
  );
}
