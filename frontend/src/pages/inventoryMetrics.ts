import type { LicenseInventory, Product } from "../api/types";

export function inventoryRows(stock: LicenseInventory[], activeProducts: Product[]): LicenseInventory[] {
  const rows = new Map(stock.map(row => [row.productId, row]));
  for (const product of activeProducts) if (!rows.has(product.id)) rows.set(product.id, {
    productId: product.id, total: 0, available: 0, assigned: 0, disabled: 0,
    availableKnownCostVnd: 0, availableKnownCostKeys: 0,
  });
  return [...rows.values()];
}

export function stockLevel(available: number, threshold: number) {
  return available === 0 ? "empty" : available <= threshold ? "low" : "ok";
}

export function stockValuation(row: LicenseInventory, price?: number) {
  const knownKeys = row.availableKnownCostKeys ?? 0;
  const knownCost = row.availableKnownCostVnd ?? 0;
  return { knownKeys, knownCost, unknownKeys: row.available - knownKeys,
    estimatedMargin: price === undefined || knownKeys === 0 ? null : price * knownKeys - knownCost };
}
