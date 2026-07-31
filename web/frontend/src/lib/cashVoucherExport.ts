import { documentTypeText } from "./documents";
import { date, money } from "./format";
import type { DocumentDetail, DocumentListItem } from "./types";

export type CashPrintableDocument = {
  row: DocumentListItem;
  detail: DocumentDetail | null;
};

const escapeHtml = (value: unknown) =>
  String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");

const linesOf = ({ row, detail }: CashPrintableDocument) =>
  detail?.lines.length
    ? detail.lines
    : [{ lineContent: row.content, spec: "", quantity: 1, unitPrice: row.total, note: "" }];

export function buildCashPrintHtml(item: CashPrintableDocument) {
  const { row } = item;
  const lines = linesOf(item);
  const total = lines.reduce((sum, line) => sum + line.quantity * line.unitPrice, 0);
  return `<!doctype html>
<html lang="vi">
<head>
  <meta charset="utf-8" />
  <title>${escapeHtml(documentTypeText(row))} ${escapeHtml(row.voucherNo)}</title>
  <style>
    * { box-sizing: border-box; }
    body { margin: 0; color: #111827; font-family: "Times New Roman", serif; }
    .sheet { width: 210mm; min-height: 297mm; margin: auto; padding: 18mm 16mm; }
    .top { display: flex; justify-content: space-between; font-size: 12px; }
    h1 { margin: 24px 0 4px; text-align: center; font-size: 25px; text-transform: uppercase; }
    .date { margin-bottom: 22px; text-align: center; font-style: italic; }
    .info { display: grid; grid-template-columns: 32mm 1fr; gap: 8px 12px; margin-bottom: 18px; font-size: 14px; }
    .label { color: #4b5563; }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    th, td { border: 1px solid #1f2937; padding: 7px; }
    th { background: #f3f4f6; }
    .right { text-align: right; }
    .center { text-align: center; }
    .total { font-weight: 700; }
    .sign { display: grid; grid-template-columns: repeat(3, 1fr); margin-top: 30px; text-align: center; font-size: 13px; }
    .space { height: 78px; }
    @page { size: A4; margin: 0; }
  </style>
</head>
<body>
  <section class="sheet">
    <div class="top"><strong>KetoanMini · Bộ phận kế toán</strong><span>In lúc ${escapeHtml(new Date().toLocaleString("vi-VN"))}</span></div>
    <h1>${escapeHtml(documentTypeText(row))}</h1>
    <div class="date">Ngày ${escapeHtml(date(row.date))}</div>
    <div class="info">
      <div class="label">Số phiếu</div><div><strong>${escapeHtml(row.voucherNo)}</strong></div>
      <div class="label">Khách hàng</div><div>${escapeHtml(row.customerName || "Khách lẻ")}</div>
      <div class="label">Nội dung</div><div>${escapeHtml(row.content)}</div>
      <div class="label">Ghi chú</div><div>${escapeHtml(item.detail?.note || "")}</div>
    </div>
    <table>
      <thead><tr><th style="width:45px">STT</th><th>Nội dung</th><th>Quy cách</th><th style="width:75px">SL</th><th style="width:120px">Đơn giá</th><th style="width:130px">Thành tiền</th></tr></thead>
      <tbody>${lines.map((line, index) => `<tr>
        <td class="center">${index + 1}</td>
        <td>${escapeHtml(line.lineContent || row.content)}</td>
        <td>${escapeHtml(line.spec)}</td>
        <td class="right">${escapeHtml(line.quantity)}</td>
        <td class="right">${escapeHtml(money(line.unitPrice))}</td>
        <td class="right">${escapeHtml(money(line.quantity * line.unitPrice))}</td>
      </tr>`).join("")}</tbody>
      <tfoot><tr class="total"><td colspan="5" class="right">Tổng cộng</td><td class="right">${escapeHtml(money(total))} ₫</td></tr></tfoot>
    </table>
    <div class="sign">
      <div><strong>Người lập</strong><div class="space"></div></div>
      <div><strong>Người nộp/nhận</strong><div class="space"></div></div>
      <div><strong>Kế toán trưởng</strong><div class="space"></div></div>
    </div>
  </section>
</body>
</html>`;
}

export function buildCashExcelHtml(items: CashPrintableDocument[], month: string) {
  const rows = items.flatMap((item) =>
    linesOf(item).map((line, index) => ({
      row: item.row,
      line,
      index,
    })),
  );
  return `\uFEFF<!doctype html>
<html lang="vi" xmlns:x="urn:schemas-microsoft-com:office:excel">
<head>
  <meta charset="utf-8" />
  <style>
    table { border-collapse: collapse; font-family: Arial, sans-serif; font-size: 11pt; }
    th, td { border: .5pt solid #d9d9d9; padding: 5px 7px; }
    th { background: #eaf2f8; font-weight: 700; text-align: center; }
    .title { background: #dbeafe; font-size: 14pt; }
    .right { text-align: right; mso-number-format: "#,##0.##"; }
  </style>
</head>
<body>
  <table>
    <thead>
      <tr><th class="title" colspan="12">Sổ thu chi ${escapeHtml(month || "tất cả")}</th></tr>
      <tr><th>Số phiếu</th><th>Ngày</th><th>Loại</th><th>Khách hàng</th><th>Nội dung phiếu</th><th>STT</th><th>Nội dung dòng</th><th>Quy cách</th><th>Số lượng</th><th>Đơn giá</th><th>Thành tiền</th><th>Người lập</th></tr>
    </thead>
    <tbody>${rows.map(({ row, line, index }) => `<tr>
      <td>${escapeHtml(row.voucherNo)}</td><td>${escapeHtml(date(row.date))}</td><td>${escapeHtml(`${documentTypeText(row)}${row.cancelledAt ? " · Đã hủy" : ""}`)}</td>
      <td>${escapeHtml(row.customerName)}</td><td>${escapeHtml(row.content)}</td><td>${index + 1}</td>
      <td>${escapeHtml(line.lineContent)}</td><td>${escapeHtml(line.spec)}</td><td class="right">${line.quantity}</td>
      <td class="right">${line.unitPrice}</td><td class="right">${line.quantity * line.unitPrice}</td><td>${escapeHtml(row.createdBy || "")}</td>
    </tr>`).join("")}</tbody>
  </table>
</body>
</html>`;
}
