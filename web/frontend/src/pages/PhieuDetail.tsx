import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ArrowLeft,
  Ban,
  Check,
  ClipboardCheck,
  Eye,
  FileText,
  Loader2,
  PackageCheck,
  PackageX,
  Plus,
  Printer,
  Trash2,
  Truck,
  Undo2,
} from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { ActionProgressButton } from "../components/ActionProgressButton";
import { Button, Field, Input, Spinner, buttonClasses, buttonInlineStyle } from "../components/ui";
import { Modal } from "../components/Modal";
import { DatePicker } from "../components/DateField";
import { CellInput, FormulaNumberInput } from "./DocumentEditor";
import { ProductCellInput } from "../components/ProductCellInput";
import { DeliveryAssignPanel, taskStatusText } from "../components/DeliveryAssignPanel";
import { GoodsReturnModal } from "../components/GoodsReturnModal";
import { useRailSlot } from "../components/rail-slot";
import { PhieuRail } from "./PhieuRail";
import { useAppNotifications } from "../components/app-notifications-context";
import { api } from "../lib/api";
import { date as fmtDate, dateTime, money, num } from "../lib/format";
import type { Customer, DocumentDetail, DocumentLine, GoodsReturn, SettlementResult } from "../lib/types";
import "./phieu-detail.css";
import "../features/giacong/giacong.css";

const emptyLine = (): DocumentLine => ({ lineContent: "", spec: "", quantity: 1, unitPrice: 0, note: "" });

/**
 * TRANG PHIẾU XUẤT KHO — một trang đầy đủ, không phải hộp thoại.
 *
 * Vì sao là trang chứ không phải popup: một tờ phiếu đi qua năm chặng (lập → in → giao cho lái xe →
 * lái xe giao → phiếu giấy về kho). Nhồi cả năm chặng vào một hộp thoại thì lúc nào cũng phải cuộn
 * và không nhìn ra đang ở đâu. Trang riêng cho phép bày theo TRÌNH TỰ:
 *
 *   • Thanh tiến trình ở đầu trang: liếc một cái là biết phiếu đang ở chặng nào, ai làm, lúc nào.
 *   • Thẻ "Việc cần làm ngay" ngay dưới đó: chỉ hiện ĐÚNG một hành động của chặng hiện tại.
 *   • Cột trái = số liệu (thông tin phiếu + bảng hàng hoá, có luôn cột chênh lệch so với bản in).
 *   • Cột phải = quy trình (giao hàng, nhật ký phiếu).
 *
 * Bảng hàng hoá là bảng DUY NHẤT: số trong đó chính là hàng khách nhận thực tế. Không có bảng nhập
 * thứ hai cho "đối soát" — hai bảng nhập cho một tờ phiếu là mời gọi sai lệch.
 */
