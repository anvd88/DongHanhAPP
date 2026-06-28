/**
 * Tích xanh kiểu Facebook: huy hiệu tròn xanh với dấu tick trắng (cánh hoa răng cưa).
 * Dùng cạnh tên người dùng đã được xác minh (Admin hoặc được admin cấp).
 */
export function VerifiedBadge({ size = 16, title = "Đã xác minh" }: { size?: number; title?: string }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      className="inline-block shrink-0 align-text-bottom"
      aria-label={title}
      role="img"
    >
      <title>{title}</title>
      <path
        fill="#1d9bf0"
        d="M12 1.5l2.39 1.74 2.95-.02 1.0 2.78 2.4 1.72-.92 2.8.92 2.8-2.4 1.72-1.0 2.78-2.95-.02L12 22.5l-2.39-1.74-2.95.02-1.0-2.78-2.4-1.72.92-2.8-.92-2.8 2.4-1.72 1.0-2.78 2.95.02L12 1.5z"
      />
      <path
        fill="none"
        stroke="#fff"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M8 12.2l2.6 2.6L16 9.4"
      />
    </svg>
  );
}
