
import { api } from "./client";

import type {
  LicenseInventory,
  LicenseKey,
  ImportLicensesRequest,
  ImportLicensesResponse,
} from "./types";

export function listLicenseInventory() {
  return api<LicenseInventory[]>(
    "/licenses/inventory",
    {
      auth: true,
    },
  );
}

export function listProductLicenses(productId: string) {
  return api<LicenseKey[]>(
    `/licenses/products/${productId}`,
    {
      auth: true,
    },
  );
}

export function importLicenses(
  data: ImportLicensesRequest,
) {
  return api<ImportLicensesResponse>(
    "/licenses/import",
    {
      method: "POST",
      auth: true,
      body: JSON.stringify(data),
    },
  );
}

export function importLicenseFile(
  productId: string,
  file: File,
) {
  const formData = new FormData();

  formData.append("productId", productId);
  formData.append("file", file);

  return api<ImportLicensesResponse>(
    "/licenses/import-file",
    {
      method: "POST",
      auth: true,
      body: formData,
    },
  );
}

export function disableLicense(id: string) {
  return api<void>(
    `/licenses/${id}/disable`,
    {
      method: "PATCH",
      auth: true,
    },
  );
}

export function listMyLicenses(orderId?: string) {
  const query = orderId
    ? `?orderId=${encodeURIComponent(orderId)}`
    : "";

  return api<import("./types").CustomerLicense[]>(
    `/licenses/me${query}`,
    {
      auth: true,
      cache: "no-store",
    },
  );
}

export function revealMyLicense(id: string) {
  return api<import("./types").RevealLicenseResponse>(
    `/licenses/me/${encodeURIComponent(id)}/reveal`,
    {
      auth: true,
      cache: "no-store",
    },
  );
}

export interface KinguinQuote {
  mode: "Mock" | "Sandbox";
  supplierProductId: string;
  currency: string;
  inStock: boolean;
  quote: { price: number; textQty: number; totalQty: number } | null;
  quantity: number;
  canFulfill: boolean;
  estimatedCost: number | null;
  previewOnly: boolean;
}

export function getKinguinQuote(productId: string, quantity: number, signal?: AbortSignal) {
  return api<KinguinQuote>(
    `/licenses/supplier/kinguin/${encodeURIComponent(productId)}?quantity=${quantity}`,
    { auth: true, cache: "no-store", signal },
  );
}

export interface KinguinMockOrder {
  requestId: string;
  productId: string;
  quantity: number;
  totalCost: number;
  status: "MOCK_PENDING" | "MOCK_FULFILLED" | "MOCK_OUT_OF_STOCK";
  simulated: boolean;
  charged: boolean;
}

export function enqueueKinguinMockOrder(requestId: string, productId: string, quantity: number) {
  return api<KinguinMockOrder>("/licenses/supplier/kinguin/mock-orders/async", {
    method: "POST", auth: true, body: JSON.stringify({ requestId, productId, quantity }),
  });
}

export function getKinguinMockOrder(requestId: string) {
  return api<KinguinMockOrder>(`/licenses/supplier/kinguin/mock-orders/${encodeURIComponent(requestId)}`, {
    auth: true, cache: "no-store",
  });
}
