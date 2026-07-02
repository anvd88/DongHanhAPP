export const IS_HR_APK = import.meta.env.VITE_APP_TARGET === "hr-apk";

export const ANDROID_VERSION_CODE = Number(import.meta.env.VITE_ANDROID_VERSION_CODE ?? "1");
export const ANDROID_VERSION_NAME = import.meta.env.VITE_ANDROID_VERSION_NAME ?? "1.0";

export const DEFAULT_AUTH_PATH = IS_HR_APK ? "/nhan-su" : "/dashboard";

export const HR_APK_NAV_KEYS = new Set(["nhan-su-portal", "chamcong", "hoso", "bangcong", "dontu", "pheduyet", "quanly-nhansu"]);

const rawApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim() ?? "";

export const API_BASE_URL = rawApiBaseUrl.replace(/\/+$/, "");

export function appUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) return path;
  if (!API_BASE_URL) return path;
  return `${API_BASE_URL}${path.startsWith("/") ? path : `/${path}`}`;
}

export function redirectToLogin() {
  if (IS_HR_APK) {
    window.location.hash = "#/login";
    return;
  }
  window.location.href = "/login";
}
