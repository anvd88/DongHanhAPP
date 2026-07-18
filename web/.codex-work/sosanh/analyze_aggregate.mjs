import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const inputPath = "C:/Users/Admin/Desktop/sosanh.xlsx";
const outputPath = "C:/Users/Admin/Desktop/KetoanMiniDotNet_Code_20260615_155926/web/.codex-work/sosanh/aggregate_analysis.json";
const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(inputPath));

const cleanVoucher = value => {
  if (value === null || value === undefined || String(value).trim() === "") return null;
  const text = String(value).trim();
  return /^-?\d+(\.0+)?$/.test(text) ? String(Number(text)) : text;
};
const cleanNumber = value => {
  if (value === null || value === undefined || value === "") return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
};
const cleanText = value => String(value ?? "").trim().replace(/\s+/g, " ");
const comparablePrice = value => {
  const n = cleanNumber(value);
  return n === null || Math.abs(n) < 1e-9 ? 0 : Math.abs(n);
};
const round6 = n => Math.round((n + Number.EPSILON) * 1e6) / 1e6;
const numKey = n => round6(n).toFixed(6);
const formatDate = serial => {
  if (!Number.isFinite(serial)) return String(serial ?? "");
  const d = new Date(Date.UTC(1899, 11, 30) + Math.round(serial) * 86400000);
  return `${String(d.getUTCDate()).padStart(2, "0")}/${String(d.getUTCMonth() + 1).padStart(2, "0")}/${d.getUTCFullYear()}`;
};

function extract(sheetName) {
  if (sheetName === "N") {
    const output = [];
    let currentVoucher = null;
    let currentDate = null;
    const source = workbook.worksheets.getItem("N").getRange("C7:H780").values;
    for (let i = 0; i < source.length; i++) {
      const r = source[i];
      const explicitVoucher = cleanVoucher(r[0]);
      const explicitDate = cleanNumber(r[1]);
      const type = cleanText(r[2]);
      const description = cleanText(r[3]);
      const quantity = cleanNumber(r[4]);
      const price = cleanNumber(r[5]);
      const isMarker = /^(tt\s*gc|chi\s*tra)$/i.test(description);

      if (explicitVoucher !== null) {
        currentVoucher = explicitVoucher;
        if (explicitDate !== null) currentDate = explicitDate;
      } else if (explicitDate !== null && explicitDate !== currentDate) {
        // A blank-voucher row that starts a new date is a separator, not a continuation.
        currentVoucher = null;
        currentDate = explicitDate;
      }

      const hasDetail = Boolean(type || description || quantity !== null || price !== null);
      const voucher = explicitVoucher ?? (!isMarker && hasDetail ? currentVoucher : null);
      const dateSerial = explicitDate ?? (!isMarker && hasDetail ? currentDate : null);
      if (voucher === null || dateSerial === null || isMarker) continue;
      output.push({
        sheet: "N", row: i + 7, voucher, dateSerial, date: formatDate(dateSerial),
        type, description, quantity, price, priceCmp: comparablePrice(price),
      });
    }
    return output;
  }
  return workbook.worksheets.getItem("me").getRange("B6:H782").values.map((r, i) => ({
    sheet: "me", row: i + 6, voucher: cleanVoucher(r[0]), dateSerial: cleanNumber(r[1]), date: formatDate(cleanNumber(r[1])),
    type: cleanText(r[2]), description: cleanText([r[3], r[4]].filter(v => cleanText(v)).join(" ")), quantity: cleanNumber(r[5]), price: cleanNumber(r[6]), priceCmp: comparablePrice(r[6]),
  })).filter(r => r.voucher !== null && r.voucher !== "0" && r.dateSerial !== null);
}

