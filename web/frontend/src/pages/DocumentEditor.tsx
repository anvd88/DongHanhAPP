import { useEffect, useState, type ReactNode } from "react";
import { Eye, Plus, Trash2 } from "lucide-react";
import { Modal } from "../components/Modal";
import { Button, Field, Input, Select } from "../components/ui";
import { DatePicker } from "../components/DateField";
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

import { ProductCellInput } from "../components/ProductCellInput";

const emptyLine = (): DocumentLine => ({ lineContent: "", spec: "", quantity: 1, unitPrice: 0, note: "" });

const formatNumericToken = (token: string) => {
  const normalized = token.replace(/,/g, "");
  const dotIndex = normalized.indexOf(".");
  const integerPart = (dotIndex >= 0 ? normalized.slice(0, dotIndex) : normalized)
    .replace(/^0+(?=\d)/, "");
  const decimalPart = dotIndex >= 0 ? normalized.slice(dotIndex) : "";
  return integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, ",") + decimalPart;
};

const formatArithmeticDraft = (value: string) =>
  value.replace(/\d[\d,]*(?:\.\d*)?/g, formatNumericToken);

const formatEditableNumber = (value: number) =>
  Number.isFinite(value) ? formatArithmeticDraft(String(value)) : "0";

const initialVoucherNo = (kind: DocumentKind) => (kind === "document" ? "" : createVoucherNo(kind));

