import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const inputPath = "C:\\Users\\Admin\\Desktop\\Chi phi nang ha 2026.xlsx";
const outputDir =
  "C:\\Users\\Admin\\Desktop\\KetoanMiniDotNet_Code_20260615_155926\\web\\outputs\\019fac34-f582-7003-a860-5b94a242681c";
const outputPath = path.join(outputDir, "Chi phi nang ha 2026 - tu dong dinh dang.xlsx");
const previewDir = path.join(
  "C:\\Users\\Admin\\Desktop\\KetoanMiniDotNet_Code_20260615_155926\\web\\.codex-spreadsheet-fix",
  "previews",
);

await fs.mkdir(outputDir, { recursive: true });
await fs.mkdir(previewDir, { recursive: true });

const input = await FileBlob.load(inputPath);
const workbook = await SpreadsheetFile.importXlsx(input);

const before = await workbook.inspect({
  kind: "workbook,sheet,table",
  maxChars: 5000,
  tableMaxRows: 5,
  tableMaxCols: 8,
});
console.log("BEFORE");
console.log(before.ndjson);

const sheet = workbook.worksheets.getItem("Chi phi nang ha");
const table = sheet.tables.items[0];
if (!table) throw new Error("Không tìm thấy Excel Table trên sheet Chi phi nang ha.");

const firstDataRow = 8;
const colAValues = sheet.getRange("A8:A1000").values;
let oldLastRow = firstDataRow - 1;
for (let i = 0; i < colAValues.length; i += 1) {
  if (colAValues[i][0] !== null && colAValues[i][0] !== "") {
    oldLastRow = firstDataRow + i;
  }
}
if (oldLastRow < firstDataRow) {
  throw new Error("Bảng không có dòng dữ liệu để làm mẫu.");
}

// Add a practical reserve of always-ready blank input rows inside the Excel Table.
const reserveRows = 100;
table.rows.add(
  null,
  Array.from({ length: reserveRows }, () => [
    null,
    null,
    null,
    null,
    null,
    null,
    null,
  ]),
);
const newRow = oldLastRow + 1;
const lastReadyRow = oldLastRow + reserveRows;

// Keep the existing visual language on every ready-to-enter row.
const ready = sheet.getRange(`A${newRow}:G${lastReadyRow}`);
ready.format = {
  fill: "#FFFFFF",
  font: { typeface: "Calibri", fontSize: 10, color: "#1F2937" },
  borders: { preset: "all", style: "thin", color: "#D9E2F3" },
  verticalAlignment: "center",
  wrapText: true,
  rowHeight: 24,
};
sheet.getRange(`A${newRow}:A${lastReadyRow}`).format.horizontalAlignment = "center";
sheet.getRange(`B${newRow}:B${lastReadyRow}`).format.horizontalAlignment = "left";
sheet.getRange(`C${newRow}:C${lastReadyRow}`).format = {
  fill: "#F8FBFF",
  font: { typeface: "Calibri", fontSize: 10, bold: true, color: "#0B2545" },
  horizontalAlignment: "right",
  verticalAlignment: "center",
  wrapText: true,
  numberFormat: "#,##0;[Red](#,##0);-",
};
sheet
  .getRange(`D${newRow}:E${lastReadyRow}`)
  .format.horizontalAlignment = "center";
sheet.getRange(`F${newRow}:F${lastReadyRow}`).format.horizontalAlignment = "left";
sheet
  .getRange(`G${newRow}:G${lastReadyRow}`)
  .format.horizontalAlignment = "center";

// Standardize date/amount display for both existing values and future table rows.
sheet.getRange(`A8:A${lastReadyRow}`).format.numberFormat =
  "dd-mm-yyyy hh:mm:ss";
sheet.getRange(`C8:C${lastReadyRow}`).format.numberFormat =
  "#,##0;[Red](#,##0);-";
sheet.getRange(`G8:G${lastReadyRow}`).format.numberFormat =
  "dd-mm-yyyy hh:mm:ss";

// Make totals continue to include future entries without relying on manual formula edits.
sheet.getRange("C4").formulas = [["=SUM(C8:C1000)"]];
sheet.getRange("G4").formulas = [["=COUNTA(A8:A1000)"]];

const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);
console.log(`SAVED ${outputPath}`);

// Re-open the exact exported file for compact verification.
const verifyBlob = await FileBlob.load(outputPath);
const verifyBook = await SpreadsheetFile.importXlsx(verifyBlob);
const verifySheet = verifyBook.worksheets.getItem("Chi phi nang ha");
const verifyTable = verifySheet.tables.items[0];

const keyCheck = await verifyBook.inspect({
  kind: "table",
  sheetId: "Chi phi nang ha",
  range: `A37:G${newRow + 2}`,
  include: "values,formulas",
  maxChars: 5000,
  tableMaxRows: 10,
  tableMaxCols: 8,
});
console.log("KEY_CHECK");
console.log(keyCheck.ndjson);

const formulaCheck = await verifyBook.inspect({
  kind: "table",
  sheetId: "Chi phi nang ha",
  range: "C4:G4",
  include: "values,formulas",
  maxChars: 2000,
  tableMaxRows: 3,
  tableMaxCols: 6,
});
console.log("FORMULAS");
console.log(formulaCheck.ndjson);

const errors = await verifyBook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
  options: { useRegex: true, maxResults: 100 },
  summary: "final formula error scan",
});
console.log("ERROR_SCAN");
console.log(errors.ndjson);

for (let i = 0; i < 2; i += 1) {
  const renderSheet = verifyBook.worksheets.getItemAt(i);
  const preview = await verifyBook.render({
    sheetName: renderSheet.name,
    autoCrop: "all",
    scale: 1.5,
    format: "png",
  });
  const safeName = renderSheet.name.replace(/[<>:"/\\|?*]/g, "_");
  await fs.writeFile(
    path.join(previewDir, `${i + 1}-${safeName}.png`),
    new Uint8Array(await preview.arrayBuffer()),
  );
}

// Simulate typing into a subsequent prepared row in memory only.
const testRow = newRow + 1;
verifySheet.getRange(`A${testRow}:G${testRow}`).values = [
  [
    new Date(Date.UTC(2026, 6, 30, 9, 15, 0)),
    "DÒNG KIỂM TRA TỰ ĐỘNG ĐỊNH DẠNG",
    1234567,
    "TEST-ROW",
    "TEST-ACCOUNT",
    "DÒNG KIỂM TRA",
    new Date(Date.UTC(2026, 6, 30, 9, 15, 0)),
  ],
];
const testStyles = await verifyBook.inspect({
  kind: "computedStyle",
  sheetId: "Chi phi nang ha",
  range: `A${newRow}:G${testRow}`,
  maxChars: 6000,
});
console.log("SIMULATED_TYPING_STYLES");
console.log(testStyles.ndjson);

console.log(`READY_ROW ${newRow}`);
console.log(`LAST_READY_ROW ${lastReadyRow}`);
console.log(`TEST_ROW ${testRow}`);
