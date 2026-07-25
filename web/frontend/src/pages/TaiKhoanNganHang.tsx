import { useMemo, useState } from "react";
import { CreditCard, Pencil, Plus, Star, Trash2, Wifi } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Modal } from "../components/Modal";
import { Button, Field, Input, Select } from "../components/ui";
import { api } from "../lib/api";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/app-notifications-context";
import {
  BANK_BRANDS,
  bankBrand,
  groupAccountNumber,
  type BankAccount,
  type EmployeeDetail,
} from "../lib/hr";

export function TaiKhoanNganHang() {
  const { notify, confirm } = useAppNotifications();
  const { data: me } = useApi<EmployeeDetail>("/api/hr/me");
  const { data, loading, reload } = useApi<BankAccount[]>("/api/bank-accounts");
  const [editing, setEditing] = useState<BankAccount | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  const accounts = data ?? [];

  const setDefault = async (acc: BankAccount) => {
    if (acc.isDefault) return;
    try {
      await api.post(`/api/bank-accounts/${acc.id}/default`);
      reload({ silent: true });
      notify.success("Đã đặt làm tài khoản mặc định.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không đặt được mặc định.");
    }
  };

  const remove = async (acc: BankAccount) => {
    const ok = await confirm({
      title: "Xóa tài khoản ngân hàng?",
      description: `${bankBrand(acc.bank).shortName} · ${groupAccountNumber(acc.accountNumber)} sẽ bị xóa.`,
      confirmLabel: "Xóa",
      tone: "warning",
    });
    if (!ok) return;
    try {
      await api.del(`/api/bank-accounts/${acc.id}`);
      reload({ silent: true });
      notify.success("Đã xóa tài khoản.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được tài khoản.");
    }
  };

  return (
    <div className="gc-root">
      <PageHeader
        title="Tài khoản ngân hàng"
        subtitle="Quản lý các thẻ ngân hàng nhận lương — nền thẻ tự đồng bộ theo ngân hàng."
        actions={
          <Button onClick={() => setCreateOpen(true)}>
            <Plus className="h-4 w-4" /> Thêm thẻ
          </Button>
        }
      />

      {loading && accounts.length === 0 ? (
        <GlassPanel strong className="rounded-[20px] p-10 text-center text-sm text-[var(--text-muted)]">
          Đang tải tài khoản…
        </GlassPanel>
      ) : accounts.length === 0 ? (
        <GlassPanel strong className="flex flex-col items-center gap-3 rounded-[20px] p-10 text-center">
          <CreditCard className="h-10 w-10 text-[var(--text-muted)]" />
          <p className="text-sm font-medium text-[var(--text-secondary)]">Chưa có tài khoản ngân hàng nào</p>
          <p className="text-xs text-[var(--text-muted)]">Thêm thẻ đầu tiên để nhận lương &amp; thanh toán.</p>
          <Button onClick={() => setCreateOpen(true)} className="mt-1">
            <Plus className="h-4 w-4" /> Thêm thẻ
          </Button>
        </GlassPanel>
      ) : (
        <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 xl:grid-cols-3">
          {accounts.map((acc) => (
            <div key={acc.id} className="flex flex-col gap-2">
              <BankCard account={acc} />
              <div className="flex items-center justify-end gap-1.5">
                {!acc.isDefault && (
                  <CardAction icon={<Star className="h-4 w-4" />} label="Đặt mặc định" onClick={() => setDefault(acc)} />
                )}
                <CardAction icon={<Pencil className="h-4 w-4" />} label="Sửa" onClick={() => setEditing(acc)} />
                <CardAction icon={<Trash2 className="h-4 w-4" />} label="Xóa" danger onClick={() => remove(acc)} />
              </div>
            </div>
          ))}
          <button
            type="button"
            onClick={() => setCreateOpen(true)}
            className="flex min-h-[190px] flex-col items-center justify-center gap-2 rounded-3xl border-2 border-dashed border-[var(--glass-border)] text-[var(--text-muted)] transition-colors hover:border-[var(--accent)] hover:text-[var(--accent)]"
            style={{ aspectRatio: "1.586 / 1" }}
          >
            <Plus className="h-8 w-8" />
            <span className="text-sm font-semibold">Thêm thẻ mới</span>
          </button>
        </div>
      )}

      {(createOpen || editing) && (
        <BankAccountModal
          account={editing}
          defaultHolder={me?.fullName ?? ""}
          onClose={() => {
            setCreateOpen(false);
            setEditing(null);
          }}
          onSaved={() => {
            setCreateOpen(false);
            setEditing(null);
            reload({ silent: true });
            notify.success("Đã lưu tài khoản ngân hàng.");
          }}
        />
      )}
    </div>
  );
}