export function PhieuDetail() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const { notify } = useAppNotifications();

  const [doc, setDoc] = useState<DocumentDetail | null>(null);
  const [settlement, setSettlement] = useState<SettlementResult | null>(null);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");

  // Form
  const [voucherNo, setVoucherNo] = useState("");
  const [docDate, setDocDate] = useState("");
  const [customerName, setCustomerName] = useState("");
  const [content, setContent] = useState("");
  const [note, setNote] = useState("");
  const [lines, setLines] = useState<DocumentLine[]>([emptyLine()]);
  const [reason, setReason] = useState("");
  const [reasonNeeded, setReasonNeeded] = useState(false);
  const [returnNote, setReturnNote] = useState("");
  const [returnOpen, setReturnOpen] = useState(false);
  const [returns, setReturns] = useState<GoodsReturn[]>([]);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [cancelReason, setCancelReason] = useState("");
  // Tăng mỗi lần trang nạp lại → thẻ Giao hàng (tự giữ state riêng) đọc lại theo.
  const [reloadToken, setReloadToken] = useState(0);

  const applyDoc = useCallback((d: DocumentDetail) => {
    setDoc(d);
    setVoucherNo(d.voucherNo);
    setDocDate(d.date);
    setCustomerName(d.customerName);
    setContent(d.content);
    setNote(d.note);
    setLines(d.lines.length ? d.lines : [emptyLine()]);
  }, []);

  const loadAll = useCallback(async () => {
    setReloadToken((n) => n + 1);
    const detail = await api.get<DocumentDetail>(`/api/documents/${id}`);
    applyDoc(detail);
    if (detail.issuedAt) {
      try {
        setSettlement(await api.get<SettlementResult>(`/api/documents/${id}/settlement`));
      } catch {
        /* Không có phần đối soát thì trang vẫn phải sửa được phiếu. */
      }
      try {
        const list = await api.get<{ items: GoodsReturn[] }>(`/api/returns?sourceDocumentId=${id}`);
        setReturns(list.items);
      } catch {
        /* Không đọc được hàng trả về thì phần còn lại của trang vẫn dùng bình thường. */
      }
    }
  }, [id, applyDoc]);

  // Đổi phiếu KHÔNG còn dựng lại trang (xem pageKey ở Layout) — nhờ vậy hết giật, nhưng đổi lại
  // phải tự dọn dấu vết phiếu cũ. Không dọn thì lý do sửa / ghi chú nhận phiếu vừa gõ cho phiếu
  // trước còn dính lại, và phần đối soát của phiếu cũ hiện nhầm trong lúc phiếu mới đang tải.
  useEffect(() => {
    setSettlement(null);
    setReturns([]);
    setReturnOpen(false);
    setReason("");
    setReasonNeeded(false);
    setReturnNote("");
    setError("");
    document.querySelector(".km-page")?.scrollTo({ top: 0 });
  }, [id]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const [, list] = await Promise.all([loadAll(), api.get<Customer[]>("/api/customers")]);
        if (!cancelled) setCustomers(list);
      } catch (cause) {
        if (!cancelled) setError(cause instanceof Error ? cause.message : "Không tải được phiếu.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [loadAll]);

  // Rìa trái đổi từ menu sang XẤP PHIẾU của ngày đang xem: nhảy qua lại giữa các phiếu mà không
  // phải quay về danh sách. Ghi nhớ theo id + ngày, nếu không thanh bên vẽ lại ở mỗi lần render và
  // ô tìm kiếm bị dựng lại giữa chừng gõ.
  useRailSlot(
    useMemo(
      () => (doc ? <PhieuRail currentId={id} initialDate={doc.date} /> : null),
      [id, doc],
    ),
  );

  const issued = !!doc?.issuedAt;
  const cancelled = !!doc?.cancelledAt;
  const locked = cancelled;
  const total = lines.reduce((sum, line) => sum + (line.quantity || 0) * (line.unitPrice || 0), 0);
  const setLine = (index: number, patch: Partial<DocumentLine>) =>
    setLines((arr) => arr.map((line, i) => (i === index ? { ...line, ...patch } : line)));

  // Chênh lệch so với BẢN IN, khoá theo vị trí dòng (line_no = số thứ tự, bắt đầu từ 1).
  const snapshotOf = (index: number) => settlement?.lines.find((l) => l.lineNo === index + 1);
  const linesChanged = useMemo(
    () =>
      !!settlement &&
      settlement.lines.some((snap) => {
        const line = lines[snap.lineNo - 1];
        return line && (Number(line.quantity) !== snap.quantity || Number(line.unitPrice) !== snap.unitPrice);
      }),
    [settlement, lines],
  );

  const issuedTotal = settlement?.totals.issuedTotal ?? 0;
  const step = currentStep(doc, settlement);

  // ── Hành động ──────────────────────────────────────────────────────────────
  /** Trả về true/false để nút gọi nó biết việc chạy trót lọt hay không (vd. nút In lại có hiệu ứng). */
  const run = async (key: string, action: () => Promise<unknown>, ok: string) => {
    setBusy(key);
    setError("");
    try {
      await action();
      await loadAll();
      notify.success(ok, "Phiếu xuất kho");
      return true;
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : "Không thực hiện được.";
      setError(message);
      notify.error(message);
      return false;
    } finally {
      setBusy("");
    }
  };

  const save = async () => {
    if (locked) return;
    setSaving(true);
    setError("");
    try {
      // Phiếu ĐÃ PHÁT HÀNH mà đổi số lượng/đơn giá thì phải ghi vết cũ→mới TRƯỚC, vì PUT phiếu bên
      // dưới xoá sạch rồi chèn lại dòng nên tự nó không còn biết giá trị cũ là bao nhiêu.
      if (issued && settlement && linesChanged) {
        if (!reason.trim()) {
          setReasonNeeded(true);
          setSaving(false);
          notify.error("Phiếu đã phát hành: vui lòng nhập lý do sửa số lượng/đơn giá.");
          return;
        }
        const changed = settlement.lines
          .filter((snap) => {
            const line = lines[snap.lineNo - 1];
            return line && (Number(line.quantity) !== snap.quantity || Number(line.unitPrice) !== snap.unitPrice);
          })
          .map((snap) => ({
            lineNo: snap.lineNo,
            quantity: Number(lines[snap.lineNo - 1].quantity),
            unitPrice: Number(lines[snap.lineNo - 1].unitPrice),
          }));
        await api.put(`/api/documents/${id}/settlement`, { lines: changed, reason: reason.trim() });
      }

      await api.put(`/api/documents/${id}`, {
        voucherNo: voucherNo.trim(),
        documentType: "document",
        date: docDate,
        customerName,
        content: content.trim(),
        note,
        lines,
      });
      setReason("");
      setReasonNeeded(false);
      await loadAll();
      notify.success(`Đã lưu phiếu ${voucherNo || ""}.`.trim(), "Phiếu xuất kho");
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : "Không lưu được phiếu.";
      setError(message);
      notify.error(message);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="gc-root flex justify-center py-24">
        <Spinner />
      </div>
    );
  }

  if (!doc) {
    return (
      <div className="gc-root">
        <PageHeader title="Phiếu xuất kho" subtitle={error || "Không tìm thấy phiếu."} />
        <Button variant="ghost" onClick={() => navigate("/ban-hang")}>
          <ArrowLeft className="h-4 w-4" /> Về danh sách
        </Button>
      </div>
    );
  }

  const task = settlement?.task ?? null;

  return (
    <div className="gc-root pd-root">
      {/* ── Đầu trang: danh tính phiếu + hành động toàn phiếu ─────────────────── */}
      <div className="pd-topbar">
        <button type="button" className="gc-icon-btn h-9 w-9" onClick={() => navigate("/ban-hang")} aria-label="Về danh sách phiếu">
          <ArrowLeft className="h-4 w-4" />
        </button>
        <div className="min-w-0">
          <h1 className="pd-title">
            {doc.voucherNo ? `Phiếu ${doc.voucherNo}` : "Phiếu nháp"}
            {cancelled && <span className="pd-chip pd-chip--danger">Đã hủy</span>}
            {!cancelled && issued && <span className="pd-chip pd-chip--ok">Đã phát hành</span>}
            {!cancelled && !issued && <span className="pd-chip">Chưa in</span>}
          </h1>
          <p className="pd-sub">
            {doc.customerName || "Khách lẻ"} · {fmtDate(doc.date)} · {money(total)} ₫
          </p>
        </div>
        <div className="pd-topbar-actions">
          {issued && !cancelled && (
            <ActionProgressButton
              icon={Printer}
              idleLabel="In lại"
              busyLabel="Đang in..."
              doneLabel="Đã in"
              className={buttonClasses("soft")}
              style={buttonInlineStyle("soft")}
              onRun={async (report) => {
                // Hai mốc có thật: (1) máy chủ nhận xong lệnh in, (2) phiếu đã tải lại theo trạng
                // thái mới. Máy chủ không báo gì trong lúc dựng phiếu + đẩy máy in nên quãng đó
                // thanh vẫn ở chế độ chưa đo được, không chạy khống.
                const ok = await run(
                  "print",
                  async () => {
                    await api.post(`/api/documents/${id}/warehouse-print`, { voucherNo: doc.voucherNo });
                    report(1, 2);
                  },
                  "Đã gửi lệnh in.",
                );
                if (ok) report(2, 2);
                return ok;
              }}
            />
          )}
          {!cancelled && (
            <Button variant="ghost" onClick={() => { setCancelReason(""); setCancelOpen(true); }}>
              <Ban className="h-4 w-4" /> Hủy phiếu
            </Button>
          )}
          {!locked && (
            <Button loading={saving} onClick={() => void save()}>
              Lưu phiếu
            </Button>
          )}
        </div>
      </div>

      {/* ── Thanh tiến trình: liếc một cái là biết phiếu đang ở đâu ───────────── */}
      <Stepper doc={doc} settlement={settlement} current={step} />

      {/* ── Việc cần làm NGAY của chặng hiện tại ─────────────────────────────── */}
      {!cancelled && (
        <NextAction
          step={step}
          task={task}
          settlement={settlement}
          busy={busy}
          returnNote={returnNote}
          onReturnNote={setReturnNote}
          onReject={() => {
            if (!returnNote.trim()) {
              setError("Vui lòng nhập lý do trả lại chuyến.");
              return;
            }
            void run("reject", () => api.post(`/api/tasks/${task?.id}/reject`, { note: returnNote.trim() }), "Đã trả lại chuyến cho lái xe.");
          }}
          onReturn={() =>
            void run("return", () => api.post(`/api/documents/${id}/settlement/return`, { note: returnNote.trim() }), "Đã xác nhận phiếu về kho.")
          }
        />
      )}

      {error && <div className="pd-error">{error}</div>}

      <div className="pd-grid">
        {/* ── Cột trái: SỐ LIỆU ───────────────────────────────────────────────── */}
        <div className="pd-col">
          <GlassPanel className="pd-card">
            <h2 className="pd-card-title"><FileText className="h-4 w-4" /> Thông tin phiếu</h2>
            <div className="pd-fields">
              <Field label="Số phiếu">
                <Input value={voucherNo} disabled={issued || locked} onChange={(e) => setVoucherNo(e.target.value)} />
              </Field>
              <Field label="Ngày lập">
                <DatePicker value={docDate} onChange={setDocDate} />
              </Field>
              <Field label="Bên mua hàng">
                <Input
                  list="pd-customers"
                  value={customerName}
                  disabled={locked}
                  onChange={(e) => setCustomerName(e.target.value)}
                />
                <datalist id="pd-customers">
                  {customers.map((c) => (
                    <option key={c.id} value={c.name} />
                  ))}
                </datalist>
              </Field>
              <Field label="Thanh toán tiền">
                <Input value={content} disabled={locked} onChange={(e) => setContent(e.target.value)} />
              </Field>
              <Field label="Ghi chú">
                <Input value={note} disabled={locked} onChange={(e) => setNote(e.target.value)} />
              </Field>
            </div>
          </GlassPanel>

          <GlassPanel className="pd-card">
            <div className="pd-card-head">
              <h2 className="pd-card-title">
                <PackageCheck className="h-4 w-4" /> Hàng hoá
                {issued && <span className="pd-hint">Số trong bảng = hàng khách nhận thực tế</span>}
              </h2>
              {!locked && (
                <button type="button" className="pd-add-line" onClick={() => setLines((a) => [...a, emptyLine()])}>
                  <Plus className="h-4 w-4" /> Thêm dòng
                </button>
              )}
            </div>

            <div className="pd-table-wrap">
              <table className="pd-table">
                <thead>
                  <tr>
                    <th>Chủng loại hàng hoá</th>
                    <th>Quy cách</th>
                    {issued && <th className="pd-num pd-muted-col">SL xuất</th>}
                    <th className="pd-num">{issued ? "SL thực nhận" : "Khối lượng (kg)"}</th>
                    {issued && <th className="pd-num pd-muted-col">Đơn giá xuất</th>}
                    <th className="pd-num">{issued ? "Đơn giá thực" : "Đơn giá"}</th>
                    <th className="pd-num">Thành tiền</th>
                    {issued && <th className="pd-num">Chênh lệch</th>}
                    {!locked && <th aria-label="Xoá dòng" />}
                  </tr>
                </thead>
                <tbody>
                  {lines.map((line, i) => {
                    const snap = snapshotOf(i);
                    const amount = (line.quantity || 0) * (line.unitPrice || 0);
                    const qtyDiff = snap ? (line.quantity || 0) - snap.issuedQuantity : 0;
                    const amountDiff = snap ? amount - snap.issuedAmount : 0;
                    return (
                      <tr key={i}>
                        <td>
                          <ProductCellInput
                            value={line.lineContent}
                            onChange={(v) => setLine(i, { lineContent: v, productId: null })}
                            onPick={(p) => setLine(i, { lineContent: p.name, spec: p.spec, productId: p.id })}
                          />
                        </td>
                        <td><CellInput value={line.spec} onChange={(v) => setLine(i, { spec: v })} /></td>
                        {issued && (
                          <td className="pd-num pd-muted-col">{snap?.hasSnapshot ? num(snap.issuedQuantity) : "—"}</td>
                        )}
                        <td className="pd-num">
                          <FormulaNumberInput value={line.quantity} align="right" onChange={(v) => setLine(i, { quantity: v })} />
                        </td>
                        {issued && (
                          <td className="pd-num pd-muted-col">{snap?.hasSnapshot ? money(snap.issuedUnitPrice) : "—"}</td>
                        )}
                        <td className="pd-num">
                          <FormulaNumberInput value={line.unitPrice} align="right" onChange={(v) => setLine(i, { unitPrice: v })} />
                        </td>
                        <td className="pd-num pd-strong">{money(amount)}</td>
                        {issued && (
                          <td className="pd-num">
                            {snap?.hasSnapshot ? <Delta quantity={qtyDiff} amount={amountDiff} /> : "—"}
                          </td>
                        )}
                        {!locked && (
                          <td>
                            <button
                              type="button"
                              className="gc-icon-btn h-7 w-7 text-rose-500"
                              aria-label={`Xoá dòng ${i + 1}`}
                              onClick={() => setLines((a) => (a.length > 1 ? a.filter((_, j) => j !== i) : a))}
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </button>
                          </td>
                        )}
                      </tr>
                    );
                  })}
                </tbody>
                <tfoot>
                  <tr>
                    <td colSpan={issued ? 5 : 3} className="pd-num pd-strong">
                      {issued ? `Hàng xuất đi ${money(issuedTotal)} ₫` : "Tổng cộng"}
                    </td>
                    <td className="pd-num pd-total">{money(total)} ₫</td>
                    {issued && (
                      <td className="pd-num">
                        <Money value={total - issuedTotal} />
                      </td>
                    )}
                    {!locked && <td />}
                  </tr>
                </tfoot>
              </table>
            </div>

            {/* Lý do chỉ hỏi khi số liệu ĐÃ LỆCH so với tờ giấy khách đang giữ. */}
            {issued && !locked && linesChanged && (
              <div className={`pd-reason ${reasonNeeded ? "is-missing" : ""}`}>
                <label htmlFor="pd-reason">Lý do sửa số lượng / đơn giá *</label>
                <textarea
                  id="pd-reason"
                  maxLength={500}
                  value={reason}
                  onChange={(e) => {
                    setReason(e.target.value);
                    if (e.target.value.trim()) setReasonNeeded(false);
                  }}
                  placeholder="Ví dụ: cân lại tại kho khách thiếu 20kg; đơn giá viết nhầm 12.000 → 12.500"
                />
              </div>
            )}
          </GlassPanel>
        </div>

        {/* ── Cột phải: QUY TRÌNH ─────────────────────────────────────────────── */}
        <div className="pd-col">
          {issued && (
            <GlassPanel className="pd-card">
              <h2 className="pd-card-title"><Truck className="h-4 w-4" /> Giao hàng</h2>
              <DeliveryAssignPanel
                documentId={id}
                voucherNo={doc.voucherNo}
                customerName={doc.customerName}
                reloadToken={reloadToken}
                onSaved={() => void loadAll()}
              />
            </GlassPanel>
          )}

          {issued && !cancelled && (
            <GlassPanel className="pd-card">
              <h2 className="pd-card-title"><PackageX className="h-4 w-4" /> Hàng khách trả về</h2>
              <p className="pd-hint">
                Khách không nhận hoặc trả lại một phần. Kế toán cân từng loại rồi chọn đúng đơn đã bán
                — đơn giá lấy theo đơn đó nên công nợ trừ đúng số tiền.
              </p>
              {returns.length > 0 && (
                <ul className="pd-returns">
                  {returns.map((item) => (
                    <li key={item.id} className={item.cancelledAt ? "is-dead" : ""}>
                      <span>
                        <b>{item.voucherNo}</b> · {fmtDate(item.docDate)}
                        {item.cancelledAt ? " · đã hủy" : ""}
                      </span>
                      <b>{money(item.total)} ₫</b>
                    </li>
                  ))}
                </ul>
              )}
              <Button variant="soft" onClick={() => setReturnOpen(true)}>
                <PackageX className="h-4 w-4" /> Nhận hàng trả về
              </Button>
            </GlassPanel>
          )}

          <GlassPanel className="pd-card">
            <h2 className="pd-card-title"><ClipboardCheck className="h-4 w-4" /> Nhật ký phiếu</h2>
            <Journal settlement={settlement} />
          </GlassPanel>
        </div>
      </div>

      <GoodsReturnModal
        open={returnOpen}
        onClose={() => setReturnOpen(false)}
        customerName={doc.customerName}
        contextDocumentId={id}
        onSaved={() => void loadAll()}
      />

      {cancelOpen && (
        <Modal
          open
          solid
          title="Hủy phiếu xuất kho"
          onClose={() => setCancelOpen(false)}
          footer={
            <>
              <Button variant="ghost" onClick={() => setCancelOpen(false)}>Đóng</Button>
              <Button
                loading={busy === "cancel"}
                onClick={() =>
                  void run("cancel", () => api.put(`/api/documents/${id}/cancel`, { reason: cancelReason }), "Đã hủy phiếu.").then(() =>
                    setCancelOpen(false),
                  )
                }
              >
                Hủy phiếu
              </Button>
            </>
          }
        >
          <Field label="Lý do hủy">
            <Input value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} placeholder="Ví dụ: khách đổi đơn" />
          </Field>
        </Modal>
      )}
    </div>
  );
}

