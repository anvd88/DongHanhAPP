// Chỉ còn HAI trang dùng trên web: Phạt (/phat) và Bảng lương (/bang-luong). Các trang HR* khác trong
// file này từng là giao diện riêng cho vỏ APK Capacitor, đã xoá cùng vỏ đó (2026-07-19).
import { useState, type ReactNode } from "react";
import {
  Ban,
  Banknote,
  CheckCircle2,
  Clock3,
  FilePlus2,
  Megaphone,
  Pencil,
  Plus,
  Save,
  Send,
  Trash2,
  X,
  XCircle,
} from "lucide-react";
import { Field, Input, MoneyInput, Select } from "../../components/ui";
import { EmployeePicker } from "../../components/hr/EmployeePicker";
import { api } from "../../lib/api";
import { PERM, useAccess } from "../../lib/access";
import { date, dateTime, moneyVnd } from "../../lib/format";
import {
  payoutMethodLabel,
  penaltySchedule,
  penaltyStatusLabel,
  penaltyTypeColor,
  refundStatusColor,
  refundStatusLabel,
  type EmployeeCard,
  type EmployeeDetail,
  type PayLine,
  type HardSalary,
  type PayrollCompute,
  type PayslipHistoryEnvelope,
  type PayslipHistoryEvent,
  type PayslipLifecycleStatus,
  type PublishedPayslipMonthPage,
  type Penalty,
  type PenaltyRefund,
  type PenaltyType,
  type SalaryComponent,
  type SalaryDetail,
  type SalaryListItem,
} from "../../lib/hr";
import { useApi } from "../../lib/useApi";
import { useAppNotifications } from "../../components/app-notifications-context";
import "./hr-pages.css";

type Tone = "neutral" | "success" | "warning" | "danger" | "muted";

function currentMonth() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

function todayKey() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/** Cộng thêm i tháng vào kỳ "yyyy-MM"; trả về "MM/yyyy" để hiển thị. */
function addMonths(period: string, i: number) {
  const [y, m] = period.split("-").map(Number);
  if (!y || !m) return period;
  const total = (y * 12 + (m - 1)) + i;
  const ny = Math.floor(total / 12);
  const nm = (total % 12) + 1;
  return `${String(nm).padStart(2, "0")}/${ny}`;
}

function HrPage({
  eyebrow,
  title,
  children,
  action,
  className = "",
}: {
  eyebrow?: string;
  title: string;
  children: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={`hr-page ${className}`.trim()}>
      <div className="hr-page-head">
        <div>
          {eyebrow && <p>{eyebrow}</p>}
          <h1>{title}</h1>
        </div>
        {action}
      </div>
      {children}
    </div>
  );
}

