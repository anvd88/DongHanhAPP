export const IS_HR_APK = import.meta.env.VITE_APP_TARGET === "hr-apk";

export const DEFAULT_AUTH_PATH = IS_HR_APK ? "/hoso" : "/dashboard";

export const HR_APK_NAV_KEYS = new Set(["hoso", "bangcong", "dontu", "pheduyet"]);

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
