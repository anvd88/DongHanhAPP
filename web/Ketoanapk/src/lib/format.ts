export const money = (n: number) =>
  new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 0 }).format(Math.round(n || 0));

export const moneyVnd = (n: number) => money(n) + " ₫";

export const num = (n: number, d = 2) =>
  new Intl.NumberFormat("vi-VN", { maximumFractionDigits: d }).format(n || 0);

/** Nhận "yyyy-MM-dd" (date) hoặc ISO datetime, trả "dd/MM/yyyy". */
export const date = (s?: string | null) => {
  if (!s) return "";
  const d = new Date(s.length <= 10 ? s + "T00:00:00" : s);
  if (isNaN(d.getTime())) return s;
  return d.toLocaleDateString("vi-VN");
};

export const dateTime = (s?: string | null) => {
  if (!s) return "";
  const d = new Date(s);
  if (isNaN(d.getTime())) return s;
  return d.toLocaleString("vi-VN");
};

/** Lấy chữ cái đầu cho avatar. */
export const initials = (name: string) =>
  (name || "?").trim().split(/\s+/).slice(-2).map((w) => w[0]).join("").toUpperCase();

/** Dung lượng tệp gọn: 0–999 B, KB, MB, GB. */
export const formatBytes = (bytes?: number | null) => {
  const n = Math.max(0, bytes ?? 0);
  if (n < 1024) return `${n} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let v = n / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toFixed(v >= 100 || v % 1 === 0 ? 0 : 1)} ${units[i]}`;
};
