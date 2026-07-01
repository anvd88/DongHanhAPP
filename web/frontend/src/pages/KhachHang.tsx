import { useEffect, useMemo, useState, type CSSProperties } from "react";
import { MotionConfig, motion } from "motion/react";
import {
  Building2,
  FileText,
  Loader2,
  MapPin,
  Pencil,
  Phone,
  Plus,
  ReceiptText,
  Search,
  Trash2,
  Users,
  Wallet,
} from "lucide-react";
import { GlassCapsule } from "../components/glass/GlassCapsule";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Modal } from "../components/Modal";
import { useAppNotifications } from "../components/AppNotifications";
import { Button, Field, Input } from "../components/ui";
import { Button as GlassButton } from "../shadcn/button";
import { api } from "../lib/api";
import { useApi } from "../lib/useApi";
import { date, money } from "../lib/format";
import { documentTypeText } from "../lib/documents";
import type { Customer, CustomerReport } from "../lib/types";
import { StatCard } from "../features/giacong/StatCard";
import "../features/giacong/giacong.css";
import "./khach-hang.css";

const EASE_IOS = [0.22, 1, 0.36, 1] as const;

type CustomerSaveResponse = { id: string };

const badgeTone = (type: string) => {
  const lower = type.toLowerCase();
  if (lower.includes("thu")) return "0, 184, 148";
  if (lower.includes("chi")) return "217, 119, 6";
  return "31, 107, 255";
};

function CustomerRowsSkeleton() {
  return (
    <>
      {Array.from({ length: 6 }).map((_, index) => (
        <tr key={index}>
          {Array.from({ length: 4 }).map((__, col) => (
            <td key={col} className="px-3.5 py-3">
              <div className="gc-skeleton h-4" style={{ width: col === 3 ? "42%" : "82%" }} />
            </td>
          ))}
        </tr>
      ))}
    </>
  );
}

