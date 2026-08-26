import { useMemo, useState } from "react";
import { Check, PackagePlus, Pencil, Search, Sparkles, Tags } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Button, EmptyState, Field, Input, Spinner } from "../components/ui";
import { Modal } from "../components/Modal";
import { useApi } from "../lib/useApi";
import { useAccess, PERM } from "../lib/access";
import { useAppNotifications } from "../components/app-notifications-context";
import { api } from "../lib/api";
import { invalidateProductCatalog } from "../components/ProductCellInput";
import { date as fmtDate, money, num } from "../lib/format";
import type { Product, ProductSuggestion } from "../lib/types";

/**
 * DANH MỤC HÀNG HOÁ — tên chuẩn cho chủng loại + quy cách.
 *
 * Trang này giải quyết một vấn đề đã âm ỉ từ lâu: mọi dòng phiếu đều là chữ tự do, nên "Thép tấm
 * 10mm" và "thep tam 10 ly" là hai mặt hàng khác nhau với máy — không thống kê được, và tra hàng
 * khách trả về phải dò chữ.
 *
 * Chỗ quan trọng nhất là tab "Gợi ý từ phiếu cũ": danh mục dựng từ CHÍNH dữ liệu đã có, bấm vài cái
 * là xong. Bắt kế toán gõ lại vài trăm mặt hàng bằng tay thì danh mục sẽ chết yểu ngay tuần đầu.
 */
