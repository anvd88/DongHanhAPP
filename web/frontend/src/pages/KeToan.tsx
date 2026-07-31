import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore, type CSSProperties } from "react";
import { MotionConfig, motion } from "motion/react";
import { ArrowDown, ArrowUp, ArrowUpDown, Ban, CalendarDays, Download, FileText, FilterX, Loader2, Pencil, Printer, Search, Server, TriangleAlert, X } from "lucide-react";
import { GlassCapsule } from "../components/glass/GlassCapsule";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Button as GlassButton } from "../shadcn/button";
import { Modal } from "../components/Modal";
import { PrintPreviewModal } from "../components/PrintPreviewModal";
import { useAppNotifications } from "../components/app-notifications-context";
import { DateRangePicker } from "../components/DateField";
import { Field, Input } from "../components/ui";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { APP_BRAND_NAME } from "../lib/branding";
import { isKeepCreateVoucherOpenEnabled, subscribeKeepCreateVoucherOpenEnabled } from "../lib/accountingPreferences";
import { money, date } from "../lib/format";
import { documentTypeText, inferDocumentKind } from "../lib/documents";
import type { AccountingSystemStatus, Customer, DocumentDetail, DocumentListItem } from "../lib/types";
import { StatCard } from "../features/giacong/StatCard";
import { DocumentEditor } from "./DocumentEditor";
import "../features/giacong/giacong.css";

type PrintableDocument = { row: DocumentListItem; detail: DocumentDetail | null };

const EASE_IOS = [0.22, 1, 0.36, 1] as const;
const FORCE_FULL_MOTION =
  import.meta.env.DEV &&
  typeof localStorage !== "undefined" &&
  localStorage.getItem("force-full-motion") === "true";

const localIsoDate = (value: Date) => {
  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, "0");
  const day = String(value.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
};
const currentDateRange = () => {
  const today = new Date();
  return {
    from: localIsoDate(new Date(today.getFullYear(), today.getMonth(), 1)),
    to: localIsoDate(today),
  };
};
const isInDateRange = (value: string, from: string, to: string) =>
  (!from || value >= from) && (!to || value <= to);
const displayIsoDate = (value: string) => {
  const [year, month, day] = value.split("-");
  return year && month && day ? `${day}/${month}/${year}` : value;
};
const dateRangeLabel = (from: string, to: string) =>
  `Từ ngày ${displayIsoDate(from)} đến ngày ${displayIsoDate(to)}`;

const htmlEscape = (value: unknown) =>
  String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");

const badgeTone = (row: DocumentListItem) => {
  const kind = inferDocumentKind(row);
  if (kind === "receipt") return "0, 184, 148";
  if (kind === "payment") return "217, 119, 6";
  return "88, 112, 152";
};

type SortKey = "voucherNo" | "date" | "customerName" | "total";
type SortState = { key: SortKey; dir: "asc" | "desc" };

const compareRows = (a: DocumentListItem, b: DocumentListItem, key: SortKey) => {
  if (key === "total") return (a.total || 0) - (b.total || 0);
  const av = String((key === "customerName" ? a.customerName : a[key]) ?? "");
  const bv = String((key === "customerName" ? b.customerName : b[key]) ?? "");
  return av.localeCompare(bv, "vi", { numeric: true, sensitivity: "base" });
};

function SkeletonRows() {
  return (
    <>
      {Array.from({ length: 6 }).map((_, i) => (
        <tr key={i}>
          {Array.from({ length: 9 }).map((_, j) => (
            <td key={j} className="px-3.5 py-3">
              <div className="gc-skeleton h-4" style={{ width: j === 8 ? "38%" : "86%" }} />
            </td>
          ))}
        </tr>
      ))}
    </>
  );
}

