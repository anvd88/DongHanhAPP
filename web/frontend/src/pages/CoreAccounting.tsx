import { Fragment, useCallback, useEffect, useMemo, useState, type ComponentType } from "react";
import {
  ArrowRight, BadgeCheck, Banknote, BookOpen, Bot, Boxes, BriefcaseBusiness,
  Building2, CalendarClock, Check, ChevronRight, CircleDollarSign,
  FileChartColumn, FilePlus2, Landmark, LayoutDashboard, Lock, LockOpen, Plus,
  RefreshCw, Scale, Search, Settings2, ShieldCheck, Sparkles, TrendingDown,
  TrendingUp, TriangleAlert, WalletCards, X,
} from "lucide-react";
import { api, ApiError } from "../lib/api";
import { useAccess, PERM } from "../lib/access";
import { subscribeRealtime } from "../lib/realtime";
import { useAppNotifications } from "../components/app-notifications-context";
import { Modal } from "../components/Modal";
import { DatePicker, MonthPicker } from "../components/DateField";
import { Badge, Button, Field, Input, Select, Spinner } from "../components/ui";
import "./core-accounting.css";

type Overview = {
  period: string;
  periodStatus: "Open" | "Locked";
  total: number;
  draft: number;
  posted: number;
  reconciliationIssues: number;
  revenue: number;
  expenses: number;
  profit: number;
  assets: number;
  liabilities: number;
  equity: number;
  cashFlow: number;
  vatInput: number;
  vatOutput: number;
  costOfGoods: number;
  budget: number;
  budgetUsed: number;
  recent: Entry[];
};

type Account = {
  code: string;
  name: string;
  type: "Asset" | "Liability" | "Equity" | "Revenue" | "Expense";
  normalSide: "Debit" | "Credit";
  parentCode: string;
  isActive: boolean;
  isSystem: boolean;
  balance: number;
};

type Entry = {
  id: string;
  entryNo: string;
  entryDate: string;
  description: string;
  reference: string;
  sourceModule: string;
  status: "Draft" | "Posted" | "Reversed";
  total: number;
  createdBy?: string;
  postedBy?: string;
};

type EntryLine = {
  entryId: string;
  id: number;
  lineNo: number;
  accountCode: string;
  accountName: string;
  description: string;
  debit: number;
  credit: number;
  partner: string;
  costCenter: string;
};

type Period = {
  period: string;
  status: "Open" | "Locked";
  lockedAt?: string | null;
  lockedBy: string;
  reopenedAt?: string | null;
  reopenedBy: string;
  reopenReason: string;
  draftCount: number;
  postedCount: number;
};

type Reconciliation = {
  id: string;
  period: string;
  kind: "Receivable" | "Payable" | "Bank" | "Inventory";
  subject: string;
  bookBalance: number;
  subledgerBalance: number;
  difference: number;
  status: "Matched" | "Unmatched" | "Investigating";
  note: string;
  checkedBy: string;
  checkedAt: string;
};

type Budget = {
  id: string;
  period: string;
  accountCode: string;
  accountName: string;
  department: string;
  amount: number;
  actual: number;
  variance: number;
};

type Automation = {
  module: string;
  name: string;
  debit: string;
  credit: string;
  trigger: string;
};

type Tab = "overview" | "accounts" | "journal" | "automation" | "reconcile" | "tax-budget" | "reports" | "periods";

const tabs: { key: Tab; label: string; icon: ComponentType<{ className?: string }> }[] = [
  { key: "overview", label: "Tổng quan", icon: LayoutDashboard },
  { key: "accounts", label: "Hệ thống tài khoản", icon: BookOpen },
  { key: "journal", label: "Nhật ký & sổ cái", icon: FileChartColumn },
  { key: "automation", label: "Tự động định khoản", icon: Bot },
  { key: "reconcile", label: "Đối chiếu", icon: Scale },
  { key: "tax-budget", label: "Thuế & ngân sách", icon: CircleDollarSign },
  { key: "reports", label: "Báo cáo tài chính", icon: BriefcaseBusiness },
  { key: "periods", label: "Khóa kỳ", icon: CalendarClock },
];

const accountTypeLabels: Record<Account["type"], string> = {
  Asset: "Tài sản",
  Liability: "Nợ phải trả",
  Equity: "Vốn chủ sở hữu",
  Revenue: "Doanh thu",
  Expense: "Chi phí",
};

const moduleLabels: Record<string, string> = {
  Manual: "Thủ công", Sales: "Bán hàng", Purchases: "Mua hàng",
  Inventory: "Kho", Cash: "Thu chi", Payroll: "Lương", Assets: "Tài sản",
};

const reconciliationMeta: Record<Reconciliation["kind"], { label: string; icon: ComponentType<{ className?: string }> }> = {
  Receivable: { label: "Công nợ phải thu", icon: WalletCards },
  Payable: { label: "Công nợ phải trả", icon: Building2 },
  Bank: { label: "Ngân hàng", icon: Landmark },
  Inventory: { label: "Tồn kho", icon: Boxes },
};

