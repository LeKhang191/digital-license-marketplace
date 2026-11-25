
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { initKeycloak, keycloak } from "./keycloak";
import { Roles, STAFF_ROLES, type BusinessRole } from "./roles";

interface AuthState {
  ready: boolean;
  authenticated: boolean;
  username?: string;

  roles: string[];

  isAdmin: boolean;
  isCustomer: boolean;
  isCatalogManager: boolean;
  isLicenseCoordinator: boolean;
  isTransactionAuditor: boolean;
  isStaff: boolean;

  hasRole: (role: BusinessRole) => boolean;

  login: () => void;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [, setAuthVersion] = useState(0);

  useEffect(() => {
    let mounted = true;

    const sync = () => {
      if (mounted) setAuthVersion((v) => v + 1);
    };

    keycloak.onAuthSuccess = sync;
    keycloak.onAuthRefreshSuccess = sync;
    keycloak.onAuthLogout = sync;
    keycloak.onAuthError = sync;
    keycloak.onAuthRefreshError = sync;

    keycloak.onTokenExpired = () => {
      void keycloak.updateToken(30).then(sync).catch(sync);
    };

    void initKeycloak().catch(sync).finally(() => {
      if (mounted) {
        setReady(true);
        sync();
      }
    });

    return () => {
      mounted = false;
      keycloak.onAuthSuccess = undefined;
      keycloak.onAuthRefreshSuccess = undefined;
      keycloak.onAuthLogout = undefined;
      keycloak.onAuthError = undefined;
      keycloak.onAuthRefreshError = undefined;
      keycloak.onTokenExpired = undefined;
    };
  }, []);

  const authenticated = !!keycloak.authenticated;

  const roles = authenticated
    ? keycloak.tokenParsed?.realm_access?.roles ?? []
    : [];

  const hasRole = (role: BusinessRole): boolean => {
    return authenticated && roles.includes(role);
  };

  const isStaff = STAFF_ROLES.some(hasRole);
  const isAdmin = authenticated && roles.includes("admin");

  const value: AuthState = {
    ready,
    authenticated,
    username: keycloak.tokenParsed?.preferred_username as string | undefined,

    roles,
    isAdmin,

    isCustomer: hasRole(Roles.CUSTOMER) && !isStaff && !isAdmin,

    isCatalogManager: hasRole(Roles.CATALOG_MANAGER),

    isLicenseCoordinator: hasRole(Roles.LICENSE_COORDINATOR),

    isTransactionAuditor: hasRole(Roles.TRANSACTION_AUDITOR),

    isStaff,
    hasRole,

    login: () => void keycloak.login({ redirectUri: window.location.href }),

    logout: () => void keycloak.logout({ redirectUri: window.location.origin }),
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);

  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");

  return ctx;
}
