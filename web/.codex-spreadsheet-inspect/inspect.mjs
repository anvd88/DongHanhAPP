import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const inputPath = "C:\\Users\\Admin\\Desktop\\Chi phi nang ha 2026.xlsx";
const outDir = "C:\\Users\\Admin\\Desktop\\KetoanMiniDotNet_Code_20260615_155926\\web\\.codex-spreadsheet-inspect\\renders";
await fs.mkdir(outDir, { recursive: true });

const input = await FileBlob.load(inputPath);
const workbook = await SpreadsheetFile.importXlsx(input);

const summary = await workbook.inspect({
  kind: "workbook,sheet,table,definedName,drawing",
  maxChars: 12000,
  tableMaxRows: 8,
  tableMaxCols: 12,
  tableMaxCellChars: 100,
});
console.log("SUMMARY");
console.log(summary.ndjson);

for (let i = 0; ; i += 1) {
  let sheet;
  try {
    sheet = workbook.worksheets.getItemAt(i);
  } catch {
    break;
  }
  if (!sheet) break;
  const used = sheet.getUsedRange();
  console.log(`SHEET ${i}: ${sheet.name}`);
  if (!used) {
    console.log("EMPTY");
    continue;
  }
  console.log(`USED ${used.address}`);
  const region = await workbook.inspect({
    kind: "region",
    sheetId: sheet.name,
    range: used.address,
    maxChars: 9000,
    tableMaxRows: 30,
    tableMaxCols: 20,
    tableMaxCellChars: 120,
  });
  console.log(region.ndjson);

  const style = await workbook.inspect({
    kind: "computedStyle",
    sheetId: sheet.name,
    range: used.address,
    maxChars: 10000,
  });
  console.log("STYLES");
  console.log(style.ndjson);

  const safeName = sheet.name.replace(/[<>:"/\\|?*]/g, "_");
  const preview = await workbook.render({
    sheetName: sheet.name,
    autoCrop: "all",
    scale: 1.5,
    format: "png",
  });
  await fs.writeFile(
    path.join(outDir, `${String(i + 1).padStart(2, "0")}-${safeName}.png`),
    new Uint8Array(await preview.arrayBuffer()),
  );
}
