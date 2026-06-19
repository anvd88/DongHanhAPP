import type { LucideIcon } from "lucide-react";
import { GlassCard } from "./Glass";

export function StatCard({
  label,
  value,
  sub,
  icon: Icon,
  color = "var(--accent)",
}: {
  label: string;
  value: string;
  sub?: string;
  icon: LucideIcon;
  color?: string;
}) {
  return (
    <GlassCard className="fade-in p-5">
      <div className="flex items-start justify-between">
        <div className="min-w-0">
          <p className="text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">{label}</p>
          <p className="mt-2 truncate text-2xl font-bold text-[var(--text)]">{value}</p>
          {sub && <p className="mt-1 text-xs text-[var(--text-secondary)]">{sub}</p>}
        </div>
        <div
          className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl"
          style={{ background: `color-mix(in srgb, ${color} 16%, transparent)`, color }}
        >
          <Icon className="h-5 w-5" />
        </div>
      </div>
    </GlassCard>
  );
}
