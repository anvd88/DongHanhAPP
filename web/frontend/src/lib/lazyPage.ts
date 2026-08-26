import { lazy, type ComponentType } from "react";

/**
 * TẢI TRANG LAZY AN TOÀN — vá lỗi "đăng nhập xong bị trắng màn hình, reload mới dùng được".
 *
 * Vì sao có lỗi: mỗi lần `vite build` chạy lại, các chunk JS đổi tên (hash mới) và bản cũ bị xoá
 * khỏi wwwroot. Tab đang mở vẫn giữ index.html CŨ, nên khi điều hướng sang một trang lazy (đúng
 * lúc vừa đăng nhập xong) trình duyệt đi tìm chunk cũ → 404 → `import()` ném lỗi. React 19 không
 * có ranh giới lỗi nào bắt được thì GỠ SẠCH cây component ⇒ màn hình trắng trơn. Bấm F5 tải
 * index.html mới nên lại chạy bình thường — đúng như hiện tượng người dùng gặp.
 *
 * Cách vá: thử tải lại một lần (phòng mạng chập chờn); vẫn hỏng thì tự F5 đúng MỘT lần để lấy
 * bản build mới, thay vì để người dùng nhìn màn hình trắng và tự đoán phải reload.
 */

const RELOAD_MARK = "km:chunk-reload-at";
/** Trong khoảng này không tự tải lại lần nữa — tránh vòng lặp F5 nếu lỗi không phải do build mới. */
const RELOAD_COOLDOWN_MS = 30_000;

export function isChunkLoadError(error: unknown) {
  const text = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  return /loading chunk|loading css chunk|dynamically imported module|importing a module script failed|failed to fetch dynamically/i
    .test(text);
}

/** Tải lại trang để lấy bản build mới. Trả về false nếu vừa tải lại xong (không lặp vô hạn). */
export function reloadForNewBuild() {
  try {
    const last = Number(sessionStorage.getItem(RELOAD_MARK) ?? 0);
    if (Number.isFinite(last) && Date.now() - last < RELOAD_COOLDOWN_MS) return false;
    sessionStorage.setItem(RELOAD_MARK, String(Date.now()));
  } catch {
    /* Trình duyệt chặn storage: vẫn cho tải lại một lần, mất mốc thời gian thôi. */
  }
  window.location.reload();
  return true;
}

/* eslint-disable-next-line @typescript-eslint/no-explicit-any -- chữ ký của React.lazy vốn nhận mọi loại props. */
export function lazyPage<T extends ComponentType<any>>(load: () => Promise<{ default: T }>) {
  return lazy(async () => {
    try {
      return await load();
    } catch (error) {
      if (!isChunkLoadError(error)) throw error;
      try {
        return await load();
      } catch {
        /* Vẫn hỏng ⇒ gần như chắc chắn là chunk cũ đã bị xoá khỏi máy chủ. */
      }
      // Treo Suspense cho tới khi trang thật sự tải lại: hiện skeleton còn hơn nháy một màn hình lỗi.
      if (reloadForNewBuild()) await new Promise<never>(() => {});
      throw error;
    }
  });
}
