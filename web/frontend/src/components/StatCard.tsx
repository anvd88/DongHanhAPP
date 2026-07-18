import { motion } from "motion/react";
import type { LucideIcon } from "lucide-react";
import { useGlow } from "./Glass";
import { CountUp } from "./CountUp";

const iosSpring = { type: "spring", stiffness: 330, damping: 25, mass: 0.82 } as const;

export function StatCard({
  label,
  value,
  sub,
  trend,
  icon: Icon,
  color,
  tone = "blue",
  loading,
}: {
  label: string;
  value: string;
  sub?: string;
  trend?: string;
  icon: LucideIcon;
  color?: string;
  tone?: "blue" | "mint" | "amber" | "violet";
  loading?: boolean;
}) {
  const { ref, onMouseMove } = useGlow();

  return (
    <motion.div
      ref={ref}
      onMouseMove={onMouseMove}
      whileHover={{ y: -7, scale: 1.018 }}
      whileTap={{ y: -2, scale: 0.985 }}
      transition={iosSpring}
      className={`km-stat-card km-stat-card-${tone}`}
    >
      <div className="km-stat-icon" style={color ? { color } : undefined}>
        <Icon className="h-5 w-5" />
      </div>
      {loading ? (
        <div className="min-w-0 space-y-2">
          <span className="km-skeleton h-3.5" style={{ width: "55%" }} />
          <span className="km-skeleton h-6" style={{ width: "75%" }} />
          <span className="km-skeleton h-3" style={{ width: "45%" }} />
        </div>
      ) : (
        <div className="min-w-0">
          <p className="km-stat-label">{label}</p>
          <p className="km-stat-value"><CountUp text={value} /></p>
          <div className="mt-1.5 flex items-center gap-2">
            {sub && <p className="km-stat-sub">{sub}</p>}
            {trend && <span className="km-trend-badge">{trend}</span>}
          </div>
        </div>
      )}
    </motion.div>
  );
}
