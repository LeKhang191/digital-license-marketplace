
import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { SupplierIntegration } from "./SupplierIntegration";
import { SupplierDirectory } from "./SupplierDirectory";
import { previewLicenseFile, downloadLicenseTemplate } from "./licenseFilePreview";
import { NavLink, useSearchParams } from "react-router-dom";

import { useAuth } from "../auth/AuthProvider";
import { listProducts } from "../api/products";

import {
  listLicenseInventory,
  listProductLicenses,
  importLicenses,
  importLicenseFile,
  disableLicense,
  getKinguinQuote,
  enqueueKinguinMockOrder,
  getKinguinMockOrder,
  type KinguinMockOrder,
  type KinguinQuote,
  listImportBatches,
  listSuppliers,
  type Supplier,
  type ImportBatch,
} from "../api/licenses";

import type {
  Product,
  LicenseKey,
  LicenseInventory,
  ImportLicensesResponse,
} from "../api/types";

export function LicensePage({ view = "inventory" }: { view?: "inventory" | "import" | "supplier" }) {
  const isInventory = view === "inventory";
  const isImport = view === "import";
  const isSupplier = view === "supplier";
  const [params] = useSearchParams();
  const [sourceId, setSourceId] = useState(() => params.get("supplier") ?? "");
  const [sources, setSources] = useState<Supplier[]>([]);
  const [sourcesError, setSourcesError] = useState("");
  const [sourcesLoading, setSourcesLoading] = useState(true);
  const [showKinguin, setShowKinguin] = useState(true);
  const [kinguinTab, setKinguinTab] = useState<"purchase" | "history" | "settings">("purchase");
  const [integrationBusy, setIntegrationBusy] = useState(false);
  const [sourceRef, setSourceRef] = useState("");
  const [unitCost, setUnitCost] = useState("");
  const [batches, setBatches] = useState<ImportBatch[]>([]);
  const [batchError, setBatchError] = useState("");
  const [batchFilter, setBatchFilter] = useState("");
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [showTestProducts, setShowTestProducts] = useState(false);
  const [lookupId, setLookupId] = useState("");
  const [lookupResult, setLookupResult] = useState<KinguinMockOrder | null>(null);
  const [lookupError, setLookupError] = useState("");
  const [lookupBusy, setLookupBusy] = useState(false);
  const { ready, isLicenseCoordinator } = useAuth();
  useEffect(() => {
    if (!ready || !isLicenseCoordinator || !isImport) return;
    let active = true;
    setSourcesLoading(true);
    listSuppliers().then(data => { if (active) setSources(data); })
      .catch(e => { if (active) setSourcesError(e instanceof Error ? e.message : "Không tải được nhà cung cấp"); })
      .finally(() => { if (active) setSourcesLoading(false); });
    return () => { active = false; };
  }, [ready, isLicenseCoordinator, isImport]);


  const [products, setProducts] = useState<Product[]>([]);
  const [inventory, setInventory] = useState<LicenseInventory[]>([]);
  const [licenses, setLicenses] = useState<LicenseKey[]>([]);

  const [productId, setProductId] = useState("");
  const [supplierQuantity, setSupplierQuantity] = useState(1);
  const [supplierQuote, setSupplierQuote] = useState<KinguinQuote | null>(null);
  const [supplierError, setSupplierError] = useState("");
  const [supplierLoading, setSupplierLoading] = useState(false);
  const [supplierRefresh, setSupplierRefresh] = useState(0);
  const [mockRequest, setMockRequest] = useState<{ id: string; product: string; quantity: number } | null>(null);
  const [mockResult, setMockResult] = useState<KinguinMockOrder | null>(null);
  const [mockBusy, setMockBusy] = useState(false);
  const [mockError, setMockError] = useState("");
  const mockDialog = useRef<HTMLDialogElement>(null);
  const pollAttempts = useRef(0);
  const terminal = !!mockResult && mockResult.status !== "MOCK_PENDING";
  useEffect(() => {
    if (!isSupplier || !terminal) return;
    if (!mockDialog.current?.open) mockDialog.current?.showModal();
    setRefreshVersion(v => v + 1);
    setSupplierRefresh(v => v + 1);
  }, [isSupplier, terminal]);
  useEffect(() => {
    if (!isSupplier || !mockRequest || mockBusy || mockError || mockResult?.status !== "MOCK_PENDING") return;
    if (pollAttempts.current >= 30) { setMockError("Đơn vẫn đang xử lý. Kiểm tra lại trạng thái, không tạo đơn mới."); return; }
    const timer = window.setTimeout(() => { pollAttempts.current++; void refreshMockOrder(); }, 2500);
    return () => window.clearTimeout(timer);
  }, [isSupplier, mockRequest, mockBusy, mockResult, mockError]);
  function finishMockOrder(history = false) {
    mockDialog.current?.close();
    pollAttempts.current = 0;
    setMockRequest(null); setMockResult(null); setMockError("");
    setSupplierRefresh(v => v + 1);
    if (history) setKinguinTab("history");
  }

  async function submitMockOrder() {
    if (integrationBusy || mockBusy || !isSupplier || (!mockRequest && (supplierLoading || supplierQuote?.mode !== "Mock" || !supplierQuote.canFulfill))) return;
    const request = mockRequest ?? { id: crypto.randomUUID(), product: productId, quantity: supplierQuantity };
    setMockRequest(request);
    setMockBusy(true);
    setMockError("");
    try {
      setMockResult(await enqueueKinguinMockOrder(request.id, request.product, request.quantity));
      setSupplierRefresh((v) => v + 1);
    } catch (e) {
      setMockError(e instanceof Error ? e.message : "Không thể gửi đơn mô phỏng");
    } finally { setMockBusy(false); }
  }

  async function refreshMockOrder() {
    if (!mockRequest || mockBusy) return;
    setMockBusy(true);
    setMockError("");
    try {
      setMockResult(await getKinguinMockOrder(mockRequest.id));
      setSupplierRefresh((v) => v + 1);
    } catch (e) {
      setMockError(e instanceof Error ? e.message : "Không thể tra cứu đơn mô phỏng");
    } finally { setMockBusy(false); }
  }


  useEffect(() => {
    setSupplierQuote(null);
    setSupplierError("");
    setSupplierLoading(false);
    if (!isSupplier || !ready || !isLicenseCoordinator || !productId || !showKinguin || kinguinTab !== "purchase" || integrationBusy) return;
    const controller = new AbortController();
    setSupplierLoading(true);
    getKinguinQuote(productId, supplierQuantity, controller.signal)
      .then((data) => { if (!controller.signal.aborted) setSupplierQuote(data); })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setSupplierError(error instanceof Error ? error.message : "Không thể tra cứu Kinguin");
      })
      .finally(() => { if (!controller.signal.aborted) setSupplierLoading(false); });
    return () => controller.abort();
  }, [isSupplier, ready, isLicenseCoordinator, productId, supplierQuantity, supplierRefresh, showKinguin, kinguinTab, integrationBusy]);

  const [mode, setMode] = useState<"manual" | "file">("manual");

  const [rawKeys, setRawKeys] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [fileVersion, setFileVersion] = useState(0);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [preview, setPreview] = useState<ReturnType<typeof previewLicenseFile> | null>(null);
  const [previewError, setPreviewError] = useState("");
  const [previewLoading, setPreviewLoading] = useState(false);
  useEffect(() => {
    let active = true;
    setPreview(null);
    setPreviewError("");
    setPreviewLoading(false);
    if (!file) return;
    if (!/\.(csv|txt)$/i.test(file.name) || !file.size || file.size > 1048576) {
      setPreviewError("Chọn file CSV/TXT có dữ liệu, tối đa 1 MB.");
      return;
    }
    setPreviewLoading(true);
    file.text().then(text => {
      if (active) setPreview(previewLicenseFile(text, /\.csv$/i.test(file.name)));
    }).catch(e => { if (active) setPreviewError(e instanceof Error ? e.message : "Không đọc được file."); })
      .finally(() => { if (active) setPreviewLoading(false); });
    return () => { active = false; };
  }, [file]);

  function chooseFile(next: File | null) {
    if (next === file) return;
    setPreview(null);
    setPreviewError("");
    setPreviewLoading(!!next);
    setFile(next);
    setResult(null);
    setError("");
    setSuccess("");
  }

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
    if (!ready || !isLicenseCoordinator || !isImport) return;
    let active = true;
    setBatchError("");
    listImportBatches().then(items => { if (active) setBatches(items); })
      .catch(e => { if (active) setBatchError(e instanceof Error ? e.message : "Không tải được lịch sử"); });
    return () => { active = false; };
  }, [ready, isLicenseCoordinator, isImport, refreshVersion]);

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

          return isInventory ? (stock[0]?.productId ?? "") : "";
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
  }, [isInventory, ready, isLicenseCoordinator, refreshVersion]);

  useEffect(() => {
    setLicenses([]);
    setDetailError("");

    if (!isInventory || !ready || !isLicenseCoordinator || !productId) {
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
    isInventory,
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
    if (mockRequest || mockBusy || integrationBusy) return;
    setSupplierQuote(null);
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
      if (previewLoading || !preview || preview.candidates === 0 || previewError) {
        setError("Vui lòng kiểm tra file trước khi nhập kho."); return;
      }
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

    const cost = unitCost.trim() ? Number(unitCost) : undefined;
    if (!sources.some(s => s.id === sourceId && s.isActive) || (cost !== undefined && (!Number.isFinite(cost) || cost < 0 || cost > 1e12 || Math.round(cost * 100) / 100 !== cost))) {
      setError("Vui lòng nhập nguồn hàng và giá vốn hợp lệ (VND)."); return;
    }
    const metadata = { supplierId: sourceId, reference: sourceRef.trim() || undefined, unitCostVnd: cost };
    setBusy(true);

    try {
      const response =
        mode === "manual"
          ? await importLicenses({
              productId,
              keys: manualKeys,
              ...metadata,
            })
          : await importLicenseFile(productId, file!, metadata);

      setResult(response);
      setSourceRef("");

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

  const matches = (name: string) => name.toLocaleLowerCase("vi").includes(search.trim().toLocaleLowerCase("vi"));
  const isTestProduct = (p: Product) => /^TEST(?:\s|$)/i.test(p.name) || /^(?:license-vault-test|allocation-)/i.test(p.slug);
  const filteredProducts = products.filter(p => p.isActive && (showTestProducts || !isTestProduct(p)) && matches(p.name + " " + p.slug));
  const filteredInventory = inventory.filter(item => matches(productName(item.productId) + " " + item.productId));
  const filteredLicenses = licenses.filter(item => statusFilter === "all" || item.status === statusFilter);

  return (
    <div className="container license-workspace">
      <h1>{isInventory ? "Quản lý kho License" : isImport ? "Nhập kho License" : "Quản lý nhà cung cấp"}</h1>

      <p className="muted">
        {isInventory ? "Theo dõi tồn kho và trạng thái license." : isImport ? "Nhập key cho sản phẩm đang mở bán trong Catalog." : "Quản lý nguồn nhập hàng và các kết nối API đã tích hợp."}
      </p>

      <nav className="license-nav" aria-label="Quản lý license">
        <NavLink end to="/licenses">Xem kho</NavLink>
        <NavLink to="/licenses/import">Nhập kho</NavLink>
        <NavLink to="/licenses/suppliers">Nhà cung cấp</NavLink>
      </nav>

      {isInventory && <div className="grid admin-summary license-summary">
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
      </div>}

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

      {isImport && <form
        className="card admin-form"
        onSubmit={handleImport}
      >
        <h2>Nhập License Key</h2>
        <p className="muted">Chọn nguồn hàng bất kỳ; không cần API nhà cung cấp. Chỉ nhập key hợp pháp hoặc key demo được đánh dấu rõ.</p>

        <fieldset disabled={busy || loading}>
          <div className="license-form">
            <label>Nhà cung cấp / nguồn hàng
              <select required value={sourceId} onChange={e => setSourceId(e.target.value)} disabled={sourcesLoading || !!sourcesError}>
                <option value="">{sourcesLoading ? "Đang tải nguồn hàng..." : "Chọn nhà cung cấp"}</option>
                {sources.filter(s => s.isActive).map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
              {sourcesError && <span role="alert">{sourcesError}</span>}
              <NavLink to="/licenses/suppliers">Quản lý / thêm nhà cung cấp</NavLink>
            </label>
            <label>Mã lô / chứng từ (không bắt buộc)<input maxLength={120} value={sourceRef} onChange={e => setSourceRef(e.target.value)} /></label>
            <label>Giá nhập một key (VND, không bắt buộc)<input type="number" min="0" max="1000000000000" step="0.01" value={unitCost} onChange={e => setUnitCost(e.target.value)} /></label>
            <label>Tìm sản phẩm trong Catalog
              <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Tên game hoặc slug" disabled={!!mockRequest || mockBusy || integrationBusy} />
            </label>
            <label>Sản phẩm đang mở bán
              <select required value={productId} onChange={e => changeProduct(e.target.value)} disabled={loading || !!mockRequest || mockBusy || integrationBusy}>
                <option value="">Chọn sản phẩm</option>
                {selectedProduct && !filteredProducts.some(p => p.id === productId) && <option value={productId}>{selectedProduct.name}</option>}
                {filteredProducts.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </label>
            <label className="license-test-toggle"><input type="checkbox" checked={showTestProducts} onChange={e => setShowTestProducts(e.target.checked)} disabled={!!mockRequest || mockBusy || integrationBusy} /> Hiện sản phẩm kiểm thử (theo tên/slug)</label>
            <p className="muted">Nguồn: Catalog Service · {filteredProducts.length} sản phẩm phù hợp.</p>
            {selectedProduct && <p className="muted">Tồn kho nội bộ: {selectedInventory?.available ?? 0} key khả dụng.</p>}
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
                Nhập từ file
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
              <div className="license-file-panel">
                <div className="license-dropzone"
                  onDragOver={e => { e.preventDefault(); e.dataTransfer.dropEffect = busy || loading ? "none" : "copy"; }}
                  onDrop={e => { e.preventDefault(); if (!busy && !loading && e.dataTransfer.files.length) { setFileVersion(v => v + 1); chooseFile(e.dataTransfer.files[0]); } }}>
                  <svg className="license-upload-icon" width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <path d="M12 16V4m-5 5 5-5 5 5M4 15v5h16v-5" />
                  </svg>
                  <strong>Kéo và thả file vào đây</strong>
                  <span className="muted">hoặc</span>
                  <input hidden ref={fileInputRef} key={fileVersion} type="file" accept=".csv,.txt"
                    onChange={e => { const next = e.currentTarget.files?.[0]; if (next) chooseFile(next); e.currentTarget.value = ""; }} />
                  <button type="button" className="btn license-file-pick" disabled={busy || loading}
                    onClick={() => fileInputRef.current?.click()}>Chọn file từ máy</button>
                  <span className="muted">CSV, TXT · Tối đa 1 MB · 1.000 key</span>
                </div>
                <div className="license-file-templates">
                  <span className="muted">Chưa có file đúng định dạng?</span>
                  <button type="button" className="license-file-link" onClick={() => downloadLicenseTemplate(true)}>↓ Mẫu CSV</button>
                  <button type="button" className="license-file-link" onClick={() => downloadLicenseTemplate(false)}>↓ Mẫu TXT</button>
                </div>
                <small className="muted">CSV: một cột license_key. TXT: mỗi dòng một key. File mẫu chỉ chứa key DEMO.</small>
                {file && <div className="license-file-selected">
                  <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M6 3h8l4 4v14H6zM14 3v5h4M9 12h6m-6 4h6" /></svg>
                  <div className="license-file-name"><strong>{file.name}</strong><small className="muted">{Math.ceil(file.size / 1024)} KB</small></div>
                  <button type="button" className="license-file-remove" aria-label="Bỏ file đã chọn" title="Bỏ file" onClick={() => { chooseFile(null); setFileVersion(v => v + 1); }}>×</button>
                </div>}
                {previewLoading && <p role="status" className="muted">Đang kiểm tra file...</p>}
                {previewError && <p role="alert" className="license-file-error">{previewError}</p>}
                {preview && <div className="license-file-preview" aria-live="polite">
                  <h3>Kết quả kiểm tra</h3>
                  <div className="license-file-stats">
                    <div><strong>{preview.total}</strong><span>Tổng key</span></div>
                    <div className="file-valid"><strong>{preview.candidates}</strong><span>Có thể nhập</span></div>
                    <div className="file-duplicate"><strong>{preview.duplicates}</strong><span>Trùng trong file</span></div>
                    <div className="file-invalid"><strong>{preview.invalid}</strong><span>Không hợp lệ</span></div>
                  </div>
                  {preview.duplicates > 0 && <p className="license-file-warning">Phát hiện {preview.duplicates} key trùng trong file.</p>}
                  {preview.invalid > 0 && <p className="license-file-error">Phát hiện {preview.invalid} key sai định dạng.</p>}
                  <small className="muted">Ước tính tại trình duyệt. Server kiểm tra key trùng trong kho và xác nhận số lượng nhập thực tế.</small>
                </div>}
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
                  sourcesLoading || !!sourcesError || !sources.some(s => s.id === sourceId && s.isActive) ||
                  (mode === "manual"
                    ? manualKeys.length === 0
                    : !file || previewLoading || !preview || !!previewError || preview.candidates === 0)
                }
              >
                {busy
                  ? "Đang xử lý..."
                  : "Nhập key vào kho"}
              </button>
            </div>
          </div>
        </fieldset>
      </form>}

      {isImport && result && (
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

      {isImport && <section className="card admin-table-container">
        <h2>Lịch sử nhập kho</h2>
        <p className="muted">200 lô gần nhất; giá vốn chỉ tính trên số key nhập thành công. Không hiển thị key.</p>
        <label>Lọc nguồn hàng<input value={batchFilter} onChange={e => setBatchFilter(e.target.value)} placeholder="Tên nhà cung cấp" /></label>
        {batchError && <p role="alert">{batchError}</p>}
        <div style={{overflowX: "auto"}}><table className="admin-table"><thead><tr><th>Thời gian / mã lô</th><th>Nguồn hàng</th><th>Sản phẩm</th><th>Phương thức</th><th>Nhập / trùng / lỗi</th><th>Giá vốn (VND)</th></tr></thead><tbody>
          {batches.filter(b => b.supplierName.toLowerCase().includes(batchFilter.trim().toLowerCase())).map(b => <tr key={b.id}>
            <td>{new Date(b.createdAtUtc).toLocaleString("vi-VN")}<br />{b.reference || b.id.slice(0,8)}</td><td>{b.supplierName}</td><td>{productName(b.productId)}</td><td>{b.method}</td><td>{b.imported} / {b.duplicates} / {b.invalid}</td><td>{b.unitCostVnd == null ? "Chưa khai báo" : (b.unitCostVnd * b.imported).toLocaleString("vi-VN")}</td>
          </tr>)}
        </tbody></table></div>
        {!batches.length && !batchError && <p className="muted">Chưa có lô nhập được ghi nhận.</p>}
      </section>}

      {isSupplier && <SupplierDirectory products={products} refreshVersion={refreshVersion} />}
      {isSupplier && <section className="card admin-form">
        <h2>Kết nối API — Kinguin</h2>
        <p className="muted">Tích hợp Kinguin · Giao dịch Mock không sử dụng tiền thật.</p>
        <button type="button" className="btn btn-secondary" disabled={integrationBusy || mockBusy || !!mockRequest} onClick={() => setShowKinguin(v => !v)}>{showKinguin ? "Ẩn công cụ Kinguin" : "Mở công cụ Kinguin"}</button>
      </section>}
      {isSupplier && showKinguin && <section className="card admin-form license-form kinguin-panel">
        <header className="kinguin-heading"><h2>Kinguin</h2><span className="kinguin-tag">{supplierQuote?.mode ?? "Tích hợp nhà cung cấp"}</span></header>
        <nav className="kinguin-tabs" aria-label="Công cụ Kinguin">
          {([ ["purchase", "Mua license"], ["history", "Lịch sử"], ["settings", "Cài đặt"] ] as const).map(([id, label]) => <button key={id} type="button" className={`btn ${kinguinTab === id ? "" : "btn-secondary"}`} aria-pressed={kinguinTab === id} disabled={integrationBusy || mockBusy || (!!mockRequest && id === "settings")} onClick={() => { setSupplierQuote(null); setKinguinTab(id); }}>{label}</button>)}
        </nav>
        {kinguinTab !== "history" && <div className="kinguin-selector">
            {loading && <p role="status">Đang tải sản phẩm...</p>}
            <label>Tìm sản phẩm trong Catalog
              <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Tên game hoặc slug" disabled={!!mockRequest || mockBusy || integrationBusy} />
            </label>
            <label>Sản phẩm đang mở bán
              <select required value={productId} onChange={e => changeProduct(e.target.value)} disabled={loading || !!mockRequest || mockBusy || integrationBusy}>
                <option value="">Chọn sản phẩm</option>
                {selectedProduct && !filteredProducts.some(p => p.id === productId) && <option value={productId}>{selectedProduct.name}</option>}
                {filteredProducts.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </label>
            <label className="license-test-toggle"><input type="checkbox" checked={showTestProducts} onChange={e => setShowTestProducts(e.target.checked)} disabled={!!mockRequest || mockBusy || integrationBusy} /> Hiện sản phẩm kiểm thử</label>
        </div>}
        <SupplierIntegration products={products} product={productId} locked={!!mockRequest || mockBusy}
          tab={kinguinTab} orderVersion={supplierRefresh} onBusy={setIntegrationBusy} onMappingSaved={() => setSupplierRefresh(v => v + 1)} />
        {kinguinTab === "purchase" && productId && <div className="kinguin-purchase">
          {selectedProduct && <div className="kinguin-product">
            {(selectedProduct.steam?.posterImage || selectedProduct.steam?.headerImage) && <img src={selectedProduct.steam?.posterImage || selectedProduct.steam?.headerImage} alt="" loading="lazy" />}
            <div><h3>{selectedProduct.name}</h3><p className="muted">{selectedProduct.steam?.appId ? "Steam · PC" : selectedProduct.category}</p>
              <p>Tồn kho nội bộ: {selectedInventory?.available ?? 0} key</p>
              {supplierQuote?.quote && <p className="kinguin-ok">Đã tìm thấy sản phẩm nhà cung cấp</p>}
            </div>
          </div>}
          {supplierLoading && <p role="status">Đang lấy báo giá…</p>}
          {supplierError && <div role="alert"><p>{supplierError.includes("KINGUIN_MAPPING_NOT_FOUND") ? "Sản phẩm chưa được liên kết với Kinguin." : supplierError}</p>
            <button className="btn btn-secondary" type="button" disabled={!!mockRequest || mockBusy || integrationBusy} onClick={() => setKinguinTab("settings")}>Cài đặt liên kết</button></div>}
          {supplierQuote && <>
            <p className="muted">{supplierQuote.mode === "Mock" ? "Mock · Giá và tồn kho mô phỏng, không dùng tiền thật." : "Sandbox · Chỉ tra cứu, chưa hỗ trợ mua."}</p>
            {supplierQuote.quote ? <div className="kinguin-metrics">
              <div><span className="muted">Giá nhập mỗi key</span><strong>{supplierQuote.quote.price.toFixed(2)} {supplierQuote.currency}</strong></div>
              <div><span className="muted">Tồn kho nhà cung cấp</span><strong>{supplierQuote.quote.textQty} key</strong></div>
            </div> : <p>Không tìm thấy sản phẩm nhà cung cấp.</p>}
          </>}
          <div className="kinguin-quantity"><strong>Số lượng nhập</strong><div>
            <button type="button" className="btn btn-secondary" aria-label="Giảm số lượng" disabled={supplierQuantity <= 1 || !!mockRequest || mockBusy || integrationBusy} onClick={() => { setSupplierQuote(null); setSupplierQuantity(n => n - 1); }}>−</button>
            <span>{supplierQuantity}</span>
            <button type="button" className="btn btn-secondary" aria-label="Tăng số lượng" disabled={supplierQuantity >= 10 || !!mockRequest || mockBusy || integrationBusy} onClick={() => { setSupplierQuote(null); setSupplierQuantity(n => n + 1); }}>+</button>
          </div></div>
          {supplierQuote?.quote && <div className="kinguin-total"><span className="muted">Tổng chi phí {supplierQuote.mode === "Mock" ? "mô phỏng" : "ước tính"}</span><strong>{supplierQuote.estimatedCost == null ? "—" : `${supplierQuote.estimatedCost.toFixed(2)} ${supplierQuote.currency}`}</strong>
            {!supplierQuote.canFulfill && <p role="status">Không đủ tồn kho cho số lượng đã chọn.</p>}
          </div>}
          <button type="button" className="btn btn-secondary kinguin-refresh" disabled={supplierLoading || mockBusy || !!mockRequest || integrationBusy} onClick={() => { setSupplierQuote(null); setSupplierRefresh(v => v + 1); }}>Làm mới báo giá</button>
                {(supplierQuote?.mode === "Mock" || mockRequest) && (
                  <div className="kinguin-order" aria-live="polite">
                    <p className="muted">{supplierQuote?.demoAutoImport ? "Nhập kho demo tự động · Không dùng tiền thật · Key không thể kích hoạt." : "Đơn thử nghiệm cách ly, thanh toán giả lập, không giao key thật."}</p>
                    {!mockRequest && <button className="btn" type="button" disabled={integrationBusy || mockBusy || supplierLoading || supplierQuote?.mode !== "Mock" || !supplierQuote.canFulfill} onClick={() => { if (window.confirm(`Xác nhận mua mô phỏng ${supplierQuantity} key ${selectedProduct?.name ?? ""}? Không dùng tiền thật.`)) void submitMockOrder(); }}>Xác nhận mua mô phỏng</button>}
                    {mockRequest && !terminal && <>
                      <p role="status">{mockResult?.status === "MOCK_PENDING" ? "Đang xử lý đơn, tự động kiểm tra trạng thái…" : mockBusy ? "Đang gửi đơn…" : "Chưa xác định kết quả đơn."}</p>
                      {mockError && <><p>Request ID: <code>{mockRequest.id}</code></p><button className="btn btn-secondary" type="button" disabled={mockBusy} onClick={() => { pollAttempts.current = 0; void refreshMockOrder(); }}>Kiểm tra trạng thái</button>{!mockResult && <button className="btn btn-secondary" type="button" disabled={mockBusy} onClick={submitMockOrder}>Gửi lại cùng Request ID</button>}</>}
                    </>}
                    {mockError && <p role="status">{mockError}</p>}
                  </div>
                )}

        </div>}
                    <dialog ref={mockDialog} className="help-dialog" onCancel={e => { e.preventDefault(); finishMockOrder(); }}>
                      {terminal && mockResult && <div className="dialog-content kinguin-order">
                        <button type="button" className="dialog-close" aria-label="Đóng" onClick={() => finishMockOrder()}>×</button>
                        <h2>{mockResult.status === "MOCK_FULFILLED" ? (mockResult.imported ? "Nhập hàng thành công" : "Mua mô phỏng thành công") : mockResult.status === "MOCK_IMPORT_BLOCKED" ? "Nhập kho bị chặn" : "Nhà cung cấp hết hàng"}</h2>
                        <p>{products.find(p => p.id === mockResult.productId)?.name ?? mockResult.productId} · Kinguin Mock</p>
                        <div className="kinguin-metrics"><div><span>Đã nhập kho</span><strong>{mockResult.imported ?? 0} key</strong></div><div><span>Chi phí mô phỏng</span><strong>{mockResult.totalCost.toFixed(2)} EUR</strong></div></div>
                        <p className="muted">Không dùng tiền thật. Key DEMO không thể kích hoạt game.</p>
                        <button type="button" className="btn" onClick={() => finishMockOrder()}>Tiếp tục mua hàng</button>
                        <button type="button" className="btn btn-secondary" onClick={() => finishMockOrder(true)}>Xem lịch sử đơn mua</button>
                        {!!mockResult.imported && <><NavLink className="btn btn-secondary" to="/licenses" onClick={() => finishMockOrder()}>Xem kho License</NavLink><NavLink className="btn btn-secondary" to="/licenses/import" onClick={() => finishMockOrder()}>Xem lịch sử nhập kho</NavLink></>}
                      </div>}
                    </dialog>
        {kinguinTab === "history" && <section className="card">
          <h3>Tra cứu lại đơn mô phỏng</h3>
          <label>Request ID<input value={lookupId} onChange={e => { setLookupId(e.target.value); setLookupResult(null); setLookupError(""); }} disabled={lookupBusy} placeholder="UUID của đơn mô phỏng" /></label>
          <button type="button" className="btn btn-secondary" disabled={lookupBusy || !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(lookupId.trim())} onClick={async () => {
            setLookupBusy(true); setLookupResult(null); setLookupError("");
            try { setLookupResult(await getKinguinMockOrder(lookupId.trim())); setSupplierRefresh(v => v + 1); }
            catch (e) { setLookupError(e instanceof Error ? e.message : "Không tìm thấy đơn mô phỏng"); }
            finally { setLookupBusy(false); }
          }}>{lookupBusy ? "Đang tải..." : "Kiểm tra trạng thái"}</button>
          {lookupError && <p role="alert">{lookupError}</p>}
          {lookupResult && <p role="status">{lookupResult.status} · Đã nhập {lookupResult.imported ?? 0}/{lookupResult.quantity} key · {lookupResult.totalCost.toFixed(2)} EUR (mô phỏng)</p>}
        </section>}
      </section>}

      {isInventory && <>
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

        <label>Tìm trong kho <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Tên hoặc mã sản phẩm" /></label>
        {loading ? (
          <p className="muted">Đang tải tồn kho...</p>
        ) : filteredInventory.length === 0 ? (
          <p className="muted">
            Không tìm thấy tồn kho phù hợp.
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
              {filteredInventory.map((item) => (
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

        <label>Trạng thái <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}><option value="all">Tất cả</option><option value="AVAILABLE">Khả dụng</option><option value="ASSIGNED">Đã cấp phát</option><option value="DISABLED">Vô hiệu hóa</option></select></label>
        {detailError && (
          <div className="alert">{detailError}</div>
        )}

        {!productId ? (
          <p className="muted">
            Chọn sản phẩm để xem danh sách Key.
          </p>
        ) : loadingKeys ? (
          <p className="muted">Đang tải...</p>
        ) : filteredLicenses.length === 0 ? (
          !detailError && (
            <p className="muted">
              Không có license phù hợp.
            </p>
          )
        ) : (
          <>
            <p className="muted">
              Hiển thị {filteredLicenses.length} bản ghi,
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
                {filteredLicenses.map((license) => (
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
      </>}
    </div>
  );
}
