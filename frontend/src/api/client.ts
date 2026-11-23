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
  if (init.body) headers.set("Content-Type", "application/json");

  if (init.auth) {
    const token = await getToken();
    // Không gửi request cần đăng nhập mà thiếu token (getToken đã chuyển
    // hướng sang trang đăng nhập khi phiên hết hạn).
    if (!token) throw new ApiError(401, "Phiên đăng nhập đã hết hạn");
    headers.set("Authorization", `Bearer ${token}`);
  }

  const res = await fetch(`${config.apiUrl}${path}`, { ...init, headers });

  if (!res.ok) {
    let message = `Lỗi ${res.status}`;
    try {
      const body = await res.json();
      message = body.error ?? body.title ?? message;
    } catch {
      /* phản hồi không phải JSON */
    }
    throw new ApiError(res.status, message);
  }

  return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
}
