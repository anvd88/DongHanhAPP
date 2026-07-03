import { forwardRef, type ComponentPropsWithoutRef, type ElementRef } from "react";
import * as TooltipPrimitive from "@radix-ui/react-tooltip";
import { cn } from "../lib/cn";

export const TooltipProvider = TooltipPrimitive.Provider;
export const Tooltip = TooltipPrimitive.Root;
export const TooltipTrigger = TooltipPrimitive.Trigger;

export const TooltipContent = forwardRef<
  ElementRef<typeof TooltipPrimitive.Content>,
  ComponentPropsWithoutRef<typeof TooltipPrimitive.Content>
>(({ className, sideOffset = 6, ...rest }, ref) => (
  <TooltipPrimitive.Portal>
    <TooltipPrimitive.Content
      ref={ref}
      sideOffset={sideOffset}
      className={cn(
        "gc-root z-[70] gc-pop-content rounded-lg border border-[var(--gc-border)] bg-[var(--gc-surface-strong)] px-2.5 py-1.5 text-xs font-semibold text-[var(--gc-text)] shadow-[0_12px_28px_-14px_rgba(15,23,42,0.5)] backdrop-blur",
        className,
      )}
      {...rest}
    />
  </TooltipPrimitive.Portal>
));
TooltipContent.displayName = "TooltipContent";