// ── Tiến trình ───────────────────────────────────────────────────────────────

type StepKey = "issue" | "assign" | "deliver" | "return" | "done";

/** Chặng phiếu ĐANG đứng — quyết định thẻ "việc cần làm ngay" hiện cái gì. */
function currentStep(doc: DocumentDetail | null, settlement: SettlementResult | null): StepKey {
  if (!doc?.issuedAt) return "issue";
  if (settlement?.flags.returned) return "done";
  const mode = settlement?.delivery.mode ?? "";
  if (!mode) return "assign";
  const status = settlement?.delivery.taskStatus ?? "";
  if (mode === "driver" && (status === "assigned" || status === "in_progress" || status === "rejected")) return "deliver";
  // 'submitted' = lái xe báo đã giao xong; việc còn lại chỉ là thu tờ phiếu ký nhận về kho.
  return "return";
}

// BỐN chặng, không còn "Nghiệm thu": khách nhận hàng rồi thì tờ phiếu có chữ ký quay về kho mới là
// bằng chứng, mà người nghiệm thu cũng chính là kế toán sắp bấm "phiếu đã về kho".
const STEPS: { key: StepKey; label: string }[] = [
  { key: "issue", label: "Phát hành" },
  { key: "assign", label: "Giao cho lái xe" },
  { key: "deliver", label: "Lái xe giao" },
  { key: "return", label: "Phiếu về kho" },
];

