import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowDown, ArrowUp, ArrowUpDown, Ban, FileText, FilterX, FileEdit, Loader2, Printer, Search, Server, TriangleAlert, X } from "lucide-react";
import { Button as GlassButton } from "../shadcn/button";
import { ExportExcelButton, type ProgressReport } from "../components/ExportExcelButton";
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
import { documentTypeText } from "../lib/documents";
import type { AccountingSystemStatus, Customer, DocumentDetail, DocumentListItem } from "../lib/types";
import { DocumentEditor } from "./DocumentEditor";
import "../features/giacong/giacong.css";
import "./ban-hang.css";

type PrintableDocument = { row: DocumentListItem; detail: DocumentDetail | null };

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

type SortKey = "voucherNo" | "date" | "customerName" | "total";
type SortState = { key: SortKey; dir: "asc" | "desc" };

const compareRows = (a: DocumentListItem, b: DocumentListItem, key: SortKey) => {
  if (key === "total") return (a.total || 0) - (b.total || 0);
  const av = String((key === "customerName" ? a.customerName : a[key]) ?? "");
  const bv = String((key === "customerName" ? b.customerName : b[key]) ?? "");
  return av.localeCompare(bv, "vi", { numeric: true, sensitivity: "base" });
};

function SkeletonRows() {
  // Bề rộng vạch chờ đi theo bề rộng THẬT của từng cột, để lúc dữ liệu về bảng
  // không nhảy — vạch đều nhau mới là thứ trông giả.
  const widths = ["62%", "54%", "70%", "84%", "92%", "58%", "66%", "60%", "40%"];
  return (
    <>
      {Array.from({ length: 8 }).map((_, i) => (
        <tr key={i}>
          {widths.map((width, j) => (
            <td key={j}>
              <div className="bh-skel" style={{ width }} />
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
      color: #10233f;
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
      color: #52647f;
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
  const navigate = useNavigate();
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
      // false = nút In phiếu không chạy tiếp sang trạng thái "Đã in".
      return false;
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

  const exportRangeExcel = async (report: ProgressReport) => {
    if (!rangeRows.length) {
      notify.info(`Không có phiếu trong khoảng ${displayIsoDate(dateFrom)} – ${displayIsoDate(dateTo)} để xuất Excel.`);
      // false = nút không chạy tiếp sang trạng thái "Đã xuất".
      return false;
    }

    // Tiến trình thật: đếm từng phiếu đã tải xong chi tiết. Chừa 1 nhịp cuối cho khâu dựng file nên
    // tổng là số phiếu + 1 — thanh chỉ đầy khi file đã thật sự được tạo.
    const steps = rangeRows.length + 1;
    let loaded = 0;
    report(0, steps);
    const items = await Promise.all(
      rangeRows.map(async (row) => {
        const item = await loadPrintableDocument(row);
        loaded += 1;
        report(loaded, steps);
        return item;
      }),
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
  };

  return (
    <div className="bh">
      {/* ── Măng-sét: trang này là gì, kỳ nào, và những việc làm được ngay ─── */}
      <header className="bh-masthead">
        <div className="bh-masthead-id">
          <span className="bh-mark" aria-hidden="true" />
          <div className="min-w-0">
            <p className="bh-org">{APP_BRAND_NAME}</p>
            <h1 className="bh-title">Bán hàng</h1>
            <p className="bh-sub">
              Sổ phiếu xuất kho · kỳ{" "}
              <span className="bh-mono">
                {displayIsoDate(dateFrom)}–{displayIsoDate(dateTo)}
              </span>
            </p>
          </div>
        </div>
        <div className="bh-masthead-actions">
          <CompactSystemStatus
            status={systemStatus}
            error={systemStatusError}
            loading={checkingSystemStatus}
            onRefresh={() => void refreshSystemStatus()}
          />
          <ExportExcelButton className="bh-btn" onExport={exportRangeExcel} />
          <button type="button" className="bh-btn bh-btn--primary" onClick={() => setEditing("new")}>
            <FileText className="h-4 w-4" /> Tạo phiếu xuất kho
          </button>
        </div>
      </header>

      {/* ── Dải số liệu của kỳ ────────────────────────────────────────────────
          Bốn con số ngăn nhau bằng kẻ dọc, KHÔNG phải bốn cái thẻ có biểu tượng
          màu: ở đây người ta đọc số, biểu tượng chỉ chiếm chỗ. Chỉ "Tổng giá trị"
          được ăn màu nhấn — đó là con số người ta mở trang này để xem. */}
      <dl className="bh-figures">
        <div className="bh-figure">
          <dt className="bh-figure-label">Phiếu trong kỳ</dt>
          <dd className="bh-figure-value">{money(stats.documentCount)}</dd>
          <dd className="bh-figure-note">Tính cả phiếu đã hủy</dd>
        </div>
        <div className="bh-figure bh-figure--lead">
          <dt className="bh-figure-label">Tổng giá trị</dt>
          <dd className="bh-figure-value">
            {money(stats.monthTotal)}
            <small>₫</small>
          </dd>
          <dd className="bh-figure-note">Không tính phiếu đã hủy</dd>
        </div>
        <div className="bh-figure">
          <dt className="bh-figure-label">Đã phát hành</dt>
          <dd className="bh-figure-value">{money(stats.issuedCount)}</dd>
          <dd className="bh-figure-note">
            {stats.documentCount ? Math.round((stats.issuedCount / stats.documentCount) * 100) : 0}% số phiếu trong kỳ
          </dd>
        </div>
        <div className="bh-figure">
          <dt className="bh-figure-label">Còn là nháp</dt>
          <dd className="bh-figure-value">{money(stats.draftCount)}</dd>
          <dd className="bh-figure-note">Chưa in, chưa phát hành</dd>
        </div>
      </dl>

      {/* ── Thanh lọc ────────────────────────────────────────────────────── */}
      <div className="bh-toolbar">
        <div className="bh-search">
          <Search className="h-4 w-4 shrink-0" aria-hidden="true" />
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
              className="bh-search-clear"
              aria-label="Xóa từ khóa tìm kiếm"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          ) : (
            <kbd className="hidden sm:block">Ctrl K</kbd>
          )}
        </div>

        <div className="bh-toolbar-date">
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
        </div>

        <p className="bh-tally">
          {hasActiveFilters ? (
            <>
              <b>{rows.length}</b>
              <span>/ {rangeRows.length}</span> phiếu khớp bộ lọc
            </>
          ) : (
            <>
              <b>{rangeRows.length}</b> phiếu trong kỳ
            </>
          )}
        </p>

        {hasActiveFilters && (
          <button type="button" onClick={clearFilters} className="bh-btn" aria-label="Xóa bộ lọc tìm kiếm">
            <FilterX className="h-4 w-4" /> Xóa lọc
          </button>
        )}
      </div>

      {/* ── Tờ sổ ────────────────────────────────────────────────────────── */}
      <section className="bh-sheet" aria-label="Danh sách phiếu xuất kho">
        <div className="bh-sheet-scroll scroll-thin">
          <table className="bh-table">
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
                <th className="text-right">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <SkeletonRows />
              ) : error ? (
                <tr>
                  <td colSpan={9} className="bh-error">
                    {error}
                  </td>
                </tr>
              ) : rows.length === 0 ? (
                <tr>
                  <td colSpan={9}>
                    <div className="bh-empty">
                      {hasActiveFilters || hasAnyData ? (
                        <>
                          <p className="bh-empty-title">Không có phiếu nào khớp</p>
                          <p className="bh-empty-note">
                            Thử đổi từ khóa, hoặc nới rộng khoảng ngày đang lọc.
                          </p>
                          <button
                            type="button"
                            onClick={() => {
                              clearFilters();
                              const initialRange = currentDateRange();
                              setDateFrom(initialRange.from);
                              setDateTo(initialRange.to);
                            }}
                            className="bh-btn"
                          >
                            <FilterX className="h-4 w-4" /> Xóa toàn bộ bộ lọc
                          </button>
                        </>
                      ) : (
                        <>
                          <p className="bh-empty-title">Sổ chưa có phiếu nào</p>
                          <p className="bh-empty-note">
                            Bấm “Tạo phiếu xuất kho” ở đầu trang để lập tờ phiếu đầu tiên.
                          </p>
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ) : (
                rows.map((row) => (
                  <tr
                    key={row.id}
                    onClick={() => navigate(`/ban-hang/${row.id}`)}
                    className={row.cancelledAt ? "is-void" : ""}
                  >
                    <td className="bh-voucher">
                      {row.voucherNo || <span className="bh-muted">Chưa nhập</span>}
                    </td>
                    <td className="bh-date">{date(row.date)}</td>
                    <td>
                      <span className="bh-kind">{documentTypeText(row)}</span>
                    </td>
                    <td>{row.customerName || <span className="bh-muted">Khách lẻ</span>}</td>
                    <td className="bh-cell-wide">{row.content}</td>
                    <td className="bh-num">{money(row.total)} ₫</td>
                    <td>{row.createdBy || <span className="bh-muted">Chưa rõ</span>}</td>
                    <td>
                      <span
                        className={`bh-state bh-state--${
                          row.cancelledAt ? "void" : row.issuedAt ? "ok" : "draft"
                        }`}
                        title={
                          row.cancelledAt
                            ? `Hủy bởi ${row.cancelledBy || "không rõ"}${row.cancelReason ? `: ${row.cancelReason}` : ""}`
                            : row.issuedAt
                              ? `Phát hành lúc ${new Date(row.issuedAt).toLocaleString("vi-VN")}`
                              : "Phiếu chưa được in"
                        }
                      >
                        {row.cancelledAt ? "Đã hủy" : row.issuedAt ? "Đã phát hành" : "Phiếu nháp"}
                      </span>
                    </td>
                    <td onClick={(e) => e.stopPropagation()}>
                      <div className="flex justify-end gap-1">
                        <button
                          type="button"
                          title={row.cancelledAt ? "Phiếu đã hủy, không thể in" : "Xem trước và in"}
                          aria-label={`Xem trước và in phiếu ${row.voucherNo || row.customerName || row.id}`}
                          disabled={!!row.cancelledAt || printingId === row.id}
                          onClick={() => requestPrint(row)}
                          className="bh-ibtn"
                        >
                          {printingId === row.id ? <Loader2 className="h-4 w-4 animate-spin" /> : <Printer className="h-4 w-4" />}
                        </button>
                        {/* MỘT nút mở phiếu: sửa nội dung, giao hàng và đối soát nằm chung một
                            màn có tab. Ba việc này đều xoay quanh cùng một tờ phiếu, tách ba nút
                            (và ba hộp thoại) chỉ bắt kế toán đóng/mở qua lại. In và Hủy đứng
                            riêng vì là hai việc dứt khoát, làm xong là xong. */}
                        <button
                          type="button"
                          title={openHint(row)}
                          aria-label={`Mở phiếu ${row.voucherNo || row.customerName || row.id}`}
                          onClick={() => navigate(`/ban-hang/${row.id}`)}
                          className="bh-ibtn"
                        >
                          <FileEdit className="h-4 w-4" />
                          {waitingReturn(row) && <span aria-hidden="true" className="bh-flag" />}
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
                            className="bh-ibtn bh-ibtn--danger"
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
                <tr>
                  <td colSpan={5} className="bh-foot-label">
                    Cộng {rows.length} phiếu
                  </td>
                  <td className="bh-foot-total">{money(visibleTotal)} ₫</td>
                  <td colSpan={3} />
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      </section>

        {/* Hộp thoại chỉ còn cho việc TẠO phiếu mới — nhập nhanh, lặp lại nhiều lần. Phiếu đã có
            thì mở TRANG /ban-hang/:id: cả vòng đời một tờ phiếu cần chỗ rộng, không nhét vào popup. */}
        {editing === "new" && (
          <DocumentEditor
            key="new:document"
            id="new"
            initialKind="document"
            allowedKinds={["document"]}
            customers={customers ?? []}
            keepOpenAfterSave={keepCreateOpen}
            onClose={() => setEditing(null)}
            onSaved={() => {
              if (!keepCreateOpen) setEditing(null);
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
            onPrint={() => printVoucher(printPreview.originalRow, printPreview.voucherNo)}
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
  const state = loading && !status ? "idle" : error || printerProblem ? "error" : healthy ? "ok" : "warn";
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

  // Trạng thái máy in là thứ chặn cả việc phát hành phiếu, nên nó phải ĐỌC ĐƯỢC
  // ngay chứ không nấp sau một chấm màu chỉ hiện chữ khi rê chuột.
  return (
    <button
      type="button"
      onClick={onRefresh}
      disabled={loading}
      title={title}
      data-state={state}
      className="bh-status"
      aria-label={`Trạng thái máy chủ và máy in: ${stateLabel}. Bấm để kiểm tra lại`}
    >
      <span className="bh-status-dot" aria-hidden="true" />
      <Server className={`h-[15px] w-[15px] ${loading ? "animate-pulse" : ""}`} aria-hidden="true" />
      <span className="bh-status-label">{stateLabel}</span>
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
        className={`bh-sort ${align === "right" ? "bh-sort--right" : ""} ${active ? "is-active" : ""}`}
        aria-label={`Sắp xếp theo ${label}`}
        aria-sort={active ? (sort.dir === "asc" ? "ascending" : "descending") : "none"}
      >
        <span>{label}</span>
        <Icon className="h-3.5 w-3.5 shrink-0" />
      </button>
    </th>
  );
}

/** Việc cần làm NGAY: lái xe đã báo giao xong, đang chờ kế toán nhận lại tờ phiếu. */
function waitingReturn(row: DocumentListItem) {
  return (
    !row.cancelledAt &&
    !!row.issuedAt &&
    !row.deliveryReturnedAt &&
    // 'accepted' chỉ còn ở phiếu cũ (trước khi bỏ chặng nghiệm thu của việc giao hàng).
    (row.deliveryTaskStatus === "submitted" || row.deliveryTaskStatus === "accepted")
  );
}

function openHint(row: DocumentListItem) {
  if (row.cancelledAt) return "Xem phiếu đã hủy";
  if (waitingReturn(row)) return "Mở phiếu · đang chờ nhận lại phiếu để đối soát";
  return "Mở phiếu · sửa, giao hàng, đối soát";
}

