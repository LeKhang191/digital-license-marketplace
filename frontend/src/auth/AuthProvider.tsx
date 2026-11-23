import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { initKeycloak, keycloak } from "./keycloak";

interface AuthState {
  ready: boolean;
  authenticated: boolean;
  username?: string;
  isAdmin: boolean;
  login: () => void;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [, setAuthVersion] = useState(0);

  useEffect(() => {
    let mounted = true;
    const sync = () => { if (mounted) setAuthVersion((v) => v + 1); };
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

  const roles = keycloak.tokenParsed?.realm_access?.roles ?? [];

  const value: AuthState = {
    ready,
    authenticated: !!keycloak.authenticated,
    username: keycloak.tokenParsed?.preferred_username as string | undefined,
    isAdmin: roles.includes("admin"),
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
