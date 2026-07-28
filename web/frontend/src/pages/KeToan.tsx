import { useEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import { MotionConfig, motion } from "motion/react";
import { ArrowDown, ArrowUp, ArrowUpDown, CalendarDays, CircleDollarSign, Download, FileText, FilterX, Loader2, Pencil, Printer, Search, Trash2, TriangleAlert, Wallet, X } from "lucide-react";
import { GlassCapsule } from "../components/glass/GlassCapsule";
import { GlassPanel } from "../components/glass/GlassPanel";
import { LiquidTabs, type LiquidTab } from "../components/glass/LiquidTabs";
import { Button as GlassButton } from "../shadcn/button";
import { Modal } from "../components/Modal";
import { useAppNotifications } from "../components/AppNotifications";
import { MonthPicker } from "../components/DateField";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { isKeepCreateVoucherOpenEnabled, subscribeKeepCreateVoucherOpenEnabled } from "../lib/accountingPreferences";
import { money, date } from "../lib/format";
import { documentTypeText, inferDocumentKind, type DocumentKind } from "../lib/documents";
import type { Customer, DocumentDetail, DocumentListItem } from "../lib/types";
import { StatCard } from "../features/giacong/StatCard";
import { DocumentEditor } from "./DocumentEditor";
import "../features/giacong/giacong.css";

type PrintableDocument = { row: DocumentListItem; detail: DocumentDetail | null };

const EASE_IOS = [0.22, 1, 0.36, 1] as const;
const FORCE_FULL_MOTION =
  import.meta.env.DEV &&
  typeof localStorage !== "undefined" &&
  localStorage.getItem("force-full-motion") === "true";

const ACCOUNTING_TABS: LiquidTab[] = [
  { key: "all", label: "Tất cả" },
  { key: "receipt", label: "Phiếu thu" },
  { key: "payment", label: "Phiếu chi" },
  { key: "document", label: "Phiếu xuất kho bán hàng" },
];

const currentMonth = () => new Date().toISOString().slice(0, 7);

const monthLabel = (value: string) => {
  if (!value) return "Tất cả";
  const [year, month] = value.split("-");
  return `${month}/${year}`;
};

const isInMonth = (value: string, month: string) => !month || value.startsWith(month);

const htmlEscape = (value: unknown) =>
  String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");

const qty = (value: number) =>
  new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 2 }).format(value || 0);

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
          {Array.from({ length: 8 }).map((_, j) => (
            <td key={j} className="px-3.5 py-3">
              <div className="gc-skeleton h-4" style={{ width: j === 7 ? "38%" : "86%" }} />
            </td>
          ))}
        </tr>
      ))}
    </>
  );
}

const printableLines = ({ row, detail }: PrintableDocument) =>
  detail?.lines.length
    ? detail.lines
    : [{ lineContent: row.content, spec: "", quantity: 1, unitPrice: row.total, note: "" }];

const printableTotal = (item: PrintableDocument) =>
  printableLines(item).reduce((sum, line) => sum + (line.quantity || 0) * (line.unitPrice || 0), 0);

const printDateParts = (value: string) => {
  const d = new Date(value.length <= 10 ? value + "T00:00:00" : value);
  if (isNaN(d.getTime())) return { day: "", month: "", year: "" };
  return { day: String(d.getDate()), month: String(d.getMonth() + 1), year: String(d.getFullYear()) };
};

