import { useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { listMyLicenses } from "../api/licenses";
import { listOrders } from "../api/orders";
import { listProducts } from "../api/products";
import {
  addSupportMessage,
  approveSupportRefund,
  createSupportTicket,
  escalateSupportTicket,
  getExternalSupportLink,
  getLicenseReplacement,
  getSupportRefundStatus,
  getSupportTicket,
  listSupportCategories,
  listSupportTickets,
  rejectSupportTicket,
  replaceDefectiveLicense,
  syncExternalSupportLink,
  updateSupportStatus,
  type ExternalSupportLink,
  type LicenseReplacementResult,
  type SupportCategory,
  type SupportRefundStatus,
  type SupportTicketDetail,
  type SupportTicketSummary,
} from "../api/support";
import type { CustomerLicense, Order, Product } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { vnd } from "../components/format";

const STATUS_META: Record<
  SupportTicketSummary["status"],
  { label: string; color: string }
> = {
  OPEN: { label: "Chờ tiếp nhận", color: "var(--color-warning)" },
  IN_PROGRESS: { label: "Đang xử lý", color: "var(--color-info)" },
  RESOLVED: { label: "Đã giải quyết", color: "var(--color-success)" },
  CLOSED: { label: "Đã đóng", color: "var(--color-muted)" },
};

const ROLE_LABEL: Record<string, string> = {
  customer: "Khách hàng",
  license_coordinator: "Điều phối kho License",
  transaction_auditor: "Kiểm toán giao dịch",
  catalog_manager: "Quản lý danh mục",
};

const REFUND_STATUS_LABEL: Record<string, string> = {
  REQUESTED: "Đã ghi nhận — Đang kiểm tra điều kiện",
  PREPARED: "Đã khóa license — Đang hoàn tiền về Ví DLM",
  CREDITED: "Đã cộng Ví DLM — Đang hoàn tất thu hồi key",
  COMPLETED: "Hoàn tiền thành công về Ví DLM",
  REJECTED: "Tự động từ chối (không đạt điều kiện)",
};

const REJECTION_CODE_VI: Record<string, string> = {
  LICENSE_ALREADY_REVEALED:
    "Key trong đơn đã được mở xem nên không đủ điều kiện hoàn tiền đơn chưa mở key.",
  REFUND_WINDOW_EXPIRED:
    "Đơn hàng đã quá thời hạn hoàn tiền (7 ngày kể từ lúc thanh toán).",
  ORDER_ALREADY_COMPENSATED:
    "Đơn hàng đã được đổi key mới trước đó nên không thể hoàn tiền lại.",
  DEFECT_REFUND_NOT_ELIGIBLE:
    "Hoàn tiền toàn bộ key lỗi chỉ áp dụng cho đơn 1 key đã mở xem. Với đơn nhiều key, vui lòng dùng chức năng Đổi key mới.",
  ORDER_NOT_REFUNDABLE: "Trạng thái đơn hàng hiện tại không đủ điều kiện hoàn tiền.",
  REFUND_ALREADY_REQUESTED: "Đơn hàng này đã có yêu cầu hoàn tiền đang xử lý.",
};

export function SupportPage() {
  const {
    ready,
    authenticated,
    subject: userId,
    isCustomer,
    isLicenseCoordinator,
    isTransactionAuditor,
    isCatalogManager,
    isStaff,
    login,
  } = useAuth();
  const [params] = useSearchParams();

  const [categories, setCategories] = useState<SupportCategory[]>([]);
  const [tickets, setTickets] = useState<SupportTicketSummary[]>([]);
  const [totalTickets, setTotalTickets] = useState(0);
  const [statusFilter, setStatusFilter] = useState("");
  const [categoryFilter, setCategoryFilter] = useState("");
  const [loadingList, setLoadingList] = useState(true);
  const [listError, setListError] = useState("");

  const [selectedId, setSelectedId] = useState<string>("");
  const [detail, setDetail] = useState<SupportTicketDetail | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [detailError, setDetailError] = useState("");

  // Customer create form state
  const [viewTab, setViewTab] = useState<"create" | "tickets">(() =>
    params.get("orderId") || params.get("category") ? "create" : "create",
  );
  const [orders, setOrders] = useState<Order[]>([]);
  const [licenses, setLicenses] = useState<CustomerLicense[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [category, setCategory] = useState(
    () => (params.get("category") || "REFUND_REQUEST").toUpperCase(),
  );
  const [orderId, setOrderId] = useState(() => params.get("orderId") || "");
  const [licenseId, setLicenseId] = useState(() => params.get("licenseId") || "");
  const [subject, setSubject] = useState("");
  const [description, setDescription] = useState("");
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState("");
  const [createSuccess, setCreateSuccess] = useState("");

  // Thread & Staff workflow state
  const [replyText, setReplyText] = useState("");
  const [actionBusy, setActionBusy] = useState(false);
  const [actionError, setActionError] = useState("");
  const [actionNotice, setActionNotice] = useState("");
  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState("");

  const [refundStatus, setRefundStatus] = useState<SupportRefundStatus | null>(null);
  const [replacement, setReplacement] = useState<LicenseReplacementResult | null>(null);
  const [externalLink, setExternalLink] = useState<ExternalSupportLink | null>(null);
  const [refreshTick, setRefreshTick] = useState(0);
  const refundPollRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(() => {
    listSupportCategories()
      .then((cats) => setCategories(cats))
      .catch(() => {});
  }, []);

  useEffect(() => {
    if (!ready || !authenticated || !isCustomer) return;
    let active = true;
    Promise.all([
      listOrders().catch(() => [] as Order[]),
      listMyLicenses().catch(() => [] as CustomerLicense[]),
      listProducts({ page: 1 }).then((p) => p.items).catch(() => [] as Product[]),
    ]).then(([o, l, p]) => {
      if (!active) return;
      setOrders(o);
      setLicenses(l);
      setProducts(p);
    });
    return () => {
      active = false;
    };
  }, [ready, authenticated, isCustomer, refreshTick]);

  useEffect(() => {
    if (!ready || !authenticated) return;
    let active = true;
    setLoadingList(true);
    setListError("");
    listSupportTickets({
      status: statusFilter || undefined,
      category: categoryFilter || undefined,
      page: 1,
      pageSize: 50,
    })
      .then((res) => {
        if (!active) return;
        setTickets(res.items);
        setTotalTickets(res.total);
        setSelectedId((prev) => {
          if (prev && res.items.some((x) => x.id === prev)) return prev;
          return res.items[0]?.id ?? "";
        });
      })
      .catch((e: Error) => {
        if (active) setListError(e.message || "Không tải được danh sách phiếu hỗ trợ.");
      })
      .finally(() => {
        if (active) setLoadingList(false);
      });
    return () => {
      active = false;
    };
  }, [ready, authenticated, statusFilter, categoryFilter, refreshTick]);

  useEffect(() => {
    clearTimeout(refundPollRef.current);
    setRefundStatus(null);
    setReplacement(null);
    setExternalLink(null);
    setRejectOpen(false);
    setRejectReason("");
    setActionError("");
    setActionNotice("");

    if (!ready || !authenticated || !selectedId) {
      setDetail(null);
      return;
    }

    let active = true;
    setLoadingDetail(true);
    setDetailError("");

    getSupportTicket(selectedId)
      .then(async (t) => {
        if (!active) return;
        setDetail(t);

        const handledByMe = isStaff && Boolean(userId) && t.handledBy === userId;
        if (!handledByMe) return;

        // Fetch auxiliary staff metadata quietly (404 means not yet created).
        getExternalSupportLink(t.id)
          .then((link) => { if (active) setExternalLink(link); })
          .catch(() => {});

        if (
          (isLicenseCoordinator &&
            (t.category === "INVALID_KEY" || t.category === "KEY_ALREADY_USED")) ||
          (isTransactionAuditor && t.category === "REFUND_REQUEST")
        ) {
          getSupportRefundStatus(t.id)
            .then((rf) => { if (active) setRefundStatus(rf); })
            .catch(() => {});
        }

        if (
          isLicenseCoordinator &&
          (t.category === "INVALID_KEY" || t.category === "KEY_ALREADY_USED")
        ) {
          getLicenseReplacement(t.id)
            .then((rep) => { if (active) setReplacement(rep); })
            .catch(() => {});
        }
      })
      .catch((e: Error) => {
        if (active) setDetailError(e.message || "Không tải được chi tiết phiếu.");
      })
      .finally(() => {
        if (active) setLoadingDetail(false);
      });

    return () => {
      active = false;
      clearTimeout(refundPollRef.current);
    };
  }, [
    ready,
    authenticated,
    selectedId,
    isStaff,
    isLicenseCoordinator,
    isTransactionAuditor,
    userId,
    refreshTick,
  ]);

  const categoryMap = useMemo(
    () => Object.fromEntries(categories.map((c) => [c.code, c])),
    [categories],
  );
  const selectedCategory = categoryMap[category];

  const productName = (id: string) =>
    products.find((p) => p.id === id)?.name ?? `Sản phẩm ${id.slice(0, 8)}`;

  const licensesByOrder = useMemo(() => {
    const map: Record<string, CustomerLicense[]> = {};
    for (const lic of licenses) {
      if (!map[lic.orderId]) map[lic.orderId] = [];
      map[lic.orderId].push(lic);
    }
    return map;
  }, [licenses]);

  const eligibleOrders = useMemo(() => {
    return orders.filter((o) => {
      switch (category) {
        case "INVALID_KEY":
        case "KEY_ALREADY_USED":
        case "WRONG_PRODUCT":
          return o.status === "COMPLETED";
        case "KEY_NOT_RECEIVED":
          return o.status === "PAID";
        case "PAYMENT_ISSUE":
          return o.status === "PENDING" || o.status === "PAID" || o.status === "COMPLETED";
        case "REFUND_REQUEST":
          return o.status === "PAID" || o.status === "COMPLETED";
        default:
          return true;
      }
    });
  }, [orders, category]);

  const selectedOrderLicenses = orderId ? (licensesByOrder[orderId] ?? []) : [];
  const selectedOrderHasRevealedKey = selectedOrderLicenses.some(
    (l) => Boolean(l.isRevealed || l.revealedAtUtc),
  );

  function pickScenario(scenario: "unrevealed" | "revealed") {
    setCreateError("");
    setCreateSuccess("");
    if (scenario === "unrevealed") {
      setCategory("REFUND_REQUEST");
      setLicenseId("");
      if (!subject) setSubject("Yêu cầu hoàn tiền đơn hàng chưa mở key");
    } else {
      setCategory("INVALID_KEY");
      if (!subject) setSubject("Báo cáo key lỗi / không kích hoạt được");
    }
  }

  async function handleCreateTicket(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (creating || !isCustomer) return;
    setCreateError("");
    setCreateSuccess("");

    if (selectedCategory?.requiresOrder && !orderId) {
      setCreateError("Vui lòng chọn đơn hàng liên quan.");
      return;
    }
    if (selectedCategory?.requiresLicense && !licenseId) {
      setCreateError("Vui lòng chọn License Key gặp sự cố.");
      return;
    }
    if (subject.trim().length < 3) {
      setCreateError("Tiêu đề cần tối thiểu 3 ký tự.");
      return;
    }
    if (description.trim().length < 5) {
      setCreateError("Vui lòng mô tả chi tiết tối thiểu 5 ký tự.");
      return;
    }

    setCreating(true);
    try {
      const created = await createSupportTicket({
        orderId: orderId || null,
        licenseId: licenseId || null,
        category,
        subject: subject.trim(),
        description: description.trim(),
      });
      setCreateSuccess(
        `Đã gửi phiếu hỗ trợ #${created.id.slice(0, 8)} tới bộ phận ${ROLE_LABEL[created.assignedRole] ?? created.assignedRole}.`,
      );
      setSubject("");
      setDescription("");
      setSelectedId(created.id);
      setViewTab("tickets");
      setRefreshTick((v) => v + 1);
    } catch (err) {
      setCreateError(
        err instanceof Error ? err.message : "Không thể gửi phiếu hỗ trợ.",
      );
    } finally {
      setCreating(false);
    }
  }

  async function handleSendReply(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!detail || actionBusy || !replyText.trim()) return;
    setActionBusy(true);
    setActionError("");
    try {
      await addSupportMessage(detail.id, replyText.trim());
      setReplyText("");
      setRefreshTick((v) => v + 1);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Không gửi được tin nhắn.");
    } finally {
      setActionBusy(false);
    }
  }

  async function handleStatusChange(next: "IN_PROGRESS" | "RESOLVED" | "CLOSED") {
    if (!detail || actionBusy) return;
    setActionBusy(true);
    setActionError("");
    setActionNotice("");
    try {
      await updateSupportStatus(detail.id, next);
      setActionNotice(
        next === "IN_PROGRESS"
          ? "Bạn đã tiếp nhận xử lý phiếu này."
          : next === "RESOLVED"
            ? "Đã đánh dấu phiếu là Đã giải quyết."
            : "Đã đóng phiếu hỗ trợ.",
      );
      setRefreshTick((v) => v + 1);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Không thể cập nhật trạng thái.");
    } finally {
      setActionBusy(false);
    }
  }

  function pollRefundProgress(ticketId: string, attempt = 0) {
    if (attempt >= 12) return;
    refundPollRef.current = setTimeout(async () => {
      try {
        const st = await getSupportRefundStatus(ticketId);
        setRefundStatus(st);
        if (st.status === "COMPLETED") {
          setActionNotice("Hoàn tiền tự động thành công! Tiền đã được cộng vào Ví DLM của khách.");
          setRefreshTick((v) => v + 1);
          return;
        }
        if (st.status === "REJECTED") {
          const reason =
            REJECTION_CODE_VI[st.rejectionCode ?? ""] ??
            `Hệ thống tự động từ chối (${st.rejectionCode ?? "NOT_ELIGIBLE"}).`;
          setActionError(reason);
          setRefreshTick((v) => v + 1);
          return;
        }
        pollRefundProgress(ticketId, attempt + 1);
      } catch {
        // Stop polling on transient error; staff can click Refresh.
      }
    }, 1500);
  }

  async function handleApproveRefund() {
    if (!detail || actionBusy) return;
    const isUnrevealed = detail.category === "REFUND_REQUEST";
    const confirmed = window.confirm(
      isUnrevealed
        ? "Xác nhận duyệt hoàn tiền tự động cho đơn CHƯA MỞ KEY? Hệ thống sẽ kiểm tra điều kiện (≤ 7 ngày, 100% key chưa mở), hoàn tiền về Ví DLM và trả key về kho."
        : "Xác nhận duyệt hoàn tiền tự động cho KEY LỖI? Hệ thống sẽ hoàn tiền về Ví DLM và khóa vĩnh viễn key lỗi.",
    );
    if (!confirmed) return;

    setActionBusy(true);
    setActionError("");
    setActionNotice("");
    try {
      const res = await approveSupportRefund(detail.id);
      setRefundStatus(res);
      setActionNotice(
        "Đã xác nhận duyệt hoàn tiền. Hệ thống đang tự động kiểm tra điều kiện và hoàn tiền về Ví DLM...",
      );
      pollRefundProgress(detail.id, 0);
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Không thể duyệt hoàn tiền.";
      setActionError(REJECTION_CODE_VI[msg] ?? msg);
    } finally {
      setActionBusy(false);
    }
  }

  async function handleRejectSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!detail || actionBusy) return;
    if (rejectReason.trim().length < 5) {
      setActionError("Vui lòng nhập lý do từ chối rõ ràng (tối thiểu 5 ký tự).");
      return;
    }

    setActionBusy(true);
    setActionError("");
    setActionNotice("");
    try {
      const res = await rejectSupportTicket(detail.id, rejectReason.trim());
      setRejectOpen(false);
      setRejectReason("");
      setActionNotice(
        res.notificationQueued
          ? "Đã từ chối yêu cầu và gửi thông báo tới khách hàng."
          : "Đã từ chối yêu cầu và ghi lý do lên phiếu hỗ trợ.",
      );
      setRefreshTick((v) => v + 1);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Không thể từ chối phiếu.");
    } finally {
      setActionBusy(false);
    }
  }

  async function handleReplaceLicense() {
    if (!detail || actionBusy) return;
    if (
      !window.confirm(
        "Xác nhận đổi key mới từ kho khả dụng và vô hiệu hóa vĩnh viễn key lỗi?",
      )
    ) {
      return;
    }
    setActionBusy(true);
    setActionError("");
    setActionNotice("");
    try {
      const rep = await replaceDefectiveLicense(detail.id);
      setReplacement(rep);
      setActionNotice(
        `Đã đổi sang key mới (${rep.newLicenseId.slice(0, 8)}) và vô hiệu hóa key lỗi.`,
      );
      setRefreshTick((v) => v + 1);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Không thể đổi key mới.");
    } finally {
      setActionBusy(false);
    }
  }

  async function handleEscalateJira() {
    if (!detail || actionBusy) return;
    setActionBusy(true);
    setActionError("");
    setActionNotice("");
    try {
      const link = await escalateSupportTicket(detail.id);
      setExternalLink(link);
      setActionNotice(`Đã đẩy phiếu sang Jira (${link.issueKey ?? link.state}).`);
    } catch (err) {
      setActionError(
        err instanceof Error
          ? err.message
          : "Không thể đẩy phiếu sang Jira (kiểm tra cấu hình Jira Service Desk).",
      );
    } finally {
      setActionBusy(false);
    }
  }

  async function handleSyncJira() {
    if (!detail || actionBusy) return;
    setActionBusy(true);
    setActionError("");
    setActionNotice("");
    try {
      const link = await syncExternalSupportLink(detail.id);
      setExternalLink(link);
      setActionNotice("Đã đồng bộ trạng thái & bình luận mới nhất từ Jira.");
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Không thể đồng bộ Jira.");
    } finally {
      setActionBusy(false);
    }
  }

  if (!ready) {
    return (
      <div className="container">
        <p className="muted">Đang tải...</p>
      </div>
    );
  }

  if (!authenticated) {
    return (
      <div className="container">
        <div className="card">
          <h1>Trung tâm Hỗ trợ &amp; Hoàn tiền</h1>
          <p>Vui lòng đăng nhập để gửi hoặc theo dõi phiếu hỗ trợ.</p>
          <button type="button" className="btn" onClick={login}>
            Đăng nhập
          </button>
        </div>
      </div>
    );
  }

  const staffRoleTitle = isLicenseCoordinator
    ? "Điều phối kho License"
    : isTransactionAuditor
      ? "Kiểm toán giao dịch"
      : isCatalogManager
        ? "Quản lý danh mục"
        : "Nhân viên hỗ trợ";

  const handledByMe =
    isStaff && Boolean(detail && userId && detail.handledBy === userId);
  const canApproveUnrevealed =
    handledByMe &&
    isTransactionAuditor &&
    detail?.category === "REFUND_REQUEST" &&
    detail?.status === "IN_PROGRESS";
  const canHandleDefectKey =
    handledByMe &&
    isLicenseCoordinator &&
    (detail?.category === "INVALID_KEY" || detail?.category === "KEY_ALREADY_USED") &&
    detail?.status === "IN_PROGRESS";

  return (
    <div className="container">
      <div className="row" style={{ justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 12 }}>
        <div>
          <h1 style={{ marginBottom: 4 }}>
            {isStaff
              ? `Hộp thư Hỗ trợ — ${staffRoleTitle}`
              : "Trung tâm Hỗ trợ & Hoàn tiền"}
          </h1>
          <p className="muted" style={{ marginTop: 0 }}>
            {isStaff
              ? "Hệ thống tự động phân luồng đúng phiếu hỗ trợ thuộc phạm vi phụ trách của bạn."
              : "Chọn chủ đề phù hợp — phiếu sẽ được gửi tự động tới đúng bộ phận phụ trách xử lý."}
          </p>
        </div>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={loadingList}
          onClick={() => setRefreshTick((v) => v + 1)}
        >
          Làm mới
        </button>
      </div>

      {isCustomer && (
        <div className="row" style={{ margin: "16px 0" }}>
          <button
            type="button"
            className={viewTab === "create" ? "btn" : "btn btn-secondary"}
            onClick={() => setViewTab("create")}
          >
            + Gửi yêu cầu hỗ trợ / Hoàn tiền
          </button>
          <button
            type="button"
            className={viewTab === "tickets" ? "btn" : "btn btn-secondary"}
            onClick={() => setViewTab("tickets")}
          >
            Phiếu của tôi ({totalTickets})
          </button>
        </div>
      )}

      {isCustomer && viewTab === "create" && (
        <section className="card" style={{ marginBottom: 24 }}>
          <h2>Chọn trường hợp hoàn tiền hoặc chủ đề hỗ trợ</h2>

          <div className="support-scenarios">
            <button
              type="button"
              className={`support-scenario-card ${category === "REFUND_REQUEST" ? "active" : ""}`}
              onClick={() => pickScenario("unrevealed")}
            >
              <span className="license-status available">Trường hợp 1 · Chưa mở key</span>
              <h3>Hoàn tiền đơn đã mua &amp; chưa xem key</h3>
              <p className="muted">
                Áp dụng trong <strong>7 ngày</strong> kể từ lúc thanh toán, <strong>100% key chưa mở xem</strong>. Khi <strong>Kiểm toán giao dịch</strong> xác nhận, hệ thống tự động hoàn 100% tiền về <strong>Ví DLM</strong> và thu hồi key về kho.
              </p>
            </button>

            <button
              type="button"
              className={`support-scenario-card ${category === "INVALID_KEY" || category === "KEY_ALREADY_USED" ? "active" : ""}`}
              onClick={() => pickScenario("revealed")}
            >
              <span className="license-status assigned">Trường hợp 2 · Đã xem key</span>
              <h3>Hoàn tiền / Đổi key khi đã xem key (Key lỗi)</h3>
              <p className="muted">
                Áp dụng khi key đã mở xem nhưng báo lỗi hoặc đã có người dùng trên Steam. Phiếu chuyển tới <strong>Điều phối kho License</strong> để xác minh (và gửi nhà cung cấp qua Jira nếu cần), sau đó <strong>đổi key mới</strong> hoặc <strong>hoàn tiền về Ví DLM</strong>.
              </p>
            </button>
          </div>

          <h3 style={{ marginTop: 22, marginBottom: 10 }}>Tất cả chủ đề hỗ trợ (tự động gửi đúng người phụ trách)</h3>
          <div className="support-category-grid">
            {categories.map((cat) => (
              <button
                key={cat.code}
                type="button"
                className={`support-cat-card ${category === cat.code ? "active" : ""}`}
                onClick={() => {
                  setCategory(cat.code);
                  setCreateError("");
                  if (!cat.requiresLicense) setLicenseId("");
                  if (!cat.requiresOrder) setOrderId("");
                }}
              >
                <div className="support-cat-top">
                  <strong>{cat.label}</strong>
                  <span className="support-role-chip">→ {cat.assignedRoleLabel}</span>
                </div>
                <p className="muted">{cat.description}</p>
              </button>
            ))}
          </div>

          <form className="license-form" style={{ marginTop: 20 }} onSubmit={handleCreateTicket}>
            {selectedCategory && (
              <div className="support-route-hint">
                Bộ phận tiếp nhận tự động:{" "}
                <strong>{selectedCategory.assignedRoleLabel}</strong>{" "}
                (<code>{selectedCategory.assignedRole}</code>)
              </div>
            )}

            {(selectedCategory?.requiresOrder || orders.length > 0) && (
              <label>
                Đơn hàng liên quan{" "}
                {selectedCategory?.requiresOrder ? "(bắt buộc)" : "(không bắt buộc)"}
                <select
                  required={Boolean(selectedCategory?.requiresOrder)}
                  value={orderId}
                  onChange={(e) => {
                    setOrderId(e.target.value);
                    setLicenseId("");
                  }}
                >
                  <option value="">
                    {selectedCategory?.requiresOrder
                      ? "Chọn đơn hàng phù hợp..."
                      : "Không gắn đơn hàng cụ thể"}
                  </option>
                  {eligibleOrders.map((o) => {
                    const lics = licensesByOrder[o.id] ?? [];
                    const anyRevealed = lics.some((l) => l.isRevealed || l.revealedAtUtc);
                    const firstName = o.items[0] ? productName(o.items[0].productId) : "Đơn hàng";
                    const suffix =
                      o.status === "COMPLETED" && lics.length > 0
                        ? anyRevealed
                          ? " [Đã xem key]"
                          : " [Chưa mở key]"
                        : ` [${o.status}]`;
                    return (
                      <option key={o.id} value={o.id}>
                        #{o.id.slice(0, 8)} · {firstName}
                        {o.items.length > 1 ? ` (+${o.items.length - 1})` : ""} ·{" "}
                        {vnd(o.totalAmount)}
                        {suffix}
                      </option>
                    );
                  })}
                </select>
              </label>
            )}

            {category === "REFUND_REQUEST" && orderId && selectedOrderHasRevealedKey && (
              <div className="alert" role="alert">
                Lưu ý: Đơn hàng này đã có key được mở xem, nên sẽ{" "}
                <strong>không đạt điều kiện hoàn tiền đơn chưa mở key</strong>. Nếu key bị lỗi khi kích hoạt, vui lòng chọn{" "}
                <button
                  type="button"
                  className="btn btn-secondary"
                  style={{ marginLeft: 8, padding: "4px 10px" }}
                  onClick={() => pickScenario("revealed")}
                >
                  Chuyển sang Báo key lỗi (Trường hợp 2)
                </button>
              </div>
            )}

            {selectedCategory?.requiresLicense && (
              <label>
                License Key gặp sự cố (bắt buộc)
                <select
                  required
                  value={licenseId}
                  onChange={(e) => setLicenseId(e.target.value)}
                  disabled={!orderId}
                >
                  <option value="">
                    {orderId ? "Chọn License Key trong đơn..." : "Chọn đơn hàng trước"}
                  </option>
                  {selectedOrderLicenses.map((l) => (
                    <option key={l.id} value={l.id}>
                      {productName(l.productId)} · Key #{l.id.slice(0, 8)} (
                      {l.isRevealed || l.revealedAtUtc ? "Đã xem key" : "Chưa mở key"})
                    </option>
                  ))}
                </select>
              </label>
            )}

            <label>
              Tiêu đề yêu cầu
              <input
                required
                minLength={3}
                maxLength={150}
                value={subject}
                onChange={(e) => setSubject(e.target.value)}
                placeholder="VD: Yêu cầu hoàn tiền đơn #... / Key Steam báo Duplicate Product Code"
              />
            </label>

            <label>
              Mô tả chi tiết
              <textarea
                required
                rows={4}
                minLength={5}
                maxLength={3000}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder={
                  category === "REFUND_REQUEST"
                    ? "Nêu rõ lý do muốn hoàn tiền (không bấm mở key trong thời gian chờ duyệt)..."
                    : category === "INVALID_KEY" || category === "KEY_ALREADY_USED"
                      ? "Mô tả lỗi Steam hiển thị (VD: Duplicate Product Code / Invalid Product Code), thời gian thử kích hoạt..."
                      : category === "WALLET_TOPUP_ISSUE"
                        ? "Ghi rõ mã giao dịch TOPUP..., thời gian nạp và số tiền trên VNPay..."
                        : "Mô tả vấn đề bạn gặp phải..."
                }
              />
            </label>

            {createError && <div className="alert" role="alert">{createError}</div>}
            {createSuccess && <div className="alert-success" role="status">{createSuccess}</div>}

            <div className="row">
              <button type="submit" className="btn" disabled={creating}>
                {creating ? "Đang gửi phiếu..." : "Gửi phiếu hỗ trợ"}
              </button>
            </div>
          </form>
        </section>
      )}

      {(isStaff || viewTab === "tickets") && (
        <div className="support-layout">
          <aside className="card support-list-panel">
            <div className="row" style={{ gap: 8, marginBottom: 12 }}>
              <select
                aria-label="Lọc theo trạng thái"
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                style={{ flex: 1 }}
              >
                <option value="">Tất cả trạng thái</option>
                <option value="OPEN">Chờ tiếp nhận</option>
                <option value="IN_PROGRESS">Đang xử lý</option>
                <option value="RESOLVED">Đã giải quyết</option>
                <option value="CLOSED">Đã đóng</option>
              </select>
              <select
                aria-label="Lọc theo chủ đề"
                value={categoryFilter}
                onChange={(e) => setCategoryFilter(e.target.value)}
                style={{ flex: 1 }}
              >
                <option value="">Tất cả chủ đề</option>
                {categories.map((c) => (
                  <option key={c.code} value={c.code}>
                    {c.label}
                  </option>
                ))}
              </select>
            </div>

            {listError && <div className="alert">{listError}</div>}
            {loadingList && <p className="muted">Đang tải danh sách phiếu...</p>}
            {!loadingList && tickets.length === 0 && (
              <p className="muted">Chưa có phiếu hỗ trợ nào.</p>
            )}

            <div className="support-ticket-list">
              {tickets.map((t) => {
                const st = STATUS_META[t.status] ?? {
                  label: t.status,
                  color: "var(--color-muted)",
                };
                return (
                  <button
                    key={t.id}
                    type="button"
                    className={`support-ticket-item ${selectedId === t.id ? "active" : ""}`}
                    onClick={() => setSelectedId(t.id)}
                  >
                    <div className="row" style={{ justifyContent: "space-between", gap: 6 }}>
                      <strong>{t.subject}</strong>
                      <span className="badge" style={{ background: st.color }}>
                        {st.label}
                      </span>
                    </div>
                    <div className="muted" style={{ fontSize: ".8rem", marginTop: 4 }}>
                      {categoryMap[t.category]?.label ?? t.category} ·{" "}
                      {ROLE_LABEL[t.assignedRole] ?? t.assignedRole}
                    </div>
                    {t.resolutionOutcome === "REJECTED" && (
                      <div className="support-mini-tag rejected">Đã từ chối</div>
                    )}
                    {t.resolutionOutcome === "APPROVED_REFUND" && (
                      <div className="support-mini-tag approved">Đã duyệt hoàn tiền</div>
                    )}
                  </button>
                );
              })}
            </div>
          </aside>

          <section className="card support-detail-panel">
            {detailError && <div className="alert">{detailError}</div>}
            {loadingDetail && <p className="muted">Đang tải chi tiết phiếu...</p>}
            {!loadingDetail && !detail && (
              <p className="muted">Chọn một phiếu ở cột bên trái để xem chi tiết và trao đổi.</p>
            )}

            {detail && (
              <>
                <div className="row" style={{ justifyContent: "space-between", alignItems: "flex-start", gap: 12 }}>
                  <div>
                    <h2 style={{ margin: 0 }}>{detail.subject}</h2>
                    <p className="muted" style={{ marginTop: 6 }}>
                      Phiếu <code>#{detail.id.slice(0, 8)}</code> ·{" "}
                      <strong>{categoryMap[detail.category]?.label ?? detail.category}</strong> ·{" "}
                      Phụ trách: <strong>{ROLE_LABEL[detail.assignedRole] ?? detail.assignedRole}</strong>
                    </p>
                    <p className="muted" style={{ fontSize: ".85rem" }}>
                      {detail.orderId && (
                        <>
                          Đơn hàng: <code>{detail.orderId}</code>{" "}
                        </>
                      )}
                      {detail.licenseId && (
                        <>
                          · License: <code>{detail.licenseId}</code>
                        </>
                      )}
                    </p>
                  </div>
                  <span
                    className="badge"
                    style={{
                      background:
                        STATUS_META[detail.status]?.color ?? "var(--color-muted)",
                    }}
                  >
                    {STATUS_META[detail.status]?.label ?? detail.status}
                  </span>
                </div>

                {detail.resolutionOutcome === "REJECTED" && (
                  <div className="alert" role="alert" style={{ marginTop: 12 }}>
                    <strong>Thông báo từ chối yêu cầu:</strong>{" "}
                    {detail.rejectionReason || "Yêu cầu không đáp ứng điều kiện theo chính sách."}
                  </div>
                )}

                {detail.resolutionOutcome === "APPROVED_REFUND" && (
                  <div className="alert-success" role="status" style={{ marginTop: 12 }}>
                    <strong>Đã xác nhận duyệt hoàn tiền tự động!</strong> Tiền hoàn sẽ được cộng trực tiếp vào Ví DLM của khách hàng.
                  </div>
                )}

                <div className="support-description-box">
                  <div className="muted" style={{ fontSize: ".8rem", marginBottom: 4 }}>
                    Nội dung yêu cầu ban đầu ·{" "}
                    {new Date(detail.createdAtUtc).toLocaleString("vi-VN")}
                  </div>
                  <p style={{ margin: 0, whiteSpace: "pre-wrap" }}>{detail.description}</p>
                </div>

                {actionError && <div className="alert" role="alert" style={{ marginTop: 12 }}>{actionError}</div>}
                {actionNotice && <div className="alert-success" role="status" style={{ marginTop: 12 }}>{actionNotice}</div>}

                {/* Staff Workflow Actions */}
                {isStaff && (
                  <div className="support-staff-box">
                    <h3>Xử lý nghiệp vụ ({staffRoleTitle})</h3>

                    {detail.status === "OPEN" && (
                      <button
                        type="button"
                        className="btn"
                        disabled={actionBusy}
                        onClick={() => void handleStatusChange("IN_PROGRESS")}
                      >
                        Tiếp nhận xử lý phiếu này
                      </button>
                    )}

                    {detail.status === "IN_PROGRESS" && !handledByMe && (
                      <p className="muted">Phiếu đang được nhân viên khác xử lý.</p>
                    )}

                    {handledByMe && detail.status === "IN_PROGRESS" && (
                      <>
                        <div className="row" style={{ flexWrap: "wrap", gap: 8 }}>
                          {canApproveUnrevealed && (
                            <button
                              type="button"
                              className="btn"
                              disabled={actionBusy}
                              onClick={() => void handleApproveRefund()}
                            >
                              Xác nhận duyệt tự động hoàn tiền (chưa mở key)
                            </button>
                          )}

                          {canHandleDefectKey && (
                            <>
                              <button
                                type="button"
                                className="btn"
                                disabled={actionBusy || Boolean(replacement)}
                                onClick={() => void handleReplaceLicense()}
                              >
                                {replacement ? "Đã đổi key mới" : "Đổi key mới từ kho"}
                              </button>
                              <button
                                type="button"
                                className="btn"
                                disabled={actionBusy || Boolean(replacement)}
                                onClick={() => void handleApproveRefund()}
                              >
                                Xác nhận duyệt tự động hoàn tiền (key lỗi)
                              </button>
                            </>
                          )}

                          <button
                            type="button"
                            className="btn btn-secondary"
                            disabled={actionBusy}
                            onClick={() => setRejectOpen((v) => !v)}
                          >
                            Từ chối yêu cầu (Reject)
                          </button>

                          <button
                            type="button"
                            className="btn btn-secondary"
                            disabled={actionBusy}
                            onClick={() => void handleStatusChange("RESOLVED")}
                          >
                            Đánh dấu đã giải quyết
                          </button>
                        </div>

                        {rejectOpen && (
                          <form
                            style={{ marginTop: 12 }}
                            onSubmit={handleRejectSubmit}
                          >
                            <label>
                              Lý do từ chối (gửi trực tiếp cho khách hàng)
                              <textarea
                                required
                                rows={3}
                                minLength={5}
                                maxLength={500}
                                value={rejectReason}
                                onChange={(e) => setRejectReason(e.target.value)}
                                placeholder="VD: Key đã được mở xem trước thời điểm yêu cầu hoàn tiền / Bằng chứng kích hoạt không hợp lệ..."
                              />
                            </label>
                            <div className="row" style={{ marginTop: 8 }}>
                              <button type="submit" className="btn" disabled={actionBusy}>
                                Xác nhận từ chối &amp; Gửi thông báo
                              </button>
                              <button
                                type="button"
                                className="btn btn-secondary"
                                onClick={() => setRejectOpen(false)}
                              >
                                Hủy
                              </button>
                            </div>
                          </form>
                        )}
                      </>
                    )}

                    {refundStatus && (
                      <div className="support-subpanel">
                        <strong>Trạng thái hoàn tiền tự động ({refundStatus.mode}):</strong>{" "}
                        <span>{REFUND_STATUS_LABEL[refundStatus.status] ?? refundStatus.status}</span>
                        {refundStatus.rejectionCode && (
                          <div className="alert" style={{ marginTop: 8 }}>
                            {REJECTION_CODE_VI[refundStatus.rejectionCode] ??
                              refundStatus.rejectionCode}
                          </div>
                        )}
                      </div>
                    )}

                    {replacement && (
                      <div className="support-subpanel">
                        <strong>Đã đổi key mới thành công:</strong>{" "}
                        Key cũ <code>{replacement.oldLicenseId.slice(0, 8)}</code> (DISABLED) → Key mới{" "}
                        <code>{replacement.newLicenseId.slice(0, 8)}</code>
                      </div>
                    )}

                    {/* Jira / Third-party Supplier Escalation */}
                    {handledByMe && (
                      <div className="support-subpanel">
                        <div className="row" style={{ justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 8 }}>
                          <strong>Theo dõi bên thứ 3 / Jira Service Desk</strong>
                          <div className="row" style={{ gap: 8 }}>
                            {!externalLink && detail.status === "IN_PROGRESS" && (
                              <button
                                type="button"
                                className="btn btn-secondary"
                                disabled={actionBusy}
                                onClick={() => void handleEscalateJira()}
                              >
                                Đẩy lên Jira / NCC
                              </button>
                            )}
                            {externalLink?.state === "LINKED" && (
                              <button
                                type="button"
                                className="btn btn-secondary"
                                disabled={actionBusy}
                                onClick={() => void handleSyncJira()}
                              >
                                Đồng bộ từ Jira
                              </button>
                            )}
                          </div>
                        </div>

                        {externalLink ? (
                          <div style={{ marginTop: 8, fontSize: ".88rem" }}>
                            <div>
                              Jira Issue: <code>{externalLink.issueKey ?? externalLink.state}</code> ·
                              Trạng thái ngoài: <strong>{externalLink.externalStatus ?? "—"}</strong>
                            </div>
                            {externalLink.comments?.length > 0 && (
                              <ul style={{ margin: "6px 0 0 18px" }}>
                                {externalLink.comments.map((c) => (
                                  <li key={c.id}>{c.body}</li>
                                ))}
                              </ul>
                            )}
                          </div>
                        ) : (
                          <p className="muted" style={{ margin: "6px 0 0", fontSize: ".82rem" }}>
                            Chưa đẩy sang Jira. Dùng khi cần truy vết với nhà cung cấp bên thứ 3 (RMA thủ công / DLM - Send to Supplier).
                          </p>
                        )}
                      </div>
                    )}
                  </div>
                )}

                {/* Customer Close Button when Resolved */}
                {isCustomer && detail.status === "RESOLVED" && (
                  <div style={{ marginTop: 14 }}>
                    <button
                      type="button"
                      className="btn"
                      disabled={actionBusy}
                      onClick={() => void handleStatusChange("CLOSED")}
                    >
                      Xác nhận &amp; Đóng phiếu hỗ trợ
                    </button>
                  </div>
                )}

                {/* Conversation Thread */}
                <h3 style={{ marginTop: 22 }}>Trao đổi ({detail.messages.length})</h3>
                <div className="support-thread">
                  {detail.messages.length === 0 ? (
                    <p className="muted">Chưa có tin nhắn phản hồi.</p>
                  ) : (
                    detail.messages.map((m) => (
                      <div
                        key={m.id}
                        className={`support-message ${m.senderRole === "customer" ? "customer" : "staff"}`}
                      >
                        <div className="support-message-meta">
                          <strong>{ROLE_LABEL[m.senderRole] ?? m.senderRole}</strong>
                          <span className="muted">
                            {new Date(m.createdAtUtc).toLocaleString("vi-VN")}
                          </span>
                        </div>
                        <p style={{ margin: "6px 0 0", whiteSpace: "pre-wrap" }}>{m.content}</p>
                      </div>
                    ))
                  )}
                </div>

                {(detail.status === "OPEN" || detail.status === "IN_PROGRESS") &&
                  (isCustomer || handledByMe) && (
                    <form style={{ marginTop: 14 }} onSubmit={handleSendReply}>
                      <textarea
                        rows={3}
                        maxLength={3000}
                        value={replyText}
                        disabled={actionBusy}
                        onChange={(e) => setReplyText(e.target.value)}
                        placeholder="Nhập nội dung phản hồi..."
                      />
                      <div className="row" style={{ marginTop: 8 }}>
                        <button
                          type="submit"
                          className="btn"
                          disabled={actionBusy || !replyText.trim()}
                        >
                          {actionBusy ? "Đang gửi..." : "Gửi phản hồi"}
                        </button>
                      </div>
                    </form>
                  )}
              </>
            )}
          </section>
        </div>
      )}

      {isCustomer && (
        <p style={{ marginTop: 20 }}>
          <Link to="/orders" className="btn btn-secondary">
            ← Quay lại Đơn hàng của tôi
          </Link>
        </p>
      )}
    </div>
  );
}
