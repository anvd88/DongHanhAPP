import { appUrl, redirectToLogin } from "./appConfig";

const TOKEN_KEY = "km_token";

export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (t: string) => localStorage.setItem(TOKEN_KEY, t),
  clear: () => localStorage.removeItem(TOKEN_KEY),
};

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {};
  const token = tokenStore.get();
  if (token) headers["Authorization"] = `Bearer ${token}`;
  if (body !== undefined) headers["Content-Type"] = "application/json";

  const res = await fetch(appUrl(path), {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (res.status === 401) {
    tokenStore.clear();
    if (!location.pathname.startsWith("/login") && location.hash !== "#/login") redirectToLogin();
    throw new ApiError(401, "Phiên đăng nhập đã hết hạn.");
  }

  if (!res.ok) {
    let msg = `Lỗi ${res.status}`;
    try {
      const data = await res.json();
      msg = data.message || data.detail || msg;
    } catch { /* body rỗng */ }
    throw new ApiError(res.status, msg);
  }

  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return text ? (JSON.parse(text) as T) : (undefined as T);
}

/** Tải tài nguyên nhị phân (vd. ảnh snapshot camera) kèm Bearer token; trả Blob để tạo object URL. */
async function requestBlob(path: string): Promise<Blob> {
  const headers: Record<string, string> = {};
  const token = tokenStore.get();
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const res = await fetch(appUrl(path), { method: "GET", headers });

  if (res.status === 401) {
    tokenStore.clear();
    if (!location.pathname.startsWith("/login") && location.hash !== "#/login") redirectToLogin();
    throw new ApiError(401, "Phiên đăng nhập đã hết hạn.");
  }

  if (!res.ok) throw new ApiError(res.status, `Lỗi ${res.status}`);
  return res.blob();
}

/** Tải LÊN một blob nhị phân (octet-stream) kèm Bearer token; trả JSON kết quả. */
async function postBlob<T>(path: string, blob: Blob): Promise<T> {
  const headers: Record<string, string> = { "Content-Type": "application/octet-stream" };
  const token = tokenStore.get();
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const res = await fetch(appUrl(path), { method: "POST", headers, body: blob });

  if (res.status === 401) {
    tokenStore.clear();
    if (!location.pathname.startsWith("/login") && location.hash !== "#/login") redirectToLogin();
    throw new ApiError(401, "Phiên đăng nhập đã hết hạn.");
  }
  if (!res.ok) {
    let msg = `Lỗi ${res.status}`;
    try {
      const data = await res.json();
      msg = data.message || data.detail || msg;
    } catch { /* body rỗng */ }
    throw new ApiError(res.status, msg);
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return text ? (JSON.parse(text) as T) : (undefined as T);
}

export const api = {
  get: <T>(p: string) => request<T>("GET", p),
  post: <T>(p: string, body?: unknown) => request<T>("POST", p, body ?? {}),
  put: <T>(p: string, body?: unknown) => request<T>("PUT", p, body ?? {}),
  del: <T>(p: string) => request<T>("DELETE", p),
  getBlob: (p: string) => requestBlob(p),
  postBlob: <T>(p: string, blob: Blob) => postBlob<T>(p, blob),
};
