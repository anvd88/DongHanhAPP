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

export function DiamondBadge({ size = 16, title = "Hội viên kim cương" }: { size?: number; title?: string }) {
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
      <path fill="#7dd3fc" d="M7.2 3h9.6l4.2 5.2L12 21 3 8.2 7.2 3z" />
      <path fill="#e0f7ff" d="M7.8 4.8h8.4l2.4 3H5.4l2.4-3z" />
      <path fill="#0891b2" d="M5.3 9.2h13.4L12 18.8 5.3 9.2z" />
      <path
        fill="none"
        stroke="#fff"
        strokeLinecap="round"
        strokeLinejoin="round"
        strokeWidth="1.15"
        d="M7.8 4.8 12 18.8l4.2-14M5.4 7.8h13.2"
        opacity="0.85"
      />
    </svg>
  );
}

export function DiamondLabel({ title = "Hội viên kim cương" }: { title?: string }) {
  return (
    <span
      title={title}
      className="inline-flex shrink-0 items-center rounded-[4px] border border-[#0e7490] bg-[#0891b2] px-2 py-0.5 text-[0.66rem] font-extrabold uppercase leading-none text-white shadow-none dark:border-cyan-300/60 dark:bg-[#155e75] dark:text-cyan-50"
    >
      Kim cương
    </span>
  );
}
