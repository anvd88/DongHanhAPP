import { useEffect, useMemo, useState } from "react";
import { PackageX, Search, Trash2, Undo2 } from "lucide-react";
import { Button, Field, Input, Spinner } from "./ui";
import { Modal } from "./Modal";
import { DatePicker } from "./DateField";
import { useAppNotifications } from "./app-notifications-context";
import { api } from "../lib/api";
import { money, num } from "../lib/format";
import type { GoodsReturnResult, ReturnSourceLine } from "../lib/types";

/**
 * NHẬN HÀNG KHÁCH TRẢ VỀ.
 *
 * Bài toán thật: lái xe chở về một xe hàng lẫn lộn — khách không nhận, hoặc trả lại một phần. Kế
 * toán phải cân từng loại rồi truy xem món đó thuộc ĐƠN NÀO mới lấy được đúng đơn giá đã bán để
 * trừ công nợ. Hệ thống không có bảng giá, cùng một mặt hàng mỗi đơn một giá, nên chọn đơn nguồn
 * là bước BẮT BUỘC chứ không phải tuỳ chọn.
 *
 * Vì vậy màn này KHÔNG cho gõ tay tên hàng: kế toán chọn thẳng từ các dòng đã bán cho khách này
 * (đơn vừa giao xếp lên đầu), rồi chỉ nhập số cân thực nhận về. Gõ tay tên hàng là mở đường cho
 * hàng "trên trời" và giá tự chế.
 *
 * Mỗi dòng hiện rõ nó sẽ được ghi bằng đường nào — hạ số trên phiếu chưa chốt, hay vào phiếu trả
 * hàng — để kế toán thấy trước khi lưu, không phải đoán.
 */
