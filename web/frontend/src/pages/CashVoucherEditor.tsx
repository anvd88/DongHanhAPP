import { useEffect, useMemo, useState } from "react";
import {
  ArrowDownLeft,
  ArrowUpRight,
  Banknote,
  Eye,
  FileText,
  Loader2,
  ReceiptText,
  UserRound,
} from "lucide-react";
import { DatePicker } from "../components/DateField";
import { Modal } from "../components/Modal";
import { Button, Field, Input } from "../components/ui";
import { api } from "../lib/api";
import { createVoucherNo, inferDocumentKind, type DocumentKind } from "../lib/documents";
import { money } from "../lib/format";
import type { Customer, DocumentDetail } from "../lib/types";

const currentDate = () => new Date().toISOString().slice(0, 10);
const cashReason = (kind: DocumentKind) => (kind === "receipt" ? "Thu tiền" : "Chi tiền");

const numericValue = (value: string) => Number(value.replace(/[^\d]/g, "")) || 0;

export function CashVoucherEditor({
  id,
  initialKind,
  customers,
  onPrint,
  printLoading,
  keepOpenAfterSave,
  readOnly = false,
  onClose,
  onSaved,
}: {
  id: string | "new";
  initialKind: DocumentKind;
  customers: Customer[];
  onPrint?: () => void;
  printLoading?: boolean;
  keepOpenAfterSave?: boolean;
  readOnly?: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [kind, setKind] = useState<DocumentKind>(initialKind);
  const [voucherNo, setVoucherNo] = useState(() => createVoucherNo(initialKind));
  const [voucherDate, setVoucherDate] = useState(currentDate);
  const [partyName, setPartyName] = useState("");
  const [reason, setReason] = useState(() => cashReason(initialKind));
  const [amountDraft, setAmountDraft] = useState("");
  const [note, setNote] = useState("");
  const [cancelledAt, setCancelledAt] = useState<string | null>(readOnly ? "locked" : null);
  const [cancelReason, setCancelReason] = useState("");
  const [loading, setLoading] = useState(id !== "new");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  useEffect(() => {
    if (id === "new") return;
    let cancelled = false;
    setLoading(true);
    api.get<DocumentDetail>(`/api/cash-vouchers/${id}`)
      .then((detail) => {
        if (cancelled) return;
        const loadedKind = inferDocumentKind(detail);
        const total = detail.lines.reduce(
          (sum, line) => sum + (line.quantity || 0) * (line.unitPrice || 0),
          0,
        );
        setKind(loadedKind);
        setVoucherNo(detail.voucherNo);
        setVoucherDate(detail.date);
        setPartyName(detail.customerName);
        setReason(detail.content);
        setAmountDraft(total > 0 ? String(Math.round(total)) : "");
        setNote(detail.note);
        setCancelledAt(detail.cancelledAt ?? null);
        setCancelReason(detail.cancelReason ?? "");
      })
      .catch((cause) => {
        if (!cancelled) setError(cause instanceof Error ? cause.message : "Không tải được phiếu.");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [id]);

  const amount = useMemo(() => numericValue(amountDraft), [amountDraft]);
  const isReceipt = kind === "receipt";
  const isLocked = readOnly || !!cancelledAt;

  const changeKind = (nextKind: DocumentKind) => {
    if (nextKind === kind || nextKind === "document") return;
    setKind(nextKind);
    setVoucherNo(createVoucherNo(nextKind));
    if (!reason.trim() || reason === cashReason(kind)) setReason(cashReason(nextKind));
    setError("");
  };

  const resetCreateForm = () => {
    setVoucherNo(createVoucherNo(kind));
    setVoucherDate(currentDate());
    setPartyName("");
    setReason(cashReason(kind));
    setAmountDraft("");
    setNote("");
    setError("");
  };

  const save = async () => {
    if (isLocked) return;
    if (!voucherNo.trim()) {
      setError("Vui lòng nhập số phiếu.");
      return;
    }
    if (!voucherDate) {
      setError("Vui lòng chọn ngày lập phiếu.");
      return;
    }
    if (!reason.trim()) {
      setError(`Vui lòng nhập lý do ${isReceipt ? "thu" : "chi"}.`);
      return;
    }
    if (amount <= 0) {
      setError("Số tiền phải lớn hơn 0.");
      return;
    }

    setSaving(true);
    setError("");
    setSuccess("");
    const body = {
      voucherNo: voucherNo.trim(),
      documentType: kind,
      date: voucherDate,
      customerName: partyName.trim(),
      content: reason.trim(),
      note: note.trim(),
      lines: [
        {
          lineContent: reason.trim(),
          spec: "",
          quantity: 1,
          unitPrice: amount,
          note: note.trim(),
        },
      ],
    };

    try {
      if (id === "new") await api.post("/api/cash-vouchers", body);
      else await api.put(`/api/cash-vouchers/${id}`, body);

      if (id === "new" && keepOpenAfterSave) {
        resetCreateForm();
        setSuccess(`Đã lưu ${isReceipt ? "phiếu thu" : "phiếu chi"}. Có thể nhập phiếu tiếp theo.`);
        onSaved();
        return;
      }
      onSaved();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Không lưu được phiếu.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      wide
      panel
      className="cv-modal"
      title={id === "new" ? (isReceipt ? "Lập phiếu thu" : "Lập phiếu chi") : isLocked ? "Xem phiếu đã hủy" : (isReceipt ? "Sửa phiếu thu" : "Sửa phiếu chi")}
      onClose={() => !saving && onClose()}
      footer={
        <>
          <Button variant="ghost" disabled={saving} onClick={onClose}>
            {isLocked ? "Đóng" : "Hủy"}
          </Button>
          {!isLocked && id !== "new" && onPrint && (
            <Button variant="soft" loading={printLoading} onClick={onPrint}>
              <Eye className="h-4 w-4" />
              Xem trước &amp; in
            </Button>
          )}
          {!isLocked && (
            <Button loading={saving} onClick={() => void save()}>
              {isReceipt ? <ArrowDownLeft className="h-4 w-4" /> : <ArrowUpRight className="h-4 w-4" />}
              Lưu {isReceipt ? "phiếu thu" : "phiếu chi"}
            </Button>
          )}
        </>
      }
    >
      {loading ? (
        <div className="grid min-h-[390px] place-items-center text-[var(--gc-text-muted)]">
          <Loader2 className="h-6 w-6 animate-spin" />
        </div>
      ) : (
        <fieldset disabled={isLocked} className="space-y-4">
          {isLocked && (
            <div className="rounded-xl border border-rose-500/25 bg-rose-500/10 px-3.5 py-3 text-sm font-semibold text-rose-600 dark:text-rose-300">
              Phiếu đã hủy và chỉ được phép xem.
              {cancelReason && <span className="mt-1 block font-medium">Lý do: {cancelReason}</span>}
            </div>
          )}

          <div className="cv-kind-switch" role="tablist" aria-label="Chọn loại phiếu">
            <button
              type="button"
              role="tab"
              aria-selected={isReceipt}
              className={isReceipt ? "is-active is-receipt" : ""}
              onClick={() => changeKind("receipt")}
            >
              <span><ArrowDownLeft className="h-5 w-5" /></span>
              <div><strong>Phiếu thu</strong><small>Tiền vào quỹ</small></div>
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={!isReceipt}
              className={!isReceipt ? "is-active is-payment" : ""}
              onClick={() => changeKind("payment")}
            >
              <span><ArrowUpRight className="h-5 w-5" /></span>
              <div><strong>Phiếu chi</strong><small>Tiền ra khỏi quỹ</small></div>
            </button>
          </div>

          <div className="grid gap-4 lg:grid-cols-[minmax(0,1.25fr)_minmax(260px,0.75fr)]">
            <div className="cv-form-panel space-y-4">
              <div className="grid gap-3 sm:grid-cols-2">
                <Field label="Số phiếu *">
                  <div className="relative">
                    <ReceiptText className="cv-field-icon" />
                    <Input
                      value={voucherNo}
                      maxLength={64}
                      onChange={(event) => setVoucherNo(event.target.value)}
                      className="pl-10 font-bold"
                      placeholder={isReceipt ? "VD: PT260729-1015" : "VD: PC260729-1015"}
                    />
                  </div>
                </Field>
                <Field label="Ngày lập *">
                  <DatePicker value={voucherDate} onChange={setVoucherDate} ariaLabel="Ngày lập phiếu" />
                </Field>
              </div>

              <Field label={isReceipt ? "Người nộp tiền" : "Người nhận tiền"}>
                <div className="relative">
                  <UserRound className="cv-field-icon" />
                  <Input
                    list="cash-voucher-customer-list"
                    value={partyName}
                    onChange={(event) => setPartyName(event.target.value)}
                    className="pl-10"
                    placeholder={isReceipt ? "Nhập người nộp tiền" : "Nhập người nhận tiền"}
                  />
                  <datalist id="cash-voucher-customer-list">
                    {customers.map((customer) => <option key={customer.id} value={customer.name} />)}
                  </datalist>
                </div>
              </Field>

              <Field label={isReceipt ? "Lý do thu *" : "Lý do chi *"}>
                <div className="relative">
                  <FileText className="cv-field-icon" />
                  <Input
                    value={reason}
                    maxLength={1000}
                    onChange={(event) => setReason(event.target.value)}
                    className="pl-10"
                    placeholder={isReceipt ? "Ví dụ: Thu tiền hàng tháng 7" : "Ví dụ: Chi thanh toán nhà cung cấp"}
                  />
                </div>
              </Field>

              <Field label="Ghi chú / chứng từ kèm theo">
                <Input
                  value={note}
                  maxLength={1000}
                  onChange={(event) => setNote(event.target.value)}
                  placeholder="Số hóa đơn, phương thức thanh toán hoặc ghi chú nội bộ"
                />
              </Field>
            </div>

            <div className={`cv-amount-panel ${isReceipt ? "is-receipt" : "is-payment"}`}>
              <span className="cv-amount-icon"><Banknote className="h-6 w-6" /></span>
              <p>Số tiền {isReceipt ? "thu" : "chi"}</p>
              <div className="cv-amount-input">
                <input
                  autoFocus={id === "new"}
                  inputMode="numeric"
                  value={amountDraft ? money(numericValue(amountDraft)) : ""}
                  onChange={(event) => {
                    setAmountDraft(event.target.value.replace(/[^\d]/g, ""));
                    if (error) setError("");
                  }}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") void save();
                  }}
                  placeholder="0"
                  aria-label={`Số tiền ${isReceipt ? "thu" : "chi"}`}
                />
                <strong>₫</strong>
              </div>
              <div className="cv-amount-summary">
                <span>{isReceipt ? "Ghi tăng quỹ" : "Ghi giảm quỹ"}</span>
                <strong>{money(amount)} ₫</strong>
              </div>
              <p className="cv-amount-hint">
                {isReceipt
                  ? "Khoản tiền sẽ được cộng vào tổng thu của kỳ."
                  : "Khoản tiền sẽ được cộng vào tổng chi của kỳ."}
              </p>
            </div>
          </div>

          {success && (
            <div className="rounded-xl bg-emerald-500/10 px-3 py-2.5 text-sm font-semibold text-emerald-700 dark:text-emerald-300">
              {success}
            </div>
          )}
          {error && (
            <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-semibold text-rose-600 dark:text-rose-300">
              {error}
            </div>
          )}
        </fieldset>
      )}
    </Modal>
  );
}
