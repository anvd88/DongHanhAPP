import { useEffect, useState } from "react";
import { Loader2, Plus, Trash2 } from "lucide-react";
import { Modal } from "../../components/Modal";
import { Button, Field, Input } from "../../components/ui";
import { DatePicker } from "../../components/DateField";
import { useAuth } from "../../lib/auth";
import { api } from "../../lib/api";
import { money } from "../../lib/format";
import type { GiaCongDetail, GiaCongLine } from "../../lib/types";

export const LOAI_XUAT = "Xuất gia công";
export const LOAI_NHAP = "Nhập gia công";
export type LoaiGiaCong = typeof LOAI_XUAT | typeof LOAI_NHAP;

const emptyLine = (loaiDong: LoaiGiaCong): GiaCongLine => ({
  id: 0,
  loaiDong,
  maHang: "",
  tenHang: "",
  quyCach: "",
  donViTinh: "Kg",
  soLuong: 1,
  donGiaGiaCong: 0,
  ghiChu: "",
});

const isNhap = (loai: string) => loai.toLowerCase().includes("nhập");
const normalizeLoai = (loai?: string): LoaiGiaCong => (isNhap(loai ?? "") ? LOAI_NHAP : LOAI_XUAT);

function CellInput({
  value,
  onChange,
  type = "text",
  align = "left",
  placeholder,
}: {
  value: string;
  onChange: (v: string) => void;
  type?: string;
  align?: "left" | "right";
  placeholder?: string;
}) {
  return (
    <input
      type={type}
      value={value}
      placeholder={placeholder}
      onChange={(e) => onChange(e.target.value)}
      className={`w-full min-w-[80px] rounded-lg bg-transparent px-2 py-1.5 text-sm outline-none focus:bg-[var(--accent-soft)] ${
        align === "right" ? "text-right" : ""
      }`}
    />
  );
}