function CardAction({
  icon,
  label,
  onClick,
  danger,
}: {
  icon: React.ReactNode;
  label: string;
  onClick: () => void;
  danger?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={label}
      aria-label={label}
      className={`inline-flex items-center gap-1 rounded-lg px-2.5 py-1.5 text-xs font-semibold transition-colors ${
        danger
          ? "text-red-600 hover:bg-red-500/10 dark:text-red-400"
          : "text-[var(--text-secondary)] hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
      }`}
    >
      {icon}
      <span className="hidden sm:inline">{label}</span>
    </button>
  );
}

/** Thẻ ngân hàng: nền gradient tự đồng bộ theo thương hiệu ngân hàng của tài khoản. */
export function BankCard({ account }: { account: BankAccount }) {
  const brand = bankBrand(account.bank);
  return (
    <div
      className="relative flex w-full flex-col justify-between overflow-hidden rounded-3xl p-5 text-white shadow-xl"
      style={{
        aspectRatio: "1.586 / 1",
        background: brand.gradient,
        boxShadow: `0 18px 40px -14px ${brand.glow}`,
      }}
    >
      {/* Hoa văn nền mờ */}
      <div
        aria-hidden
        className="pointer-events-none absolute -right-10 -top-16 h-52 w-52 rounded-full"
        style={{ background: "radial-gradient(circle, rgba(255,255,255,0.22), transparent 70%)" }}
      />
      <div
        aria-hidden
        className="pointer-events-none absolute -bottom-20 -left-8 h-52 w-52 rounded-full"
        style={{ background: "radial-gradient(circle, rgba(255,255,255,0.10), transparent 70%)" }}
      />

      <div className="relative flex items-start justify-between">
        <div>
          <div className="text-base font-extrabold tracking-wide drop-shadow-sm">{brand.shortName}</div>
          <div className="text-[0.62rem] font-medium uppercase tracking-[0.18em] text-white/70">Thẻ nhận lương</div>
        </div>
        {account.isDefault && (
          <span className="inline-flex items-center gap-1 rounded-full bg-white/20 px-2 py-0.5 text-[0.62rem] font-bold backdrop-blur-sm">
            <Star className="h-3 w-3 fill-current" /> Mặc định
          </span>
        )}
      </div>

      <div className="relative flex items-center gap-3">
        {/* Chip + NFC giống thẻ vật lý */}
        <span
          className="h-7 w-9 rounded-md"
          style={{ background: "linear-gradient(135deg, #f4d78a, #cc9b3f)" }}
        />
        <Wifi className="h-4 w-4 rotate-90 text-white/80" />
      </div>

      <div className="relative">
        <div className="font-mono text-lg font-semibold tracking-[0.12em] drop-shadow-sm sm:text-xl">
          {groupAccountNumber(account.accountNumber) || "•••• •••• ••••"}
        </div>
      </div>

      <div className="relative flex items-end justify-between gap-3">
        <div className="min-w-0">
          <div className="text-[0.6rem] uppercase tracking-widest text-white/60">Chủ tài khoản</div>
          <div className="truncate text-sm font-bold uppercase">{account.accountHolder || "—"}</div>
        </div>
        {account.branch && (
          <div className="max-w-[45%] text-right">
            <div className="text-[0.6rem] uppercase tracking-widest text-white/60">Chi nhánh</div>
            <div className="truncate text-xs font-medium text-white/90">{account.branch}</div>
          </div>
        )}
      </div>
    </div>
  );
}

