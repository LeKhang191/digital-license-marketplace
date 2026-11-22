import Keycloak from "keycloak-js";
import { config } from "../config";

export const keycloak = new Keycloak({
  url: config.keycloakUrl,
  realm: config.realm,
  clientId: config.clientId,
});

// React StrictMode chạy effect 2 lần: chỉ init Keycloak đúng 1 lần
let initPromise: Promise<boolean> | null = null;

export function initKeycloak(): Promise<boolean> {
  initPromise ??= keycloak.init({
    onLoad: "check-sso",
    pkceMethod: "S256",
    checkLoginIframe: false,
  });
  return initPromise;
}

// Lấy token còn hạn (tự làm mới nếu sắp hết)
export async function getToken(): Promise<string | undefined> {
  if (!keycloak.authenticated) return undefined;
  try {
    await keycloak.updateToken(30);
  } catch {
    keycloak.login();
    return undefined;
  }
  return keycloak.token;
}
