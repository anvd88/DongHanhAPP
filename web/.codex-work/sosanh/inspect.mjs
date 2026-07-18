import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const inputPath = "C:/Users/Admin/Desktop/sosanh.xlsx";
const outputDir = "C:/Users/Admin/Desktop/KetoanMiniDotNet_Code_20260615_155926/web/.codex-work/sosanh";

const input = await FileBlob.load(inputPath);
const workbook = await SpreadsheetFile.importXlsx(input);

const overview = await workbook.inspect({
  kind: "workbook,sheet,table",
  maxChars: 12000,
  tableMaxRows: 12,
  tableMaxCols: 16,
  tableMaxCellChars: 120,
});
console.log("OVERVIEW");
console.log(overview.ndjson);

for (const sheetName of ["N", "me"]) {
  try {
    const sheet = workbook.worksheets.getItem(sheetName);
    const used = sheet.getUsedRange();
    console.log(`USED ${sheetName}`);
    console.log(JSON.stringify({ address: used.address, values: used.values }));

    const styles = await workbook.inspect({
      kind: "computedStyle",
      sheetId: sheetName,
      range: used.address,
      maxChars: 3500,
    });
    console.log(`STYLES ${sheetName}`);
    console.log(styles.ndjson);

    const ranges = sheetName === "N" ? ["C6:H45", "C741:H780"] : ["B5:H44", "B743:H782"];
    for (let i = 0; i < ranges.length; i++) {
      const preview = await workbook.render({
        sheetName,
        range: ranges[i],
        scale: 1,
        format: "png",
      });
      await fs.writeFile(`${outputDir}/preview-${sheetName}-${i + 1}.png`, new Uint8Array(await preview.arrayBuffer()));
    }
  } catch (error) {
    console.log(`ERROR ${sheetName}`);
    console.log(error?.stack ?? String(error));
  }
}
