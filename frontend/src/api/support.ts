import { api } from "./client";

export interface SupportCategory {
  code: string;
  label: string;
  description: string;
  assignedRole: string;
  assignedRoleLabel: string;
  requiresOrder: boolean;
  requiresLicense: boolean;
  refundMode: "NONE" | "UNREVEALED" | "VERIFIED_DEFECT";
}

export interface SupportMessage {
  id: string;
  ticketId: string;
  senderId: string;
  senderRole: string;
  content: string;
  createdAtUtc: string;
}

export interface SupportTicketSummary {
  id: string;
  orderId?: string | null;
  licenseId?: string | null;
  category: string;
  subject: string;
  status: "OPEN" | "IN_PROGRESS" | "RESOLVED" | "CLOSED";
  assignedRole: string;
  handledBy?: string | null;
  resolutionOutcome?: "APPROVED_REFUND" | "APPROVED_REPLACEMENT" | "REJECTED" | null;
  rejectionReason?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface SupportTicketDetail extends SupportTicketSummary {
  customerId: string;
  description: string;
  messages: SupportMessage[];
}

export interface PagedSupportTickets {
  items: SupportTicketSummary[];
  total: number;
  page: number;
  pageSize: number;
}

export interface CreateSupportTicketPayload {
  orderId?: string | null;
  licenseId?: string | null;
  category: string;
  subject: string;
  description: string;
}

export interface SupportRefundStatus {
  orderId: string;
  requestId: string;
  refundId?: string | null;
  mode: string;
  status: "REQUESTED" | "PREPARED" | "CREDITED" | "COMPLETED" | "REJECTED";
  createdAtUtc: string;
  updatedAtUtc: string;
  rejectionCode?: string | null;
}

export interface ExternalSupportComment {
  id: string;
  body: string;
}

export interface ExternalSupportLink {
  ticketId: string;
  state: "PENDING" | "LINKED" | "UNCERTAIN";
  issueId?: string | null;
  issueKey?: string | null;
  externalStatus?: string | null;
  comments: ExternalSupportComment[];
  syncedAtUtc?: string | null;
}

export interface LicenseReplacementResult {
  ticketId: string;
  orderId: string;
  orderItemId: string;
  productId: string;
  oldLicenseId: string;
  newLicenseId: string;
  handledBy: string;
  createdAtUtc: string;
  alreadyProcessed: boolean;
}

export const listSupportCategories = () =>
  api<SupportCategory[]>("/support/tickets/categories", { cache: "no-store" });

export const listSupportTickets = (params?: {
  status?: string;
  category?: string;
  page?: number;
  pageSize?: number;
}) => {
  const q = new URLSearchParams();
  if (params?.status) q.set("status", params.status);
  if (params?.category) q.set("category", params.category);
  if (params?.page) q.set("page", String(params.page));
  if (params?.pageSize) q.set("pageSize", String(params.pageSize));
  const suffix = q.toString() ? `?${q}` : "";
  return api<PagedSupportTickets>(`/support/tickets${suffix}`, {
    auth: true,
    cache: "no-store",
  });
};

export const getSupportTicket = (id: string) =>
  api<SupportTicketDetail>(`/support/tickets/${encodeURIComponent(id)}`, {
    auth: true,
    cache: "no-store",
  });

export const createSupportTicket = (payload: CreateSupportTicketPayload) =>
  api<SupportTicketDetail>("/support/tickets", {
    method: "POST",
    auth: true,
    body: JSON.stringify(payload),
  });

export const addSupportMessage = (id: string, content: string) =>
  api<SupportMessage>(`/support/tickets/${encodeURIComponent(id)}/messages`, {
    method: "POST",
    auth: true,
    body: JSON.stringify({ content }),
  });

export const updateSupportStatus = (
  id: string,
  status: "IN_PROGRESS" | "RESOLVED" | "CLOSED",
) =>
  api<{ id: string; status: string; handledBy?: string | null }>(
    `/support/tickets/${encodeURIComponent(id)}/status`,
    {
      method: "PATCH",
      auth: true,
      body: JSON.stringify({ status }),
    },
  );

export const approveSupportRefund = (id: string) =>
  api<SupportRefundStatus>(`/support/tickets/${encodeURIComponent(id)}/refund`, {
    method: "POST",
    auth: true,
  });

export const getSupportRefundStatus = (id: string) =>
  api<SupportRefundStatus>(`/support/tickets/${encodeURIComponent(id)}/refund`, {
    auth: true,
    cache: "no-store",
  });

export const rejectSupportTicket = (id: string, reason: string) =>
  api<{
    id: string;
    status: string;
    resolutionOutcome: string;
    rejectionReason: string;
    notificationQueued: boolean;
  }>(`/support/tickets/${encodeURIComponent(id)}/reject`, {
    method: "POST",
    auth: true,
    body: JSON.stringify({ reason }),
  });

export const escalateSupportTicket = (id: string) =>
  api<ExternalSupportLink>(`/support/tickets/${encodeURIComponent(id)}/escalate`, {
    method: "POST",
    auth: true,
  });

export const getExternalSupportLink = (id: string) =>
  api<ExternalSupportLink>(`/support/tickets/${encodeURIComponent(id)}/external`, {
    auth: true,
    cache: "no-store",
  });

export const syncExternalSupportLink = (id: string) =>
  api<ExternalSupportLink>(
    `/support/tickets/${encodeURIComponent(id)}/external/sync`,
    {
      method: "POST",
      auth: true,
    },
  );

export const replaceDefectiveLicense = (ticketId: string) =>
  api<LicenseReplacementResult>(
    `/licenses/replacements/${encodeURIComponent(ticketId)}`,
    {
      method: "POST",
      auth: true,
    },
  );

export const getLicenseReplacement = (ticketId: string) =>
  api<LicenseReplacementResult>(
    `/licenses/replacements/${encodeURIComponent(ticketId)}`,
    {
      auth: true,
      cache: "no-store",
    },
  );
