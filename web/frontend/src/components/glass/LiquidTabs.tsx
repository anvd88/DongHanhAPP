import { useId, useState } from "react";
import { LayoutGroup, MotionConfig, motion } from "motion/react";
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
  const layoutGroupId = useId();
  const [previewKey, setPreviewKey] = useState<string | null>(null);
  const indicatorKey = previewKey ?? value;

  return (
    <MotionConfig reducedMotion="never">
      <LayoutGroup id={layoutGroupId}>
        <div
          role="tablist"
          aria-orientation="horizontal"
          className={cn("gc-tabs gc-capsule", className)}
          onPointerLeave={() => setPreviewKey(null)}
          onBlur={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget)) setPreviewKey(null);
          }}
        >
          {tabs.map((tab) => {
            const active = tab.key === value;
            const showsIndicator = tab.key === indicatorKey;
            return (
              <button
                key={tab.key}
                type="button"
                role="tab"
                aria-selected={active}
                data-active={active}
                className="gc-tab"
                onPointerEnter={(event) => {
                  if (event.pointerType !== "touch") setPreviewKey(tab.key);
                }}
                onFocus={() => setPreviewKey(tab.key)}
                onClick={() => onChange(tab.key)}
              >
                {showsIndicator && (
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
      </LayoutGroup>
    </MotionConfig>
  );
}
