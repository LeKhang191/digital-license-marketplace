export interface CartItem {
  productId: string;
  name: string;
  price: number;
  image?: string;
  quantity: number;
}

export const CART_STORAGE_KEY = "dlm_cart_v2";
export const GUEST_SCOPE = "guest";
export type CartStore = Record<string, CartItem[]>;

export function customerScope(subject: string): string {
  return `customer:${subject}`;
}

export function normalizeQuantity(quantity: number): number {
  return Math.min(10, Math.max(1, Math.trunc(Number.isFinite(quantity) ? quantity : 1)));
}

export function mergeItems(existing: CartItem[], incoming: CartItem[]): CartItem[] {
  const result = new Map<string, CartItem>();
  for (const item of [...existing, ...incoming]) {
    const previous = result.get(item.productId);
    result.set(item.productId, {
      ...item,
      image: item.image ?? previous?.image,
      quantity: Math.min(10, (previous?.quantity ?? 0) + normalizeQuantity(item.quantity)),
    });
  }
  return [...result.values()];
}

export function readStore(raw: string | null): CartStore {
  const result: CartStore = Object.create(null);
  try {
    const parsed = JSON.parse(raw ?? "{}");
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return result;
    for (const [scope, items] of Object.entries(parsed)) {
      if (scope !== GUEST_SCOPE && !scope.startsWith("customer:")) continue;
      if (!Array.isArray(items)) continue;
      result[scope] = mergeItems([], items.filter((i): i is CartItem =>
        !!i && typeof i.productId === "string" && !!i.productId &&
        typeof i.name === "string" && typeof i.price === "number" &&
        Number.isFinite(i.price) && i.price >= 0 &&
        typeof i.quantity === "number" && Number.isFinite(i.quantity) && i.quantity >= 1 &&
        (i.image === undefined || typeof i.image === "string")));
    }
  } catch { /* Ignore invalid storage. */ }
  return result;
}

export function activateScope(store: CartStore, scope: string): CartStore {
  if (!scope.startsWith("customer:") || !store[GUEST_SCOPE]?.length) return store;
  return { ...store, [scope]: mergeItems(store[scope] ?? [], store[GUEST_SCOPE]), [GUEST_SCOPE]: [] };
}
