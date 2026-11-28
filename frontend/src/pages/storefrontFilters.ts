import type { Product } from "../api/types";

export const PAGE_SIZE = 12;
export const SORT_OPTIONS = ["default", "price-asc", "price-desc", "name-asc", "name-desc", "newest"] as const;
export type StoreSort = typeof SORT_OPTIONS[number];

export function readPrice(value: string | null): number | undefined {
  if (!value || !/^\d+(\.\d+)?$/.test(value)) return undefined;
  const number = Number(value);
  return Number.isFinite(number) && number >= 0 ? number : undefined;
}

export function filterProducts(products: Product[], filters: {
  search: string; category: string; genre: string;
  min?: number; max?: number; sort: StoreSort;
}): Product[] {
  const normalize = (value: string) => value.toLocaleLowerCase("vi");
  const search = normalize(filters.search.trim());
  const items = products.filter(p => p.isActive &&
    (!search || normalize(`${p.name} ${p.description ?? ""}`).includes(search)) &&
    (!filters.category || normalize(p.category) === normalize(filters.category)) &&
    (!filters.genre || (p.steam?.genres ?? []).some(g => normalize(g) === normalize(filters.genre))) &&
    (filters.min === undefined || p.price >= filters.min) &&
    (filters.max === undefined || p.price <= filters.max));
  const nameOrder = (a: Product, b: Product) => a.name.localeCompare(b.name, "vi", { sensitivity: "base" });
  const time = (p: Product) => Date.parse(p.createdAtUtc) || 0;
  if (filters.sort !== "default") items.sort((a, b) => {
    let result = 0;
    switch (filters.sort) {
      case "price-asc": result = a.price - b.price; break;
      case "price-desc": result = b.price - a.price; break;
      case "name-asc": result = nameOrder(a, b); break;
      case "name-desc": result = nameOrder(b, a); break;
      case "newest": result = time(b) - time(a); break;
    }
    return result || a.id.localeCompare(b.id);
  });
  return items;
}

export function pageNumbers(page: number, last: number): (number | string)[] {
  const selected = new Set([1, last]);
  for (let i = Math.max(1, page - 2); i <= Math.min(last, page + 2); i++) selected.add(i);
  const result: (number | string)[] = [];
  for (const n of [...selected].sort((a, b) => a - b)) {
    const previous = result[result.length - 1];
    if (typeof previous === "number" && n - previous > 1) result.push(`gap-${n}`);
    result.push(n);
  }
  return result;
}