function Stepper({
  doc,
  settlement,
  current,
}: {
  doc: DocumentDetail;
  settlement: SettlementResult | null;
  current: StepKey;
}) {
  const order = STEPS.map((s) => s.key);
  const activeIndex = current === "done" ? STEPS.length : order.indexOf(current);
  const detail: Record<StepKey, string> = {
    issue: doc.issuedAt ? dateTime(doc.issuedAt) : "Chưa in phiếu",
    assign: settlement?.delivery.driverName || (settlement?.delivery.mode === "pickup" ? "Khách lấy tại kho" : "Chưa gán"),
    deliver: settlement?.delivery.taskNo ? taskStatusText(settlement.delivery.taskStatus) : "—",
    return: settlement?.flags.returned ? dateTime(settlement.delivery.returnedAt) : "Chưa nhận lại phiếu",
    done: "",
  };

  return (
    <ol className="pd-steps">
      {STEPS.map((s, i) => {
        const state = i < activeIndex ? "done" : i === activeIndex ? "now" : "todo";
        return (
          <li key={s.key} className={`pd-step is-${state}`}>
            <span className="pd-step-dot">{state === "done" ? <Check className="h-3.5 w-3.5" /> : i + 1}</span>
            <span className="pd-step-text">
              <b>{s.label}</b>
              <small>{detail[s.key]}</small>
            </span>
          </li>
        );
      })}
    </ol>
  );
}

