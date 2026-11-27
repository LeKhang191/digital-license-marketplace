export interface SteamMetadata {
  source?: string; appId: number; name: string; description: string; headerImage: string; posterImage: string;
  screenshots: string[]; genres: string[]; developers: string[]; publishers: string[];
  releaseDate: string; minimumRequirements: string; recommendedRequirements: string; trailerUrl?: string | null;
}

export interface Product {
  steam?: SteamMetadata | null;
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


export interface ProductUpsertRequest {
  name: string;
  slug: string;
  description: string;
  category: string;
  price: number;
  isActive: boolean;
}


export type LicenseStatus =
  | "AVAILABLE"
  | "ASSIGNED"
  | "DISABLED";

export interface LicenseKey {
  id: string;
  productId: string;
  status: LicenseStatus;
  createdAtUtc: string;
  assignedAtUtc: string | null;
}

export interface LicenseInventory {
  productId: string;
  total: number;
  available: number;
  assigned: number;
  disabled: number;
}

export interface ImportLicensesRequest {
  productId: string;
  keys: string[];
}

export interface ImportLicensesResponse {
  productId: string;
  total: number;
  imported: number;
  duplicates: number;
  invalid: number;
}

export interface CustomerLicense {
  id: string;
  productId: string;
  orderId: string;
  orderItemId: string;
  status: LicenseStatus;
  assignedAtUtc: string;
}

export interface RevealLicenseResponse {
  id: string;
  licenseKey: string;
}