const evaluateArithmeticInput = (raw: string): number | null => {
  const input = raw
    .trim()
    .replace(/^=/, "")
    .replace(/[xX×]/g, "*")
    .replace(/÷/g, "/")
    .replace(/,/g, "");

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
  readOnly = false,
  apiBasePath = "/api/documents",
  allowedKinds = ["document"],
  renderShell,
  beforeSave,
  onClose,
  onSaved,
}: {
  id: string | null | "new";
  initialKind?: DocumentKind;
  customers: Customer[];
  onPrint?: () => void;
  printLoading?: boolean;
  keepOpenAfterSave?: boolean;
  readOnly?: boolean;
  apiBasePath?: string;
  allowedKinds?: DocumentKind[];
  /**
   * Bọc phần thân + thanh nút của form bằng khung KHÁC thay cho hộp thoại mặc định.
   *
   * Dùng để nhét nguyên form này vào một màn lớn hơn (màn Phiếu gộp ở trang Bán hàng, nơi phiếu ·
   * giao hàng · đối soát nằm chung một hộp thoại có tab) mà không phải lồng hộp thoại trong hộp
   * thoại, và không phải tách form ra thành component riêng. Không truyền ⇒ vẫn là hộp thoại như cũ.
   */
  renderShell?: (parts: { title: string; body: ReactNode; footer: ReactNode }) => ReactNode;
  /**
   * Chạy TRƯỚC khi ghi phiếu; trả về false để dừng lại (kèm thông báo do người gọi tự hiện).
   *
   * Màn Phiếu gộp dùng nó để ghi LỊCH SỬ chỉnh sửa hàng thực nhận trước: phiếu đã phát hành mà sửa
   * số lượng/đơn giá thì phải để lại vết cũ→mới kèm lý do, còn PUT phiếu bên dưới thì xoá sạch rồi
   * chèn lại dòng nên tự nó không biết gì về "cũ".
   */
  beforeSave?: (lines: DocumentLine[]) => Promise<boolean>;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [docKind, setDocKind] = useState<DocumentKind>(initialKind);
  const [voucherNo, setVoucherNo] = useState(() => initialVoucherNo(initialKind));
  const [docDate, setDocDate] = useState(new Date().toISOString().slice(0, 10));
  const [customerName, setCustomerName] = useState("");
  const [content, setContent] = useState(() => defaultDocumentContent(initialKind));
  const [note, setNote] = useState("");
  const [lines, setLines] = useState<DocumentLine[]>([emptyLine()]);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [saving, setSaving] = useState(false);
  const [cancelledAt, setCancelledAt] = useState<string | null>(readOnly ? "locked" : null);
  const [cancelReason, setCancelReason] = useState("");

  const resetCreateForm = (kind: DocumentKind) => {
    setDocKind(kind);
    setVoucherNo(initialVoucherNo(kind));
    setDocDate(new Date().toISOString().slice(0, 10));
    setCustomerName("");
    setContent(defaultDocumentContent(kind));
    setNote("");
    setLines([emptyLine()]);
    setError("");
  };

  // CHỈ còn việc TẢI chứng từ đang sửa. Trường hợp "lập mới" không cần làm gì: trang cha gắn key theo
  // chứng từ + loại chứng từ (xem KeToan.tsx), nên mỗi lần mở là một component mới đã có sẵn giá trị
  // khởi tạo đúng. (resetCreateForm vẫn dùng cho luồng "lưu xong giữ form mở để nhập tiếp".)
  useEffect(() => {
    if (!id || id === "new") return;
    api.get<DocumentDetail>(`${apiBasePath}/${id}`).then((d) => {
      setDocKind(inferDocumentKind(d));
      setVoucherNo(d.voucherNo);
      setDocDate(d.date);
      setCustomerName(d.customerName);
      setContent(d.content);
      setNote(d.note);
      setLines(d.lines.length ? d.lines : [emptyLine()]);
      setCancelledAt(d.cancelledAt ?? null);
      setCancelReason(d.cancelReason ?? "");
    });
  }, [apiBasePath, id]);

  const isWarehouseSale = docKind === "document";
  const isLocked = readOnly || !!cancelledAt;
  const total = lines.reduce((s, l) => s + (l.quantity || 0) * (l.unitPrice || 0), 0);
  const setLine = (i: number, patch: Partial<DocumentLine>) =>
    setLines((arr) => arr.map((l, j) => (j === i ? { ...l, ...patch } : l)));

  const changeKind = (kind: DocumentKind) => {
    const previousKind = docKind;
    setDocKind(kind);
    setVoucherNo((current) => {
      const currentKind = inferDocumentKind({ voucherNo: current, content });
      return !current.trim() || currentKind === previousKind ? initialVoucherNo(kind) : current;
    });
    setContent((current) => {
      const previousDefault = defaultDocumentContent(previousKind);
      return !current.trim() || current === previousDefault ? defaultDocumentContent(kind) : current;
    });
  };

  const save = async () => {
    if (isLocked) {
      setError("Phiếu đã hủy và được khóa để bảo toàn lịch sử.");
      return;
    }
    if (!isWarehouseSale && !voucherNo.trim()) {
      setError("Vui lòng nhập số phiếu.");
      return;
    }
    setSaving(true);
    setError("");
    setSuccess("");
    if (beforeSave && !(await beforeSave(lines))) {
      setSaving(false);
      return;
    }
    const body = {
      voucherNo: voucherNo.trim(),
      documentType: docKind,
      date: docDate,
      customerName,
      content: content.trim() || defaultDocumentContent(docKind),
      note,
      lines,
    };
    try {
      if (id === "new") await api.post(apiBasePath, body);
      else await api.put(`${apiBasePath}/${id}`, body);

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

  const title =
    id === "new" ? `Tạo ${documentKindLabel(docKind).toLowerCase()}` : isLocked ? "Xem phiếu đã hủy" : "Sửa phiếu";
  const footer = (
    <>
      <Button variant="ghost" onClick={onClose}>{isLocked ? "Đóng" : "Hủy"}</Button>
      {!isLocked && id !== "new" && onPrint && (
        <Button variant="soft" onClick={onPrint} loading={printLoading}>
          <Eye className="h-4 w-4" /> Xem trước &amp; in
        </Button>
      )}
      {!isLocked && <Button onClick={save} loading={saving}>Lưu phiếu</Button>}
    </>
  );
  const body = (
    <fieldset disabled={isLocked} className="space-y-4">
        {isLocked && (
          <div className="rounded-xl border border-rose-500/25 bg-rose-500/10 px-3.5 py-3 text-sm font-semibold text-rose-600 dark:text-rose-300">
            Phiếu đã hủy và chỉ được phép xem.
            {cancelReason ? <span className="mt-1 block font-medium">Lý do: {cancelReason}</span> : null}
          </div>
        )}
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Field label="Loại phiếu">
            {allowedKinds.length === 1 ? (
              <div className="km-form-control flex min-h-[42px] items-center rounded-xl border px-3.5 py-2.5 text-sm font-semibold text-[var(--text)]">
                {documentKindLabel(allowedKinds[0])}
              </div>
            ) : (
              <Select value={docKind} onChange={(e) => changeKind(e.target.value as DocumentKind)} className="w-full">
                {allowedKinds.includes("receipt") && <option value="receipt">Phiếu thu</option>}
                {allowedKinds.includes("payment") && <option value="payment">Phiếu chi</option>}
                {allowedKinds.includes("document") && <option value="document">Phiếu xuất kho bán hàng</option>}
              </Select>
            )}
          </Field>
          {isWarehouseSale ? (
            <Field label="Số phiếu">
              <div className="km-form-control flex min-h-[42px] items-center rounded-xl border px-3.5 py-2.5 text-sm">
                {voucherNo ? (
                  <span className="font-semibold text-[var(--text)]">{voucherNo}</span>
                ) : (
                  <span className="text-[var(--text-muted)]">Sẽ nhập khi bấm Xem trước &amp; in</span>
                )}
              </div>
            </Field>
          ) : (
            <Field label="Số phiếu *">
              <Input
                value={voucherNo}
                onChange={(e) => setVoucherNo(e.target.value)}
                placeholder={docKind === "receipt" ? "VD: PT260625-0930" : "VD: PC260625-0930"}
              />
            </Field>
          )}
          <Field label="Ngày lập">
            <DatePicker value={docDate} onChange={setDocDate} ariaLabel="Ngày lập" />
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
                  <th className="px-2 py-2 text-left font-semibold">Quy cách</th>
                  <th className="px-2 py-2 text-right font-semibold">{isWarehouseSale ? "Khối lượng (kg)" : "SL"}</th>
                  <th className="px-2 py-2 text-right font-semibold">Đơn giá</th>
                  <th className="px-2 py-2 text-right font-semibold">Thành tiền</th>
                  <th className="w-8" />
                </tr>
              </thead>
              <tbody>
                {lines.map((l, i) => (
                  <tr key={i} className="border-b border-[var(--glass-border)]/40">
                    <td className="p-1">
                      <ProductCellInput
                        value={l.lineContent}
                        onChange={(v) => setLine(i, { lineContent: v, productId: null })}
                        onPick={(p) => setLine(i, { lineContent: p.name, spec: p.spec, productId: p.id })}
                      />
                    </td>
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
    </fieldset>
  );

  // Khung ngoài do người gọi quyết định: mặc định là hộp thoại riêng, còn màn Phiếu gộp thì nhét
  // thân form vào tab của hộp thoại lớn.
  if (renderShell) return <>{renderShell({ title, body, footer })}</>;
  return (
    <Modal open onClose={onClose} wide solid title={title} footer={footer}>
      {body}
    </Modal>
  );
}

export function CellInput({
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

export function FormulaNumberInput({
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

  // Ô số bám theo `value` do bên ngoài đổi (vd đổi loại chứng từ, nạp lại phiếu) — nhưng TUYỆT ĐỐI
  // không đè lên khi người dùng đang gõ dở. Làm lúc render thay vì trong effect nên con số mới hiện
  // ngay trong cùng một khung hình, không nhấp nháy giá trị cũ.
  // (Chuẩn hoá lúc rời ô đã do onBlur → commitDraft lo, nên ở đây chỉ cần theo dõi `value`.)
  const [syncedValue, setSyncedValue] = useState(value);
  if (!focused && value !== syncedValue) {
    setSyncedValue(value);
    setDraft(formatEditableNumber(value));
  }

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
      title="Dấu phẩy ngăn cách hàng nghìn; có thể nhập phép tính, ví dụ 1,000*2"
      onFocus={() => {
        setFocused(true);
        setInvalid(false);
      }}
      onChange={(e) => setDraft(formatArithmeticDraft(e.target.value))}
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
