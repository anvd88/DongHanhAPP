import { appUrl, redirectToLogin } from "./appConfig";

const LEGACY_TOKEN_KEY = "km_token";
const KIOSK_KEY = "km_kiosk_key";
const CSRF_COOKIE = "km_csrf";

/**
 * PHIÊN ĐĂNG NHẬP NẰM TRONG COOKIE HttpOnly — JavaScript KHÔNG đọc được, và đó chính là mục đích:
 * một lỗ hổng XSS (kể cả trong thư viện phụ thuộc) không lấy được phiên đăng nhập mang đi máy khác.
 * Cookie do trình duyệt tự gửi kèm mọi request cùng origin, nên frontend không phải cầm token nữa.
 *
 * Thứ duy nhất frontend đọc được là cookie CSRF (cố ý không HttpOnly): nó phải được gắn lại vào
 * header X-CSRF-Token ở mọi request ghi dữ liệu. Trang web lạ ép được trình duyệt GỬI cookie của ta
 * nhưng không ĐỌC được nó, nên không dựng nổi header khớp.
 */
function readCookie(name: string): string | null {
  const hit = document.cookie
    .split("; ")
    .find((c) => c.startsWith(`${name}=`));
  return hit ? decodeURIComponent(hit.slice(name.length + 1)) : null;
}

export const session = {
  /** Đã đăng nhập hay chưa, theo góc nhìn của client. Cookie phiên không đọc được nên dùng cookie
   *  CSRF làm cờ — hai cookie luôn được đặt và xoá cùng nhau ở phía máy chủ. */
  isSignedIn: () => readCookie(CSRF_COOKIE) !== null,
  csrfToken: () => readCookie(CSRF_COOKIE),
  /**
   * Dọn dấu vết phiên phía client. KHÔNG xoá được cookie HttpOnly từ đây (đúng như thiết kế) —
   * việc đó do POST /api/auth/logout hoặc phản hồi 401 của máy chủ làm.
   */
  clearLocal: () => {
    // Token cũ còn sót trong localStorage từ trước khi chuyển sang cookie: xoá đi cho sạch, nó
    // không còn được dùng để xác thực nữa và để lại chỉ tổ rủi ro.
    try { localStorage.removeItem(LEGACY_TOKEN_KEY); } catch { /* bỏ qua */ }
  },
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
  session.clearLocal();
  // Màn hình đăng nhập nay CHỈ nằm ở "/". Đang ở đó rồi thì đừng hard-redirect nữa kẻo nạp lại
  // trang thành vòng lặp.
  const path = location.pathname.replace(/\/+$/, "") || "/";
  if (!isPublicNoAuthRoute() && path !== "/") redirectToLogin();
  throw new ApiError(401, "Phiên đăng nhập đã hết hạn.");
}

/**
 * Header chung cho mọi lời gọi. Không còn Authorization: phiên đi bằng cookie HttpOnly mà trình
 * duyệt tự gửi. Đổi lại phải tự gắn X-CSRF-Token cho request GHI dữ liệu — xem lib/api.ts phần đầu.
 */
function authHeaders(method: string, options: RequestOptions = {}): Record<string, string> {
  const headers: Record<string, string> = {};

  // Khóa kiosk là "danh tính thiết bị" — lời gọi ẩn danh cố ý không mang nó theo.
  if (!options.anonymous) {
    const kioskKey = kioskKeyStore.get();
    if (kioskKey) headers["X-Kiosk-Key"] = kioskKey;
  }

  // Header CSRF thì gắn cho MỌI request ghi, kể cả lời gọi ẩn danh: trình duyệt vẫn tự đính cookie
  // phiên (nếu có) vào request ẩn danh, nên máy chủ vẫn coi đó là request kiểu cookie và vẫn đòi
  // header. Bỏ qua ở đây thì đăng nhập QR sẽ 403 với người còn phiên cũ trong trình duyệt.
  if (!SAFE_METHODS.includes(method.toUpperCase())) {
    const csrf = session.csrfToken();
    if (csrf) headers["X-CSRF-Token"] = csrf;
  }
  return headers;
}

const SAFE_METHODS = ["GET", "HEAD", "OPTIONS"];

/**
 * "same-origin" chứ không "include": cookie phiên chỉ được gửi khi frontend và API cùng origin —
 * đúng cách hệ thống được triển khai (API phục vụ luôn wwwroot). Nếu sau này tách origin thì phải
 * đổi sang "include" ĐỒNG THỜI bật AllowCredentials + siết danh sách origin ở CORS, không làm nửa vời.
 */
async function request<T>(method: string, path: string, body?: unknown, options: RequestOptions = {}): Promise<T> {
  const headers = authHeaders(method, options);
  if (body !== undefined) headers["Content-Type"] = "application/json";

  const res = await fetch(appUrl(path), {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    cache: options.cache,
    // Kể cả lời gọi "anonymous" cũng phải để same-origin: luồng đăng nhập QR poll là ẩn danh nhưng
    // chính nó là chỗ máy chủ ĐẶT cookie phiên — dùng "omit" thì trình duyệt vứt Set-Cookie đi và
    // đăng nhập QR im lặng không có tác dụng.
    credentials: "same-origin",
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
  const res = await fetch(appUrl(path), {
    method: "GET",
    headers: authHeaders("GET"),
    credentials: "same-origin",
  });

  if (res.status === 401) {
    handleUnauthorized();
  }

  if (!res.ok) throw new ApiError(res.status, `Lỗi ${res.status}`);
  return res.blob();
}

/** Tải LÊN một blob nhị phân (octet-stream) kèm Bearer token; trả JSON kết quả. */
async function postBlob<T>(path: string, blob: Blob): Promise<T> {
  const headers = { ...authHeaders("POST"), "Content-Type": "application/octet-stream" };
  const res = await fetch(appUrl(path), { method: "POST", headers, body: blob, credentials: "same-origin" });

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
  const res = await fetch(appUrl(path), {
    method,
    headers: authHeaders(method),
    body: form,
    credentials: "same-origin",
  });

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
