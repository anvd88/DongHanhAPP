type FaceScanIconProps = {
  className?: string;
};

/**
 * Icon nhận diện khuôn mặt — khung quét 4 góc bo tròn + mắt/mũi/miệng,
 * đối xứng quanh trục x=50. Dùng currentColor nên màu lấy từ phần tử cha.
 */
export function FaceScanIcon({ className }: FaceScanIconProps) {
  return (
    <svg
      className={className}
      viewBox="20 20 60 60"
      fill="none"
      stroke="currentColor"
      strokeWidth={4.4}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <path d="M27 42 V33 A7 7 0 0 1 34 26 H44" />
      <path d="M56 26 H66 A7 7 0 0 1 73 33 V42" />
      <path d="M73 58 V67 A7 7 0 0 1 66 74 H56" />
      <path d="M44 74 H34 A7 7 0 0 1 27 67 V58" />
      <path d="M41 40.5 V46.5" />
      <path d="M59 40.5 V46.5" />
      <path d="M50 41 V54.5 A5 5 0 0 1 45 59.5 H43" />
      <path d="M40 62 C44.5 67.6 55.5 67.6 60 62" />
    </svg>
  );
}