function buildAccountingPrintSheet(item: PrintableDocument, printedAt: string) {
  const { row } = item;
  const lines = printableLines(item);
  const total = printableTotal(item);

  return `<section class="sheet accounting-sheet">
  <div class="top">
    <div>
      <div class="company">Đồng Hành</div>
      <div>Bộ phận kế toán</div>
    </div>
    <div>In lúc: ${htmlEscape(printedAt)}</div>
  </div>
  <h1>${htmlEscape(documentTypeText(row))}</h1>
  <div class="date-line">Ngày ${htmlEscape(date(row.date))}</div>
  <div class="info">
    <div class="label">Số phiếu</div><div><strong>${htmlEscape(row.voucherNo)}</strong></div>
    <div class="label">Người lập</div><div>${htmlEscape(row.createdBy || "Chưa rõ")}</div>
    <div class="label">Khách hàng</div><div>${htmlEscape(row.customerName || "Khách lẻ")}</div>
    <div class="label">Tổng tiền</div><div><strong>${htmlEscape(money(total))} đ</strong></div>
    <div class="label">Nội dung</div><div style="grid-column: span 3;">${htmlEscape(row.content)}</div>
  </div>
  <table class="accounting-table">
    <thead>
      <tr>
        <th style="width: 42px;">STT</th>
        <th>Nội dung</th>
        <th style="width: 95px;">Quy cách</th>
        <th style="width: 70px;">SL</th>
        <th style="width: 105px;">Đơn giá</th>
        <th style="width: 115px;">Thành tiền</th>
      </tr>
    </thead>
    <tbody>
      ${lines
        .map(
          (line, index) => `<tr>
        <td class="center">${index + 1}</td>
        <td>${htmlEscape(line.lineContent || row.content)}</td>
        <td>${htmlEscape(line.spec)}</td>
        <td class="right">${htmlEscape(qty(line.quantity))}</td>
        <td class="right">${htmlEscape(money(line.unitPrice))}</td>
        <td class="right">${htmlEscape(money(line.quantity * line.unitPrice))}</td>
      </tr>`,
        )
        .join("")}
    </tbody>
    <tfoot>
      <tr class="total"><td colspan="5" class="right">Tổng cộng</td><td class="right">${htmlEscape(money(total))} đ</td></tr>
    </tfoot>
  </table>
  <div class="signatures">
    <div><div class="signature-title">Người lập</div><div class="signature-note">(Ký, ghi rõ họ tên)</div><div class="signature-space"></div></div>
    <div><div class="signature-title">Kế toán trưởng</div><div class="signature-note">(Ký, ghi rõ họ tên)</div><div class="signature-space"></div></div>
    <div><div class="signature-title">Người nhận/nộp</div><div class="signature-note">(Ký, ghi rõ họ tên)</div><div class="signature-space"></div></div>
    <div><div class="signature-title">Thủ quỹ</div><div class="signature-note">(Ký, ghi rõ họ tên)</div><div class="signature-space"></div></div>
  </div>
</section>`;
}

