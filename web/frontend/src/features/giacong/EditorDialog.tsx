import { useEffect, useState, type InputHTMLAttributes, type ReactNode } from "react";
import { motion } from "motion/react";
import { Loader2, Plus, Trash2, X } from "lucide-react";
import { Dialog, DialogClose, DialogContent, DialogDescription, DialogTitle } from "../../shadcn/dialog";
import { Button } from "../../shadcn/button";
import { cn } from "../../lib/cn";
import { useAuth } from "../../lib/auth";
import { api } from "../../lib/api";
import { money } from "../../lib/format";
import { DatePicker } from "../../components/DateField";
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

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="gc-editor-field block">
      <span className="mb-1.5 block text-xs font-semibold text-[var(--gc-text-soft)]">{label}</span>
      {children}
    </label>
  );
}

function GlassInput({ className, ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...rest} className={cn("gc-input", className)} />;
}

function LineCell({
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
      className={cn("gc-cell", align === "right" && "text-right")}
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
  const [nhanVien, setNhanVien] = useState("");
  const [ngayLap, setNgayLap] = useState(() => new Date().toISOString().slice(0, 10));
  const [hanHoanThanh, setHanHoanThanh] = useState("");
  const [ghiChu, setGhiChu] = useState("");
  const [lines, setLines] = useState<GiaCongLine[]>([emptyLine(initialLoaiPhieu)]);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
  const [loadingDetail, setLoadingDetail] = useState(false);

  useEffect(() => {
    if (!open) return;
    const source = isEdit ? (id as number) : seedId;
    setError("");
    if (source === undefined) {
      setLoaiPhieu(initialLoaiPhieu);
      setDoiTac("");
      setNhanVien(loggedInName);
      setNgayLap(new Date().toISOString().slice(0, 10));
      setHanHoanThanh("");
      setGhiChu("");
      setLines([emptyLine(initialLoaiPhieu)]);
      return;
    }

    setLoadingDetail(true);
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
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        className="gc-editor-dialog flex max-h-[calc(100vh-34px)] flex-col"
        onPointerDownOutside={(e) => saving && e.preventDefault()}
      >
        <div className="gc-editor-header flex items-start justify-between gap-4 border-b border-[var(--gc-border)] px-6 py-4">
          <div>
            <DialogTitle className="gc-editor-title">
              {isEdit ? `Sửa ${loaiPhieu.toLowerCase()}` : `Tạo ${loaiPhieu.toLowerCase()}`}
            </DialogTitle>
            <DialogDescription className="gc-editor-description mt-0.5">
              {isNhapPhieu ? "Nhập hàng gia công về và ghi nhận phí phải trả." : "Xuất hàng đi gia công, không ghi nhận giá tiền."}
            </DialogDescription>
          </div>
          <DialogClose asChild>
            <button className="gc-icon-btn gc-editor-close h-9 w-9 shrink-0" aria-label="Đóng">
              <X className="h-4 w-4" />
            </button>
          </DialogClose>
        </div>

        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.34, delay: 0.1, ease: [0.22, 1, 0.36, 1] }}
          className="gc-editor-body gc-scroll flex-1 space-y-4 overflow-auto px-6 py-5"
        >
          <div className="gc-editor-grid grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="Đối tác gia công">
              <GlassInput value={doiTac} onChange={(e) => setDoiTac(e.target.value)} placeholder="Tên đối tác" />
            </Field>
            <Field label={isNhapPhieu ? "Ngày nhập" : "Ngày xuất"}>
              <DatePicker value={ngayLap} onChange={setNgayLap} ariaLabel={isNhapPhieu ? "Ngày nhập" : "Ngày xuất"} />
            </Field>
            <Field label="Nhân viên phụ trách">
              <GlassInput value={nhanVien} disabled readOnly placeholder="Họ tên" />
            </Field>
            {!isNhapPhieu && (
              <Field label="Hạn hoàn thành">
                <DatePicker value={hanHoanThanh} onChange={setHanHoanThanh} clearable ariaLabel="Hạn hoàn thành" />
              </Field>
            )}
          </div>

          <div className="gc-editor-lines">
            <div className="mb-2 flex items-center justify-between">
              <span className="text-sm font-bold text-[var(--gc-text)]">Hàng hóa</span>
              <Button
                variant="soft"
                size="sm"
                onClick={() => setLines((arr) => [...arr, emptyLine(loaiPhieu)])}
                className="gc-editor-add-line"
              >
                <Plus className="h-4 w-4" /> Thêm dòng
              </Button>
            </div>
            <div className="gc-linetable-wrap gc-editor-linetable gc-scroll overflow-x-auto">
              <table className="gc-linetable w-full text-sm">
                <thead>
                  <tr className="text-xs text-[var(--gc-text-muted)]">
                    <th className="px-2 py-2.5 text-left font-semibold">Tên hàng</th>
                    <th className="px-2 py-2.5 text-left font-semibold">Quy cách</th>
                    <th className="px-2 py-2.5 text-left font-semibold">ĐVT</th>
                    <th className="px-2 py-2.5 text-right font-semibold">SL</th>
                    {isNhapPhieu && <th className="px-2 py-2.5 text-right font-semibold">Đơn giá GC</th>}
                    {isNhapPhieu && <th className="px-2 py-2.5 text-right font-semibold">Thành tiền</th>}
                    <th className="w-9" />
                  </tr>
                </thead>
                <tbody>
                  {lines.map((line, i) => (
                    <tr key={i}>
                      <td className="p-1">
                        <LineCell value={line.tenHang} onChange={(v) => setLine(i, { tenHang: v })} placeholder="Nhập tên hàng" />
                      </td>
                      <td className="p-1">
                        <LineCell value={line.quyCach} onChange={(v) => setLine(i, { quyCach: v })} placeholder="Quy cách" />
                      </td>
                      <td className="p-1">
                        <LineCell value={line.donViTinh} onChange={(v) => setLine(i, { donViTinh: v })} placeholder="ĐVT" />
                      </td>
                      <td className="p-1">
                        <LineCell
                          type="number"
                          align="right"
                          value={String(line.soLuong)}
                          onChange={(v) => setLine(i, { soLuong: +v || 0 })}
                        />
                      </td>
                      {isNhapPhieu && (
                        <td className="p-1">
                          <LineCell
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
                          className="grid h-8 w-8 place-items-center rounded-lg text-[var(--gc-text-muted)] transition-colors hover:bg-rose-500/10 hover:text-rose-500"
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
                      <td colSpan={5} className="px-2 py-2.5 text-right text-sm font-bold">
                        Tổng phí gia công
                      </td>
                      <td className="whitespace-nowrap px-2 py-2.5 text-right text-base font-black text-[var(--gc-accent)] tabular-nums">
                        {money(total)} ₫
                      </td>
                      <td />
                    </tr>
                  </tfoot>
                )}
              </table>
            </div>
          </div>

          <Field label="Ghi chú">
            <GlassInput value={ghiChu} onChange={(e) => setGhiChu(e.target.value)} placeholder="Ghi chú thêm" />
          </Field>

          {error && <div className="rounded-xl bg-rose-500/10 px-3 py-2.5 text-sm font-medium text-rose-500">{error}</div>}
          {loadingDetail && (
            <div className="flex items-center gap-2 text-sm text-[var(--gc-text-muted)]">
              <Loader2 className="h-4 w-4 animate-spin" /> Đang tải phiếu...
            </div>
          )}
        </motion.div>

        <div className="gc-editor-footer flex items-center justify-end gap-2.5 border-t border-[var(--gc-border)] px-6 py-4">
          <Button variant="ghost" onClick={onClose} className="gc-editor-cancel">
            Hủy
          </Button>
          <Button onClick={save} disabled={saving} className="gc-editor-save">
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Lưu phiếu
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
