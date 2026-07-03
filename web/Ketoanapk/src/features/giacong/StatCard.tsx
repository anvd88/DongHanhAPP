import { motion } from "motion/react";
import type { CSSProperties } from "react";
import type { LucideIcon } from "lucide-react";
import { GlassCard } from "../../components/glass/GlassCard";

export interface StatCardProps {
  index: number;
  icon: LucideIcon;
  label: string;
  value: string;
  sub?: string;
  trend?: string;
  /** Tông màu icon dạng "r, g, b". */
  tone: string;
}

export function StatCard({ index, icon: Icon, label, value, sub, trend, tone }: StatCardProps) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 22, scale: 0.965, filter: "blur(10px)" }}
      animate={{ opacity: 1, y: 0, scale: 1, filter: "blur(0px)" }}
      transition={{ duration: 0.52, delay: index * 0.09, ease: [0.22, 1, 0.36, 1] }}
    >
      <GlassCard className="flex items-center gap-3.5 p-4">
        <motion.div
          className="gc-icon-box"
          style={{ "--gc-tone": tone } as CSSProperties}
          initial={{ scale: 0.72, opacity: 0, rotate: -5 }}
          animate={{ scale: 1, opacity: 1, rotate: 0 }}
          transition={{ duration: 0.42, delay: index * 0.09 + 0.08, ease: [0.22, 1, 0.36, 1] }}
        >
          <Icon className="h-[22px] w-[22px]" />
        </motion.div>
        <div className="min-w-0">
          <p className="text-[0.78rem] font-bold text-[var(--gc-text-soft)]">{label}</p>
          <p className="mt-1 truncate text-[1.45rem] font-black leading-none text-[var(--gc-text)]">{value}</p>
          <div className="mt-1.5 flex items-center gap-2">
            {sub && <span className="text-[0.74rem] font-semibold text-[var(--gc-text-muted)]">{sub}</span>}
            {trend && (
              <span className="rounded-md bg-emerald-500/15 px-1.5 py-0.5 text-[0.72rem] font-black text-emerald-600 dark:text-emerald-400">
                {trend}
              </span>
            )}
          </div>
        </div>
      </GlassCard>
    </motion.div>
  );
}
