import type { LucideIcon } from "lucide-react";
import { useGlow } from "./Glass";

export function StatCard({
  label,
  value,
  sub,
  trend,
  icon: Icon,
  color,
  tone = "blue",
}: {
  label: string;
  value: string;
  sub?: string;
  trend?: string;
  icon: LucideIcon;
  color?: string;
  tone?: "blue" | "mint" | "amber" | "violet";
}) {
  const { ref, onMouseMove } = useGlow();

  return (
    <div ref={ref} onMouseMove={onMouseMove} className={`km-stat-card km-stat-card-${tone}`}>
      <div className="km-stat-icon" style={color ? { color } : undefined}>
        <Icon className="h-5 w-5" />
      </div>
      <div className="min-w-0">
        <p className="km-stat-label">{label}</p>
        <p className="km-stat-value">{value}</p>
        <div className="mt-1.5 flex items-center gap-2">
          {sub && <p className="km-stat-sub">{sub}</p>}
          {trend && <span className="km-trend-badge">{trend}</span>}
        </div>
      </div>
    </div>
  );
}
