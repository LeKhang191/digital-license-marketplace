
import { api } from "./client";
import type {
  PagedProducts,
  Product,
  ProductUpsertRequest,
} from "./types";

export function listProducts(params: {
  search?: string;
  category?: string;
  page?: number;
}) {
  const q = new URLSearchParams({ pageSize: "12" });

  if (params.search) q.set("search", params.search);
  if (params.category) q.set("category", params.category);
  if (params.page) q.set("page", String(params.page));

  return api<PagedProducts>(`/catalog/products?${q}`);
}

export const getProduct = (id: string) =>
  api<Product>(`/catalog/products/${id}`);

export function createProduct(data: ProductUpsertRequest) {
  return api<Product>("/catalog/products", {
    method: "POST",
    auth: true,
    body: JSON.stringify(data),
  });
}

export function updateProduct(
  id: string,
  data: ProductUpsertRequest,
) {
  return api<Product>(`/catalog/products/${id}`, {
    method: "PUT",
    auth: true,
    body: JSON.stringify(data),
  });
}

export function deleteProduct(id: string) {
  return api<void>(`/catalog/products/${id}`, {
    method: "DELETE",
    auth: true,
  });
}