function buildWarehouseDeliverySheet(item: PrintableDocument) {
  const { row, detail } = item;
  const lines = printableLines(item);
  const total = printableTotal(item);
  const { day, month, year } = printDateParts(row.date);
  const tableRows = Array.from({ length: 14 }, (_, index) => {
    const line = lines[index];
    return `<tr>
      <td class="xk-center">${index + 1}</td>
      <td>${line ? htmlEscape(line.lineContent || row.content) : ""}</td>
      <td>${line ? htmlEscape(line.spec) : ""}</td>
      <td class="xk-right">${line ? htmlEscape(qty(line.quantity)) : ""}</td>
      <td class="xk-right">${line ? htmlEscape(money(line.unitPrice)) : ""}</td>
      <td class="xk-right">${line ? htmlEscape(money((line.quantity || 0) * (line.unitPrice || 0))) : ""}</td>
    </tr>`;
  }).join("");

  return `<section class="sheet xk-sheet">
    <div class="xk-header">
      <div class="xk-company">
        <div class="xk-national">CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM</div>
        <div class="xk-slogan">ĐỘC LẬP - TỰ DO - HẠNH PHÚC</div>
        <div class="xk-name">CÔNG TY TNHH INOX CƯỜNG PHÁT</div>
        <div>Đ/C: Số 12, Đường Ganuda Garden 3-7/1B, Khu đô thị Gamuda Gardens, Phường Hoàng Mai, Thành phố Hà Nội</div>
        <div>MST: 0105844593 - ĐT: 0919.304.316 / 0834.304.316</div>
        <div>ĐC/VPGD - Kho: Xóm 3 - Thôn Văn Giáp - Xã Thường Tín - Thành phố Hà Nội</div>
        <div>TK1: 020017028686 tại Ngân hàng Sacombank - PGD Tân Mai - CN Thanh Trì - Hà Nội</div>
        <div>TK2: 1037526789 tại Ngân hàng Vietcombank - CN Tây Hồ - Hà Nội</div>
      </div>
    </div>

    <h1 class="xk-title">BIÊN BẢN GIAO NHẬN HÀNG KIÊM GIẤY XÁC NHẬN NỢ</h1>
    <div class="xk-form-lines">
      <div><span>Bên mua hàng:</span><strong>${htmlEscape(row.customerName || "Khách lẻ")}</strong></div>
      <div><span>Địa chỉ:</span><strong>${htmlEscape(detail?.note || "")}</strong></div>
    </div>
    <div class="xk-date-row">
      <div>Hà Nội, ngày <strong>${htmlEscape(day)}</strong> tháng <strong>${htmlEscape(month)}</strong> năm <strong>${htmlEscape(year)}</strong></div>
      <div>Số: <strong>${htmlEscape(row.voucherNo)}</strong></div>
    </div>

    <div class="xk-watermark">CP</div>
    <table class="xk-table">
      <thead>
        <tr>
          <th style="width: 46px;">STT</th>
          <th>Chủng loại hàng hóa</th>
          <th style="width: 84px;">Mã hàng</th>
          <th style="width: 110px;">Khối lượng<br />(kg)</th>
          <th style="width: 130px;">Đơn giá</th>
          <th style="width: 155px;">Thành tiền</th>
        </tr>
      </thead>
      <tbody>${tableRows}</tbody>
      <tfoot>
        <tr>
          <td></td>
          <td class="xk-total-label">TỔNG CỘNG</td>
          <td></td>
          <td></td>
          <td></td>
          <td class="xk-right">${htmlEscape(money(total))}</td>
        </tr>
      </tfoot>
    </table>

    <div class="xk-payment">
      <div><span>Thanh toán tiền:</span><strong>${htmlEscape(row.content || documentTypeText(row))}</strong></div>
      <div><span>Số tiền đã thanh toán:</span><strong></strong></div>
      <div><span>Số tiền còn phải thanh toán:</span><strong>${htmlEscape(money(total))} đ</strong></div>
    </div>

    <div class="xk-terms">
      <strong>Điều khoản thanh toán:</strong>
      <p>- Bên mua hàng thanh toán tiền trong vòng ........ ngày kể từ ngày nhận hàng. Nếu bên mua hàng thanh toán không đúng hạn sẽ chịu lãi suất quá hạn theo lãi suất vay ngân hàng ngắn hạn tại thời điểm quá hạn.</p>
      <p>- Quý khách hàng vui lòng kiểm tra đúng số lượng, chủng loại, chất lượng hàng hóa khi nhận hàng.</p>
      <p>- Các chi phí gia công như: phủ No4 PVC, HL, nhám, cắt,... chưa có trong đơn giá hàng hóa. Quý khách hàng có nhu cầu xuất hóa đơn gia công vui lòng thông báo lại cho Công ty chúng tôi.</p>
      <p>- Mọi khiếu nại về hàng hóa có hiệu lực trong vòng 5 ngày kể từ ngày bên mua nhận hàng.</p>
    </div>

    <div class="xk-signatures">
      <div><strong>Xác nhận của thủ kho</strong><em>(Ký, ghi rõ họ tên)</em></div>
      <div><strong>Nhân viên bán hàng</strong><em>(Ký, ghi rõ họ tên)</em></div>
      <div><strong>Đại diện bên mua hàng</strong><em>(Ký, ghi rõ họ tên)</em></div>
    </div>
  </section>`;
}

