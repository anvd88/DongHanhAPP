/**
 * Chế độ hiệu năng ("nhẹ" cho máy yếu).
 *
 * Khi bật (class `perf-lite` trên <html>), CSS sẽ:
 *  - Bỏ toàn bộ `backdrop-filter: blur()` (thứ nặng nhất với GPU/CPU yếu) và chuyển
 *    nền kính sang màu đục để vẫn đọc rõ.
 *  - Ẩn các quả cầu nền động + tắt các animation trang trí lặp vô hạn (glint/sheen).
 *
 * Mặc định tự phát hiện máy yếu (ít RAM/nhân CPU). Người dùng có thể ép bật/tắt trong
 * trang Hệ thống; lựa chọn được lưu theo từng thiết bị (localStorage).
 */

const KEY = "km_perf_mode";
type Stored = "full" | "lite";

const listeners = new Set<() => void>();

function readStored(): Stored | null {
  const v = localStorage.getItem(KEY);
  return v === "full" || v === "lite" ? v : null;
}

/** Máy yếu: RAM ≤ 4GB hoặc CPU ≤ 2 nhân (khi trình duyệt cung cấp thông tin). */
export function detectLowEnd(): boolean {
  const mem = (navigator as unknown as { deviceMemory?: number }).deviceMemory;
  const cores = navigator.hardwareConcurrency;
  if (typeof mem === "number" && mem > 0 && mem <= 4) return true;
  if (typeof cores === "number" && cores > 0 && cores <= 2) return true;
  return false;
}

/** Chế độ đang áp dụng thực tế (ưu tiên lựa chọn thủ công, nếu chưa đặt thì tự phát hiện). */
export function effectivePerfMode(): Stored {
  return readStored() ?? (detectLowEnd() ? "lite" : "full");
}

export function isLiteMode(): boolean {
  return effectivePerfMode() === "lite";
}

/** Người dùng có tự chọn (true) hay đang để hệ thống tự phát hiện (false). */
export function isPerfModeExplicit(): boolean {
  return readStored() !== null;
}

export function applyPerfMode(): void {
  document.documentElement.classList.toggle("perf-lite", isLiteMode());
}

export function setLiteMode(on: boolean): void {
  localStorage.setItem(KEY, on ? "lite" : "full");
  applyPerfMode();
  listeners.forEach((fn) => fn());
}

export function subscribePerfMode(fn: () => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}
