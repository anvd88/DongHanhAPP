import { motion } from "motion/react";
import { cn } from "../../lib/cn";

export interface LiquidTab {
  key: string;
  label: string;
}

const tabSpring = { type: "spring", stiffness: 310, damping: 25, mass: 0.85 } as const;

/**
 * Tabs Liquid Glass: thanh chọn là một khối kính trượt liên tục giữa các tab
 * nhờ Motion `layoutId` — không biến mất rồi hiện lại, không đổi kích thước tab.
 */
export function LiquidTabs({
  tabs,
  value,
  onChange,
  className,
}: {
  tabs: LiquidTab[];
  value: string;
  onChange: (key: string) => void;
  className?: string;
}) {
  return (
    <div role="tablist" aria-orientation="horizontal" className={cn("gc-tabs gc-capsule", className)}>
      {tabs.map((tab) => {
        const active = tab.key === value;
        return (
          <button
            key={tab.key}
            type="button"
            role="tab"
            aria-selected={active}
            data-active={active}
            className="gc-tab"
            onClick={() => onChange(tab.key)}
          >
            {active && (
              <motion.span
                layoutId="gc-active-tab"
                className="gc-tab-indicator"
                transition={tabSpring}
                aria-hidden="true"
              >
                <span className="gc-tab-indicator-shine" />
                <span className="gc-tab-indicator-glow" />
              </motion.span>
            )}
            <span className="relative z-[1]">{tab.label}</span>
          </button>
        );
      })}
    </div>
  );
}
