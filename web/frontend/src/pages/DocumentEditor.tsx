import { useEffect, useState } from "react";
import { Plus, Printer, Trash2 } from "lucide-react";
import { Modal } from "../components/Modal";
import { Button, Field, Input, Select } from "../components/ui";
import { api } from "../lib/api";
import {
  createVoucherNo,
  defaultDocumentContent,
  documentKindLabel,
  inferDocumentKind,
  type DocumentKind,
} from "../lib/documents";
import { money } from "../lib/format";
import type { Customer, DocumentDetail, DocumentLine } from "../lib/types";

const emptyLine = (): DocumentLine => ({ lineContent: "", spec: "", quantity: 1, unitPrice: 0, note: "" });

const formatEditableNumber = (value: number) => (Number.isFinite(value) ? String(value) : "0");

const evaluateArithmeticInput = (raw: string): number | null => {
  const input = raw
    .trim()
    .replace(/^=/, "")
    .replace(/[xX×]/g, "*")
    .replace(/÷/g, "/")
    .replace(/,/g, ".");

  if (!input) return 0;

  let index = 0;

  const skipSpaces = () => {
    while (/\s/.test(input[index] ?? "")) index += 1;
  };

  const parseNumber = (): number | null => {
    skipSpaces();
    const start = index;
    let hasDigit = false;
    let hasDot = false;

    while (index < input.length) {
      const ch = input[index];
      if (/\d/.test(ch)) {
        hasDigit = true;
        index += 1;
      } else if (ch === "." && !hasDot) {
        hasDot = true;
        index += 1;
      } else {
        break;
      }
    }

    if (!hasDigit) return null;
    const value = Number(input.slice(start, index));
    return Number.isFinite(value) ? value : null;
  };

  const parseFactor = (): number | null => {
    skipSpaces();
    const ch = input[index];

    if (ch === "+" || ch === "-") {
      index += 1;
      const value = parseFactor();
      if (value === null) return null;
      return ch === "-" ? -value : value;
    }

    if (ch === "(") {
      index += 1;
      const value = parseExpression();
      skipSpaces();
      if (input[index] !== ")") return null;
      index += 1;
      return value;
    }

    return parseNumber();
  };

  const parseTerm = (): number | null => {
    let value = parseFactor();
    if (value === null) return null;

    while (true) {
      skipSpaces();
      const operator = input[index];
      if (operator !== "*" && operator !== "/") return value;

      index += 1;
      const next = parseFactor();
      if (next === null) return null;
      if (operator === "/" && next === 0) return null;
      value = operator === "*" ? value * next : value / next;
    }
  };

  const parseExpression = (): number | null => {
    let value = parseTerm();
    if (value === null) return null;

    while (true) {
      skipSpaces();
      const operator = input[index];
      if (operator !== "+" && operator !== "-") return value;

      index += 1;
      const next = parseTerm();
      if (next === null) return null;
      value = operator === "+" ? value + next : value - next;
    }
  };

  const result = parseExpression();
  skipSpaces();

  if (result === null || index !== input.length || !Number.isFinite(result)) return null;
  return Object.is(result, -0) ? 0 : result;
};

