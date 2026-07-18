import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const inputPath = "C:/Users/Admin/Desktop/sosanh.xlsx";
const outputPath = "C:/Users/Admin/Desktop/KetoanMiniDotNet_Code_20260615_155926/web/.codex-work/sosanh/analysis.json";

const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(inputPath));

function cleanVoucher(value) {
  if (value === null || value === undefined || String(value).trim() === "") return null;
  const text = String(value).trim();
  return /^-?\d+(\.0+)?$/.test(text) ? String(Number(text)) : text;
}

function cleanNumber(value) {
  if (value === null || value === undefined || value === "") return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function cleanText(value) {
  return String(value ?? "").trim().replace(/\s+/g, " ");
}

function normalizeText(value) {
  return cleanText(value)
    .toLocaleLowerCase("vi")
    .replaceAll(",", ".")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/đ/g, "d")
    .replace(/[^a-z0-9.]+/g, "");
}

function formatDate(serial) {
  if (!Number.isFinite(serial)) return String(serial ?? "");
  const epoch = Date.UTC(1899, 11, 30);
  const date = new Date(epoch + Math.round(serial) * 86400000);
  return `${String(date.getUTCDate()).padStart(2, "0")}/${String(date.getUTCMonth() + 1).padStart(2, "0")}/${date.getUTCFullYear()}`;
}

function priceMagnitude(value) {
  const n = cleanNumber(value);
  return n === null ? null : Math.abs(n);
}

function numKey(value) {
  return value === null ? "∅" : Number(value).toFixed(6);
}

function lineSignature(row) {
  return `${numKey(row.quantity)}|${numKey(row.priceAbs)}`;
}

function groupSignature(rows) {
  return rows.map(lineSignature).sort().join(";");
}

function extractN() {
  const values = workbook.worksheets.getItem("N").getRange("C7:H780").values;
  return values.map((r, i) => ({
    sheet: "N",
    row: i + 7,
    voucher: cleanVoucher(r[0]),
    dateSerial: cleanNumber(r[1]),
    date: formatDate(cleanNumber(r[1])),
    type: cleanText(r[2]),
    description: cleanText(r[3]),
    descriptionNorm: normalizeText(r[3]),
    quantity: cleanNumber(r[4]),
    price: cleanNumber(r[5]),
    priceAbs: priceMagnitude(r[5]),
  })).filter(r => r.voucher !== null);
}

function extractMe() {
  const values = workbook.worksheets.getItem("me").getRange("B6:H782").values;
  return values.map((r, i) => ({
    sheet: "me",
    row: i + 6,
    voucher: cleanVoucher(r[0]),
    dateSerial: cleanNumber(r[1]),
    date: formatDate(cleanNumber(r[1])),
    type: cleanText(r[2]),
    description: cleanText([r[3], r[4]].filter(v => cleanText(v)).join(" ")),
    descriptionNorm: normalizeText([r[3], r[4]].filter(v => cleanText(v)).join(" ")),
    quantity: cleanNumber(r[5]),
    price: cleanNumber(r[6]),
    priceAbs: priceMagnitude(r[6]),
  })).filter(r => r.voucher !== null);
}

function groupRows(rows) {
  const map = new Map();
  for (const row of rows) {
    const key = `${row.voucher}|${row.dateSerial}`;
    if (!map.has(key)) map.set(key, []);
    map.get(key).push(row);
  }
  return map;
}

function removeExactPairs(nRows, mRows) {
  const nRemain = [...nRows];
  const mRemain = [...mRows];
  for (let i = nRemain.length - 1; i >= 0; i--) {
    const sig = lineSignature(nRemain[i]);
    const j = mRemain.findIndex(r => lineSignature(r) === sig);
    if (j >= 0) {
      nRemain.splice(i, 1);
      mRemain.splice(j, 1);
    }
  }
  return { nRemain, mRemain };
}

function pairRemainders(nRows, mRows) {
  const nRemain = [...nRows];
  const mRemain = [...mRows];
  const pairs = [];

  const takePairs = (predicate, reason) => {
    for (let i = nRemain.length - 1; i >= 0; i--) {
      const j = mRemain.findIndex(m => predicate(nRemain[i], m));
      if (j >= 0) {
        pairs.push({ n: nRemain[i], me: mRemain[j], reason });
        nRemain.splice(i, 1);
        mRemain.splice(j, 1);
      }
    }
  };

  takePairs((n, m) => n.descriptionNorm && n.descriptionNorm === m.descriptionNorm, "cùng mô tả");
  takePairs((n, m) => numKey(n.quantity) === numKey(m.quantity), "cùng số lượng");
  takePairs((n, m) => numKey(n.priceAbs) === numKey(m.priceAbs), "cùng trị tuyệt đối đơn giá");
  while (nRemain.length && mRemain.length) {
    pairs.push({ n: nRemain.shift(), me: mRemain.shift(), reason: "cùng vị trí còn lại trong phiếu" });
  }
  return { pairs, missingInMe: nRemain, extraInMe: mRemain };
}

