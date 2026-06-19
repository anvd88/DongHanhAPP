import { useEffect, useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { Modal } from "../components/Modal";
import { Button, Field, Input } from "../components/ui";
import { api } from "../lib/api";
import { money } from "../lib/format";
import type { Customer, DocumentDetail, DocumentLine } from "../lib/types";

const emptyLine = (): DocumentLine => ({ lineContent: "", spec: "", quantity: 1, unitPrice: 0, note: "" });

export function DocumentEditor({
  id,
  customers,
  onClose,
  onSaved,
}: {
  id: string | null | "new";
  customers: Customer[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [voucherNo, setVoucherNo] = useState("");
  const [docDate, setDocDate] = useState(new Date().toISOString().slice(0, 10));
  const [customerName, setCustomerName] = useState("");
  const [content, setContent] = useState("");
  const [note, setNote] = useState("");
  const [lines, setLines] = useState<DocumentLine[]>([emptyLine()]);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (id && id !== "new") {
      api.get<DocumentDetail>(`/api/documents/${id}`).then((d) => {
        setVoucherNo(d.voucherNo);
        setDocDate(d.date);
        setCustomerName(d.customerName);
        setContent(d.content);
        setNote(d.note);
        setLines(d.lines.length ? d.lines : [emptyLine()]);
      });
    }
  }, [id]);

  const total = lines.reduce((s, l) => s + (l.quantity || 0) * (l.unitPrice || 0), 0);
  const setLine = (i: number, patch: Partial<DocumentLine>) =>
    setLines((arr) => arr.map((l, j) => (j === i ? { ...l, ...patch } : l)));

  const save = async () => {
    if (!voucherNo.trim()) {
      setError("Vui lòng nhập số phiếu.");
      return;
    }
    setSaving(true);
    setError("");
    const body = { voucherNo, date: docDate, customerName, content, note, lines };
    try {
      if (id === "new") await api.post("/api/documents", body);
      else await api.put(`/api/documents/${id}`, body);
      onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Lỗi lưu chứng từ.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      wide
      title={id === "new" ? "Tạo chứng từ" : "Sửa chứng từ"}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Hủy</Button>
          <Button onClick={save} loading={saving}>Lưu chứng từ</Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Số phiếu *">
            <Input value={voucherNo} onChange={(e) => setVoucherNo(e.target.value)} placeholder="VD: BH26-0001" />
          </Field>
          <Field label="Ngày chứng từ">
            <Input type="date" value={docDate} onChange={(e) => setDocDate(e.target.value)} />
          </Field>
          <Field label="Khách hàng">
            <Input
              list="customer-list"
              value={customerName}
              onChange={(e) => setCustomerName(e.target.value)}
              placeholder="Chọn hoặc nhập tên khách hàng"
            />
            <datalist id="customer-list">
              {customers.map((c) => (
                <option key={c.id} value={c.name} />
              ))}
            </datalist>
          </Field>
          <Field label="Nội dung">
            <Input value={content} onChange={(e) => setContent(e.target.value)} placeholder="Diễn giải" />
          </Field>
        </div>

        {/* Dòng hàng */}
        <div>
          <div className="mb-2 flex items-center justify-between">
            <span className="text-sm font-bold text-[var(--text)]">Chi tiết dòng hàng</span>
            <Button variant="soft" onClick={() => setLines((a) => [...a, emptyLine()])}>
              <Plus className="h-4 w-4" /> Thêm dòng
            </Button>
          </div>
          <div className="scroll-thin overflow-x-auto rounded-xl border border-[var(--glass-border)]">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-[var(--glass-border)] text-xs text-[var(--text-muted)]">
                  <th className="px-2 py-2 text-left font-semibold">Nội dung</th>
                  <th className="px-2 py-2 text-left font-semibold">Quy cách</th>
                  <th className="px-2 py-2 text-right font-semibold">SL</th>
                  <th className="px-2 py-2 text-right font-semibold">Đơn giá</th>
                  <th className="px-2 py-2 text-right font-semibold">Thành tiền</th>
                  <th className="w-8" />
                </tr>
              </thead>
              <tbody>
                {lines.map((l, i) => (
                  <tr key={i} className="border-b border-[var(--glass-border)]/40">
                    <td className="p-1"><CellInput value={l.lineContent} onChange={(v) => setLine(i, { lineContent: v })} /></td>
                    <td className="p-1"><CellInput value={l.spec} onChange={(v) => setLine(i, { spec: v })} /></td>
                    <td className="p-1"><CellInput type="number" align="right" value={String(l.quantity)} onChange={(v) => setLine(i, { quantity: +v || 0 })} /></td>
                    <td className="p-1"><CellInput type="number" align="right" value={String(l.unitPrice)} onChange={(v) => setLine(i, { unitPrice: +v || 0 })} /></td>
                    <td className="px-2 py-1 text-right font-semibold">{money(l.quantity * l.unitPrice)}</td>
                    <td className="px-1">
                      <button
                        onClick={() => setLines((a) => (a.length > 1 ? a.filter((_, j) => j !== i) : a))}
                        className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-red-500/10 hover:text-[var(--danger)]"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td colSpan={4} className="px-2 py-2.5 text-right text-sm font-bold">Tổng cộng</td>
                  <td className="px-2 py-2.5 text-right text-base font-bold accent-text">{money(total)} ₫</td>
                  <td />
                </tr>
              </tfoot>
            </table>
          </div>
        </div>

        {error && <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>}
      </div>
    </Modal>
  );
}

function CellInput({
  value,
  onChange,
  type = "text",
  align = "left",
}: {
  value: string;
  onChange: (v: string) => void;
  type?: string;
  align?: "left" | "right";
}) {
  return (
    <input
      type={type}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className={`w-full min-w-[80px] rounded-lg bg-transparent px-2 py-1.5 text-sm outline-none focus:bg-[var(--accent-soft)] ${
        align === "right" ? "text-right" : ""
      }`}
    />
  );
}
