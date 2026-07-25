import { type RefObject } from "react";
import { Search } from "lucide-react";
import { GlassPanel } from "../../components/glass/GlassPanel";
import { GlassCapsule } from "../../components/glass/GlassCapsule";
import { LiquidTabs, type LiquidTab } from "../../components/glass/LiquidTabs";

// Không export: chỉ dùng ngay trong file này (giữ export làm Fast Refresh mất tác dụng cho cả file).
const TABS: LiquidTab[] = [
  { key: "all", label: "Tất cả" },
  { key: "xuat", label: "Xuất gia công" },
  { key: "nhap", label: "Nhập gia công" },
];

export function Toolbar({
  filter,
  onFilter,
  search,
  onSearch,
  searchRef,
}: {
  filter: string;
  onFilter: (key: string) => void;
  search: string;
  onSearch: (value: string) => void;
  searchRef: RefObject<HTMLInputElement | null>;
}) {
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
      </div>
    </GlassPanel>
  );
}