function buildPrintHtml(items: PrintableDocument[]) {
  const printedAt = new Date().toLocaleString("vi-VN");

  return `<!doctype html>
<html lang="vi">
<head>
  <meta charset="utf-8" />
  <title>In phiếu</title>
  <style>
    * { box-sizing: border-box; }
    body { margin: 0; color: #111827; font-family: "Times New Roman", serif; background: #f3f4f6; }
    .sheet { width: 210mm; min-height: 297mm; margin: 0 auto 12px; padding: 18mm 16mm; background: white; page-break-after: always; }
    .top { display: flex; justify-content: space-between; gap: 24px; font-size: 12px; }
    .company { font-weight: 700; text-transform: uppercase; }
    .accounting-sheet h1 { margin: 24px 0 4px; text-align: center; font-size: 24px; text-transform: uppercase; letter-spacing: 0; }
    .date-line { text-align: center; font-style: italic; margin-bottom: 20px; }
    .info { display: grid; grid-template-columns: 34mm 1fr 28mm 1fr; gap: 8px 12px; margin-bottom: 16px; font-size: 14px; }
    .label { color: #4b5563; }
    .accounting-table { width: 100%; border-collapse: collapse; font-size: 13px; }
    .accounting-table th, .accounting-table td { border: 1px solid #1f2937; padding: 6px 7px; vertical-align: top; }
    .accounting-table th { background: #f3f4f6; text-align: center; font-weight: 700; }
    .right { text-align: right; }
    .center { text-align: center; }
    .total td { font-weight: 700; }
    .signatures { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; margin-top: 28px; text-align: center; font-size: 13px; }
    .signature-title { font-weight: 700; }
    .signature-note { margin-top: 4px; font-style: italic; color: #4b5563; }
    .signature-space { height: 68px; }
    .xk-sheet { --xk-blue: #0b6fae; --xk-blue-rgb: 11, 111, 174; position: relative; overflow: hidden; padding: 6mm 9mm 7mm; color: var(--xk-blue); page-break-after: auto; }
    .xk-header { display: block; align-items: start; }
    .xk-company { max-width: 178mm; margin: 0 auto; text-align: center; font-size: 9.4pt; font-weight: 700; line-height: 1.14; }
    .xk-national, .xk-slogan, .xk-name { font-size: 13.2pt; font-weight: 900; }
    .xk-slogan { display: inline-block; margin-bottom: 1mm; border-bottom: 1.5px solid var(--xk-blue); padding: 0 10mm .5mm; }
    .xk-name { margin-bottom: .5mm; font-size: 15pt; }
    .xk-title { margin: 5mm 0 2.5mm; text-align: center; font-size: 16.5pt; font-weight: 900; letter-spacing: .2px; }
    .xk-form-lines { display: grid; gap: 2mm; font-size: 11.6pt; font-weight: 700; }
    .xk-form-lines div, .xk-payment div { display: grid; grid-template-columns: 34mm 1fr; gap: 2mm; align-items: end; }
    .xk-form-lines strong, .xk-payment strong { min-height: 4.6mm; border-bottom: 1px dotted rgba(var(--xk-blue-rgb), .55); color: #111827; font-weight: 700; }
    .xk-date-row { display: flex; justify-content: space-between; gap: 12mm; margin: 2.5mm 0 1.5mm; font-size: 11.8pt; font-weight: 700; }
    .xk-date-row strong { min-width: 12mm; display: inline-block; border-bottom: 1px dotted rgba(var(--xk-blue-rgb), .55); color: #111827; text-align: center; }
    .xk-watermark { position: absolute; left: 50%; top: 50%; z-index: 0; transform: translate(-50%, -50%); border: 7px solid rgba(var(--xk-blue-rgb), .09); color: rgba(var(--xk-blue-rgb), .09); font-size: 70pt; font-weight: 900; width: 72mm; height: 72mm; display: grid; place-items: center; clip-path: polygon(50% 0, 100% 96%, 0 96%); pointer-events: none; }
    .xk-table { position: relative; z-index: 1; width: 100%; border-collapse: collapse; table-layout: fixed; font-size: 10.8pt; }
    .xk-table th, .xk-table td { border: 1.4px solid var(--xk-blue); height: 6.75mm; padding: 1mm 1.2mm; vertical-align: middle; }
    .xk-table th { text-align: center; font-size: 10.8pt; font-weight: 900; }
    .xk-center { text-align: center; }
    .xk-right { text-align: right; color: #111827; }
    .xk-total-label { text-align: center; font-weight: 900; }
    .xk-payment { margin-top: 3mm; display: grid; gap: 1.4mm; font-size: 11.4pt; font-weight: 700; }
    .xk-terms { margin-top: 2.4mm; font-size: 8.7pt; font-style: italic; font-weight: 700; line-height: 1.08; }
    .xk-terms p { margin: .6mm 0 0; }
    .xk-signatures { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10mm; margin-top: 4.5mm; min-height: 18mm; text-align: center; font-size: 10.8pt; }
    .xk-signatures strong, .xk-signatures em { display: block; }
    .xk-signatures em { margin-top: 1mm; font-style: italic; }
    @page { size: A4; margin: 0; }
    @media print {
      body { background: white; }
      .sheet { margin: 0; box-shadow: none; }
    }
  </style>
</head>
<body>
${items
  .map((item) => (inferDocumentKind(item.row) === "document" ? buildWarehouseDeliverySheet(item) : buildAccountingPrintSheet(item, printedAt)))
  .join("")}
</body>
</html>`;
}

