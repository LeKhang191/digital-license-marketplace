export interface Product {
  id: string;
  name: string;
  slug: string;
  description: string;
  category: string;
  price: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface PagedProducts {
  items: Product[];
  page: number;
  pageSize: number;
  total: number;
}

export type OrderStatus =
  | "PENDING"
  | "PAID"
  | "COMPLETED"
  | "REFUNDING"
  | "REFUNDED"
  | "CANCELED";

export interface OrderItem {
  id: string;
  productId: string;
  price: number;
}

export interface Order {
  id: string;
  status: OrderStatus;
  totalAmount: number;
  createdAtUtc: string;
  items: OrderItem[];
  paymentUrl?: string | null;
}
