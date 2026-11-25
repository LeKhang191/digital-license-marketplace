
import { useEffect, useState } from "react";
import type { FormEvent } from "react";

import { useAuth } from "../auth/AuthProvider";
import { listProducts } from "../api/products";

import {
  listLicenseInventory,
  listProductLicenses,
  importLicenses,
  importLicenseFile,
  disableLicense,
} from "../api/licenses";

import type {
  Product,
  LicenseKey,
  LicenseInventory,
  ImportLicensesResponse,
} from "../api/types";

export function LicensePage() {
  const { ready, isLicenseCoordinator } = useAuth();

  const [products, setProducts] = useState<Product[]>([]);
  const [inventory, setInventory] = useState<LicenseInventory[]>([]);
  const [licenses, setLicenses] = useState<LicenseKey[]>([]);

  const [productId, setProductId] = useState("");

  const [mode, setMode] = useState<"manual" | "file">("manual");

  const [rawKeys, setRawKeys] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [fileVersion, setFileVersion] = useState(0);

  const [loading, setLoading] = useState(true);
  const [loadingKeys, setLoadingKeys] = useState(false);
  const [busy, setBusy] = useState(false);

  const [error, setError] = useState("");
  const [detailError, setDetailError] = useState("");
  const [success, setSuccess] = useState("");

  const [result, setResult] =
    useState<ImportLicensesResponse | null>(null);

  const [refreshVersion, setRefreshVersion] = useState(0);

  useEffect(() => {
    if (!ready || !isLicenseCoordinator) return;

    let cancelled = false;

    async function loadDashboard() {
      setLoading(true);

      try {
        const first = await listProducts({ page: 1 });

        const items = [...first.items];

        const lastPage = Math.ceil(
          first.total / first.pageSize
        );

        for (let page = 2; page <= lastPage; page++) {
          const response = await listProducts({ page });
          items.push(...response.items);
        }

        const stock = await listLicenseInventory();

        if (cancelled) return;

        setProducts(items);
        setInventory(stock);

        setProductId((current) => {
          const exists =
            items.some((p) => p.id === current) ||
            stock.some((i) => i.productId === current);

          if (current && exists) return current;

          return items[0]?.id ?? stock[0]?.productId ?? "";
        });
      } catch (e) {
        if (!cancelled) {
          setError(
            e instanceof Error
              ? e.message
              : "Không tải được dữ liệu kho."
          );
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void loadDashboard();

    return () => {
      cancelled = true;
    };
  }, [ready, isLicenseCoordinator, refreshVersion]);

  useEffect(() => {
    setLicenses([]);
    setDetailError("");

    if (!ready || !isLicenseCoordinator || !productId) {
      setLoadingKeys(false);
      return;
    }

    let cancelled = false;

    setLoadingKeys(true);

    listProductLicenses(productId)
      .then((data) => {
        if (!cancelled) setLicenses(data);
      })
      .catch((e: Error) => {
        if (!cancelled) setDetailError(e.message);
      })
      .finally(() => {
        if (!cancelled) setLoadingKeys(false);
      });

    return () => {
      cancelled = true;
    };
  }, [
    ready,
    isLicenseCoordinator,
    productId,
    refreshVersion,
  ]);

  const selectedProduct = products.find(
    (p) => p.id === productId
  );

  const selectedInventory = inventory.find(
    (i) => i.productId === productId
  );

  const totals = inventory.reduce(
    (acc, item) => ({
      total: acc.total + item.total,
      available: acc.available + item.available,
      assigned: acc.assigned + item.assigned,
      disabled: acc.disabled + item.disabled,
    }),
    {
      total: 0,
      available: 0,
      assigned: 0,
      disabled: 0,
    }
  );

  const manualKeys = rawKeys
    .split(/\r?\n/)
    .map((key) => key.trim())
    .filter(Boolean);

  function productName(id: string) {
    return products.find((p) => p.id === id)?.name
      ?? `Sản phẩm ${id.slice(0, 8)} (không mở bán)`;
  }

  function changeProduct(id: string) {
    setProductId(id);
    setRawKeys("");
    setFile(null);
    setFileVersion((v) => v + 1);
    setResult(null);
    setError("");
    setSuccess("");
  }

  function changeMode(value: "manual" | "file") {
    setMode(value);
    setRawKeys("");
    setFile(null);
    setFileVersion((v) => v + 1);
    setResult(null);
    setError("");
    setSuccess("");
  }

  async function handleImport(
    e: FormEvent<HTMLFormElement>
  ) {
    e.preventDefault();

    if (!isLicenseCoordinator || busy || loading) return;

    setError("");
    setSuccess("");
    setResult(null);

    if (!selectedProduct?.isActive) {
      setError("Vui lòng chọn sản phẩm đang mở bán.");
      return;
    }

    if (mode === "manual") {
      if (manualKeys.length === 0) {
        setError("Vui lòng nhập ít nhất một Key.");
        return;
      }

      if (manualKeys.length > 1000) {
        setError("Chỉ được nhập tối đa 1.000 Key mỗi lần.");
        return;
      }
    }

    if (mode === "file") {
      if (!file) {
        setError("Vui lòng chọn file CSV hoặc TXT.");
        return;
      }

      if (!/\.(csv|txt)$/i.test(file.name)) {
        setError("Chỉ hỗ trợ file CSV và TXT.");
        return;
      }

      if (file.size === 0 || file.size > 1048576) {
        setError("File phải có dữ liệu và không vượt quá 1 MB.");
        return;
      }
    }

    setBusy(true);

    try {
      const response =
        mode === "manual"
          ? await importLicenses({
              productId,
              keys: manualKeys,
            })
          : await importLicenseFile(productId, file!);

      setResult(response);

      setSuccess(
        `Đã nhập thành công ${response.imported} Key.`
      );

      setRawKeys("");
      setFile(null);
      setFileVersion((v) => v + 1);

      setRefreshVersion((v) => v + 1);
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "Không thể nhập License Key."
      );
    } finally {
      setBusy(false);
    }
  }

  async function handleDisable(license: LicenseKey) {
    if (
      !isLicenseCoordinator ||
      busy ||
      loading ||
      loadingKeys ||
      license.status !== "AVAILABLE"
    ) return;

    const confirmed = window.confirm(
      "Bạn có chắc muốn vô hiệu hóa License Key này?"
    );

    if (!confirmed) return;

    setBusy(true);
    setError("");
    setSuccess("");

    try {
      await disableLicense(license.id);

      setSuccess("Đã vô hiệu hóa License Key.");

      setRefreshVersion((v) => v + 1);
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "Không thể vô hiệu hóa License Key."
      );
    } finally {
      setBusy(false);
    }
  }

  if (!ready) {
    return (
      <div className="container">
        <p className="muted">Đang tải...</p>
      </div>
    );
  }

  if (!isLicenseCoordinator) {
    return (
      <div className="container">
        <div className="alert">
          Bạn không có quyền truy cập trang này.
        </div>
      </div>
    );
  }

  return (
    <div className="container">
      <h1>Quản lý kho License Key</h1>

      <p className="muted">
        License Coordinator Dashboard
      </p>

      <div className="grid admin-summary license-summary">
        <div className="card">
          <h2>Tổng License Key</h2>
          <strong>{loading ? "—" : totals.total}</strong>
        </div>

        <div className="card">
          <h2>Khả dụng</h2>
          <strong className="license-available">
            {loading ? "—" : totals.available}
          </strong>
        </div>

        <div className="card">
          <h2>Đã cấp phát</h2>
          <strong className="license-assigned">
            {loading ? "—" : totals.assigned}
          </strong>
        </div>

        <div className="card">
          <h2>Vô hiệu hóa</h2>
          <strong className="license-disabled">
            {loading ? "—" : totals.disabled}
          </strong>
        </div>
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

      <form
        className="card admin-form"
        onSubmit={handleImport}
      >
        <h2>Nhập License Key</h2>

        <fieldset disabled={busy || loading}>
          <div className="license-form">
            <label>
              Sản phẩm

              <select
                required
                value={productId}
                onChange={(e) =>
                  changeProduct(e.target.value)
                }
              >
                <option value="">
                  Chọn sản phẩm
                </option>

                {products.map((product) => (
                  <option
                    key={product.id}
                    value={product.id}
                  >
                    {product.name}
                  </option>
                ))}

                {inventory
                  .filter((item) =>
                    !products.some(
                      (p) => p.id === item.productId
                    )
                  )
                  .map((item) => (
                    <option
                      key={item.productId}
                      value={item.productId}
                    >
                      {productName(item.productId)}
                    </option>
                  ))}
              </select>
            </label>

            {productId && (
              <p className="muted">
                Tồn kho hiện tại:{" "}
                {selectedInventory?.available ?? 0} Key
                khả dụng
              </p>
            )}

            {!selectedProduct && productId && (
              <p className="muted">
                Sản phẩm không mở bán.
                Chỉ có thể xem kho, chưa thể nhập thêm Key.
              </p>
            )}

            <div className="row">
              <button
                type="button"
                className={
                  mode === "manual"
                    ? "btn"
                    : "btn btn-secondary"
                }
                onClick={() => changeMode("manual")}
              >
                Nhập thủ công
              </button>

              <button
                type="button"
                className={
                  mode === "file"
                    ? "btn"
                    : "btn btn-secondary"
                }
                onClick={() => changeMode("file")}
              >
                Import dataset
              </button>
            </div>

            {mode === "manual" ? (
              <label>
                Danh sách License Key

                <textarea
                  rows={7}
                  value={rawKeys}
                  autoComplete="off"
                  spellCheck={false}
                  onChange={(e) =>
                    setRawKeys(e.target.value)
                  }
                  placeholder={
                    "DEMO-KEY-00001\n" +
                    "DEMO-KEY-00002\n" +
                    "DEMO-KEY-00003"
                  }
                />

                <span className="muted">
                  Mỗi dòng một Key.
                  {` ${manualKeys.length}/1000 Key`}
                </span>
              </label>
            ) : (
              <div className="license-upload">
                <label>
                  Chọn dataset CSV hoặc TXT

                  <input
                    key={fileVersion}
                    type="file"
                    accept=".csv,.txt"
                    onChange={(e) =>
                      setFile(e.target.files?.[0] ?? null)
                    }
                  />
                </label>

                <p className="muted">
                  Tối đa 1 MB và 1.000 Key mỗi lần.
                </p>

                {file && (
                  <p>
                    File đã chọn: <strong>{file.name}</strong>
                  </p>
                )}

                <div className="muted">
                  CSV: một cột license_key.
                  <br />
                  TXT: mỗi dòng một Key.
                </div>
              </div>
            )}

            <div className="row">
              <button
                type="submit"
                className="btn"
                disabled={
                  busy ||
                  loading ||
                  !selectedProduct?.isActive ||
                  (mode === "manual"
                    ? manualKeys.length === 0
                    : !file)
                }
              >
                {busy
                  ? "Đang xử lý..."
                  : "Nhập Key vào kho"}
              </button>
            </div>
          </div>
        </fieldset>
      </form>

      {result && (
        <div className="card license-import-result">
          <h2>Kết quả nhập kho</h2>

          <div className="grid license-summary">
            <div>
              <div className="muted">Tổng</div>
              <strong>{result.total}</strong>
            </div>

            <div>
              <div className="muted">Đã nhập</div>
              <strong className="license-available">
                {result.imported}
              </strong>
            </div>

            <div>
              <div className="muted">Trùng lặp</div>
              <strong className="license-duplicate">
                {result.duplicates}
              </strong>
            </div>

            <div>
              <div className="muted">Không hợp lệ</div>
              <strong className="license-disabled">
                {result.invalid}
              </strong>
            </div>
          </div>
        </div>
      )}

      <div className="card admin-table-container">
        <div className="row admin-toolbar">
          <h2>Tồn kho theo sản phẩm</h2>

          <span className="spacer" />

          <button
            type="button"
            className="refresh-btn"
            title="Làm mới dữ liệu"
            aria-label="Làm mới dữ liệu"
            aria-busy={loading || loadingKeys}
            disabled={busy || loading || loadingKeys}
            onClick={() => {
              setError("");
              setSuccess("");
              setRefreshVersion((v) => v + 1);
            }}
          >
            <svg
              className={
                loading || loadingKeys
                  ? "refresh-icon spinning"
                  : "refresh-icon"
              }
              width="20"
              height="20"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <path d="M20 7v5h-5" />
              <path d="M4 17v-5h5" />
              <path d="M6.1 6.1A8.3 8.3 0 0 1 20 12" />
              <path d="M4 12a8.3 8.3 0 0 0 13.9 5.9" />
            </svg>
          </button>
        </div>

        {loading ? (
          <p className="muted">Đang tải tồn kho...</p>
        ) : inventory.length === 0 ? (
          <p className="muted">
            Kho chưa có License Key.
          </p>
        ) : (
          <table className="admin-table">
            <thead>
              <tr>
                <th>Sản phẩm</th>
                <th>Tổng Key</th>
                <th>Khả dụng</th>
                <th>Đã cấp</th>
                <th>Vô hiệu hóa</th>
                <th>Thao tác</th>
              </tr>
            </thead>

            <tbody>
              {inventory.map((item) => (
                <tr key={item.productId}>
                  <td>
                    <strong>
                      {productName(item.productId)}
                    </strong>
                  </td>

                  <td>{item.total}</td>

                  <td className="license-available">
                    {item.available}
                  </td>

                  <td>{item.assigned}</td>

                  <td>{item.disabled}</td>

                  <td>
                    <button
                      className="btn btn-secondary"
                      disabled={busy}
                      onClick={() => {
                        changeProduct(item.productId);

                        document
                          .getElementById("license-details")
                          ?.scrollIntoView({
                            behavior: "smooth",
                          });
                      }}
                    >
                      Xem kho
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div
        id="license-details"
        className="card admin-table-container"
      >
        <h2>Danh sách License Key</h2>

        {productId && (
          <p className="muted">
            {productName(productId)}
          </p>
        )}

        {detailError && (
          <div className="alert">{detailError}</div>
        )}

        {!productId ? (
          <p className="muted">
            Chọn sản phẩm để xem danh sách Key.
          </p>
        ) : loadingKeys ? (
          <p className="muted">Đang tải...</p>
        ) : licenses.length === 0 ? (
          !detailError && (
            <p className="muted">
              Sản phẩm chưa có License Key.
            </p>
          )
        ) : (
          <>
            <p className="muted">
              Hiển thị {licenses.length} bản ghi,
              tối đa 1.000 Key.
            </p>

            <table className="admin-table">
              <thead>
                <tr>
                  <th>License ID</th>
                  <th>Trạng thái</th>
                  <th>Ngày nhập</th>
                  <th>Ngày cấp phát</th>
                  <th>Thao tác</th>
                </tr>
              </thead>

              <tbody>
                {licenses.map((license) => (
                  <tr key={license.id}>
                    <td>
                      <code title={license.id}>
                        {license.id.slice(0, 8)}
                      </code>
                    </td>

                    <td>
                      <span
                        className={
                          "license-status " +
                          license.status.toLowerCase()
                        }
                      >
                        {license.status === "AVAILABLE"
                          ? "Khả dụng"
                          : license.status === "ASSIGNED"
                            ? "Đã cấp phát"
                            : "Vô hiệu hóa"}
                      </span>
                    </td>

                    <td>
                      {new Date(
                        license.createdAtUtc
                      ).toLocaleString("vi-VN")}
                    </td>

                    <td>
                      {license.assignedAtUtc
                        ? new Date(
                            license.assignedAtUtc
                          ).toLocaleString("vi-VN")
                        : "—"}
                    </td>

                    <td>
                      <button
                        className="btn btn-danger"
                        disabled={
                          busy ||
                          loading ||
                          loadingKeys ||
                          license.status !== "AVAILABLE"
                        }
                        onClick={() =>
                          void handleDisable(license)
                        }
                      >
                        Vô hiệu hóa
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </div>
    </div>
  );
}
