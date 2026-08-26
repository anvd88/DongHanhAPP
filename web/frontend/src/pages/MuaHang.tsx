import { useEffect, useMemo, useState } from "react";
import { Ban, PackagePlus, Pencil, Plus, Search, Trash2 } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Button, EmptyState, Field, Input, Select, Spinner } from "../components/ui";
import { Modal } from "../components/Modal";
import { DatePicker } from "../components/DateField";
import { ProductCellInput } from "../components/ProductCellInput";
import { CellInput, FormulaNumberInput } from "./DocumentEditor";
import { useApi } from "../lib/useApi";
import { useAccess, PERM } from "../lib/access";
import { useAppNotifications } from "../components/app-notifications-context";
import { api } from "../lib/api";
import { date as fmtDate, money, num } from "../lib/format";
import type { Purchase, PurchaseDetail, PurchaseLine, Supplier } from "../lib/types";

const emptyLine = (): PurchaseLine => ({ lineContent: "", spec: "", quantity: 1, unitPrice: 0, note: "" });

/**
 * MUA HÀNG — phiếu nhập mua từ nhà cung cấp.
 *
 * Đây là vế NHẬP mà hệ thống chưa từng có. Ngoài việc ghi được "hàng về kho ngày nào, của ai, giá
 * bao nhiêu", nó còn là điều kiện cần để sau này tính tồn kho và giá vốn — không có nhập thì không
 * có gì để trừ ra.
 *
 * Ô hàng hoá dùng chung bộ gợi ý với phiếu bán (danh mục hàng hoá), nên mua và bán nói cùng một thứ
 * tiếng — nếu mỗi bên gõ một kiểu thì tồn kho sau này không bao giờ cộng đúng.
 */
