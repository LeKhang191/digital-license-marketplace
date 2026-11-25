
export const Roles = {
  CUSTOMER: "customer",
  CATALOG_MANAGER: "catalog_manager",
  LICENSE_COORDINATOR: "license_coordinator",
  TRANSACTION_AUDITOR: "transaction_auditor",
} as const;

export type BusinessRole =
  (typeof Roles)[keyof typeof Roles];

export const STAFF_ROLES: BusinessRole[] = [
  Roles.CATALOG_MANAGER,
  Roles.LICENSE_COORDINATOR,
  Roles.TRANSACTION_AUDITOR,
];
