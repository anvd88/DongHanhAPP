export const IS_HR_APK = import.meta.env.VITE_APP_TARGET === "hr-apk";

const configuredAndroidVersionCode = Number(import.meta.env.VITE_ANDROID_VERSION_CODE ?? "1");
export const ANDROID_VERSION_CODE =
  Number.isFinite(configuredAndroidVersionCode) && configuredAndroidVersionCode > 0
    ? configuredAndroidVersionCode
    : 1;
export const ANDROID_VERSION_NAME = import.meta.env.VITE_ANDROID_VERSION_NAME ?? "1.0";

export const DEFAULT_AUTH_PATH = IS_HR_APK ? "/nhan-su" : "/dashboard";

export const HR_APK_NAV_KEYS = new Set(["nhan-su-portal", "chamcong", "hoso", "bangcong", "dontu", "pheduyet", "quanly-nhansu", "hethong"]);

const HR_MODULE_PATHS = [
  "/nhan-su",
  "/nhansu",
  "/hoso",
  "/dontu",
  "/pheduyet",
  "/bangcong",
  "/quanly-nhansu",
  "/phat",
  "/tai-khoan-ngan-hang",
  "/bang-luong",
  "/chamcong",
  "/ql-chamcong",
  "/caidat",
];

export function isHrModulePath(pathname: string): boolean {
  const normalized = (pathname.split(/[?#]/)[0] || "/").replace(/\/+$/, "") || "/";
  return HR_MODULE_PATHS.some((path) => normalized === path || normalized.startsWith(`${path}/`));
}

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
