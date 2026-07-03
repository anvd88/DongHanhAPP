import type { DocumentListItem } from "./types";

export type DocumentKind = "receipt" | "payment" | "document";

const meta: Record<DocumentKind, { label: string; prefix: string }> = {
  receipt: { label: "Phiếu thu", prefix: "PT" },
  payment: { label: "Phiếu chi", prefix: "PC" },
  document: { label: "Phiếu xuất kho bán hàng", prefix: "XK" },
};

export const documentKindLabel = (kind: DocumentKind) => meta[kind].label;

export const documentKindPrefix = (kind: DocumentKind) => meta[kind].prefix;

export const defaultDocumentContent = (kind: DocumentKind) =>
  kind === "receipt" ? "Phiếu thu" : kind === "payment" ? "Phiếu chi" : "Phiếu xuất kho bán hàng";

export function inferDocumentKind(doc: { voucherNo?: string; content?: string; documentType?: string }): DocumentKind {
  const voucher = (doc.voucherNo ?? "").trim().toUpperCase();
  const type = (doc.documentType ?? "").trim().toLowerCase();
  const content = ` ${(doc.content ?? "").trim().toLowerCase()} `;

  if (voucher.startsWith("PT") || type.includes("thu") || content.includes(" phiếu thu ") || content.includes(" thu tiền ")) {
    return "receipt";
  }

  if (voucher.startsWith("PC") || type.includes("chi") || content.includes(" phiếu chi ") || content.includes(" chi tiền ")) {
    return "payment";
  }

  return "document";
}

export const documentTypeText = (doc: Pick<DocumentListItem, "voucherNo" | "content" | "documentType">) => {
  const kind = inferDocumentKind(doc);
  return documentKindLabel(kind);
};

let lastVoucherKey = "";
let sameMinuteVoucherCount = 0;

export function createVoucherNo(kind: DocumentKind) {
  const now = new Date();
  const yy = String(now.getFullYear()).slice(-2);
  const mm = String(now.getMonth() + 1).padStart(2, "0");
  const dd = String(now.getDate()).padStart(2, "0");
  const hh = String(now.getHours()).padStart(2, "0");
  const mi = String(now.getMinutes()).padStart(2, "0");
  const key = `${kind}:${yy}${mm}${dd}-${hh}${mi}`;

  if (key === lastVoucherKey) {
    sameMinuteVoucherCount += 1;
  } else {
    lastVoucherKey = key;
    sameMinuteVoucherCount = 1;
  }

  const suffix = sameMinuteVoucherCount > 1 ? `-${String(sameMinuteVoucherCount).padStart(2, "0")}` : "";
  return `${documentKindPrefix(kind)}${yy}${mm}${dd}-${hh}${mi}${suffix}`;
}
