import { api } from "./client";
import { config } from "../config";

export interface WalletBalance {
  balance: number;
  currency: string;
}

export interface WalletTransaction {
  id: string;
  type: string;
  amount: number;
  balanceAfter: number;
  referenceId: string;
  createdAtUtc: string;
}

export interface WalletTopUpSession {
  topUpId: string;
  paymentRef: string;
  amount: number;
  status: string;
  expiresAtUtc: string;
  paymentUrl: string;
}

export interface WalletTopUpStatus {
  topUpId: string;
  paymentRef: string;
  amount: number;
  status: "PENDING" | "SUCCEEDED" | "FAILED";
  vnpTransactionNo?: string | null;
  createdAtUtc: string;
  paidAtUtc?: string | null;
  walletBalance: number;
  currency: string;
}

export const getMyWallet = () =>
  api<WalletBalance>("/wallet/me", { auth: true, cache: "no-store" });

export const listWalletTransactions = (page = 1, pageSize = 20) =>
  api<WalletTransaction[]>(
    `/wallet/me/transactions?page=${page}&pageSize=${pageSize}`,
    { auth: true, cache: "no-store" },
  );

export const createWalletTopUp = (amount: number) =>
  api<WalletTopUpSession>("/wallet/me/topups", {
    method: "POST",
    auth: true,
    body: JSON.stringify({ amount }),
  });

export const getWalletTopUp = (paymentRef: string) =>
  api<WalletTopUpStatus>(
    `/wallet/me/topups/${encodeURIComponent(paymentRef)}`,
    { auth: true, cache: "no-store" },
  );

export async function confirmVnPayReturn(
  search: string,
): Promise<{ RspCode?: string; Message?: string } | null> {
  if (!search || !search.includes("vnp_SecureHash=")) return null;
  try {
    const res = await fetch(`${config.apiUrl}/orders/vnpay-ipn${search}`, {
      method: "GET",
      headers: { Accept: "application/json" },
      cache: "no-store",
    });
    if (!res.ok) return null;
    return (await res.json()) as { RspCode?: string; Message?: string };
  } catch {
    return null;
  }
}
