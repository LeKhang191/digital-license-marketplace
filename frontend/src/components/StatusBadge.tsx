import type { OrderStatus } from "../api/types";
import { useLanguage } from "../i18n/LanguageContext";

const STYLE: Record<OrderStatus, { vi: string; en: string; color: string }> = {
  PENDING: { vi: "Chờ thanh toán", en: "Pending Payment", color: "var(--color-warning)" },
  PAID: { vi: "Đã thanh toán, đang cấp Key", en: "Paid · Delivering Key", color: "var(--color-info)" },
  COMPLETED: { vi: "Hoàn tất", en: "Completed", color: "var(--color-success)" },
  REFUNDING: { vi: "Đang hoàn tiền", en: "Refunding", color: "var(--color-warning)" },
  REFUNDED: { vi: "Đã hoàn tiền", en: "Refunded", color: "var(--color-muted)" },
  CANCELED: { vi: "Đã hủy", en: "Canceled", color: "var(--color-danger)" },
};

export function StatusBadge({ status }: { status: OrderStatus }) {
  const { lang } = useLanguage();
  const s = STYLE[status];
  return (
    <span className="badge" style={{ background: s.color }}>
      {lang === "en" ? s.en : s.vi}
    </span>
  );
}