export function EditorDialog({
  open,
  id,
  seedId,
  initialLoaiPhieu,
  onClose,
  onSaved,
}: {
  open: boolean;
  id: number | "new";
  seedId?: number;
  initialLoaiPhieu: LoaiGiaCong;
  onClose: () => void;
  onSaved: () => void;
}) {
  const isEdit = id !== "new";
  const { user } = useAuth();
  const loggedInName = user?.fullName?.trim() || user?.username?.trim() || "";
  const [loaiPhieu, setLoaiPhieu] = useState<LoaiGiaCong>(initialLoaiPhieu);
  const [doiTac, setDoiTac] = useState("");
  const [nhanVien, setNhanVien] = useState(loggedInName);
  const [ngayLap, setNgayLap] = useState(() => new Date().toISOString().slice(0, 10));
  const [hanHoanThanh, setHanHoanThanh] = useState("");
  const [ghiChu, setGhiChu] = useState("");
  const [lines, setLines] = useState<GiaCongLine[]>([emptyLine(initialLoaiPhieu)]);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
  // Vào thẳng trạng thái "đang tải" nếu lần mở này có phiếu để tải. Component được dựng lại mỗi lần
  // mở (key ở GiaCongPage) nên biết ngay từ đầu, không phải bật cờ trong effect — và cũng không còn
  // khung hình nào hiện form rỗng trước khi spinner kịp xuất hiện.
  const [loadingDetail, setLoadingDetail] = useState(() => open && (isEdit || seedId !== undefined));

  // CHỈ còn việc TẢI phiếu về — không còn đoạn "dọn sạch các ô" nào ở đây. Trang cha gắn key theo
  // từng lần mở (xem `editorSession` trong GiaCongPage), nên mỗi lần mở là một component mới với giá
  // trị khởi tạo đã đúng cho phiếu mới. Nhờ vậy hết cảnh mở phiếu B mà thoáng thấy dữ liệu phiếu A.
  useEffect(() => {
    if (!open) return;
    const source = isEdit ? (id as number) : seedId;
    if (source === undefined) return; // phiếu mới: giá trị khởi tạo đã đúng, không phải tải gì

    api
      .get<GiaCongDetail>(`/api/giacong/${source}`)
      .then((d) => {
        const nextLoai = normalizeLoai(d.loaiPhieu);
        setLoaiPhieu(nextLoai);
        setDoiTac(d.doiTac);
        setNhanVien(isEdit ? d.nhanVienPhuTrach : loggedInName);
        setNgayLap(isEdit ? d.ngayLap : new Date().toISOString().slice(0, 10));
        setHanHoanThanh(d.hanHoanThanh ?? "");
        setGhiChu(d.ghiChu);
        setLines(
          d.lines.length
            ? d.lines.map((line) => ({
                ...line,
                loaiDong: nextLoai,
                quyCach: line.quyCach ?? "",
                donGiaGiaCong: isNhap(nextLoai) ? line.donGiaGiaCong : 0,
                id: isEdit ? line.id : 0,
              }))
            : [emptyLine(nextLoai)],
        );
      })
      .catch((e) => setError(e instanceof Error ? e.message : "Lỗi tải phiếu."))
      .finally(() => setLoadingDetail(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, id, seedId, initialLoaiPhieu, loggedInName]);

  const isNhapPhieu = isNhap(loaiPhieu);
  const total = isNhapPhieu ? lines.reduce((sum, line) => sum + (line.soLuong || 0) * (line.donGiaGiaCong || 0), 0) : 0;

  const setLine = (i: number, patch: Partial<GiaCongLine>) =>
    setLines((arr) => arr.map((line, j) => (j === i ? { ...line, ...patch } : line)));

  const save = async () => {
    setSaving(true);
    setError("");
    const body = {
      loaiPhieu,
      doiTac,
      nhanVienPhuTrach: isEdit ? nhanVien : loggedInName,
      ngayLap,
      hanHoanThanh: hanHoanThanh || null,
      ghiChu,
      lines: lines.map((line) => ({
        ...line,
        loaiDong: loaiPhieu,
        donGiaGiaCong: isNhapPhieu ? line.donGiaGiaCong : 0,
      })),
    };

    try {
      if (isEdit) await api.put(`/api/giacong/${id}`, body);
      else await api.post("/api/giacong", body);
      onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Lỗi lưu phiếu.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={() => !saving && onClose()}
      wide
      solid
      title={isEdit ? `Sửa ${loaiPhieu.toLowerCase()}` : `Tạo ${loaiPhieu.toLowerCase()}`}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Hủy
          </Button>
          <Button onClick={save} loading={saving}>
            Lưu phiếu
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Field label="Đối tác gia công">
            <Input value={doiTac} onChange={(e) => setDoiTac(e.target.value)} placeholder="Tên đối tác" />
          </Field>
          <Field label={isNhapPhieu ? "Ngày nhập" : "Ngày xuất"}>
            <DatePicker value={ngayLap} onChange={setNgayLap} ariaLabel={isNhapPhieu ? "Ngày nhập" : "Ngày xuất"} />
          </Field>
          <Field label="Nhân viên phụ trách">
            <Input value={nhanVien} disabled readOnly placeholder="Họ tên" />
          </Field>
          {!isNhapPhieu && (
            <Field label="Hạn hoàn thành">
              <DatePicker value={hanHoanThanh} onChange={setHanHoanThanh} clearable ariaLabel="Hạn hoàn thành" />
            </Field>
          )}
          <Field label="Ghi chú">
            <Input value={ghiChu} onChange={(e) => setGhiChu(e.target.value)} placeholder="Ghi chú thêm" />
          </Field>
        </div>

        {/* Dòng hàng */}
        <div>
          <div className="mb-2 flex items-center justify-between">
            <span className="text-sm font-bold text-[var(--text)]">Hàng hóa</span>
            <Button variant="soft" onClick={() => setLines((arr) => [...arr, emptyLine(loaiPhieu)])}>
              <Plus className="h-4 w-4" /> Thêm dòng
            </Button>
          </div>
          <div className="scroll-thin overflow-x-auto rounded-xl border border-[var(--glass-border)]">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-[var(--glass-border)] text-xs text-[var(--text-muted)]">
                  <th className="px-2 py-2 text-left font-semibold">Tên hàng</th>
                  <th className="px-2 py-2 text-left font-semibold">Quy cách</th>
                  <th className="px-2 py-2 text-left font-semibold">ĐVT</th>
                  <th className="px-2 py-2 text-right font-semibold">SL</th>
                  {isNhapPhieu && <th className="px-2 py-2 text-right font-semibold">Đơn giá GC</th>}
                  {isNhapPhieu && <th className="px-2 py-2 text-right font-semibold">Thành tiền</th>}
                  <th className="w-8" />
                </tr>
              </thead>
              <tbody>
                {lines.map((line, i) => (
                  <tr key={i} className="border-b border-[var(--glass-border)]/40">
                    <td className="p-1">
                      <CellInput value={line.tenHang} onChange={(v) => setLine(i, { tenHang: v })} placeholder="Nhập tên hàng" />
                    </td>
                    <td className="p-1">
                      <CellInput value={line.quyCach} onChange={(v) => setLine(i, { quyCach: v })} placeholder="Quy cách" />
                    </td>
                    <td className="p-1">
                      <CellInput value={line.donViTinh} onChange={(v) => setLine(i, { donViTinh: v })} placeholder="ĐVT" />
                    </td>
                    <td className="p-1">
                      <CellInput
                        type="number"
                        align="right"
                        value={String(line.soLuong)}
                        onChange={(v) => setLine(i, { soLuong: +v || 0 })}
                      />
                    </td>
                    {isNhapPhieu && (
                      <td className="p-1">
                        <CellInput
                          type="number"
                          align="right"
                          value={String(line.donGiaGiaCong)}
                          onChange={(v) => setLine(i, { donGiaGiaCong: +v || 0 })}
                        />
                      </td>
                    )}
                    {isNhapPhieu && (
                      <td className="whitespace-nowrap px-2 py-1 text-right font-semibold tabular-nums">
                        {money(line.soLuong * line.donGiaGiaCong)} ₫
                      </td>
                    )}
                    <td className="px-1">
                      <button
                        onClick={() => setLines((arr) => (arr.length > 1 ? arr.filter((_, j) => j !== i) : arr))}
                        className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-red-500/10 hover:text-[var(--danger)]"
                        aria-label="Xóa dòng"
                        type="button"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
              {isNhapPhieu && (
                <tfoot>
                  <tr>
                    <td colSpan={4} className="px-2 py-2.5 text-right text-sm font-bold">
                      Tổng phí gia công
                    </td>
                    <td className="whitespace-nowrap px-2 py-2.5 text-right text-base font-bold accent-text tabular-nums">
                      {money(total)} ₫
                    </td>
                    <td />
                  </tr>
                </tfoot>
              )}
            </table>
          </div>
        </div>

        {error && <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>}
        {loadingDetail && (
          <div className="flex items-center gap-2 text-sm text-[var(--text-muted)]">
            <Loader2 className="h-4 w-4 animate-spin" /> Đang tải phiếu...
          </div>
        )}
      </div>
    </Modal>
  );
}