export function DanhMucHang() {
  const access = useAccess();
  const { notify } = useAppNotifications();
  const canEdit = access.can(PERM.vouchersCreate);

  const [tab, setTab] = useState<"catalog" | "suggestions">("catalog");
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<Product | "new" | null>(null);

  const { data, loading, reload } = useApi<{ items: Product[] }>("/api/products?includeInactive=true");
  const {
    data: suggestionData,
    loading: suggestionsLoading,
    reload: reloadSuggestions,
  } = useApi<{ items: ProductSuggestion[] }>("/api/products/suggestions");

  const products = useMemo(() => {
    const items = data?.items ?? [];
    const keyword = search.trim().toLowerCase();
    if (!keyword) return items;
    return items.filter((item) =>
      `${item.code} ${item.name} ${item.spec}`.toLowerCase().includes(keyword),
    );
  }, [data, search]);

  const suggestions = suggestionData?.items ?? [];

  return (
    <div className="gc-root">
      <PageHeader
        title="Danh mục hàng hoá"
        subtitle="Tên chuẩn cho chủng loại và quy cách — để thống kê theo mặt hàng và tra hàng trả về không lệ thuộc vào chính tả"
        actions={
          canEdit && (
            <Button onClick={() => setEditing("new")}>
              <PackagePlus className="h-4 w-4" /> Thêm mặt hàng
            </Button>
          )
        }
      />

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <TabButton active={tab === "catalog"} onClick={() => setTab("catalog")}>
          <Tags className="h-4 w-4" /> Danh mục ({data?.items.length ?? 0})
        </TabButton>
        <TabButton active={tab === "suggestions"} onClick={() => setTab("suggestions")}>
          <Sparkles className="h-4 w-4" /> Gợi ý từ phiếu cũ ({suggestions.length})
        </TabButton>
        {tab === "catalog" && (
          <div className="ml-auto flex min-w-52 items-center gap-2">
            <Search className="h-4 w-4 shrink-0 opacity-60" />
            <Input
              className="w-full"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Tìm theo mã, tên, quy cách…"
            />
          </div>
        )}
      </div>

      {tab === "catalog" ? (
        <GlassPanel strong className="overflow-hidden rounded-[20px]">
          {loading ? (
            <div className="flex justify-center py-16">
              <Spinner />
            </div>
          ) : products.length === 0 ? (
            <div className="py-10">
              <EmptyState
                icon={<Tags className="h-7 w-7" />}
                title="Danh mục còn trống"
                hint="Mở tab “Gợi ý từ phiếu cũ” để dựng danh mục từ chính những mặt hàng đã bán."
              />
            </div>
          ) : (
            <div className="gc-scroll max-h-[calc(100vh-320px)] overflow-auto">
              <table className="gc-table w-full">
                <thead>
                  <tr>
                    <th>Mã</th>
                    <th>Tên hàng hoá</th>
                    <th>Quy cách</th>
                    <th>ĐVT</th>
                    <th className="text-right">Đã bán</th>
                    <th className="text-right">Doanh số</th>
                    <th className="text-right">Giá mua gần nhất</th>
                    <th className="text-right">Giá bán gần nhất</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {products.map((item) => (
                    <tr key={item.id} className={item.isActive ? "" : "opacity-50"}>
                      <td className="whitespace-nowrap font-bold">{item.code}</td>
                      <td className="font-semibold">
                        {item.name}
                        {!item.isActive && <span className="ml-2 text-xs font-bold">(ngừng dùng)</span>}
                      </td>
                      <td>{item.spec}</td>
                      <td>{item.unit}</td>
                      <td className="whitespace-nowrap text-right tabular-nums">
                        {item.timesUsed > 0 ? `${num(item.soldQuantity)} · ${item.timesUsed} lần` : "—"}
                      </td>
                      <td className="whitespace-nowrap text-right tabular-nums">
                        {item.soldAmount > 0 ? `${money(item.soldAmount)} ₫` : "—"}
                      </td>
                      <td className="whitespace-nowrap text-right tabular-nums">
                        {item.lastCost != null ? (
                          <>
                            {money(item.lastCost)} ₫
                            {item.lastBoughtDate && (
                              <div className="text-xs font-semibold opacity-60">{fmtDate(item.lastBoughtDate)}</div>
                            )}
                          </>
                        ) : (
                          "—"
                        )}
                      </td>
                      <td className="whitespace-nowrap text-right tabular-nums font-bold">
                        {item.lastPrice != null ? (
                          <>
                            {money(item.lastPrice)} ₫
                            {item.lastSoldDate && (
                              <div className="text-xs font-semibold opacity-60">{fmtDate(item.lastSoldDate)}</div>
                            )}
                          </>
                        ) : (
                          "—"
                        )}
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
      ) : (
        <SuggestionPanel
          items={suggestions}
          loading={suggestionsLoading}
          canEdit={canEdit}
          onImported={(added, linked) => {
            notify.success(
              `Đã thêm ${added} mặt hàng vào danh mục` +
                (linked > 0 ? `, gắn mã cho ${linked} dòng phiếu cũ.` : "."),
              "Danh mục hàng hoá",
            );
            invalidateProductCatalog();
            reload();
            reloadSuggestions();
          }}
        />
      )}

      {editing && (
        <ProductEditor
          product={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            invalidateProductCatalog();
            reload();
            reloadSuggestions();
          }}
        />
      )}
    </div>
  );
}

function TabButton({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex items-center gap-2 rounded-xl border px-3 py-2 text-sm font-bold transition ${
        active
          ? "border-[var(--gc-accent)] bg-[var(--gc-accent)]/10 text-[var(--gc-accent)]"
          : "border-[var(--gc-border)] text-[var(--gc-text-muted)] hover:border-[var(--gc-accent)]/50"
      }`}
    >
      {children}
    </button>
  );
}

/**
 * Dựng danh mục từ dữ liệu cũ. Mặc định TÍCH SẴN những mặt hàng hay dùng (từ 3 lần trở lên) — đó là
 * phần lõi mà kế toán chắc chắn muốn, chọn thủ công 300 dòng là không ai làm.
 */
function SuggestionPanel({
  items,
  loading,
  canEdit,
  onImported,
}: {
  items: ProductSuggestion[];
  loading: boolean;
  canEdit: boolean;
  onImported: (added: number, linked: number) => void;
}) {
  const { notify } = useAppNotifications();
  const keyOf = (item: ProductSuggestion) => `${item.name}###${item.spec}`;
  const [picked, setPicked] = useState<Set<string> | null>(null);
  const [saving, setSaving] = useState(false);

  // Chọn mặc định tính ngay lúc render đầu, không qua effect: không có khung hình nào hiện bảng
  // trống rồi mới tích vào.
  const selected = picked ?? new Set(items.filter((item) => item.timesUsed >= 3).map(keyOf));

  const toggle = (key: string) => {
    const next = new Set(selected);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    setPicked(next);
  };

  const importSelected = async () => {
    const rows = items.filter((item) => selected.has(keyOf(item)));
    if (rows.length === 0) {
      notify.error("Chưa chọn mặt hàng nào.");
      return;
    }
    setSaving(true);
    try {
      const result = await api.post<{ added: number; linkedLines: number }>("/api/products/import", {
        items: rows.map((item) => ({ name: item.name, spec: item.spec })),
      });
      setPicked(null);
      onImported(result.added, result.linkedLines);
    } catch (cause) {
      notify.error(cause instanceof Error ? cause.message : "Không thêm được vào danh mục.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <GlassPanel strong className="overflow-hidden rounded-[20px]">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--gc-border)] p-4">
        <p className="text-xs font-semibold text-[var(--gc-text-muted)]">
          Các cặp chủng loại + quy cách đã gõ trên phiếu mà chưa có trong danh mục. Thêm vào rồi, mọi
          dòng phiếu cũ khớp đúng tên sẽ được gắn mã hàng luôn.
        </p>
        {canEdit && items.length > 0 && (
          <Button loading={saving} onClick={() => void importSelected()}>
            <Check className="h-4 w-4" /> Thêm {selected.size} mặt hàng đã chọn
          </Button>
        )}
      </div>
      {loading ? (
        <div className="flex justify-center py-16">
          <Spinner />
        </div>
      ) : items.length === 0 ? (
        <div className="py-10">
          <EmptyState
            icon={<Sparkles className="h-7 w-7" />}
            title="Không còn gì để gợi ý"
            hint="Mọi mặt hàng đã gõ trên phiếu đều đã có trong danh mục."
          />
        </div>
      ) : (
        <div className="gc-scroll max-h-[calc(100vh-340px)] overflow-auto">
          <table className="gc-table w-full">
            <thead>
              <tr>
                <th className="w-10" />
                <th>Tên hàng hoá</th>
                <th>Quy cách</th>
                <th className="text-right">Số lần dùng</th>
                <th className="text-right">Dùng gần nhất</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                const key = keyOf(item);
                return (
                  <tr key={key}>
                    <td>
                      <input
                        type="checkbox"
                        checked={selected.has(key)}
                        disabled={!canEdit}
                        onChange={() => toggle(key)}
                        aria-label={`Chọn ${item.name}`}
                      />
                    </td>
                    <td className="font-semibold">{item.name}</td>
                    <td>{item.spec}</td>
                    <td className="text-right tabular-nums">{item.timesUsed}</td>
                    <td className="text-right">{item.lastUsed ? fmtDate(item.lastUsed) : "—"}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </GlassPanel>
  );
}

function ProductEditor({
  product,
  onClose,
  onSaved,
}: {
  product: Product | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const [name, setName] = useState(product?.name ?? "");
  const [spec, setSpec] = useState(product?.spec ?? "");
  const [unit, setUnit] = useState(product?.unit ?? "kg");
  const [note, setNote] = useState(product?.note ?? "");
  const [isActive, setIsActive] = useState(product?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const save = async () => {
    if (!name.trim()) {
      setError("Vui lòng nhập tên hàng hoá.");
      return;
    }
    setSaving(true);
    setError("");
    try {
      const body = { name: name.trim(), spec: spec.trim(), unit: unit.trim(), note: note.trim(), isActive };
      if (product) await api.put(`/api/products/${product.id}`, body);
      else await api.post("/api/products", body);
      notify.success(product ? "Đã cập nhật mặt hàng." : "Đã thêm mặt hàng.", "Danh mục hàng hoá");
      onSaved();
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : "Không lưu được mặt hàng.";
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
      title={product ? `Sửa ${product.code}` : "Thêm mặt hàng"}
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
        <Field label="Tên hàng hoá *">
          <Input value={name} maxLength={256} onChange={(event) => setName(event.target.value)} />
        </Field>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Quy cách">
            <Input value={spec} maxLength={256} onChange={(event) => setSpec(event.target.value)} placeholder="10mm, D60…" />
          </Field>
          <Field label="Đơn vị tính">
            <Input value={unit} maxLength={24} onChange={(event) => setUnit(event.target.value)} placeholder="kg" />
          </Field>
        </div>
        <Field label="Ghi chú">
          <Input value={note} maxLength={500} onChange={(event) => setNote(event.target.value)} />
        </Field>
        {product && (
          <label className="flex items-center gap-2 text-sm font-semibold">
            <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
            Còn sử dụng (bỏ tích để ẩn khỏi ô gợi ý mà vẫn giữ số liệu cũ)
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
