import { useEffect, useRef } from "react";
import type { Order } from "../api/types";
import { vnd } from "./format";

export interface ReceiptLine {
  label: string;
  value: string;
}

export interface ReceiptData {
  title: string;
  subtitle?: string;
  code: string;
  dateText: string;
  statusText: string;
  lines: ReceiptLine[];
  totalLabel: string;
  totalValue: string;
  note: string;
}

export function ReceiptDialog({
  receipt,
  onClose,
}: {
  receipt: ReceiptData | null;
  onClose: () => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (receipt && !dialog.open) dialog.showModal();
    else if (!receipt && dialog.open) dialog.close();
  }, [receipt]);

  return (
    <dialog
      ref={dialogRef}
      className="help-dialog receipt-dialog"
      onClose={onClose}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      {receipt && (
        <div className="dialog-content receipt-content">
          <button
            type="button"
            className="dialog-close no-print"
            aria-label="Đóng"
            onClick={onClose}
          >
            ×
          </button>

          <div className="receipt-header">
            <span className="receipt-brand">DIGITAL KEY</span>
            <h2>{receipt.title}</h2>
            {receipt.subtitle && <p className="muted">{receipt.subtitle}</p>}
          </div>

          <div className="receipt-meta">
            <div>
              <span className="muted">Mã chứng từ</span>
              <code>{receipt.code}</code>
            </div>
            <div>
              <span className="muted">Thời gian</span>
              <strong>{receipt.dateText}</strong>
            </div>
            <div>
              <span className="muted">Trạng thái</span>
              <strong>{receipt.statusText}</strong>
            </div>
          </div>

          <dl className="receipt-lines">
            {receipt.lines.map((line) => (
              <div key={line.label} className="receipt-line">
                <dt className="muted">{line.label}</dt>
                <dd>{line.value}</dd>
              </div>
            ))}
          </dl>

          <div className="receipt-total">
            <span>{receipt.totalLabel}</span>
            <strong>{receipt.totalValue}</strong>
          </div>

          <p className="muted receipt-note">{receipt.note}</p>

          <div className="receipt-actions no-print">
            <button
              type="button"
              className="btn"
              onClick={() => window.print()}
            >
              In / Lưu PDF
            </button>
            <button
              type="button"
              className="btn btn-secondary"
              onClick={onClose}
            >
              Đóng
            </button>
          </div>
        </div>
      )}
    </dialog>
  );
}

const ORDER_STATUS_TEXT: Record<Order["status"], string> = {
  PENDING: "Chờ thanh toán",
  PAID: "Đã thanh toán (đang cấp key)",
  COMPLETED: "Hoàn thành",
  REFUNDING: "Đang hoàn tiền",
  REFUNDED: "Đã hoàn tiền",
  CANCELED: "Đã hủy",
};

export function buildOrderReceipt(
  order: Order,
  productName: (id: string) => string,
): ReceiptData {
  const counts = new Map<string, { qty: number; subtotal: number }>();
  for (const item of order.items) {
    const cur = counts.get(item.productId) ?? { qty: 0, subtotal: 0 };
    cur.qty += 1;
    cur.subtotal += item.price;
    counts.set(item.productId, cur);
  }
  const lines: ReceiptLine[] = [
    { label: "Mã đơn hàng", value: order.id },
    ...Array.from(counts.entries()).map(([productId, info]) => ({
      label: `${productName(productId)} × ${info.qty}`,
      value: vnd(info.subtotal),
    })),
  ];
  return {
    title: "Chứng từ giao dịch",
    subtitle: "Biên nhận điện tử — Digital License Marketplace",
    code: order.id.slice(0, 8).toUpperCase(),
    dateText: new Date(order.createdAtUtc).toLocaleString("vi-VN"),
    statusText: ORDER_STATUS_TEXT[order.status] ?? order.status,
    lines,
    totalLabel: "Tổng thanh toán",
    totalValue: vnd(order.totalAmount),
    note: "Đây là chứng từ xác nhận giao dịch trên hệ thống, không phải hóa đơn thuế GTGT. Không chứa plaintext license key.",
  };
}
