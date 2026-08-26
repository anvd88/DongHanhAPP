import { useEffect, useMemo, useState, type CSSProperties } from "react";
import { MotionConfig, motion } from "motion/react";
import {
  ArrowDownLeft,
  ArrowUpRight,
  BadgeCheck,
  BanknoteArrowDown,
  CalendarClock,
  CircleDollarSign,
  FileText,
  HandCoins,
  Landmark,
  Loader2,
  Search,
  Undo2,
  WalletCards,
} from "lucide-react";
import { DatePicker } from "../components/DateField";
import { ExportExcelButton, type ProgressReport } from "../components/ExportExcelButton";
import { GoodsReturnModal } from "../components/GoodsReturnModal";
import { GlassCapsule } from "../components/glass/GlassCapsule";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Modal } from "../components/Modal";
import { useAppNotifications } from "../components/app-notifications-context";
import { Field, Input, Select } from "../components/ui";
import { Button as GlassButton } from "../shadcn/button";
import { useAccess, PERM } from "../lib/access";
import { api } from "../lib/api";
import { useApi } from "../lib/useApi";
import { date, money } from "../lib/format";
import type { DebtDetail, DebtOverview, DebtSummary, DebtTransaction } from "../lib/types";
import { StatCard } from "../features/giacong/StatCard";
import "../features/giacong/giacong.css";
import "./cong-no.css";

const EASE_IOS = [0.22, 1, 0.36, 1] as const;
const today = () => {
  const now = new Date();
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
};

type DebtFilter = "all" | "outstanding" | "settled" | "advance";

function statusOf(balance: number) {
  if (balance > 0) return { label: "Còn nợ", tone: "225, 29, 72" };
  if (balance < 0) return { label: "Dư có", tone: "124, 70, 255" };
  return { label: "Đã thanh toán", tone: "0, 150, 110" };
}

function transactionLabel(transaction: DebtTransaction) {
  if (transaction.kind === "opening") return "Đầu kỳ";
  if (transaction.kind === "sale") return "Bán hàng";
  if (transaction.kind === "receipt") return "Phiếu thu";
  // Trả HÀNG khác trả TIỀN: cả hai đều ghi Có nhưng đọc sổ phải phân biệt được.
  if (transaction.kind === "return") return "Hàng trả về";
  return "Thu công nợ";
}

function transactionTone(transaction: DebtTransaction) {
  if (transaction.cancelled) return "100, 116, 139";
  if (transaction.kind === "opening") return "124, 70, 255";
  if (transaction.kind === "return") return "245, 158, 11";
  return transaction.debit > 0 ? "225, 29, 72" : "0, 150, 110";
}

function DebtRowsSkeleton({ columns = 5 }: { columns?: number }) {
  return (
    <>
      {Array.from({ length: 7 }).map((_, row) => (
        <tr key={row}>
          {Array.from({ length: columns }).map((__, col) => (
            <td key={col}>
              <div className="gc-skeleton h-4" style={{ width: col === 4 ? "58%" : "82%" }} />
            </td>
          ))}
        </tr>
      ))}
    </>
  );
}

