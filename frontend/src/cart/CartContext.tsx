import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import type { Product } from "../api/types";

export interface CartItem {
  productId: string;
  name: string;
  price: number;
  image?: string;
  quantity: number;
}

interface CartContextValue {
  items: CartItem[];
  totalCount: number;
  totalAmount: number;
  addItem: (product: Product, quantity: number) => void;
  updateQuantity: (productId: string, quantity: number) => void;
  removeItem: (productId: string) => void;
  clearCart: () => void;
}

const STORAGE_KEY = "dlm_cart_v1";

function loadInitialCart(): CartItem[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter(
      (i): i is CartItem =>
        i &&
        typeof i.productId === "string" &&
        typeof i.name === "string" &&
        typeof i.price === "number" &&
        typeof i.quantity === "number" &&
        i.quantity >= 1,
    );
  } catch {
    return [];
  }
}

const CartContext = createContext<CartContextValue | null>(null);

export function CartProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<CartItem[]>(loadInitialCart);

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(items));
    } catch {
      // Ignore storage quota errors.
    }
  }, [items]);

  function addItem(product: Product, quantity: number) {
    const qty = Math.min(10, Math.max(1, Math.trunc(quantity)));
    const image = product.steam?.posterImage || product.steam?.headerImage || undefined;
    setItems((prev) => {
      const existing = prev.find((i) => i.productId === product.id);
      if (existing) {
        return prev.map((i) =>
          i.productId === product.id
            ? { ...i, name: product.name, price: product.price, image: image ?? i.image, quantity: Math.min(10, i.quantity + qty) }
            : i,
        );
      }
      return [...prev, { productId: product.id, name: product.name, price: product.price, image, quantity: qty }];
    });
  }

  function updateQuantity(productId: string, quantity: number) {
    const qty = Math.min(10, Math.max(1, Math.trunc(quantity)));
    setItems((prev) => prev.map((i) => (i.productId === productId ? { ...i, quantity: qty } : i)));
  }

  function removeItem(productId: string) {
    setItems((prev) => prev.filter((i) => i.productId !== productId));
  }

  function clearCart() {
    setItems([]);
  }

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
