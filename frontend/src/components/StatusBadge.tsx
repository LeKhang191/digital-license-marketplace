import type { OrderStatus } from "../api/types";

const STYLE: Record<OrderStatus, { label: string; color: string }> = {
  PENDING: { label: "Chờ thanh toán", color: "var(--color-warning)" },
  PAID: { label: "Đã thanh toán, đang cấp Key", color: "var(--color-info)" },
  COMPLETED: { label: "Hoàn tất", color: "var(--color-success)" },
  REFUNDING: { label: "Đang hoàn tiền", color: "var(--color-warning)" },
  REFUNDED: { label: "Đã hoàn tiền", color: "var(--color-muted)" },
  CANCELED: { label: "Đã hủy", color: "var(--color-danger)" },
};

export function StatusBadge({ status }: { status: OrderStatus }) {
  const s = STYLE[status];
  return (
    <span className="badge" style={{ background: s.color }}>
      {s.label}
    </span>
  );
}
