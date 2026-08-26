import { useMemo, useState } from "react";
import {
  AlertTriangle,
  Banknote,
  CheckCircle2,
  Clock3,
  HandCoins,
  History,
  Loader2,
  Plus,
  RefreshCw,
  Search,
  UserRound,
  WalletCards,
  XCircle,
} from "lucide-react";
import { Modal } from "../components/Modal";
import { Field, Input, Select } from "../components/ui";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Button } from "../components/ui";
import { useAppNotifications } from "../components/app-notifications-context";
import { useAccess, PERM } from "../lib/access";
import { api, ApiError } from "../lib/api";
import { useApi } from "../lib/useApi";
import { date, money } from "../lib/format";
import "../features/giacong/giacong.css";
import "./cash-collections.css";

const DENOMINATIONS = [500_000, 200_000, 100_000, 50_000, 20_000, 10_000, 5_000, 2_000, 1_000, 500, 200, 100] as const;

type CashMap = Record<string, number>;

interface Driver {
  id: string;
  username: string;
  name: string;
  employeeCode: string;
  position: string;
}

interface CollectionCustomer {
  id: string;
  name: string;
  phone: string;
}

interface CashCollection {
  id: string;
  orderNo: string;
  customerId: string;
  customerName: string;
  customerPhone: string;
  driverEmployeeId: string;
  driverUsername: string;
  driverName: string;
  expectedAmount: number;
  scheduledDate: string;
  handoverDueAt: string;
  note: string;
  status: string;
  createdBy: string;
  createdAt: string;
  acceptedAt?: string | null;
  collectedAt?: string | null;
  collectedAmount?: number | null;
  failureReason: string;
  receivedBy: string;
  receivedAt?: string | null;
  receivedAmount?: number | null;
  paymentId?: string | null;
  cancelReason: string;
  driverCash: CashMap;
  accountantCash: CashMap;
  overdue: boolean;
  expectedVariance: boolean;
  cashVariance: boolean;
  canReceive: boolean;
  canCancel: boolean;
  canResolve: boolean;
}

interface CollectionDetail {
  order: CashCollection;
  counts: Array<{
    id: string;
    stage: "driver" | "accountant";
    revision: number;
    actor: string;
    total: number;
    confirmedAt: string;
    lines: Array<{ denomination: number; quantity: number; subtotal: number }>;
  }>;
  events: Array<{
    id: string;
    action: string;
    actor: string;
    beforeStatus?: string | null;
    afterStatus?: string | null;
    note: string;
    occurredAt: string;
  }>;
}

const STATUS: Record<string, { label: string; tone: string }> = {
  Assigned: { label: "Chờ tài xế nhận", tone: "blue" },
  Accepted: { label: "Tài xế đã nhận", tone: "violet" },
  PendingHandover: { label: "Chờ bàn giao", tone: "amber" },
  Variance: { label: "Sai lệch tiền", tone: "red" },
  Failed: { label: "Không thu được", tone: "slate" },
  Completed: { label: "Đã nộp đủ tiền", tone: "green" },
  Cancelled: { label: "Đã hủy", tone: "slate" },
};

const EVENT_LABELS: Record<string, string> = {
  created: "Tạo lệnh",
  accepted: "Tài xế nhận lệnh",
  collected: "Tài xế xác nhận đã thu",
  recollected: "Tài xế khai lại tiền đã thu",
  failed: "Không thu được",
  variance_detected: "Phát hiện sai lệch",
  expected_variance_detected: "Thực thu lệch số dự kiến",
  variance_returned: "Trả tài xế kiểm đếm lại",
  variance_resolved: "Kế toán trưởng duyệt sai lệch",
  completed: "Nhận đủ — đã nộp đủ tiền",
  cancelled: "Hủy lệnh",
};

function localDateInput(offsetDays = 0) {
  const value = new Date();
  value.setDate(value.getDate() + offsetDays);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
}

function defaultDueInput() {
  return `${localDateInput(1)}T10:00`;
}

function cashTotal(cash: CashMap) {
  return DENOMINATIONS.reduce((sum, denomination) => sum + denomination * (cash[String(denomination)] ?? 0), 0);
}

