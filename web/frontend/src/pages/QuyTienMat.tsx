import { useMemo, useState } from "react";
import {
  ArrowDownLeft,
  ArrowUpRight,
  Ban,
  BookOpen,
  HandCoins,
  Loader2,
  PiggyBank,
  Plus,
  RefreshCw,
  Search,
  TrendingDown,
  TrendingUp,
} from "lucide-react";
import { GlassPanel } from "../components/glass/GlassPanel";
import { LiquidTabs, type LiquidTab } from "../components/glass/LiquidTabs";
import { DateTimePicker, MonthPicker } from "../components/DateField";
import { Modal } from "../components/Modal";
import { CashFundBalanceCard } from "../components/CashFundBalanceCard";
import { useAppNotifications } from "../components/app-notifications-context";
import { Badge, Button, EmptyState, Field, Input, MoneyInput, Select } from "../components/ui";
import { StatCard } from "../features/giacong/StatCard";
import { PERM, useAccess } from "../lib/access";
import { api } from "../lib/api";
import { dateTime, money, moneyVnd } from "../lib/format";
import { useApi } from "../lib/useApi";
import "../features/giacong/giacong.css";

const TABS: LiquidTab[] = [
  { key: "ledger", label: "Sổ quỹ" },
  { key: "manual", label: "Bút toán thủ công" },
];

/** Nguồn sinh ra một dòng sổ quỹ — quyết định nhãn và nơi phải sửa nếu số sai. */
const SOURCES: Record<string, { label: string; color: string }> = {
  collection: { label: "Lệnh thu tiền", color: "success" },
  payout: { label: "Phiếu chi tiền mặt", color: "danger" },
  receipt: { label: "Phiếu thu", color: "accent" },
  payment: { label: "Phiếu chi", color: "warning" },
  manual: { label: "Ghi tay", color: "muted" },
};

interface LedgerEntry {
  sourceId: string;
  sourceKind: string;
  sourceRef: string;
  direction: "in" | "out";
  amount: number;
  occurredAt: string;
  reason: string;
  counterparty: string;
  actor: string;
  note: string;
  balanceAfter: number;
}

interface LedgerPage {
  month: string;
  openingBalance: number;
  totalIn: number;
  totalOut: number;
  closingBalance: number;
  entries: LedgerEntry[];
}

interface ManualEntry {
  id: string;
  entryNo: string;
  direction: "in" | "out";
  amount: number;
  occurredAt: string;
  reason: string;
  counterparty: string;
  note: string;
  isOpening: boolean;
  createdBy: string;
  createdAt: string;
  reversedAt?: string | null;
  reversedBy: string;
  reverseReason: string;
}

const currentMonth = () => new Date().toISOString().slice(0, 7);

