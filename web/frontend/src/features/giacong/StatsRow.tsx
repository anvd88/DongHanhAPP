import { useMemo } from "react";
import { CheckCircle2, Coins, Hammer, Layers } from "lucide-react";
import { money, moneyVnd } from "../../lib/format";
import type { GiaCongListItem } from "../../lib/types";
import { StatCard } from "./StatCard";

export function StatsRow({ rows }: { rows: GiaCongListItem[] }) {
  const stats = useMemo(() => {
    const total = rows.length;
    const dangXuLy = rows.filter((r) => r.trangThai === "Đang xử lý").length;
    const hoanThanh = rows.filter((r) => r.trangThai === "Hoàn thành").length;
    const tongGiaTri = rows.reduce((sum, r) => sum + (r.tongGiaTri || 0), 0);
    return { total, dangXuLy, hoanThanh, tongGiaTri };
  }, [rows]);

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <StatCard
        index={0}
        icon={Layers}
        label="Tổng phiếu gia công"
        value={money(stats.total)}
        sub="Toàn bộ phiếu"
        tone="31, 107, 255"
      />
      <StatCard
        index={1}
        icon={Hammer}
        label="Đang xử lý"
        value={money(stats.dangXuLy)}
        sub="Phiếu đang chạy"
        tone="217, 119, 6"
      />
      <StatCard
        index={2}
        icon={CheckCircle2}
        label="Hoàn thành"
        value={money(stats.hoanThanh)}
        sub="Đã nghiệm thu"
        tone="0, 184, 148"
      />
      <StatCard
        index={3}
        icon={Coins}
        label="Tổng giá trị"
        value={moneyVnd(stats.tongGiaTri)}
        sub="Cộng dồn các phiếu"
        tone="124, 70, 255"
      />
    </div>
  );
}
