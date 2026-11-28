import { api } from "./client";
import type { Order } from "./types";

export type PaymentMethod = "VNPAY" | "WALLET";

export interface CreateOrderItem {
  productId: string;
  quantity: number;
}

export const createOrder = (
  productId: string,
  quantity: number,
  paymentMethod: PaymentMethod = "VNPAY",
) =>
  api<Order>("/orders", {
    method: "POST",
    auth: true,
    body: JSON.stringify({ productId, quantity, paymentMethod }),
  });

export const createMultiOrder = (
  items: CreateOrderItem[],
  paymentMethod: PaymentMethod = "VNPAY",
) =>
  api<Order>("/orders", {
    method: "POST",
    auth: true,
    body: JSON.stringify({ items, paymentMethod }),
  });

export const listOrders = () => api<Order[]>("/orders", { auth: true });
export const getOrder = (id: string) => api<Order>(`/orders/${id}`, { auth: true });
export const payOrder = (id: string) =>
  api<Order>(`/orders/${id}/pay`, { method: "POST", auth: true });
