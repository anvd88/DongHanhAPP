import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const outputDir = "C:/Users/Admin/Desktop/KetoanMiniDotNet_Code_20260615_155926/web/.codex-work/sosanh";
const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load("C:/Users/Admin/Desktop/sosanh.xlsx"));
const samples = [
  ["N", "C6:H45", "preview-N-top.png"],
  ["N", "C741:H780", "preview-N-bottom.png"],
  ["me", "B5:H44", "preview-me-top.png"],
  ["me", "B743:H782", "preview-me-bottom.png"],
];
for (const [sheetName, range, fileName] of samples) {
  const image = await workbook.render({ sheetName, range, scale: 1, format: "png" });
  await fs.writeFile(`${outputDir}/${fileName}`, new Uint8Array(await image.arrayBuffer()));
}
console.log(samples.map(x => `${x[0]} ${x[1]} -> ${x[2]}`).join("\n"));
