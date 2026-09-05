/** Tuỳ chọn giao diện lưu ở trình duyệt (mật độ bảng, menu thu gọn, màn hình mở gần đây). */

export function readPref<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key)
    return raw == null ? fallback : (JSON.parse(raw) as T)
  } catch {
    return fallback
  }
}

export function writePref(key: string, value: unknown) {
  try {
    localStorage.setItem(key, JSON.stringify(value))
  } catch {
    /* Bộ nhớ đầy hoặc bị chặn: bỏ qua, giao diện vẫn chạy. */
  }
}

const RECENT_KEY = 'km.recent'
const RECENT_MAX = 8

/** Ghi lại màn hình vừa mở để bảng lệnh gợi ý "Mở gần đây". */
export function pushRecent(path: string) {
  const list = readPref<string[]>(RECENT_KEY, []).filter((p) => p !== path)
  list.unshift(path)
  writePref(RECENT_KEY, list.slice(0, RECENT_MAX))
}

export function readRecent(): string[] {
  return readPref<string[]>(RECENT_KEY, [])
}