export function MuaHang() {
  const access = useAccess();
  const canCreate = access.can(PERM.vouchersCreate);
  const canCancel = access.can(PERM.vouchersCancel);
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<string | "new" | null>(null);
  const [cancelling, setCancelling] = useState<Purchase | null>(null);

  const { data, loading, reload } = useApi<{ items: Purchase[] }>("/api/purchases");

  const purchases = useMemo(() => {
    const items = data?.items ?? [];
    const keyword = search.trim().toLowerCase();
    if (!keyword) return items;
    return items.filter((item) =>
      `${item.voucherNo} ${item.supplierName} ${item.supplierInvoiceNo} ${item.note}`
        .toLowerCase()
        .includes(keyword),
    );
  }, [data, search]);

  const totals = useMemo(
    () =>
      purchases
        .filter((item) => !item.cancelledAt)
        .reduce(
          (acc, item) => ({ total: acc.total + item.total, remaining: acc.remaining + item.remaining }),
          { total: 0, remaining: 0 },
        ),
    [purchases],
  );

  return (
    <div className="gc-root">
      <PageHeader
        title="Mua hàng"
        subtitle="Phiếu nhập mua — hàng về kho từ nhà cung cấp"
        actions={
          canCreate && (
            <Button onClick={() => setEditing("new")}>
              <Plus className="h-4 w-4" /> Lập phiếu nhập
            </Button>
          )
        }
      />

      <div className="mb-3 flex flex-wrap items-center gap-3">
        <div className="flex min-w-52 flex-1 items-center gap-2">
          <Search className="h-4 w-4 shrink-0 opacity-60" />
          <Input
            className="w-full"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Tìm theo số phiếu, nhà cung cấp, số hoá đơn…"
          />
        </div>
        <div className="text-sm font-bold">
          Tổng nhập: {money(totals.total)} ₫
          {totals.remaining > 0 && (
            <span className="ml-3 text-rose-600 dark:text-rose-400">
              Còn nợ: {money(totals.remaining)} ₫
            </span>
          )}
        </div>
      </div>

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        {loading ? (
          <div className="flex justify-center py-16">
            <Spinner />
          </div>
        ) : purchases.length === 0 ? (
          <div className="py-10">
            <EmptyState
              icon={<PackagePlus className="h-7 w-7" />}
              title="Chưa có phiếu nhập nào"
              hint="Lập phiếu nhập mỗi lần hàng về để sau này tính được tồn kho và giá vốn."
            />
          </div>
        ) : (
          <div className="gc-scroll max-h-[calc(100vh-300px)] overflow-auto">
            <table className="gc-table w-full">
              <thead>
                <tr>
                  <th>Số phiếu</th>
                  <th>Ngày</th>
                  <th>Nhà cung cấp</th>
                  <th>Hoá đơn NCC</th>
                  <th className="text-right">Giá trị</th>
                  <th className="text-right">Đã trả</th>
                  <th className="text-right">Còn nợ</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {purchases.map((item) => (
                  <tr key={item.id} className={item.cancelledAt ? "opacity-50" : ""}>
                    <td className="whitespace-nowrap font-bold">
                      {item.voucherNo}
                      {item.cancelledAt && <div className="text-xs font-bold text-rose-500">Đã hủy</div>}
                    </td>
                    <td className="whitespace-nowrap">{fmtDate(item.docDate)}</td>
                    <td className="font-semibold">{item.supplierName}</td>
                    <td>{item.supplierInvoiceNo}</td>
                    <td className="whitespace-nowrap text-right font-bold tabular-nums">
                      {money(item.total)} ₫
                    </td>
                    <td className="whitespace-nowrap text-right tabular-nums text-emerald-600 dark:text-emerald-400">
                      {item.paidAmount > 0 ? `${money(item.paidAmount)} ₫` : "—"}
                    </td>
                    <td
                      className={`whitespace-nowrap text-right font-black tabular-nums ${
                        item.remaining > 0 && !item.cancelledAt
                          ? "text-rose-600 dark:text-rose-400"
                          : "text-[var(--gc-text-soft)]"
                      }`}
                    >
                      {item.cancelledAt ? "—" : item.remaining > 0 ? `${money(item.remaining)} ₫` : "Đã trả đủ"}
                    </td>
                    <td>
                      <div className="flex gap-1">
                        {canCreate && !item.cancelledAt && (
                          <button
                            type="button"
                            className="gc-icon-btn h-7 w-7"
                            aria-label={`Sửa ${item.voucherNo}`}
                            onClick={() => setEditing(item.id)}
                          >
                            <Pencil className="h-3.5 w-3.5" />
                          </button>
                        )}
                        {canCancel && !item.cancelledAt && (
                          <button
                            type="button"
                            className="gc-icon-btn h-7 w-7 text-rose-500"
                            aria-label={`Hủy ${item.voucherNo}`}
                            onClick={() => setCancelling(item)}
                          >
                            <Ban className="h-3.5 w-3.5" />
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </GlassPanel>

      {editing && (
        <PurchaseEditor
          id={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            reload();
          }}
        />
      )}

      {cancelling && (
        <CancelPurchaseModal
          purchase={cancelling}
          onClose={() => setCancelling(null)}
          onDone={() => {
            setCancelling(null);
            reload();
          }}
        />
      )}
    </div>
  );
}

function PurchaseEditor({
  id,
  onClose,
  onSaved,
}: {
  id: string | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const { data: supplierData } = useApi<{ items: Supplier[] }>("/api/suppliers");
  const [supplierId, setSupplierId] = useState("");
  const [docDate, setDocDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [invoiceNo, setInvoiceNo] = useState("");
  const [note, setNote] = useState("");
  const [paidAmount, setPaidAmount] = useState(0);
  const [lines, setLines] = useState<PurchaseLine[]>([emptyLine()]);
  const [loading, setLoading] = useState(!!id);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!id) return;
    let cancelled = false;
    void (async () => {
      try {
        const detail = await api.get<PurchaseDetail>(`/api/purchases/${id}`);
        if (cancelled) return;
        setSupplierId(detail.purchase.supplierId ?? "");
        setDocDate(detail.purchase.docDate.slice(0, 10));
        setInvoiceNo(detail.purchase.supplierInvoiceNo);
        setNote(detail.purchase.note);
        setPaidAmount(detail.purchase.paidAmount);
        setLines(detail.lines.length ? detail.lines : [emptyLine()]);
      } catch (cause) {
        if (!cancelled) setError(cause instanceof Error ? cause.message : "Không tải được phiếu nhập.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [id]);

  const suppliers = supplierData?.items ?? [];
  const setLine = (index: number, patch: Partial<PurchaseLine>) =>
    setLines((arr) => arr.map((line, i) => (i === index ? { ...line, ...patch } : line)));
  const total = lines.reduce((sum, line) => sum + (line.quantity || 0) * (line.unitPrice || 0), 0);

  const save = async () => {
    if (!supplierId) {
      setError("Vui lòng chọn nhà cung cấp.");
      return;
    }
    if (!lines.some((line) => line.lineContent.trim())) {
      setError("Phiếu nhập phải có ít nhất một dòng hàng.");
      return;
    }
    if (paidAmount > total) {
      setError("Số tiền đã trả không được lớn hơn giá trị phiếu nhập.");
      return;
    }
    setSaving(true);
    setError("");
    try {
      const body = {
        date: docDate,
        supplierId,
        supplierInvoiceNo: invoiceNo.trim(),
        note: note.trim(),
        paidAmount,
        lines: lines.filter((line) => line.lineContent.trim()),
      };
      if (id) await api.put(`/api/purchases/${id}`, body);
      else await api.post("/api/purchases", body);
      notify.success(id ? "Đã cập nhật phiếu nhập." : "Đã lập phiếu nhập.", "Mua hàng");
      onSaved();
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : "Không lưu được phiếu nhập.";
      setError(message);
      notify.error(message);
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      wide
      solid
      onClose={onClose}
      title={id ? "Sửa phiếu nhập mua" : "Lập phiếu nhập mua"}
      footer={
        <>
          <div className="mr-auto text-sm font-bold">
            Giá trị phiếu: <span className="text-lg font-black">{money(total)} ₫</span>
            {paidAmount > 0 && total - paidAmount > 0 && (
              <span className="ml-3 text-rose-600 dark:text-rose-400">
                còn nợ {money(total - paidAmount)} ₫
              </span>
            )}
          </div>
          <Button variant="ghost" onClick={onClose}>
            Đóng
          </Button>
          <Button loading={saving} onClick={() => void save()}>
            Lưu phiếu nhập
          </Button>
        </>
      }
    >
      {loading ? (
        <div className="flex justify-center py-12">
          <Spinner />
        </div>
      ) : (
        <div className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Nhà cung cấp *">
              <Select
                className="w-full"
                value={supplierId}
                onChange={(event) => setSupplierId(event.target.value)}
              >
                <option value="">— Chọn nhà cung cấp —</option>
                {suppliers.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Ngày nhập">
              <DatePicker value={docDate} onChange={setDocDate} />
            </Field>
            <Field label="Số hoá đơn / phiếu của NCC">
              <Input
                value={invoiceNo}
                maxLength={64}
                onChange={(event) => setInvoiceNo(event.target.value)}
                placeholder="Để đối chiếu khi họ đòi tiền"
              />
            </Field>
            <Field label="Đã trả nhà cung cấp">
              <FormulaNumberInput value={paidAmount} onChange={setPaidAmount} align="right" />
            </Field>
          </div>

          <div className="overflow-x-auto">
            <table className="gc-table w-full text-sm">
              <thead>
                <tr>
                  <th>Chủng loại hàng hoá</th>
                  <th>Quy cách</th>
                  <th className="text-right">Số lượng</th>
                  <th className="text-right">Đơn giá mua</th>
                  <th className="text-right">Thành tiền</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {lines.map((line, index) => (
                  <tr key={index}>
                    <td>
                      <ProductCellInput
                        value={line.lineContent}
                        onChange={(v) => setLine(index, { lineContent: v, productId: null })}
                        onPick={(p) => setLine(index, { lineContent: p.name, spec: p.spec, productId: p.id })}
                      />
                    </td>
                    <td>
                      <CellInput value={line.spec} onChange={(v) => setLine(index, { spec: v })} />
                    </td>
                    <td className="text-right">
                      <FormulaNumberInput
                        value={line.quantity}
                        align="right"
                        onChange={(v) => setLine(index, { quantity: v })}
                      />
                    </td>
                    <td className="text-right">
                      <FormulaNumberInput
                        value={line.unitPrice}
                        align="right"
                        onChange={(v) => setLine(index, { unitPrice: v })}
                      />
                    </td>
                    <td className="whitespace-nowrap text-right font-black tabular-nums">
                      {money((line.quantity || 0) * (line.unitPrice || 0))}
                    </td>
                    <td>
                      <button
                        type="button"
                        className="gc-icon-btn h-7 w-7 text-rose-500"
                        aria-label={`Xoá dòng ${index + 1}`}
                        onClick={() =>
                          setLines((arr) => (arr.length > 1 ? arr.filter((_, i) => i !== index) : arr))
                        }
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Button variant="ghost" onClick={() => setLines((arr) => [...arr, emptyLine()])}>
            <Plus className="h-4 w-4" /> Thêm dòng
          </Button>

          <Field label="Ghi chú">
            <Input value={note} maxLength={500} onChange={(event) => setNote(event.target.value)} />
          </Field>

          {error && (
            <div className="rounded-xl bg-rose-500/10 p-3 text-sm font-semibold text-rose-600 dark:text-rose-400">
              {error}
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}

function CancelPurchaseModal({
  purchase,
  onClose,
  onDone,
}: {
  purchase: Purchase;
  onClose: () => void;
  onDone: () => void;
}) {
  const { notify } = useAppNotifications();
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);

  return (
    <Modal
      open
      solid
      onClose={onClose}
      title={`Hủy phiếu nhập ${purchase.voucherNo}`}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Đóng
          </Button>
          <Button
            loading={saving}
            onClick={async () => {
              if (!reason.trim()) return notify.error("Vui lòng nhập lý do hủy.");
              setSaving(true);
              try {
                await api.put(`/api/purchases/${purchase.id}/cancel`, { reason: reason.trim() });
                notify.success("Đã hủy phiếu nhập.", "Mua hàng");
                onDone();
              } catch (cause) {
                notify.error(cause instanceof Error ? cause.message : "Không hủy được phiếu.");
              } finally {
                setSaving(false);
              }
            }}
          >
            Hủy phiếu
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-sm font-semibold text-[var(--gc-text-muted)]">
          {purchase.supplierName} · {num(purchase.total)} ₫. Phiếu vẫn nằm trong sổ với dấu đã hủy để
          còn đối chiếu.
        </p>
        <Field label="Lý do hủy *">
          <Input value={reason} maxLength={500} onChange={(event) => setReason(event.target.value)} />
        </Field>
      </div>
    </Modal>
  );
}
