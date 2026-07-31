import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore, type CSSProperties } from "react";
import {
  ArrowDownLeft,
  ArrowUpRight,
  Ban,
  CheckCircle2,
  CircleDollarSign,
  Download,
  FileText,
  Loader2,
  Pencil,
  Printer,
  Search,
  Sparkles,
  Trash2,
  Wallet,
  X,
} from "lucide-react";
import { GlassCapsule } from "../components/glass/GlassCapsule";
import { GlassPanel } from "../components/glass/GlassPanel";
import { LiquidTabs, type LiquidTab } from "../components/glass/LiquidTabs";
import { Modal } from "../components/Modal";
import { PrintPreviewModal } from "../components/PrintPreviewModal";
import { MonthPicker } from "../components/DateField";
import { useAppNotifications } from "../components/app-notifications-context";
import { Button, Field, Input } from "../components/ui";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { PERM, useAccess } from "../lib/access";
import {
  isKeepCreateVoucherOpenEnabled,
  subscribeKeepCreateVoucherOpenEnabled,
} from "../lib/accountingPreferences";
import { date, money } from "../lib/format";
import { documentTypeText, inferDocumentKind, type DocumentKind } from "../lib/documents";
import type { Customer, DocumentDetail, DocumentListItem } from "../lib/types";
import { CashVoucherEditor } from "./CashVoucherEditor";
import {
  buildCashExcelHtml,
  buildCashPrintHtml,
  type CashPrintableDocument,
} from "../lib/cashVoucherExport";
import "../features/giacong/giacong.css";
import "./thu-chi.css";

const CASH_TABS: LiquidTab[] = [
  { key: "all", label: "Tất cả" },
  { key: "receipt", label: "Phiếu thu" },
  { key: "payment", label: "Phiếu chi" },
];

const currentMonth = () => new Date().toISOString().slice(0, 7);
const monthLabel = (value: string) => {
  if (!value) return "tất cả";
  const [year, month] = value.split("-");
  return `${month}/${year}`;
};

const inMonth = (value: string, month: string) => !month || value.startsWith(month);

