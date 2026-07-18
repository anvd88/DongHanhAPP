import { appUrl, redirectToLogin } from "./appConfig";

const TOKEN_KEY = "km_token";
const KIOSK_KEY = "km_kiosk_key";

export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (t: string) => localStorage.setItem(TOKEN_KEY, t),
  clear: () => localStorage.removeItem(TOKEN_KEY),
};

/** Khóa kiosk cấp riêng cho THIẾT BỊ chấm công (không phải tài khoản). Lưu cục bộ, gửi qua header
 *  X-Kiosk-Key để backend cho phép chấm công ẩn danh khi hệ thống mở ra Internet. */
export const kioskKeyStore = {
  get: () => { try { return localStorage.getItem(KIOSK_KEY); } catch { return null; } },
  set: (k: string) => { try { localStorage.setItem(KIOSK_KEY, k.trim()); } catch { /* ignore */ } },
  clear: () => { try { localStorage.removeItem(KIOSK_KEY); } catch { /* ignore */ } },
};

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

type RequestOptions = {
  /** Endpoint công khai: không gửi Bearer/kiosk cũ và không redirect khi nhận 401. */
  anonymous?: boolean;
  signal?: AbortSignal;
  cache?: RequestCache;
};

function isPublicNoAuthRoute() {
  const path = location.pathname.replace(/\/+$/, "") || "/";
  const hashPath = location.hash.replace(/^#/, "").split(/[?#]/)[0].replace(/\/+$/, "") || "/";
  return path === "/tai-apk" || hashPath === "/tai-apk";
}

function handleUnauthorized(): never {
  tokenStore.clear();
  if (!isPublicNoAuthRoute() && !location.pathname.startsWith("/login") && location.hash !== "#/login") redirectToLogin();
  throw new ApiError(401, "Phiên đăng nhập đã hết hạn.");
}

async function request<T>(method: string, path: string, body?: unknown, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = {};
  if (!options.anonymous) {
    const token = tokenStore.get();
    if (token) headers["Authorization"] = `Bearer ${token}`;
    const kioskKey = kioskKeyStore.get();
    if (kioskKey) headers["X-Kiosk-Key"] = kioskKey;
  }
  if (body !== undefined) headers["Content-Type"] = "application/json";

  const res = await fetch(appUrl(path), {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    cache: options.cache,
    credentials: options.anonymous ? "omit" : "same-origin",
    signal: options.signal,
  });

  if (res.status === 401) {
    if (options.anonymous) {
      const data = await res.clone().json().catch(() => null);
      throw new ApiError(401, (data as { message?: string } | null)?.message || "Yêu cầu công khai không được chấp nhận.");
    }
    // Kiosk chưa được cấp quyền → KHÔNG đá về trang đăng nhập; để màn kiosk hiện ô nhập mã.
    const data = await res.clone().json().catch(() => null);
    if (data && (data as { code?: string }).code === "kiosk_key_required")
      throw new ApiError(401, (data as { message?: string }).message || "Cần mã kiosk.");
    handleUnauthorized();
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
    handleUnauthorized();
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
    handleUnauthorized();
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

async function requestForm<T>(method: string, path: string, form: FormData): Promise<T> {
  const headers: Record<string, string> = {};
  const token = tokenStore.get();
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const res = await fetch(appUrl(path), { method, headers, body: form });

  if (res.status === 401) {
    handleUnauthorized();
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
  postPublic: <T>(p: string, body?: unknown, signal?: AbortSignal) =>
    request<T>("POST", p, body ?? {}, { anonymous: true, cache: "no-store", signal }),
  put: <T>(p: string, body?: unknown) => request<T>("PUT", p, body ?? {}),
  del: <T>(p: string) => request<T>("DELETE", p),
  getBlob: (p: string) => requestBlob(p),
  postBlob: <T>(p: string, blob: Blob) => postBlob<T>(p, blob),
  postForm: <T>(p: string, form: FormData) => requestForm<T>("POST", p, form),
};
