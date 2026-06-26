import { Copy, MoreHorizontal, Pencil, PackageOpen, Trash2 } from "lucide-react";
import { GlassPanel } from "../../components/glass/GlassPanel";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "../../shadcn/dropdown-menu";
import { date, money, num } from "../../lib/format";
import type { GiaCongListItem } from "../../lib/types";
import { LoaiBadge } from "./LoaiBadge";

function SkeletonRows() {
  return (
    <>
      {Array.from({ length: 6 }).map((_, i) => (
        <tr key={i}>
          {Array.from({ length: 8 }).map((_, j) => (
            <td key={j} className="px-3.5 py-3">
              <div className="gc-skeleton h-4" style={{ width: j === 7 ? "35%" : "85%" }} />
            </td>
          ))}
        </tr>
      ))}
    </>
  );
}

export function PhieuTable({
  rows,
  loading,
  error,
  onOpen,
  onEdit,
  onDelete,
  onDuplicate,
}: {
  rows: GiaCongListItem[];
  loading: boolean;
  error: string | null;
  onOpen: (id: number) => void;
  onEdit: (id: number) => void;
  onDelete: (id: number) => void;
  onDuplicate: (id: number) => void;
}) {
  return (
    <GlassPanel strong className="overflow-hidden rounded-[20px]">
      <div className="gc-scroll max-h-[calc(100vh-360px)] min-h-[220px] overflow-auto">
        <table className="gc-table">
          <thead>
            <tr>
              <th>Mã phiếu</th>
              <th>Ngày</th>
              <th>Đối tác</th>
              <th>Loại</th>
              <th className="text-center">Mặt hàng</th>
              <th className="text-right">Số lượng</th>
              <th className="text-right">Phí gia công</th>
              <th className="w-12 text-right" aria-label="Thao tác" />
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <SkeletonRows />
            ) : error ? (
              <tr>
                <td colSpan={8} className="py-14 text-center text-sm font-semibold text-rose-500">
                  {error}
                </td>
              </tr>
            ) : rows.length === 0 ? (
              <tr>
                <td colSpan={8}>
                  <div className="flex flex-col items-center justify-center gap-2 py-16 text-center">
                    <PackageOpen className="h-9 w-9 text-[var(--gc-text-muted)] opacity-70" />
                    <p className="text-sm font-semibold text-[var(--gc-text-soft)]">Chưa có phiếu gia công</p>
                    <p className="text-xs text-[var(--gc-text-muted)]">Tạo phiếu xuất hoặc nhập gia công.</p>
                  </div>
                </td>
              </tr>
            ) : (
              rows.map((row) => {
                const isNhap = row.loaiPhieu.toLowerCase().includes("nhập");
                const soLuong = isNhap ? row.soLuongNhap : row.soLuongXuat;
                return (
                  <tr key={row.id} onClick={() => onOpen(row.id)}>
                    <td className="font-bold text-[var(--gc-text)]">{row.maPhieu}</td>
                    <td className="whitespace-nowrap text-[var(--gc-text-soft)]">{date(row.ngayLap)}</td>
                    <td>{row.doiTac || "-"}</td>
                    <td>
                      <LoaiBadge loai={row.loaiPhieu} />
                    </td>
                    <td className="text-center tabular-nums">{row.soMatHang}</td>
                    <td className="whitespace-nowrap text-right font-bold tabular-nums">{num(soLuong)}</td>
                    <td className="whitespace-nowrap text-right font-semibold tabular-nums">
                      {isNhap ? `${money(row.tienGiaCongPhaiTra)} ₫` : "-"}
                    </td>
                    <td className="text-right" onClick={(e) => e.stopPropagation()}>
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <button className="gc-icon-btn h-8 w-8" aria-label={`Thao tác phiếu ${row.maPhieu}`}>
                            <MoreHorizontal className="h-4 w-4" />
                          </button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent>
                          <DropdownMenuItem onSelect={() => onEdit(row.id)}>
                            <Pencil className="h-4 w-4" /> Sửa phiếu
                          </DropdownMenuItem>
                          <DropdownMenuItem onSelect={() => onDuplicate(row.id)}>
                            <Copy className="h-4 w-4" /> Nhân bản
                          </DropdownMenuItem>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem danger onSelect={() => onDelete(row.id)}>
                            <Trash2 className="h-4 w-4" /> Xóa phiếu
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </GlassPanel>
  );
}