function buildExcelHtml(items: PrintableDocument[], companyName: string, dateFrom: string, dateTo: string) {
  const total = items.reduce((sum, item) => sum + (item.row.cancelledAt ? 0 : item.row.total), 0);
  const blankRows = Array.from({ length: 12 }, () => "<tr>" + "<td></td>".repeat(14) + "</tr>").join("");

  return `\uFEFF<!doctype html>
<html lang="vi" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">
<head>
  <meta charset="utf-8" />
  <!--[if gte mso 9]><xml>
    <x:ExcelWorkbook>
      <x:ExcelWorksheets>
        <x:ExcelWorksheet>
          <x:Name>Chi tiet phieu</x:Name>
          <x:WorksheetOptions><x:DisplayGridlines/></x:WorksheetOptions>
        </x:ExcelWorksheet>
      </x:ExcelWorksheets>
    </x:ExcelWorkbook>
  </xml><![endif]-->
  <style>
    table { border-collapse: collapse; table-layout: fixed; font-family: Arial, sans-serif; font-size: 11pt; }
    col.c-voucher { width: 105px; }
    col.c-date { width: 86px; }
    col.c-type { width: 112px; }
    col.c-customer { width: 180px; }
    col.c-doc-content { width: 220px; }
    col.c-line-no { width: 52px; }
    col.c-line-content { width: 240px; }
    col.c-spec { width: 130px; }
    col.c-qty { width: 76px; }
    col.c-price { width: 104px; }
    col.c-amount { width: 116px; }
    col.c-note { width: 150px; }
    col.c-total { width: 116px; }
    col.c-user { width: 120px; }
    th, td {
      border: .5pt solid #d9d9d9;
      height: 22px;
      padding: 4px 6px;
      vertical-align: middle;
      white-space: normal;
      mso-number-format: "\\@";
    }
    th {
      background: #eaf2f8;
      border-color: #b7c9d6;
      color: #0f172a;
      font-weight: 700;
      text-align: center;
    }
    .title {
      height: 30px;
      background: #dbeafe;
      border: .5pt solid #9eb8d3;
      font-size: 14pt;
      font-weight: 700;
      text-align: center;
    }
    .company {
      height: 24px;
      border: 0;
      background: #ffffff;
      font-size: 11pt;
      font-weight: 500;
      text-align: left;
    }
    .subtitle {
      background: #f8fafc;
      color: #475569;
      font-style: italic;
      text-align: center;
    }
    .right { text-align: right; mso-number-format: "#,##0"; }
    .qty { text-align: right; mso-number-format: "#,##0.##"; }
    .center { text-align: center; }
    .total-row td { background: #f8fafc; font-weight: 700; }
  </style>
</head>
<body>
  <table>
    <colgroup>
      <col class="c-voucher" />
      <col class="c-date" />
      <col class="c-type" />
      <col class="c-customer" />
      <col class="c-doc-content" />
      <col class="c-line-no" />
      <col class="c-line-content" />
      <col class="c-spec" />
      <col class="c-qty" />
      <col class="c-price" />
      <col class="c-amount" />
      <col class="c-note" />
      <col class="c-total" />
      <col class="c-user" />
    </colgroup>
    <thead>
      <tr><td class="company" colspan="14">Tên đơn vị: ${htmlEscape(companyName)}</td></tr>
      <tr><th class="title" colspan="14">BÁO CÁO CHI TIẾT PHIẾU XUẤT HÀNG HÓA</th></tr>
      <tr><td class="subtitle" colspan="14">${htmlEscape(dateRangeLabel(dateFrom, dateTo))}</td></tr>
      <tr>
        <th>Số phiếu</th>
        <th>Ngày</th>
        <th>Loại phiếu</th>
        <th>Khách hàng</th>
        <th>Nội dung phiếu</th>
        <th>STT dòng</th>
        <th>Nội dung dòng hàng</th>
        <th>Quy cách</th>
        <th>Số lượng</th>
        <th>Đơn giá</th>
        <th>Thành tiền</th>
        <th>Ghi chú dòng</th>
        <th>Tổng phiếu</th>
        <th>Người lập</th>
      </tr>
    </thead>
    <tbody>
      ${items
        .flatMap(({ row, detail }) => {
          const lines = detail?.lines.length
            ? detail.lines
            : [{ lineContent: row.content, spec: "", quantity: 1, unitPrice: row.total, note: "" }];

          return lines.map((line, index) => `<tr>
        <td>${htmlEscape(row.voucherNo)}</td>
        <td>${htmlEscape(date(row.date))}</td>
        <td>${htmlEscape(`${documentTypeText(row)}${row.cancelledAt ? " · Đã hủy" : ""}`)}</td>
        <td>${htmlEscape(row.customerName || "Khách lẻ")}</td>
        <td>${htmlEscape(row.content)}</td>
        <td class="center">${index + 1}</td>
        <td>${htmlEscape(line.lineContent || row.content)}</td>
        <td>${htmlEscape(line.spec)}</td>
        <td class="qty">${line.quantity || 0}</td>
        <td class="right">${Math.round(line.unitPrice || 0)}</td>
        <td class="right">${Math.round((line.quantity || 0) * (line.unitPrice || 0))}</td>
        <td>${htmlEscape(line.note)}</td>
        <td class="right">${Math.round(row.total || 0)}</td>
        <td>${htmlEscape(row.createdBy || "Chưa rõ")}</td>
      </tr>`);
        })
        .join("")}
      ${blankRows}
    </tbody>
    <tfoot>
      <tr class="total-row">
        <td colspan="10">Tổng cộng</td>
        <td class="right">${Math.round(total)}</td>
        <td></td>
        <td class="right">${Math.round(total)}</td>
        <td></td>
      </tr>
    </tfoot>
  </table>
</body>
</html>`;
}

