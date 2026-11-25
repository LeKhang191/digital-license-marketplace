
import { config } from "../config";
import { getToken } from "../auth/keycloak";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function api<T>(
  path: string,
  init: RequestInit & { auth?: boolean } = {},
): Promise<T> {
  const headers = new Headers(init.headers);

  headers.set("Accept", "application/json");

  // Let the browser generate multipart boundaries.
  if (init.body instanceof FormData) {
    headers.delete("Content-Type");
  } else if (init.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  if (init.auth) {
    const token = await getToken();

    if (!token) {
      throw new ApiError(
        401,
        "Phiên đăng nhập đã hết hạn",
      );
    }

    headers.set("Authorization", `Bearer ${token}`);
  }

  const res = await fetch(
    `${config.apiUrl}${path}`,
    { ...init, headers },
  );

  if (!res.ok) {
    let message = `Lỗi ${res.status}`;

    try {
      const body = await res.json();

      message = body.error ?? body.title ?? message;
    } catch {
      // Response is not JSON.
    }

    throw new ApiError(res.status, message);
  }

  return res.status === 204
    ? (undefined as T)
    : ((await res.json()) as T);
}
