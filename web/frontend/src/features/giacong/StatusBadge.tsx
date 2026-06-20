import type { CSSProperties, ReactNode } from "react";

type Tone = "mint" | "blue" | "amber" | "rose" | "violet" | "slate";

const TONES: Record<Tone, { rgb: string; text: string; dark: string }> = {
  mint: { rgb: "0, 184, 148", text: "4, 120, 87", dark: "110, 231, 183" },
  blue: { rgb: "31, 107, 255", text: "29, 78, 216", dark: "147, 197, 253" },
  amber: { rgb: "217, 119, 6", text: "180, 83, 9", dark: "252, 211, 77" },
  rose: { rgb: "244, 63, 94", text: "190, 24, 60", dark: "253, 164, 175" },
  violet: { rgb: "124, 70, 255", text: "109, 40, 217", dark: "196, 181, 253" },
  slate: { rgb: "100, 116, 139", text: "71, 85, 105", dark: "203, 213, 225" },
};

function toneStyle(tone: Tone): CSSProperties {
  const t = TONES[tone];
  return {
    "--gc-badge": t.rgb,
    "--gc-badge-text": t.text,
    "--gc-badge-dark": t.dark,
  } as CSSProperties;
}

export function GlassBadge({ tone, dot, children }: { tone: Tone; dot?: boolean; children: ReactNode }) {
  return (
    <span className="gc-badge" style={toneStyle(tone)}>
      {dot && <span className="gc-dot" />}
      {children}
    </span>
  );
}

const STATUS_TONE: Record<string, Tone> = {
  "Hoàn thành": "mint",
  "Đang xử lý": "blue",
  "Chờ đối tác": "amber",
  "Hủy": "rose",
};

export function StatusBadge({ status }: { status: string }) {
  return (
    <GlassBadge tone={STATUS_TONE[status] ?? "slate"} dot>
      {status}
    </GlassBadge>
  );
}

export function LoaiBadge({ loai }: { loai: string }) {
  const isXuat = loai.toLowerCase().includes("xuất");
  return <GlassBadge tone={isXuat ? "blue" : "violet"}>{loai}</GlassBadge>;
}
