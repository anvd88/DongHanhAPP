import { useMemo, useState } from "react";
import { Building2, Pencil, Plus, Search } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Button, EmptyState, Field, Input, Spinner } from "../components/ui";
import { Modal } from "../components/Modal";
import { useApi } from "../lib/useApi";
import { useAccess, PERM } from "../lib/access";
import { useAppNotifications } from "../components/app-notifications-context";
import { api } from "../lib/api";
import { date as fmtDate, money } from "../lib/format";
import type { Supplier } from "../lib/types";

/**
 * NHÀ CUNG CẤP — vế đối xứng của Khách hàng, cho chiều MUA.
 *
 * Cột "Còn nợ" ở đây là tổng phiếu nhập chưa hủy trừ đi số đã trả ghi trên từng phiếu. Cố ý chưa
 * dựng sổ chi tiết thanh toán: con số tổng đã trả lời đúng câu hỏi hằng ngày ("còn nợ ai bao nhiêu"),
 * và khi nào cần sổ thì thêm bảng riêng mà không phải sửa lại chỗ này.
 */
export function NhaCungCap() {
  const access = useAccess();
  const canEdit = access.can(PERM.vouchersCreate);
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<Supplier | "new" | null>(null);

  const { data, loading, reload } = useApi<{ items: Supplier[] }>("/api/suppliers?includeInactive=true");

  const suppliers = useMemo(() => {
    const items = data?.items ?? [];
    const keyword = search.trim().toLowerCase();
    if (!keyword) return items;
    return items.filter((item) =>
      `${item.name} ${item.taxCode} ${item.phone}`.toLowerCase().includes(keyword),
    );
  }, [data, search]);

  const totalOwed = suppliers.reduce((sum, item) => sum + Math.max(item.balance, 0), 0);

  return (
    <div className="gc-root">
      <PageHeader
        title="Nhà cung cấp"
        subtitle="Bên bán hàng cho mình — nguồn của phiếu nhập mua"
        actions={
          canEdit && (
            <Button onClick={() => setEditing("new")}>
              <Plus className="h-4 w-4" /> Thêm nhà cung cấp
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
            placeholder="Tìm theo tên, mã số thuế, điện thoại…"
          />
        </div>
        {totalOwed > 0 && (
          <div className="rounded-xl bg-rose-500/10 px-3 py-2 text-sm font-bold text-rose-600 dark:text-rose-400">
            Còn nợ nhà cung cấp: {money(totalOwed)} ₫
          </div>
        )}
      </div>

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        {loading ? (
          <div className="flex justify-center py-16">
            <Spinner />
          </div>
        ) : suppliers.length === 0 ? (
          <div className="py-10">
            <EmptyState
              icon={<Building2 className="h-7 w-7" />}
              title="Chưa có nhà cung cấp nào"
              hint="Thêm nhà cung cấp rồi mới lập được phiếu nhập mua."
            />
          </div>
        ) : (
          <div className="gc-scroll max-h-[calc(100vh-300px)] overflow-auto">
            <table className="gc-table w-full">
              <thead>
                <tr>
                  <th>Nhà cung cấp</th>
                  <th>Mã số thuế</th>
                  <th>Điện thoại</th>
                  <th className="text-right">Số phiếu nhập</th>
                  <th className="text-right">Đã mua</th>
                  <th className="text-right">Đã trả</th>
                  <th className="text-right">Còn nợ</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {suppliers.map((item) => (
                  <tr key={item.id} className={item.isActive ? "" : "opacity-50"}>
                    <td className="font-semibold">
                      {item.name}
                      {!item.isActive && <span className="ml-2 text-xs font-bold">(ngừng dùng)</span>}
                      {item.lastPurchaseDate && (
                        <div className="text-xs text-[var(--gc-text-muted)]">
                          Nhập gần nhất {fmtDate(item.lastPurchaseDate)}
                        </div>
                      )}
                    </td>
                    <td>{item.taxCode}</td>
                    <td>{item.phone}</td>
                    <td className="text-right tabular-nums">{item.purchaseCount || "—"}</td>
                    <td className="whitespace-nowrap text-right tabular-nums">
                      {item.purchasedTotal > 0 ? `${money(item.purchasedTotal)} ₫` : "—"}
                    </td>
                    <td className="whitespace-nowrap text-right tabular-nums text-emerald-600 dark:text-emerald-400">
                      {item.paidTotal > 0 ? `${money(item.paidTotal)} ₫` : "—"}
                    </td>
                    <td
                      className={`whitespace-nowrap text-right font-black tabular-nums ${
                        item.balance > 0 ? "text-rose-600 dark:text-rose-400" : "text-[var(--gc-text-soft)]"
                      }`}
                    >
                      {item.balance !== 0 ? `${money(item.balance)} ₫` : "—"}
                    </td>
                    <td>
                      {canEdit && (
                        <button
                          type="button"
                          className="gc-icon-btn h-7 w-7"
                          aria-label={`Sửa ${item.name}`}
                          onClick={() => setEditing(item)}
                        >
                          <Pencil className="h-3.5 w-3.5" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </GlassPanel>

      {editing && (
        <SupplierEditor
          supplier={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            reload();
          }}
        />
      )}
    </div>
  );
}

function SupplierEditor({
  supplier,
  onClose,
  onSaved,
}: {
  supplier: Supplier | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const [name, setName] = useState(supplier?.name ?? "");
  const [taxCode, setTaxCode] = useState(supplier?.taxCode ?? "");
  const [phone, setPhone] = useState(supplier?.phone ?? "");
  const [address, setAddress] = useState(supplier?.address ?? "");
  const [note, setNote] = useState(supplier?.note ?? "");
  const [isActive, setIsActive] = useState(supplier?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const save = async () => {
    if (!name.trim()) {
      setError("Vui lòng nhập tên nhà cung cấp.");
      return;
    }
    setSaving(true);
    setError("");
    try {
      const body = {
        name: name.trim(),
        taxCode: taxCode.trim(),
        phone: phone.trim(),
        address: address.trim(),
        note: note.trim(),
        isActive,
      };
      if (supplier) await api.put(`/api/suppliers/${supplier.id}`, body);
      else await api.post("/api/suppliers", body);
      notify.success(supplier ? "Đã cập nhật nhà cung cấp." : "Đã thêm nhà cung cấp.", "Mua hàng");
      onSaved();
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : "Không lưu được nhà cung cấp.";
      setError(message);
      notify.error(message);
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      solid
      onClose={onClose}
      title={supplier ? `Sửa ${supplier.name}` : "Thêm nhà cung cấp"}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Đóng
          </Button>
          <Button loading={saving} onClick={() => void save()}>
            Lưu
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Tên nhà cung cấp *">
          <Input value={name} maxLength={256} onChange={(event) => setName(event.target.value)} />
        </Field>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Mã số thuế">
            <Input value={taxCode} maxLength={64} onChange={(event) => setTaxCode(event.target.value)} />
          </Field>
          <Field label="Điện thoại">
            <Input value={phone} maxLength={64} onChange={(event) => setPhone(event.target.value)} />
          </Field>
        </div>
        <Field label="Địa chỉ">
          <Input value={address} maxLength={500} onChange={(event) => setAddress(event.target.value)} />
        </Field>
        <Field label="Ghi chú">
          <Input value={note} maxLength={500} onChange={(event) => setNote(event.target.value)} />
        </Field>
        {supplier && (
          <label className="flex items-center gap-2 text-sm font-semibold">
            <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
            Còn giao dịch
          </label>
        )}
        {error && (
          <div className="rounded-xl bg-rose-500/10 p-3 text-sm font-semibold text-rose-600 dark:text-rose-400">
            {error}
          </div>
        )}
      </div>
    </Modal>
  );
}