function groupRows(rows) {
  const groups = new Map();
  for (const row of rows) {
    const key = `${row.voucher}|${row.dateSerial}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(row);
  }
  return groups;
}

function aggregate(rows) {
  const bands = new Map();
  for (const row of rows) {
    const key = numKey(row.priceCmp);
    if (!bands.has(key)) bands.set(key, { price: row.priceCmp, quantity: 0, rows: [] });
    const band = bands.get(key);
    band.quantity = round6(band.quantity + (row.quantity ?? 0));
    band.rows.push(row.row);
  }
  const list = [...bands.values()].sort((a, b) => a.price - b.price);
  return {
    voucher: rows[0].voucher,
    dateSerial: rows[0].dateSerial,
    date: rows[0].date,
    bands: list,
    signature: list.map(b => `${numKey(b.price)}:${numKey(b.quantity)}`).join("|"),
    totalQuantity: round6(list.reduce((s, b) => s + b.quantity, 0)),
    totalValueAbs: round6(list.reduce((s, b) => s + b.quantity * b.price, 0)),
    rows,
  };
}

function summarizeGroup(group) {
  return {
    voucher: group.voucher,
    date: group.date,
    totalQuantity: group.totalQuantity,
    totalValueAbs: group.totalValueAbs,
    priceBands: group.bands.map(b => ({ price: b.price, quantity: b.quantity, rows: b.rows })),
  };
}

function compareBands(n, me) {
  const prices = [...new Set([...n.bands.map(b => numKey(b.price)), ...me.bands.map(b => numKey(b.price))])]
    .map(Number).sort((a, b) => a - b);
  const nMap = new Map(n.bands.map(b => [numKey(b.price), b]));
  const mMap = new Map(me.bands.map(b => [numKey(b.price), b]));
  return prices.map(price => {
    const nb = nMap.get(numKey(price));
    const mb = mMap.get(numKey(price));
    return {
      price,
      nQuantity: nb?.quantity ?? 0,
      meQuantity: mb?.quantity ?? 0,
      differenceMeMinusN: round6((mb?.quantity ?? 0) - (nb?.quantity ?? 0)),
      nRows: nb?.rows ?? [],
      meRows: mb?.rows ?? [],
    };
  }).filter(x => Math.abs(x.differenceMeMinusN) > 1e-6);
}

const nRows = extract("N");
const minDate = Math.min(...nRows.map(r => r.dateSerial));
const maxDate = Math.max(...nRows.map(r => r.dateSerial));
const meRowsAll = extract("me");
const meRows = meRowsAll.filter(r => r.dateSerial >= minDate && r.dateSerial <= maxDate);

const nGroups = new Map([...groupRows(nRows)].map(([k, rows]) => [k, aggregate(rows)]));
const meGroups = new Map([...groupRows(meRows)].map(([k, rows]) => [k, aggregate(rows)]));

const sharedKeys = [...nGroups.keys()].filter(k => meGroups.has(k));
const quantityPriceMismatches = [];
let exactGroups = 0;
for (const key of sharedKeys) {
  const n = nGroups.get(key);
  const me = meGroups.get(key);
  if (n.signature === me.signature) {
    exactGroups++;
  } else {
    const bandDifferences = compareBands(n, me);
    if (!bandDifferences.length) {
      exactGroups++;
      continue;
    }
    quantityPriceMismatches.push({
      voucher: n.voucher,
      date: n.date,
      totalQuantityN: n.totalQuantity,
      totalQuantityMe: me.totalQuantity,
      totalValueAbsN: n.totalValueAbs,
      totalValueAbsMe: me.totalValueAbs,
      bandDifferences,
      nRows: n.rows.map(r => r.row),
      meRows: me.rows.map(r => r.row),
    });
  }
}

let nOnly = [...nGroups.entries()].filter(([k]) => !meGroups.has(k)).map(([key, group]) => ({ key, group }));
let meOnly = [...meGroups.entries()].filter(([k]) => !nGroups.has(k)).map(([key, group]) => ({ key, group }));
const pairedN = new Set();
const pairedMe = new Set();
const dateMismatches = [];
const voucherMismatches = [];
const dateAndValueMismatches = [];

function pairUnique(nPredicate, mPredicate, consumer) {
  for (const nItem of nOnly) {
    if (pairedN.has(nItem.key) || !nPredicate(nItem.group)) continue;
    const candidates = meOnly.filter(mItem => !pairedMe.has(mItem.key) && mPredicate(nItem.group, mItem.group));
    if (candidates.length === 1) {
      const mItem = candidates[0];
      consumer(nItem.group, mItem.group);
      pairedN.add(nItem.key);
      pairedMe.add(mItem.key);
    }
  }
}

pairUnique(
  () => true,
  (n, me) => n.voucher === me.voucher && n.signature === me.signature,
  (n, me) => dateMismatches.push({ voucher: n.voucher, nDate: n.date, meDate: me.date, nRows: n.rows.map(r => r.row), meRows: me.rows.map(r => r.row), totalQuantity: n.totalQuantity, priceBands: n.bands.map(b => ({ price: b.price, quantity: b.quantity })) }),
);
pairUnique(
  () => true,
  (n, me) => n.dateSerial === me.dateSerial && n.signature === me.signature,
  (n, me) => voucherMismatches.push({ date: n.date, nVoucher: n.voucher, meVoucher: me.voucher, nRows: n.rows.map(r => r.row), meRows: me.rows.map(r => r.row), totalQuantity: n.totalQuantity, priceBands: n.bands.map(b => ({ price: b.price, quantity: b.quantity })) }),
);

// If a voucher appears only once on each unmatched side, pair it even when both date and values differ.
for (const nItem of nOnly) {
  if (pairedN.has(nItem.key)) continue;
  const nSameVoucher = nOnly.filter(x => !pairedN.has(x.key) && x.group.voucher === nItem.group.voucher);
  const mSameVoucher = meOnly.filter(x => !pairedMe.has(x.key) && x.group.voucher === nItem.group.voucher);
  if (nSameVoucher.length === 1 && mSameVoucher.length === 1) {
    const meItem = mSameVoucher[0];
    dateAndValueMismatches.push({
      voucher: nItem.group.voucher,
      nDate: nItem.group.date,
      meDate: meItem.group.date,
      n: summarizeGroup(nItem.group),
      me: summarizeGroup(meItem.group),
      bandDifferences: compareBands(nItem.group, meItem.group),
    });
    pairedN.add(nItem.key);
    pairedMe.add(meItem.key);
  }
}

nOnly = nOnly.filter(x => !pairedN.has(x.key));
meOnly = meOnly.filter(x => !pairedMe.has(x.key));

const report = {
  comparisonPeriod: { minDate: formatDate(minDate), maxDate: formatDate(maxDate) },
  counts: {
    nRows: nRows.length,
    meRowsInPeriod: meRows.length,
    meRowsAfterNPeriodIgnored: meRowsAll.filter(r => r.dateSerial > maxDate).length,
    nGroups: nGroups.size,
    meGroupsInPeriod: meGroups.size,
    exactGroups,
    quantityPriceMismatchGroups: quantityPriceMismatches.length,
    dateMismatchGroups: dateMismatches.length,
    voucherMismatchGroups: voucherMismatches.length,
    dateAndValueMismatchGroups: dateAndValueMismatches.length,
    missingGroupsInMe: nOnly.length,
    extraGroupsInMe: meOnly.length,
  },
  quantityPriceMismatches,
  dateMismatches,
  voucherMismatches,
  dateAndValueMismatches,
  missingGroupsInMe: nOnly.map(x => summarizeGroup(x.group)),
  extraGroupsInMe: meOnly.map(x => summarizeGroup(x.group)),
};

await fs.writeFile(outputPath, JSON.stringify(report, null, 2), "utf8");
console.log(JSON.stringify(report, null, 2));
