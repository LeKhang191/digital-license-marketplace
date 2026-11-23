import type { OrderStatus } from "../api/types";

export const POLL_INTERVAL_MS = 2000;
// 30 lần x 2s = 60s. Sau đó dừng hỏi lại và báo người dùng xem ở trang đơn hàng.
export const MAX_POLL_ATTEMPTS = 30;

const FINAL: ReadonlySet<OrderStatus> = new Set([
  "COMPLETED",
  "REFUNDED",
  "CANCELED",
]);

export const isFinalStatus = (status: OrderStatus): boolean =>
  FINAL.has(status);

// `attempt` = số lần đã hỏi (kể cả lần vừa xong). Trả null khi không hỏi nữa.
export function nextPollDelayMs(
  status: OrderStatus,
  attempt: number,
): number | null {
  if (isFinalStatus(status)) return null;
  if (attempt >= MAX_POLL_ATTEMPTS) return null;
  return POLL_INTERVAL_MS;
}
