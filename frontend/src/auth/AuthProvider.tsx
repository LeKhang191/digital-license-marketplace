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

  useEffect(() => {
    initKeycloak().finally(() => setReady(true));
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
