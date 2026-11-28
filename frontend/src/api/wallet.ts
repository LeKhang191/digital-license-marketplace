import { api } from "./client";

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

export const getMyWallet = () =>
  api<WalletBalance>("/wallet/me", { auth: true, cache: "no-store" });

export const listWalletTransactions = (page = 1, pageSize = 20) =>
  api<WalletTransaction[]>(
    `/wallet/me/transactions?page=${page}&pageSize=${pageSize}`,
    { auth: true, cache: "no-store" },
  );