/**
 * Chặng cuối chỉ còn MỘT thẻ và MỘT ô ghi chú. Không có bước "nghiệm thu" riêng nữa: khách đã nhận
 * hàng thì việc còn lại là thu tờ phiếu ký nhận. Nút phụ "Trả lại chuyến" dành cho tình huống lái
 * xe báo đã giao nhưng hàng phải quay đầu — lúc đó ô ghi chú thành LÝ DO bắt buộc.
 */
function NextAction({
  step,
  task,
  settlement,
  busy,
  returnNote,
  onReturnNote,
  onReject,
  onReturn,
}: {
  step: StepKey;
  task: SettlementResult["task"];
  settlement: SettlementResult | null;
  busy: string;
  returnNote: string;
  onReturnNote: (v: string) => void;
  onReject: () => void;
  onReturn: () => void;
}) {
  if (step === "return" && settlement?.flags.canConfirmReturn) {
    const submitted = settlement.delivery.taskStatus === "submitted";
    const canReject = submitted && !!task?.canReject;
    return (
      <div className="pd-next">
        <div className="pd-next-head">
          <PackageCheck className="h-4 w-4" />
          {submitted && task
            ? `Lái xe ${task.assigneeName} đã giao xong — thu lại tờ phiếu`
            : "Nhận lại tờ phiếu có chữ ký khách"}
        </div>
        {task?.submitNote && <p className="pd-next-note">Lái xe ghi: {task.submitNote}</p>}
        <p className="pd-next-note">
          Xác nhận xong là việc của lái xe khép lại ở “Đã hoàn thành”.
          {canReject && " Nếu thực ra hàng phải quay đầu thì bấm “Trả lại chuyến” kèm lý do."}
        </p>
        <textarea
          value={returnNote}
          maxLength={500}
          onChange={(e) => onReturnNote(e.target.value)}
          placeholder={canReject ? "Ghi chú khi nhận phiếu — hoặc lý do nếu trả lại chuyến" : "Ghi chú khi nhận phiếu (không bắt buộc)"}
        />
        <div className="pd-next-buttons">
          <Button loading={busy === "return"} onClick={onReturn}>
            <ClipboardCheck className="h-4 w-4" /> Xác nhận phiếu đã về kho
          </Button>
          {canReject && (
            <Button variant="ghost" loading={busy === "reject"} onClick={onReject}>
              <Undo2 className="h-4 w-4" /> Trả lại chuyến
            </Button>
          )}
        </div>
      </div>
    );
  }

  if (step === "issue") {
    return (
      <div className="pd-next pd-next--calm">
        <div className="pd-next-head"><Eye className="h-4 w-4" /> Phiếu chưa in</div>
        <p className="pd-next-note">In phiếu ở danh sách Bán hàng để phát hành; sau đó mới gán được lái xe.</p>
      </div>
    );
  }

  if (step === "assign") {
    return (
      <div className="pd-next pd-next--calm">
        <div className="pd-next-head"><Truck className="h-4 w-4" /> Chưa biết ai cầm tờ phiếu</div>
        <p className="pd-next-note">Chọn lái xe hoặc “khách lấy tại kho” ở thẻ Giao hàng bên phải.</p>
      </div>
    );
  }

  if (step === "done") {
    return (
      <div className="pd-next pd-next--done">
        <div className="pd-next-head"><PackageCheck className="h-4 w-4" /> Phiếu đã về kho — trọn vòng</div>
        <p className="pd-next-note">
          {dateTime(settlement?.delivery.returnedAt)}
          {settlement?.delivery.returnedBy ? ` · ${settlement.delivery.returnedBy}` : ""}
        </p>
      </div>
    );
  }

  return (
    <div className="pd-next pd-next--calm">
      <div className="pd-next-head"><Loader2 className="h-4 w-4" /> Đang chờ lái xe giao hàng</div>
      <p className="pd-next-note">
        Lái xe báo giao xong thì nút thu phiếu sẽ hiện ở đây. Xe hỏng hay lái xe bận thì vẫn đổi được
        người ở thẻ Giao hàng bên phải (phải nêu lý do).
      </p>
      {/* Lái xe quên bấm "đã giao" nhưng đã mang phiếu về tận nơi: đừng bắt phiếu kẹt lại chờ một
          nút mà người cầm phiếu đang đứng ngay trước mặt. */}
      {settlement?.flags.canConfirmReturn && (
        <div className="pd-next-buttons">
          <Button variant="ghost" loading={busy === "return"} onClick={onReturn}>
            <ClipboardCheck className="h-4 w-4" /> Lái xe đã mang phiếu về — đóng luôn
          </Button>
        </div>
      )}
    </div>
  );
}