export function DocumentEditor({
  id,
  initialKind = "document",
  customers,
  onPrint,
  printLoading,
  keepOpenAfterSave,
  onClose,
  onSaved,
}: {
  id: string | null | "new";
  initialKind?: DocumentKind;
  customers: Customer[];
  onPrint?: () => void;
  printLoading?: boolean;
  keepOpenAfterSave?: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [docKind, setDocKind] = useState<DocumentKind>(initialKind);
  const [voucherNo, setVoucherNo] = useState(() => createVoucherNo(initialKind));
  const [docDate, setDocDate] = useState(new Date().toISOString().slice(0, 10));
  const [customerName, setCustomerName] = useState("");
  const [content, setContent] = useState(() => defaultDocumentContent(initialKind));
  const [note, setNote] = useState("");
  const [lines, setLines] = useState<DocumentLine[]>([emptyLine()]);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [saving, setSaving] = useState(false);

  const resetCreateForm = (kind: DocumentKind) => {
    setDocKind(kind);
    setVoucherNo(createVoucherNo(kind));
    setDocDate(new Date().toISOString().slice(0, 10));
    setCustomerName("");
    setContent(defaultDocumentContent(kind));
    setNote("");
    setLines([emptyLine()]);
    setError("");
  };

  useEffect(() => {
    if (id && id !== "new") {
      api.get<DocumentDetail>(`/api/documents/${id}`).then((d) => {
        setDocKind(inferDocumentKind(d));
        setVoucherNo(d.voucherNo);
        setDocDate(d.date);
        setCustomerName(d.customerName);
        setContent(d.content);
        setNote(d.note);
        setLines(d.lines.length ? d.lines : [emptyLine()]);
      });
    } else if (id === "new") {
      resetCreateForm(initialKind);
      setSuccess("");
    }
  }, [id, initialKind]);

  const isWarehouseSale = docKind === "document";
  const total = lines.reduce((s, l) => s + (l.quantity || 0) * (l.unitPrice || 0), 0);
  const setLine = (i: number, patch: Partial<DocumentLine>) =>
    setLines((arr) => arr.map((l, j) => (j === i ? { ...l, ...patch } : l)));

  const changeKind = (kind: DocumentKind) => {
    const previousKind = docKind;
    setDocKind(kind);
    setVoucherNo((current) => {
      const currentKind = inferDocumentKind({ voucherNo: current, content });
      return !current.trim() || currentKind === previousKind ? createVoucherNo(kind) : current;
    });
    setContent((current) => {
      const previousDefault = defaultDocumentContent(previousKind);
      return !current.trim() || current === previousDefault ? defaultDocumentContent(kind) : current;
    });
  };

  const save = async () => {
    if (!voucherNo.trim()) {
      setError("Vui lòng nhập số phiếu.");
      return;
    }
    setSaving(true);
    setError("");
    setSuccess("");
    const body = {
      voucherNo: voucherNo.trim(),
      date: docDate,
      customerName,
      content: content.trim() || defaultDocumentContent(docKind),
      note,
      lines,
    };
    try {
      if (id === "new") await api.post("/api/documents", body);
      else await api.put(`/api/documents/${id}`, body);

      if (id === "new" && keepOpenAfterSave) {
        resetCreateForm(docKind);
        setSuccess("Đã lưu phiếu.");
        onSaved();
        return;
      }

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
      solid
      title={id === "new" ? `Tạo ${documentKindLabel(docKind).toLowerCase()}` : "Sửa phiếu"}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Hủy</Button>
          {id !== "new" && onPrint && (
            <Button variant="soft" onClick={onPrint} loading={printLoading}>
              <Printer className="h-4 w-4" /> In phiếu
            </Button>
          )}
          <Button onClick={save} loading={saving}>Lưu phiếu</Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Field label="Loại phiếu">
            <Select value={docKind} onChange={(e) => changeKind(e.target.value as DocumentKind)} className="w-full">
              <option value="receipt">Phiếu thu</option>
              <option value="payment">Phiếu chi</option>
              <option value="document">Phiếu xuất kho bán hàng</option>
            </Select>
          </Field>
          <Field label="Số phiếu *">
            <Input value={voucherNo} onChange={(e) => setVoucherNo(e.target.value)} placeholder={isWarehouseSale ? "VD: XK260625-0930" : "VD: PT260625-0930"} />
          </Field>
          <Field label="Ngày lập">
            <Input type="date" value={docDate} onChange={(e) => setDocDate(e.target.value)} />
          </Field>
          <Field label={isWarehouseSale ? "Bên mua hàng" : "Khách hàng"}>
            <Input
              list="customer-list"
              value={customerName}
              onChange={(e) => setCustomerName(e.target.value)}
              placeholder={isWarehouseSale ? "Chọn hoặc nhập bên mua hàng" : "Chọn hoặc nhập tên khách hàng"}
            />
            <datalist id="customer-list">
              {customers.map((c) => (
                <option key={c.id} value={c.name} />
              ))}
            </datalist>
          </Field>
          <Field label={isWarehouseSale ? "Địa chỉ" : "Ghi chú"}>
            <Input value={note} onChange={(e) => setNote(e.target.value)} placeholder={isWarehouseSale ? "Địa chỉ bên mua hàng" : "Ghi chú phiếu"} />
          </Field>
          <Field label={isWarehouseSale ? "Thanh toán tiền" : "Nội dung"}>
            <Input value={content} onChange={(e) => setContent(e.target.value)} placeholder={isWarehouseSale ? "VD: Thanh toán tiền hàng inox" : "Diễn giải"} />
          </Field>
        </div>

        {/* Dòng hàng */}
        <div>
          <div className="mb-2 flex items-center justify-between">
            <span className="text-sm font-bold text-[var(--text)]">{isWarehouseSale ? "Chi tiết hàng xuất kho" : "Chi tiết dòng hàng"}</span>
            <Button variant="soft" onClick={() => setLines((a) => [...a, emptyLine()])}>
              <Plus className="h-4 w-4" /> Thêm dòng
            </Button>
          </div>
          <div className="scroll-thin overflow-x-auto rounded-xl border border-[var(--glass-border)]">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-[var(--glass-border)] text-xs text-[var(--text-muted)]">
                  <th className="px-2 py-2 text-left font-semibold">{isWarehouseSale ? "Chủng loại hàng hóa" : "Nội dung"}</th>
                  <th className="px-2 py-2 text-left font-semibold">{isWarehouseSale ? "Mã hàng" : "Quy cách"}</th>
                  <th className="px-2 py-2 text-right font-semibold">{isWarehouseSale ? "Khối lượng (kg)" : "SL"}</th>
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
                    <td className="p-1"><FormulaNumberInput align="right" value={l.quantity} onChange={(quantity) => setLine(i, { quantity })} /></td>
                    <td className="p-1"><FormulaNumberInput align="right" value={l.unitPrice} onChange={(unitPrice) => setLine(i, { unitPrice })} /></td>
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

        {success && <div className="rounded-xl bg-emerald-500/10 px-3 py-2.5 text-sm font-semibold text-emerald-700 dark:text-emerald-300">{success}</div>}
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

function FormulaNumberInput({
  value,
  onChange,
  align = "left",
}: {
  value: number;
  onChange: (v: number) => void;
  align?: "left" | "right";
}) {
  const [draft, setDraft] = useState(() => formatEditableNumber(value));
  const [focused, setFocused] = useState(false);
  const [invalid, setInvalid] = useState(false);

  useEffect(() => {
    if (!focused) setDraft(formatEditableNumber(value));
  }, [focused, value]);

  const commitDraft = () => {
    const result = evaluateArithmeticInput(draft);
    if (result === null) {
      setDraft(formatEditableNumber(value));
      setInvalid(true);
      return;
    }

    setInvalid(false);
    onChange(result);
    setDraft(formatEditableNumber(result));
  };

  return (
    <input
      type="text"
      inputMode="decimal"
      value={draft}
      title="Có thể nhập phép tính, ví dụ 1+1 hoặc =1+1"
      onFocus={() => {
        setFocused(true);
        setInvalid(false);
      }}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={() => {
        setFocused(false);
        commitDraft();
      }}
      onKeyDown={(e) => {
        if (e.key === "Enter") e.currentTarget.blur();
        if (e.key === "Escape") {
          setDraft(formatEditableNumber(value));
          e.currentTarget.blur();
        }
      }}
      className={`w-full min-w-[80px] rounded-lg bg-transparent px-2 py-1.5 text-sm outline-none focus:bg-[var(--accent-soft)] ${
        align === "right" ? "text-right" : ""
      } ${invalid ? "ring-1 ring-[var(--danger)]" : ""}`}
    />
  );
}