export function GoodsReturnModal({
  open,
  onClose,
  customerId,
  customerName,
  contextDocumentId,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  customerId?: string | null;
  customerName: string;
  /** Phiếu đang mở trên màn hình — dòng trả về chính phiếu này (nếu chưa chốt) sẽ hạ số tại chỗ. */
  contextDocumentId?: string;
  onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const [sources, setSources] = useState<ReturnSourceLine[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [picked, setPicked] = useState<PickedLine[]>([]);
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [reason, setReason] = useState("");
  const [note, setNote] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setLoading(true);
    const timer = setTimeout(() => {
      const params = new URLSearchParams();
      if (customerId) params.set("customerId", customerId);
      else params.set("customerName", customerName);
      if (keyword.trim()) params.set("q", keyword.trim());
      if (contextDocumentId) params.set("preferDocumentId", contextDocumentId);
      api
        .get<{ items: ReturnSourceLine[] }>(`/api/returns/sources?${params}`)
        .then((res) => {
          if (!cancelled) setSources(res.items);
        })
        .catch(() => {
          if (!cancelled) setSources([]);
        })
        .finally(() => {
          if (!cancelled) setLoading(false);
        });
    }, keyword ? 300 : 0);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [open, customerId, customerName, keyword, contextDocumentId]);

  // Mở lại cho phiếu khác thì phải sạch: giữ lại dòng đã chọn của phiếu trước là trừ nhầm công nợ.
  useEffect(() => {
    if (!open) return;
    setPicked([]);
    setReason("");
    setNote("");
    setError("");
    setKeyword("");
  }, [open, contextDocumentId]);

  const keyOf = (line: ReturnSourceLine) => `${line.documentId}#${line.lineNo}`;
  const total = useMemo(
    () => picked.reduce((sum, p) => sum + (p.quantity || 0) * p.source.unitPrice, 0),
    [picked],
  );

  const add = (line: ReturnSourceLine) => {
    if (picked.some((p) => keyOf(p.source) === keyOf(line))) return;
    setPicked((arr) => [...arr, { source: line, quantity: line.remaining }]);
  };

  const save = async () => {
    if (picked.length === 0) {
      setError("Chưa chọn dòng hàng nào để trả.");
      return;
    }
    if (!reason.trim()) {
      setError("Vui lòng nhập lý do khách trả hàng.");
      return;
    }
    const bad = picked.find((p) => !(p.quantity > 0) || p.quantity > p.source.remaining);
    if (bad) {
      setError(
        `${bad.source.content}: số cân thực nhận phải lớn hơn 0 và không quá ${num(bad.source.remaining)}.`,
      );
      return;
    }
    setSaving(true);
    setError("");
    try {
      const result = await api.post<GoodsReturnResult>("/api/returns", {
        date,
        reason: reason.trim(),
        note: note.trim(),
        contextDocumentId,
        lines: picked.map((p) => ({
          sourceDocumentId: p.source.documentId,
          sourceLineNo: p.source.lineNo,
          quantity: p.quantity,
        })),
      });
      const parts: string[] = [];
      if (result.returnNo) parts.push(`phiếu trả ${result.returnNo} (${money(result.returnTotal)} ₫)`);
      if (result.adjustedLines > 0) parts.push(`hạ số ${result.adjustedLines} dòng trên phiếu chưa chốt`);
      notify.success(`Đã nhận hàng trả về: ${parts.join(", ")}.`, "Hàng trả về");
      onSaved();
      onClose();
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : "Không ghi được hàng trả về.";
      setError(message);
      notify.error(message);
    } finally {
      setSaving(false);
    }
  };

  const pickedKeys = new Set(picked.map((p) => keyOf(p.source)));

  return (
    <Modal
      open={open}
      onClose={onClose}
      wide
      solid
      title={`Nhận hàng trả về · ${customerName || "khách lẻ"}`}
      footer={
        <>
          <div className="mr-auto text-sm font-bold">
            Trừ công nợ: <span className="text-lg font-black">{money(total)} ₫</span>
          </div>
          <Button variant="ghost" onClick={onClose}>
            Đóng
          </Button>
          <Button loading={saving} onClick={() => void save()}>
            <Undo2 className="h-4 w-4" /> Ghi hàng trả về
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {/* ── Đã chọn: bảng kê hàng thực nhận về ────────────────────────────── */}
        <div>
          <div className="mb-2 text-xs font-bold text-[var(--gc-text-muted)]">
            Hàng thực nhận về ({picked.length} dòng)
          </div>
          {picked.length === 0 ? (
            <p className="rounded-xl border border-dashed border-[var(--gc-border)] p-4 text-sm font-semibold text-[var(--gc-text-muted)]">
              Chọn dòng hàng ở danh sách bên dưới — mỗi dòng gắn với đúng đơn đã bán, đơn giá lấy theo
              đơn đó.
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="gc-table w-full text-sm">
                <thead>
                  <tr>
                    <th>Hàng hoá</th>
                    <th>Đơn nguồn</th>
                    <th className="text-right">Đơn giá</th>
                    <th className="text-right">Số cân thực nhận</th>
                    <th className="text-right">Thành tiền</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {picked.map((p, index) => {
                    const inPlace = !p.source.settled && p.source.documentId === contextDocumentId;
                    return (
                      <tr key={keyOf(p.source)}>
                        <td>
                          <div className="font-bold">{p.source.content || "(không tên)"}</div>
                          {p.source.spec && (
                            <div className="text-xs text-[var(--gc-text-muted)]">{p.source.spec}</div>
                          )}
                        </td>
                        <td>
                          <div className="font-bold">{p.source.voucherNo}</div>
                          <div className="text-xs text-[var(--gc-text-muted)]">
                            {inPlace ? "→ hạ số ngay trên phiếu này" : "→ ghi phiếu trả hàng"}
                          </div>
                        </td>
                        <td className="text-right font-semibold">{money(p.source.unitPrice)}</td>
                        <td className="text-right">
                          <Input
                            className="w-28 text-right"
                            type="number"
                            step="0.01"
                            min={0}
                            max={p.source.remaining}
                            value={p.quantity}
                            onChange={(event) =>
                              setPicked((arr) =>
                                arr.map((item, i) =>
                                  i === index ? { ...item, quantity: Number(event.target.value) } : item,
                                ),
                              )
                            }
                          />
                          <div className="text-xs text-[var(--gc-text-muted)]">
                            còn {num(p.source.remaining)}
                          </div>
                        </td>
                        <td className="text-right font-black">
                          {money((p.quantity || 0) * p.source.unitPrice)}
                        </td>
                        <td>
                          <button
                            type="button"
                            className="gc-icon-btn h-7 w-7 text-rose-500"
                            aria-label="Bỏ dòng này"
                            onClick={() => setPicked((arr) => arr.filter((_, i) => i !== index))}
                          >
                            <Trash2 className="h-3.5 w-3.5" />
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Ngày nhận hàng về">
            <DatePicker value={date} onChange={setDate} />
          </Field>
          <Field label="Lý do khách trả *">
            <Input
              value={reason}
              maxLength={500}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Ví dụ: khách không nhận, hàng sai quy cách"
            />
          </Field>
        </div>
        <Field label="Ghi chú">
          <Input value={note} maxLength={1000} onChange={(event) => setNote(event.target.value)} />
        </Field>

        {error && (
          <div className="rounded-xl bg-rose-500/10 p-3 text-sm font-semibold text-rose-600 dark:text-rose-400">
            {error}
          </div>
        )}

        {/* ── Tra cứu: món này nằm ở đơn nào ────────────────────────────────── */}
        <div className="rounded-2xl border border-[var(--gc-border)] p-3">
          <div className="mb-2 flex items-center gap-2">
            <Search className="h-4 w-4 shrink-0 opacity-60" />
            <Input
              className="w-full"
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              placeholder="Tìm theo chủng loại, quy cách hoặc số phiếu…"
            />
          </div>
          {loading ? (
            <div className="flex justify-center py-6">
              <Spinner />
            </div>
          ) : sources.length === 0 ? (
            <p className="py-4 text-center text-sm font-semibold text-[var(--gc-text-muted)]">
              {keyword
                ? "Không có dòng hàng nào khớp."
                : "Khách này chưa có phiếu xuất kho nào còn hàng để trả."}
            </p>
          ) : (
            <ul className="gc-scroll max-h-64 space-y-1.5 overflow-auto pr-1">
              {sources.map((line) => {
                const key = `${line.documentId}#${line.lineNo}`;
                const isContext = line.documentId === contextDocumentId;
                return (
                  <li key={key}>
                    <button
                      type="button"
                      disabled={pickedKeys.has(key)}
                      onClick={() => add(line)}
                      className="flex w-full items-center gap-3 rounded-xl border border-[var(--gc-border)] p-2.5 text-left transition hover:border-[var(--gc-accent)] disabled:opacity-40"
                    >
                      <PackageX className="h-4 w-4 shrink-0 opacity-60" />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-bold">
                          {line.content || "(không tên)"}
                          {line.spec ? ` · ${line.spec}` : ""}
                        </span>
                        <span className="block text-xs font-semibold text-[var(--gc-text-muted)]">
                          {line.voucherNo} · {line.docDate?.slice(0, 10).split("-").reverse().join("/")} ·
                          đã bán {num(line.quantity)} × {money(line.unitPrice)} ₫
                          {line.returnedQuantity > 0 ? ` · đã trả ${num(line.returnedQuantity)}` : ""}
                          {isContext ? " · đơn vừa giao" : ""}
                        </span>
                      </span>
                      <span className="shrink-0 text-right text-xs font-black">
                        còn {num(line.remaining)}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      </div>
    </Modal>
  );
}

interface PickedLine {
  source: ReturnSourceLine;
  quantity: number;
}