function BankAccountModal({
  account,
  defaultHolder,
  onClose,
  onSaved,
}: {
  account: BankAccount | null;
  defaultHolder: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const isEdit = account != null;
  const [bank, setBank] = useState(account?.bank ?? BANK_BRANDS[0].code);
  const [accountNumber, setAccountNumber] = useState(account?.accountNumber ?? "");
  const [accountHolder, setAccountHolder] = useState(account?.accountHolder ?? defaultHolder.toUpperCase());
  const [branch, setBranch] = useState(account?.branch ?? "");
  const [note, setNote] = useState(account?.note ?? "");
  const [isDefault, setIsDefault] = useState(account?.isDefault ?? false);
  const [saving, setSaving] = useState(false);

  // Thẻ xem trước: nền đồng bộ ngay khi đổi ngân hàng / nhập số.
  const preview = useMemo<BankAccount>(
    () => ({
      id: "preview",
      employeeId: "",
      employeeName: "",
      employeeCode: "",
      bank,
      accountNumber,
      accountHolder,
      branch,
      isDefault,
      note,
    }),
    [bank, accountNumber, accountHolder, branch, isDefault, note],
  );

  const submit = async () => {
    if (!accountNumber.trim()) {
      notify.error("Vui lòng nhập số tài khoản.");
      return;
    }
    setSaving(true);
    try {
      const body = {
        bank,
        accountNumber: accountNumber.trim(),
        accountHolder: accountHolder.trim(),
        branch: branch.trim(),
        note: note.trim(),
        isDefault,
      };
      if (isEdit) await api.put(`/api/bank-accounts/${account!.id}`, body);
      else await api.post("/api/bank-accounts", body);
      onSaved();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được tài khoản.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={isEdit ? "Sửa tài khoản ngân hàng" : "Thêm tài khoản ngân hàng"}
      panel
      wide
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Hủy</Button>
          <Button onClick={submit} loading={saving}>{isEdit ? "Lưu thay đổi" : "Thêm thẻ"}</Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <div className="order-2 space-y-4 lg:order-1">
          <Field label="Ngân hàng">
            <Select value={bank} onChange={(e) => setBank(e.target.value)} className="w-full">
              {BANK_BRANDS.map((b) => (
                <option key={b.code} value={b.code}>{b.shortName}</option>
              ))}
            </Select>
          </Field>
          <Field label="Số tài khoản">
            <Input
              inputMode="numeric"
              value={accountNumber}
              onChange={(e) => setAccountNumber(e.target.value.replace(/[^\d]/g, ""))}
              placeholder="Nhập số tài khoản"
            />
          </Field>
          <Field label="Chủ tài khoản">
            <Input
              value={accountHolder}
              onChange={(e) => setAccountHolder(e.target.value.toUpperCase())}
              placeholder="Tự điền theo tên nhân viên nếu để trống"
            />
          </Field>
          <Field label="Chi nhánh (tùy chọn)">
            <Input value={branch} onChange={(e) => setBranch(e.target.value)} placeholder="VD: CN Hà Nội" />
          </Field>
          <Field label="Ghi chú (tùy chọn)">
            <Input value={note} onChange={(e) => setNote(e.target.value)} />
          </Field>
          <label className="flex cursor-pointer items-center gap-2 text-sm text-[var(--text-secondary)]">
            <input
              type="checkbox"
              checked={isDefault}
              onChange={(e) => setIsDefault(e.target.checked)}
              className="h-4 w-4 accent-[var(--accent)]"
            />
            Đặt làm tài khoản mặc định
          </label>
        </div>

        <div className="order-1 lg:order-2">
          <div className="mb-2 text-xs font-semibold text-[var(--text-secondary)]">Xem trước thẻ</div>
          <BankCard account={preview} />
        </div>
      </div>
    </Modal>
  );
}
