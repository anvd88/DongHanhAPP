import { type RefObject } from "react";
import { Check, Search, SlidersHorizontal } from "lucide-react";
import { GlassPanel } from "../../components/glass/GlassPanel";
import { GlassCapsule } from "../../components/glass/GlassCapsule";
import { LiquidTabs, type LiquidTab } from "../../components/glass/LiquidTabs";
import { Button } from "../../shadcn/button";
import { Popover, PopoverContent, PopoverTrigger } from "../../shadcn/popover";
import { Tooltip, TooltipContent, TooltipTrigger } from "../../shadcn/tooltip";
import { cn } from "../../lib/cn";

export const TABS: LiquidTab[] = [
  { key: "all", label: "Tất cả" },
  { key: "xuat", label: "Xuất gia công" },
  { key: "nhap", label: "Nhập gia công" },
  { key: "dangxuly", label: "Đang xử lý" },
];

export const STATUS_OPTIONS = ["all", "Đang xử lý", "Hoàn thành", "Chờ đối tác", "Hủy"] as const;

export function Toolbar({
  filter,
  onFilter,
  search,
  onSearch,
  status,
  onStatus,
  searchRef,
}: {
  filter: string;
  onFilter: (key: string) => void;
  search: string;
  onSearch: (value: string) => void;
  status: string;
  onStatus: (value: string) => void;
  searchRef: RefObject<HTMLInputElement | null>;
}) {
  const statusActive = status !== "all";

  return (
    <GlassPanel className="flex flex-wrap items-center gap-3 rounded-[20px] p-3">
      <LiquidTabs tabs={TABS} value={filter} onChange={onFilter} />

      <div className="ml-auto flex flex-1 items-center justify-end gap-2.5">
        <GlassCapsule className="gc-search min-w-[220px] max-w-[400px] flex-1 px-4">
          <Search className="mr-2.5 h-[18px] w-[18px] shrink-0 text-[var(--gc-text-muted)]" aria-hidden="true" />
          <input
            ref={searchRef}
            value={search}
            onChange={(e) => onSearch(e.target.value)}
            placeholder="Tìm mã phiếu, đối tác..."
            aria-label="Tìm mã phiếu, đối tác"
          />
          <kbd className="ml-2 hidden rounded-md border border-[var(--gc-border)] bg-white/30 px-1.5 py-0.5 text-[0.68rem] font-bold text-[var(--gc-text-muted)] sm:block dark:bg-white/5">
            Ctrl K
          </kbd>
        </GlassCapsule>

        <Popover>
          <Tooltip>
            <TooltipTrigger asChild>
              <PopoverTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label="Lọc theo trạng thái"
                  className={cn("relative", statusActive && "text-[var(--gc-accent)]")}
                >
                  <SlidersHorizontal className="h-[18px] w-[18px]" />
                  {statusActive && (
                    <span className="absolute right-2 top-2 h-1.5 w-1.5 rounded-full bg-[var(--gc-accent)]" />
                  )}
                </Button>
              </PopoverTrigger>
            </TooltipTrigger>
            <TooltipContent>Lọc theo trạng thái</TooltipContent>
          </Tooltip>

          <PopoverContent>
            <div className="mb-2 text-[0.72rem] font-bold uppercase tracking-wide text-[var(--gc-text-muted)]">
              Trạng thái
            </div>
            <div className="flex flex-col gap-1">
              {STATUS_OPTIONS.map((opt) => {
                const active = status === opt;
                return (
                  <button
                    key={opt}
                    type="button"
                    onClick={() => onStatus(opt)}
                    className={cn(
                      "flex items-center justify-between rounded-lg px-2.5 py-2 text-sm font-semibold transition-colors",
                      active
                        ? "bg-[rgba(var(--gc-accent-rgb),0.14)] text-[var(--gc-text)]"
                        : "text-[var(--gc-text-soft)] hover:bg-[rgba(var(--gc-accent-rgb),0.08)]",
                    )}
                  >
                    {opt === "all" ? "Tất cả trạng thái" : opt}
                    {active && <Check className="h-4 w-4 text-[var(--gc-accent)]" />}
                  </button>
                );
              })}
            </div>
          </PopoverContent>
        </Popover>
      </div>
    </GlassPanel>
  );
}