function buildExcelHtml(items: PrintableDocument[], month: string) {
  const total = items.reduce((sum, item) => sum + item.row.total, 0);
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
      <tr><th class="title" colspan="14">Chi tiết phiếu kế toán tháng ${htmlEscape(monthLabel(month))}</th></tr>
      <tr><td class="subtitle" colspan="14">Mỗi dòng trong file là một dòng hàng của phiếu; các ô có viền lưới như bảng Excel.</td></tr>
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
        <td>${htmlEscape(documentTypeText(row))}</td>
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
  const [kindFilter, setKindFilter] = useState("all");
  const [month, setMonth] = useState(currentMonth);
  const [sort, setSort] = useState<SortState>({ key: "date", dir: "desc" });
  const [initialKind, setInitialKind] = useState<DocumentKind>("document");
  const [editing, setEditing] = useState<string | null | "new">(null);
  const [deleting, setDeleting] = useState<DocumentListItem | null>(null);
  const [deletingBusy, setDeletingBusy] = useState(false);
  const [printingId, setPrintingId] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);
  const [keepCreateOpen, setKeepCreateOpen] = useState(() =>
    user ? isKeepCreateVoucherOpenEnabled(user.id) : false,
  );
  const searchRef = useRef<HTMLInputElement>(null);

  const monthRows = useMemo(
    () => (data ?? []).filter((d) => isInMonth(d.date, month)),
    [data, month]
  );

  // Thống kê bám theo tháng đang chọn để 4 thẻ luôn nhất quán với bảng.
  const stats = useMemo(() => {
    const receipts = monthRows.filter((row) => inferDocumentKind(row) === "receipt");
    const payments = monthRows.filter((row) => inferDocumentKind(row) === "payment");
    const documents = monthRows.filter((row) => inferDocumentKind(row) === "document");
    const monthTotal = monthRows.reduce((sum, row) => sum + (row.total || 0), 0);
    return {
      receiptCount: receipts.length,
      receiptTotal: receipts.reduce((sum, row) => sum + (row.total || 0), 0),
      paymentCount: payments.length,
      paymentTotal: payments.reduce((sum, row) => sum + (row.total || 0), 0),
      documentCount: documents.length,
      monthTotal,
    };
  }, [monthRows]);

  const rows = useMemo(() => {
    const filtered = monthRows.filter((d) => {
      const q = search.trim().toLowerCase();
      if (kindFilter !== "all" && inferDocumentKind(d) !== kindFilter) return false;
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
  }, [kindFilter, monthRows, search, sort]);

  const visibleTotal = useMemo(() => rows.reduce((sum, row) => sum + (row.total || 0), 0), [rows]);
  const hasActiveFilters = search.trim() !== "" || kindFilter !== "all";
  const hasAnyData = (data?.length ?? 0) > 0;
  const periodLabel = month ? `Tháng ${monthLabel(month)}` : "Tất cả các tháng";

  const clearFilters = () => {
    setSearch("");
    setKindFilter("all");
  };

  const toggleSort = (key: SortKey) =>
    setSort((current) =>
      current.key === key
        ? { key, dir: current.dir === "asc" ? "desc" : "asc" }
        : { key, dir: key === "date" || key === "total" ? "desc" : "asc" },
    );

  useEffect(() => {
    if (!user) {
      setKeepCreateOpen(false);
      return;
    }

    setKeepCreateOpen(isKeepCreateVoucherOpenEnabled(user.id));
    return subscribeKeepCreateVoucherOpenEnabled(user.id, () => {
      setKeepCreateOpen(isKeepCreateVoucherOpenEnabled(user.id));
    });
  }, [user]);

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

  const startCreate = (kind: DocumentKind) => {
    setInitialKind(kind);
    setEditing("new");
  };

  const confirmDelete = async () => {
    if (!deleting) return;
    setDeletingBusy(true);
    try {
      await api.del(`/api/documents/${deleting.id}`);
      setDeleting(null);
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Lỗi xóa phiếu.");
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

  const printVoucher = async (row: DocumentListItem) => {
    const printWindow = window.open("", "_blank", "width=1024,height=768");
    if (!printWindow) {
      notify.warning("Trình duyệt đang chặn cửa sổ in. Vui lòng cho phép popup rồi thử lại.");
      return;
    }

    printWindow.document.write("<p style='font-family: sans-serif; padding: 24px;'>Đang chuẩn bị phiếu in...</p>");
    setPrintingId(row.id);
    try {
      const item = await loadPrintableDocument(row);
      printWindow.document.open();
      printWindow.document.write(buildPrintHtml([item]));
      printWindow.document.close();
      printWindow.focus();
      setTimeout(() => printWindow.print(), 250);
    } finally {
      setPrintingId(null);
    }
  };

  const exportMonthExcel = async () => {
    if (!monthRows.length) {
      notify.info(`Không có phiếu trong tháng ${monthLabel(month)} để xuất Excel.`);
      return;
    }

    setExporting(true);
    try {
      const items = await Promise.all(
        monthRows.map((row) => loadPrintableDocument(row)),
      );

      const blob = new Blob([buildExcelHtml(items, month)], {
        type: "application/vnd.ms-excel;charset=utf-8",
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `chi-tiet-phieu-ke-toan-${month || "tat-ca"}.xls`;
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
              Sổ phiếu rõ ràng cho nghiệp vụ thu, chi và xuất kho bán hàng
            </motion.p>
          </div>
          <motion.div
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.14, ease: EASE_IOS }}
            className="flex flex-wrap items-center gap-2.5"
          >
            <GlassButton onClick={() => startCreate("receipt")}>
              <CircleDollarSign className="h-4 w-4" /> Tạo phiếu thu
            </GlassButton>
            <GlassButton variant="soft" onClick={() => startCreate("payment")}>
              <Wallet className="h-4 w-4" /> Tạo phiếu chi
            </GlassButton>
            <GlassButton variant="soft" onClick={() => startCreate("document")}>
              <FileText className="h-4 w-4" /> Tạo phiếu xuất kho
            </GlassButton>
            <GlassButton variant="ghost" onClick={exportMonthExcel} disabled={exporting}>
              {exporting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
              Xuất Excel
            </GlassButton>
          </motion.div>
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            index={0}
            icon={CircleDollarSign}
            label="Phiếu thu"
            value={`${money(stats.receiptTotal)} ₫`}
            sub={`${money(stats.receiptCount)} phiếu · ${periodLabel}`}
            tone="0, 184, 148"
          />
          <StatCard
            index={1}
            icon={Wallet}
            label="Phiếu chi"
            value={`${money(stats.paymentTotal)} ₫`}
            sub={`${money(stats.paymentCount)} phiếu · ${periodLabel}`}
            tone="217, 119, 6"
          />
          <StatCard
            index={2}
            icon={FileText}
            label="Phiếu xuất kho"
            value={money(stats.documentCount)}
            sub={`Bán hàng · ${periodLabel}`}
            tone="31, 107, 255"
          />
          <StatCard
            index={3}
            icon={CalendarDays}
            label="Tổng cộng"
            value={`${money(stats.monthTotal)} ₫`}
            sub={periodLabel}
            tone="124, 70, 255"
          />
        </div>

        <GlassPanel className="flex flex-wrap items-center gap-3 rounded-[20px] p-3">
          <LiquidTabs tabs={ACCOUNTING_TABS} value={kindFilter} onChange={setKindFilter} />

          <div className="ml-auto grid flex-1 grid-cols-1 items-center gap-2.5 md:grid-cols-[minmax(220px,1fr)_180px_auto]">
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

            <MonthPicker value={month} onChange={setMonth} ariaLabel="Lọc theo tháng" placeholder="Tất cả các tháng" />

            <div className="flex items-center gap-2">
              <div className="whitespace-nowrap rounded-xl border border-[var(--gc-border)] bg-white/20 px-3 py-2 text-sm font-bold text-[var(--gc-text-soft)] dark:bg-white/5">
                {hasActiveFilters ? (
                  <>
                    {rows.length}<span className="text-[var(--gc-text-muted)]">/{monthRows.length}</span> phiếu
                  </>
                ) : (
                  <>{monthRows.length} phiếu · {periodLabel}</>
                )}
              </div>
              {hasActiveFilters && (
                <button
                  type="button"
                  onClick={clearFilters}
                  className="gc-icon-btn inline-flex h-[38px] items-center gap-1.5 whitespace-nowrap px-3 text-sm font-bold text-[var(--gc-text-soft)]"
                  aria-label="Xóa bộ lọc tìm kiếm và loại phiếu"
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
                  <th className="w-28 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {loading ? (
                  <SkeletonRows />
                ) : error ? (
                  <tr>
                    <td colSpan={8} className="py-14 text-center text-sm font-semibold text-rose-500">
                      {error}
                    </td>
                  </tr>
                ) : rows.length === 0 ? (
                  <tr>
                    <td colSpan={8}>
                      <div className="flex flex-col items-center justify-center gap-2.5 py-16 text-center">
                        {hasActiveFilters || hasAnyData ? (
                          <>
                            <Search className="h-9 w-9 text-[var(--gc-text-muted)] opacity-70" />
                            <p className="text-sm font-semibold text-[var(--gc-text-soft)]">Không tìm thấy phiếu phù hợp</p>
                            <p className="text-xs text-[var(--gc-text-muted)]">Thử đổi từ khóa, loại phiếu hoặc tháng đang lọc.</p>
                            <button
                              type="button"
                              onClick={() => {
                                clearFilters();
                                setMonth("");
                              }}
                              className="gc-icon-btn mt-1 inline-flex h-9 items-center gap-1.5 px-3.5 text-sm font-bold text-[var(--gc-text-soft)]"
                            >
                              <FilterX className="h-4 w-4" /> Xóa toàn bộ bộ lọc
                            </button>
                          </>
                        ) : (
                          <>
                            <FileText className="h-9 w-9 text-[var(--gc-text-muted)] opacity-70" />
                            <p className="text-sm font-semibold text-[var(--gc-text-soft)]">Chưa có phiếu kế toán</p>
                            <p className="text-xs text-[var(--gc-text-muted)]">Tạo phiếu thu, phiếu chi hoặc phiếu xuất kho bán hàng.</p>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ) : (
                  rows.map((row) => (
                    <tr key={row.id} onClick={() => setEditing(row.id)}>
                      <td className="whitespace-nowrap font-bold text-[var(--gc-text)]">{row.voucherNo}</td>
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
                      <td className="text-right" onClick={(e) => e.stopPropagation()}>
                        <div className="flex justify-end gap-1.5">
                          <button
                            type="button"
                            title="In phiếu"
                            aria-label={`In phiếu ${row.voucherNo}`}
                            disabled={printingId === row.id}
                            onClick={() => void printVoucher(row)}
                            className="gc-icon-btn h-8 w-8 disabled:pointer-events-none disabled:opacity-50"
                          >
                            {printingId === row.id ? <Loader2 className="h-4 w-4 animate-spin" /> : <Printer className="h-4 w-4" />}
                          </button>
                          <button
                            type="button"
                            title="Sửa phiếu"
                            aria-label={`Sửa phiếu ${row.voucherNo}`}
                            onClick={() => setEditing(row.id)}
                            className="gc-icon-btn h-8 w-8"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                          <button
                            type="button"
                            title="Xóa phiếu"
                            aria-label={`Xóa phiếu ${row.voucherNo}`}
                            onClick={() => setDeleting(row)}
                            className="gc-icon-btn h-8 w-8 text-rose-500 hover:text-rose-600"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
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
                    <td colSpan={2} />
                  </tr>
                </tfoot>
              )}
            </table>
          </div>
        </GlassPanel>

        {editing !== null && (
          <DocumentEditor
            id={editing}
            initialKind={initialKind}
            customers={customers ?? []}
            onPrint={
              editing !== "new"
                ? () => {
                    const row = data?.find((item) => item.id === editing);
                    if (row) void printVoucher(row);
                  }
                : undefined
            }
            printLoading={editing !== "new" && printingId === editing}
            keepOpenAfterSave={editing === "new" && keepCreateOpen}
            onClose={() => setEditing(null)}
            onSaved={() => {
              if (!(editing === "new" && keepCreateOpen)) setEditing(null);
              reload();
            }}
          />
        )}

        {deleting && (
          <Modal
            open
            solid
            title="Xóa phiếu kế toán"
            onClose={() => {
              if (!deletingBusy) setDeleting(null);
            }}
            footer={
              <>
                <GlassButton variant="ghost" onClick={() => setDeleting(null)} disabled={deletingBusy}>
                  Hủy
                </GlassButton>
                <GlassButton variant="danger" onClick={confirmDelete} disabled={deletingBusy}>
                  {deletingBusy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}
                  Xóa phiếu
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
                  Bạn có chắc muốn xóa phiếu{" "}
                  <span className="font-bold">{documentTypeText(deleting).toLowerCase()}</span>{" "}
                  <span className="font-bold">{deleting.voucherNo}</span>?
                </p>
                <div className="rounded-xl border border-[var(--glass-border)] bg-white/30 px-3 py-2.5 text-[var(--text-secondary)] dark:bg-white/5">
                  <div>Khách hàng: <span className="font-semibold text-[var(--text)]">{deleting.customerName || "Khách lẻ"}</span></div>
                  <div>Ngày: <span className="font-semibold text-[var(--text)]">{date(deleting.date)}</span></div>
                  <div>Tổng tiền: <span className="font-semibold text-[var(--text)]">{money(deleting.total)} ₫</span></div>
                </div>
                <p className="text-xs font-semibold text-rose-500">Hành động này không thể hoàn tác.</p>
              </div>
            </div>
          </Modal>
        )}
      </div>
    </MotionConfig>
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
