import type { CSSProperties, ReactNode } from "react";

type Tone = "mint" | "blue";

const TONES: Record<Tone, { rgb: string; text: string; dark: string }> = {
  mint: { rgb: "0, 184, 148", text: "4, 120, 87", dark: "110, 231, 183" },
  blue: { rgb: "31, 107, 255", text: "29, 78, 216", dark: "147, 197, 253" },
};

function toneStyle(tone: Tone): CSSProperties {
  const t = TONES[tone];
  return {
    "--gc-badge": t.rgb,
    "--gc-badge-text": t.text,
    "--gc-badge-dark": t.dark,
  } as CSSProperties;
}

function GlassBadge({ tone, children }: { tone: Tone; children: ReactNode }) {
  return (
    <span className="gc-badge" style={toneStyle(tone)}>
      {children}
    </span>
  );
}

export function LoaiBadge({ loai }: { loai: string }) {
  const tone: Tone = loai.toLowerCase().includes("nhập") ? "mint" : "blue";
  return <GlassBadge tone={tone}>{loai}</GlassBadge>;
}