/** Một dòng thời gian DUY NHẤT: việc của lái xe và các lần sửa số liệu trộn chung theo thời gian. */
function Journal({ settlement }: { settlement: SettlementResult | null }) {
  const entries = useMemo(() => {
    if (!settlement) return [];
    const fromTask = (settlement.task?.events ?? []).map((e) => ({
      id: `t${e.id}`,
      at: e.createdAt,
      who: e.actorName,
      text: e.note,
      kind: "task" as const,
    }));
    const fromEdits = settlement.history.map((h) => ({
      id: `e${h.id}`,
      at: h.createdAt,
      who: h.actorName || h.actorUsername,
      text:
        `${h.content || `Dòng ${h.lineNo}`}: ` +
        [
          h.oldQuantity !== h.newQuantity ? `SL ${num(h.oldQuantity)} → ${num(h.newQuantity)}` : "",
          h.oldUnitPrice !== h.newUnitPrice ? `đơn giá ${money(h.oldUnitPrice)} → ${money(h.newUnitPrice)}` : "",
        ]
          .filter(Boolean)
          .join(", ") + (h.reason ? ` — ${h.reason}` : ""),
      kind: "edit" as const,
    }));
    return [...fromTask, ...fromEdits].sort((a, b) => a.at.localeCompare(b.at));
  }, [settlement]);

  if (entries.length === 0) {
    return <p className="pd-hint">Chưa có gì xảy ra với phiếu này.</p>;
  }

  return (
    <ol className="pd-journal">
      {entries.map((e) => (
        <li key={e.id} className={`pd-journal-item is-${e.kind}`}>
          <div className="pd-journal-meta">
            <b>{e.who}</b> · {dateTime(e.at)}
          </div>
          {e.text && <div className="pd-journal-text">{e.text}</div>}
        </li>
      ))}
    </ol>
  );
}

function Delta({ quantity, amount }: { quantity: number; amount: number }) {
  if (quantity === 0 && Math.round(amount) === 0) return <span className="pd-zero">0</span>;
  return (
    <span className="pd-delta">
      {quantity !== 0 && (
        <b className={quantity < 0 ? "pd-down" : "pd-up"}>
          {quantity > 0 ? "+" : ""}
          {num(quantity)}
        </b>
      )}
      <Money value={amount} />
    </span>
  );
}

function Money({ value }: { value: number }) {
  if (Math.round(value) === 0) return <span className="pd-zero">0 ₫</span>;
  return (
    <span className={value < 0 ? "pd-down" : "pd-up"}>
      {value > 0 ? "+" : ""}
      {money(value)} ₫
    </span>
  );
}