export function KeToan() {
  const { user } = useAuth();
  const { notify } = useAppNotifications();
  const { data, loading, error, reload } = useApi<DocumentListItem[]>("/api/documents");
  const { data: customers } = useApi<Customer[]>("/api/customers");
  const [search, setSearch] = useState("");
  const [dateFrom, setDateFrom] = useState(() => currentDateRange().from);
  const [dateTo, setDateTo] = useState(() => currentDateRange().to);
  const [sort, setSort] = useState<SortState>({ key: "date", dir: "desc" });
  const [editing, setEditing] = useState<string | null | "new">(null);
  const [deleting, setDeleting] = useState<DocumentListItem | null>(null);
  const [deletingBusy, setDeletingBusy] = useState(false);
  const [cancelReason, setCancelReason] = useState("");
  const [numberingForPrint, setNumberingForPrint] = useState<DocumentListItem | null>(null);
  const [printVoucherNo, setPrintVoucherNo] = useState("");
  const [printVoucherNoError, setPrintVoucherNoError] = useState("");
  const [previewLoading, setPreviewLoading] = useState(false);
  const [printPreview, setPrintPreview] = useState<{
    originalRow: DocumentListItem;
    voucherNo: string;
    previewUrl: string;
  } | null>(null);
  const [printingId, setPrintingId] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);
  const [systemStatus, setSystemStatus] = useState<AccountingSystemStatus | null>(null);
  const [systemStatusError, setSystemStatusError] = useState("");
  const [checkingSystemStatus, setCheckingSystemStatus] = useState(true);
  const systemStatusRequest = useRef(0);
  const searchRef = useRef<HTMLInputElement>(null);

  const rangeRows = useMemo(
    () => (data ?? []).filter((document) => isInDateRange(document.date, dateFrom, dateTo)),
    [data, dateFrom, dateTo],
  );

  // Trang Kế toán chỉ còn phiếu xuất kho; phiếu thu/chi đã chuyển sang mô-đun Thu chi.
  const stats = useMemo(() => {
    const activeRows = rangeRows.filter((row) => !row.cancelledAt);
    const monthTotal = activeRows.reduce((sum, row) => sum + (row.total || 0), 0);
    return {
      documentCount: rangeRows.length,
      monthTotal,
      issuedCount: activeRows.filter((row) => !!row.issuedAt).length,
      draftCount: activeRows.filter((row) => !row.issuedAt).length,
    };
  }, [rangeRows]);

  const rows = useMemo(() => {
    const filtered = rangeRows.filter((d) => {
      const q = search.trim().toLowerCase();
      return (
        !q ||
        d.voucherNo.toLowerCase().includes(q) ||
        documentTypeText(d).toLowerCase().includes(q) ||
        d.customerName.toLowerCase().includes(q) ||
        d.content.toLowerCase().includes(q) ||
        (d.createdBy ?? "").toLowerCase().includes(q)
      );
    });
    const dir = sort.dir === "asc" ? 1 : -1;
    return filtered.sort((a, b) => compareRows(a, b, sort.key) * dir);
  }, [rangeRows, search, sort]);

  const visibleTotal = useMemo(
    () => rows.reduce((sum, row) => sum + (row.cancelledAt ? 0 : row.total || 0), 0),
    [rows],
  );
  const hasActiveFilters = search.trim() !== "";
  const hasAnyData = (data?.length ?? 0) > 0;
  const periodLabel = dateRangeLabel(dateFrom, dateTo);

  const clearFilters = () => {
    setSearch("");
  };

  const toggleSort = (key: SortKey) =>
    setSort((current) =>
      current.key === key
        ? { key, dir: current.dir === "asc" ? "desc" : "asc" }
        : { key, dir: key === "date" || key === "total" ? "desc" : "asc" },
    );

  // Tuỳ chọn này lưu ở localStorage — nguồn NGOÀI React, nên đọc bằng useSyncExternalStore (React tự
  // đăng ký/huỷ và đọc lại đúng lúc render) thay vì useState + useEffect(setState). Chưa đăng nhập thì
  // không có khoá nào để đọc ⇒ coi như tắt.
  const keepCreateOpen = useSyncExternalStore(
    useCallback(
      (onChange: () => void) =>
        user ? subscribeKeepCreateVoucherOpenEnabled(user.id, onChange) : () => {},
      [user],
    ),
    () => (user ? isKeepCreateVoucherOpenEnabled(user.id) : false),
  );

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        searchRef.current?.focus();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  useEffect(() => {
    const previewUrl = printPreview?.previewUrl;
    return () => {
      if (previewUrl?.startsWith("blob:")) URL.revokeObjectURL(previewUrl);
    };
  }, [printPreview?.previewUrl]);

  const refreshSystemStatus = useCallback(async () => {
    const requestId = ++systemStatusRequest.current;
    setCheckingSystemStatus(true);
    try {
      const nextStatus = await api.get<AccountingSystemStatus>("/api/accounting/system-status");
      if (requestId !== systemStatusRequest.current) return;
      setSystemStatus(nextStatus);
      setSystemStatusError("");
    } catch (statusError) {
      if (requestId !== systemStatusRequest.current) return;
      setSystemStatusError(
        statusError instanceof SyntaxError
          ? "Máy chủ chưa được cập nhật chức năng trạng thái."
          : statusError instanceof Error
            ? statusError.message
            : "Không kết nối được máy chủ.",
      );
    } finally {
      if (requestId === systemStatusRequest.current) setCheckingSystemStatus(false);
    }
  }, []);

  useEffect(() => {
    void refreshSystemStatus();
    const interval = window.setInterval(() => {
      if (document.visibilityState === "visible") void refreshSystemStatus();
    }, 15_000);
    const onVisibilityChange = () => {
      if (document.visibilityState === "visible") void refreshSystemStatus();
    };
    document.addEventListener("visibilitychange", onVisibilityChange);
    return () => {
      window.clearInterval(interval);
      document.removeEventListener("visibilitychange", onVisibilityChange);
      systemStatusRequest.current += 1;
    };
  }, [refreshSystemStatus]);

  const confirmCancel = async () => {
    if (!deleting) return;
    setDeletingBusy(true);
    try {
      await api.put(`/api/documents/${deleting.id}/cancel`, { reason: cancelReason.trim() });
      setDeleting(null);
      setCancelReason("");
      reload({ silent: true });
      notify.success("Đã chuyển phiếu sang trạng thái hủy; dữ liệu vẫn được lưu trong hệ thống.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không hủy được phiếu.");
    } finally {
      setDeletingBusy(false);
    }
  };

  const loadPrintableDocument = async (row: DocumentListItem): Promise<PrintableDocument> => {
    let detail: DocumentDetail | null = null;
    try {
      detail = await api.get<DocumentDetail>(`/api/documents/${row.id}`);
    } catch {
      /* Vẫn in/xuất được dòng tổng hợp nếu một phiếu không tải được chi tiết. */
    }
    return { row, detail };
  };

  const printVoucher = async (row: DocumentListItem, voucherNoOverride: string) => {
    const voucherNo = voucherNoOverride.trim();
    setPrintingId(row.id);
    try {
      const result = await api.post<{ voucherNo: string; printerName: string }>(
        `/api/documents/${row.id}/warehouse-print`,
        { voucherNo },
      );
      setNumberingForPrint(null);
      setPrintPreview(null);
      setPrintVoucherNoError("");
      reload({ silent: true });
      notify.success(`Đã phát hành ${result.voucherNo} và gửi tới máy in ${result.printerName}.`);
    } catch (e) {
      const message = e instanceof Error ? e.message : "Máy chủ không in được phiếu.";
      setPrintVoucherNoError(message);
      notify.error(message);
    } finally {
      setPrintingId(null);
      void refreshSystemStatus();
    }
  };

  const requestPrint = (row: DocumentListItem) => {
    if (row.cancelledAt) {
      notify.error("Phiếu đã hủy, không thể xem trước hoặc in.");
      return;
    }
    setNumberingForPrint(row);
    setPrintVoucherNo(row.voucherNo);
    setPrintVoucherNoError("");
  };

  const confirmWarehousePreview = async () => {
    if (!numberingForPrint) return;
    const voucherNo = printVoucherNo.trim();
    if (!voucherNo) {
      setPrintVoucherNoError("Vui lòng nhập số phiếu trước khi in.");
      return;
    }

    setPreviewLoading(true);
    try {
      const previewPath =
        `/api/documents/${numberingForPrint.id}/warehouse-preview?voucherNo=${encodeURIComponent(voucherNo)}`;
      const previewPdf = await api.getSameOriginBlob(previewPath);
      if (previewPdf.type && previewPdf.type !== "application/pdf") {
        throw new Error("Máy chủ trả về nội dung xem trước không đúng định dạng PDF.");
      }
      const previewUrl = URL.createObjectURL(
        previewPdf.type === "application/pdf"
          ? previewPdf
          : new Blob([previewPdf], { type: "application/pdf" }),
      );
      setPrintPreview({
        originalRow: numberingForPrint,
        voucherNo,
        previewUrl,
      });
      setNumberingForPrint(null);
    } catch (previewError) {
      setPrintVoucherNoError(
        previewError instanceof TypeError
          ? "Không kết nối được máy chủ để tạo bản xem trước. Hãy kiểm tra lại dịch vụ web."
          : previewError instanceof Error
            ? previewError.message
            : "Không tạo được bản xem trước.",
      );
    } finally {
      setPreviewLoading(false);
    }
  };

  const exportRangeExcel = async () => {
    if (!rangeRows.length) {
      notify.info(`Không có phiếu trong khoảng ${displayIsoDate(dateFrom)} – ${displayIsoDate(dateTo)} để xuất Excel.`);
      return;
    }

    setExporting(true);
    try {
      const items = await Promise.all(
        rangeRows.map((row) => loadPrintableDocument(row)),
      );

      const blob = new Blob([buildExcelHtml(items, APP_BRAND_NAME, dateFrom, dateTo)], {
        type: "application/vnd.ms-excel;charset=utf-8",
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `bao-cao-phieu-xuat-${dateFrom}-den-${dateTo}.xls`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } finally {
      setExporting(false);
    }
  };

  return (
    <MotionConfig reducedMotion={FORCE_FULL_MOTION ? "never" : "user"}>
      <div className="gc-root space-y-4 pb-6">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <motion.h1
              initial={{ opacity: 0, y: 18, scale: 0.985, filter: "blur(10px)" }}
              animate={{ opacity: 1, y: 0, scale: 1, filter: "blur(0px)" }}
              transition={{ duration: 0.48, ease: EASE_IOS }}
              className="text-[1.6rem] font-black leading-tight text-[var(--gc-text)]"
            >
              Kế toán
            </motion.h1>
            <motion.p
              initial={{ opacity: 0, y: 12, filter: "blur(8px)" }}
              animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
              transition={{ duration: 0.44, delay: 0.08, ease: EASE_IOS }}
              className="mt-1 text-sm font-semibold text-[var(--gc-text-soft)]"
            >
              Quản lý phiếu xuất kho bán hàng
            </motion.p>
          </div>
          <motion.div
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.14, ease: EASE_IOS }}
            className="flex flex-wrap items-center gap-2.5"
          >
            <CompactSystemStatus
              status={systemStatus}
              error={systemStatusError}
              loading={checkingSystemStatus}
              onRefresh={() => void refreshSystemStatus()}
            />
            <GlassButton variant="soft" onClick={() => setEditing("new")}>
              <FileText className="h-4 w-4" /> Tạo phiếu xuất kho
            </GlassButton>
            <GlassButton variant="ghost" onClick={exportRangeExcel} disabled={exporting}>
              {exporting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
              Xuất Excel
            </GlassButton>
          </motion.div>
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            index={0}
            icon={FileText}
            label="Phiếu xuất kho"
            value={money(stats.documentCount)}
            sub={`Bán hàng · ${periodLabel}`}
            tone="31, 107, 255"
          />
          <StatCard
            index={1}
            icon={CalendarDays}
            label="Tổng giá trị"
            value={`${money(stats.monthTotal)} ₫`}
            sub={periodLabel}
            tone="124, 70, 255"
          />
          <StatCard
            index={2}
            icon={Printer}
            label="Đã phát hành"
            value={money(stats.issuedCount)}
            sub={`${stats.documentCount ? Math.round((stats.issuedCount / stats.documentCount) * 100) : 0}% số phiếu`}
            tone="0, 150, 110"
          />
          <StatCard
            index={3}
            icon={FileText}
            label="Phiếu nháp"
            value={money(stats.draftCount)}
            sub="Chưa in/phát hành"
            tone="217, 119, 6"
          />
        </div>

        <GlassPanel className="flex flex-wrap items-center gap-3 rounded-[20px] p-3">
          <div className="ml-auto grid flex-1 grid-cols-1 items-center gap-2.5 md:grid-cols-[minmax(220px,1fr)_minmax(280px,330px)_auto]">
            <GlassCapsule className="gc-search min-w-[200px] px-4">
              <Search className="mr-2.5 h-[18px] w-[18px] shrink-0 text-[var(--gc-text-muted)]" aria-hidden="true" />
              <input
                ref={searchRef}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                onKeyDown={(e) => e.key === "Escape" && setSearch("")}
                placeholder="Tìm số phiếu, khách hàng, nội dung..."
                aria-label="Tìm số phiếu, khách hàng, nội dung"
              />
              {search ? (
                <button
                  type="button"
                  onClick={() => {
                    setSearch("");
                    searchRef.current?.focus();
                  }}
                  className="ml-1.5 grid h-5 w-5 shrink-0 place-items-center rounded-full text-[var(--gc-text-muted)] transition-colors hover:bg-black/10 hover:text-[var(--gc-text)] dark:hover:bg-white/10"
                  aria-label="Xóa từ khóa tìm kiếm"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              ) : (
                <kbd className="ml-2 hidden rounded-md border border-[var(--gc-border)] bg-white/30 px-1.5 py-0.5 text-[0.68rem] font-bold text-[var(--gc-text-muted)] sm:block dark:bg-white/5">
                  Ctrl K
                </kbd>
              )}
            </GlassCapsule>

            <DateRangePicker
              from={dateFrom}
              to={dateTo}
              maxDays={60}
              ariaLabel="Lọc phiếu theo khoảng ngày"
              onChange={(nextFrom, nextTo) => {
                setDateFrom(nextFrom);
                setDateTo(nextTo);
              }}
            />

            <div className="flex items-center gap-2">
              <div className="whitespace-nowrap rounded-xl border border-[var(--gc-border)] bg-white/20 px-3 py-2 text-sm font-bold text-[var(--gc-text-soft)] dark:bg-white/5">
                {hasActiveFilters ? (
                  <>
                    {rows.length}<span className="text-[var(--gc-text-muted)]">/{rangeRows.length}</span> phiếu
                  </>
                ) : (
                  <>{rangeRows.length} phiếu · {periodLabel}</>
                )}
              </div>
              {hasActiveFilters && (
                <button
                  type="button"
                  onClick={clearFilters}
                  className="gc-icon-btn inline-flex h-[38px] items-center gap-1.5 whitespace-nowrap px-3 text-sm font-bold text-[var(--gc-text-soft)]"
                  aria-label="Xóa bộ lọc tìm kiếm"
                >
                  <FilterX className="h-4 w-4" /> Xóa lọc
                </button>
              )}
            </div>
          </div>
        </GlassPanel>

        <GlassPanel strong className="overflow-hidden rounded-[20px]">
          <div className="gc-scroll max-h-[calc(100vh-430px)] min-h-[260px] overflow-auto">
            <table className="gc-table">
              <thead>
                <tr>
                  <SortHeader label="Số phiếu" sortKey="voucherNo" sort={sort} onSort={toggleSort} />
                  <SortHeader label="Ngày" sortKey="date" sort={sort} onSort={toggleSort} />
                  <th>Loại phiếu</th>
                  <SortHeader label="Khách hàng" sortKey="customerName" sort={sort} onSort={toggleSort} />
                  <th>Nội dung</th>
                  <SortHeader label="Tổng tiền" sortKey="total" sort={sort} onSort={toggleSort} align="right" />
                  <th>Người lập</th>
                  <th>Trạng thái</th>
                  <th className="w-28 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {loading ? (
                  <SkeletonRows />
                ) : error ? (
                  <tr>
                    <td colSpan={9} className="py-14 text-center text-sm font-semibold text-rose-500">
                      {error}
                    </td>
                  </tr>
                ) : rows.length === 0 ? (
                  <tr>
                    <td colSpan={9}>
                      <div className="flex flex-col items-center justify-center gap-2.5 py-16 text-center">
                        {hasActiveFilters || hasAnyData ? (
                          <>
                            <Search className="h-9 w-9 text-[var(--gc-text-muted)] opacity-70" />
                            <p className="text-sm font-semibold text-[var(--gc-text-soft)]">Không tìm thấy phiếu phù hợp</p>
                            <p className="text-xs text-[var(--gc-text-muted)]">Thử đổi từ khóa hoặc khoảng ngày đang lọc.</p>
                            <button
                              type="button"
                              onClick={() => {
                                clearFilters();
                                const initialRange = currentDateRange();
                                setDateFrom(initialRange.from);
                                setDateTo(initialRange.to);
                              }}
                              className="gc-icon-btn mt-1 inline-flex h-9 items-center gap-1.5 px-3.5 text-sm font-bold text-[var(--gc-text-soft)]"
                            >
                              <FilterX className="h-4 w-4" /> Xóa toàn bộ bộ lọc
                            </button>
                          </>
                        ) : (
                          <>
                            <FileText className="h-9 w-9 text-[var(--gc-text-muted)] opacity-70" />
                            <p className="text-sm font-semibold text-[var(--gc-text-soft)]">Chưa có phiếu xuất kho</p>
                            <p className="text-xs text-[var(--gc-text-muted)]">Bấm “Tạo phiếu xuất kho” để bắt đầu.</p>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ) : (
                  rows.map((row) => (
                    <tr key={row.id} onClick={() => setEditing(row.id)} className={row.cancelledAt ? "opacity-60" : ""}>
                      <td className="whitespace-nowrap font-bold text-[var(--gc-text)]">
                        {row.voucherNo || <span className="font-semibold text-[var(--gc-text-muted)]">Chưa nhập</span>}
                      </td>
                      <td className="whitespace-nowrap text-[var(--gc-text-soft)]">{date(row.date)}</td>
                      <td>
                        <span
                          className="gc-badge"
                          style={{ "--gc-badge": badgeTone(row) } as CSSProperties}
                        >
                          <span className="gc-dot" />
                          {documentTypeText(row)}
                        </span>
                      </td>
                      <td>{row.customerName || "Khách lẻ"}</td>
                      <td className="min-w-[220px] text-[var(--gc-text-soft)]">{row.content}</td>
                      <td className="whitespace-nowrap text-right font-bold tabular-nums">{money(row.total)} ₫</td>
                      <td className="whitespace-nowrap">{row.createdBy || "Chưa rõ"}</td>
                      <td className="whitespace-nowrap">
                        <span
                          className="gc-badge"
                          style={{ "--gc-badge": row.cancelledAt ? "225, 29, 72" : row.issuedAt ? "0, 150, 110" : "217, 119, 6" } as CSSProperties}
                          title={row.cancelledAt
                            ? `Hủy bởi ${row.cancelledBy || "không rõ"}${row.cancelReason ? `: ${row.cancelReason}` : ""}`
                            : row.issuedAt
                              ? `Phát hành lúc ${new Date(row.issuedAt).toLocaleString("vi-VN")}`
                              : "Phiếu chưa được in"}
                        >
                          <span className="gc-dot" />
                          {row.cancelledAt ? "Đã hủy" : row.issuedAt ? "Đã phát hành" : "Phiếu nháp"}
                        </span>
                      </td>
                      <td className="text-right" onClick={(e) => e.stopPropagation()}>
                        <div className="flex justify-end gap-1.5">
                          <button
                            type="button"
                            title={row.cancelledAt ? "Phiếu đã hủy, không thể in" : "Xem trước và in"}
                            aria-label={`Xem trước và in phiếu ${row.voucherNo || row.customerName || row.id}`}
                            disabled={!!row.cancelledAt || printingId === row.id}
                            onClick={() => requestPrint(row)}
                            className="gc-icon-btn h-8 w-8 disabled:pointer-events-none disabled:opacity-50"
                          >
                            {printingId === row.id ? <Loader2 className="h-4 w-4 animate-spin" /> : <Printer className="h-4 w-4" />}
                          </button>
                          <button
                            type="button"
                            title={row.cancelledAt ? "Xem phiếu đã hủy" : "Sửa phiếu"}
                            aria-label={`${row.cancelledAt ? "Xem" : "Sửa"} phiếu ${row.voucherNo || row.customerName || row.id}`}
                            onClick={() => setEditing(row.id)}
                            className="gc-icon-btn h-8 w-8"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                          {!row.cancelledAt && (
                            <button
                              type="button"
                              title="Hủy phiếu"
                              aria-label={`Hủy phiếu ${row.voucherNo || row.customerName || row.id}`}
                              onClick={() => {
                                setCancelReason("");
                                setDeleting(row);
                              }}
                              className="gc-icon-btn h-8 w-8 text-rose-500 hover:text-rose-600"
                            >
                              <Ban className="h-4 w-4" />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
              {!loading && !error && rows.length > 0 && (
                <tfoot>
                  <tr className="gc-foot">
                    <td colSpan={5} className="text-right font-bold text-[var(--gc-text-soft)]">
                      Tổng cộng · {rows.length} phiếu
                    </td>
                    <td className="whitespace-nowrap text-right font-black tabular-nums text-[var(--gc-text)]">
                      {money(visibleTotal)} ₫
                    </td>
                    <td colSpan={3} />
                  </tr>
                </tfoot>
              )}
            </table>
          </div>
        </GlassPanel>

        {editing !== null && (
          <DocumentEditor
            // Mỗi chứng từ (và mỗi loại chứng từ mới) là một form riêng: đổi key ⇒ React dựng lại
            // component với giá trị khởi tạo đúng, thay cho việc dọn/nạp lại từng ô bằng useEffect.
            key={`${editing}:document`}
            id={editing}
            initialKind="document"
            allowedKinds={["document"]}
            customers={customers ?? []}
            onPrint={
              editing !== "new"
                ? () => {
                    const row = data?.find((item) => item.id === editing);
                    if (row) requestPrint(row);
                  }
                : undefined
            }
            printLoading={editing !== "new" && printingId === editing}
            keepOpenAfterSave={editing === "new" && keepCreateOpen}
            readOnly={editing !== "new" && !!data?.find((item) => item.id === editing)?.cancelledAt}
            onClose={() => setEditing(null)}
            onSaved={() => {
              if (!(editing === "new" && keepCreateOpen)) setEditing(null);
              reload();
            }}
          />
        )}

        {numberingForPrint && (
          <Modal
            open
            solid
            title={numberingForPrint.issuedAt ? "Xem lại phiếu đã phát hành" : "Nhập số phiếu trước khi xem"}
            onClose={() => {
              if (!previewLoading) setNumberingForPrint(null);
            }}
            footer={
              <>
                <GlassButton
                  variant="ghost"
                  onClick={() => setNumberingForPrint(null)}
                  disabled={previewLoading}
                >
                  Hủy
                </GlassButton>
                <GlassButton onClick={() => void confirmWarehousePreview()} disabled={previewLoading}>
                  {previewLoading ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    <FileText className="h-4 w-4" />
                  )}
                  Xem trước
                </GlassButton>
              </>
            }
          >
            <div className="space-y-3">
              <p className="text-sm text-[var(--text-secondary)]">
                {numberingForPrint.issuedAt
                  ? "Số phiếu đã được khóa theo lần phát hành đầu tiên. Bạn có thể xem trước hoặc in lại bằng đúng số này."
                  : "Kiểm tra số phiếu và nội dung bản xem trước trước khi gửi lệnh tới máy in mặc định của máy chủ."}
              </p>
              <Field label={numberingForPrint.issuedAt ? "Số phiếu đã phát hành" : "Số phiếu *"}>
                <Input
                  autoFocus
                  maxLength={64}
                  value={printVoucherNo}
                  readOnly={!!numberingForPrint.issuedAt}
                  onChange={(e) => {
                    if (numberingForPrint.issuedAt) return;
                    setPrintVoucherNo(e.target.value);
                    if (printVoucherNoError) setPrintVoucherNoError("");
                  }}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") void confirmWarehousePreview();
                  }}
                  placeholder="Nhập số phiếu"
                />
              </Field>
              {printVoucherNoError && (
                <p className="text-sm font-semibold text-rose-500">{printVoucherNoError}</p>
              )}
            </div>
          </Modal>
        )}

        {printPreview && (
          <PrintPreviewModal
            title={`Xem trước phiếu xuất kho ${printPreview.voucherNo}`}
            src={printPreview.previewUrl}
            printing={printingId === printPreview.originalRow.id}
            printLabel="In tại máy chủ"
            onClose={() => {
              if (printingId !== printPreview.originalRow.id) setPrintPreview(null);
            }}
            onPrint={() => void printVoucher(printPreview.originalRow, printPreview.voucherNo)}
          />
        )}

        {deleting && (
          <Modal
            open
            solid
            title="Hủy phiếu xuất kho"
            onClose={() => {
              if (!deletingBusy) setDeleting(null);
            }}
            footer={
              <>
                <GlassButton variant="ghost" onClick={() => setDeleting(null)} disabled={deletingBusy}>
                  Đóng
                </GlassButton>
                <GlassButton variant="danger" onClick={confirmCancel} disabled={deletingBusy}>
                  {deletingBusy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Ban className="h-4 w-4" />}
                  Hủy phiếu
                </GlassButton>
              </>
            }
          >
            <div className="flex gap-3.5">
              <div className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-red-500/12 text-rose-500">
                <TriangleAlert className="h-6 w-6" />
              </div>
              <div className="space-y-2 text-sm">
                <p className="text-[var(--text)]">
                  Phiếu{" "}
                  <span className="font-bold">{documentTypeText(deleting).toLowerCase()}</span>{" "}
                  <span className="font-bold">{deleting.voucherNo || "chưa có số"}</span>{" "}
                  sẽ chuyển sang trạng thái <span className="font-bold text-rose-500">Đã hủy</span>.
                </p>
                <div className="rounded-xl border border-[var(--glass-border)] bg-white/30 px-3 py-2.5 text-[var(--text-secondary)] dark:bg-white/5">
                  <div>Khách hàng: <span className="font-semibold text-[var(--text)]">{deleting.customerName || "Khách lẻ"}</span></div>
                  <div>Ngày: <span className="font-semibold text-[var(--text)]">{date(deleting.date)}</span></div>
                  <div>Tổng tiền: <span className="font-semibold text-[var(--text)]">{money(deleting.total)} ₫</span></div>
                </div>
                <Field label="Lý do hủy (không bắt buộc)">
                  <Input
                    value={cancelReason}
                    maxLength={500}
                    onChange={(event) => setCancelReason(event.target.value)}
                    placeholder="Nhập lý do hủy phiếu"
                  />
                </Field>
                <p className="text-xs font-semibold text-[var(--text-secondary)]">
                  Phiếu và toàn bộ nội dung vẫn được lưu trong hệ thống để tra cứu.
                </p>
              </div>
            </div>
          </Modal>
        )}
      </div>
    </MotionConfig>
  );
}

function CompactSystemStatus({
  status,
  error,
  loading,
  onRefresh,
}: {
  status: AccountingSystemStatus | null;
  error: string;
  loading: boolean;
  onRefresh: () => void;
}) {
  const serverReady = !!status?.server.printServiceReady && !error;
  const printerReady = !!status?.printer.ready && !error;
  const printerProblem = !!status && !status.printer.ready && !error;
  const healthy = serverReady && printerReady;
  const stateLabel = loading && !status
    ? "Đang kiểm tra"
    : error
      ? "Mất kết nối"
      : printerProblem
        ? "Máy in lỗi"
      : healthy
        ? "Sẵn sàng"
        : "Cần kiểm tra";
  const title = error
    ? `${error}\nBấm để kiểm tra lại.`
    : [
        `Hệ thống in: ${stateLabel}`,
        status?.server.message ?? "Đang kiểm tra máy chủ...",
        status?.printer.name
          ? `${status.printer.name}: ${status.printer.message}`
          : status?.printer.message ?? "Đang kiểm tra máy in...",
        "Bấm để kiểm tra lại.",
      ].join("\n");

  return (
    <button
      type="button"
      onClick={onRefresh}
      disabled={loading}
      title={title}
      className="gc-icon-btn relative grid h-10 w-10 shrink-0 place-items-center overflow-visible rounded-xl p-0 disabled:opacity-70"
      aria-label={`Trạng thái máy chủ và máy in: ${stateLabel}. Bấm để kiểm tra lại`}
    >
      <span className="relative grid h-7 w-7 place-items-center text-[var(--gc-text-soft)]">
        <Server className={`h-[18px] w-[18px] ${loading ? "animate-pulse" : ""}`} aria-hidden="true" />
        <span className="absolute -bottom-1 -right-1 grid h-4 w-4 place-items-center rounded-full bg-white text-[var(--gc-text-soft)] shadow-sm dark:bg-slate-800">
          <Printer className="h-2.5 w-2.5" aria-hidden="true" />
        </span>
      </span>
      <span
        className={`absolute right-0.5 top-0.5 h-2.5 w-2.5 rounded-full border-2 border-white shadow-sm dark:border-slate-900 ${
          loading && !status
            ? "animate-pulse bg-sky-500"
            : error || printerProblem
              ? "bg-rose-500"
              : healthy
                ? "bg-emerald-500"
                : "bg-amber-500"
        }`}
        aria-hidden="true"
      />
    </button>
  );
}

function SortHeader({
  label,
  sortKey,
  sort,
  onSort,
  align,
}: {
  label: string;
  sortKey: SortKey;
  sort: SortState;
  onSort: (key: SortKey) => void;
  align?: "right";
}) {
  const active = sort.key === sortKey;
  const Icon = !active ? ArrowUpDown : sort.dir === "asc" ? ArrowUp : ArrowDown;
  return (
    <th className={align === "right" ? "text-right" : undefined}>
      <button
        type="button"
        onClick={() => onSort(sortKey)}
        className={`gc-sort ${align === "right" ? "ml-auto flex-row-reverse" : ""} ${active ? "is-active" : ""}`}
        aria-label={`Sắp xếp theo ${label}`}
      >
        <span>{label}</span>
        <Icon className="h-3.5 w-3.5 shrink-0" />
      </button>
    </th>
  );
}
