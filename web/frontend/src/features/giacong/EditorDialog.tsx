import {
  useEffect,
  useState,
  type CSSProperties,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
} from "react";
import { motion } from "motion/react";
import { Loader2, Plus, Trash2, X } from "lucide-react";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogTitle,
} from "../../shadcn/dialog";
import { Button } from "../../shadcn/button";
import { cn } from "../../lib/cn";
import { useAuth } from "../../lib/auth";
import { api } from "../../lib/api";
import { money } from "../../lib/format";
import type { GiaCongDetail, GiaCongLine } from "../../lib/types";

const LOAI_PHIEU = ["Xuất gia công", "Nhập gia công"];
const TRANG_THAI = ["Đang xử lý", "Hoàn thành", "Chờ đối tác", "Hủy"];

const emptyLine = (): GiaCongLine => ({
  id: 0,
  loaiDong: "Hàng hóa",
  maHang: "",
  tenHang: "",
  quyCach: "",
  donViTinh: "Kg",
  soLuong: 1,
  donGiaGiaCong: 0,
  trangThaiDong: "Chờ",
  ghiChu: "",
});

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

function GlassSelect({ className, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select {...rest} className={cn("gc-input", className)}>
      {children}
    </select>
  );
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
  onClose,
  onSaved,
}: {
  open: boolean;
  id: number | "new";
  seedId?: number;
  onClose: () => void;
  onSaved: () => void;
}) {
  const isEdit = id !== "new";
  const { user } = useAuth();
  const loggedInName = user?.fullName?.trim() || user?.username?.trim() || "";
  const [loaiPhieu, setLoaiPhieu] = useState(LOAI_PHIEU[0]);
  const [doiTac, setDoiTac] = useState("");
  const [nhanVien, setNhanVien] = useState("");
  const [ngayLap, setNgayLap] = useState(() => new Date().toISOString().slice(0, 10));
  const [hanHoanThanh, setHanHoanThanh] = useState("");
  const [trangThai, setTrangThai] = useState(TRANG_THAI[0]);
  const [tienDo, setTienDo] = useState(0);
  const [ghiChu, setGhiChu] = useState("");
  const [lines, setLines] = useState<GiaCongLine[]>([emptyLine()]);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
  const [loadingDetail, setLoadingDetail] = useState(false);

  useEffect(() => {
    if (!open) return;
    const source = isEdit ? (id as number) : seedId;
    setError("");
    if (source === undefined) {
      setLoaiPhieu(LOAI_PHIEU[0]);
      setDoiTac("");
      setNhanVien(loggedInName);
      setNgayLap(new Date().toISOString().slice(0, 10));
      setHanHoanThanh("");
      setTrangThai(TRANG_THAI[0]);
      setTienDo(0);
      setGhiChu("");
      setLines([emptyLine()]);
      return;
    }
    setLoadingDetail(true);
    api
      .get<GiaCongDetail>(`/api/giacong/${source}`)
      .then((d) => {
        setLoaiPhieu(d.loaiPhieu);
        setDoiTac(d.doiTac);
        setNhanVien(isEdit ? d.nhanVienPhuTrach : loggedInName);
        setNgayLap(isEdit ? d.ngayLap : new Date().toISOString().slice(0, 10));
        setHanHoanThanh(d.hanHoanThanh ?? "");
        setTrangThai(isEdit ? d.trangThai : TRANG_THAI[0]);
        setTienDo(isEdit ? d.tienDo : 0);
        setGhiChu(d.ghiChu);
        setLines(
          d.lines.length
            ? d.lines.map((l) => ({ ...l, quyCach: l.quyCach ?? "", id: isEdit ? l.id : 0 }))
            : [emptyLine()],
        );
      })
      .catch((e) => setError(e instanceof Error ? e.message : "Lỗi tải phiếu."))
      .finally(() => setLoadingDetail(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, id, seedId, loggedInName]);

  const total = lines.reduce((s, l) => s + (l.soLuong || 0) * (l.donGiaGiaCong || 0), 0);
  const setLine = (i: number, patch: Partial<GiaCongLine>) =>
    setLines((arr) => arr.map((l, j) => (j === i ? { ...l, ...patch } : l)));

  const save = async () => {
    setSaving(true);
    setError("");
    const body = {
      loaiPhieu,
      doiTac,
      nhanVienPhuTrach: isEdit ? nhanVien : loggedInName,
      ngayLap,
      hanHoanThanh: hanHoanThanh || null,
      trangThai,
      tienDo: +tienDo,
      buocHienTai: 1,
      ghiChu,
      lines,
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
        {/* Header */}
        <div className="gc-editor-header flex items-start justify-between gap-4 border-b border-[var(--gc-border)] px-6 py-4">
          <div>
            <DialogTitle className="gc-editor-title">{isEdit ? "Sửa phiếu gia công" : "Tạo phiếu gia công"}</DialogTitle>
            <DialogDescription className="gc-editor-description mt-0.5">
              Nhập thông tin phiếu và danh sách hàng hóa gia công.
            </DialogDescription>
          </div>
          <DialogClose asChild>
            <button className="gc-icon-btn gc-editor-close h-9 w-9 shrink-0" aria-label="Đóng">
              <X className="h-4 w-4" />
            </button>
          </DialogClose>
        </div>

        {/* Body */}
        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.34, delay: 0.1, ease: [0.22, 1, 0.36, 1] }}
          className="gc-editor-body gc-scroll flex-1 space-y-4 overflow-auto px-6 py-5"
        >
          <div className="gc-editor-grid grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="Loại phiếu">
              <GlassSelect value={loaiPhieu} onChange={(e) => setLoaiPhieu(e.target.value)}>
                {LOAI_PHIEU.map((x) => (
                  <option key={x}>{x}</option>
                ))}
              </GlassSelect>
            </Field>
            <Field label="Đối tác">
              <GlassInput value={doiTac} onChange={(e) => setDoiTac(e.target.value)} placeholder="Tên đối tác" />
            </Field>
            <Field label="Nhân viên phụ trách">
              <GlassInput value={nhanVien} disabled readOnly placeholder="Họ tên" />
            </Field>
            <Field label="Ngày lập">
              <GlassInput type="date" value={ngayLap} onChange={(e) => setNgayLap(e.target.value)} />
            </Field>
            <Field label="Hạn hoàn thành">
              <GlassInput type="date" value={hanHoanThanh} onChange={(e) => setHanHoanThanh(e.target.value)} />
            </Field>
            <Field label="Trạng thái">
              <GlassSelect value={trangThai} onChange={(e) => setTrangThai(e.target.value)}>
                {TRANG_THAI.map((x) => (
                  <option key={x}>{x}</option>
                ))}
              </GlassSelect>
            </Field>
          </div>

          <div className="gc-editor-progress">
            <div className="mb-1.5 flex items-center justify-between">
              <span className="text-sm font-semibold text-[var(--gc-text-soft)]">Tiến độ: {tienDo}%</span>
            </div>
            <input
              type="range"
              min={0}
              max={100}
              value={tienDo}
              onChange={(e) => setTienDo(+e.target.value)}
              className="gc-range"
              style={{ "--gc-range": `${tienDo}%` } as CSSProperties}
            />
          </div>

          {/* Bảng dòng hàng */}
          <div className="gc-editor-lines">
            <div className="mb-2 flex items-center justify-between">
              <span className="text-sm font-bold text-[var(--gc-text)]">Hàng hóa</span>
              <Button
                variant="soft"
                size="sm"
                onClick={() => setLines((a) => [...a, emptyLine()])}
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
                    <th className="px-2 py-2.5 text-right font-semibold">Đơn giá GC</th>
                    <th className="px-2 py-2.5 text-right font-semibold">Thành tiền</th>
                    <th className="w-9" />
                  </tr>
                </thead>
                <tbody>
                  {lines.map((l, i) => (
                    <tr key={i}>
                      <td className="p-1">
                        <LineCell
                          value={l.tenHang}
                          onChange={(v) => setLine(i, { tenHang: v })}
                          placeholder="Nhập tên hàng"
                        />
                      </td>
                      <td className="p-1">
                        <LineCell
                          value={l.quyCach}
                          onChange={(v) => setLine(i, { quyCach: v })}
                          placeholder="Nhập quy cách"
                        />
                      </td>
                      <td className="p-1">
                        <LineCell
                          value={l.donViTinh}
                          onChange={(v) => setLine(i, { donViTinh: v })}
                          placeholder="ĐVT"
                        />
                      </td>
                      <td className="p-1">
                        <LineCell
                          type="number"
                          align="right"
                          value={String(l.soLuong)}
                          onChange={(v) => setLine(i, { soLuong: +v || 0 })}
                        />
                      </td>
                      <td className="p-1">
                        <LineCell
                          type="number"
                          align="right"
                          value={String(l.donGiaGiaCong)}
                          onChange={(v) => setLine(i, { donGiaGiaCong: +v || 0 })}
                        />
                      </td>
                      <td className="whitespace-nowrap px-2 py-1 text-right font-semibold tabular-nums">
                        {money(l.soLuong * l.donGiaGiaCong)}
                      </td>
                      <td className="px-1">
                        <button
                          onClick={() => setLines((a) => (a.length > 1 ? a.filter((_, j) => j !== i) : a))}
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
                <tfoot>
                  <tr>
                    <td colSpan={5} className="px-2 py-2.5 text-right text-sm font-bold">
                      Tổng giá trị
                    </td>
                    <td className="whitespace-nowrap px-2 py-2.5 text-right text-base font-black text-[var(--gc-accent)] tabular-nums">
                      {money(total)} ₫
                    </td>
                    <td />
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>

          <Field label="Ghi chú">
            <GlassInput value={ghiChu} onChange={(e) => setGhiChu(e.target.value)} placeholder="Ghi chú thêm (nếu có)" />
          </Field>

          {error && (
            <div className="rounded-xl bg-rose-500/10 px-3 py-2.5 text-sm font-medium text-rose-500">{error}</div>
          )}
          {loadingDetail && (
            <div className="flex items-center gap-2 text-sm text-[var(--gc-text-muted)]">
              <Loader2 className="h-4 w-4 animate-spin" /> Đang tải phiếu...
            </div>
          )}
        </motion.div>

        {/* Footer */}
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
