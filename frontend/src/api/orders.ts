import { api } from "./client";
import type { Order } from "./types";

export const createOrder = (productId: string, quantity: number) =>
  api<Order>("/orders", {
    method: "POST",
    auth: true,
    body: JSON.stringify({ productId, quantity }),
  });

export const listOrders = () => api<Order[]>("/orders", { auth: true });
export const getOrder = (id: string) => api<Order>(`/orders/${id}`, { auth: true });
export const payOrder = (id: string) =>
  api<Order>(`/orders/${id}/pay`, { method: "POST", auth: true });