const currentPeriod = () => {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`;
};

const today = () => {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
};

const vnd = (value: number) =>
  new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 0 }).format(Number(value) || 0) + " ₫";

const shortVnd = (value: number) => {
  const n = Number(value) || 0;
  if (Math.abs(n) >= 1_000_000_000) return `${(n / 1_000_000_000).toLocaleString("vi-VN", { maximumFractionDigits: 1 })} tỷ`;
  if (Math.abs(n) >= 1_000_000) return `${(n / 1_000_000).toLocaleString("vi-VN", { maximumFractionDigits: 1 })} tr`;
  return vnd(n);
};

const displayPeriod = (period: string) => {
  const [year, month] = period.split("-");
  return `Tháng ${Number(month)}/${year}`;
};

const errorMessage = (error: unknown) =>
  error instanceof ApiError ? error.message : error instanceof Error ? error.message : "Không thể hoàn tất thao tác.";

export function CoreAccounting() {
  const { can } = useAccess();
  const { notify, confirm } = useAppNotifications();
  const canApprove = can(PERM.vouchersApprove);
  const [period, setPeriod] = useState(currentPeriod);
  const [tab, setTab] = useState<Tab>("overview");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [overview, setOverview] = useState<Overview | null>(null);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [entries, setEntries] = useState<Entry[]>([]);
  const [lines, setLines] = useState<EntryLine[]>([]);
  const [periods, setPeriods] = useState<Period[]>([]);
  const [reconciliations, setReconciliations] = useState<Reconciliation[]>([]);
  const [budgets, setBudgets] = useState<Budget[]>([]);
  const [automations, setAutomations] = useState<Automation[]>([]);
  const [search, setSearch] = useState("");
  const [entryModal, setEntryModal] = useState(false);
  const [accountModal, setAccountModal] = useState(false);
  const [reconcileModal, setReconcileModal] = useState(false);
  const [budgetModal, setBudgetModal] = useState(false);
  const [reopenModal, setReopenModal] = useState(false);

  const load = useCallback(async (quiet = false) => {
    if (!quiet) setLoading(true);
    try {
      const [o, a, j, p, r, b, rules] = await Promise.all([
        api.get<Overview>(`/api/core-accounting/overview?period=${period}`),
        api.get<Account[]>(`/api/core-accounting/accounts?period=${period}`),
        api.get<{ entries: Entry[]; lines: EntryLine[] }>(
          `/api/core-accounting/entries?period=${period}&search=${encodeURIComponent(search)}`,
        ),
        api.get<Period[]>("/api/core-accounting/periods"),
        api.get<Reconciliation[]>(`/api/core-accounting/reconciliations?period=${period}`),
        api.get<Budget[]>(`/api/core-accounting/budgets?period=${period}`),
        api.get<Automation[]>("/api/core-accounting/automation"),
      ]);
      setOverview(o);
      setAccounts(a);
      setEntries(j.entries);
      setLines(j.lines);
      setPeriods(p);
      setReconciliations(r);
      setBudgets(b);
      setAutomations(rules);
    } catch (error) {
      notify.error(errorMessage(error), "Không tải được kế toán lõi");
    } finally {
      setLoading(false);
    }
  }, [notify, period, search]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), search ? 250 : 0);
    return () => window.clearTimeout(timer);
  }, [load, search]);

  useEffect(() => {
    let timer: number | undefined;
    const unsubscribe = subscribeRealtime(() => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => void load(true), 250);
    }, ["data"]);
    return () => {
      unsubscribe();
      window.clearTimeout(timer);
    };
  }, [load]);

  const runAutomation = async (module?: string) => {
    setBusy(true);
    try {
      const result = await api.post<{ created: number }>("/api/core-accounting/automation/run", { period, module: module ?? "" });
      notify.success(
        result.created ? `Đã tạo và ghi sổ ${result.created} bút toán mới.` : "Không có chứng từ mới cần định khoản.",
        "Tự động định khoản",
      );
      await load(true);
    } catch (error) {
      notify.error(errorMessage(error));
    } finally {
      setBusy(false);
    }
  };

  const lockPeriod = async () => {
    if (!overview || !canApprove) return;
    const ok = await confirm({
      title: `Khóa ${displayPeriod(period)}?`,
      description: "Sau khi khóa, hệ thống từ chối mọi bút toán mới và thao tác ghi sổ trong kỳ.",
      confirmLabel: "Khóa kỳ",
      tone: "danger",
    });
    if (!ok) return;
    setBusy(true);
    try {
      await api.post(`/api/core-accounting/periods/${period}/lock`);
      notify.success(`${displayPeriod(period)} đã được khóa an toàn.`);
      await load(true);
    } catch (error) {
      notify.error(errorMessage(error));
    } finally {
      setBusy(false);
    }
  };

  if (loading && !overview) return <Spinner />;

  const isLocked = overview?.periodStatus === "Locked";

  return (
    <div className="core-accounting">
      <section className="ca-hero">
        <div>
          <div className="ca-eyebrow"><ShieldCheck className="h-4 w-4" /> SỔ KẾ TOÁN TẬP TRUNG</div>
          <h1>Kế toán lõi</h1>
          <p>Một nguồn dữ liệu duy nhất cho tài khoản, bút toán, đối chiếu và báo cáo tài chính.</p>
        </div>
        <div className="ca-hero-actions">
          <div className="ca-period-picker">
            <span>Kỳ làm việc</span>
            <MonthPicker
              value={period}
              onChange={(next) => setPeriod(next || currentPeriod())}
              clearable={false}
              className="ca-period-field"
              ariaLabel="Chọn kỳ kế toán"
            />
          </div>
          <span className={`ca-period-state ${isLocked ? "is-locked" : "is-open"}`}>
            {isLocked ? <Lock className="h-4 w-4" /> : <LockOpen className="h-4 w-4" />}
            {isLocked ? "Đã khóa" : "Đang mở"}
          </span>
          <Button variant="ghost" className="ca-refresh" onClick={() => void load()} disabled={loading}>
            <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} /> Làm mới
          </Button>
        </div>
      </section>

      <nav className="ca-tabs" aria-label="Phân hệ kế toán lõi">
        {tabs.map((item) => {
          const Icon = item.icon;
          return (
            <button key={item.key} type="button" className={tab === item.key ? "active" : ""} onClick={() => setTab(item.key)}>
              <Icon className="h-4 w-4" /><span>{item.label}</span>
              {item.key === "journal" && overview?.draft ? <em>{overview.draft}</em> : null}
              {item.key === "reconcile" && overview?.reconciliationIssues ? <em>{overview.reconciliationIssues}</em> : null}
            </button>
          );
        })}
      </nav>

      {tab === "overview" && overview && (
        <OverviewPanel overview={overview} setTab={setTab} onAutomation={() => void runAutomation()} busy={busy} />
      )}
      {tab === "accounts" && (
        <AccountsPanel accounts={accounts} period={period} canEdit={canApprove} onCreate={() => setAccountModal(true)} />
      )}
      {tab === "journal" && (
        <JournalPanel
          entries={entries} lines={lines} search={search} setSearch={setSearch}
          locked={isLocked} canApprove={canApprove} onCreate={() => setEntryModal(true)}
          onPost={async (entry) => {
            setBusy(true);
            try {
              await api.post(`/api/core-accounting/entries/${entry.id}/post`);
              notify.success(`${entry.entryNo} đã được ghi vào sổ cái.`);
              await load(true);
            } catch (error) { notify.error(errorMessage(error)); }
            finally { setBusy(false); }
          }}
        />
      )}
      {tab === "automation" && (
        <AutomationPanel rules={automations} locked={isLocked} busy={busy} onRun={(module) => void runAutomation(module)} />
      )}
      {tab === "reconcile" && (
        <ReconcilePanel rows={reconciliations} period={period} onCreate={() => setReconcileModal(true)} />
      )}
      {tab === "tax-budget" && overview && (
        <TaxBudgetPanel overview={overview} budgets={budgets} onCreate={() => setBudgetModal(true)} />
      )}
      {tab === "reports" && overview && (
        <ReportsPanel overview={overview} period={period} />
      )}
      {tab === "periods" && (
        <PeriodsPanel
          periods={periods} selected={period} canApprove={canApprove} busy={busy}
          onSelect={setPeriod} onLock={() => void lockPeriod()} onReopen={() => setReopenModal(true)}
        />
      )}

      {entryModal && <EntryDialog open onClose={() => setEntryModal(false)} accounts={accounts} period={period}
        onSaved={async () => { setEntryModal(false); await load(true); }} />}
      <AccountDialog open={accountModal} onClose={() => setAccountModal(false)}
        onSaved={async () => { setAccountModal(false); await load(true); }} />
      <ReconcileDialog open={reconcileModal} onClose={() => setReconcileModal(false)} period={period}
        onSaved={async () => { setReconcileModal(false); await load(true); }} />
      <BudgetDialog open={budgetModal} onClose={() => setBudgetModal(false)} period={period}
        accounts={accounts.filter((item) => item.type === "Expense")}
        onSaved={async () => { setBudgetModal(false); await load(true); }} />
      <ReopenDialog open={reopenModal} onClose={() => setReopenModal(false)} period={period}
        onSaved={async () => { setReopenModal(false); await load(true); }} />
    </div>
  );
}

function OverviewPanel({ overview, setTab, onAutomation, busy }: {
  overview: Overview; setTab: (tab: Tab) => void; onAutomation: () => void; busy: boolean;
}) {
  const stats = [
    { label: "Doanh thu thuần", value: overview.revenue, icon: TrendingUp, tone: "teal", note: `${overview.posted} bút toán đã ghi sổ` },
    { label: "Tổng chi phí", value: overview.expenses, icon: TrendingDown, tone: "orange", note: `Giá vốn ${shortVnd(overview.costOfGoods)}` },
    { label: "Lợi nhuận kỳ này", value: overview.profit, icon: Sparkles, tone: overview.profit >= 0 ? "blue" : "red", note: overview.revenue ? `Biên ${((overview.profit / overview.revenue) * 100).toFixed(1)}%` : "Chưa có doanh thu" },
    { label: "Dòng tiền thuần", value: overview.cashFlow, icon: Banknote, tone: "purple", note: "Tiền mặt + tiền gửi" },
  ];
  return (
    <div className="ca-panel-stack">
      <section className="ca-stat-grid">
        {stats.map((stat) => {
          const Icon = stat.icon;
          return (
            <article className={`ca-stat ca-tone-${stat.tone}`} key={stat.label}>
              <span className="ca-stat-icon"><Icon className="h-5 w-5" /></span>
              <p>{stat.label}</p><strong>{shortVnd(stat.value)}</strong><small>{stat.note}</small>
            </article>
          );
        })}
      </section>
      <section className="ca-overview-grid">
        <article className="ca-card ca-financial-snapshot">
          <CardTitle eyebrow="TÌNH HÌNH TÀI CHÍNH" title="Ảnh chụp cuối kỳ" action="Xem báo cáo" onAction={() => setTab("reports")} />
          <div className="ca-balance-bars">
            <BalanceBar label="Tài sản" value={overview.assets} max={Math.max(overview.assets, overview.liabilities + overview.equity, 1)} color="var(--ca-teal)" />
            <BalanceBar label="Nợ phải trả" value={overview.liabilities} max={Math.max(overview.assets, overview.liabilities + overview.equity, 1)} color="var(--ca-orange)" />
            <BalanceBar label="Vốn chủ sở hữu" value={overview.equity} max={Math.max(overview.assets, overview.liabilities + overview.equity, 1)} color="var(--ca-blue)" />
          </div>
          <div className="ca-equation">
            <span><small>Tài sản</small><b>{shortVnd(overview.assets)}</b></span>
            <i>=</i>
            <span><small>Nợ phải trả + Vốn</small><b>{shortVnd(overview.liabilities + overview.equity)}</b></span>
          </div>
        </article>
        <article className="ca-card">
          <CardTitle eyebrow="KIỂM SOÁT KỲ" title="Việc cần xử lý" />
          <button className="ca-task-row" onClick={() => setTab("journal")}>
            <span className="warning"><FilePlus2 className="h-4 w-4" /></span>
            <div><b>{overview.draft} bút toán chờ ghi sổ</b><small>Cần hoàn tất trước khi khóa kỳ</small></div><ChevronRight />
          </button>
          <button className="ca-task-row" onClick={() => setTab("reconcile")}>
            <span className={overview.reconciliationIssues ? "danger" : "success"}><Scale className="h-4 w-4" /></span>
            <div><b>{overview.reconciliationIssues} đối chiếu còn lệch</b><small>Công nợ, ngân hàng và tồn kho</small></div><ChevronRight />
          </button>
          <button className="ca-task-row" onClick={onAutomation} disabled={busy || overview.periodStatus === "Locked"}>
            <span className="info"><Bot className="h-4 w-4" /></span>
            <div><b>Đồng bộ định khoản tự động</b><small>Quét chứng từ từ các phân hệ</small></div><RefreshCw className={busy ? "animate-spin" : ""} />
          </button>
        </article>
      </section>
      <section className="ca-card">
        <CardTitle eyebrow="NHẬT KÝ CHUNG" title="Bút toán gần đây" action="Xem tất cả" onAction={() => setTab("journal")} />
        <EntryTable entries={overview.recent} compact />
      </section>
    </div>
  );
}

function AccountsPanel({ accounts, period, canEdit, onCreate }: { accounts: Account[]; period: string; canEdit: boolean; onCreate: () => void }) {
  const [query, setQuery] = useState("");
  const [type, setType] = useState("");
  const filtered = accounts.filter((item) =>
    (!type || item.type === type) && (!query || `${item.code} ${item.name}`.toLowerCase().includes(query.toLowerCase())),
  );
  return (
    <section className="ca-card">
      <CardTitle eyebrow="DANH MỤC DÙNG CHUNG" title={`Hệ thống tài khoản · ${displayPeriod(period)}`}
        action={canEdit ? "Thêm tài khoản" : undefined} onAction={onCreate} icon={Plus} />
      <div className="ca-toolbar">
        <label className="ca-search"><Search /><input placeholder="Tìm theo mã hoặc tên tài khoản…" value={query} onChange={(e) => setQuery(e.target.value)} /></label>
        <Select value={type} onChange={(e) => setType(e.target.value)}>
          <option value="">Tất cả nhóm tài khoản</option>
          {Object.entries(accountTypeLabels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </Select>
        <span className="ca-count">{filtered.length} tài khoản</span>
      </div>
      <div className="ca-table-wrap">
        <table className="ca-table">
          <thead><tr><th>Mã TK</th><th>Tên tài khoản</th><th>Phân loại</th><th>Tính chất</th><th className="num">Số dư trong kỳ</th><th>Trạng thái</th></tr></thead>
          <tbody>
            {filtered.map((item) => (
              <tr key={item.code}>
                <td><span className="ca-account-code">{item.code}</span></td>
                <td><b>{item.name}</b>{item.parentCode && <small className="ca-subline">TK cha: {item.parentCode}</small>}</td>
                <td><span className={`ca-type ca-type-${item.type.toLowerCase()}`}>{accountTypeLabels[item.type]}</span></td>
                <td>{item.normalSide === "Debit" ? "Dư Nợ" : "Dư Có"}</td>
                <td className="num"><b>{vnd(item.balance)}</b></td>
                <td><Badge color={item.isActive ? "success" : "muted"}>{item.isActive ? "Đang dùng" : "Ngừng dùng"}</Badge></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function JournalPanel({ entries, lines, search, setSearch, locked, canApprove, onCreate, onPost }: {
  entries: Entry[]; lines: EntryLine[]; search: string; setSearch: (value: string) => void;
  locked: boolean; canApprove: boolean; onCreate: () => void; onPost: (entry: Entry) => void;
}) {
  const [expanded, setExpanded] = useState<string | null>(null);
  return (
    <section className="ca-card">
      <CardTitle eyebrow="BÚT TOÁN KÉP" title="Nhật ký chung & sổ cái" action="Lập bút toán" onAction={onCreate} icon={Plus} disabled={locked} />
      <div className="ca-toolbar">
        <label className="ca-search"><Search /><input placeholder="Tìm số bút toán, diễn giải, tham chiếu…" value={search} onChange={(e) => setSearch(e.target.value)} /></label>
        {locked && <span className="ca-inline-alert"><Lock /> Kỳ đã khóa</span>}
      </div>
      <div className="ca-table-wrap">
        <table className="ca-table ca-entry-table">
          <thead><tr><th>Số bút toán</th><th>Ngày</th><th>Diễn giải / tham chiếu</th><th>Nguồn</th><th className="num">Tổng tiền</th><th>Trạng thái</th><th /></tr></thead>
          <tbody>
            {entries.map((entry) => {
              const open = expanded === entry.id;
              const entryLines = lines.filter((line) => line.entryId === entry.id);
              return (
                <Fragment key={entry.id}>
                  <tr className={open ? "is-expanded" : ""}>
                    <td><button className="ca-link" onClick={() => setExpanded(open ? null : entry.id)}>{entry.entryNo}</button></td>
                    <td>{new Date(`${entry.entryDate}T00:00:00`).toLocaleDateString("vi-VN")}</td>
                    <td><b>{entry.description}</b><small className="ca-subline">{entry.reference || "Không có tham chiếu"}</small></td>
                    <td>{moduleLabels[entry.sourceModule] ?? entry.sourceModule}</td>
                    <td className="num"><b>{vnd(entry.total)}</b></td>
                    <td><Badge color={entry.status === "Posted" ? "success" : "warning"}>{entry.status === "Posted" ? "Đã ghi sổ" : "Chờ ghi sổ"}</Badge></td>
                    <td>{entry.status === "Draft" && canApprove && !locked ? <button className="ca-mini-action" onClick={() => onPost(entry)}><Check /> Ghi sổ</button> : <button className="ca-chevron" onClick={() => setExpanded(open ? null : entry.id)}><ChevronRight className={open ? "rotate-90" : ""} /></button>}</td>
                  </tr>
                  {open && (
                    <tr className="ca-lines-row"><td colSpan={7}>
                      <div className="ca-lines">
                        {entryLines.map((line) => (
                          <div key={line.id}>
                            <span><b>{line.accountCode}</b> · {line.accountName}<small>{line.partner || line.description || "—"}</small></span>
                            <span className="num">{line.debit ? `Nợ ${vnd(line.debit)}` : `Có ${vnd(line.credit)}`}</span>
                          </div>
                        ))}
                        <footer><span>Tổng cộng</span><b>Nợ {vnd(entry.total)} · Có {vnd(entry.total)}</b></footer>
                      </div>
                    </td></tr>
                  )}
                </Fragment>
              );
            })}
            {!entries.length && <tr><td colSpan={7} className="ca-empty">Chưa có bút toán trong kỳ này.</td></tr>}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function AutomationPanel({ rules, locked, busy, onRun }: { rules: Automation[]; locked: boolean; busy: boolean; onRun: (module: string) => void }) {
  const iconMap: Record<string, ComponentType<{ className?: string }>> = {
    Sales: TrendingUp, Purchases: Building2, Inventory: Boxes, Payroll: WalletCards, Assets: BriefcaseBusiness,
  };
  return (
    <div className="ca-panel-stack">
      <section className="ca-callout">
        <span><Sparkles /></span><div><b>Định khoản xuyên suốt, không nhập lại</b><p>Chứng từ hoàn tất ở các phân hệ nguồn được tạo bút toán cân đối và ghi sổ tự động. Mỗi chứng từ nguồn chỉ được định khoản một lần.</p></div>
        <Button onClick={() => onRun("")} disabled={locked || busy} loading={busy}><RefreshCw className="h-4 w-4" /> Đồng bộ tất cả</Button>
      </section>
      <section className="ca-automation-grid">
        {rules.map((rule) => {
          const Icon = iconMap[rule.module] ?? Settings2;
          return (
            <article className="ca-card ca-rule" key={rule.module}>
              <header><span><Icon /></span><Badge color="success">Đang bật</Badge></header>
              <h3>{rule.name}</h3><p>{rule.trigger}</p>
              <div className="ca-rule-entry"><span>Nợ <b>{rule.debit}</b></span><ArrowRight /><span>Có <b>{rule.credit}</b></span></div>
              <button onClick={() => onRun(rule.module)} disabled={locked || busy}>Chạy ngay <ChevronRight /></button>
            </article>
          );
        })}
      </section>
    </div>
  );
}

function ReconcilePanel({ rows, period, onCreate }: { rows: Reconciliation[]; period: string; onCreate: () => void }) {
  const summary = (Object.keys(reconciliationMeta) as Reconciliation["kind"][]).map((kind) => {
    const items = rows.filter((row) => row.kind === kind);
    return { kind, count: items.length, issues: items.filter((row) => row.status !== "Matched").length };
  });
  return (
    <div className="ca-panel-stack">
      <section className="ca-reconcile-grid">
        {summary.map((item) => {
          const meta = reconciliationMeta[item.kind]; const Icon = meta.icon;
          return <article className="ca-card ca-reconcile-summary" key={item.kind}><span><Icon /></span><div><p>{meta.label}</p><b>{item.issues ? `${item.issues} chênh lệch` : item.count ? "Đã khớp" : "Chưa đối chiếu"}</b></div><Badge color={item.issues ? "danger" : item.count ? "success" : "muted"}>{item.count} lần</Badge></article>;
        })}
      </section>
      <section className="ca-card">
        <CardTitle eyebrow="BIÊN BẢN ĐỐI CHIẾU" title={displayPeriod(period)} action="Ghi nhận đối chiếu" onAction={onCreate} icon={Plus} />
        <div className="ca-table-wrap"><table className="ca-table">
          <thead><tr><th>Loại</th><th>Đối tượng</th><th className="num">Sổ cái</th><th className="num">Sổ chi tiết / thực tế</th><th className="num">Chênh lệch</th><th>Kết quả</th><th>Người kiểm tra</th></tr></thead>
          <tbody>
            {rows.map((row) => <tr key={row.id}>
              <td><b>{reconciliationMeta[row.kind].label}</b></td><td>{row.subject}</td>
              <td className="num">{vnd(row.bookBalance)}</td><td className="num">{vnd(row.subledgerBalance)}</td>
              <td className={`num ${row.difference ? "ca-negative" : "ca-positive"}`}><b>{vnd(row.difference)}</b></td>
              <td><Badge color={row.status === "Matched" ? "success" : row.status === "Investigating" ? "warning" : "danger"}>{row.status === "Matched" ? "Đã khớp" : row.status === "Investigating" ? "Đang xử lý" : "Chưa khớp"}</Badge></td>
              <td>{row.checkedBy || "—"}</td>
            </tr>)}
            {!rows.length && <tr><td colSpan={7} className="ca-empty">Chưa có biên bản đối chiếu cho kỳ này.</td></tr>}
          </tbody>
        </table></div>
      </section>
    </div>
  );
}

function TaxBudgetPanel({ overview, budgets, onCreate }: { overview: Overview; budgets: Budget[]; onCreate: () => void }) {
  const vatPayable = overview.vatOutput - overview.vatInput;
  return (
    <div className="ca-overview-grid ca-tax-grid">
      <section className="ca-card">
        <CardTitle eyebrow="THUẾ GIÁ TRỊ GIA TĂNG" title="GTGT đầu vào / đầu ra" />
        <div className="ca-vat-cards">
          <div><span>Thuế GTGT đầu vào</span><b>{vnd(overview.vatInput)}</b><small>Tài khoản 1331</small></div>
          <div><span>Thuế GTGT đầu ra</span><b>{vnd(overview.vatOutput)}</b><small>Tài khoản 3331</small></div>
        </div>
        <div className={`ca-vat-result ${vatPayable > 0 ? "payable" : ""}`}>
          <span>{vatPayable > 0 ? "Thuế GTGT dự kiến phải nộp" : "Thuế GTGT còn được khấu trừ"}</span>
          <strong>{vnd(Math.abs(vatPayable))}</strong>
        </div>
        <div className="ca-cost-row"><span><Boxes /> Giá vốn hàng bán</span><b>{vnd(overview.costOfGoods)}</b></div>
      </section>
      <section className="ca-card">
        <CardTitle eyebrow="KIỂM SOÁT CHI PHÍ" title="Ngân sách theo tài khoản" action="Thiết lập ngân sách" onAction={onCreate} icon={Plus} />
        <div className="ca-budget-total">
          <span><small>Ngân sách kỳ này</small><b>{vnd(overview.budget)}</b></span>
          <span><small>Đã sử dụng</small><b>{overview.budgetUsed}%</b></span>
        </div>
        <div className="ca-progress"><i style={{ width: `${Math.min(overview.budgetUsed, 100)}%` }} /></div>
        <div className="ca-budget-list">
          {budgets.map((budget) => <div key={budget.id}><span><b>{budget.accountCode} · {budget.accountName}</b><small>{budget.department}</small></span><span className="num"><b>{vnd(budget.actual)}</b><small>/ {vnd(budget.amount)}</small></span></div>)}
          {!budgets.length && <p className="ca-empty">Chưa thiết lập ngân sách cho kỳ này.</p>}
        </div>
      </section>
    </div>
  );
}

function ReportsPanel({ overview, period }: { overview: Overview; period: string }) {
  const reports = [
    { title: "Báo cáo kết quả kinh doanh", subtitle: "Doanh thu, giá vốn, chi phí và lợi nhuận", icon: TrendingUp, rows: [["Doanh thu", overview.revenue], ["Chi phí", overview.expenses], ["Lợi nhuận", overview.profit]] },
    { title: "Bảng cân đối kế toán", subtitle: "Tài sản, nợ phải trả và vốn chủ sở hữu", icon: Scale, rows: [["Tài sản", overview.assets], ["Nợ phải trả", overview.liabilities], ["Vốn chủ sở hữu", overview.equity]] },
    { title: "Báo cáo lưu chuyển tiền tệ", subtitle: "Dòng tiền từ hoạt động kinh doanh", icon: Banknote, rows: [["Lưu chuyển tiền thuần", overview.cashFlow], ["Tiền và tương đương tiền", overview.cashFlow], ["Biến động trong kỳ", overview.cashFlow]] },
  ];
  return (
    <div className="ca-panel-stack">
      <section className="ca-report-heading"><div><span>BỘ BÁO CÁO TÀI CHÍNH</span><h2>{displayPeriod(period)}</h2><p>Số liệu lấy từ các bút toán đã ghi sổ trong kỳ.</p></div><Badge color="success"><BadgeCheck /> Đã cân đối</Badge></section>
      <section className="ca-report-grid">
        {reports.map((report) => {
          const Icon = report.icon;
          return <article className="ca-card ca-report" key={report.title}><header><span><Icon /></span><div><h3>{report.title}</h3><p>{report.subtitle}</p></div></header>
            <div>{report.rows.map(([label, value], index) => <p key={label as string} className={index === report.rows.length - 1 ? "total" : ""}><span>{label}</span><b>{vnd(value as number)}</b></p>)}</div>
            <button onClick={() => window.print()}>In / Lưu PDF <ArrowRight /></button>
          </article>;
        })}
      </section>
    </div>
  );
}

function PeriodsPanel({ periods, selected, canApprove, busy, onSelect, onLock, onReopen }: {
  periods: Period[]; selected: string; canApprove: boolean; busy: boolean;
  onSelect: (period: string) => void; onLock: () => void; onReopen: () => void;
}) {
  const selectedPeriod = periods.find((item) => item.period === selected);
  return (
    <div className="ca-overview-grid ca-period-layout">
      <section className="ca-card">
        <CardTitle eyebrow="KỲ KẾ TOÁN" title="Lịch sử đóng / mở kỳ" />
        <div className="ca-period-list">
          {periods.slice(0, 18).map((item) => <button key={item.period} className={item.period === selected ? "active" : ""} onClick={() => onSelect(item.period)}>
            <span className={item.status === "Locked" ? "locked" : "open"}>{item.status === "Locked" ? <Lock /> : <LockOpen />}</span>
            <div><b>{displayPeriod(item.period)}</b><small>{item.postedCount} đã ghi sổ · {item.draftCount} nháp</small></div>
            <Badge color={item.status === "Locked" ? "muted" : "success"}>{item.status === "Locked" ? "Đã khóa" : "Đang mở"}</Badge>
          </button>)}
        </div>
      </section>
      <section className="ca-card ca-period-control">
        <span className={`ca-big-lock ${selectedPeriod?.status === "Locked" ? "locked" : ""}`}>{selectedPeriod?.status === "Locked" ? <Lock /> : <LockOpen />}</span>
        <p>KIỂM SOÁT KỲ</p><h2>{displayPeriod(selected)}</h2>
        <Badge color={selectedPeriod?.status === "Locked" ? "muted" : "success"}>{selectedPeriod?.status === "Locked" ? "Kỳ đã khóa" : "Kỳ đang mở"}</Badge>
        <div className="ca-period-info">
          <span><small>Bút toán đã ghi sổ</small><b>{selectedPeriod?.postedCount ?? 0}</b></span>
          <span><small>Bút toán chờ xử lý</small><b>{selectedPeriod?.draftCount ?? 0}</b></span>
        </div>
        {selectedPeriod?.lockedAt && <p className="ca-lock-history">Khóa bởi <b>{selectedPeriod.lockedBy}</b> lúc {new Date(selectedPeriod.lockedAt).toLocaleString("vi-VN")}</p>}
        {selectedPeriod?.reopenReason && <p className="ca-lock-history">Lần mở lại gần nhất: {selectedPeriod.reopenReason}</p>}
        {!canApprove ? <div className="ca-permission-note"><ShieldCheck /> Chỉ Kế toán trưởng hoặc người có quyền duyệt chứng từ được đóng/mở kỳ.</div>
          : selectedPeriod?.status === "Locked"
            ? <Button variant="soft" onClick={onReopen} disabled={busy}><LockOpen /> Mở lại kỳ kế toán</Button>
            : <Button variant="danger" onClick={onLock} disabled={busy || Boolean(selectedPeriod?.draftCount)}><Lock /> Khóa kỳ kế toán</Button>}
      </section>
    </div>
  );
}

function EntryTable({ entries, compact = false }: { entries: Entry[]; compact?: boolean }) {
  return <div className="ca-table-wrap"><table className="ca-table"><thead><tr><th>Số bút toán</th><th>Ngày</th><th>Diễn giải</th><th>Nguồn</th><th className="num">Giá trị</th><th>Trạng thái</th></tr></thead>
    <tbody>{entries.map((entry) => <tr key={entry.id}><td><span className="ca-entry-no">{entry.entryNo}</span></td><td>{new Date(`${entry.entryDate}T00:00:00`).toLocaleDateString("vi-VN")}</td><td><b>{entry.description}</b>{!compact && <small className="ca-subline">{entry.reference}</small>}</td><td>{moduleLabels[entry.sourceModule] ?? entry.sourceModule}</td><td className="num"><b>{vnd(entry.total)}</b></td><td><Badge color={entry.status === "Posted" ? "success" : "warning"}>{entry.status === "Posted" ? "Đã ghi sổ" : "Chờ ghi sổ"}</Badge></td></tr>)}
      {!entries.length && <tr><td colSpan={6} className="ca-empty">Chưa có bút toán trong kỳ.</td></tr>}</tbody></table></div>;
}

function CardTitle({ eyebrow, title, action, onAction, icon: Icon, disabled }: {
  eyebrow: string; title: string; action?: string; onAction?: () => void;
  icon?: ComponentType<{ className?: string }>; disabled?: boolean;
}) {
  return <header className="ca-card-title"><div><span>{eyebrow}</span><h2>{title}</h2></div>{action && <button onClick={onAction} disabled={disabled}>{Icon && <Icon />}{action}<ChevronRight /></button>}</header>;
}

function BalanceBar({ label, value, max, color }: { label: string; value: number; max: number; color: string }) {
  return <div><p><span>{label}</span><b>{shortVnd(value)}</b></p><i><em style={{ width: `${Math.max(2, Math.min(100, Math.abs(value) / max * 100))}%`, background: color }} /></i></div>;
}

type DraftLine = { accountCode: string; description: string; debit: string; credit: string; partner: string; costCenter: string };
const blankLine = (): DraftLine => ({ accountCode: "", description: "", debit: "", credit: "", partner: "", costCenter: "" });

function EntryDialog({ open, onClose, accounts, period, onSaved }: { open: boolean; onClose: () => void; accounts: Account[]; period: string; onSaved: () => Promise<void> }) {
  const { notify } = useAppNotifications();
  const [date, setDate] = useState(period === currentPeriod() ? today() : `${period}-01`);
  const [description, setDescription] = useState("");
  const [reference, setReference] = useState("");
  const [saving, setSaving] = useState(false);
  const [draftLines, setDraftLines] = useState<DraftLine[]>([blankLine(), blankLine()]);
  const totals = useMemo(() => draftLines.reduce((sum, line) => ({ debit: sum.debit + Number(line.debit || 0), credit: sum.credit + Number(line.credit || 0) }), { debit: 0, credit: 0 }), [draftLines]);
  const setLine = (index: number, patch: Partial<DraftLine>) => setDraftLines((lines) => lines.map((line, i) => i === index ? { ...line, ...patch } : line));
  const save = async () => {
    setSaving(true);
    try {
      await api.post("/api/core-accounting/entries", {
        entryDate: date, description, reference, sourceModule: "Manual", sourceId: "",
        lines: draftLines.map((line) => ({ ...line, debit: Number(line.debit || 0), credit: Number(line.credit || 0) })),
      });
      notify.success("Bút toán đã được lưu ở trạng thái chờ ghi sổ.");
      await onSaved();
    } catch (error) { notify.error(errorMessage(error)); }
    finally { setSaving(false); }
  };
  return <Modal open={open} onClose={onClose} title="Lập bút toán kế toán" wide panel footer={<><Button variant="ghost" onClick={onClose}>Hủy</Button><Button loading={saving} onClick={() => void save()} disabled={!description || totals.debit <= 0 || Math.abs(totals.debit - totals.credit) >= .01}>Lưu bút toán</Button></>}>
    <div className="ca-form-grid ca-form-grid-3"><Field label="Ngày hạch toán"><DatePicker value={date} onChange={setDate} className="w-full" /></Field><Field label="Số tham chiếu"><Input value={reference} onChange={(e) => setReference(e.target.value)} placeholder="HĐ, phiếu, hợp đồng…" /></Field><Field label="Kỳ kế toán"><Input value={date.slice(0, 7)} disabled /></Field></div>
    <Field label="Diễn giải"><Input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Nội dung nghiệp vụ kinh tế phát sinh" /></Field>
    <div className="ca-entry-editor"><header><span>Tài khoản / diễn giải</span><span>Nợ</span><span>Có</span><span /></header>
      {draftLines.map((line, index) => <div key={index} className="ca-entry-line">
        <div><Select value={line.accountCode} onChange={(e) => setLine(index, { accountCode: e.target.value })}><option value="">Chọn tài khoản</option>{accounts.filter((a) => a.isActive).map((a) => <option key={a.code} value={a.code}>{a.code} · {a.name}</option>)}</Select><Input value={line.description} onChange={(e) => setLine(index, { description: e.target.value })} placeholder="Diễn giải dòng (không bắt buộc)" /></div>
        <Input className="num" type="number" min="0" value={line.debit} onChange={(e) => setLine(index, { debit: e.target.value, credit: e.target.value ? "" : line.credit })} placeholder="0" />
        <Input className="num" type="number" min="0" value={line.credit} onChange={(e) => setLine(index, { credit: e.target.value, debit: e.target.value ? "" : line.debit })} placeholder="0" />
        <button onClick={() => setDraftLines((lines) => lines.length > 2 ? lines.filter((_, i) => i !== index) : lines)}><X /></button>
      </div>)}
      <button className="ca-add-line" onClick={() => setDraftLines((lines) => [...lines, blankLine()])}><Plus /> Thêm dòng hạch toán</button>
      <footer><span>Tổng cộng</span><b>{vnd(totals.debit)}</b><b>{vnd(totals.credit)}</b><span className={Math.abs(totals.debit - totals.credit) < .01 && totals.debit > 0 ? "balanced" : "unbalanced"}>{Math.abs(totals.debit - totals.credit) < .01 && totals.debit > 0 ? "Đã cân" : "Chưa cân"}</span></footer>
    </div>
  </Modal>;
}

function AccountDialog({ open, onClose, onSaved }: { open: boolean; onClose: () => void; onSaved: () => Promise<void> }) {
  const { notify } = useAppNotifications(); const [saving, setSaving] = useState(false);
  const [form, setForm] = useState({ code: "", name: "", type: "Asset", normalSide: "Debit", parentCode: "", isActive: true });
  const save = async () => { setSaving(true); try { await api.post("/api/core-accounting/accounts", form); notify.success("Đã cập nhật hệ thống tài khoản."); await onSaved(); } catch (e) { notify.error(errorMessage(e)); } finally { setSaving(false); } };
  return <Modal open={open} onClose={onClose} title="Thêm tài khoản kế toán" panel footer={<><Button variant="ghost" onClick={onClose}>Hủy</Button><Button loading={saving} onClick={() => void save()} disabled={!form.code || !form.name}>Lưu tài khoản</Button></>}>
    <div className="ca-form-grid"><Field label="Mã tài khoản"><Input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} placeholder="VD: 6421" /></Field><Field label="Tài khoản cha"><Input value={form.parentCode} onChange={(e) => setForm({ ...form, parentCode: e.target.value })} placeholder="Không bắt buộc" /></Field></div>
    <Field label="Tên tài khoản"><Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></Field>
    <div className="ca-form-grid"><Field label="Phân loại"><Select value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value })}>{Object.entries(accountTypeLabels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</Select></Field><Field label="Tính chất số dư"><Select value={form.normalSide} onChange={(e) => setForm({ ...form, normalSide: e.target.value })}><option value="Debit">Dư Nợ</option><option value="Credit">Dư Có</option></Select></Field></div>
  </Modal>;
}

function ReconcileDialog({ open, onClose, period, onSaved }: { open: boolean; onClose: () => void; period: string; onSaved: () => Promise<void> }) {
  const { notify } = useAppNotifications(); const [saving, setSaving] = useState(false);
  const [form, setForm] = useState({ kind: "Receivable", subject: "Tổng hợp", bookBalance: "", subledgerBalance: "", status: "Unmatched", note: "" });
  const save = async () => { setSaving(true); try { await api.post("/api/core-accounting/reconciliations", { ...form, period, bookBalance: Number(form.bookBalance), subledgerBalance: Number(form.subledgerBalance) }); notify.success("Đã lưu kết quả đối chiếu."); await onSaved(); } catch (e) { notify.error(errorMessage(e)); } finally { setSaving(false); } };
  return <Modal open={open} onClose={onClose} title="Ghi nhận đối chiếu" panel footer={<><Button variant="ghost" onClick={onClose}>Hủy</Button><Button loading={saving} onClick={() => void save()}>Lưu đối chiếu</Button></>}>
    <div className="ca-form-grid"><Field label="Loại đối chiếu"><Select value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })}>{Object.entries(reconciliationMeta).map(([key, meta]) => <option key={key} value={key}>{meta.label}</option>)}</Select></Field><Field label="Đối tượng / tài khoản"><Input value={form.subject} onChange={(e) => setForm({ ...form, subject: e.target.value })} /></Field></div>
    <div className="ca-form-grid"><Field label="Số dư sổ cái"><Input type="number" value={form.bookBalance} onChange={(e) => setForm({ ...form, bookBalance: e.target.value })} /></Field><Field label="Số dư sổ chi tiết / thực tế"><Input type="number" value={form.subledgerBalance} onChange={(e) => setForm({ ...form, subledgerBalance: e.target.value })} /></Field></div>
    <Field label="Ghi chú xử lý"><Input value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} placeholder="Nguyên nhân chênh lệch nếu có" /></Field>
  </Modal>;
}

function BudgetDialog({ open, onClose, period, accounts, onSaved }: { open: boolean; onClose: () => void; period: string; accounts: Account[]; onSaved: () => Promise<void> }) {
  const { notify } = useAppNotifications(); const [saving, setSaving] = useState(false);
  const [form, setForm] = useState({ accountCode: "", department: "Toàn công ty", amount: "" });
  const save = async () => { setSaving(true); try { await api.post("/api/core-accounting/budgets", { ...form, period, amount: Number(form.amount) }); notify.success("Đã cập nhật ngân sách."); await onSaved(); } catch (e) { notify.error(errorMessage(e)); } finally { setSaving(false); } };
  return <Modal open={open} onClose={onClose} title={`Thiết lập ngân sách · ${displayPeriod(period)}`} panel footer={<><Button variant="ghost" onClick={onClose}>Hủy</Button><Button loading={saving} onClick={() => void save()} disabled={!form.accountCode}>Lưu ngân sách</Button></>}>
    <Field label="Tài khoản chi phí"><Select value={form.accountCode} onChange={(e) => setForm({ ...form, accountCode: e.target.value })}><option value="">Chọn tài khoản</option>{accounts.map((a) => <option key={a.code} value={a.code}>{a.code} · {a.name}</option>)}</Select></Field>
    <div className="ca-form-grid"><Field label="Bộ phận / trung tâm chi phí"><Input value={form.department} onChange={(e) => setForm({ ...form, department: e.target.value })} /></Field><Field label="Mức ngân sách"><Input type="number" min="0" value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} /></Field></div>
  </Modal>;
}

function ReopenDialog({ open, onClose, period, onSaved }: { open: boolean; onClose: () => void; period: string; onSaved: () => Promise<void> }) {
  const { notify } = useAppNotifications(); const [reason, setReason] = useState(""); const [saving, setSaving] = useState(false);
  const save = async () => { setSaving(true); try { await api.post(`/api/core-accounting/periods/${period}/reopen`, { reason }); notify.success(`${displayPeriod(period)} đã được mở lại và ghi nhật ký.`); await onSaved(); } catch (e) { notify.error(errorMessage(e)); } finally { setSaving(false); } };
  return <Modal open={open} onClose={onClose} title={`Mở lại ${displayPeriod(period)}`} panel footer={<><Button variant="ghost" onClick={onClose}>Hủy</Button><Button loading={saving} onClick={() => void save()} disabled={reason.trim().length < 10}><LockOpen /> Mở lại kỳ</Button></>}>
    <div className="ca-warning-box"><TriangleAlert /><div><b>Thao tác được kiểm soát</b><p>Người thực hiện, thời gian và lý do mở lại kỳ sẽ được lưu vĩnh viễn trong nhật ký.</p></div></div>
    <Field label="Lý do mở lại kỳ (bắt buộc)"><Input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Nêu rõ chứng từ hoặc sai sót cần điều chỉnh…" /></Field>
    <small className="ca-form-hint">Tối thiểu 10 ký tự · hiện có {reason.trim().length}</small>
  </Modal>;
}