function HrCard({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <section className={`hr-card ${className}`}>{children}</section>;
}

function HrButton({
  children,
  onClick,
  type = "button",
  tone = "primary",
  disabled,
}: {
  children: ReactNode;
  onClick?: () => void;
  type?: "button" | "submit";
  tone?: "primary" | "secondary" | "danger";
  disabled?: boolean;
}) {
  return (
    <button type={type} className="hr-button" data-tone={tone} onClick={onClick} disabled={disabled}>
      {children}
    </button>
  );
}

function HrStat({ label, value, hint, tone = "neutral" }: { label: string; value: string; hint?: string; tone?: Tone }) {
  return (
    <article className="hr-stat" data-tone={tone}>
      <span>{label}</span>
      <strong>{value}</strong>
      {hint && <small>{hint}</small>}
    </article>
  );
}

function HrStatus({ status, children }: { status: Tone; children: ReactNode }) {
  return (
    <span className="hr-status" data-tone={status}>
      {children}
    </span>
  );
}

function HrEmpty({ text }: { text: string }) {
  return <div className="hr-empty">{text}</div>;
}

function HrModal({
  title,
  children,
  footer,
  onClose,
  wide,
}: {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  onClose: () => void;
  wide?: boolean;
}) {
  return (
    <div className="hr-modal-layer" onClick={onClose}>
      <div className="hr-modal" data-wide={wide} onClick={(event) => event.stopPropagation()}>
        <header>
          <h2>{title}</h2>
          <button type="button" onClick={onClose} aria-label="Đóng">
            <X className="h-5 w-5" />
          </button>
        </header>
        <div className="hr-modal-body scroll-thin">{children}</div>
        {footer && <footer>{footer}</footer>}
      </div>
    </div>
  );
}

export function HRPenaltyPage() {
  const { can } = useAccess();
  const admin = can(PERM.penaltyManage);
  const canApproveRefund = can(PERM.payoutApprove);
  const canPayRefund = can(PERM.payoutPay);
  const { notify, confirm } = useAppNotifications();
  const [month, setMonth] = useState("");
  const query = admin
    ? `/api/penalties?scope=all${month ? `&month=${month}` : ""}`
    : "/api/penalties?scope=mine";
  const { data, loading, reload } = useApi<Penalty[]>(query, [query]);
  const { data: me } = useApi<EmployeeDetail>("/api/hr/me");
  const canAccounting = !!me?.isAccounting && can(PERM.payoutRead);
  const { data: myRefunds, reload: reloadMyRefunds } = useApi<PenaltyRefund[]>("/api/penalty-refunds?scope=mine");
  const { data: queueRefunds, reload: reloadQueue } = useApi<PenaltyRefund[]>(
    canAccounting ? "/api/penalty-refunds?scope=queue" : null,
    [canAccounting],
  );
  const [editing, setEditing] = useState<Penalty | "new" | null>(null);
  const [appealing, setAppealing] = useState<Penalty | null>(null);
  const [approvingRefund, setApprovingRefund] = useState<PenaltyRefund | null>(null);

  const items = data ?? [];
  // Tổng phạt tiền CÒN NỢ (đang hiệu lực): lấy số CÒN LẠI, không phải mức phạt gốc — phạt đã tất toán không tính.
  const activeFines = items
    .filter((p) => p.status === "Active" && p.penaltyType === "fine")
    .reduce((sum, p) => sum + (p.progress?.remaining ?? p.amount ?? 0), 0);

  const waive = async (p: Penalty) => {
    const ok = await confirm({ title: "Miễn phạt?", description: `${p.penaltyNo} sẽ chuyển sang trạng thái đã miễn.`, confirmLabel: "Miễn phạt", tone: "warning" });
    if (!ok) return;
    try {
      await api.post(`/api/penalties/${p.id}/waive`);
      notify.success("Đã miễn phạt.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xử lý được.");
    }
  };

  const remove = async (p: Penalty) => {
    const ok = await confirm({ title: "Xóa quyết định phạt?", description: `Xóa vĩnh viễn ${p.penaltyNo}.`, confirmLabel: "Xóa", tone: "danger" });
    if (!ok) return;
    try {
      await api.del(`/api/penalties/${p.id}`);
      notify.success("Đã xóa.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được.");
    }
  };

  const refundAction = async (r: PenaltyRefund, action: "reject" | "mark-paid") => {
    const label = action === "reject" ? "Từ chối khoản hoàn?" : "Xác nhận đã chi tiền mặt?";
    const ok = await confirm({ title: label, description: `${r.refundNo} · ${moneyVnd(r.amount)}`, confirmLabel: "Xác nhận", tone: action === "reject" ? "danger" : "info" });
    if (!ok) return;
    try {
      await api.post(`/api/penalty-refunds/${r.id}/${action}`, action === "reject" ? { note: "" } : {});
      notify.success(action === "reject" ? "Đã từ chối." : "Đã đánh dấu đã chi.");
      reloadQueue({ silent: true });
      reloadMyRefunds({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xử lý được.");
    }
  };

  const pendingQueue = (queueRefunds ?? []).filter((r) => r.status === "PendingAccounting" || r.status === "Approved");

  return (
    <HrPage
      eyebrow="Kỷ luật"
      title={admin ? "Phạt / kỷ luật" : "Phạt của tôi"}
      action={admin ? <HrButton onClick={() => setEditing("new")}><FilePlus2 className="h-4 w-4" /> Lập phạt</HrButton> : undefined}
      className="hr-page--penalty"
    >
      <div className="hr-grid-3">
        <HrStat label="Số lần bị phạt" value={`${items.length}`} tone={items.length ? "warning" : "neutral"} />
        <HrStat label="Còn hiệu lực" value={`${items.filter((p) => p.status === "Active").length}`} />
        <HrStat label="Tổng phạt tiền" value={moneyVnd(activeFines)} hint="Đang hiệu lực" tone={activeFines > 0 ? "danger" : "neutral"} />
      </div>

      {admin && (
        <HrCard className="hr-penalty-filter-card">
          <div className="hr-range-row">
            <Field label="Lọc theo tháng">
              <Input type="month" value={month} onChange={(e) => setMonth(e.target.value)} />
            </Field>
            {month && <HrButton tone="secondary" onClick={() => setMonth("")}>Xóa lọc</HrButton>}
          </div>
        </HrCard>
      )}

      <HrCard className="hr-penalty-list-card">
        {loading && !data ? <HrEmpty text="Đang tải danh sách phạt..." /> : (items.length ? items.map((p) => (
          <PenaltyRow key={p.id} p={p} admin={admin} onEdit={() => setEditing(p)} onWaive={() => waive(p)} onDelete={() => remove(p)} onAppeal={() => setAppealing(p)} />
        )) : <HrEmpty text={admin ? "Chưa có quyết định phạt nào." : "Bạn chưa bị phạt lần nào. 🎉"} />)}
      </HrCard>

      {(myRefunds?.length ?? 0) > 0 && (
        <HrCard className="hr-penalty-list-card">
          <div className="hr-card-title">Hoàn tiền phạt của tôi</div>
          {myRefunds!.map((r) => <RefundRow key={r.id} r={r} showEmployee={false} />)}
        </HrCard>
      )}

      {canAccounting && (
        <HrCard className="hr-penalty-list-card">
          <div className="hr-card-title">Duyệt chi hoàn phạt {pendingQueue.length > 0 ? `(${pendingQueue.length})` : ""}</div>
          {pendingQueue.length === 0 ? <HrEmpty text="Không có khoản hoàn nào chờ xử lý." /> : pendingQueue.map((r) => (
            <RefundRow
              key={r.id}
              r={r}
              showEmployee
              actions={
                r.status === "PendingAccounting" && canApproveRefund ? (
                  <>
                    <HrButton onClick={() => setApprovingRefund(r)}><CheckCircle2 className="h-4 w-4" /> Duyệt</HrButton>
                    <HrButton tone="danger" onClick={() => refundAction(r, "reject")}><XCircle className="h-4 w-4" /> Từ chối</HrButton>
                  </>
                ) : r.status === "Approved" && r.payoutMethod === "cash" && canPayRefund ? (
                  <HrButton onClick={() => refundAction(r, "mark-paid")}><Banknote className="h-4 w-4" /> Đã chi tiền</HrButton>
                ) : null
              }
            />
          ))}
        </HrCard>
      )}

      {approvingRefund && (
        <RefundApproveModal
          refund={approvingRefund}
          onClose={() => setApprovingRefund(null)}
          onDone={() => { setApprovingRefund(null); reloadQueue({ silent: true }); reloadMyRefunds({ silent: true }); notify.success("Đã duyệt khoản hoàn."); }}
        />
      )}

      {editing && (
        <PenaltyModal
          penalty={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); reload({ silent: true }); notify.success("Đã lưu quyết định phạt."); }}
        />
      )}

      {appealing && (
        <PenaltyAppealModal
          penalty={appealing}
          onClose={() => setAppealing(null)}
          onSent={() => { setAppealing(null); notify.success("Đã gửi khiếu nại. Theo dõi tại mục Đơn từ."); }}
        />
      )}
    </HrPage>
  );
}

function PenaltyRow({ p, admin, onEdit, onWaive, onDelete, onAppeal }: { p: Penalty; admin: boolean; onEdit: () => void; onWaive: () => void; onDelete: () => void; onAppeal: () => void }) {
  const waived = p.status === "Waived";
  const settled = p.status === "Settled";
  const progress = p.progress ?? null;
  return (
    <article className="hr-day-row hr-penalty-row" style={waived ? { opacity: 0.6 } : undefined}>
      <div className="hr-penalty-main">
        <div className="hr-penalty-titleline">
          <strong>{admin ? p.employeeName : p.penaltyTypeLabel}</strong>
          <span>{p.penaltyNo}</span>
        </div>
        <small>
          {admin ? `${p.penaltyTypeLabel} · ` : ""}
          {p.penaltyDate ? date(p.penaltyDate) : "--"}
          {p.amount > 0 ? ` · ${moneyVnd(p.amount)}` : ""}
          {p.penaltyType === "fine" && p.installments > 1 ? ` · trừ ${p.installments} tháng` : ""}
          {p.reason ? ` · ${p.reason}` : ""}
        </small>
      </div>
      <div className="hr-penalty-right">
        <HrStatus status={waived ? "muted" : settled ? "success" : (penaltyTypeColor(p.penaltyType) as Tone)}>
          {waived || settled ? penaltyStatusLabel(p.status) : p.penaltyTypeLabel}
        </HrStatus>
        {admin ? (
          <div className="hr-penalty-actions">
            <button type="button" className="hr-icon-btn" onClick={onEdit} aria-label="Sửa"><Pencil className="h-4 w-4" /></button>
            {!waived && !settled && <button type="button" className="hr-icon-btn" onClick={onWaive} aria-label="Miễn phạt"><Ban className="h-4 w-4" /></button>}
            <button type="button" className="hr-icon-btn" onClick={onDelete} aria-label="Xóa"><Trash2 className="h-4 w-4" /></button>
          </div>
        ) : (
          !waived && (
            <button type="button" className="hr-appeal-btn" onClick={onAppeal}>
              <Megaphone className="h-3.5 w-3.5" /> Khiếu nại
            </button>
          )
        )}
      </div>

      {progress && <PenaltyProgressView progress={progress} />}
    </article>
  );
}

/** Thanh tiến trình khấu trừ phạt tiền: đã trừ / còn lại / còn bao nhiêu kỳ, kèm chi tiết từng kỳ. */
function PenaltyProgressView({ progress }: { progress: NonNullable<Penalty["progress"]> }) {
  const [open, setOpen] = useState(false);
  const pct = progress.total > 0 ? Math.min(100, Math.round((progress.deducted / progress.total) * 100)) : 0;
  const done = progress.settled || progress.remaining <= 0;
  return (
    <div className="hr-penalty-progress">
      <div className="hr-penalty-progress-bar">
        <span style={{ width: `${pct}%` }} data-done={done} />
      </div>
      <div className="hr-penalty-progress-meta">
        <span>Đã trừ <b>{moneyVnd(progress.deducted)}</b> / {moneyVnd(progress.total)}</span>
        <span>Còn lại <b>{moneyVnd(progress.remaining)}</b></span>
        <span>{done ? "Đã tất toán" : `Còn ${progress.remainingMonths}/${progress.totalMonths} kỳ`}</span>
      </div>
      {!done && progress.nextPeriod && (
        <small className="hr-penalty-progress-next">
          Kỳ tới ({addMonths(progress.nextPeriod, 0)}) sẽ trừ <b>{moneyVnd(progress.nextAmount)}</b> khi có phiếu lương.
        </small>
      )}
      <button type="button" className="hr-penalty-progress-toggle" onClick={() => setOpen((v) => !v)}>
        {open ? "Ẩn chi tiết" : "Xem chi tiết từng kỳ"}
      </button>
      {open && (
        <div className="hr-penalty-schedule">
          {progress.periods.map((it) => (
            <div key={it.installmentNo}>
              <span>
                Kỳ {addMonths(it.period, 0)}{progress.totalMonths > 1 ? ` · đợt ${it.installmentNo}/${progress.totalMonths}` : ""}
                {it.paid ? " · đã trừ" : " · chưa tới"}
              </span>
              <b data-paid={it.paid}>{it.paid ? "✓ " : ""}{moneyVnd(it.amount)}</b>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** Khiếu nại một án phạt: tạo đơn từ loại "penalty_appeal" ngay trong giao diện phạt của nhân viên. */
function PenaltyAppealModal({ penalty, onClose, onSent }: { penalty: Penalty; onClose: () => void; onSent: () => void }) {
  const { notify } = useAppNotifications();
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);

  const submit = async () => {
    if (!reason.trim()) { notify.error("Vui lòng nhập nội dung khiếu nại."); return; }
    setSaving(true);
    try {
      await api.post("/api/requests", {
        type: "penalty_appeal",
        title: `Khiếu nại án phạt ${penalty.penaltyNo}`,
        payload: {
          penaltyNo: penalty.penaltyNo,
          penaltyType: penalty.penaltyTypeLabel,
          penaltyAmount: penalty.amount || 0,
          reason: reason.trim(),
        },
      });
      onSent();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không gửi được khiếu nại.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <HrModal
      title={`Khiếu nại án phạt · ${penalty.penaltyNo}`}
      onClose={onClose}
      footer={<><HrButton tone="secondary" onClick={onClose}>Hủy</HrButton><HrButton onClick={submit} disabled={saving}><Send className="h-4 w-4" /> Gửi khiếu nại</HrButton></>}
    >
      <div className="hr-form-stack">
        <div className="hr-penalty-schedule">
          <div><span>Hình thức</span><b>{penalty.penaltyTypeLabel}</b></div>
          {penalty.amount > 0 && <div><span>Số tiền phạt</span><b>{moneyVnd(penalty.amount)}</b></div>}
          {penalty.reason && <div><span>Lý do phạt</span><b>{penalty.reason}</b></div>}
        </div>
        <Field label="Nội dung khiếu nại">
          <textarea className="hr-textarea" rows={4} value={reason} onChange={(e) => setReason(e.target.value)}
            placeholder="Trình bày lý do bạn không đồng ý với quyết định phạt này…" />
        </Field>
        <p className="hr-hint-text">Khiếu nại sẽ được gửi như một đơn từ và chuyển qua quản lý trực tiếp rồi đến quản trị / HR để xem xét. Bạn theo dõi tiến trình tại mục Đơn từ.</p>
      </div>
    </HrModal>
  );
}

/** Một dòng khoản hoàn tiền phạt (dùng cho "của tôi" và hàng đợi kế toán). */
function RefundRow({ r, showEmployee, actions }: { r: PenaltyRefund; showEmployee: boolean; actions?: ReactNode }) {
  const paid = r.status === "Paid";
  return (
    <article className="hr-day-row hr-penalty-row hr-refund-row" style={r.status === "Rejected" ? { opacity: 0.6 } : undefined}>
      <div className="hr-penalty-main">
        <strong>{showEmployee ? r.employeeName : `Hoàn phạt ${r.penaltyNo}`}</strong>
        <small>
          {showEmployee ? `${r.refundNo} · phạt ${r.penaltyNo} · ` : `${r.refundNo} · `}
          {moneyVnd(r.amount)}
          {r.payoutMethod ? ` · ${payoutMethodLabel(r.payoutMethod)}` : ""}
          {paid && r.payoutMethod === "payroll" && r.appliedPeriod ? ` (kỳ ${r.appliedPeriod})` : ""}
        </small>
      </div>
      <div className="hr-penalty-right">
        <HrStatus status={refundStatusColor(r.status) as Tone}>{refundStatusLabel(r.status)}</HrStatus>
      </div>
      {actions && <div className="hr-refund-actions">{actions}</div>}
    </article>
  );
}

/** Kế toán duyệt khoản hoàn: chọn hình thức chi trả (cộng vào lương / chi tiền mặt). */
function RefundApproveModal({ refund, onClose, onDone }: { refund: PenaltyRefund; onClose: () => void; onDone: () => void }) {
  const { notify } = useAppNotifications();
  const [payoutMethod, setPayoutMethod] = useState<"payroll" | "cash">("payroll");
  const [note, setNote] = useState("");
  const [saving, setSaving] = useState(false);

  const submit = async () => {
    setSaving(true);
    try {
      await api.post(`/api/penalty-refunds/${refund.id}/approve`, { payoutMethod, note: note.trim() });
      onDone();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không duyệt được.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <HrModal
      title={`Duyệt hoàn phạt · ${refund.refundNo}`}
      onClose={onClose}
      footer={<><HrButton tone="secondary" onClick={onClose}>Hủy</HrButton><HrButton onClick={submit} disabled={saving}><CheckCircle2 className="h-4 w-4" /> Duyệt chi</HrButton></>}
    >
      <div className="hr-form-stack">
        <div className="hr-penalty-schedule">
          <div><span>Nhân viên</span><b>{refund.employeeName} ({refund.employeeCode})</b></div>
          <div><span>Án phạt</span><b>{refund.penaltyNo}</b></div>
          <div><span>Số tiền hoàn</span><b>{moneyVnd(refund.amount)}</b></div>
          {refund.reason && <div><span>Lý do</span><b>{refund.reason}</b></div>}
        </div>
        <Field label="Hình thức chi trả">
          <div className="hr-radio-stack">
            <label><input type="radio" name="payout" checked={payoutMethod === "payroll"} onChange={() => setPayoutMethod("payroll")} /> Cộng vào phiếu lương kỳ kế tiếp</label>
            <label><input type="radio" name="payout" checked={payoutMethod === "cash"} onChange={() => setPayoutMethod("cash")} /> Chi tiền mặt (nhân viên nhận tại phòng kế toán)</label>
          </div>
        </Field>
        <Field label="Ghi chú"><textarea className="hr-textarea" rows={2} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
        <p className="hr-hint-text">
          {payoutMethod === "payroll"
            ? "Khoản hoàn sẽ tự cộng vào phiếu lương kỳ tiếp theo khi lập phiếu."
            : "Sau khi duyệt, bấm “Đã chi tiền” khi nhân viên đã nhận tiền mặt."}
        </p>
      </div>
    </HrModal>
  );
}

function PenaltyModal({ penalty, onClose, onSaved }: { penalty: Penalty | null; onClose: () => void; onSaved: () => void }) {
  const { notify } = useAppNotifications();
  const { data: employees } = useApi<EmployeeCard[]>(penalty ? null : "/api/hr/employees");
  const { data: types } = useApi<PenaltyType[]>("/api/penalties/types");
  const [employeeId, setEmployeeId] = useState(penalty?.employeeId ?? "");
  const [penaltyType, setPenaltyType] = useState(penalty?.penaltyType ?? "reminder");
  const [penaltyDate, setPenaltyDate] = useState(penalty?.penaltyDate?.slice(0, 10) ?? todayKey());
  const [amount, setAmount] = useState(penalty ? String(penalty.amount ?? 0) : "");
  const [installments, setInstallments] = useState(String(penalty?.installments ?? 1));
  const [startPeriod, setStartPeriod] = useState(penalty?.startPeriod || (penalty?.penaltyDate?.slice(0, 7)) || currentMonth());
  const [reason, setReason] = useState(penalty?.reason ?? "");
  const [note, setNote] = useState(penalty?.note ?? "");
  const [status, setStatus] = useState(penalty?.status ?? "Active");
  const [saving, setSaving] = useState(false);

  const isFine = penaltyType === "fine";
  const nInstallments = Math.max(1, Number(installments) || 1);
  const schedule = isFine ? penaltySchedule(Number(amount) || 0, nInstallments) : [];

  const submit = async () => {
    if (!penalty && !employeeId) { notify.error("Vui lòng chọn nhân viên."); return; }
    if (!reason.trim()) { notify.error("Vui lòng nhập lý do phạt."); return; }
    setSaving(true);
    try {
      const body = {
        employeeId: penalty?.employeeId ?? employeeId,
        penaltyType,
        penaltyDate: penaltyDate || null,
        amount: Number(amount) || 0,
        installments: isFine ? nInstallments : 1,
        startPeriod: isFine ? startPeriod : "",
        reason: reason.trim(),
        note: note.trim(),
        status,
      };
      if (penalty) await api.put(`/api/penalties/${penalty.id}`, body);
      else await api.post("/api/penalties", body);
      onSaved();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <HrModal
      title={penalty ? `Sửa phạt · ${penalty.penaltyNo}` : "Lập quyết định phạt"}
      onClose={onClose}
      footer={<><HrButton tone="secondary" onClick={onClose}>Hủy</HrButton><HrButton onClick={submit} disabled={saving}><Save className="h-4 w-4" /> Lưu</HrButton></>}
    >
      <div className="hr-form-stack">
        {penalty ? (
          <Field label="Nhân viên"><Input value={`${penalty.employeeName} (${penalty.employeeCode})`} disabled /></Field>
        ) : (
          <Field label="Nhân viên">
            <EmployeePicker
              employees={employees ?? []}
              value={employeeId}
              onChange={setEmployeeId}
              placeholder="Chọn nhân viên"
              allowClear
              clearLabel="-- Chọn nhân viên --"
            />
          </Field>
        )}
        <Field label="Hình thức">
          <Select value={penaltyType} onChange={(e) => setPenaltyType(e.target.value)} className="w-full">
            {(types ?? []).map((t) => <option key={t.type} value={t.type}>{t.label}</option>)}
          </Select>
        </Field>
        <Field label="Ngày phạt"><Input type="date" value={penaltyDate} onChange={(e) => setPenaltyDate(e.target.value)} /></Field>
        <Field label="Số tiền phạt (₫)"><MoneyInput value={amount} onChange={setAmount} /></Field>
        {isFine && (
          <>
            <Field label="Trừ trong (số tháng)">
              <Input type="number" min={1} max={60} value={installments} onChange={(e) => setInstallments(e.target.value)} />
            </Field>
            <Field label="Bắt đầu trừ từ kỳ"><Input type="month" value={startPeriod} onChange={(e) => setStartPeriod(e.target.value)} /></Field>
            {schedule.length > 0 && (
              <div className="hr-penalty-schedule">
                <strong>Lịch khấu trừ dự kiến</strong>
                {schedule.map((amt, i) => (
                  <div key={i}>
                    <span>Kỳ {addMonths(startPeriod, i)}{nInstallments > 1 ? ` · đợt ${i + 1}/${schedule.length}` : ""}</span>
                    <b>{moneyVnd(amt)}</b>
                  </div>
                ))}
              </div>
            )}
          </>
        )}
        <Field label="Lý do"><textarea className="hr-textarea" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
        <Field label="Ghi chú"><textarea className="hr-textarea" rows={2} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
        {penalty && (
          <Field label="Trạng thái">
            <Select value={status} onChange={(e) => setStatus(e.target.value)} className="w-full">
              <option value="Active">Còn hiệu lực</option>
              <option value="Waived">Đã miễn</option>
              {status === "Settled" && <option value="Settled">Đã tất toán (đã thu đủ)</option>}
            </Select>
          </Field>
        )}
      </div>
    </HrModal>
  );
}

export function HRPayrollPage() {
  const [tab, setTab] = useState<"salary" | "published" | "payslip">("salary");
  const [payslipTarget, setPayslipTarget] = useState<{ employeeId: string; period: string } | null>(null);
  const { can } = useAccess();
  const canManagePayroll = can(PERM.payrollManage);
  const openPublishedPayslip = (employeeId: string, period: string) => {
    setPayslipTarget({ employeeId, period });
    setTab("payslip");
  };
  return (
    <HrPage eyebrow="Quản trị" title="Bảng lương" className="hr-page--payroll">
      <div className="hr-tabs">
        {[
          ["salary", "Mức lương"],
          ["published", "Phiếu đã phát hành"],
          ["payslip", "Tính & lập phiếu"],
        ].map(([key, label]) => (
          <button key={key} type="button" data-active={tab === key} onClick={() => setTab(key as typeof tab)}>{label}</button>
        ))}
      </div>
      {tab === "salary"
        ? <SalaryAdmin canManage={canManagePayroll} />
        : tab === "published"
          ? <PublishedPayslipsMonth onOpen={openPublishedPayslip} />
          : <PayslipMaker
              canManage={canManagePayroll}
              initialEmployeeId={payslipTarget?.employeeId}
              initialPeriod={payslipTarget?.period}
            />}
    </HrPage>
  );
}

function PublishedPayslipsMonth({ onOpen }: { onOpen: (employeeId: string, period: string) => void }) {
  const [period, setPeriod] = useState(currentMonth());
  const [searchDraft, setSearchDraft] = useState("");
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<"all" | "pending" | "acknowledged">("all");
  const [page, setPage] = useState(1);
  const path = `/api/payroll/payslips/published?period=${encodeURIComponent(period)}&search=${encodeURIComponent(search)}&status=${status}&page=${page}&pageSize=50`;
  const { data, loading, error } = useApi<PublishedPayslipMonthPage>(path, [period, search, status, page]);
  const view = data?.period === period && data.search === search && data.status === status && data.page === page ? data : null;

  const applySearch = () => {
    setPage(1);
    setSearch(searchDraft.trim());
  };

  return (
    <>
      <HrCard className="hr-payroll-filter-card">
        <form className="hr-published-payroll-filters" onSubmit={(e) => { e.preventDefault(); applySearch(); }}>
          <Field label="Kỳ lương">
            <Input type="month" value={period} onChange={(e) => { setPeriod(e.target.value); setPage(1); }} />
          </Field>
          <Field label="Tìm nhân viên">
            <Input value={searchDraft} placeholder="Tên, mã, phòng ban hoặc chi nhánh" onChange={(e) => setSearchDraft(e.target.value)} />
          </Field>
          <Field label="Trạng thái xác nhận">
            <Select value={status} onChange={(e) => { setStatus(e.target.value as typeof status); setPage(1); }}>
              <option value="all">Tất cả phiếu đã phát hành</option>
              <option value="pending">Chờ nhân viên xác nhận</option>
              <option value="acknowledged">Nhân viên đã xác nhận</option>
            </Select>
          </Field>
          <HrButton type="submit" tone="secondary">Tìm kiếm</HrButton>
        </form>
      </HrCard>

      {loading && !view ? <HrCard><HrEmpty text="Đang tải phiếu lương đã phát hành..." /></HrCard> : error && !view ? (
        <HrCard><HrEmpty text={error} /></HrCard>
      ) : view ? (
        <>
          <div className="hr-grid-4 hr-published-payroll-stats">
            <HrStat
              label="Đã phát hành"
              value={`${view.summary.publishedCount}/${view.summary.activeEmployeeCount}`}
              hint="phiếu / nhân sự đang hoạt động"
              tone="neutral"
            />
            <HrStat label="Đã xác nhận" value={`${view.summary.acknowledgedCount}`} hint="nhân viên đã xem phiếu" tone="success" />
            <HrStat label="Chờ xác nhận" value={`${view.summary.pendingAcknowledgementCount}`} hint="cần theo dõi" tone={view.summary.pendingAcknowledgementCount > 0 ? "warning" : "neutral"} />
            <HrStat label="Tổng thực nhận" value={moneyVnd(view.summary.totalNetPay)} hint={`Kỳ ${addMonths(view.period, 0)}`} tone="success" />
          </div>

          <HrCard className="hr-published-payroll-balance">
            <div><span>Tổng thu nhập</span><strong>{moneyVnd(view.summary.totalEarnings)}</strong></div>
            <b>−</b>
            <div><span>Tổng khấu trừ</span><strong>{moneyVnd(view.summary.totalDeductions)}</strong></div>
            <b>=</b>
            <div data-net><span>Tổng thực nhận</span><strong>{moneyVnd(view.summary.totalNetPay)}</strong></div>
          </HrCard>

          <HrCard className="hr-published-payroll-card">
            <div className="hr-published-payroll-head">
              <div>
                <strong>Phiếu đã phát hành · {addMonths(view.period, 0)}</strong>
                <small>{view.totalItems} kết quả{search ? ` cho “${search}”` : ""}</small>
              </div>
              <span>Trang {view.page}/{view.totalPages}</span>
            </div>
            {view.items.length === 0 ? <HrEmpty text="Không có phiếu lương phù hợp bộ lọc." /> : (
              <div className="hr-published-payroll-list">
                {view.items.map((row) => (
                  <article key={row.id} className="hr-published-payroll-row">
                    <div className="hr-published-payroll-person">
                      <span>{row.employeeCode}</span>
                      <strong>{row.employeeName}</strong>
                      <small>{[row.departmentName, row.locationName].filter(Boolean).join(" · ") || "Chưa phân phòng ban"}</small>
                    </div>
                    <div className="hr-published-payroll-money">
                      <div><span>Thu nhập</span><b>{moneyVnd(row.totalEarnings)}</b></div>
                      <div><span>Khấu trừ</span><b data-deduction>−{moneyVnd(row.totalDeductions)}</b></div>
                      <div><span>Thực nhận</span><b data-net>{moneyVnd(row.netPay)}</b></div>
                    </div>
                    <div className="hr-published-payroll-meta">
                      <small>Tăng ca {row.overtimeHours} giờ</small>
                      <small>Cập nhật {dateTime(row.updatedAt)}</small>
                    </div>
                    <div className="hr-published-payroll-actions">
                      <HrStatus status={row.status === "Acknowledged" ? "success" : "warning"}>
                        {row.status === "Acknowledged" ? "Đã xác nhận" : "Chờ xác nhận"}
                      </HrStatus>
                      <HrButton tone="secondary" onClick={() => onOpen(row.employeeId, row.period)}>
                        <Pencil className="h-4 w-4" /> Xem / điều chỉnh
                      </HrButton>
                    </div>
                  </article>
                ))}
              </div>
            )}
            {view.totalPages > 1 && (
              <div className="hr-published-payroll-pagination">
                <HrButton tone="secondary" disabled={view.page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))}>Trang trước</HrButton>
                <span>Trang {view.page} / {view.totalPages}</span>
                <HrButton tone="secondary" disabled={view.page >= view.totalPages} onClick={() => setPage((p) => Math.min(view.totalPages, p + 1))}>Trang sau</HrButton>
              </div>
            )}
          </HrCard>
        </>
      ) : null}
    </>
  );
}

function SalaryAdmin({ canManage }: { canManage: boolean }) {
  const { data, loading, reload } = useApi<SalaryListItem[]>("/api/payroll/salaries");
  const [edit, setEdit] = useState<SalaryListItem | null>(null);
  return (
    <HrCard className="hr-payroll-list-card">
      {loading && !data ? <HrEmpty text="Đang tải mức lương..." /> : (data?.length ? data.map((row) => (
        <article key={row.employeeId} className="hr-day-row hr-payroll-employee-row">
          <div className="hr-payroll-employee-main">
            <div className="hr-payroll-employee-titleline">
              <strong>{row.employeeName}</strong>
              <span>{row.employeeCode}</span>
            </div>
            <small>
              {row.hasSalary ? `Lương cứng ${moneyVnd(row.baseSalary)}` : "Chưa thiết lập mức lương"}
              {row.hardSalary?.raiseTotal ? ` (HĐ ${moneyVnd(row.hardSalary.contractBase)} + tăng ${moneyVnd(row.hardSalary.raiseTotal)})` : ""}
              {row.hasSalary && row.extraCount > 0 ? ` · +${row.extraCount} khoản` : ""}
              {row.hardSalary && !row.hardSalary.fromContract ? " · chưa có hợp đồng" : ""}
            </small>
          </div>
          <div className="hr-penalty-right">
            <HrStatus status={row.hasSalary ? "success" : "muted"}>{row.hasSalary ? "Đã gán" : "Chưa gán"}</HrStatus>
            {canManage && <div className="hr-penalty-actions">
              <button type="button" className="hr-icon-btn" onClick={() => setEdit(row)} aria-label="Sửa mức lương"><Pencil className="h-4 w-4" /></button>
            </div>}
          </div>
        </article>
      )) : <HrEmpty text="Chưa có nhân viên." />)}
      {canManage && edit && <SalaryModal item={edit} onClose={() => setEdit(null)} onSaved={() => { setEdit(null); reload({ silent: true }); }} />}
    </HrCard>
  );
}

function SalaryModal({ item, onClose, onSaved }: { item: SalaryListItem; onClose: () => void; onSaved: () => void }) {
  const { notify } = useAppNotifications();
  const { data, loading } = useApi<SalaryDetail>(`/api/payroll/salaries/${item.employeeId}`);
  const [base, setBase] = useState("");
  const [overtimeRate, setOvertimeRate] = useState("");
  const [components, setComponents] = useState<SalaryComponent[]>([]);
  const [note, setNote] = useState("");
  const [ready, setReady] = useState(false);
  const [saving, setSaving] = useState(false);

  // Nạp lương từ máy chủ vào ô nhập, ĐÚNG MỘT LẦN (cờ `ready`): tải lại dữ liệu giữa chừng không được
  // đè lên con số kế toán đang gõ dở. Làm lúc render thay vì trong useEffect nên không có khung hình
  // nào hiện "0 đ" trước khi số thật kịp hiện — trên màn lương thì cái nháy đó rất dễ gây hiểu nhầm.
  if (data && !ready) {
    // Ô "Lương cứng" chỉ còn dùng cho nhân viên CHƯA có hợp đồng nào (số nhập tay cũ). Có hợp đồng
    // thì lương cứng là số dẫn xuất, hiển thị chứ không cho sửa ở đây.
    setBase(String(data.legacyBaseSalary ?? 0));
    setOvertimeRate(String(data.overtimeRate ?? 0));
    // Gộp "Phụ cấp" cũ (nếu có) thành một khoản tự nhập để mọi khoản ngoài lương cứng nằm chung một chỗ.
    const extra: SalaryComponent[] = [...(data.components ?? [])];
    if ((data.allowance ?? 0) !== 0) extra.unshift({ label: "Phụ cấp", amount: data.allowance, kind: "earning" });
    setComponents(extra);
    setNote(data.note ?? "");
    setReady(true);
  }

  const addComponent = (kind: "earning" | "deduction") => setComponents((s) => [...s, { label: "", amount: 0, kind }]);
  const updateComponent = (i: number, patch: Partial<SalaryComponent>) =>
    setComponents((s) => s.map((c, idx) => (idx === i ? { ...c, ...patch } : c)));
  const removeComponent = (i: number) => setComponents((s) => s.filter((_, idx) => idx !== i));

  const hard = data?.hardSalary;
  const fromContract = Boolean(hard?.fromContract);
  const hardAmount = fromContract ? hard!.amount : Number(base) || 0;
  const cleanComponents = components.filter((c) => c.label.trim());
  const extraEarn = cleanComponents.filter((c) => c.kind === "earning").reduce((s, c) => s + (Number(c.amount) || 0), 0);
  const extraDeduct = cleanComponents.filter((c) => c.kind === "deduction").reduce((s, c) => s + (Number(c.amount) || 0), 0);
  const monthly = hardAmount + extraEarn - extraDeduct;

  const submit = async () => {
    setSaving(true);
    try {
      await api.put(`/api/payroll/salaries/${item.employeeId}`, {
        // Có hợp đồng: giữ nguyên con số cũ trong hr_salaries, lương cứng đã do hợp đồng quyết định.
        baseSalary: Number(base) || 0,
        allowance: 0, // Phụ cấp giờ là một khoản tự nhập trong "components" → không dùng ô riêng nữa.
        overtimeRate: Number(overtimeRate) || 0,
        components: cleanComponents.map((c) => ({ label: c.label.trim(), amount: Number(c.amount) || 0, kind: c.kind })),
        note: note.trim(),
      });
      notify.success("Đã lưu mức lương.");
      onSaved();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <HrModal
      title={`Mức lương · ${item.employeeName}`}
      onClose={onClose}
      footer={<><HrButton tone="secondary" onClick={onClose}>Hủy</HrButton><HrButton onClick={submit} disabled={saving || !ready}><Save className="h-4 w-4" /> Lưu</HrButton></>}
    >
      {loading && !data ? <HrEmpty text="Đang tải..." /> : (
        <div className="hr-form-stack">
          {fromContract ? (
            <HardSalaryBreakdown hard={hard!} />
          ) : (
            <>
              <Field label="Lương cứng (₫/tháng)"><MoneyInput value={base} onChange={setBase} /></Field>
              <p className="hr-salary-empty">
                Nhân viên này chưa có hợp đồng nào nên lương cứng vẫn nhập tay ở đây. Thêm hợp đồng ở
                Nhân sự → Quyền lợi → Hợp đồng để hệ thống tự lấy lương cơ bản + các kỳ tăng lương.
              </p>
            </>
          )}

          <div className="hr-salary-components">
            <div className="hr-salary-components-head">
              <strong>Các khoản khác (tự nhập)</strong>
              <div>
                <button type="button" onClick={() => addComponent("earning")}><Plus className="h-3.5 w-3.5" /> Khoản cộng</button>
                <button type="button" onClick={() => addComponent("deduction")}><Plus className="h-3.5 w-3.5" /> Khoản trừ</button>
              </div>
            </div>
            {components.length === 0 && <p className="hr-salary-empty">Chưa có khoản nào. Bấm “Khoản cộng” (vd: phụ cấp, thưởng) hoặc “Khoản trừ” (vd: BHXH) để thêm.</p>}
            {components.map((c, i) => (
              <div key={i} className="hr-salary-comp-row" data-kind={c.kind}>
                <Input value={c.label} placeholder={c.kind === "earning" ? "Tên khoản cộng (vd: Phụ cấp ăn trưa)" : "Tên khoản trừ (vd: BHXH)"} onChange={(e) => updateComponent(i, { label: e.target.value })} />
                <MoneyInput value={c.amount} onChange={(raw) => updateComponent(i, { amount: Number(raw) || 0 })} />
                <span className="hr-salary-comp-tag">{c.kind === "earning" ? "Cộng" : "Trừ"}</span>
                <button type="button" className="hr-icon-btn" onClick={() => removeComponent(i)} aria-label="Xóa"><Trash2 className="h-4 w-4" /></button>
              </div>
            ))}
          </div>

          <div className="hr-payslip-net">
            <span>Tạm tính mỗi tháng (chưa gồm tăng ca / phạt)</span>
            <strong>{moneyVnd(monthly)}</strong>
          </div>

          <details className="hr-salary-advanced">
            <summary>Nâng cao · tăng ca</summary>
            <Field label="Đơn giá tăng ca (₫/giờ)">
              <MoneyInput value={overtimeRate} onChange={setOvertimeRate} />
            </Field>
            <p className="hr-salary-empty">Tiền mỗi giờ tăng ca. App tự tính theo số giờ tăng ca của bảng công.</p>
          </details>

          <Field label="Ghi chú"><textarea className="hr-textarea" rows={2} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
        </div>
      )}
    </HrModal>
  );
}

/** Giải thích lương cứng của kỳ: lương cơ bản trên hợp đồng + từng lần tăng lương đã có hiệu lực. */
function HardSalaryBreakdown({ hard, period }: { hard: HardSalary; period?: string }) {
  if (!hard.fromContract) {
    return <p className="hr-salary-empty">Nhân viên chưa có hợp đồng — lương cứng đang lấy từ mức nhập tay cũ.</p>;
  }
  return (
    <div className="hr-salary-components">
      <div className="hr-salary-components-head">
        <strong>Lương cứng {period ? `kỳ ${addMonths(period, 0)}` : "hiện tại"}</strong>
        <span className="hr-salary-comp-tag">
          {hard.contractType || "Hợp đồng"}{hard.contractNo ? ` · ${hard.contractNo}` : ""}
        </span>
      </div>
      <PaylineRow label="Lương cơ bản theo hợp đồng" amount={hard.contractBase} />
      {hard.raises.map((r, i) => (
        <PaylineRow
          key={`${r.period}-${i}`}
          label={`Tăng lương từ ${addMonths(r.period, 0)}${r.decisionNo ? ` · QĐ ${r.decisionNo}` : ""}${r.reason ? ` · ${r.reason}` : ""}`}
          amount={r.amount}
        />
      ))}
      <PaylineRow label="Lương cứng áp dụng" amount={hard.amount} total />
      {!hard.contractEffective && (
        <p className="hr-salary-empty">
          Hợp đồng này không còn hiệu lực trong kỳ{hard.contractEndDate ? ` (hết hạn ${date(hard.contractEndDate)})` : ""}.
          Hệ thống vẫn dùng mức lương gần nhất — hãy ký lại hợp đồng để số liệu đúng.
        </p>
      )}
      {hard.raises.length === 0 && <p className="hr-salary-empty">Chưa có kỳ tăng lương nào. Ghi nhận ở Nhân sự → Quyền lợi → Hợp đồng → Tăng lương.</p>}
    </div>
  );
}

function PayslipMaker({
  canManage,
  initialEmployeeId = "",
  initialPeriod = currentMonth(),
}: {
  canManage: boolean;
  initialEmployeeId?: string;
  initialPeriod?: string;
}) {
  const { notify, confirm } = useAppNotifications();
  const { data: employees } = useApi<EmployeeCard[]>("/api/hr/employees");
  const [employeeId, setEmployeeId] = useState(initialEmployeeId);
  const [period, setPeriod] = useState(initialPeriod);
  const [published, setPublished] = useState(true);
  const [adjustments, setAdjustments] = useState<SalaryComponent[]>([]);
  const [otSelected, setOtSelected] = useState<Set<string>>(new Set());
  const [saving, setSaving] = useState(false);
  const canQuery = Boolean(employeeId && period);
  const { data: compute, loading } = useApi<PayrollCompute>(
    canQuery ? `/api/payroll/compute?employeeId=${employeeId}&period=${period}` : null,
    [employeeId, period],
  );
  const { data: savedPayslip, loading: historyLoading, reload: reloadHistory, setData: setSavedPayslip } = useApi<PayslipHistoryEnvelope>(
    canQuery ? `/api/payroll/payslips/history?employeeId=${employeeId}&period=${period}` : null,
    [employeeId, period],
  );

  // Khi tính xong: mặc định DUYỆT tất cả ngày tăng ca phát hiện được (admin có thể bỏ chọn từng ngày).
  // Chỉ reset khi TẬP ngày đổi (đổi nhân viên/kỳ), không reset khi dữ liệu chỉ được nạp lại ngầm.
  const otDayKey = (compute?.overtimeDays ?? []).map((d) => d.date).join(",");
  const [otSeededKey, setOtSeededKey] = useState<string | null>(null);
  const [deletingPayslipId, setDeletingPayslipId] = useState<string | null>(null);
  if (otSeededKey !== otDayKey) {
    setOtSeededKey(otDayKey);
    setOtSelected(new Set(otDayKey ? otDayKey.split(",") : []));
  }

  const addAdj = (kind: "earning" | "deduction") => setAdjustments((s) => [...s, { label: "", amount: 0, kind }]);
  const updateAdj = (i: number, patch: Partial<SalaryComponent>) => setAdjustments((s) => s.map((c, idx) => (idx === i ? { ...c, ...patch } : c)));
  const removeAdj = (i: number) => setAdjustments((s) => s.filter((_, idx) => idx !== i));
  const toggleOt = (dateKey: string) => setOtSelected((s) => {
    const next = new Set(s);
    if (next.has(dateKey)) next.delete(dateKey); else next.add(dateKey);
    return next;
  });

  const otDays = compute?.overtimeDays ?? [];
  const otRate = compute?.overtimeRate ?? 0;
  const otMinutes = otDays.filter((d) => otSelected.has(d.date)).reduce((s, d) => s + d.minutes, 0);
  const otHours = Math.round((otMinutes / 60) * 100) / 100;
  const otPay = Math.round((otMinutes / 60) * otRate);

  const adjEarnings = adjustments.filter((a) => a.kind === "earning" && a.label.trim());
  const adjDeductions = adjustments.filter((a) => a.kind === "deduction" && a.label.trim());
  const earnings: PayLine[] = [
    ...(compute?.earnings ?? []),
    ...(otPay !== 0 ? [{ label: `Tăng ca (${otHours} giờ)`, amount: otPay }] : []),
    ...adjEarnings.map((a) => ({ label: a.label.trim(), amount: Number(a.amount) || 0 })),
  ];
  const deductions: PayLine[] = [...(compute?.deductions ?? []), ...adjDeductions.map((a) => ({ label: a.label.trim(), amount: Number(a.amount) || 0 }))];
  const totalEarnings = earnings.reduce((s, e) => s + e.amount, 0);
  const totalDeductions = deductions.reduce((s, e) => s + e.amount, 0);
  const net = totalEarnings - totalDeductions;

  const create = async () => {
    if (!compute) return;
    setSaving(true);
    try {
      await api.post("/api/payroll/payslips", {
        employeeId,
        period,
        published,
        adjustments: adjustments.filter((a) => a.label.trim()).map((a) => ({ label: a.label.trim(), amount: Number(a.amount) || 0, kind: a.kind })),
        approvedOvertimeDates: otDays.filter((d) => otSelected.has(d.date)).map((d) => d.date),
      });
      notify.success(published ? "Đã phát hành phiếu lương." : "Đã lưu phiếu lương nháp.");
      setAdjustments([]);
      reloadHistory({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lập được phiếu.");
    } finally {
      setSaving(false);
    }
  };

  const deleteSavedPayslip = async () => {
    const current = savedPayslip?.payslip;
    if (!current || !canManage || deletingPayslipId) return;
    const isDraft = current.status === "Draft";
    const acknowledged = current.status === "Acknowledged";
    const ok = await confirm({
      title: isDraft ? "Xóa phiếu lương nháp?" : "Thu hồi và xóa phiếu lương đã phát hành?",
      description: isDraft
        ? `Phiếu nháp kỳ ${addMonths(current.period, 0)} của ${current.employeeName} sẽ bị xóa.`
        : `Phiếu kỳ ${addMonths(current.period, 0)} của ${current.employeeName} sẽ không còn hiển thị cho nhân viên. Sau đó bạn có thể điều chỉnh và phát hành lại.`,
      detail: acknowledged
        ? "Nhân viên đã xác nhận xem phiếu này. Sự kiện xóa và toàn bộ bản chụp số liệu cũ vẫn được giữ trong lịch sử kiểm toán. Phiếu chi đã duyệt hoặc đã chi sẽ không thể xóa."
        : "Phiếu chi lương liên quan sẽ được hủy nếu chưa duyệt/chưa chi. Lịch sử và bản chụp số liệu cũ vẫn được giữ để kiểm toán.",
      confirmLabel: isDraft ? "Xóa bản nháp" : "Thu hồi & xóa",
      tone: "danger",
    });
    if (!ok) return;

    setDeletingPayslipId(current.id);
    try {
      await api.del(`/api/hr/payslips/${current.id}`);
      // Nạp trực tiếp trạng thái mới để nút xóa biến mất ngay và timeline hiện sự kiện Deleted,
      // không phải chờ tín hiệu realtime/refetch nền.
      const next = await api.get<PayslipHistoryEnvelope>(
        `/api/payroll/payslips/history?employeeId=${employeeId}&period=${period}`,
      );
      setSavedPayslip(next);
      notify.success(
        isDraft
          ? "Đã xóa phiếu lương nháp."
          : "Đã thu hồi và xóa phiếu lương. Bạn có thể điều chỉnh số liệu rồi phát hành lại.",
      );
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được phiếu lương.");
    } finally {
      setDeletingPayslipId(null);
    }
  };

  return (
    <>
      <HrCard className="hr-payroll-filter-card">
        <div className="hr-form-grid">
          <Field label="Nhân viên">
            <EmployeePicker
              employees={employees ?? []}
              value={employeeId}
              onChange={setEmployeeId}
              placeholder="Chọn nhân viên"
              allowClear
              clearLabel="-- Chọn nhân viên --"
            />
          </Field>
          <Field label="Kỳ lương"><Input type="month" value={period} onChange={(e) => setPeriod(e.target.value)} /></Field>
        </div>
      </HrCard>

      {canQuery && (
        <PayslipHistoryPanel
          data={savedPayslip}
          loading={historyLoading}
          employeeId={employeeId}
          period={period}
          canManage={canManage}
          deleting={Boolean(deletingPayslipId)}
          onDelete={deleteSavedPayslip}
        />
      )}

      {!canQuery ? (
        <HrCard className="hr-payroll-empty-card"><HrEmpty text="Chọn nhân viên và kỳ lương để tính." /></HrCard>
      ) : loading && !compute ? (
        <HrCard className="hr-payroll-empty-card"><HrEmpty text="Đang tính lương..." /></HrCard>
      ) : compute ? (
        <>
          <div className="hr-grid-3">
            <HrStat label="Ngày công" value={`${compute.workedDays}`} hint={`Vắng ${compute.absentDays}`} />
            <HrStat label="Tăng ca duyệt" value={`${otHours} giờ`} hint={moneyVnd(otPay)} />
            <HrStat label="Đi muộn" value={`${compute.lateDays}`} tone={compute.lateDays > 0 ? "warning" : "neutral"} />
          </div>

          {compute.hardSalary && <HrCard className="hr-payroll-preview-card"><HardSalaryBreakdown hard={compute.hardSalary} period={period} /></HrCard>}

          <HrCard className="hr-payroll-overtime-card">
            <div className="hr-salary-components-head">
              <strong>Duyệt tăng ca theo ngày</strong>
              {canManage && otDays.length > 0 && (
                <div>
                  <button
                    type="button"
                    onClick={() => setOtSelected(otSelected.size > 0 ? new Set() : new Set(otDays.map((d) => d.date)))}
                  >
                    {otSelected.size > 0 ? "Bỏ chọn" : "Chọn tất cả"}
                  </button>
                </div>
              )}
            </div>
            {otDays.length === 0 ? (
              <p className="hr-salary-empty">Không có ngày nào tăng ca đủ 15 phút trước 08:00 hoặc sau 17:00 trong kỳ này.</p>
            ) : (
              <div className="hr-ot-list">
                {otDays.map((d) => (
                  <label key={d.date} className="hr-ot-row" data-on={otSelected.has(d.date)}>
                    <input type="checkbox" checked={otSelected.has(d.date)} disabled={!canManage} onChange={() => toggleOt(d.date)} />
                    <span className="hr-ot-date">{date(d.date)}</span>
                    <span className="hr-ot-out">Vào {d.checkIn}{d.checkOut ? ` · Ra ${d.checkOut}` : ""}</span>
                    <span className="hr-ot-min">{Math.round((d.minutes / 60) * 100) / 100} giờ</span>
                    <b>{moneyVnd(Math.round((d.minutes / 60) * otRate))}</b>
                  </label>
                ))}
              </div>
            )}
            {otDays.length > 0 && otRate === 0 && (
              <p className="hr-salary-empty">Chưa đặt “Đơn giá tăng ca” cho nhân viên này nên tiền tăng ca = 0. Đặt ở tab Mức lương.</p>
            )}
          </HrCard>

          <HrCard className="hr-payroll-preview-card">
            <div className="hr-payslip-lines">
              <div className="hr-payslip-group">
                <h3>Khoản cộng</h3>
                {earnings.map((e, i) => <PaylineRow key={`e${i}`} label={e.label} amount={e.amount} />)}
                <PaylineRow label="Tổng thu nhập" amount={totalEarnings} total />
              </div>
              <div className="hr-payslip-group">
                <h3>Khoản trừ</h3>
                {deductions.length === 0 && <p className="hr-salary-empty">Không có khoản trừ.</p>}
                {deductions.map((d, i) => <PaylineRow key={`d${i}`} label={d.label} amount={d.amount} minus />)}
                <PaylineRow label="Tổng khấu trừ" amount={totalDeductions} total minus />
              </div>
            </div>
            <div className="hr-payslip-net">
              <span>Thực nhận</span>
              <strong>{moneyVnd(net)}</strong>
            </div>
          </HrCard>

          {canManage && <HrCard className="hr-payroll-adjust-card">
            <div className="hr-salary-components-head">
              <strong>Điều chỉnh thêm cho kỳ này</strong>
              <div>
                <button type="button" onClick={() => addAdj("earning")}><Plus className="h-3.5 w-3.5" /> Cộng</button>
                <button type="button" onClick={() => addAdj("deduction")}><Plus className="h-3.5 w-3.5" /> Trừ</button>
              </div>
            </div>
            {adjustments.length === 0 && <p className="hr-salary-empty">Không có điều chỉnh.</p>}
            {adjustments.map((c, i) => (
              <div key={i} className="hr-salary-comp-row" data-kind={c.kind}>
                <Input value={c.label} placeholder={c.kind === "earning" ? "Tên khoản cộng" : "Tên khoản trừ"} onChange={(e) => updateAdj(i, { label: e.target.value })} />
                <MoneyInput value={c.amount} onChange={(raw) => updateAdj(i, { amount: Number(raw) || 0 })} />
                <span className="hr-salary-comp-tag">{c.kind === "earning" ? "Cộng" : "Trừ"}</span>
                <button type="button" className="hr-icon-btn" onClick={() => removeAdj(i)} aria-label="Xóa"><Trash2 className="h-4 w-4" /></button>
              </div>
            ))}
          </HrCard>}

          {canManage ? <HrCard className="hr-sync-card hr-payroll-submit-card">
            <label className="hr-publish-check">
              <input type="checkbox" checked={published} onChange={(e) => setPublished(e.target.checked)} /> Phát hành cho nhân viên
            </label>
            <HrButton onClick={create} disabled={saving}><Banknote className="h-4 w-4" /> Lập phiếu lương</HrButton>
          </HrCard> : <HrCard className="hr-sync-card hr-payroll-submit-card">
            <p className="hr-salary-empty">Bạn có quyền xem bảng lương và lịch sử, nhưng không có quyền sửa mức lương hoặc lập/phát hành phiếu.</p>
          </HrCard>}
        </>
      ) : <HrCard className="hr-payroll-empty-card"><HrEmpty text="Không tính được lương." /></HrCard>}
    </>
  );
}

const payslipStatusLabel = (status?: PayslipLifecycleStatus | null) =>
  ({ Draft: "Bản nháp", Published: "Đã phát hành", Acknowledged: "Nhân viên đã xác nhận", Deleted: "Đã xóa" } as Record<string, string>)[status ?? ""] ?? "Chưa có";

const payslipStatusTone = (status?: PayslipLifecycleStatus | null): Tone =>
  status === "Acknowledged" ? "success" : status === "Published" ? "success" : status === "Draft" ? "warning" : "muted";

const payslipActionLabel = (event: PayslipHistoryEvent) =>
  ({
    Imported: "Ghi nhận phiếu có sẵn",
    DraftCreated: "Tạo phiếu nháp",
    PublishedCreated: "Tạo và phát hành phiếu",
    DraftUpdated: "Cập nhật phiếu nháp",
    Published: "Phát hành phiếu",
    PublishedUpdated: "Cập nhật phiếu đã phát hành",
    PublishedRevised: "Sửa phiếu sau khi nhân viên xác nhận",
    ReturnedToDraft: "Chuyển phiếu về nháp",
    Acknowledged: "Nhân viên xác nhận đã xem",
    Deleted: "Xóa phiếu",
  } as Record<string, string>)[event.action] ?? event.action;

function PayslipHistoryPanel({
  data,
  loading,
  employeeId,
  period,
  canManage,
  deleting,
  onDelete,
}: {
  data: PayslipHistoryEnvelope | null;
  loading: boolean;
  employeeId: string;
  period: string;
  canManage: boolean;
  deleting: boolean;
  onDelete: () => void;
}) {
  const [historyOpen, setHistoryOpen] = useState(false);
  // useApi giữ dữ liệu cũ trong lúc đổi bộ lọc để tránh chớp trang. Với lương thì không được hiện tạm
  // phiếu của người vừa chọn trước dưới tên người mới, nên chặn dữ liệu không khớp ngay tại đây.
  const owner = data?.payslip ?? data?.history[0];
  if (owner && (owner.employeeId !== employeeId || owner.period !== period))
    return <HrCard className="hr-payroll-history-card"><HrEmpty text="Đang tải lịch sử phiếu lương..." /></HrCard>;
  if (loading && !data) return <HrCard className="hr-payroll-history-card"><HrEmpty text="Đang tải lịch sử phiếu lương..." /></HrCard>;
  if (!data?.payslip && !data?.history.length) {
    return <HrCard className="hr-payroll-history-card"><HrEmpty text="Kỳ này chưa có phiếu đã lưu, kể cả bản nháp." /></HrCard>;
  }

  const current = data.payslip;
  return (
    <HrCard className="hr-payroll-history-card">
      <div className="hr-payroll-history-head">
        <div>
          <span>Phiếu đã lưu</span>
          <strong>{current ? `Kỳ ${addMonths(current.period, 0)} · ${moneyVnd(current.netPay)}` : "Phiếu đã được xóa"}</strong>
          {current?.status === "Draft" && <small>Bản nháp chỉ được lưu trong sổ, chưa hiển thị cho nhân viên.</small>}
          {current?.status === "Published" && <small>Phiếu đã gửi cho nhân viên và đang chờ xác nhận đã xem.</small>}
          {current?.status === "Acknowledged" && <small>Nhân viên đã xác nhận phiếu lương này.</small>}
        </div>
        <div className="hr-payroll-history-head-right">
          <HrStatus status={payslipStatusTone(current?.status)}>{payslipStatusLabel(current?.status ?? (data.history[0]?.statusAfter))}</HrStatus>
          <button type="button" className="hr-payroll-history-btn" onClick={() => setHistoryOpen(true)}>
            <Clock3 className="h-4 w-4" /> Lịch sử
            {data.history.length > 0 && <span>{data.history.length}</span>}
          </button>
        </div>
      </div>

      {current && canManage && (
        <div className="hr-payroll-history-actions" data-published={current.status !== "Draft"}>
          <div>
            <strong>{current.status === "Draft" ? "Không dùng phiếu nháp này?" : "Cần điều chỉnh phiếu đã phát hành?"}</strong>
            <small>
              {current.status === "Draft"
                ? "Xóa bản nháp để lập lại từ đầu."
                : "Thu hồi và xóa phiếu hiện tại, sau đó sửa khoản cộng/trừ hoặc tăng ca rồi phát hành lại."}
            </small>
          </div>
          <HrButton tone="danger" onClick={onDelete} disabled={deleting}>
            <Trash2 className="h-4 w-4" />
            {deleting ? "Đang xóa..." : current.status === "Draft" ? "Xóa bản nháp" : "Thu hồi & xóa"}
          </HrButton>
        </div>
      )}

      {historyOpen && (
        <HrModal
          title="Lịch sử thay đổi"
          onClose={() => setHistoryOpen(false)}
          footer={<HrButton tone="secondary" onClick={() => setHistoryOpen(false)}>Đóng</HrButton>}
        >
          {data.history.length === 0 ? <HrEmpty text="Chưa có sự kiện nào." /> : (
            <ol className="hr-payroll-timeline">
              {data.history.map((event) => (
                <li key={event.id} data-status={event.statusAfter}>
                  <span className="hr-payroll-timeline-dot" aria-hidden="true" />
                  <div className="hr-payroll-timeline-event">
                    <div>
                      <strong>{payslipActionLabel(event)}</strong>
                      <time dateTime={event.occurredAt}>{dateTime(event.occurredAt)}</time>
                    </div>
                    <p>
                      {event.statusBefore ? `${payslipStatusLabel(event.statusBefore)} → ` : ""}
                      <b>{payslipStatusLabel(event.statusAfter)}</b> · bởi {event.actor || "Hệ thống"}
                    </p>
                    {typeof event.summary?.netPay === "number" && (
                      <small>Thực nhận {moneyVnd(event.summary.netPay)}{event.summary.note ? ` · ${event.summary.note}` : ""}</small>
                    )}
                  </div>
                </li>
              ))}
            </ol>
          )}
        </HrModal>
      )}
    </HrCard>
  );
}

function PaylineRow({ label, amount, minus, total }: { label: string; amount: number; minus?: boolean; total?: boolean }) {
  return (
    <div className="hr-payline" data-total={total}>
      <span>{label}</span>
      <b>{minus && amount > 0 ? "−" : ""}{moneyVnd(amount)}</b>
    </div>
  );
}