function statusMeta(status: string) {
  return STATUS[status] ?? { label: status, tone: "slate" };
}

export function CashCollections() {
  const access = useAccess();
  const { notify } = useAppNotifications();
  const { data: orders, loading, error, reload } = useApi<CashCollection[]>("/api/cash-collections?scope=all");
  const { data: customers } = useApi<CollectionCustomer[]>(access.can(PERM.collectionsCreate) ? "/api/cash-collections/customers" : null);
  const { data: drivers } = useApi<Driver[]>(access.can(PERM.collectionsCreate) ? "/api/cash-collections/drivers" : null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("active");
  const [creating, setCreating] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [receiving, setReceiving] = useState<CashCollection | null>(null);
  const [resolving, setResolving] = useState<CashCollection | null>(null);

  const filtered = useMemo(() => {
    const q = query.trim().toLocaleLowerCase("vi");
    return (orders ?? []).filter((row) => {
      const statusMatch = status === "all" ||
        (status === "active" && ["Assigned", "Accepted", "PendingHandover", "Variance"].includes(row.status)) ||
        row.status === status;
      return statusMatch && (!q || [row.orderNo, row.customerName, row.driverName, row.customerPhone]
        .some((value) => value.toLocaleLowerCase("vi").includes(q)));
    });
  }, [orders, query, status]);

  const holding = (orders ?? []).filter((row) => ["PendingHandover", "Variance"].includes(row.status));
  const holdingTotal = holding.reduce((sum, row) => sum + (row.collectedAmount ?? 0), 0);
  const overdue = holding.filter((row) => row.overdue).length;
  const completed = (orders ?? []).filter((row) => row.status === "Completed");

  const cancelOrder = async (row: CashCollection) => {
    const reason = window.prompt(`Nhập lý do hủy lệnh ${row.orderNo}:`);
    if (!reason?.trim()) return;
    try {
      await api.post(`/api/cash-collections/${row.id}/cancel`, { reason: reason.trim() });
      notify.success(`Đã hủy lệnh ${row.orderNo}.`, "Lệnh thu tiền");
      reload();
    } catch (cause) {
      notify.error(cause instanceof Error ? cause.message : "Không hủy được lệnh.");
    }
  };

  return (
    <div className="gc-root space-y-5">
      <div className="flex flex-col justify-between gap-3 md:flex-row md:items-end">
        <div>
          <div className="mb-1 flex items-center gap-2 text-sm font-bold text-emerald-600 dark:text-emerald-400">
            <HandCoins className="h-4 w-4" /> Công nợ khách hàng
          </div>
          <h1 className="text-[1.75rem] font-black text-[var(--gc-text)]">Lệnh thu tiền khách hàng</h1>
          <p className="mt-1 text-sm font-semibold text-[var(--gc-text-muted)]">
            Theo dõi tiền tài xế đang giữ, kiểm đếm bàn giao và tự động ghi nhận đã nộp đủ tiền khi khớp.
          </p>
        </div>
        {access.can(PERM.collectionsCreate) && (
          <Button onClick={() => setCreating(true)}><Plus className="h-4 w-4" /> Tạo lệnh thu tiền</Button>
        )}
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Metric icon={Clock3} label="Lệnh đang thực hiện" value={String((orders ?? []).filter((x) => ["Assigned", "Accepted"].includes(x.status)).length)} />
        <Metric icon={WalletCards} label="Tài xế đang giữ" value={`${money(holdingTotal)} ₫`} tone="amber" />
        <Metric icon={AlertTriangle} label="Quá hạn bàn giao" value={String(overdue)} tone="red" />
        <Metric icon={CheckCircle2} label="Đã hoàn tất" value={`${completed.length} lệnh`} tone="green" />
      </div>

      <GlassPanel strong className="overflow-hidden">
        <div className="flex flex-col gap-3 border-b border-[var(--gc-border)] p-4 lg:flex-row lg:items-center">
          <div className="relative min-w-0 flex-1">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--gc-text-muted)]" />
            <Input className="pl-9" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Tìm mã lệnh, khách hàng hoặc tài xế" />
          </div>
          <Select value={status} onChange={(event) => setStatus(event.target.value)} className="min-w-[190px]">
            <option value="active">Đang hoạt động</option>
            <option value="all">Tất cả trạng thái</option>
            {Object.entries(STATUS).map(([key, value]) => <option key={key} value={key}>{value.label}</option>)}
          </Select>
          <Button variant="soft" onClick={() => reload()}><RefreshCw className="h-4 w-4" /> Làm mới</Button>
        </div>

        <div className="gc-scroll overflow-auto">
          <table className="gc-table min-w-[980px]">
            <thead><tr><th>Lệnh</th><th>Khách hàng</th><th>Tài xế</th><th>Ngày thu</th><th className="text-right">Dự kiến</th><th className="text-right">Đã thu</th><th>Trạng thái</th><th /></tr></thead>
            <tbody>
              {loading ? (
                <tr><td colSpan={8} className="py-16 text-center"><Loader2 className="mx-auto h-6 w-6 animate-spin" /></td></tr>
              ) : error ? (
                <tr><td colSpan={8} className="py-16 text-center font-semibold text-rose-500">{error}</td></tr>
              ) : filtered.length === 0 ? (
                <tr><td colSpan={8} className="py-16 text-center text-sm font-semibold text-[var(--gc-text-muted)]">Chưa có lệnh thu tiền phù hợp.</td></tr>
              ) : filtered.map((row) => {
                const meta = statusMeta(row.status);
                return (
                  <tr key={row.id} className={row.overdue ? "cc-overdue-row" : ""}>
                    <td><button className="text-left font-black text-[var(--gc-text)] hover:text-[var(--accent)]" onClick={() => setSelectedId(row.id)}>{row.orderNo}</button><div className="mt-1 text-xs text-[var(--gc-text-muted)]">Tạo bởi {row.createdBy}</div></td>
                    <td><div className="font-bold text-[var(--gc-text)]">{row.customerName}</div><div className="text-xs text-[var(--gc-text-muted)]">{row.customerPhone || "Không có số điện thoại"}</div></td>
                    <td><div className="flex items-center gap-1.5 font-semibold"><UserRound className="h-3.5 w-3.5" />{row.driverName}</div></td>
                    <td><div>{date(row.scheduledDate)}</div><div className={`text-xs ${row.overdue ? "font-bold text-rose-500" : "text-[var(--gc-text-muted)]"}`}>Hạn {new Date(row.handoverDueAt).toLocaleString("vi-VN")}</div></td>
                    <td className="text-right font-bold tabular-nums">{money(row.expectedAmount)} ₫</td>
                    <td className="text-right font-black tabular-nums text-emerald-600 dark:text-emerald-400">{row.collectedAmount ? `${money(row.collectedAmount)} ₫` : "—"}</td>
                    <td><span className={`cc-status cc-status-${meta.tone}`}>{row.overdue && "Quá hạn · "}{meta.label}</span></td>
                    <td><div className="flex justify-end gap-2">
                      {row.canReceive && <Button className="px-3 py-2" onClick={() => setReceiving(row)}><Banknote className="h-4 w-4" /> Kiểm đếm</Button>}
                      {row.canResolve && <Button variant="soft" className="px-3 py-2" onClick={() => setResolving(row)}><AlertTriangle className="h-4 w-4" /> Xử lý lệch</Button>}
                      {row.canCancel && <Button variant="ghost" className="px-3 py-2" onClick={() => void cancelOrder(row)}><XCircle className="h-4 w-4" /></Button>}
                    </div></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </GlassPanel>

      {creating && customers && drivers && (
        <CreateCollectionModal customers={customers} drivers={drivers} onClose={() => setCreating(false)} onSaved={() => { setCreating(false); reload(); }} />
      )}
      {receiving && <ReceiveCashModal order={receiving} onClose={() => setReceiving(null)} onSaved={() => { setReceiving(null); reload(); }} onVariance={() => { setReceiving(null); reload(); }} />}
      {resolving && <ResolveVarianceModal order={resolving} onClose={() => setResolving(null)} onSaved={() => { setResolving(null); reload(); }} />}
      {selectedId && <CollectionDetailModal id={selectedId} onClose={() => setSelectedId(null)} onReceive={(row) => { setSelectedId(null); setReceiving(row); }} onResolve={(row) => { setSelectedId(null); setResolving(row); }} />}
    </div>
  );
}

function Metric({ icon: Icon, label, value, tone = "blue" }: { icon: typeof Clock3; label: string; value: string; tone?: string }) {
  return <GlassPanel className={`cc-metric cc-metric-${tone}`}><div className="flex items-center gap-3 p-4"><span className="cc-metric-icon"><Icon className="h-5 w-5" /></span><div><div className="text-xs font-bold text-[var(--gc-text-muted)]">{label}</div><div className="mt-1 text-xl font-black tabular-nums text-[var(--gc-text)]">{value}</div></div></div></GlassPanel>;
}

function CreateCollectionModal({ customers, drivers, onClose, onSaved }: { customers: CollectionCustomer[]; drivers: Driver[]; onClose: () => void; onSaved: () => void }) {
  const { notify } = useAppNotifications();
  const [customerId, setCustomerId] = useState(customers[0]?.id ?? "");
  const [driverId, setDriverId] = useState(drivers[0]?.id ?? "");
  const [amount, setAmount] = useState("");
  const [scheduledDate, setScheduledDate] = useState(localDateInput());
  const [due, setDue] = useState(defaultDueInput());
  const [note, setNote] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const save = async () => {
    const parsed = Number(amount.replace(/[^\d]/g, ""));
    if (!customerId || !driverId || !Number.isSafeInteger(parsed) || parsed <= 0 || !scheduledDate || !due) {
      setError("Vui lòng chọn khách hàng, tài xế, ngày thu, hạn bàn giao và nhập số tiền hợp lệ.");
      return;
    }
    setSaving(true); setError("");
    try {
      const result = await api.post<{ orderNo: string }>("/api/cash-collections", {
        customerId, driverEmployeeId: driverId, expectedAmount: parsed, scheduledDate,
        handoverDueAt: new Date(due).toISOString(), note: note.trim(),
      });
      notify.success(`Đã tạo lệnh ${result.orderNo}.`, "Lệnh thu tiền");
      onSaved();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Không tạo được lệnh.");
    } finally { setSaving(false); }
  };

  return <Modal open panel title="Tạo lệnh thu tiền" onClose={() => !saving && onClose()} footer={<><Button variant="ghost" disabled={saving} onClick={onClose}>Hủy</Button><Button loading={saving} onClick={() => void save()}><Plus className="h-4 w-4" /> Tạo và giao tài xế</Button></>}>
    <div className="space-y-4">
      <Field label="Khách hàng *"><Select className="w-full" value={customerId} onChange={(e) => setCustomerId(e.target.value)}>{customers.map((x) => <option key={x.id} value={x.id}>{x.name}{x.phone ? ` · ${x.phone}` : ""}</option>)}</Select></Field>
      <Field label="Tài xế *"><Select className="w-full" value={driverId} onChange={(e) => setDriverId(e.target.value)}>{drivers.map((x) => <option key={x.id} value={x.id}>{x.name} · {x.position || x.employeeCode}</option>)}</Select></Field>
      <Field label="Số tiền dự kiến *"><Input inputMode="numeric" value={amount ? money(Number(amount.replace(/[^\d]/g, ""))) : ""} onChange={(e) => setAmount(e.target.value.replace(/[^\d]/g, ""))} placeholder="Ví dụ: 10.000.000" /></Field>
      <div className="grid gap-4 sm:grid-cols-2"><Field label="Ngày đi thu *"><Input type="date" value={scheduledDate} onChange={(e) => setScheduledDate(e.target.value)} /></Field><Field label="Hạn bàn giao *"><Input type="datetime-local" value={due} onChange={(e) => setDue(e.target.value)} /></Field></div>
      <Field label="Nội dung / ghi chú"><textarea className="km-form-control min-h-24 w-full rounded-xl border p-3 text-sm outline-none" value={note} onChange={(e) => setNote(e.target.value)} placeholder="Thông tin cần lưu ý khi thu tiền" /></Field>
      {error && <div className="rounded-xl bg-rose-500/10 p-3 text-sm font-semibold text-rose-600 dark:text-rose-400">{error}</div>}
      <p className="text-xs font-semibold text-[var(--gc-text-muted)]">Lệnh không lưu GPS và không sao chép địa chỉ khách hàng.</p>
    </div>
  </Modal>;
}

function CashCounter({ value, onChange, compareTotal }: { value: CashMap; onChange: (cash: CashMap) => void; compareTotal?: number }) {
  const total = cashTotal(value);
  const difference = compareTotal === undefined ? 0 : total - compareTotal;
  return <div className="space-y-3">
    <div className="cc-cash-grid">
      {DENOMINATIONS.map((denomination) => {
        const quantity = value[String(denomination)] ?? 0;
        return <div className="cc-cash-row" key={denomination}>
          <div><div className="font-black tabular-nums">{money(denomination)} ₫</div><div className="text-xs text-[var(--gc-text-muted)]">{quantity ? `${money(denomination * quantity)} ₫` : "—"}</div></div>
          <Input aria-label={`Số tờ ${denomination}`} inputMode="numeric" className="w-24 text-right font-black tabular-nums" value={quantity || ""} onChange={(e) => onChange({ ...value, [String(denomination)]: Math.max(0, Number(e.target.value.replace(/[^\d]/g, "")) || 0) })} placeholder="0" />
        </div>;
      })}
    </div>
    <div className="cc-counter-total"><span>Tổng đang đếm</span><strong>{money(total)} ₫</strong></div>
    {compareTotal !== undefined && <div className={`cc-difference ${difference === 0 ? "is-match" : "is-mismatch"}`}><span>{difference === 0 ? <CheckCircle2 className="h-5 w-5" /> : <AlertTriangle className="h-5 w-5" />}{difference === 0 ? "Đã khớp số tài xế giao" : "Chênh lệch so với tài xế"}</span><strong>{difference > 0 ? "+" : ""}{money(difference)} ₫</strong></div>}
  </div>;
}

function ReceiveCashModal({ order, onClose, onSaved, onVariance }: { order: CashCollection; onClose: () => void; onSaved: () => void; onVariance: () => void }) {
  const { notify } = useAppNotifications();
  const [cash, setCash] = useState<CashMap>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const total = cashTotal(cash);
  const driverTotal = order.collectedAmount ?? cashTotal(order.driverCash);
  const save = async () => {
    if (total <= 0) { setError("Vui lòng nhập số lượng các mệnh giá thủ quỹ thực tế đếm được."); return; }
    setSaving(true); setError("");
    try {
      await api.post(`/api/cash-collections/${order.id}/receive`, { lines: DENOMINATIONS.map((denomination) => ({ denomination, quantity: cash[String(denomination)] ?? 0 })) });
      notify.success(`Đã nhận đủ ${money(total)} ₫ — đã nộp đủ tiền.`, "Bàn giao tiền");
      onSaved();
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : "Không xác nhận được bàn giao.";
      if (cause instanceof ApiError && cause.status === 409) {
        notify.warning(message, "Chưa nộp đủ tiền");
        onVariance();
        return;
      }
      setError(message);
    } finally { setSaving(false); }
  };
  const expectedMismatch = driverTotal !== order.expectedAmount;
  const confirmLabel = total !== driverTotal ? "Ghi nhận sai lệch" : expectedMismatch ? "Ghi nhận và chờ duyệt" : "Nhận đủ — đã nộp đủ tiền";
  return <Modal open wide panel title={`Kiểm đếm bàn giao · ${order.orderNo}`} onClose={() => !saving && onClose()} footer={<><Button variant="ghost" disabled={saving} onClick={onClose}>Đóng</Button><Button loading={saving} onClick={() => void save()}>{total === driverTotal && !expectedMismatch ? <CheckCircle2 className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}{confirmLabel}</Button></>}>
    <div className="grid gap-5 lg:grid-cols-[0.8fr_1.2fr]">
      <div className="space-y-4">
        <div className="rounded-2xl border border-[var(--gc-border)] p-4"><div className="text-xs font-bold text-[var(--gc-text-muted)]">Khách hàng</div><div className="mt-1 text-lg font-black">{order.customerName}</div><div className="mt-3 text-xs font-bold text-[var(--gc-text-muted)]">Tài xế bàn giao</div><div className="mt-1 font-bold">{order.driverName}</div></div>
        <div className="rounded-2xl border border-amber-500/25 bg-amber-500/8 p-4"><div className="text-xs font-bold text-amber-700 dark:text-amber-300">TÀI XẾ ĐÃ XÁC NHẬN</div><div className="mt-1 text-2xl font-black tabular-nums">{money(driverTotal)} ₫</div><div className="mt-3 space-y-1.5">{DENOMINATIONS.filter((d) => order.driverCash[String(d)]).map((d) => <div key={d} className="flex justify-between text-sm"><span>{money(d)} ₫ × {order.driverCash[String(d)]}</span><b>{money(d * order.driverCash[String(d)])} ₫</b></div>)}</div></div>
        {expectedMismatch && <div className="rounded-xl bg-rose-500/10 p-3 text-sm font-semibold text-rose-600 dark:text-rose-400">Số tài xế khai lệch {money(driverTotal - order.expectedAmount)} ₫ so với dự kiến. Dù bàn giao khớp, việc ghi nhận đã nộp đủ tiền vẫn phải chờ Kế toán trưởng duyệt.</div>}
        <p className="text-xs font-semibold leading-relaxed text-[var(--gc-text-muted)]">Thủ quỹ hãy đếm độc lập. Máy chủ chỉ tự ghi nhận đã nộp đủ tiền khi số kiểm đếm khớp tài xế và đúng số tiền dự kiến.</p>
      </div>
      <CashCounter value={cash} onChange={setCash} compareTotal={driverTotal} />
    </div>
    {error && <div className="mt-4 rounded-xl bg-rose-500/10 p-3 text-sm font-semibold text-rose-600 dark:text-rose-400">{error}</div>}
  </Modal>;
}

function ResolveVarianceModal({ order, onClose, onSaved }: { order: CashCollection; onClose: () => void; onSaved: () => void }) {
  const { notify } = useAppNotifications();
  const [action, setAction] = useState(order.cashVariance ? "return_to_driver" : "approve_actual");
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const cashierTotal = order.receivedAmount ?? cashTotal(order.accountantCash);
  const driverTotal = order.collectedAmount ?? cashTotal(order.driverCash);

  const save = async () => {
    if (!reason.trim()) { setError("Vui lòng nhập lý do xử lý sai lệch."); return; }
    setSaving(true); setError("");
    try {
      await api.post(`/api/cash-collections/${order.id}/resolve`, { action, reason: reason.trim() });
      if (action === "approve_actual") notify.success(`Đã duyệt ${money(cashierTotal)} ₫ — đã nộp đủ tiền.`, "Xử lý sai lệch");
      else notify.success(`Đã trả lệnh ${order.orderNo} để tài xế khai lại tiền.`, "Xử lý sai lệch");
      onSaved();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Không xử lý được sai lệch.");
    } finally { setSaving(false); }
  };

  return <Modal open panel title={`Xử lý sai lệch · ${order.orderNo}`} onClose={() => !saving && onClose()} footer={<><Button variant="ghost" disabled={saving} onClick={onClose}>Đóng</Button><Button loading={saving} onClick={() => void save()}><AlertTriangle className="h-4 w-4" /> Xác nhận xử lý</Button></>}>
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-3"><Info label="Dự kiến" value={`${money(order.expectedAmount)} ₫`} /><Info label="Tài xế khai" value={`${money(driverTotal)} ₫`} /><Info label="Thủ quỹ đếm" value={`${money(cashierTotal)} ₫`} /></div>
      <Field label="Phương án xử lý *"><Select className="w-full" value={action} onChange={(event) => setAction(event.target.value)}><option value="approve_actual">Duyệt số thủ quỹ thực đếm</option><option value="return_to_driver">Trả tài xế kiểm đếm và khai lại</option></Select></Field>
      <Field label="Lý do xử lý *"><textarea className="km-form-control min-h-24 w-full rounded-xl border p-3 text-sm outline-none" maxLength={1000} value={reason} onChange={(event) => setReason(event.target.value)} placeholder="Nêu nguyên nhân chênh lệch và căn cứ xử lý" /></Field>
      <div className={`rounded-xl p-3 text-sm font-semibold ${action === "approve_actual" ? "bg-amber-500/10 text-amber-700 dark:text-amber-300" : "bg-blue-500/10 text-blue-700 dark:text-blue-300"}`}>
        {action === "approve_actual" ? `Sau khi xác nhận, ${money(cashierTotal)} ₫ được ghi nhận là đã nộp đủ tiền.` : "Lệnh sẽ quay về trạng thái đã nhận; tài xế phải kiểm đếm, khai lại và bàn giao lại cho thủ quỹ."}
      </div>
      {error && <div className="rounded-xl bg-rose-500/10 p-3 text-sm font-semibold text-rose-600 dark:text-rose-400">{error}</div>}
    </div>
  </Modal>;
}

function CollectionDetailModal({ id, onClose, onReceive, onResolve }: { id: string; onClose: () => void; onReceive: (row: CashCollection) => void; onResolve: (row: CashCollection) => void }) {
  const { data, loading, error } = useApi<CollectionDetail>(`/api/cash-collections/${id}`);
  const footer = data && (data.order.canReceive || data.order.canResolve) ? <>{data.order.canReceive && <Button onClick={() => onReceive(data.order)}><Banknote className="h-4 w-4" /> Kiểm đếm bàn giao</Button>}{data.order.canResolve && <Button variant="soft" onClick={() => onResolve(data.order)}><AlertTriangle className="h-4 w-4" /> Xử lý sai lệch</Button>}</> : undefined;
  return <Modal open wide panel title={data ? `Lệnh ${data.order.orderNo}` : "Chi tiết lệnh thu tiền"} onClose={onClose} footer={footer}>
    {loading ? <div className="py-16 text-center"><Loader2 className="mx-auto h-6 w-6 animate-spin" /></div> : error || !data ? <div className="py-12 text-center font-semibold text-rose-500">{error || "Không đọc được lệnh."}</div> : <div className="grid gap-5 lg:grid-cols-2">
      <div className="space-y-3"><Info label="Khách hàng" value={`${data.order.customerName}${data.order.customerPhone ? ` · ${data.order.customerPhone}` : ""}`} /><Info label="Tài xế" value={data.order.driverName} /><Info label="Số tiền dự kiến" value={`${money(data.order.expectedAmount)} ₫`} /><Info label="Đã thu" value={data.order.collectedAmount ? `${money(data.order.collectedAmount)} ₫` : "Chưa thu"} />{data.order.receivedAmount != null && <Info label="Thủ quỹ kiểm đếm" value={`${money(data.order.receivedAmount)} ₫`} />}<Info label="Ngày thu / hạn bàn giao" value={`${date(data.order.scheduledDate)} · ${new Date(data.order.handoverDueAt).toLocaleString("vi-VN")}`} /><Info label="Ghi chú" value={data.order.note || "Không có"} />
        {data.order.failureReason && <Info label="Lý do không thu được" value={data.order.failureReason} />}{data.order.cancelReason && <Info label="Lý do hủy" value={data.order.cancelReason} />}
      </div>
      <div><div className="mb-3 flex items-center gap-2 font-black"><History className="h-4 w-4" /> Nhật ký quy trình</div><div className="space-y-3">{data.events.map((event) => <div key={event.id} className="cc-event"><span className="cc-event-dot" /><div><div className="font-bold">{EVENT_LABELS[event.action] ?? event.action}</div><div className="mt-0.5 text-sm text-[var(--gc-text-soft)]">{event.note}</div><div className="mt-1 text-xs text-[var(--gc-text-muted)]">{event.actor} · {new Date(event.occurredAt).toLocaleString("vi-VN")}</div></div></div>)}</div></div>
    </div>}
  </Modal>;
}

function Info({ label, value }: { label: string; value: string }) {
  return <div className="rounded-xl border border-[var(--gc-border)] p-3"><div className="text-xs font-bold text-[var(--gc-text-muted)]">{label}</div><div className="mt-1 font-bold text-[var(--gc-text)]">{value}</div></div>;
}