export function ThuChi() {
  const { user } = useAuth();
  const access = useAccess();
  const { notify, confirm } = useAppNotifications();
  const { data, loading, error, reload } = useApi<DocumentListItem[]>("/api/cash-vouchers");
  const { data: customers } = useApi<Customer[]>("/api/customers");
  const [tab, setTab] = useState("all");
  const [month, setMonth] = useState(currentMonth);
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<string | null | "new">(null);
  const [initialKind, setInitialKind] = useState<DocumentKind>("receipt");
  const [deleting, setDeleting] = useState<DocumentListItem | null>(null);
  const [deletingBusy, setDeletingBusy] = useState(false);
  const [cancelReason, setCancelReason] = useState("");
  const [printingId, setPrintingId] = useState<string | null>(null);
  const [previewLoadingId, setPreviewLoadingId] = useState<string | null>(null);
  const [printPreview, setPrintPreview] = useState<CashPrintableDocument | null>(null);
  const [exporting, setExporting] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [compact, setCompact] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);
  const pageRef = useRef<HTMLDivElement>(null);

  const keepCreateOpen = useSyncExternalStore(
    useCallback(
      (onChange: () => void) =>
        user ? subscribeKeepCreateVoucherOpenEnabled(user.id, onChange) : () => {},
      [user],
    ),
    () => (user ? isKeepCreateVoucherOpenEnabled(user.id) : false),
  );

  useEffect(() => {
    const page = pageRef.current?.closest<HTMLElement>(".km-page");
    if (!page) return;
    let frame = 0;
    let touchY: number | null = null;
    const updateFromScroll = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        if (page.scrollTop > 18) setCompact(true);
      });
    };
    const onWheel = (event: WheelEvent) => {
      if (event.deltaY > 0.5) setCompact(true);
      else if (event.deltaY < -0.5) {
        const tableScroller = (event.target as Element | null)?.closest<HTMLElement>(".tc-table-scroll");
        if (page.scrollTop <= 2 && (!tableScroller || tableScroller.scrollTop <= 2)) setCompact(false);
      }
    };
    const onTouchStart = (event: TouchEvent) => {
      touchY = event.touches[0]?.clientY ?? null;
    };
    const onTouchMove = (event: TouchEvent) => {
      const nextY = event.touches[0]?.clientY;
      if (touchY === null || nextY === undefined) return;
      const delta = touchY - nextY;
      if (delta > 3) setCompact(true);
      else if (delta < -3) {
        const tableScroller = (event.target as Element | null)?.closest<HTMLElement>(".tc-table-scroll");
        if (page.scrollTop <= 2 && (!tableScroller || tableScroller.scrollTop <= 2)) setCompact(false);
      }
      touchY = nextY;
    };

    updateFromScroll();
    page.addEventListener("scroll", updateFromScroll, { passive: true });
    page.addEventListener("wheel", onWheel, { passive: true });
    page.addEventListener("touchstart", onTouchStart, { passive: true });
    page.addEventListener("touchmove", onTouchMove, { passive: true });
    return () => {
      cancelAnimationFrame(frame);
      page.removeEventListener("scroll", updateFromScroll);
      page.removeEventListener("wheel", onWheel);
      page.removeEventListener("touchstart", onTouchStart);
      page.removeEventListener("touchmove", onTouchMove);
    };
  }, []);

  const monthRows = useMemo(
    () => (data ?? []).filter((row) => inMonth(row.date, month)),
    [data, month],
  );

  const stats = useMemo(() => {
    const activeRows = monthRows.filter((row) => !row.cancelledAt);
    const receipts = activeRows.filter((row) => inferDocumentKind(row) === "receipt");
    const payments = activeRows.filter((row) => inferDocumentKind(row) === "payment");
    const receiptTotal = receipts.reduce((sum, row) => sum + (row.total || 0), 0);
    const paymentTotal = payments.reduce((sum, row) => sum + (row.total || 0), 0);
    return {
      activeCount: activeRows.length,
      cancelledCount: monthRows.length - activeRows.length,
      draftCount: activeRows.filter((row) => !row.issuedAt).length,
      receiptCount: receipts.length,
      paymentCount: payments.length,
      receiptTotal,
      paymentTotal,
      balance: receiptTotal - paymentTotal,
    };
  }, [monthRows]);

  const rows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase("vi");
    return monthRows.filter((row) => {
      const kind = inferDocumentKind(row);
      if (tab !== "all" && kind !== tab) return false;
      return !query
        || row.voucherNo.toLocaleLowerCase("vi").includes(query)
        || row.customerName.toLocaleLowerCase("vi").includes(query)
        || row.content.toLocaleLowerCase("vi").includes(query)
        || (row.createdBy ?? "").toLocaleLowerCase("vi").includes(query);
    });
  }, [monthRows, search, tab]);

  const startCreate = (kind: DocumentKind) => {
    setInitialKind(kind);
    setEditing("new");
  };

  const loadPrintable = async (row: DocumentListItem): Promise<CashPrintableDocument> => {
    let detail: DocumentDetail | null = null;
    try {
      detail = await api.get<DocumentDetail>(`/api/cash-vouchers/${row.id}`);
    } catch {
      // Bản tổng hợp vẫn đủ để in/xuất khi chi tiết tạm thời không tải được.
    }
    return { row, detail };
  };

  const openPrintPreview = async (row: DocumentListItem) => {
    if (row.cancelledAt) {
      notify.error("Phiếu đã hủy, không thể xem trước để in.");
      return;
    }
    setPreviewLoadingId(row.id);
    try {
      setPrintPreview(await loadPrintable(row));
    } finally {
      setPreviewLoadingId(null);
    }
  };

  const printVoucher = async (frame: HTMLIFrameElement | null) => {
    if (!printPreview) return;
    const row = printPreview.row;
    const printWindow = frame?.contentWindow;
    if (!printWindow) {
      notify.error("Không mở được khung xem trước để in.");
      return;
    }
    setPrintingId(row.id);
    try {
      printWindow.focus();
      printWindow.print();
      await api.put(`/api/cash-vouchers/${row.id}/issued`);
      setPrintPreview(null);
      reload({ silent: true });
    } catch (printError) {
      notify.error(printError instanceof Error ? printError.message : "Không in được phiếu.");
    } finally {
      setPrintingId(null);
    }
  };

  const exportExcel = async () => {
    if (!monthRows.length) {
      notify.info(`Không có phiếu trong kỳ ${monthLabel(month)} để xuất Excel.`);
      return;
    }
    setExporting(true);
    try {
      const items = await Promise.all(monthRows.map(loadPrintable));
      const blob = new Blob([buildCashExcelHtml(items, month)], {
        type: "application/vnd.ms-excel;charset=utf-8",
      });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `so-thu-chi-${month || "tat-ca"}.xls`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    } finally {
      setExporting(false);
    }
  };

  const confirmCancel = async () => {
    if (!deleting) return;
    setDeletingBusy(true);
    try {
      await api.put(`/api/cash-vouchers/${deleting.id}/cancel`, { reason: cancelReason.trim() });
      setDeleting(null);
      setCancelReason("");
      reload({ silent: true });
      notify.success("Đã chuyển phiếu sang trạng thái hủy; dữ liệu vẫn được lưu trong hệ thống.");
    } catch (cancelError) {
      notify.error(cancelError instanceof Error ? cancelError.message : "Không hủy được phiếu.");
    } finally {
      setDeletingBusy(false);
    }
  };

  const deleteVoucher = async (row: DocumentListItem) => {
    if (deletingId) return;
    if (row.issuedAt && !row.cancelledAt) {
      notify.info("Phiếu đã phát hành cần được hủy trước khi xóa vĩnh viễn.");
      return;
    }
    const ok = await confirm({
      title: "Xóa vĩnh viễn phiếu?",
      description: `Phiếu ${row.voucherNo} sẽ bị xóa khỏi sổ Thu chi và không thể khôi phục.`,
      confirmLabel: "Xóa vĩnh viễn",
      tone: "danger",
    });
    if (!ok) return;

    setDeletingId(row.id);
    try {
      await api.del(`/api/cash-vouchers/${row.id}/permanent`);
      if (editing === row.id) setEditing(null);
      reload({ silent: true });
      notify.success(`Đã xóa phiếu ${row.voucherNo}.`);
    } catch (cause) {
      notify.error(cause instanceof Error ? cause.message : "Không xóa được phiếu.");
    } finally {
      setDeletingId(null);
    }
  };

  const totalFlow = stats.receiptTotal + stats.paymentTotal;
  const receiptShare = totalFlow > 0 ? (stats.receiptTotal / totalFlow) * 100 : 50;

  return (
    <div ref={pageRef} className="gc-root tc-page space-y-4 pb-6" data-compact={compact ? "true" : "false"}>
      <div className="tc-compact-slot" aria-hidden={!compact}>
        <div className={`tc-compact-summary ${compact ? "is-visible" : ""}`}>
          <div className="tc-compact-title">
            <span>Thu chi</span>
            <small>Kỳ {monthLabel(month)}</small>
          </div>
          <div className={`tc-compact-balance ${stats.balance >= 0 ? "is-positive" : "is-negative"}`}>
            <small>Thu − chi</small>
            <strong>{stats.balance >= 0 ? "+" : "−"}{money(Math.abs(stats.balance))} ₫</strong>
          </div>
          <div className="tc-compact-flow is-receipt">
            <ArrowDownLeft className="h-4 w-4" />
            <span><small>Thu</small><strong>{money(stats.receiptTotal)} ₫</strong></span>
          </div>
          <div className="tc-compact-flow is-payment">
            <ArrowUpRight className="h-4 w-4" />
            <span><small>Chi</small><strong>{money(stats.paymentTotal)} ₫</strong></span>
          </div>
          <div className="tc-compact-actions">
            <button type="button" className="is-receipt" onClick={() => startCreate("receipt")}>
              <ArrowDownLeft className="h-4 w-4" />
              Thu
            </button>
            <button type="button" className="is-payment" onClick={() => startCreate("payment")}>
              <ArrowUpRight className="h-4 w-4" />
              Chi
            </button>
          </div>
        </div>
      </div>

      <div className="tc-overview-stage">
      <div className="tc-overview-stage-inner space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <div className="mb-1.5 inline-flex items-center gap-1.5 rounded-full border border-[var(--gc-border)] bg-white/20 px-2.5 py-1 text-[0.68rem] font-black uppercase tracking-[0.12em] text-[var(--gc-text-muted)] dark:bg-white/5">
            <Sparkles className="h-3 w-3" />
            Sổ quỹ tiền mặt
          </div>
          <h1 className="text-[1.75rem] font-black leading-tight text-[var(--gc-text)]">Thu chi</h1>
          <p className="mt-1 text-sm font-semibold text-[var(--gc-text-soft)]">
            Theo dõi dòng tiền và quản lý phiếu thu, phiếu chi tập trung.
          </p>
        </div>
        <Button variant="ghost" onClick={exportExcel} disabled={exporting}>
          {exporting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
          Xuất sổ Excel
        </Button>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.4fr)_minmax(320px,0.6fr)]">
        <GlassPanel strong className="tc-flow-card overflow-hidden rounded-[24px] p-5 sm:p-6">
          <div className="relative z-[1] flex h-full flex-col justify-between gap-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <p className="text-xs font-black uppercase tracking-[0.1em] text-[var(--gc-text-muted)]">
                  Chênh lệch thu − chi
                </p>
                <div className={`mt-2 text-[clamp(2rem,4vw,3.25rem)] font-black leading-none tracking-[-0.04em] tabular-nums ${
                  stats.balance >= 0 ? "text-emerald-600 dark:text-emerald-300" : "text-rose-600 dark:text-rose-300"
                }`}>
                  {stats.balance >= 0 ? "+" : "−"}{money(Math.abs(stats.balance))} ₫
                </div>
                <p className="mt-2 text-xs font-bold text-[var(--gc-text-muted)]">
                  Kỳ {monthLabel(month)} · {stats.activeCount} chứng từ hiệu lực
                </p>
              </div>
              <span className={`tc-flow-state ${stats.balance >= 0 ? "is-positive" : "is-negative"}`}>
                {stats.balance >= 0 ? <ArrowUpRight className="h-4 w-4" /> : <ArrowDownLeft className="h-4 w-4" />}
                {stats.balance >= 0 ? "Thu lớn hơn chi" : "Chi lớn hơn thu"}
              </span>
            </div>

            <div>
              <div className="tc-flow-track" aria-label={`Tỷ trọng thu ${Math.round(receiptShare)} phần trăm`}>
                <span className="tc-flow-track-receipt" style={{ width: `${receiptShare}%` }} />
              </div>
              <div className="mt-3 grid grid-cols-2 gap-3">
                <div className="tc-flow-metric">
                  <span className="tc-flow-icon is-receipt"><ArrowDownLeft className="h-4 w-4" /></span>
                  <div>
                    <p>Tổng thu · {stats.receiptCount} phiếu</p>
                    <strong>{money(stats.receiptTotal)} ₫</strong>
                  </div>
                </div>
                <div className="tc-flow-metric">
                  <span className="tc-flow-icon is-payment"><ArrowUpRight className="h-4 w-4" /></span>
                  <div>
                    <p>Tổng chi · {stats.paymentCount} phiếu</p>
                    <strong>{money(stats.paymentTotal)} ₫</strong>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </GlassPanel>

        <GlassPanel strong className="tc-quick-panel rounded-[24px] p-4">
          <div className="mb-3 flex items-center justify-between">
            <div>
              <p className="text-sm font-black text-[var(--gc-text)]">Tạo chứng từ</p>
              <p className="mt-0.5 text-xs font-semibold text-[var(--gc-text-muted)]">Chọn đúng nghiệp vụ cần lập</p>
            </div>
            <span className="rounded-lg bg-blue-500/10 px-2 py-1 text-[0.68rem] font-black text-blue-600 dark:text-cyan-300">
              {stats.draftCount} nháp
            </span>
          </div>
          <div className="grid gap-2.5">
            <button type="button" className="tc-quick-action is-receipt" onClick={() => startCreate("receipt")}>
              <span className="tc-quick-action-icon"><CircleDollarSign className="h-5 w-5" /></span>
              <span>
                <strong>Tạo phiếu thu</strong>
                <small>Ghi nhận tiền vào quỹ</small>
              </span>
              <ArrowDownLeft className="ml-auto h-5 w-5" />
            </button>
            <button type="button" className="tc-quick-action is-payment" onClick={() => startCreate("payment")}>
              <span className="tc-quick-action-icon"><Wallet className="h-5 w-5" /></span>
              <span>
                <strong>Tạo phiếu chi</strong>
                <small>Ghi nhận tiền ra khỏi quỹ</small>
              </span>
              <ArrowUpRight className="ml-auto h-5 w-5" />
            </button>
          </div>
          <div className="mt-3 flex items-center justify-between rounded-xl border border-[var(--gc-border)] bg-white/18 px-3 py-2.5 text-xs font-bold text-[var(--gc-text-muted)] dark:bg-white/5">
            <span className="inline-flex items-center gap-1.5"><CheckCircle2 className="h-3.5 w-3.5 text-emerald-500" /> {stats.activeCount} hiệu lực</span>
            <span>{stats.cancelledCount} đã hủy</span>
          </div>
        </GlassPanel>
      </div>
      </div>
      </div>

      <GlassPanel strong className="tc-ledger overflow-hidden rounded-[22px]">
        <div className="tc-ledger-head border-b border-[var(--gc-border)] p-4">
          <div className="mb-3 flex flex-wrap items-end justify-between gap-2">
            <div>
              <h2 className="text-base font-black text-[var(--gc-text)]">Sổ giao dịch</h2>
              <p className="mt-0.5 text-xs font-semibold text-[var(--gc-text-muted)]">
                {rows.length} kết quả trong {monthRows.length} phiếu của kỳ {monthLabel(month)}
              </p>
            </div>
            <LiquidTabs tabs={CASH_TABS} value={tab} onChange={setTab} />
          </div>
          <div className="grid grid-cols-1 gap-2.5 md:grid-cols-[minmax(260px,1fr)_190px]">
            <GlassCapsule className="gc-search px-4">
              <Search className="mr-2.5 h-[18px] w-[18px] text-[var(--gc-text-muted)]" />
              <input
                ref={searchRef}
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Tìm số phiếu, đối tượng, nội dung, người lập..."
                aria-label="Tìm phiếu thu chi"
              />
              {search && (
                <button type="button" onClick={() => setSearch("")} className="ml-1.5 grid h-6 w-6 place-items-center rounded-full text-[var(--gc-text-muted)] hover:bg-black/5 dark:hover:bg-white/10" aria-label="Xóa tìm kiếm">
                  <X className="h-4 w-4" />
                </button>
              )}
            </GlassCapsule>
            <MonthPicker value={month} onChange={setMonth} ariaLabel="Lọc theo tháng" placeholder="Tất cả các tháng" />
          </div>
        </div>

        <div className="gc-scroll tc-table-scroll min-h-[320px] overflow-auto">
          <table className="gc-table tc-table">
            <thead>
              <tr>
                <th>Chứng từ</th>
                <th>Loại</th>
                <th>Đối tượng &amp; diễn giải</th>
                <th>Người lập</th>
                <th className="text-right">Số tiền</th>
                <th>Trạng thái</th>
                <th className="tc-actions-col text-right">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                Array.from({ length: 7 }).map((_, row) => (
                  <tr key={row}>
                    {Array.from({ length: 7 }).map((__, col) => (
                      <td key={col}><div className="gc-skeleton h-4" style={{ width: col === 4 ? "55%" : "82%" }} /></td>
                    ))}
                  </tr>
                ))
              ) : error ? (
                <tr><td colSpan={7} className="py-16 text-center text-sm font-semibold text-rose-500">{error}</td></tr>
              ) : rows.length === 0 ? (
                <tr>
                  <td colSpan={7} className="py-16 text-center">
                    <div className="flex flex-col items-center gap-2 text-[var(--gc-text-soft)]">
                      <span className="grid h-12 w-12 place-items-center rounded-2xl bg-blue-500/10 text-blue-600 dark:text-cyan-300">
                        <FileText className="h-6 w-6" />
                      </span>
                      <p className="text-sm font-black">Chưa có phiếu thu chi phù hợp</p>
                      <p className="text-xs font-semibold text-[var(--gc-text-muted)]">Thử đổi bộ lọc hoặc tạo chứng từ mới.</p>
                    </div>
                  </td>
                </tr>
              ) : rows.map((row) => {
                const kind = inferDocumentKind(row);
                const isReceipt = kind === "receipt";
                return (
                  <tr key={row.id} onClick={() => setEditing(row.id)} className={row.cancelledAt ? "is-cancelled" : ""}>
                    <td>
                      <div className="font-black text-[var(--gc-text)]">{row.voucherNo}</div>
                      <div className="mt-1 text-[0.7rem] font-bold text-[var(--gc-text-muted)]">{date(row.date)}</div>
                    </td>
                    <td>
                      <span className="gc-badge" style={{ "--gc-badge": isReceipt ? "0, 184, 148" : "217, 119, 6" } as CSSProperties}>
                        <span className="gc-dot" /> {documentTypeText(row)}
                      </span>
                    </td>
                    <td className="min-w-[260px]">
                      <div className="font-bold text-[var(--gc-text)]">{row.customerName || "Khách lẻ"}</div>
                      <div className="mt-1 line-clamp-2 text-xs font-semibold text-[var(--gc-text-soft)]">{row.content || "Không có diễn giải"}</div>
                    </td>
                    <td className="whitespace-nowrap text-[var(--gc-text-soft)]">{row.createdBy || "Chưa rõ"}</td>
                    <td className={`whitespace-nowrap text-right text-[0.95rem] font-black tabular-nums ${
                      isReceipt ? "text-emerald-600 dark:text-emerald-400" : "text-amber-700 dark:text-amber-300"
                    }`}>
                      {isReceipt ? "+" : "−"}{money(row.total)} ₫
                    </td>
                    <td>
                      <span
                        className="gc-badge"
                        style={{ "--gc-badge": row.cancelledAt ? "225, 29, 72" : row.issuedAt ? "0, 150, 110" : "217, 119, 6" } as CSSProperties}
                        title={row.cancelledAt ? `Hủy bởi ${row.cancelledBy || "không rõ"}${row.cancelReason ? `: ${row.cancelReason}` : ""}` : undefined}
                      >
                        <span className="gc-dot" /> {row.cancelledAt ? "Đã hủy" : row.issuedAt ? "Đã phát hành" : "Phiếu nháp"}
                      </span>
                    </td>
                    <td className="tc-actions-col text-right" onClick={(event) => event.stopPropagation()}>
                      <div className="flex justify-end gap-1.5">
                        <button type="button" className="gc-icon-btn h-8 w-8" onClick={() => void openPrintPreview(row)} disabled={!!row.cancelledAt || previewLoadingId === row.id || printingId === row.id} title={row.cancelledAt ? "Phiếu đã hủy, không thể in" : "Xem trước và in"}>
                          {previewLoadingId === row.id || printingId === row.id ? <Loader2 className="h-4 w-4 animate-spin" /> : <Printer className="h-4 w-4" />}
                        </button>
                        <button type="button" className="gc-icon-btn h-8 w-8" onClick={() => setEditing(row.id)} title={row.cancelledAt ? "Xem phiếu đã hủy" : "Sửa phiếu"}><Pencil className="h-4 w-4" /></button>
                        {!row.cancelledAt && access.can(PERM.vouchersCancel) && (
                          <button
                            type="button"
                            className="gc-icon-btn h-8 w-8 text-rose-500"
                            onClick={() => {
                              setCancelReason("");
                              setDeleting(row);
                            }}
                            title="Hủy phiếu"
                          >
                            <Ban className="h-4 w-4" />
                          </button>
                        )}
                        {access.can(PERM.vouchersCancel) && (!row.issuedAt || !!row.cancelledAt) && (
                          <button
                            type="button"
                            className="gc-icon-btn h-8 w-8 text-rose-500 hover:text-rose-600"
                            onClick={() => void deleteVoucher(row)}
                            disabled={deletingId === row.id}
                            title="Xóa vĩnh viễn"
                          >
                            {deletingId === row.id
                              ? <Loader2 className="h-4 w-4 animate-spin" />
                              : <Trash2 className="h-4 w-4" />}
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
            {!loading && !error && rows.length > 0 && (
              <tfoot>
                <tr>
                  <td colSpan={4} className="text-right font-black text-[var(--gc-text-soft)]">Tổng theo bộ lọc</td>
                  <td className="whitespace-nowrap text-right font-black tabular-nums text-[var(--gc-text)]">
                    {money(rows.filter((row) => !row.cancelledAt).reduce((sum, row) =>
                      sum + (inferDocumentKind(row) === "receipt" ? row.total : -row.total), 0))} ₫
                  </td>
                  <td colSpan={2} />
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      </GlassPanel>

      {editing !== null && (
        <CashVoucherEditor
          key={`${editing}:${initialKind}`}
          id={editing}
          initialKind={initialKind}
          customers={customers ?? []}
          onPrint={editing !== "new" ? () => {
            const row = data?.find((item) => item.id === editing);
            if (row) void openPrintPreview(row);
          } : undefined}
          printLoading={editing !== "new" && (printingId === editing || previewLoadingId === editing)}
          keepOpenAfterSave={editing === "new" && keepCreateOpen}
          readOnly={editing !== "new" && !!data?.find((item) => item.id === editing)?.cancelledAt}
          onClose={() => setEditing(null)}
          onSaved={() => {
            if (!(editing === "new" && keepCreateOpen)) setEditing(null);
            reload();
          }}
        />
      )}

      {printPreview && (
        <PrintPreviewModal
          title={`Xem trước ${documentTypeText(printPreview.row).toLowerCase()} ${printPreview.row.voucherNo}`}
          html={buildCashPrintHtml(printPreview)}
          printing={printingId === printPreview.row.id}
          onClose={() => {
            if (printingId !== printPreview.row.id) setPrintPreview(null);
          }}
          onPrint={(frame) => void printVoucher(frame)}
        />
      )}

      {deleting && (
        <Modal
          open
          solid
          title="Hủy phiếu thu chi"
          onClose={() => !deletingBusy && setDeleting(null)}
          footer={
            <>
              <Button variant="ghost" onClick={() => setDeleting(null)} disabled={deletingBusy}>Đóng</Button>
              <Button variant="danger" onClick={confirmCancel} disabled={deletingBusy}>
                {deletingBusy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Ban className="h-4 w-4" />}
                Hủy phiếu
              </Button>
            </>
          }
        >
          <div className="space-y-3 text-sm text-[var(--gc-text-soft)]">
            <p>
              Phiếu <span className="font-bold text-[var(--gc-text)]">{deleting.voucherNo}</span> sẽ chuyển sang trạng thái
              <span className="font-bold text-rose-500"> Đã hủy</span>. Phiếu và toàn bộ nội dung vẫn được lưu để tra cứu.
            </p>
            <Field label="Lý do hủy (không bắt buộc)">
              <Input value={cancelReason} maxLength={500} onChange={(event) => setCancelReason(event.target.value)} placeholder="Nhập lý do hủy phiếu" />
            </Field>
          </div>
        </Modal>
      )}
    </div>
  );
}
