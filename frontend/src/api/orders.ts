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

export interface PurchasedLicense {
  itemId: string;
  productId: string;
  key: string;
}

export const getOrderKeys = (id: string, signal?: AbortSignal) =>
  api<PurchasedLicense[]>(`/orders/${encodeURIComponent(id)}/keys`, {
    auth: true, cache: "no-store", signal,
  });

export interface VaultStockSummary {
  productId: string;
  available: number;
  allocated: number;
}

export const getVaultStock = () =>
  api<VaultStockSummary[]>("/vault/stock", { auth: true });

export const importVaultKeys = (productId: string, keys: string[]) =>
  api<{ added: number; duplicates: number }>("/vault/keys/import", {
    method: "POST",
    auth: true,
    body: JSON.stringify({ productId, keys }),
  });

export const listAdminRefunds = () =>
  api<Order[]>("/orders/admin/refunds", { auth: true });

export const completeAdminRefund = (orderId: string) =>
  api<Order>(`/orders/${encodeURIComponent(orderId)}/refund`, {
    method: "POST",
    auth: true,
  });