export function KhachHang() {
  const { notify, confirm } = useAppNotifications();
  const { data: customers, loading, error, reload } = useApi<Customer[]>("/api/customers");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editing, setEditing] = useState<Customer | "new" | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const {
    data: report,
    loading: reportLoading,
    error: reportError,
    reload: reloadReport,
  } = useApi<CustomerReport>(selectedId ? `/api/customers/${selectedId}/report` : null, [selectedId]);

  const filteredCustomers = useMemo(() => {
    const query = search.trim().toLowerCase();
    return (customers ?? []).filter((customer) => {
      if (!query) return true;
      return (
        customer.name.toLowerCase().includes(query) ||
        customer.phone.toLowerCase().includes(query) ||
        customer.taxCode.toLowerCase().includes(query) ||
        customer.address.toLowerCase().includes(query)
      );
    });
  }, [customers, search]);

  useEffect(() => {
    if (!customers?.length) {
      setSelectedId(null);
      return;
    }

    if (!selectedId || !customers.some((customer) => customer.id === selectedId)) {
      setSelectedId(customers[0].id);
    }
  }, [customers, selectedId]);

  const removeCustomer = async (customer: Customer) => {
    if (deletingId) return;
    const ok = await confirm({
      title: "Xóa khách hàng?",
      description: `Xóa vĩnh viễn khách hàng "${customer.name}" và toàn bộ phiếu, dòng hàng, thanh toán liên quan?`,
      confirmLabel: "Xóa",
      tone: "danger",
    });
    if (!ok) return;

    setDeletingId(customer.id);
    try {
      await api.del(`/api/customers/${customer.id}`);
      if (selectedId === customer.id) setSelectedId(null);
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được khách hàng");
    } finally {
      setDeletingId(null);
    }
  };

  const selectedCustomer = customers?.find((customer) => customer.id === selectedId) ?? report?.customer ?? null;
  const reportDocs = report?.documents ?? [];

  return (
    <MotionConfig reducedMotion="user">
      <div className="gc-root kh-page space-y-4 pb-6">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <motion.h1
              initial={{ opacity: 0, y: 18, scale: 0.985, filter: "blur(10px)" }}
              animate={{ opacity: 1, y: 0, scale: 1, filter: "blur(0px)" }}
              transition={{ duration: 0.48, ease: EASE_IOS }}
              className="text-[1.6rem] font-black leading-tight text-[var(--gc-text)]"
            >
              Khách hàng
            </motion.h1>
            <motion.p
              initial={{ opacity: 0, y: 12, filter: "blur(8px)" }}
              animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
              transition={{ duration: 0.44, delay: 0.08, ease: EASE_IOS }}
              className="mt-1 text-sm font-semibold text-[var(--gc-text-soft)]"
            >
              Danh mục khách hàng và báo cáo phiếu theo từng khách.
            </motion.p>
          </div>

          <motion.div
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.14, ease: EASE_IOS }}
          >
            <GlassButton onClick={() => setEditing("new")}>
              <Plus className="h-4 w-4" /> Thêm khách hàng
            </GlassButton>
          </motion.div>
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            index={0}
            icon={Users}
            label="Khách hàng"
            value={money(customers?.length ?? 0)}
            sub="Đang hoạt động"
            tone="31, 107, 255"
          />
          <StatCard
            index={1}
            icon={ReceiptText}
            label="Phiếu của khách"
            value={money(report?.documentCount ?? 0)}
            sub={selectedCustomer?.name ?? "Chưa chọn khách"}
            tone="0, 184, 148"
          />
          <StatCard
            index={2}
            icon={FileText}
            label="Xuất kho bán hàng"
            value={`${money(report?.salesTotal ?? 0)} ₫`}
            sub="Theo khách đang chọn"
            tone="31, 107, 255"
          />
          <StatCard
            index={3}
            icon={Wallet}
            label="Tổng phát sinh"
            value={`${money(report?.total ?? 0)} ₫`}
            sub="Tất cả phiếu liên quan"
            tone="124, 70, 255"
          />
        </div>

        <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(360px,0.95fr)_minmax(0,1.45fr)]">
          <GlassPanel strong className="kh-panel overflow-hidden rounded-[20px]">
            <div className="kh-search-bar border-b border-[var(--gc-border)] p-3">
              <GlassCapsule className="gc-search px-4">
                <Search className="mr-2.5 h-[18px] w-[18px] shrink-0 text-[var(--gc-text-muted)]" aria-hidden="true" />
                <input
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Tìm tên, số điện thoại, mã số thuế..."
                  aria-label="Tìm khách hàng"
                />
              </GlassCapsule>
            </div>
            <div className="gc-scroll kh-table-scroll max-h-[calc(100vh-420px)] min-h-[320px] overflow-auto">
              <table className="gc-table kh-table kh-customers-table">
                <thead>
                  <tr>
                    <th>Khách hàng</th>
                    <th>Điện thoại</th>
                    <th>MST</th>
                    <th className="kh-actions-col text-right">Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {loading ? (
                    <CustomerRowsSkeleton />
                  ) : error ? (
                    <tr>
                      <td colSpan={4} className="py-14 text-center text-sm font-semibold text-rose-500">
                        {error}
                      </td>
                    </tr>
                  ) : filteredCustomers.length === 0 ? (
                    <tr>
                      <td colSpan={4} className="py-14 text-center text-sm font-semibold text-[var(--gc-text-soft)]">
                        Chưa có khách hàng phù hợp.
                      </td>
                    </tr>
                  ) : (
                    filteredCustomers.map((customer) => (
                      <tr
                        key={customer.id}
                        onClick={() => setSelectedId(customer.id)}
                        className={selectedId === customer.id ? "bg-white/20 dark:bg-white/5" : ""}
                      >
                        <td>
                          <div className="font-black text-[var(--gc-text)]">{customer.name}</div>
                        </td>
                        <td className="whitespace-nowrap">{customer.phone || "Chưa có"}</td>
                        <td className="font-semibold text-[var(--gc-text-soft)]">{customer.taxCode || "Chưa có"}</td>
                        <td className="text-right" onClick={(event) => event.stopPropagation()}>
                          <div className="flex justify-end gap-1.5">
                            <button
                              type="button"
                              title="Sửa khách hàng"
                              aria-label={`Sửa khách hàng ${customer.name}`}
                              onClick={() => setEditing(customer)}
                              className="gc-icon-btn h-8 w-8"
                            >
                              <Pencil className="h-4 w-4" />
                            </button>
                            <button
                              type="button"
                              title="Xóa khách hàng"
                              aria-label={`Xóa khách hàng ${customer.name}`}
                              disabled={deletingId === customer.id}
                              onClick={() => void removeCustomer(customer)}
                              className="gc-icon-btn h-8 w-8 text-rose-500 hover:text-rose-600 disabled:pointer-events-none disabled:opacity-50"
                            >
                              {deletingId === customer.id ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </GlassPanel>

          <GlassPanel strong className="kh-panel overflow-hidden rounded-[20px]">
            <div className="flex flex-wrap items-start justify-between gap-3 border-b border-[var(--gc-border)] p-4">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <Building2 className="h-5 w-5 text-[var(--gc-text-muted)]" />
                  <h2 className="text-lg font-black text-[var(--gc-text)]">{selectedCustomer?.name ?? "Báo cáo khách hàng"}</h2>
                </div>
                <div className="mt-2 flex flex-wrap gap-3 text-xs font-bold text-[var(--gc-text-muted)]">
                  <span className="inline-flex items-center gap-1.5">
                    <Phone className="h-3.5 w-3.5" /> {selectedCustomer?.phone || "Chưa có số điện thoại"}
                  </span>
                  <span className="inline-flex items-center gap-1.5">
                    <MapPin className="h-3.5 w-3.5" /> {selectedCustomer?.address || "Chưa có địa chỉ"}
                  </span>
                </div>
              </div>
              {selectedCustomer && (
                <GlassButton variant="soft" onClick={() => setEditing(selectedCustomer)}>
                  <Pencil className="h-4 w-4" /> Sửa khách
                </GlassButton>
              )}
            </div>

            <div className="grid grid-cols-1 gap-3 p-4 sm:grid-cols-3">
              <div className="rounded-2xl border border-[var(--gc-border)] bg-white/20 p-3 dark:bg-white/5">
                <div className="text-xs font-bold text-[var(--gc-text-muted)]">Phiếu thu</div>
                <div className="mt-1 text-lg font-black text-[var(--gc-text)]">{money(report?.receiptTotal ?? 0)} ₫</div>
              </div>
              <div className="rounded-2xl border border-[var(--gc-border)] bg-white/20 p-3 dark:bg-white/5">
                <div className="text-xs font-bold text-[var(--gc-text-muted)]">Phiếu chi</div>
                <div className="mt-1 text-lg font-black text-[var(--gc-text)]">{money(report?.paymentTotal ?? 0)} ₫</div>
              </div>
              <div className="rounded-2xl border border-[var(--gc-border)] bg-white/20 p-3 dark:bg-white/5">
                <div className="text-xs font-bold text-[var(--gc-text-muted)]">Bán hàng</div>
                <div className="mt-1 text-lg font-black text-[var(--gc-text)]">{money(report?.salesTotal ?? 0)} ₫</div>
              </div>
            </div>

            <div className="gc-scroll kh-table-scroll max-h-[calc(100vh-560px)] min-h-[260px] overflow-auto">
              <table className="gc-table kh-table kh-report-table">
                <thead>
                  <tr>
                    <th>Số phiếu</th>
                    <th>Ngày</th>
                    <th>Loại phiếu</th>
                    <th>Nội dung</th>
                    <th className="text-right">Tổng tiền</th>
                    <th>Người lập</th>
                  </tr>
                </thead>
                <tbody>
                  {!selectedId ? (
                    <tr>
                      <td colSpan={6} className="py-14 text-center text-sm font-semibold text-[var(--gc-text-soft)]">
                        Chọn một khách hàng để xem báo cáo.
                      </td>
                    </tr>
                  ) : reportLoading ? (
                    Array.from({ length: 6 }).map((_, i) => (
                      <tr key={i}>
                        {Array.from({ length: 6 }).map((__, col) => (
                          <td key={col} className="px-3.5 py-3">
                            <div className="gc-skeleton h-4" style={{ width: col === 4 ? "44%" : "80%" }} />
                          </td>
                        ))}
                      </tr>
                    ))
                  ) : reportError ? (
                    <tr>
                      <td colSpan={6} className="py-14 text-center text-sm font-semibold text-rose-500">
                        {reportError}
                      </td>
                    </tr>
                  ) : reportDocs.length === 0 ? (
                    <tr>
                      <td colSpan={6} className="py-14 text-center text-sm font-semibold text-[var(--gc-text-soft)]">
                        Khách hàng này chưa có phiếu kế toán.
                      </td>
                    </tr>
                  ) : (
                    reportDocs.map((doc) => {
                      const type = documentTypeText(doc);
                      return (
                        <tr key={doc.id}>
                          <td className="whitespace-nowrap font-bold text-[var(--gc-text)]">{doc.voucherNo}</td>
                          <td className="whitespace-nowrap text-[var(--gc-text-soft)]">{date(doc.date)}</td>
                          <td>
                            <span className="gc-badge" style={{ "--gc-badge": badgeTone(type) } as CSSProperties}>
                              <span className="gc-dot" />
                              {type}
                            </span>
                          </td>
                          <td className="min-w-[220px] text-[var(--gc-text-soft)]">{doc.content}</td>
                          <td className="whitespace-nowrap text-right font-bold tabular-nums">{money(doc.total)} ₫</td>
                          <td className="whitespace-nowrap">{doc.createdBy || "Chưa rõ"}</td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </GlassPanel>
        </div>

        {editing && (
          <CustomerEditor
            customer={editing === "new" ? null : editing}
            onClose={() => setEditing(null)}
            onSaved={(id) => {
              setEditing(null);
              setSelectedId(id);
              reload({ silent: true });
              reloadReport({ silent: true });
            }}
          />
        )}
      </div>
    </MotionConfig>
  );
}

function CustomerEditor({
  customer,
  onClose,
  onSaved,
}: {
  customer: Customer | null;
  onClose: () => void;
  onSaved: (id: string) => void;
}) {
  const [name, setName] = useState(customer?.name ?? "");
  const [taxCode, setTaxCode] = useState(customer?.taxCode ?? "");
  const [phone, setPhone] = useState(customer?.phone ?? "");
  const [address, setAddress] = useState(customer?.address ?? "");
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  const save = async () => {
    if (!name.trim()) {
      setError("Vui lòng nhập tên khách hàng.");
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
      };
      const res = customer
        ? await api.put<CustomerSaveResponse>(`/api/customers/${customer.id}`, body)
        : await api.post<CustomerSaveResponse>("/api/customers", body);
      onSaved(res.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Lỗi lưu khách hàng.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      solid
      title={customer ? "Sửa khách hàng" : "Thêm khách hàng"}
      onClose={onClose}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Hủy</Button>
          <Button onClick={save} loading={saving}>Lưu khách hàng</Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label="Tên khách hàng *">
          <Input value={name} onChange={(event) => setName(event.target.value)} placeholder="Nhập tên khách hàng" />
        </Field>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Mã số thuế">
            <Input value={taxCode} onChange={(event) => setTaxCode(event.target.value)} placeholder="MST" />
          </Field>
          <Field label="Điện thoại">
            <Input value={phone} onChange={(event) => setPhone(event.target.value)} placeholder="Số điện thoại" />
          </Field>
        </div>
        <Field label="Địa chỉ">
          <Input value={address} onChange={(event) => setAddress(event.target.value)} placeholder="Địa chỉ khách hàng" />
        </Field>
        {error && <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>}
      </div>
    </Modal>
  );
}
