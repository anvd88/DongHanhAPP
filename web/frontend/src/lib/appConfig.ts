// ĐÃ GỠ cờ IS_HR_APK (cùng HR_APK_NAV_KEYS): nó chỉ bật khi build với VITE_APP_TARGET=hr-apk — chế độ
// build riêng cho vỏ APK Capacitor, đã xoá hẳn 2026-07-19. Cờ luôn false nghĩa là mọi nhánh
// `IS_HR_APK ? ... : ...` chỉ còn nhánh sau, nên đã rút gọn tại chỗ ở App/Layout/Sidebar/Header/Login.
export const DEFAULT_AUTH_PATH = "/dashboard";

// Các đường dẫn thuộc module NHÂN SỰ trên web. Không liên quan gì tới APK: dùng để ẩn bớt phần "web
// chính" (nhắc nước/mắt, toast phản hồi) cho đỡ nhiễu khi đang làm việc trong khu nhân sự.
const HR_MODULE_PATHS = [
  "/nhan-su",
  "/nhansu",
  "/hoso",
  "/dontu",
  "/pheduyet",
  "/bangcong",
  "/quanly-nhansu",
  "/quanly-dontu",
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
  // Màn hình đăng nhập nay nằm ở TRANG GỐC "/", không còn ở "/login".
  window.location.href = "/";
}