export function CongNo() {
  const access = useAccess();
  const { data, loading, error, reload } = useApi<DebtOverview>("/api/debts");
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<DebtFilter>("all");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [collecting, setCollecting] = useState<DebtSummary | null>(null);
  const [editingOpening, setEditingOpening] = useState<DebtSummary | null>(null);
  const {
    data: detail,
    loading: detailLoading,
    error: detailError,
    reload: reloadDetail,
  } = useApi<DebtDetail>(selectedId ? `/api/debts/${selectedId}` : null, [selectedId]);

  useEffect(() => {
    if (!data?.customers.length) {
      setSelectedId(null);
      return;
    }
    if (selectedId && data.customers.some((item) => item.customer.id === selectedId)) return;
    setSelectedId((data.customers.find((item) => item.balance > 0) ?? data.customers[0]).customer.id);
  }, [data, selectedId]);

  const rows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase("vi");
    return (data?.customers ?? []).filter((item) => {
      const matchesSearch =
        !query ||
        item.customer.name.toLocaleLowerCase("vi").includes(query) ||
        item.customer.phone.toLocaleLowerCase("vi").includes(query) ||
        item.customer.taxCode.toLocaleLowerCase("vi").includes(query);
      const matchesFilter =
        filter === "all" ||
        (filter === "outstanding" && item.balance > 0) ||
        (filter === "settled" && item.balance === 0) ||
        (filter === "advance" && item.balance < 0);
      return matchesSearch && matchesFilter;
    });
  }, [data, filter, search]);

  const selectedSummary =
    data?.customers.find((item) => item.customer.id === selectedId) ?? detail?.summary ?? null;
  const canCollect = access.can(PERM.vouchersCreate);
  const canEditOpening = access.can(PERM.vouchersUpdate);
  // Hàng trả về của ĐƠN CŨ không gắn với chuyến giao nào hôm nay, nên phải vào được từ sổ công nợ
  // chứ không chỉ từ trang phiếu.
  const canReturn = access.can(PERM.accountingAccess);
  const [returning, setReturning] = useState<DebtSummary | null>(null);

  const exportOverview = async (report: ProgressReport) => {
    // Trả về false để nút xuất Excel không chạy hiệu ứng "Đã xuất" khi thật ra chẳng có gì để xuất.
    if (!data?.customers.length) return false;
    const escape = (value: unknown) =>
      String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;");
    // Dựng theo lô và nhường luồng giữa các lô: tiến trình báo ra là số khách hàng ĐÃ dựng xong
    // thật, đồng thời trình duyệt còn kịp vẽ lại thanh thay vì đứng hình tới lúc xong.
    const customers = data.customers;
    const rows: string[] = [];
    const BATCH = 150;
    for (let index = 0; index < customers.length; index += BATCH) {
      for (const item of customers.slice(index, index + BATCH)) {
        rows.push(`
      <tr>
        <td>${escape(item.customer.name)}</td>
        <td>${escape(item.customer.taxCode)}</td>
        <td>${escape(item.openingDate ? date(item.openingDate) : "")}</td>
        <td>${item.openingBalance}</td>
        <td>${item.salesTotal}</td>
        <td>${item.returnsTotal}</td>
        <td>${item.collectedTotal}</td>
        <td>${item.balance}</td>
        <td>${escape(statusOf(item.balance).label)}</td>
      </tr>`);
      }
      report(rows.length, customers.length);
      if (rows.length < customers.length) await new Promise((resolve) => setTimeout(resolve, 0));
    }
    const tableRows = rows.join("");
    const html = `<!doctype html><html><head><meta charset="utf-8"></head><body>
      <table border="1"><thead><tr>
        <th>Khách hàng</th><th>Mã số thuế</th><th>Ngày đầu kỳ</th><th>Nợ đầu kỳ</th>
        <th>Phát sinh bán hàng</th><th>Hàng trả về</th><th>Đã thu</th><th>Dư nợ</th><th>Trạng thái</th>
      </tr></thead><tbody>${tableRows}</tbody></table></body></html>`;
    const url = URL.createObjectURL(new Blob([html], { type: "application/vnd.ms-excel;charset=utf-8" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `tong-hop-cong-no-${today()}.xls`;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  };

  return (
    <MotionConfig reducedMotion="user">
      <div className="gc-root cn-page space-y-4 pb-6">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <motion.h1
              initial={{ opacity: 0, y: 18, scale: 0.985, filter: "blur(10px)" }}
              animate={{ opacity: 1, y: 0, scale: 1, filter: "blur(0px)" }}
              transition={{ duration: 0.48, ease: EASE_IOS }}
              className="text-[1.6rem] font-black leading-tight text-[var(--gc-text)]"
            >
              Công nợ khách hàng
            </motion.h1>
            <motion.p
              initial={{ opacity: 0, y: 12, filter: "blur(8px)" }}
              animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
              transition={{ duration: 0.44, delay: 0.08, ease: EASE_IOS }}
              className="mt-1 text-sm font-semibold text-[var(--gc-text-soft)]"
            >
              Theo dõi tiền bán hàng, số đã thu và dư nợ còn lại theo từng khách hàng.
            </motion.p>
          </div>
          <div className="flex flex-wrap gap-2">
            <ExportExcelButton variant="soft" onExport={exportOverview} disabled={!data?.customers.length} />
          {canEditOpening && selectedSummary && (
            <GlassButton variant="soft" onClick={() => setEditingOpening(selectedSummary)}>
              <Landmark className="h-4 w-4" />
              Nợ đầu kỳ
            </GlassButton>
          )}
          {canCollect && selectedSummary && (
            <motion.div
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.4, delay: 0.14, ease: EASE_IOS }}
            >
              <GlassButton onClick={() => setCollecting(selectedSummary)}>
                <HandCoins className="h-4 w-4" />
                Ghi nhận thu nợ
              </GlassButton>
            </motion.div>
          )}
          </div>
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            index={0}
            icon={Landmark}
            label="Nợ đầu kỳ"
            value={`${money(data?.totalOpeningBalance ?? 0)} ₫`}
            sub="Số dư tại ngày chốt"
            tone="124, 70, 255"
          />
          <StatCard
            index={1}
            icon={FileText}
            label="Tổng bán hàng"
            value={`${money(data?.totalSales ?? 0)} ₫`}
            sub="Phiếu xuất kho còn hiệu lực"
            tone="31, 107, 255"
          />
          <StatCard
            index={2}
            icon={BanknoteArrowDown}
            label="Đã thu"
            value={`${money(data?.totalCollected ?? 0)} ₫`}
            sub="Phiếu thu và thu công nợ"
            tone="0, 184, 148"
          />
          <StatCard
            index={3}
            icon={CircleDollarSign}
            label="Phải thu"
            value={`${money(data?.totalReceivable ?? 0)} ₫`}
            sub={`${money(data?.debtorCount ?? 0)} khách còn nợ`}
            tone="225, 29, 72"
          />
        </div>

        <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(480px,1.05fr)_minmax(0,1.35fr)]">
          <GlassPanel strong className="cn-panel overflow-hidden rounded-[20px]">
            <div className="cn-toolbar flex flex-wrap gap-2 border-b border-[var(--gc-border)] p-3">
              <GlassCapsule className="gc-search min-w-[230px] flex-1 px-4">
                <Search className="mr-2.5 h-[18px] w-[18px] shrink-0 text-[var(--gc-text-muted)]" aria-hidden="true" />
                <input
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Tìm tên, điện thoại, mã số thuế..."
                  aria-label="Tìm khách hàng trong danh sách công nợ"
                />
              </GlassCapsule>
              <Select
                value={filter}
                onChange={(event) => setFilter(event.target.value as DebtFilter)}
                aria-label="Lọc trạng thái công nợ"
                className="cn-filter"
              >
                <option value="all">Tất cả trạng thái</option>
                <option value="outstanding">Còn nợ</option>
                <option value="settled">Đã thanh toán</option>
                <option value="advance">Khách trả trước</option>
              </Select>
            </div>

            <div className="gc-scroll cn-table-scroll max-h-[calc(100vh-440px)] min-h-[340px] overflow-auto">
              <table className="gc-table cn-table cn-customer-table">
                <thead>
                  <tr>
                    <th>Khách hàng</th>
                    <th className="text-right">Đầu kỳ</th>
                    <th className="text-right">Bán hàng</th>
                    <th className="text-right">Đã thu</th>
                    <th className="text-right">Còn lại</th>
                    <th>Trạng thái</th>
                  </tr>
                </thead>
                <tbody>
                  {loading ? (
                    <DebtRowsSkeleton columns={6} />
                  ) : error ? (
                    <tr>
                      <td colSpan={6} className="py-16 text-center text-sm font-semibold text-rose-500">
                        {error}
                      </td>
                    </tr>
                  ) : rows.length === 0 ? (
                    <tr>
                      <td colSpan={6} className="py-16 text-center">
                        <div className="flex flex-col items-center gap-2 text-[var(--gc-text-soft)]">
                          <BadgeCheck className="h-9 w-9 text-emerald-500" />
                          <p className="text-sm font-bold">Không có công nợ phù hợp</p>
                          <p className="text-xs text-[var(--gc-text-muted)]">Thử đổi từ khóa hoặc trạng thái đang lọc.</p>
                        </div>
                      </td>
                    </tr>
                  ) : (
                    rows.map((item) => {
                      const status = statusOf(item.balance);
                      const selected = item.customer.id === selectedId;
                      return (
                        <tr
                          key={item.customer.id}
                          className={selected ? "is-selected" : ""}
                          onClick={() => setSelectedId(item.customer.id)}
                        >
                          <td>
                            <div className="max-w-[220px] truncate font-black text-[var(--gc-text)]">
                              {item.customer.name}
                            </div>
                            <div className="mt-1 text-[0.7rem] font-semibold text-[var(--gc-text-muted)]">
                              {item.invoiceCount} phiếu
                              {item.lastActivityDate ? ` · gần nhất ${date(item.lastActivityDate)}` : ""}
                            </div>
                          </td>
                          <td className="whitespace-nowrap text-right font-bold tabular-nums text-violet-600 dark:text-violet-400">
                            {money(item.openingBalance)} ₫
                          </td>
                          <td className="whitespace-nowrap text-right font-bold tabular-nums">
                            {money(item.salesTotal)} ₫
                          </td>
                          <td className="whitespace-nowrap text-right font-bold tabular-nums text-emerald-600 dark:text-emerald-400">
                            {money(item.collectedTotal)} ₫
                          </td>
                          <td
                            className={`whitespace-nowrap text-right font-black tabular-nums ${
                              item.balance > 0
                                ? "text-rose-600 dark:text-rose-400"
                                : item.balance < 0
                                  ? "text-violet-600 dark:text-violet-400"
                                  : "text-[var(--gc-text-soft)]"
                            }`}
                          >
                            {money(Math.abs(item.balance))} ₫
                          </td>
                          <td>
                            <span className="gc-badge" style={{ "--gc-badge": status.tone } as CSSProperties}>
                              <span className="gc-dot" />
                              {status.label}
                            </span>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </GlassPanel>

          <DebtLedger
            detail={detail}
            loading={detailLoading}
            error={detailError}
            summary={selectedSummary}
            canCollect={canCollect}
            canEditOpening={canEditOpening}
            canReturn={canReturn}
            onCollect={() => selectedSummary && setCollecting(selectedSummary)}
            onEditOpening={() => selectedSummary && setEditingOpening(selectedSummary)}
            onReturn={() => selectedSummary && setReturning(selectedSummary)}
          />

          <GoodsReturnModal
            open={!!returning}
            onClose={() => setReturning(null)}
            customerId={returning?.customer.id}
            customerName={returning?.customer.name ?? ""}
            onSaved={() => {
              setReturning(null);
              reload();
              reloadDetail();
            }}
          />
        </div>

        {collecting && (
          <CollectDebtModal
            debt={collecting}
            onClose={() => setCollecting(null)}
            onSaved={() => {
              setCollecting(null);
              reload({ silent: true });
              reloadDetail({ silent: true });
            }}
          />
        )}
        {editingOpening && (
          <OpeningBalanceModal
            debt={editingOpening}
            onClose={() => setEditingOpening(null)}
            onSaved={() => {
              setEditingOpening(null);
              reload({ silent: true });
              reloadDetail({ silent: true });
            }}
          />
        )}
      </div>
    </MotionConfig>
  );
}

function DebtLedger({
  detail,
  loading,
  error,
  summary,
  canCollect,
  canEditOpening,
  canReturn,
  onCollect,
  onEditOpening,
  onReturn,
}: {
  detail: DebtDetail | null;
  loading: boolean;
  error: string | null;
  summary: DebtSummary | null;
  canCollect: boolean;
  canEditOpening: boolean;
  canReturn: boolean;
  onCollect: () => void;
  onEditOpening: () => void;
  onReturn: () => void;
}) {
  const status = summary ? statusOf(summary.balance) : null;
  return (
    <GlassPanel strong className="cn-panel overflow-hidden rounded-[20px]">
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-[var(--gc-border)] p-4">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <WalletCards className="h-5 w-5 shrink-0 text-[var(--gc-text-muted)]" />
            <h2 className="truncate text-lg font-black text-[var(--gc-text)]">
              {summary?.customer.name ?? "Sổ chi tiết công nợ"}
            </h2>
          </div>
          <p className="mt-1.5 text-xs font-semibold text-[var(--gc-text-muted)]">
            {summary?.customer.phone || summary?.customer.taxCode
              ? [summary.customer.phone, summary.customer.taxCode && `MST ${summary.customer.taxCode}`]
                  .filter(Boolean)
                  .join(" · ")
              : "Chọn khách hàng để xem các khoản phát sinh."}
          </p>
          {summary?.openingDate && (
            <p className="mt-1 inline-flex items-center gap-1.5 text-[0.7rem] font-bold text-violet-600 dark:text-violet-300">
              <CalendarClock className="h-3.5 w-3.5" />
              Sổ tính từ ngày đầu kỳ {date(summary.openingDate)}
            </p>
          )}
        </div>
        {status && (
          <span className="gc-badge" style={{ "--gc-badge": status.tone } as CSSProperties}>
            <span className="gc-dot" />
            {status.label}
          </span>
        )}
      </div>

      {summary && (
        <div className="grid grid-cols-1 gap-2.5 border-b border-[var(--gc-border)] p-4 sm:grid-cols-2 2xl:grid-cols-4">
          <LedgerMetric label="Nợ đầu kỳ" value={summary.openingBalance} icon={Landmark} tone="violet" />
          <LedgerMetric label="Phát sinh nợ" value={summary.salesTotal} icon={ArrowUpRight} tone="rose" />
          {summary.returnsTotal > 0 && (
            <LedgerMetric label="Hàng trả về" value={summary.returnsTotal} icon={Undo2} tone="amber" />
          )}
          <LedgerMetric label="Đã thu" value={summary.collectedTotal} icon={ArrowDownLeft} tone="emerald" />
          <LedgerMetric label={summary.balance < 0 ? "Khách trả trước" : "Dư nợ"} value={Math.abs(summary.balance)} icon={WalletCards} tone="blue" />
        </div>
      )}

      <div className="gc-scroll cn-table-scroll max-h-[calc(100vh-570px)] min-h-[280px] overflow-auto">
        <table className="gc-table cn-table cn-ledger-table">
          <thead>
            <tr>
              <th>Ngày</th>
              <th>Chứng từ</th>
              <th>Diễn giải</th>
              <th className="text-right">Phát sinh nợ</th>
              <th className="text-right">Đã thu</th>
              <th className="text-right">Dư nợ</th>
            </tr>
          </thead>
          <tbody>
            {!summary ? (
              <tr>
                <td colSpan={6} className="py-16 text-center text-sm font-semibold text-[var(--gc-text-soft)]">
                  Chọn một khách hàng trong danh sách bên trái.
                </td>
              </tr>
            ) : loading ? (
              <DebtRowsSkeleton columns={6} />
            ) : error ? (
              <tr>
                <td colSpan={6} className="py-16 text-center text-sm font-semibold text-rose-500">
                  {error}
                </td>
              </tr>
            ) : !detail?.transactions.length ? (
              <tr>
                <td colSpan={6} className="py-16 text-center">
                  <div className="flex flex-col items-center gap-2 text-[var(--gc-text-soft)]">
                    <FileText className="h-9 w-9 opacity-70" />
                    <p className="text-sm font-bold">Chưa có phát sinh công nợ</p>
                    <p className="text-xs text-[var(--gc-text-muted)]">Phiếu bán hàng và các khoản thu sẽ xuất hiện tại đây.</p>
                  </div>
                </td>
              </tr>
            ) : (
              detail.transactions.map((transaction) => (
                <tr key={`${transaction.kind}-${transaction.id}`} className={transaction.cancelled ? "is-cancelled" : ""}>
                  <td className="whitespace-nowrap text-[var(--gc-text-soft)]">{date(transaction.date)}</td>
                  <td>
                    <span
                      className="gc-badge"
                      style={{ "--gc-badge": transactionTone(transaction) } as CSSProperties}
                    >
                      <span className="gc-dot" />
                      {transaction.cancelled ? "Đã hủy" : transactionLabel(transaction)}
                    </span>
                    {transaction.reference && (
                      <div className="mt-1 text-[0.68rem] font-bold text-[var(--gc-text-muted)]">
                        {transaction.reference}
                      </div>
                    )}
                  </td>
                  <td className="min-w-[180px] text-[var(--gc-text-soft)]">
                    {transaction.description || (transaction.kind === "payment" ? "Thu tiền công nợ" : "Không có diễn giải")}
                  </td>
                  <td className="whitespace-nowrap text-right font-bold tabular-nums text-rose-600 dark:text-rose-400">
                    {transaction.debit ? `${money(transaction.debit)} ₫` : "—"}
                  </td>
                  <td className="whitespace-nowrap text-right font-bold tabular-nums text-emerald-600 dark:text-emerald-400">
                    {transaction.credit ? `${money(transaction.credit)} ₫` : "—"}
                  </td>
                  <td className="whitespace-nowrap text-right font-black tabular-nums">
                    {money(Math.abs(transaction.runningBalance))} ₫
                    {transaction.runningBalance < 0 ? " (dư có)" : ""}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {summary && (canCollect || canEditOpening || canReturn) && (
        <div className="flex items-center justify-between gap-3 border-t border-[var(--gc-border)] px-4 py-3">
          <p className="text-xs font-semibold text-[var(--gc-text-muted)]">
            Cập nhật đầu kỳ, nhận hàng khách trả về, hoặc ghi nhận khoản khách đã thanh toán.
          </p>
          <div className="flex gap-2">
            {canReturn && (
              <GlassButton variant="soft" onClick={onReturn}>
                <Undo2 className="h-4 w-4" />
                Hàng trả về
              </GlassButton>
            )}
            {canEditOpening && (
              <GlassButton variant="soft" onClick={onEditOpening}>
                <Landmark className="h-4 w-4" />
                Đầu kỳ
              </GlassButton>
            )}
            {canCollect && (
              <GlassButton onClick={onCollect}>
                <HandCoins className="h-4 w-4" />
                Thu nợ
              </GlassButton>
            )}
          </div>
        </div>
      )}
    </GlassPanel>
  );
}

function LedgerMetric({
  label,
  value,
  icon: Icon,
  tone,
}: {
  label: string;
  value: number;
  icon: typeof ArrowUpRight;
  tone: "rose" | "emerald" | "violet" | "blue" | "amber";
}) {
  const color = {
    rose: "text-rose-600 dark:text-rose-400 bg-rose-500/10",
    emerald: "text-emerald-600 dark:text-emerald-400 bg-emerald-500/10",
    violet: "text-violet-600 dark:text-violet-400 bg-violet-500/10",
    blue: "text-blue-600 dark:text-cyan-300 bg-blue-500/10",
    amber: "text-amber-600 dark:text-amber-300 bg-amber-500/10",
  }[tone];
  return (
    <div className="rounded-2xl border border-[var(--gc-border)] bg-white/20 p-3 dark:bg-white/5">
      <div className="flex items-center gap-2">
        <span className={`grid h-7 w-7 place-items-center rounded-lg ${color}`}>
          <Icon className="h-3.5 w-3.5" />
        </span>
        <span className="text-xs font-bold text-[var(--gc-text-muted)]">{label}</span>
      </div>
      <div className="mt-2 text-base font-black tabular-nums text-[var(--gc-text)]">{money(value)} ₫</div>
    </div>
  );
}

function OpeningBalanceModal({
  debt,
  onClose,
  onSaved,
}: {
  debt: DebtSummary;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const existingAmount = Math.abs(debt.openingBalance);
  const [balanceType, setBalanceType] = useState<"receivable" | "advance">(
    debt.openingBalance < 0 ? "advance" : "receivable",
  );
  const [amount, setAmount] = useState(existingAmount ? String(Math.round(existingAmount)) : "");
  const [asOfDate, setAsOfDate] = useState(
    debt.openingDate ?? `${new Date().getFullYear()}-01-01`,
  );
  const [note, setNote] = useState(debt.openingNote ?? "");
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  const save = async () => {
    const parsedAmount = Number(amount.replace(/[^\d]/g, ""));
    if (!Number.isFinite(parsedAmount) || parsedAmount < 0) {
      setError("Số dư đầu kỳ không hợp lệ.");
      return;
    }
    if (!asOfDate) {
      setError("Vui lòng chọn ngày bắt đầu tính công nợ.");
      return;
    }
    if (asOfDate > today()) {
      setError("Ngày đầu kỳ không được lớn hơn ngày hiện tại.");
      return;
    }

    setSaving(true);
    setError("");
    try {
      await api.put(`/api/debts/${debt.customer.id}/opening-balance`, {
        amount: balanceType === "advance" ? -parsedAmount : parsedAmount,
        asOfDate,
        note: note.trim(),
      });
      notify.success(`Đã cập nhật số dư đầu kỳ của ${debt.customer.name}.`, "Công nợ");
      onSaved();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Không cập nhật được nợ đầu kỳ.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      solid
      title="Thiết lập số dư đầu kỳ"
      onClose={() => !saving && onClose()}
      footer={
        <>
          <GlassButton variant="ghost" disabled={saving} onClick={onClose}>
            Hủy
          </GlassButton>
          <GlassButton disabled={saving} onClick={() => void save()}>
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Landmark className="h-4 w-4" />}
            Lưu số dư đầu kỳ
          </GlassButton>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-2xl border border-violet-500/20 bg-violet-500/8 p-3.5">
          <div className="font-black text-[var(--gc-text)]">{debt.customer.name}</div>
          <p className="mt-1 text-xs font-semibold leading-relaxed text-[var(--gc-text-muted)]">
            Các phiếu và khoản thu trước ngày đầu kỳ sẽ không cộng lại vào dư nợ, tránh trùng số liệu cũ.
          </p>
        </div>

        <Field label="Loại số dư">
          <Select
            value={balanceType}
            onChange={(event) => setBalanceType(event.target.value as "receivable" | "advance")}
            className="w-full"
          >
            <option value="receivable">Khách hàng còn nợ</option>
            <option value="advance">Khách hàng trả trước</option>
          </Select>
        </Field>

        <Field label="Số tiền đầu kỳ">
          <div className="relative">
            <Input
              autoFocus
              inputMode="numeric"
              value={amount ? money(Number(amount.replace(/[^\d]/g, ""))) : ""}
              onChange={(event) => {
                setAmount(event.target.value.replace(/[^\d]/g, ""));
                if (error) setError("");
              }}
              className="pr-9 font-black tabular-nums"
              placeholder="0"
            />
            <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-sm font-bold text-[var(--gc-text-muted)]">
              ₫
            </span>
          </div>
        </Field>

        <Field label="Tính từ ngày *">
          <DatePicker
            value={asOfDate}
            onChange={(value) => {
              setAsOfDate(value);
              if (error) setError("");
            }}
            ariaLabel="Chọn ngày bắt đầu số dư công nợ"
          />
        </Field>

        <Field label="Ghi chú">
          <Input
            value={note}
            maxLength={1000}
            onChange={(event) => setNote(event.target.value)}
            placeholder="Ví dụ: Số dư chuyển từ sổ năm trước"
          />
        </Field>

        {error && <p className="text-sm font-semibold text-rose-500">{error}</p>}
      </div>
    </Modal>
  );
}

function CollectDebtModal({
  debt,
  onClose,
  onSaved,
}: {
  debt: DebtSummary;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const [amount, setAmount] = useState(debt.balance > 0 ? String(Math.round(debt.balance)) : "");
  const [paymentDate, setPaymentDate] = useState(today);
  const [note, setNote] = useState("");
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  const save = async () => {
    const parsedAmount = Number(amount.replace(/[^\d]/g, ""));
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError("Vui lòng nhập số tiền thu lớn hơn 0.");
      return;
    }
    if (!paymentDate) {
      setError("Vui lòng chọn ngày thu tiền.");
      return;
    }

    setSaving(true);
    setError("");
    try {
      await api.post(`/api/debts/${debt.customer.id}/payments`, {
        amount: parsedAmount,
        date: paymentDate,
        note: note.trim(),
      });
      notify.success(`Đã ghi nhận thu ${money(parsedAmount)} ₫ từ ${debt.customer.name}.`, "Công nợ");
      onSaved();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Không ghi nhận được khoản thu.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      solid
      title="Ghi nhận thu công nợ"
      onClose={() => !saving && onClose()}
      footer={
        <>
          <GlassButton variant="ghost" disabled={saving} onClick={onClose}>
            Hủy
          </GlassButton>
          <GlassButton disabled={saving} onClick={() => void save()}>
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <HandCoins className="h-4 w-4" />}
            Ghi nhận khoản thu
          </GlassButton>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-2xl border border-[var(--gc-border)] bg-white/25 p-3.5 dark:bg-white/5">
          <div className="text-sm font-black text-[var(--gc-text)]">{debt.customer.name}</div>
          <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-xs font-semibold text-[var(--gc-text-muted)]">
            <span>Dư nợ hiện tại: {money(Math.max(debt.balance, 0))} ₫</span>
            <span>Đã thu: {money(debt.collectedTotal)} ₫</span>
            {debt.returnsTotal > 0 && <span>Hàng trả về: {money(debt.returnsTotal)} ₫</span>}
          </div>
        </div>

        <Field label="Số tiền thu *">
          <div className="relative">
            <Input
              autoFocus
              inputMode="numeric"
              value={amount ? money(Number(amount.replace(/[^\d]/g, ""))) : ""}
              onChange={(event) => {
                setAmount(event.target.value.replace(/[^\d]/g, ""));
                if (error) setError("");
              }}
              onKeyDown={(event) => {
                if (event.key === "Enter") void save();
              }}
              className="pr-9 font-black tabular-nums"
              placeholder="0"
            />
            <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-sm font-bold text-[var(--gc-text-muted)]">
              ₫
            </span>
          </div>
        </Field>

        <Field label="Ngày thu *">
          <DatePicker
            value={paymentDate}
            onChange={(value) => {
              setPaymentDate(value);
              if (error) setError("");
            }}
            ariaLabel="Chọn ngày thu công nợ"
          />
        </Field>

        <Field label="Nội dung">
          <Input
            value={note}
            maxLength={1000}
            onChange={(event) => setNote(event.target.value)}
            placeholder="Ví dụ: Khách chuyển khoản đợt 1"
          />
        </Field>

        {debt.balance > 0 && Number(amount.replace(/[^\d]/g, "")) > debt.balance && (
          <div className="rounded-xl border border-violet-500/25 bg-violet-500/10 px-3 py-2 text-xs font-semibold text-violet-700 dark:text-violet-300">
            Khoản thu lớn hơn dư nợ {money(Number(amount.replace(/[^\d]/g, "")) - debt.balance)} ₫; phần vượt sẽ được ghi nhận là khách trả trước.
          </div>
        )}
        {error && <p className="text-sm font-semibold text-rose-500">{error}</p>}
      </div>
    </Modal>
  );
}
