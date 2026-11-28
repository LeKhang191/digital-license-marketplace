import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import type { Product } from "../api/types";

import { useAuth } from "../auth/AuthProvider";
import { CART_STORAGE_KEY, GUEST_SCOPE, activateScope, customerScope, mergeItems, normalizeQuantity, readStore, type CartItem, type CartStore } from "./cartStorage";
export type { CartItem } from "./cartStorage";

interface CartContextValue {
  items: CartItem[];
  totalCount: number;
  totalAmount: number;
  addItem: (product: Product, quantity: number) => void;
  updateQuantity: (productId: string, quantity: number) => void;
  removeItem: (productId: string) => void;
  clearCart: () => void;
}

const CartContext = createContext<CartContextValue | null>(null);

// A single storage write transfers the guest cart and clears it together.
// Never adopt dlm_cart_v1: its owner cannot be determined reliably.
let memoryStore: CartStore = Object.create(null);
let storageFailed = false;
function loadStore(): CartStore {
  if (storageFailed) return memoryStore;
  try { return readStore(localStorage.getItem(CART_STORAGE_KEY)); }
  catch { return memoryStore; }
}
function saveStore(store: CartStore) {
  memoryStore = store;
  try { localStorage.setItem(CART_STORAGE_KEY, JSON.stringify(store)); }
  catch { storageFailed = true; /* Keep the current session in memory. */ }
}

export function CartProvider({ children }: { children: ReactNode }) {
  const { ready, authenticated, isCustomer, subject } = useAuth();
  const scope = !ready ? null : !authenticated ? GUEST_SCOPE :
    isCustomer && subject ? customerScope(subject) : null;
  return <ScopedCart key={scope ?? "disabled"} scope={scope}>{children}</ScopedCart>;
}

function ScopedCart({ children, scope }: { children: ReactNode; scope: string | null }) {
  const [items, setItems] = useState<CartItem[]>([]);
  const [loaded, setLoaded] = useState(false);
  useEffect(() => {
    if (scope) {
      const before = loadStore();
      const after = activateScope(before, scope);
      if (after !== before) saveStore(after);
      setItems(after[scope] ?? []);
    }
    setLoaded(true);
  }, [scope]);

  function change(update: (previous: CartItem[]) => CartItem[]) {
    if (!scope || !loaded) return;
    const store = loadStore();
    const next = update(store[scope] ?? []);
    saveStore({ ...store, [scope]: next });
    setItems(next);
  }

  function addItem(product: Product, quantity: number) {
    const image = product.steam?.posterImage || product.steam?.headerImage || undefined;
    change(prev => mergeItems(prev, [{ productId: product.id, name: product.name,
      price: product.price, image, quantity: normalizeQuantity(quantity) }]));
  }

  function updateQuantity(productId: string, quantity: number) {
    change(prev => prev.map(i => i.productId === productId ? { ...i, quantity: normalizeQuantity(quantity) } : i));
  }

  function removeItem(productId: string) {
    change(prev => prev.filter(i => i.productId !== productId));
  }

  function clearCart() { change(() => []); }

  if (scope && !loaded) return <p role="status">Đang tải giỏ hàng...</p>;

  const totalCount = items.reduce((sum, i) => sum + i.quantity, 0);
  const totalAmount = items.reduce((sum, i) => sum + i.price * i.quantity, 0);

  return (
    <CartContext.Provider value={{ items, totalCount, totalAmount, addItem, updateQuantity, removeItem, clearCart }}>
      {children}
    </CartContext.Provider>
  );
}

export function useCart() {
  const ctx = useContext(CartContext);
  if (!ctx) throw new Error("useCart must be used within CartProvider");
  return ctx;
}
