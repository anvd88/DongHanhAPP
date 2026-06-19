import { useEffect, useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { Modal } from "../components/Modal";
import { Button, Field, Input, Select } from "../components/ui";
import { api } from "../lib/api";
import { money } from "../lib/format";
import type { GiaCongDetail, GiaCongLine } from "../lib/types";

const LOAI_PHIEU = ["Xuất gia công", "Nhập gia công"];
const TRANG_THAI = ["Đang xử lý", "Hoàn thành", "Chờ đối tác", "Hủy"];
const LOAI_DONG = ["Nguyên liệu", "Thành phẩm", "Hao hụt"];

const emptyLine = (): GiaCongLine => ({
  id: 0, loaiDong: "Nguyên liệu", maHang: "", tenHang: "", donViTinh: "", soLuong: 1, donGiaGiaCong: 0, trangThaiDong: "Chờ", ghiChu: "",
});

export function GiaCongEditor({ id, onClose, onSaved }: { id: number | "new"; onClose: () => void; onSaved: () => void }) {
  const [loaiPhieu, setLoaiPhieu] = useState(LOAI_PHIEU[0]);
  const [doiTac, setDoiTac] = useState("");
  const [nhanVien, setNhanVien] = useState("");
  const [ngayLap, setNgayLap] = useState(new Date().toISOString().slice(0, 10));
  const [hanHoanThanh, setHanHoanThanh] = useState("");
  const [trangThai, setTrangThai] = useState(TRANG_THAI[0]);
  const [tienDo, setTienDo] = useState(0);
  const [ghiChu, setGhiChu] = useState("");
  const [lines, setLines] = useState<GiaCongLine[]>([emptyLine()]);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (id !== "new") {
      api.get<GiaCongDetail>(`/api/giacong/${id}`).then((d) => {
        setLoaiPhieu(d.loaiPhieu); setDoiTac(d.doiTac); setNhanVien(d.nhanVienPhuTrach);
        setNgayLap(d.ngayLap); setHanHoanThanh(d.hanHoanThanh ?? ""); setTrangThai(d.trangThai);
        setTienDo(d.tienDo); setGhiChu(d.ghiChu); setLines(d.lines.length ? d.lines : [emptyLine()]);
      });
    }
  }, [id]);

  const total = lines.reduce((s, l) => s + (l.soLuong || 0) * (l.donGiaGiaCong || 0), 0);
  const setLine = (i: number, patch: Partial<GiaCongLine>) =>
    setLines((arr) => arr.map((l, j) => (j === i ? { ...l, ...patch } : l)));

  const save = async () => {
    setSaving(true);
    setError("");
    const body = {
      loaiPhieu, doiTac, nhanVienPhuTrach: nhanVien, ngayLap,
      hanHoanThanh: hanHoanThanh || null, trangThai, tienDo: +tienDo, buocHienTai: 1, ghiChu, lines,
    };
    try {
      if (id === "new") await api.post("/api/giacong", body);
      else await api.put(`/api/giacong/${id}`, body);
      onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Lỗi lưu phiếu.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      wide
      title={id === "new" ? "Tạo phiếu gia công" : "Sửa phiếu gia công"}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Hủy</Button>
          <Button onClick={save} loading={saving}>Lưu phiếu</Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="Loại phiếu"><Select value={loaiPhieu} onChange={(e) => setLoaiPhieu(e.target.value)} className="w-full">{LOAI_PHIEU.map((x) => <option key={x}>{x}</option>)}</Select></Field>
          <Field label="Đối tác"><Input value={doiTac} onChange={(e) => setDoiTac(e.target.value)} /></Field>
          <Field label="Nhân viên phụ trách"><Input value={nhanVien} onChange={(e) => setNhanVien(e.target.value)} /></Field>
          <Field label="Ngày lập"><Input type="date" value={ngayLap} onChange={(e) => setNgayLap(e.target.value)} /></Field>
          <Field label="Hạn hoàn thành"><Input type="date" value={hanHoanThanh} onChange={(e) => setHanHoanThanh(e.target.value)} /></Field>
          <Field label="Trạng thái"><Select value={trangThai} onChange={(e) => setTrangThai(e.target.value)} className="w-full">{TRANG_THAI.map((x) => <option key={x}>{x}</option>)}</Select></Field>
        </div>
        <Field label={`Tiến độ: ${tienDo}%`}>
          <input type="range" min={0} max={100} value={tienDo} onChange={(e) => setTienDo(+e.target.value)} className="w-full accent-[var(--accent)]" />
        </Field>

        <div>
          <div className="mb-2 flex items-center justify-between">
            <span className="text-sm font-bold text-[var(--text)]">Hàng hóa</span>
            <Button variant="soft" onClick={() => setLines((a) => [...a, emptyLine()])}><Plus className="h-4 w-4" /> Thêm dòng</Button>
          </div>
          <div className="scroll-thin overflow-x-auto rounded-xl border border-[var(--glass-border)]">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-[var(--glass-border)] text-xs text-[var(--text-muted)]">
                  <th className="px-2 py-2 text-left font-semibold">Loại</th>
                  <th className="px-2 py-2 text-left font-semibold">Tên hàng</th>
                  <th className="px-2 py-2 text-left font-semibold">ĐVT</th>
                  <th className="px-2 py-2 text-right font-semibold">SL</th>
                  <th className="px-2 py-2 text-right font-semibold">Đơn giá GC</th>
                  <th className="px-2 py-2 text-right font-semibold">Thành tiền</th>
                  <th className="w-8" />
                </tr>
              </thead>
              <tbody>
                {lines.map((l, i) => (
                  <tr key={i} className="border-b border-[var(--glass-border)]/40">
                    <td className="p-1">
                      <select value={l.loaiDong} onChange={(e) => setLine(i, { loaiDong: e.target.value })} className="rounded-lg bg-transparent px-1.5 py-1.5 text-sm outline-none focus:bg-[var(--accent-soft)]">
                        {LOAI_DONG.map((x) => <option key={x}>{x}</option>)}
                      </select>
                    </td>
                    <td className="p-1"><Cell value={l.tenHang} onChange={(v) => setLine(i, { tenHang: v })} /></td>
                    <td className="p-1"><Cell value={l.donViTinh} onChange={(v) => setLine(i, { donViTinh: v })} /></td>
                    <td className="p-1"><Cell type="number" align="right" value={String(l.soLuong)} onChange={(v) => setLine(i, { soLuong: +v || 0 })} /></td>
                    <td className="p-1"><Cell type="number" align="right" value={String(l.donGiaGiaCong)} onChange={(v) => setLine(i, { donGiaGiaCong: +v || 0 })} /></td>
                    <td className="px-2 py-1 text-right font-semibold">{money(l.soLuong * l.donGiaGiaCong)}</td>
                    <td className="px-1">
                      <button onClick={() => setLines((a) => (a.length > 1 ? a.filter((_, j) => j !== i) : a))} className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-red-500/10 hover:text-[var(--danger)]">
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td colSpan={5} className="px-2 py-2.5 text-right text-sm font-bold">Tổng giá trị</td>
                  <td className="px-2 py-2.5 text-right text-base font-bold accent-text">{money(total)} ₫</td>
                  <td />
                </tr>
              </tfoot>
            </table>
          </div>
        </div>

        <Field label="Ghi chú"><Input value={ghiChu} onChange={(e) => setGhiChu(e.target.value)} /></Field>
        {error && <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>}
      </div>
    </Modal>
  );
}

function Cell({ value, onChange, type = "text", align = "left" }: { value: string; onChange: (v: string) => void; type?: string; align?: "left" | "right" }) {
  return (
    <input type={type} value={value} onChange={(e) => onChange(e.target.value)}
      className={`w-full min-w-[90px] rounded-lg bg-transparent px-2 py-1.5 text-sm outline-none focus:bg-[var(--accent-soft)] ${align === "right" ? "text-right" : ""}`} />
  );
}
