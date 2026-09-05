/**
 * Bỏ dấu tiếng Việt phục vụ tìm kiếm. Backend cũng tìm không dấu ở /api/directory nên hai phía
 * dùng cùng một luật chuẩn hoá. Ký tự Đ/đ phải xử lý riêng vì NFD không tách được nét ngang.
 */
export function deaccent(value: string) {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .toLowerCase()
}

/** So khớp mềm: bỏ dấu, bỏ phân biệt hoa thường, cho phép nhập thiếu. */
export function matches(haystack: string, needle: string) {
  const query = deaccent(needle.trim())
  if (!query) return true
  return deaccent(haystack).includes(query)
}