/** "2026-08-26T09:30" cho input datetime-local, theo giờ máy chứ không phải UTC. */
function localDateTimeInput() {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}`;
}

/**
 * QUỸ TIỀN MẶT — sổ theo dõi tiền thật trong két.
 *
 * Sổ hợp nhất bốn nguồn (lệnh thu đã hoàn tất, phiếu chi tiền mặt đã chi, phiếu thu/chi ở trang Thu
 * chi, và bút toán ghi tay). Dòng sinh từ chứng từ KHÔNG sửa được ở đây — muốn đổi phải về đúng
 * chứng từ gốc, nhờ vậy sổ quỹ không bao giờ nói khác chứng từ.
 */
export function QuyTienMat() {
  const { can } = useAccess();
  const { notify } = useAppNotifications();
  const [tab, setTab] = useState("ledger");
  const [month, setMonth] = useState(currentMonth);
  const [direction, setDirection] = useState("");
  const [source, setSource] = useState("");
  const [query, setQuery] = useState("");
  const [creating, setCreating] = useState(false);
  const [reversing, setReversing] = useState<ManualEntry | null>(null);

  const canManage = can(PERM.cashFundManage);
  const ledgerPath = `/api/cash-fund?month=${month}${direction ? `&direction=${direction}` : ""}${source ? `&source=${source}` : ""}`;
  const { data, loading, error, reload } = useApi<LedgerPage>(ledgerPath);
  const { data: manual, reload: reloadManual } = useApi<{ entries: ManualEntry[] }>(
    `/api/cash-fund/entries?month=${month}`,
  );

  const refreshAll = () => { reload(); reloadManual(); };

  // Lọc chữ ở phía trình duyệt: một tháng chỉ vài trăm dòng, gọi lại máy chủ theo từng phím gõ
  // vừa chậm vừa không cần thiết.
  const rows = useMemo(() => {
    const q = query.trim().toLocaleLowerCase("vi");
    const list = data?.entries ?? [];
    const matched = q
      ? list.filter((row) => [row.sourceRef, row.reason, row.counterparty, row.actor]
          .some((value) => (value ?? "").toLocaleLowerCase("vi").includes(q)))
      : list;
    return [...matched].reverse();
  }, [data, query]);

  const reverseEntry = async (entry: ManualEntry, reason: string) => {
    try {
      await api.post(`/api/cash-fund/entries/${entry.id}/reverse`, { reason });
      notify.success(`Đã hủy bút toán ${entry.entryNo}.`, "Quỹ tiền mặt");
      setReversing(null);
      refreshAll();
    } catch (cause) {
      notify.error(cause instanceof Error ? cause.message : "Không hủy được bút toán.");
    }
  };

  return (
    <div className="gc-root space-y-5">
      <div className="flex flex-col justify-between gap-3 md:flex-row md:items-end">
        <div>
          <h1 className="text-[1.75rem] font-black text-[var(--gc-text)]">Quỹ tiền mặt</h1>
          <p className="mt-1 text-sm font-semibold text-[var(--gc-text-muted)]">
            Hợp nhất tiền vào từ lệnh thu, tiền ra từ phiếu chi và phiếu thu/chi nghiệp vụ — cộng bút toán ghi tay.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="soft" onClick={refreshAll}><RefreshCw className="h-4 w-4" /> Làm mới</Button>
        </div>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <CashFundBalanceCard index={0} month={month} />
        <StatCard index={1} icon={TrendingUp} label="Tiền vào trong tháng" value={moneyVnd(data?.totalIn ?? 0)} tone="5, 150, 105" />
        <StatCard index={2} icon={TrendingDown} label="Tiền ra trong tháng" value={moneyVnd(data?.totalOut ?? 0)} tone="225, 29, 72" />
        <StatCard index={3} icon={PiggyBank} label="Số dư đầu kỳ" value={moneyVnd(data?.openingBalance ?? 0)} sub={`Cuối kỳ ${moneyVnd(data?.closingBalance ?? 0)}`} tone="88, 112, 152" />
      </div>

      <GlassPanel strong className="overflow-hidden">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--gc-border)] p-4">
          <LiquidTabs tabs={TABS} value={tab} onChange={setTab} />
          <MonthPicker value={month} onChange={(next) => setMonth(next || currentMonth())} clearable={false} ariaLabel="Lọc theo tháng" />
        </div>

        {tab === "ledger" ? (
          <>
            <div className="flex flex-col gap-3 border-b border-[var(--gc-border)] p-4 lg:flex-row lg:items-center">
              <div className="relative min-w-0 flex-1">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--gc-text-muted)]" />
                <Input className="pl-9" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Tìm số chứng từ, diễn giải hoặc đối tượng" />
              </div>
              <Select value={direction} onChange={(e) => setDirection(e.target.value)} className="min-w-[150px]">
                <option value="">Cả thu và chi</option>
                <option value="in">Chỉ tiền vào</option>
                <option value="out">Chỉ tiền ra</option>
              </Select>
              <Select value={source} onChange={(e) => setSource(e.target.value)} className="min-w-[190px]">
                <option value="">Mọi nguồn chứng từ</option>
                {Object.entries(SOURCES).map(([key, meta]) => <option key={key} value={key}>{meta.label}</option>)}
              </Select>
            </div>

            <div className="gc-scroll overflow-auto">
              <table className="gc-table min-w-[1040px]">
                <thead>
                  <tr>
                    <th>Thời điểm</th><th>Chứng từ</th><th>Diễn giải</th><th>Đối tượng</th>
                    <th className="text-right">Tiền vào</th><th className="text-right">Tiền ra</th><th className="text-right">Tồn quỹ</th>
                  </tr>
                </thead>
                <tbody>
                  {loading && !data ? (
                    <tr><td colSpan={7} className="py-16 text-center"><Loader2 className="mx-auto h-6 w-6 animate-spin" /></td></tr>
                  ) : error ? (
                    <tr><td colSpan={7} className="py-16 text-center font-semibold text-rose-500">{error}</td></tr>
                  ) : rows.length === 0 ? (
                    <tr><td colSpan={7}><EmptyState icon={<BookOpen className="h-8 w-8" />} title="Tháng này chưa có phát sinh tiền mặt." hint="Đổi tháng ở góc trên hoặc ghi một bút toán thủ công." /></td></tr>
                  ) : rows.map((row) => {
                    const meta = SOURCES[row.sourceKind] ?? { label: row.sourceKind, color: "muted" };
                    return (
                      <tr key={`${row.sourceKind}-${row.sourceId}`}>
                        <td className="whitespace-nowrap text-sm">{dateTime(row.occurredAt)}</td>
                        <td>
                          <div className="font-black text-[var(--gc-text)]">{row.sourceRef || "—"}</div>
                          <div className="mt-1"><Badge color={meta.color}>{meta.label}</Badge></div>
                        </td>
                        <td>
                          <div className="font-semibold text-[var(--gc-text)]">{row.reason || "—"}</div>
                          {row.note && <div className="mt-0.5 text-xs text-[var(--gc-text-muted)]">{row.note}</div>}
                        </td>
                        <td>
                          <div className="font-semibold">{row.counterparty || "—"}</div>
                          {row.actor && <div className="text-xs text-[var(--gc-text-muted)]">{row.actor}</div>}
                        </td>
                        <td className="text-right font-black tabular-nums text-emerald-600 dark:text-emerald-400">
                          {row.direction === "in" ? `${money(row.amount)} ₫` : "—"}
                        </td>
                        <td className="text-right font-black tabular-nums text-rose-600 dark:text-rose-400">
                          {row.direction === "out" ? `${money(row.amount)} ₫` : "—"}
                        </td>
                        <td className="text-right font-bold tabular-nums">{money(row.balanceAfter)} ₫</td>
                      </tr>
                    );
                  })}
                </tbody>
                {data && rows.length > 0 && (
                  <tfoot>
                    <tr className="bg-black/[0.035] dark:bg-white/[0.05]">
                      <td colSpan={4} className="font-black">Cộng tháng {month}</td>
                      <td className="text-right font-black tabular-nums text-emerald-600 dark:text-emerald-400">{money(data.totalIn)} ₫</td>
                      <td className="text-right font-black tabular-nums text-rose-600 dark:text-rose-400">{money(data.totalOut)} ₫</td>
                      <td className="text-right font-black tabular-nums">{money(data.closingBalance)} ₫</td>
                    </tr>
                  </tfoot>
                )}
              </table>
            </div>
          </>
        ) : (
          <>
            {/* Nút ghi tay nằm NGAY trong tab của nó: người dùng đang xem danh sách bút toán thủ công
                thì việc "ghi thêm một bút toán" phải ở ngay tầm mắt, không phải ở đầu trang. */}
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--gc-border)] p-4">
              <div className="text-sm font-semibold text-[var(--gc-text-muted)]">
                Bút toán ghi tay dùng cho tiền ra/vào không có chứng từ nào khác trong hệ thống.
              </div>
              {canManage
                ? <Button onClick={() => setCreating(true)}><Plus className="h-4 w-4" /> Tạo bút toán ghi tay</Button>
                : <Badge color="muted">Bạn không có quyền ghi bút toán quỹ</Badge>}
            </div>

            <div className="gc-scroll overflow-auto">
            <table className="gc-table min-w-[980px]">
              <thead>
                <tr><th>Bút toán</th><th>Thời điểm</th><th>Diễn giải</th><th>Người ghi</th><th className="text-right">Số tiền</th><th /></tr>
              </thead>
              <tbody>
                {(manual?.entries ?? []).length === 0 ? (
                  <tr><td colSpan={6}><EmptyState icon={<HandCoins className="h-8 w-8" />} title="Tháng này chưa có bút toán ghi tay." hint="Bấm “Tạo bút toán ghi tay” ở trên để ghi khoản tiền ra/vào đầu tiên." /></td></tr>
                ) : (manual?.entries ?? []).map((entry) => (
                  <tr key={entry.id} className={entry.reversedAt ? "opacity-60" : ""}>
                    <td>
                      <div className="font-black text-[var(--gc-text)]">{entry.entryNo}</div>
                      <div className="mt-1 flex flex-wrap gap-1">
                        <Badge color={entry.direction === "in" ? "success" : "danger"}>
                          {entry.direction === "in" ? <ArrowDownLeft className="h-3 w-3" /> : <ArrowUpRight className="h-3 w-3" />}
                          {entry.direction === "in" ? "Thu" : "Chi"}
                        </Badge>
                        {entry.isOpening && <Badge color="purple">Đầu kỳ</Badge>}
                        {entry.reversedAt && <Badge color="muted">Đã hủy</Badge>}
                      </div>
                    </td>
                    <td className="whitespace-nowrap text-sm">{dateTime(entry.occurredAt)}</td>
                    <td>
                      <div className="font-semibold text-[var(--gc-text)]">{entry.reason}</div>
                      {entry.counterparty && <div className="text-xs text-[var(--gc-text-muted)]">{entry.counterparty}</div>}
                      {entry.note && <div className="mt-0.5 text-xs text-[var(--gc-text-muted)]">{entry.note}</div>}
                      {entry.reversedAt && <div className="mt-1 text-xs font-semibold text-rose-500">Hủy bởi {entry.reversedBy}: {entry.reverseReason}</div>}
                    </td>
                    <td className="text-sm font-semibold">{entry.createdBy}</td>
                    <td className={`text-right font-black tabular-nums ${entry.reversedAt ? "text-[var(--gc-text-muted)] line-through" : entry.direction === "in" ? "text-emerald-600 dark:text-emerald-400" : "text-rose-600 dark:text-rose-400"}`}>
                      {money(entry.amount)} ₫
                    </td>
                    <td className="text-right">
                      {canManage && !entry.reversedAt && (
                        <Button variant="ghost" className="px-3 py-2" onClick={() => setReversing(entry)}>
                          <Ban className="h-4 w-4" /> Hủy
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            </div>
          </>
        )}
      </GlassPanel>

      {creating && <ManualEntryModal onClose={() => setCreating(false)} onSaved={() => { setCreating(false); refreshAll(); }} />}
      {reversing && <ReverseEntryModal entry={reversing} onClose={() => setReversing(null)} onConfirm={reverseEntry} />}
    </div>
  );
}

function ManualEntryModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const { notify } = useAppNotifications();
  const [direction, setDirection] = useState("in");
  const [amount, setAmount] = useState("");
  const [occurredAt, setOccurredAt] = useState(localDateTimeInput);
  const [reason, setReason] = useState("");
  const [counterparty, setCounterparty] = useState("");
  const [note, setNote] = useState("");
  const [isOpening, setIsOpening] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const save = async () => {
    const parsed = Number(amount.replace(/[^\d]/g, ""));
    if (!Number.isSafeInteger(parsed) || parsed <= 0) { setError("Vui lòng nhập số tiền hợp lệ."); return; }
    if (!isOpening && !reason.trim()) { setError("Vui lòng nhập lý do thu/chi."); return; }
    setSaving(true); setError("");
    try {
      const result = await api.post<{ entryNo: string }>("/api/cash-fund/entries", {
        direction: isOpening ? "in" : direction,
        amount: parsed,
        occurredAt: new Date(occurredAt).toISOString(),
        reason: isOpening ? "Số dư đầu kỳ" : reason.trim(),
        counterparty: counterparty.trim(),
        note: note.trim(),
        isOpening,
      });
      notify.success(`Đã ghi bút toán ${result.entryNo}.`, "Quỹ tiền mặt");
      onSaved();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Không ghi được bút toán.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal open panel title="Ghi bút toán quỹ tiền mặt" onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Đóng</Button><Button loading={saving} onClick={() => void save()}>Ghi vào quỹ</Button></>}>
      <div className="space-y-3">
        <label className="flex items-start gap-2.5 rounded-xl border border-[var(--gc-border)] p-3">
          <input type="checkbox" className="mt-0.5" checked={isOpening} onChange={(e) => setIsOpening(e.target.checked)} />
          <span>
            <span className="block text-sm font-bold text-[var(--gc-text)]">Đây là số dư đầu kỳ</span>
            <span className="mt-0.5 block text-xs font-semibold text-[var(--gc-text-muted)]">
              Số tiền đang có sẵn trong két lúc bắt đầu dùng sổ quỹ. Chỉ khai được một lần.
            </span>
          </span>
        </label>

        {!isOpening && (
          <Field label="Chiều tiền *">
            <Select className="w-full" value={direction} onChange={(e) => setDirection(e.target.value)}>
              <option value="in">Thu — tiền vào quỹ</option>
              <option value="out">Chi — tiền ra khỏi quỹ</option>
            </Select>
          </Field>
        )}

        <Field label="Số tiền (₫) *">
          <MoneyInput value={amount} onChange={setAmount} placeholder="Ví dụ: 10.000.000" />
        </Field>
        <Field label="Thời điểm phát sinh *">
          <DateTimePicker
            value={occurredAt}
            onChange={(next) => setOccurredAt(next || localDateTimeInput())}
            className="w-full"
          />
        </Field>
        {!isOpening && (
          <>
            <Field label="Lý do *">
              <Input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Ví dụ: Rút tiền ngân hàng về quỹ" />
            </Field>
            <Field label="Người nộp / người nhận">
              <Input value={counterparty} onChange={(e) => setCounterparty(e.target.value)} placeholder="Không bắt buộc" />
            </Field>
          </>
        )}
        <Field label="Ghi chú">
          <Input value={note} onChange={(e) => setNote(e.target.value)} placeholder="Không bắt buộc" />
        </Field>
        {error && <div className="rounded-xl bg-rose-500/10 p-3 text-sm font-semibold text-rose-600 dark:text-rose-400">{error}</div>}
      </div>
    </Modal>
  );
}

function ReverseEntryModal({ entry, onClose, onConfirm }: {
  entry: ManualEntry;
  onClose: () => void;
  onConfirm: (entry: ManualEntry, reason: string) => Promise<void>;
}) {
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);

  return (
    <Modal open panel title={`Hủy bút toán ${entry.entryNo}`} onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Đóng</Button>
        <Button variant="danger" loading={saving} disabled={!reason.trim()}
          onClick={() => { setSaving(true); void onConfirm(entry, reason.trim()).finally(() => setSaving(false)); }}>
          Hủy bút toán
        </Button></>}>
      <div className="space-y-3">
        <p className="text-sm font-semibold text-[var(--gc-text-soft)]">
          Bút toán bị hủy sẽ không còn tính vào tồn quỹ, nhưng vẫn nằm lại trong danh sách để tra soát.
          Số tiền: <strong className="text-[var(--gc-text)]">{moneyVnd(entry.amount)}</strong> · {entry.reason}
        </p>
        <Field label="Lý do hủy *">
          <Input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Ví dụ: Nhập nhầm số tiền" />
        </Field>
      </div>
    </Modal>
  );
}
