import { useMemo } from "react";
import { ArrowDownToLine, ArrowUpFromLine, Coins, Layers } from "lucide-react";
import { moneyVnd, num } from "../../lib/format";
import type { GiaCongListItem } from "../../lib/types";
import { StatCard } from "./StatCard";

export function StatsRow({ rows }: { rows: GiaCongListItem[] }) {
  const stats = useMemo(() => {
    const phieuXuat = rows.filter((row) => row.loaiPhieu.toLowerCase().includes("xuất")).length;
    const phieuNhap = rows.filter((row) => row.loaiPhieu.toLowerCase().includes("nhập")).length;
    const soLuongXuat = rows.reduce((sum, row) => sum + (row.soLuongXuat || 0), 0);
    const soLuongNhap = rows.reduce((sum, row) => sum + (row.soLuongNhap || 0), 0);
    const tienGiaCong = rows.reduce((sum, row) => sum + (row.tienGiaCongPhaiTra || 0), 0);
    return { phieuXuat, phieuNhap, soLuongXuat, soLuongNhap, tienGiaCong };
  }, [rows]);

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <StatCard
        index={0}
        icon={ArrowUpFromLine}
        label="Xuất gia công"
        value={num(stats.soLuongXuat)}
        sub={`${num(stats.phieuXuat)} phiếu xuất`}
        tone="31, 107, 255"
      />
      <StatCard
        index={1}
        icon={ArrowDownToLine}
        label="Nhập gia công"
        value={num(stats.soLuongNhap)}
        sub={`${num(stats.phieuNhap)} phiếu nhập`}
        tone="0, 184, 148"
      />
      <StatCard
        index={2}
        icon={Coins}
        label="Phí gia công"
        value={moneyVnd(stats.tienGiaCong)}
        sub="Chỉ tính khi nhập về"
        tone="217, 119, 6"
      />
      <StatCard
        index={3}
        icon={Layers}
        label="Tổng phiếu"
        value={num(rows.length)}
        sub="Xuất và nhập"
        tone="124, 70, 255"
      />
    </div>
  );
}
