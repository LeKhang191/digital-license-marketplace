import { api } from "./client";
import type { PagedProducts, Product } from "./types";

export function listProducts(params: { search?: string; category?: string; page?: number; pageSize?: number }) {
  const q = new URLSearchParams({ pageSize: String(params.pageSize ?? 12) });
  if (params.search) q.set("search", params.search);
  if (params.category) q.set("category", params.category);
  if (params.page) q.set("page", String(params.page));
  return api<PagedProducts>(`/catalog/products?${q}`);
}

export const getProduct = (id: string) => api<Product>(`/catalog/products/${id}`);

export const previewSteam = (app: string) => api<import("./types").SteamMetadata>(`/catalog/steam-import/preview?${new URLSearchParams({app})}`, { auth: true });
export const importSteam = (app: string, price: number) => api<Product>("/catalog/steam-import", { method: "POST", auth: true, body: JSON.stringify({app, price}) });