const nRows = extractN();
const meRows = extractMe();
const nGroups = groupRows(nRows);
const meGroups = groupRows(meRows);

const sharedKeys = [...nGroups.keys()].filter(k => meGroups.has(k));
let nOnlyKeys = [...nGroups.keys()].filter(k => !meGroups.has(k));
let meOnlyKeys = [...meGroups.keys()].filter(k => !nGroups.has(k));

const dateMismatches = [];
const consumedN = new Set();
const consumedMe = new Set();
for (const nk of nOnlyKeys) {
  const nr = nGroups.get(nk);
  const candidates = meOnlyKeys.filter(mk => {
    if (consumedMe.has(mk)) return false;
    const mr = meGroups.get(mk);
    return nr[0].voucher === mr[0].voucher && groupSignature(nr) === groupSignature(mr);
  });
  if (candidates.length === 1) {
    const mk = candidates[0];
    const mr = meGroups.get(mk);
    dateMismatches.push({
      voucher: nr[0].voucher,
      nDate: nr[0].date,
      meDate: mr[0].date,
      nRows: nr.map(r => r.row),
      meRows: mr.map(r => r.row),
      lineCount: nr.length,
    });
    consumedN.add(nk);
    consumedMe.add(mk);
  }
}
nOnlyKeys = nOnlyKeys.filter(k => !consumedN.has(k));
meOnlyKeys = meOnlyKeys.filter(k => !consumedMe.has(k));

const valueMismatches = [];
for (const key of sharedKeys) {
  const nGroup = nGroups.get(key);
  const meGroup = meGroups.get(key);
  const { nRemain, mRemain } = removeExactPairs(nGroup, meGroup);
  if (!nRemain.length && !mRemain.length) continue;
  const details = pairRemainders(nRemain, mRemain);
  valueMismatches.push({
    voucher: nGroup[0].voucher,
    date: nGroup[0].date,
    nRows: nGroup.map(r => r.row),
    meRows: meGroup.map(r => r.row),
    pairs: details.pairs.map(({ n, me, reason }) => ({
      reason,
      n: { row: n.row, type: n.type, description: n.description, quantity: n.quantity, price: n.price },
      me: { row: me.row, type: me.type, description: me.description, quantity: me.quantity, price: me.price },
      quantityWrong: numKey(n.quantity) !== numKey(me.quantity),
      priceWrong: numKey(n.priceAbs) !== numKey(me.priceAbs),
      typeWrong: normalizeText(n.type) !== normalizeText(me.type),
    })),
    missingInMe: details.missingInMe.map(r => ({ row: r.row, type: r.type, description: r.description, quantity: r.quantity, price: r.price })),
    extraInMe: details.extraInMe.map(r => ({ row: r.row, type: r.type, description: r.description, quantity: r.quantity, price: r.price })),
  });
}

const missingGroupsInMe = nOnlyKeys.map(key => {
  const rows = nGroups.get(key);
  return { voucher: rows[0].voucher, date: rows[0].date, rows: rows.map(r => ({ row: r.row, type: r.type, description: r.description, quantity: r.quantity, price: r.price })) };
});
const extraGroupsInMe = meOnlyKeys.map(key => {
  const rows = meGroups.get(key);
  return { voucher: rows[0].voucher, date: rows[0].date, rows: rows.map(r => ({ row: r.row, type: r.type, description: r.description, quantity: r.quantity, price: r.price })) };
});

const report = {
  counts: {
    nRows: nRows.length,
    meRows: meRows.length,
    nGroups: nGroups.size,
    meGroups: meGroups.size,
    exactGroups: sharedKeys.length - valueMismatches.length,
    valueMismatchGroups: valueMismatches.length,
    dateMismatchGroups: dateMismatches.length,
    missingGroupsInMe: missingGroupsInMe.length,
    extraGroupsInMe: extraGroupsInMe.length,
  },
  dateMismatches,
  valueMismatches,
  missingGroupsInMe,
  extraGroupsInMe,
};

await fs.writeFile(outputPath, JSON.stringify(report, null, 2), "utf8");
console.log(JSON.stringify(report, null, 2));
