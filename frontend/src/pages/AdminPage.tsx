
import { useEffect, useState } from "react";
import type { FormEvent } from "react";

import { useAuth } from "../auth/AuthProvider";

import {
  listManagedProducts,
  createProduct,
  updateProduct,
  deleteProduct,
} from "../api/products";

import type {
  Product,
  ProductUpsertRequest,
} from "../api/types";

import { vnd } from "../components/format";

const EMPTY_FORM: ProductUpsertRequest = {
  name: "",
  slug: "",
  description: "",
  category: "Game",
  price: 0,
  isActive: true,
};

function generateSlug(value: string) {
  return value
    .toLowerCase()
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/đ/g, "d")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

export function AdminPage() {
  const { ready, isCatalogManager } = useAuth();

  const [products, setProducts] = useState<Product[]>([]);
  const [form, setForm] = useState<ProductUpsertRequest>({
    ...EMPTY_FORM,
  });

  const [editingId, setEditingId] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  async function loadProducts() {
    setLoading(true);

    try {
      const first = await listManagedProducts({ page: 1 });

      const items = [...first.items];
      const lastPage = Math.ceil(first.total / first.pageSize);

      for (let page = 2; page <= lastPage; page++) {
        const result = await listManagedProducts({ page });
        items.push(...result.items);
      }

      setProducts(items);
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "Không tải được sản phẩm",
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (ready && isCatalogManager) {
      void loadProducts();
    }
  }, [ready, isCatalogManager]);

  function openCreateForm() {
    setForm({ ...EMPTY_FORM });
    setEditingId(null);
    setError("");
    setSuccess("");
    setShowForm(true);
  }

  function openEditForm(product: Product) {
    setForm({
      name: product.name,
      slug: product.slug,
      description: product.description,
      category: product.category,
      price: product.price,
      isActive: product.isActive,
    });

    setEditingId(product.id);
    setError("");
    setSuccess("");
    setShowForm(true);
  }

  function closeForm() {
    setShowForm(false);
    setEditingId(null);
    setForm({ ...EMPTY_FORM });
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();

    setError("");
    setSuccess("");

    if (!form.name.trim() || !form.slug.trim()) {
      setError("Vui lòng nhập tên và slug sản phẩm.");
      return;
    }

    if (!Number.isFinite(form.price) || form.price < 0) {
      setError("Giá sản phẩm không hợp lệ.");
      return;
    }

    setSaving(true);

    try {
      if (editingId) {
        await updateProduct(editingId, form);
        setSuccess("Cập nhật sản phẩm thành công.");
      } else {
        await createProduct(form);
        setSuccess("Tạo sản phẩm thành công.");
      }

      closeForm();
      await loadProducts();
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "Không thể lưu sản phẩm",
      );
    } finally {
      setSaving(false);
    }
  }

  async function toggleProductVisibility(product: Product) {
    setError("");
    setSuccess("");
    setSaving(true);

    try {
      await updateProduct(product.id, {
        name: product.name,
        slug: product.slug,
        description: product.description,
        category: product.category,
        price: product.price,
        isActive: !product.isActive,
      });

      setSuccess(
        product.isActive
          ? "Đã ẩn sản phẩm khỏi cửa hàng."
          : "Đã mở bán lại sản phẩm.",
      );

      if (editingId === product.id) {
        setForm((old) => ({
          ...old,
          isActive: !product.isActive,
        }));
      }

      await loadProducts();
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "Không thể thay đổi trạng thái sản phẩm.",
      );
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(product: Product) {
    const confirmed = window.confirm(
      `Bạn có chắc muốn xóa "${product.name}"?`,
    );

    if (!confirmed) return;

    setError("");
    setSuccess("");
    setSaving(true);

    try {
      await deleteProduct(product.id);

      if (editingId === product.id) {
        closeForm();
      }

      setSuccess("Xóa sản phẩm thành công.");
      await loadProducts();
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "Không thể xóa sản phẩm",
      );
    } finally {
      setSaving(false);
    }
  }

  if (!ready) {
    return (
      <div className="container">
        <p className="muted">Đang tải...</p>
      </div>
    );
  }

  if (!isCatalogManager) {
    return (
      <div className="container">
        <div className="alert">
          Bạn không có quyền truy cập trang này.
        </div>
      </div>
    );
  }

  const activeCount = products.filter((p) => p.isActive).length;
  const inactiveCount = products.length - activeCount;

  return (
    <div className="container">
      <h1>Quản lý danh mục sản phẩm</h1>

      <div className="grid admin-summary">
        <div className="card">
          <h2>Tổng sản phẩm</h2>
          <strong>{products.length}</strong>
        </div>

        <div className="card">
          <h2>Đang mở bán</h2>
          <strong>{activeCount}</strong>
        </div>

        <div className="card">
          <h2>Đã ẩn</h2>
          <strong>{inactiveCount}</strong>
        </div>
      </div>

      <div className="row admin-toolbar">
        <h2>Quản lý sản phẩm</h2>

        <div className="spacer" />

        <button
          className="btn"
          onClick={openCreateForm}
          disabled={saving}
        >
          + Tạo sản phẩm
        </button>
      </div>

      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}

      {success && (
        <div className="alert-success" role="status">
          {success}
        </div>
      )}

      {showForm && (
        <form
          className="card admin-form"
          onSubmit={handleSubmit}
        >
          <h2>
            {editingId
              ? "Chỉnh sửa sản phẩm"
              : "Tạo sản phẩm mới"}
          </h2>

          <fieldset disabled={saving}>
            <div className="admin-form-grid">
              <label>
                Tên sản phẩm
                <input
                  required
                  minLength={2}
                  maxLength={160}
                  value={form.name}
                  onChange={(e) =>
                    setForm((old) => ({
                      ...old,
                      name: e.target.value,
                      slug: editingId
                        ? old.slug
                        : generateSlug(e.target.value),
                    }))
                  }
                  placeholder="Red Dead Redemption 2"
                />
              </label>

              <label>
                Slug
                <input
                  required
                  maxLength={180}
                  pattern="[a-z0-9]+(-[a-z0-9]+)*"
                  value={form.slug}
                  onChange={(e) =>
                    setForm((old) => ({
                      ...old,
                      slug: e.target.value,
                    }))
                  }
                  placeholder="red-dead-redemption-2"
                />
              </label>

              <label>
                Danh mục
                <select
                  value={form.category}
                  onChange={(e) =>
                    setForm((old) => ({
                      ...old,
                      category: e.target.value,
                    }))
                  }
                >
                  <option value="Game">Game</option>
                  <option value="Software">Phần mềm</option>
                </select>
              </label>

              <label>
                Giá (VNĐ)
                <input
                  type="number"
                  required
                  min="0"
                  max="9999999999.99"
                  step="1"
                  value={form.price}
                  onChange={(e) =>
                    setForm((old) => ({
                      ...old,
                      price: Number(e.target.value),
                    }))
                  }
                />
              </label>

              <label className="admin-form-full">
                Mô tả
                <textarea
                  rows={4}
                  maxLength={2000}
                  value={form.description}
                  onChange={(e) =>
                    setForm((old) => ({
                      ...old,
                      description: e.target.value,
                    }))
                  }
                  placeholder="Mô tả sản phẩm..."
                />
              </label>

              <label className="admin-checkbox admin-form-full">
                <input
                  type="checkbox"
                  checked={form.isActive}
                  onChange={(e) =>
                    setForm((old) => ({
                      ...old,
                      isActive: e.target.checked,
                    }))
                  }
                />
                Hiển thị sản phẩm trên cửa hàng
              </label>
            </div>

            <div className="row">
              <button type="submit" className="btn">
                {saving
                  ? "Đang lưu..."
                  : editingId
                    ? "Lưu thay đổi"
                    : "Tạo sản phẩm"}
              </button>

              <button
                type="button"
                className="btn btn-secondary"
                onClick={closeForm}
              >
                Hủy
              </button>
            </div>
          </fieldset>
        </form>
      )}

      <div className="card admin-table-container">
        {loading ? (
          <p className="muted">Đang tải sản phẩm...</p>
        ) : products.length === 0 ? (
          <p className="muted">
            Chưa có sản phẩm nào.
            Hãy tạo sản phẩm đầu tiên.
          </p>
        ) : (
          <table className="admin-table">
            <thead>
              <tr>
                <th>Tên sản phẩm</th>
                <th>Danh mục</th>
                <th>Giá</th>
                <th>Trạng thái</th>
                <th>Thao tác</th>
              </tr>
            </thead>

            <tbody>
              {products.map((product) => (
                <tr key={product.id}>
                  <td>
                    <strong>{product.name}</strong>
                    <div className="muted">
                      {product.slug}
                    </div>
                  </td>

                  <td>{product.category}</td>

                  <td>{vnd(product.price)}</td>

                  <td>
                    <span
                      style={{
                        color: product.isActive
                          ? "#16a34a"
                          : "#6b7280",
                        fontWeight: 600,
                      }}
                    >
                      {product.isActive
                        ? "Đang mở bán"
                        : "Đã ẩn"}
                    </span>
                  </td>

                  <td>
                    <div className="row">
                      <button
                        className="btn btn-secondary"
                        disabled={saving}
                        onClick={() => openEditForm(product)}
                      >
                        Sửa
                      </button>

                      <button
                        className="btn btn-secondary"
                        disabled={saving}
                        onClick={() =>
                          void toggleProductVisibility(product)
                        }
                      >
                        {product.isActive ? "Ẩn" : "Mở bán"}
                      </button>

                      <button
                        className="btn btn-danger"
                        disabled={saving}
                        onClick={() =>
                          void handleDelete(product)
                        }
                      >
                        Xóa
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
