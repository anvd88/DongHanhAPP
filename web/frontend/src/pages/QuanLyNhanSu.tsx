import { useEffect, useMemo, useRef, useState } from "react";
import { Building2, CalendarDays, CalendarRange, Clock, Eye, HeartHandshake, MapPin, Pencil, Plus, RotateCcw, Save, Search, Sparkles, Trash2, UserPlus, Users, Wifi, WifiOff } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Modal } from "../components/Modal";
import { Badge, Button, Field, Input, Select } from "../components/ui";
import { Table } from "../components/Table";
import { api } from "../lib/api";
import { date, dateTime, moneyVnd } from "../lib/format";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/app-notifications-context";
import { AccountCreateForm, AccountManagePanel, ASSIGNABLE_ROLES } from "./NhanSu";
import { PERM, useAccess } from "../lib/access";
import type { UserAdmin } from "../lib/types";
import { DiamondLabel, VerifiedBadge } from "../components/VerifiedBadge";
import {
  docTypeLabel,
  holidayTypeLabel,
  leaveTypeLabel,
  ACCESS_ROLES,
  accessRoleLabel,
  type Contract,
  type Department,
  type EmployeeCard,
  type EmployeeDoc,
  type Holiday,
  type LeaveBalance,
  type Location,
  type Payslip,
  type PenaltyDeductions,
  type Shift,
  type ShiftAssignment,
} from "../lib/hr";

type Tab = "employees" | "departments" | "locations" | "shifts" | "assignments" | "holidays" | "anniversary";
const TABS: { key: Tab; label: string; icon: React.ReactNode }[] = [
  { key: "employees", label: "Nhân viên", icon: <Users className="h-4 w-4" /> },
  { key: "departments", label: "Phòng ban", icon: <Building2 className="h-4 w-4" /> },
  { key: "locations", label: "Địa điểm", icon: <MapPin className="h-4 w-4" /> },
  { key: "shifts", label: "Ca làm", icon: <Clock className="h-4 w-4" /> },
  { key: "assignments", label: "Phân ca", icon: <CalendarRange className="h-4 w-4" /> },
  { key: "holidays", label: "Ngày nghỉ", icon: <CalendarDays className="h-4 w-4" /> },
  { key: "anniversary", label: "Thư tri ân", icon: <HeartHandshake className="h-4 w-4" /> },
];

export function QuanLyNhanSu() {
  const [tab, setTab] = useState<Tab>("employees");
  const { can } = useAccess();
  const visibleTabs = can(PERM.hrManage) ? TABS : TABS.filter((item) => item.key === "employees");
  return (
    <div className="gc-root">
      <PageHeader title="Quản lý nhân sự" subtitle="Hồ sơ, tài khoản nhân viên, lịch làm việc và thư tri ân" />
      <div className="mb-4 flex flex-wrap gap-2">
        {visibleTabs.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTab(t.key)}
            className={`inline-flex items-center gap-1.5 rounded-full px-3.5 py-1.5 text-xs font-bold transition ${
              tab === t.key ? "bg-[var(--accent)] text-white shadow-lg shadow-[rgba(var(--accent-rgb),0.25)]"
                : "bg-[var(--accent-soft)] text-[var(--text-secondary)] hover:text-[var(--accent)]"
            }`}
          >
            {t.icon} {t.label}
          </button>
        ))}
      </div>
      {tab === "employees" && <EmployeesTab />}
      {tab === "departments" && <DepartmentsTab />}
      {tab === "locations" && <LocationsTab />}
      {tab === "shifts" && <ShiftsTab />}
      {tab === "assignments" && <AssignmentsTab />}
      {tab === "holidays" && <HolidaysTab />}
      {tab === "anniversary" && <AnniversaryLetterTab />}
    </div>
  );
}

type AnniversaryLetterTemplate = {
  enabled: boolean;
  title: string;
  body: string;
  signature: string;
};

type LetterField = "title" | "body" | "signature";

const EMPTY_ANNIVERSARY_TEMPLATE: AnniversaryLetterTemplate = {
  enabled: true,
  title: "",
  body: "",
  signature: "",
};

const LETTER_TOKENS = [
  { token: "{ten}", label: "Tên nhân viên" },
  { token: "{so_nam}", label: "Số năm" },
  { token: "{ngay_vao_lam}", label: "Ngày vào làm" },
] as const;

function fillLetterSample(value: string) {
  return value
    .replaceAll("{ten}", "Nguyễn Văn An")
    .replaceAll("{so_nam}", "5")
    .replaceAll("{ngay_vao_lam}", "21/07/2021");
}

// ---------------- Thư tri ân thâm niên ----------------
function AnniversaryLetterTab() {
  const { notify } = useAppNotifications();
  const { data, loading, error, setData } = useApi<AnniversaryLetterTemplate>("/api/hr/anniversary/template");
  const [form, setForm] = useState<AnniversaryLetterTemplate>(EMPTY_ANNIVERSARY_TEMPLATE);
  const [saving, setSaving] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [activeField, setActiveField] = useState<LetterField>("body");
  const titleRef = useRef<HTMLInputElement>(null);
  const bodyRef = useRef<HTMLTextAreaElement>(null);
  const signatureRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (data) setForm(data);
  }, [data]);

  const dirty = data !== null && (
    data.enabled !== form.enabled ||
    data.title !== form.title ||
    data.body !== form.body ||
    data.signature !== form.signature
  );

  const set = (key: keyof AnniversaryLetterTemplate, value: string | boolean) =>
    setForm((current) => ({ ...current, [key]: value }));

  const insertToken = (token: string) => {
    const element = activeField === "title" ? titleRef.current : activeField === "body" ? bodyRef.current : signatureRef.current;
    const value = form[activeField];
    const start = element?.selectionStart ?? value.length;
    const end = element?.selectionEnd ?? start;
    const next = `${value.slice(0, start)}${token}${value.slice(end)}`;
    set(activeField, next);
    requestAnimationFrame(() => {
      element?.focus();
      element?.setSelectionRange(start + token.length, start + token.length);
    });
  };

  const save = async () => {
    if (!form.title.trim()) { notify.error("Vui lòng nhập tiêu đề thư."); return; }
    if (!form.body.trim()) { notify.error("Vui lòng nhập nội dung thư."); return; }
    setSaving(true);
    try {
      const saved = await api.put<AnniversaryLetterTemplate>("/api/hr/anniversary/template", {
        enabled: form.enabled,
        title: form.title.trim(),
        body: form.body.trim(),
        signature: form.signature.trim(),
      });
      setData(saved);
      setForm(saved);
      notify.success("Đã lưu mẫu thư tri ân.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được mẫu thư.");
    } finally {
      setSaving(false);
    }
  };

  if (loading && !data) {
    return <GlassPanel strong className="rounded-[20px] p-8 text-center text-sm text-[var(--text-muted)]">Đang tải mẫu thư…</GlassPanel>;
  }
  if (error && !data) {
    return <GlassPanel strong className="rounded-[20px] p-8 text-center text-sm text-red-600">{error}</GlassPanel>;
  }

  return (
    <>
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
        <GlassPanel strong className="overflow-hidden rounded-[20px]">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--gc-border)] px-5 py-4">
            <div>
              <h2 className="flex items-center gap-2 font-bold text-[var(--text)]"><HeartHandshake className="h-5 w-5 text-[var(--accent)]" /> Nội dung thư</h2>
              <p className="mt-1 text-xs text-[var(--text-muted)]">Mẫu này dùng chung cho mọi mốc thâm niên.</p>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={form.enabled}
              onClick={() => set("enabled", !form.enabled)}
              className={`inline-flex items-center gap-2 rounded-full px-3 py-2 text-xs font-bold transition ${form.enabled ? "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400" : "bg-black/5 text-[var(--text-muted)] dark:bg-white/10"}`}
            >
              <span className={`relative h-5 w-9 rounded-full transition ${form.enabled ? "bg-emerald-500" : "bg-slate-400"}`}>
                <span className={`absolute top-0.5 h-4 w-4 rounded-full bg-white shadow transition-all ${form.enabled ? "left-[18px]" : "left-0.5"}`} />
              </span>
              {form.enabled ? "Đang bật" : "Đang tắt"}
            </button>
          </div>

          <div className="space-y-5 p-5">
            <div className="rounded-2xl border border-[var(--gc-border)] bg-[var(--accent-soft)]/40 p-4">
              <p className="mb-2 flex items-center gap-2 text-xs font-bold text-[var(--text-secondary)]"><Sparkles className="h-4 w-4 text-[var(--accent)]" /> Chèn thông tin tự động</p>
              <div className="flex flex-wrap gap-2">
                {LETTER_TOKENS.map((item) => (
                  <button
                    type="button"
                    key={item.token}
                    onClick={() => insertToken(item.token)}
                    className="rounded-lg border border-[var(--gc-border)] bg-[var(--surface)] px-2.5 py-1.5 text-xs font-semibold text-[var(--accent)] transition hover:border-[var(--accent)]"
                    title={`Chèn vào ${activeField === "title" ? "tiêu đề" : activeField === "body" ? "nội dung" : "chữ ký"}`}
                  >
                    {item.label} <code className="ml-1 text-[10px] opacity-70">{item.token}</code>
                  </button>
                ))}
              </div>
              <p className="mt-2 text-[11px] text-[var(--text-muted)]">Đặt con trỏ vào ô cần sửa, rồi bấm biến muốn chèn.</p>
            </div>

            <Field label="Tiêu đề thư *">
              <input
                ref={titleRef}
                value={form.title}
                onFocus={() => setActiveField("title")}
                onChange={(e) => set("title", e.target.value)}
                placeholder="Ví dụ: Tri ân {so_nam} năm gắn bó"
                maxLength={180}
                className="km-form-control w-full rounded-xl border px-3.5 py-2.5 text-sm outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
              />
            </Field>

            <Field label="Nội dung thư *">
              <textarea
                ref={bodyRef}
                value={form.body}
                onFocus={() => setActiveField("body")}
                onChange={(e) => set("body", e.target.value)}
                rows={11}
                maxLength={4000}
                placeholder="Soạn lời cảm ơn dành cho nhân viên…"
                className="km-form-control w-full resize-y rounded-xl border px-3.5 py-3 text-sm leading-6 outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
              />
              <span className="mt-1 block text-right text-[10px] text-[var(--text-muted)]">{form.body.length}/4000</span>
            </Field>

            <Field label="Chữ ký">
              <textarea
                ref={signatureRef}
                value={form.signature}
                onFocus={() => setActiveField("signature")}
                onChange={(e) => set("signature", e.target.value)}
                rows={3}
                maxLength={500}
                placeholder={"Trân trọng,\nBan Giám đốc"}
                className="km-form-control w-full resize-y rounded-xl border px-3.5 py-3 text-sm leading-6 outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
              />
            </Field>

            <div className="flex flex-wrap justify-end gap-2 border-t border-[var(--gc-border)] pt-4">
              <Button variant="ghost" disabled={!dirty || saving} onClick={() => data && setForm(data)}><RotateCcw className="h-4 w-4" /> Hoàn tác</Button>
              <Button variant="soft" disabled={!form.title.trim() || !form.body.trim()} onClick={() => setPreviewOpen(true)}><Eye className="h-4 w-4" /> Xem thử</Button>
              <Button loading={saving} disabled={!dirty || !form.title.trim() || !form.body.trim()} onClick={save}><Save className="h-4 w-4" /> Lưu mẫu thư</Button>
            </div>
          </div>
        </GlassPanel>

        <div className="space-y-4">
          <GlassPanel strong className="rounded-[20px] p-5">
            <h3 className="font-bold text-[var(--text)]">Cách hoạt động</h3>
            <ol className="mt-3 space-y-3 text-sm text-[var(--text-secondary)]">
              <li className="flex gap-3"><span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-bold text-[var(--accent)]">1</span><span>Hệ thống lấy <b>Ngày vào làm</b> trong hồ sơ nhân viên.</span></li>
              <li className="flex gap-3"><span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-bold text-[var(--accent)]">2</span><span>Khi đủ tròn năm, các biến trong thư được điền tự động.</span></li>
              <li className="flex gap-3"><span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-bold text-[var(--accent)]">3</span><span>APK hiển thị thư với hiệu ứng máy đánh chữ, một lần cho mỗi mốc.</span></li>
            </ol>
          </GlassPanel>
          <GlassPanel strong className="rounded-[20px] p-5">
            <h3 className="font-bold text-[var(--text)]">Lưu ý</h3>
            <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">Tắt mẫu thư sẽ ngừng gửi cho tất cả nhân viên. Nội dung đang soạn chỉ có hiệu lực sau khi bấm <b>Lưu mẫu thư</b>.</p>
          </GlassPanel>
        </div>
      </div>

      {previewOpen && <AnniversaryLetterPreview template={form} onClose={() => setPreviewOpen(false)} />}
    </>
  );
}

function AnniversaryLetterPreview({ template, onClose }: { template: AnniversaryLetterTemplate; onClose: () => void }) {
  const title = fillLetterSample(template.title);
  const fullLetter = [fillLetterSample(template.body.trim()), fillLetterSample(template.signature.trim())].filter(Boolean).join("\n\n");
  const [visible, setVisible] = useState(0);

  useEffect(() => {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      setVisible(fullLetter.length);
      return;
    }
    if (visible >= fullLetter.length) return;
    const next = fullLetter[visible];
    const wait = ".!?".includes(next) ? 105 : ",;:".includes(next) ? 55 : next === "\n" ? 90 : 18;
    const timer = window.setTimeout(() => setVisible((value) => value + 1), wait);
    return () => window.clearTimeout(timer);
  }, [fullLetter, visible]);

  return (
    <Modal open onClose={onClose} title="Xem thử thư tri ân" panel wide
      footer={<><Button variant="soft" disabled={visible >= fullLetter.length} onClick={() => setVisible(fullLetter.length)}>Hiện toàn bộ</Button><Button onClick={onClose}>Đóng xem thử</Button></>}>
      <div className="mx-auto max-w-2xl overflow-hidden rounded-[28px] border border-amber-200/70 bg-gradient-to-br from-amber-50 via-white to-orange-50 shadow-xl dark:border-amber-700/40 dark:from-slate-900 dark:via-slate-950 dark:to-amber-950/40">
        <div className="flex items-start gap-4 border-b border-amber-200/60 p-6 dark:border-amber-700/30">
          <div className="flex h-16 w-16 shrink-0 flex-col items-center justify-center rounded-full bg-amber-400 text-amber-950 shadow-lg shadow-amber-500/20">
            <span className="text-2xl font-black leading-none">5</span><span className="text-[10px] font-black">NĂM</span>
          </div>
          <div>
            <p className="flex items-center gap-1.5 text-xs font-black tracking-wider text-amber-700 dark:text-amber-400"><Sparkles className="h-4 w-4" /> BẢN XEM THỬ · THƯ TRI ÂN</p>
            <h3 className="mt-1 text-xl font-black text-slate-900 dark:text-white">{title}</h3>
          </div>
        </div>
        <div className="min-h-64 whitespace-pre-wrap p-6 font-mono text-sm leading-7 text-slate-700 dark:text-slate-200">
          {fullLetter.slice(0, visible)}{visible < fullLetter.length && <span className="animate-pulse text-amber-600">▌</span>}
        </div>
      </div>
    </Modal>
  );
}

function toolbar(title: string, onAdd: () => void, addLabel: string) {
  return (
    <div className="flex items-center justify-between border-b border-[var(--gc-border)] px-5 py-4">
      <h2 className="font-bold text-[var(--text)]">{title}</h2>
      <Button onClick={onAdd}><Plus className="h-4 w-4" /> {addLabel}</Button>
    </div>
  );
}

function currentMonthKey() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

function monthRange(month: string) {
  const [year, mon] = month.split("-").map(Number);
  const safe = year && mon ? new Date(year, mon - 1, 1) : new Date();
  const from = `${safe.getFullYear()}-${String(safe.getMonth() + 1).padStart(2, "0")}-01`;
  const last = new Date(safe.getFullYear(), safe.getMonth() + 1, 0);
  const to = `${last.getFullYear()}-${String(last.getMonth() + 1).padStart(2, "0")}-${String(last.getDate()).padStart(2, "0")}`;
  return { from, to };
}

// ---------------- Nhân viên ----------------
type EmployeeAccountRow = {
  key: string;
  employee: EmployeeCard | null;
  account: UserAdmin | null;
};

const usernameKey = (value?: string | null) => (value ?? "").trim().toLocaleLowerCase("vi");

function accountCreatedDateValue(value?: string | null) {
  if (!value) return "";
  const parts = new Intl.DateTimeFormat("en", {
    timeZone: "Asia/Ho_Chi_Minh", year: "numeric", month: "2-digit", day: "2-digit",
  }).formatToParts(new Date(value));
  const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value ?? "";
  return `${get("year")}-${get("month")}-${get("day")}`;
}

const ACCOUNT_ROLE_FILTERS = [
  { value: "", label: "Tất cả vai trò hệ thống" },
  { value: "Admin", label: "Admin" },
  { value: "Accounting", label: "Kế toán" },
  { value: "HR", label: "Nhân sự (HR)" },
  { value: "Employee", label: "Nhân viên" },
  { value: "Pending", label: "Chờ duyệt" },
  { value: "Locked", label: "Đã khóa" },
] as const;

function EmployeesTab() {
  const { can } = useAccess();
  const canManageProfiles = can(PERM.hrManage);
  const canManageAccounts = can(PERM.usersManage);
  const canCompareLinks = canManageProfiles && canManageAccounts;
  const { data: employees, loading: employeesLoading, reload: reloadEmployees } = useApi<EmployeeCard[]>(canManageProfiles ? "/api/hr/employees" : null);
  const { data: accounts, loading: accountsLoading, reload: reloadAccounts } = useApi<UserAdmin[]>(canManageAccounts ? "/api/users/?search=&role=" : null);
  const { data: departments } = useApi<Department[]>(canManageProfiles ? "/api/hr/departments" : null);
  const { data: locations } = useApi<Location[]>(canManageProfiles ? "/api/hr/locations" : null);
  const [manage, setManage] = useState<EmployeeAccountRow | "new" | null>(null);
  const [search, setSearch] = useState("");
  const [linkFilter, setLinkFilter] = useState("all");
  const [roleFilter, setRoleFilter] = useState("");

  const rows = useMemo<EmployeeAccountRow[]>(() => {
    const accountByUsername = new Map<string, UserAdmin>();
    for (const account of accounts ?? []) {
      const key = usernameKey(account.username);
      if (key) accountByUsername.set(key, account);
    }

    const matchedAccountIds = new Set<string>();
    const merged: EmployeeAccountRow[] = (employees ?? []).map((employee) => {
      const account = accountByUsername.get(usernameKey(employee.username)) ?? null;
      if (account) matchedAccountIds.add(account.id);
      return { key: `employee-${employee.id}`, employee, account };
    });

    for (const account of accounts ?? []) {
      if (!matchedAccountIds.has(account.id)) merged.push({ key: `account-${account.id}`, employee: null, account });
    }

    return merged.sort((a, b) => {
      const nameA = a.employee?.fullName || a.account?.fullName || a.account?.username || "";
      const nameB = b.employee?.fullName || b.account?.fullName || b.account?.username || "";
      return nameA.localeCompare(nameB, "vi");
    });
  }, [accounts, employees]);

  const visibleRows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase("vi");
    return rows.filter(({ employee, account }) => {
      if (canCompareLinks && linkFilter === "linked" && (!employee || !account)) return false;
      if (canCompareLinks && linkFilter === "missing-account" && (!employee || account)) return false;
      if (canCompareLinks && linkFilter === "missing-profile" && (employee || !account)) return false;
      if (roleFilter === "Pending" && account?.approvalStatus !== "Pending") return false;
      if (roleFilter === "Locked" && account?.isActive !== false) return false;
      if (roleFilter === "Employee" && account?.role !== "Employee" && account?.role !== "User") return false;
      if (roleFilter && roleFilter !== "Pending" && roleFilter !== "Locked" && roleFilter !== "Employee" && account?.role !== roleFilter) return false;
      if (!query) return true;
      return [employee?.employeeCode, employee?.fullName, employee?.username, employee?.position,
        employee?.departmentName, employee?.locationName, account?.username, account?.fullName, account?.email]
        .some((value) => value?.toLocaleLowerCase("vi").includes(query));
    });
  }, [canCompareLinks, linkFilter, roleFilter, rows, search]);

  const loading = (canManageProfiles && employeesLoading) || (canManageAccounts && accountsLoading);

  return (
    <GlassPanel strong className="overflow-hidden rounded-[20px]">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--gc-border)] px-5 py-4">
        <div>
          <h2 className="font-bold text-[var(--text)]">{canCompareLinks ? "Nhân viên & tài khoản" : canManageProfiles ? "Danh sách nhân viên" : "Danh sách tài khoản"}</h2>
          <p className="mt-1 text-xs text-[var(--text-muted)]">Mỗi người một dòng — bấm “Quản lý” để xử lý hồ sơ, tài khoản và quyền lợi ở cùng một chỗ.</p>
        </div>
        {(canManageProfiles || canManageAccounts) && <Button onClick={() => setManage("new")}><Plus className="h-4 w-4" /> Thêm</Button>}
      </div>
      <div className="flex flex-wrap items-center gap-3 border-b border-[var(--gc-border)] p-3">
        <div className="relative min-w-56 flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
          <Input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Tìm tên, mã nhân viên, username, phòng ban…" className="pl-9" />
        </div>
        {canCompareLinks && <Select value={linkFilter} onChange={(event) => setLinkFilter(event.target.value)}>
          <option value="all">Tất cả liên kết</option>
          <option value="linked">Đã đủ hồ sơ & tài khoản</option>
          <option value="missing-account">Chưa có tài khoản</option>
          <option value="missing-profile">Chưa có hồ sơ nhân viên</option>
        </Select>}
        {canManageAccounts && <Select value={roleFilter} onChange={(event) => setRoleFilter(event.target.value)}>
          {ACCOUNT_ROLE_FILTERS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
        </Select>}
      </div>
      <Table<EmployeeAccountRow>
        loading={loading}
        rows={visibleRows}
        keyOf={(row) => row.key}
        empty="Không có nhân viên hoặc tài khoản phù hợp"
        columns={[
          { header: "Thông tin chung", cell: ({ employee, account }) => {
            const fullName = employee?.fullName?.trim() || account?.fullName?.trim() || account?.username || "—";
            const username = account?.username?.trim() || employee?.username?.trim() || "";
            const email = employee?.email?.trim() || account?.email?.trim() || "";
            return (
              <div className="min-w-52">
                <p className="flex items-center gap-1.5 font-semibold">
                  {fullName}
                  {account?.verified && <VerifiedBadge size={14} />}
                  {account?.isDiamond && <DiamondLabel />}
                </p>
                <p className="mt-1 font-mono text-xs font-bold text-[var(--accent)]">{username || "Chưa gắn username"}</p>
                <p className="mt-0.5 text-[11px] text-[var(--text-muted)]">{email || "Chưa có email"}</p>
                <div className="mt-1.5 flex flex-wrap gap-1">
                  {employee ? <Badge color="muted">{employee.employeeCode || "Chưa có mã NV"}</Badge> : canManageProfiles && <Badge color="warning">Chưa có hồ sơ</Badge>}
                  {!account && canManageAccounts && <Badge color="warning">Chưa có tài khoản</Badge>}
                </div>
                {account?.createdAt && <p className="mt-1 text-[10px] text-[var(--text-muted)]">Tài khoản tạo: {date(account.createdAt)}</p>}
              </div>
            );
          } },
          { header: "Công việc", cell: ({ employee }) => employee ? (
            <div className="min-w-36 text-xs">
              <p className="font-semibold text-[var(--text-secondary)]">{employee.position || "Chưa có chức vụ"}</p>
              <p className="mt-1">{employee.departmentName || "Chưa có phòng ban"}</p>
              {employee.locationName && <p className="mt-0.5 text-[var(--text-muted)]">{employee.locationName}</p>}
            </div>
          ) : "—" },
          { header: "Vai trò hệ thống", cell: ({ account }) => (
            <div className="flex min-w-32 flex-col items-start gap-1">
              {account && <Badge color={account.role === "Admin" ? "purple" : "accent"}>{account.role}</Badge>}
              {account && account.secondaryRoles?.length > 0 && <span className="text-[11px] text-[var(--text-muted)]">Vai trò phụ: {account.secondaryRoles.join(", ")}</span>}
              {!account && <span className="text-xs text-[var(--text-muted)]">Chưa có tài khoản</span>}
            </div>
          ) },
          { header: "Phạm vi dữ liệu HR", cell: ({ employee }) => (
            <div className="min-w-32">
              {employee ? <Badge color={employee.accessRole && employee.accessRole !== "staff" ? "success" : "muted"}>{accessRoleLabel(employee.accessRole)}</Badge>
                : <span className="text-xs text-[var(--text-muted)]">Chưa có hồ sơ</span>}
            </div>
          ) },
          { header: "Online", cell: ({ account }) => account ? (
            <div className="flex min-w-28 flex-col items-start gap-1">
              <Badge color={account.isOnline ? "success" : "muted"}>
                {account.isOnline ? <Wifi className="h-3.5 w-3.5" /> : <WifiOff className="h-3.5 w-3.5" />}
                {account.isOnline ? "Online" : "Offline"}
              </Badge>
              {account.lastSeen && <span className="text-[10px] text-[var(--text-muted)]">Lần cuối: {dateTime(account.lastSeen)}</span>}
            </div>
          ) : <span className="text-xs text-[var(--text-muted)]">Chưa có tài khoản</span> },
          { header: "Trạng thái", cell: ({ employee, account }) => (
            <div className="flex min-w-28 flex-col items-start gap-1">
              {employee && <Badge color={employee.status === "Active" ? "success" : "muted"}>{employee.status === "Active" ? "Đang làm" : "Đã nghỉ · mất truy cập"}</Badge>}
              {account && <Badge color={account.approvalStatus === "Pending" ? "warning" : account.isActive ? "accent" : "danger"}>
                {account.approvalStatus === "Pending" ? "Chờ duyệt" : account.isActive ? "Hoạt động" : "Tài khoản khóa"}
              </Badge>}
            </div>
          ) },
          {
            header: "", align: "right",
            cell: (row) => (
              <div className="flex justify-end">
                <button onClick={() => setManage(row)} className="inline-flex items-center gap-1 rounded-lg px-2.5 py-1.5 text-xs font-semibold text-[var(--accent)] hover:bg-[var(--accent-soft)]" title="Hồ sơ, tài khoản và quyền lợi"><Pencil className="h-3.5 w-3.5" /> Quản lý</button>
              </div>
            ),
          },
        ]}
      />
      {manage && <PersonModal
        target={manage}
        departments={departments ?? []} locations={locations ?? []}
        employees={employees ?? []} accounts={accounts ?? []}
        canManageProfiles={canManageProfiles} canManageAccounts={canManageAccounts}
        onClose={() => setManage(null)}
        onEmployeesChanged={() => reloadEmployees({ silent: true })}
        onAccountsChanged={() => reloadAccounts({ silent: true })} />}
    </GlassPanel>
  );
}

type PersonTarget = EmployeeAccountRow | "new";

/**
 * Một người = hồ sơ nhân viên + tài khoản đăng nhập (ghép theo username). Modal này gom cả hai vào MỘT
 * chỗ với các mục con: Hồ sơ · Tài khoản · Quyền lợi — thay cho việc mở nhiều hộp thoại rời rạc trước đây.
 */
function PersonModal({
  target, departments, locations, employees, accounts, canManageProfiles, canManageAccounts,
  onClose, onEmployeesChanged, onAccountsChanged,
}: {
  target: PersonTarget;
  departments: Department[];
  locations: Location[];
  employees: EmployeeCard[];
  accounts: UserAdmin[];
  canManageProfiles: boolean;
  canManageAccounts: boolean;
  onClose: () => void;
  onEmployeesChanged: () => void;
  onAccountsChanged: () => void;
}) {
  const isNew = target === "new";
  // Lấy bản mới nhất từ danh sách vừa tải lại (sau khi đổi vai trò/khóa…) để modal không hiển thị dữ liệu cũ.
  const employee = isNew || !target.employee ? null : employees.find((e) => e.id === target.employee!.id) ?? target.employee;
  const account = isNew || !target.account ? null : accounts.find((a) => a.id === target.account!.id) ?? target.account;

  type Sub = "profile" | "account" | "benefits";
  const subs: { key: Sub; label: string }[] = [];
  if (canManageProfiles) subs.push({ key: "profile", label: employee ? "Hồ sơ" : "Tạo hồ sơ" });
  // Thêm người mới: "Tạo hồ sơ" đã tự tạo luôn tài khoản đăng nhập, nên bỏ tab "Tạo tài khoản" cho gọn
  // (chỉ giữ khi người quản trị này không quản được hồ sơ mà chỉ quản tài khoản).
  const hideAccountCreateForNew = isNew && canManageProfiles;
  if (canManageAccounts && !hideAccountCreateForNew) subs.push({ key: "account", label: account ? "Tài khoản" : "Tạo tài khoản" });
  if (canManageProfiles && employee) subs.push({ key: "benefits", label: "Quyền lợi" });

  const [sub, setSub] = useState<Sub>(
    employee && canManageProfiles ? "profile" : account && canManageAccounts ? "account" : subs[0]?.key ?? "profile",
  );

  const title = isNew
    ? "Thêm người mới"
    : `Quản lý · ${employee?.fullName?.trim() || account?.fullName?.trim() || account?.username || "—"}`;

  return (
    <Modal open onClose={onClose} title={title} panel wide footer={<Button variant="ghost" onClick={onClose}>Đóng</Button>}>
      {subs.length > 1 && (
        <div className="mb-4 flex flex-wrap gap-2">
          {subs.map((s) => (
            <button key={s.key} type="button" onClick={() => setSub(s.key)}
              className={`rounded-full px-3.5 py-1.5 text-xs font-bold transition ${sub === s.key ? "bg-[var(--accent)] text-white" : "bg-[var(--accent-soft)] text-[var(--text-secondary)]"}`}>
              {s.label}
            </button>
          ))}
        </div>
      )}
      {sub === "profile" && (
        <ProfilePanel
          employee={employee} initialAccount={account}
          departments={departments} locations={locations} employees={employees}
          onSavedEdit={onEmployeesChanged}
          onCreated={() => { onEmployeesChanged(); onClose(); }}
          onDeleted={() => { onEmployeesChanged(); onClose(); }}
        />
      )}
      {sub === "account" && (account
        ? <AccountManagePanel value={account} onChanged={onAccountsChanged} onClose={onClose} />
        : <AccountCreateForm
            initial={{ username: employee?.username ?? undefined, fullName: employee?.fullName ?? undefined, email: employee?.email ?? undefined }}
            onSaved={() => { onAccountsChanged(); onClose(); }} />)}
      {sub === "benefits" && employee && <BenefitsPanel empId={employee.id} />}
    </Modal>
  );
}

function ProfilePanel({ employee, initialAccount, departments, locations, employees, onSavedEdit, onCreated, onDeleted }: {
  employee: EmployeeCard | null; initialAccount?: UserAdmin | null; departments: Department[]; locations: Location[]; employees: EmployeeCard[];
  onSavedEdit: () => void; onCreated: () => void; onDeleted: () => void;
}) {
  const { notify, confirm } = useAppNotifications();
  const [detail] = useState(employee);
  const [form, setForm] = useState({
    employeeCode: employee?.employeeCode ?? "",
    username: employee?.username ?? initialAccount?.username ?? "",
    fullName: employee?.fullName ?? initialAccount?.fullName ?? "",
    position: employee?.position ?? "",
    // Chỉ dùng khi TẠO MỚI: "Chức vụ" là dropdown vai trò hệ thống → quyết định quyền của tài khoản tạo ra.
    role: "Employee",
    departmentId: employee?.departmentId ?? "",
    locationId: employee?.locationId ?? "",
    accessRole: employee?.accessRole ?? "staff",
    status: employee?.status ?? "Active",
    phone: employee?.phone ?? "",
    email: employee?.email ?? initialAccount?.email ?? "",
    managerId: "",
    hireDate: employee?.hireDate ?? accountCreatedDateValue(initialAccount?.createdAt),
    dob: "",
    gender: "",
    address: "",
  });
  const [saving, setSaving] = useState(false);
  const [removing, setRemoving] = useState(false);
  const set = (k: keyof typeof form, v: string) => setForm((s) => ({ ...s, [k]: v }));

  const save = async () => {
    if (!form.fullName.trim()) { notify.error("Vui lòng nhập họ tên."); return; }
    if (!form.departmentId) { notify.error("Vui lòng chọn phòng ban cho nhân viên."); return; }
    setSaving(true);
    try {
      const body = {
        ...form,
        departmentId: form.departmentId || null,
        locationId: form.locationId || null,
        managerId: form.managerId || null,
        hireDate: form.hireDate || null,
        dob: form.dob || null,
      };
      if (detail) { await api.put(`/api/hr/employees/${detail.id}`, body); notify.success("Đã lưu hồ sơ."); onSavedEdit(); }
      else {
        // Tạo hồ sơ mới LUÔN kèm tài khoản đăng nhập (backend tự sinh mã NV + username, mật khẩu mặc định 123).
        // "Chức vụ" chọn = vai trò hệ thống: lưu nhãn vào position để hiển thị, gửi role để đặt quyền tài khoản.
        const roleLabel = ASSIGNABLE_ROLES.find((r) => r.key === form.role)?.label ?? "";
        const res = await api.post<{ username?: string; accountCreated?: boolean; password?: string }>(
          "/api/hr/employees", { ...body, position: roleLabel, role: form.role, createAccount: true });
        if (res?.accountCreated)
          notify.success(`Đã tạo hồ sơ và tài khoản đăng nhập “${res.username}” · mật khẩu ${res.password}.`);
        else notify.success("Đã tạo hồ sơ nhân viên.");
        onCreated();
      }
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được.");
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    if (!detail) return;
    const ok = await confirm({ title: `Xóa hồ sơ ${detail.fullName}?`, description: "Toàn bộ hợp đồng, phiếu lương, phép của nhân viên này sẽ bị xóa.", confirmLabel: "Xóa", tone: "danger" });
    if (!ok) return;
    setRemoving(true);
    try {
      await api.del(`/api/hr/employees/${detail.id}`);
      notify.success("Đã xóa hồ sơ.");
      onDeleted();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được.");
    } finally {
      setRemoving(false);
    }
  };

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        {detail && <Field label="Mã nhân viên"><Input value={form.employeeCode} onChange={(e) => set("employeeCode", e.target.value)} placeholder="Tự sinh nếu để trống" /></Field>}
        {detail && <Field label="Tài khoản đăng nhập"><Input value={form.username} onChange={(e) => set("username", e.target.value)} placeholder="username" /></Field>}
        <Field label="Họ tên *"><Input value={form.fullName} onChange={(e) => set("fullName", e.target.value)} /></Field>
        {detail ? (
          <Field label="Chức vụ"><Input value={form.position} onChange={(e) => set("position", e.target.value)} /></Field>
        ) : (
          <Field label="Chức vụ (vai trò hệ thống) *">
            <Select value={form.role} onChange={(e) => set("role", e.target.value)} className="w-full">
              {ASSIGNABLE_ROLES.map((r) => <option key={r.key} value={r.key}>{r.label}</option>)}
            </Select>
            <p className="mt-1 text-[11px] text-[var(--text-muted)]">Vừa là chức vụ hiển thị, vừa quyết định quyền của tài khoản đăng nhập được tạo.</p>
          </Field>
        )}
        <Field label="Phòng ban *">
          <Select value={form.departmentId} onChange={(e) => set("departmentId", e.target.value)} className="w-full">
            <option value="">— Chọn phòng ban —</option>
            {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </Select>
        </Field>
        <Field label="Địa điểm / chi nhánh">
          <Select value={form.locationId} onChange={(e) => set("locationId", e.target.value)} className="w-full">
            <option value="">— Không —</option>
            {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </Select>
        </Field>
        <Field label="Phạm vi dữ liệu nhân sự">
          <Select value={form.accessRole} onChange={(e) => set("accessRole", e.target.value)} className="w-full">
            {ACCESS_ROLES.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
          </Select>
          <p className="mt-1 text-[11px] text-[var(--text-muted)]">Chỉ giới hạn dữ liệu HR được xem/quản lý; không thay đổi quyền sử dụng ứng dụng.</p>
        </Field>
        <Field label="Quản lý trực tiếp">
          <Select value={form.managerId} onChange={(e) => set("managerId", e.target.value)} className="w-full">
            <option value="">— Không —</option>
            {employees.filter((e) => e.id !== detail?.id).map((e) => <option key={e.id} value={e.id}>{e.fullName}</option>)}
          </Select>
        </Field>
        <Field label="Ngày vào làm / ngày tạo tài khoản"><Input type="date" value={form.hireDate} onChange={(e) => set("hireDate", e.target.value)} /></Field>
        <Field label="Trạng thái">
          <Select value={form.status} onChange={(e) => set("status", e.target.value)} className="w-full">
            <option value="Active">Đang làm việc</option>
            <option value="Inactive">Đã nghỉ — khóa quyền truy cập</option>
          </Select>
        </Field>
        <Field label="Số điện thoại"><Input value={form.phone} onChange={(e) => set("phone", e.target.value)} /></Field>
        <Field label="Email"><Input value={form.email} onChange={(e) => set("email", e.target.value)} /></Field>
        <Field label="Ngày sinh"><Input type="date" value={form.dob} onChange={(e) => set("dob", e.target.value)} /></Field>
        <Field label="Giới tính"><Input value={form.gender} onChange={(e) => set("gender", e.target.value)} placeholder="Nam / Nữ" /></Field>
        <div className="sm:col-span-2"><Field label="Địa chỉ"><Input value={form.address} onChange={(e) => set("address", e.target.value)} /></Field></div>
      </div>
      {!detail && !initialAccount && (
        <div className="flex items-start gap-2.5 rounded-xl border border-[var(--gc-border)] bg-[var(--accent-soft)]/30 p-3">
          <UserPlus className="mt-0.5 h-4 w-4 shrink-0 text-[var(--accent)]" />
          <span className="text-sm">
            <span className="font-semibold text-[var(--text)]">Tài khoản đăng nhập sẽ được tạo tự động</span>
            <span className="block text-xs text-[var(--text-secondary)]">Hệ thống tự sinh mã nhân viên và tên đăng nhập từ họ tên (vd: <code className="font-mono">annv01</code>), mật khẩu mặc định <b>123</b>. Quyền của tài khoản lấy theo <b>Chức vụ</b> đã chọn ở trên.</span>
          </span>
        </div>
      )}
      {detail && <p className="text-xs text-[var(--text-muted)]">Lưu ý: ngày sinh/giới tính/địa chỉ đầy đủ sẽ ghi đè khi lưu ở chế độ quản trị.</p>}
      <div className="flex flex-wrap justify-end gap-2 border-t border-[var(--gc-border)] pt-4">
        {detail && <Button variant="danger" loading={removing} disabled={saving} onClick={() => void remove()}><Trash2 className="h-4 w-4" /> Xóa hồ sơ</Button>}
        <Button onClick={save} loading={saving} disabled={removing}><Save className="h-4 w-4" /> {detail ? "Lưu hồ sơ" : "Tạo hồ sơ"}</Button>
      </div>
    </div>
  );
}

// ---------------- Quyền lợi (hợp đồng / phiếu lương / phép / bằng cấp) ----------------
function BenefitsPanel({ empId }: { empId: string }) {
  const [sub, setSub] = useState<"contract" | "payslip" | "leave" | "docs">("contract");
  const subs = [
    { k: "contract", l: "Hợp đồng" },
    { k: "payslip", l: "Phiếu lương" },
    { k: "leave", l: "Ngày phép" },
    { k: "docs", l: "Bằng cấp" },
  ] as const;
  return (
    <div>
      <div className="mb-4 flex flex-wrap gap-2">
        {subs.map((s) => (
          <button key={s.k} onClick={() => setSub(s.k)}
            className={`rounded-full px-3 py-1.5 text-xs font-bold ${sub === s.k ? "bg-[var(--accent)] text-white" : "bg-[var(--accent-soft)] text-[var(--text-secondary)]"}`}>
            {s.l}
          </button>
        ))}
      </div>
      {sub === "contract" && <ContractAdmin empId={empId} />}
      {sub === "payslip" && <PayslipAdmin empId={empId} />}
      {sub === "leave" && <LeaveAdmin empId={empId} />}
      {sub === "docs" && <DocsAdmin empId={empId} />}
    </div>
  );
}

function ContractAdmin({ empId }: { empId: string }) {
  const { notify } = useAppNotifications();
  const { data, loading, reload } = useApi<Contract[]>(`/api/hr/employees/${empId}/contracts`, [empId]);
  const [f, setF] = useState({ contractNo: "", contractType: "Xác định thời hạn", startDate: "", endDate: "", baseSalary: "", allowance: "" });
  const set = (k: keyof typeof f, v: string) => setF((s) => ({ ...s, [k]: v }));
  const add = async () => {
    try {
      await api.post(`/api/hr/employees/${empId}/contracts`, {
        ...f, startDate: f.startDate || null, endDate: f.endDate || null,
        baseSalary: Number(f.baseSalary) || 0, allowance: Number(f.allowance) || 0, status: "Active",
      });
      setF({ contractNo: "", contractType: "Xác định thời hạn", startDate: "", endDate: "", baseSalary: "", allowance: "" });
      reload({ silent: true });
      notify.success("Đã thêm hợp đồng.");
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi."); }
  };
  const del = async (id: string) => { await api.del(`/api/hr/contracts/${id}`); reload({ silent: true }); };
  return (
    <div>
      <div className="mb-4 grid grid-cols-2 gap-2 sm:grid-cols-3">
        <Field label="Số HĐ"><Input value={f.contractNo} onChange={(e) => set("contractNo", e.target.value)} /></Field>
        <Field label="Loại HĐ"><Input value={f.contractType} onChange={(e) => set("contractType", e.target.value)} /></Field>
        <Field label="Lương cơ bản"><Input type="number" value={f.baseSalary} onChange={(e) => set("baseSalary", e.target.value)} /></Field>
        <Field label="Phụ cấp"><Input type="number" value={f.allowance} onChange={(e) => set("allowance", e.target.value)} /></Field>
        <Field label="Từ ngày"><Input type="date" value={f.startDate} onChange={(e) => set("startDate", e.target.value)} /></Field>
        <Field label="Đến ngày"><Input type="date" value={f.endDate} onChange={(e) => set("endDate", e.target.value)} /></Field>
      </div>
      <div className="mb-4 flex justify-end"><Button onClick={add}><Plus className="h-4 w-4" /> Thêm hợp đồng</Button></div>
      <Table<Contract> loading={loading} rows={data ?? []} keyOf={(r) => r.id} empty="Chưa có hợp đồng"
        columns={[
          { header: "Số HĐ", cell: (r) => r.contractNo || "—" },
          { header: "Loại", cell: (r) => r.contractType || "—" },
          { header: "Lương", align: "right", cell: (r) => moneyVnd(r.baseSalary) },
          { header: "Từ", cell: (r) => r.startDate ? date(r.startDate) : "—" },
          { header: "", align: "right", cell: (r) => <button onClick={() => del(r.id)} className="rounded-lg p-2 text-red-600 hover:bg-red-500/10"><Trash2 className="h-4 w-4" /></button> },
        ]} />
    </div>
  );
}

function PayslipAdmin({ empId }: { empId: string }) {
  const { notify } = useAppNotifications();
  const { data, loading, reload } = useApi<Payslip[]>(`/api/hr/employees/${empId}/payslips`, [empId]);
  const [f, setF] = useState({ period: "", workDays: "", baseSalary: "", allowance: "", overtimePay: "", deductions: "", published: true });
  const set = (k: keyof typeof f, v: string | boolean) => setF((s) => ({ ...s, [k]: v }));
  // Lương còn có thể trừ cho phạt = lương+phụ cấp+tăng ca − khấu trừ KHÁC (không để phạt làm âm lương).
  const otherDeductions = Number(f.deductions) || 0;
  const available = Math.max(0, (Number(f.baseSalary) || 0) + (Number(f.allowance) || 0) + (Number(f.overtimePay) || 0) - otherDeductions);
  // Tiền phạt thực trừ kỳ này (backend tự tính + cap; phần chưa thu tự chuyển sang kỳ sau).
  const { data: penalty } = useApi<PenaltyDeductions>(
    f.period ? `/api/penalties/deductions?employeeId=${empId}&period=${f.period}&available=${available}` : null,
    [empId, f.period, available],
  );
  const penaltyTotal = penalty?.total ?? 0;
  const totalDeductions = otherDeductions + penaltyTotal;
  const add = async () => {
    if (!f.period) { notify.error("Nhập kỳ lương (yyyy-MM)."); return; }
    try {
      // deductions gửi lên = khấu trừ KHÁC; backend tự tính + cap tiền phạt rồi cộng vào.
      await api.post(`/api/hr/employees/${empId}/payslips`, {
        period: f.period, workDays: Number(f.workDays) || 0, overtimeHours: 0,
        baseSalary: Number(f.baseSalary) || 0, allowance: Number(f.allowance) || 0,
        overtimePay: Number(f.overtimePay) || 0, deductions: otherDeductions,
        note: penaltyTotal > 0 ? `Đã trừ phạt ${moneyVnd(penaltyTotal)}` : "", published: f.published,
      });
      reload({ silent: true });
      notify.success("Đã lập phiếu lương.");
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi."); }
  };
  const del = async (id: string) => { await api.del(`/api/hr/payslips/${id}`); reload({ silent: true }); };
  return (
    <div>
      <div className="mb-4 grid grid-cols-2 gap-2 sm:grid-cols-3">
        <Field label="Kỳ (yyyy-MM)"><Input type="month" value={f.period} onChange={(e) => set("period", e.target.value)} /></Field>
        <Field label="Ngày công"><Input type="number" value={f.workDays} onChange={(e) => set("workDays", e.target.value)} /></Field>
        <Field label="Lương CB"><Input type="number" value={f.baseSalary} onChange={(e) => set("baseSalary", e.target.value)} /></Field>
        <Field label="Phụ cấp"><Input type="number" value={f.allowance} onChange={(e) => set("allowance", e.target.value)} /></Field>
        <Field label="Tăng ca (₫)"><Input type="number" value={f.overtimePay} onChange={(e) => set("overtimePay", e.target.value)} /></Field>
        <Field label="Khấu trừ khác"><Input type="number" value={f.deductions} onChange={(e) => set("deductions", e.target.value)} /></Field>
      </div>
      {penaltyTotal > 0 && (
        <div className="mb-4 rounded-xl border border-red-500/30 bg-red-500/5 p-3 text-sm">
          <div className="flex items-center justify-between font-semibold text-red-600">
            <span>Khấu trừ do phạt (kỳ {f.period})</span>
            <span>{moneyVnd(penaltyTotal)}</span>
          </div>
          <ul className="mt-1.5 space-y-0.5 text-xs text-[var(--text-secondary)]">
            {penalty?.items.map((it) => (
              <li key={it.penaltyNo} className="flex items-center justify-between gap-2">
                <span className="truncate">{it.penaltyNo}{it.installments > 1 ? ` · đợt ${it.installmentNo}/${it.installments}` : ""}{it.reason ? ` · ${it.reason}` : ""}</span>
                <span className="whitespace-nowrap">{moneyVnd(it.monthAmount)}</span>
              </li>
            ))}
          </ul>
          <div className="mt-2 flex items-center justify-between border-t border-red-500/20 pt-2 font-semibold text-[var(--text)]">
            <span>Tổng khấu trừ</span>
            <span>{moneyVnd(totalDeductions)}</span>
          </div>
        </div>
      )}
      <div className="mb-4 flex items-center justify-between">
        <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
          <input type="checkbox" checked={f.published} onChange={(e) => set("published", e.target.checked)} /> Phát hành cho nhân viên
        </label>
        <Button onClick={add}><Plus className="h-4 w-4" /> Lập phiếu</Button>
      </div>
      <Table<Payslip> loading={loading} rows={data ?? []} keyOf={(r) => r.id} empty="Chưa có phiếu lương"
        columns={[
          { header: "Kỳ", cell: (r) => r.period },
          { header: "Thực nhận", align: "right", cell: (r) => <span className="font-bold text-emerald-600">{moneyVnd(r.netPay)}</span> },
          { header: "Trạng thái", cell: (r) => <Badge color={r.published ? "success" : "muted"}>{r.published ? "Đã phát hành" : "Nháp"}</Badge> },
          { header: "", align: "right", cell: (r) => <button onClick={() => del(r.id)} className="rounded-lg p-2 text-red-600 hover:bg-red-500/10"><Trash2 className="h-4 w-4" /></button> },
        ]} />
    </div>
  );
}

function LeaveAdmin({ empId }: { empId: string }) {
  const { notify } = useAppNotifications();
  const { data, loading, reload } = useApi<LeaveBalance[]>(`/api/hr/employees/${empId}/leave-balances`, [empId]);
  const [f, setF] = useState({ year: String(new Date().getFullYear()), leaveType: "annual", totalDays: "12", usedDays: "0" });
  const set = (k: keyof typeof f, v: string) => setF((s) => ({ ...s, [k]: v }));
  const save = async () => {
    try {
      await api.post(`/api/hr/employees/${empId}/leave-balances`, {
        year: Number(f.year), leaveType: f.leaveType, totalDays: Number(f.totalDays) || 0, usedDays: Number(f.usedDays) || 0,
      });
      reload({ silent: true });
      notify.success("Đã cập nhật số phép.");
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi."); }
  };
  return (
    <div>
      <div className="mb-4 grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Field label="Năm"><Input type="number" value={f.year} onChange={(e) => set("year", e.target.value)} /></Field>
        <Field label="Loại phép">
          <Select value={f.leaveType} onChange={(e) => set("leaveType", e.target.value)} className="w-full">
            <option value="annual">Phép năm</option>
            <option value="sick">Nghỉ ốm</option>
            <option value="unpaid">Không lương</option>
          </Select>
        </Field>
        <Field label="Tổng ngày"><Input type="number" value={f.totalDays} onChange={(e) => set("totalDays", e.target.value)} /></Field>
        <Field label="Đã dùng"><Input type="number" value={f.usedDays} onChange={(e) => set("usedDays", e.target.value)} /></Field>
      </div>
      <div className="mb-4 flex justify-end"><Button onClick={save}>Lưu số phép</Button></div>
      <Table<LeaveBalance> loading={loading} rows={data ?? []} keyOf={(r) => r.id} empty="Chưa thiết lập"
        columns={[
          { header: "Năm", cell: (r) => r.year },
          { header: "Loại", cell: (r) => leaveTypeLabel(r.leaveType) },
          { header: "Tổng", align: "right", cell: (r) => `${r.totalDays} ngày` },
          { header: "Còn lại", align: "right", cell: (r) => <Badge color={r.remainingDays > 0 ? "success" : "muted"}>{r.remainingDays} ngày</Badge> },
        ]} />
    </div>
  );
}

function DocsAdmin({ empId }: { empId: string }) {
  const { notify } = useAppNotifications();
  const { data, loading, reload } = useApi<EmployeeDoc[]>(`/api/hr/employees/${empId}/documents`, [empId]);
  const [f, setF] = useState({ docType: "certificate", title: "", issuedBy: "", issuedDate: "" });
  const set = (k: keyof typeof f, v: string) => setF((s) => ({ ...s, [k]: v }));
  const add = async () => {
    if (!f.title.trim()) { notify.error("Nhập tên bằng cấp/chứng chỉ."); return; }
    try {
      await api.post(`/api/hr/employees/${empId}/documents`, { ...f, issuedDate: f.issuedDate || null, fileUrl: "", note: "" });
      setF({ docType: "certificate", title: "", issuedBy: "", issuedDate: "" });
      reload({ silent: true });
      notify.success("Đã thêm.");
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi."); }
  };
  const del = async (id: string) => { await api.del(`/api/hr/documents/${id}`); reload({ silent: true }); };
  return (
    <div>
      <div className="mb-4 grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Field label="Loại">
          <Select value={f.docType} onChange={(e) => set("docType", e.target.value)} className="w-full">
            <option value="degree">Bằng cấp</option>
            <option value="certificate">Chứng chỉ</option>
            <option value="reward">Khen thưởng</option>
          </Select>
        </Field>
        <Field label="Tên"><Input value={f.title} onChange={(e) => set("title", e.target.value)} /></Field>
        <Field label="Nơi cấp"><Input value={f.issuedBy} onChange={(e) => set("issuedBy", e.target.value)} /></Field>
        <Field label="Ngày cấp"><Input type="date" value={f.issuedDate} onChange={(e) => set("issuedDate", e.target.value)} /></Field>
      </div>
      <div className="mb-4 flex justify-end"><Button onClick={add}><Plus className="h-4 w-4" /> Thêm</Button></div>
      <Table<EmployeeDoc> loading={loading} rows={data ?? []} keyOf={(r) => r.id} empty="Chưa có"
        columns={[
          { header: "Loại", cell: (r) => <Badge color="purple">{docTypeLabel(r.docType)}</Badge> },
          { header: "Tên", cell: (r) => r.title },
          { header: "Nơi cấp", cell: (r) => r.issuedBy || "—" },
          { header: "", align: "right", cell: (r) => <button onClick={() => del(r.id)} className="rounded-lg p-2 text-red-600 hover:bg-red-500/10"><Trash2 className="h-4 w-4" /></button> },
        ]} />
    </div>
  );
}

// ---------------- Phòng ban ----------------
function DepartmentsTab() {
  const { notify, confirm } = useAppNotifications();
  const { data, loading, reload } = useApi<Department[]>("/api/hr/departments");
  const { data: employees } = useApi<EmployeeCard[]>("/api/hr/employees");
  const [edit, setEdit] = useState<Department | "new" | null>(null);

  const remove = async (r: Department) => {
    const ok = await confirm({ title: `Xóa phòng ban ${r.name}?`, description: "Nhân viên thuộc phòng ban này sẽ được gỡ liên kết.", confirmLabel: "Xóa", tone: "danger" });
    if (!ok) return;
    await api.del(`/api/hr/departments/${r.id}`);
    reload({ silent: true });
    notify.success("Đã xóa.");
  };

  return (
    <GlassPanel strong className="overflow-hidden rounded-[20px]">
      {toolbar("Phòng ban", () => setEdit("new"), "Thêm phòng ban")}
      <Table<Department> loading={loading} rows={data ?? []} keyOf={(r) => r.id} empty="Chưa có phòng ban"
        columns={[
          { header: "Mã", cell: (r) => <span className="font-mono text-xs">{r.code || "—"}</span> },
          { header: "Tên", cell: (r) => (
            <span className="inline-flex items-center gap-1.5 font-semibold">
              {r.name}
              {r.isAccounting && <Badge color="success">Kế toán</Badge>}
            </span>
          ) },
          { header: "Trực thuộc", cell: (r) => r.parentName || "—" },
          { header: "Trưởng phòng", cell: (r) => r.managerName || "—" },
          { header: "Nhân sự", align: "right", cell: (r) => <Badge>{r.employeeCount}</Badge> },
          { header: "", align: "right", cell: (r) => (
            <div className="flex justify-end gap-1.5">
              <button onClick={() => setEdit(r)} className="rounded-lg p-2 text-[var(--accent)] hover:bg-[var(--accent-soft)]"><Pencil className="h-4 w-4" /></button>
              <button onClick={() => remove(r)} className="rounded-lg p-2 text-red-600 hover:bg-red-500/10"><Trash2 className="h-4 w-4" /></button>
            </div>
          ) },
        ]} />
      {edit && (
        <DepartmentModal value={edit === "new" ? null : edit} departments={data ?? []} employees={employees ?? []}
          onClose={() => setEdit(null)} onSaved={() => { setEdit(null); reload({ silent: true }); notify.success("Đã lưu."); }} />
      )}
    </GlassPanel>
  );
}

function DepartmentModal({ value, departments, employees, onClose, onSaved }: {
  value: Department | null; departments: Department[]; employees: EmployeeCard[]; onClose: () => void; onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const [f, setF] = useState({ code: value?.code ?? "", name: value?.name ?? "", parentId: value?.parentId ?? "", managerEmployeeId: value?.managerEmployeeId ?? "" });
  const [saving, setSaving] = useState(false);
  const set = (k: keyof typeof f, v: string) => setF((s) => ({ ...s, [k]: v }));
  const save = async () => {
    if (!f.name.trim()) { notify.error("Nhập tên phòng ban."); return; }
    setSaving(true);
    try {
      const body = { code: f.code, name: f.name, parentId: f.parentId || null, managerEmployeeId: f.managerEmployeeId || null };
      if (value) await api.put(`/api/hr/departments/${value.id}`, body);
      else await api.post("/api/hr/departments", body);
      onSaved();
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi."); } finally { setSaving(false); }
  };
  return (
    <Modal open onClose={onClose} title={value ? "Sửa phòng ban" : "Thêm phòng ban"} panel
      footer={<><Button variant="ghost" onClick={onClose}>Hủy</Button><Button onClick={save} loading={saving}>Lưu</Button></>}>
      <div className="space-y-3">
        <Field label="Mã phòng ban"><Input value={f.code} onChange={(e) => set("code", e.target.value)} /></Field>
        <Field label="Tên phòng ban *"><Input value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <Field label="Trực thuộc">
          <Select value={f.parentId} onChange={(e) => set("parentId", e.target.value)} className="w-full">
            <option value="">— Không —</option>
            {departments.filter((d) => d.id !== value?.id).map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </Select>
        </Field>
        <Field label="Trưởng phòng">
          <Select value={f.managerEmployeeId} onChange={(e) => set("managerEmployeeId", e.target.value)} className="w-full">
            <option value="">— Không —</option>
            {employees.map((e) => <option key={e.id} value={e.id}>{e.fullName}</option>)}
          </Select>
        </Field>
        {value?.isAccounting && (
          <div className="flex items-start gap-2.5 rounded-xl border border-emerald-500/30 bg-emerald-500/5 p-3">
            <Badge color="success">Phòng kế toán</Badge>
            <span className="text-xs text-[var(--text-secondary)]">Phòng do hệ thống quản lý: nhân viên phòng này được lập/duyệt phiếu chi tiền mặt & hoàn tiền phạt. Cờ này không chỉnh trong biểu mẫu.</span>
          </div>
        )}
      </div>
    </Modal>
  );
}

// ---------------- Địa điểm / chi nhánh ----------------
function LocationsTab() {
  const { notify, confirm } = useAppNotifications();
  const { data, loading, reload } = useApi<Location[]>("/api/hr/locations");
  const [edit, setEdit] = useState<Location | "new" | null>(null);

  const remove = async (r: Location) => {
    const ok = await confirm({ title: `Xóa địa điểm ${r.name}?`, description: "Nhân viên thuộc địa điểm này sẽ được gỡ liên kết.", confirmLabel: "Xóa", tone: "danger" });
    if (!ok) return;
    await api.del(`/api/hr/locations/${r.id}`);
    reload({ silent: true });
    notify.success("Đã xóa.");
  };

  return (
    <GlassPanel strong className="overflow-hidden rounded-[20px]">
      {toolbar("Địa điểm / chi nhánh", () => setEdit("new"), "Thêm địa điểm")}
      <Table<Location> loading={loading} rows={data ?? []} keyOf={(r) => r.id} empty="Chưa có địa điểm"
        columns={[
          { header: "Mã", cell: (r) => <span className="font-mono text-xs">{r.code || "—"}</span> },
          { header: "Tên", cell: (r) => <span className="font-semibold">{r.name}</span> },
          { header: "Địa chỉ", cell: (r) => <span className="text-[var(--text-secondary)]">{r.address || "—"}</span> },
          { header: "Nhân sự", align: "right", cell: (r) => <Badge>{r.employeeCount}</Badge> },
          { header: "", align: "right", cell: (r) => (
            <div className="flex justify-end gap-1.5">
              <button onClick={() => setEdit(r)} className="rounded-lg p-2 text-[var(--accent)] hover:bg-[var(--accent-soft)]"><Pencil className="h-4 w-4" /></button>
              <button onClick={() => remove(r)} className="rounded-lg p-2 text-red-600 hover:bg-red-500/10"><Trash2 className="h-4 w-4" /></button>
            </div>
          ) },
        ]} />
      {edit && (
        <LocationModal value={edit === "new" ? null : edit}
          onClose={() => setEdit(null)} onSaved={() => { setEdit(null); reload({ silent: true }); notify.success("Đã lưu."); }} />
      )}
    </GlassPanel>
  );
}

function LocationModal({ value, onClose, onSaved }: { value: Location | null; onClose: () => void; onSaved: () => void }) {
  const { notify } = useAppNotifications();
  const [f, setF] = useState({ code: value?.code ?? "", name: value?.name ?? "", address: value?.address ?? "" });
  const [saving, setSaving] = useState(false);
  const set = (k: keyof typeof f, v: string) => setF((s) => ({ ...s, [k]: v }));
  const save = async () => {
    if (!f.name.trim()) { notify.error("Nhập tên địa điểm."); return; }
    setSaving(true);
    try {
      if (value) await api.put(`/api/hr/locations/${value.id}`, f);
      else await api.post("/api/hr/locations", f);
      onSaved();
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi."); } finally { setSaving(false); }
  };
  return (
    <Modal open onClose={onClose} title={value ? "Sửa địa điểm" : "Thêm địa điểm"} panel
      footer={<><Button variant="ghost" onClick={onClose}>Hủy</Button><Button onClick={save} loading={saving}>Lưu</Button></>}>
      <div className="space-y-3">
        <Field label="Mã địa điểm"><Input value={f.code} onChange={(e) => set("code", e.target.value)} /></Field>
        <Field label="Tên địa điểm *"><Input value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <Field label="Địa chỉ"><Input value={f.address} onChange={(e) => set("address", e.target.value)} /></Field>
      </div>
    </Modal>
  );
}

// ---------------- Ngày nghỉ ----------------
function HolidaysTab() {
  const { notify, confirm } = useAppNotifications();
  const [month, setMonth] = useState(currentMonthKey());
  const range = monthRange(month);
  const { data, loading, reload } = useApi<Holiday[]>(`/api/shifts/holidays?from=${range.from}&to=${range.to}`, [range.from, range.to]);
  const [f, setF] = useState({ holidayDate: range.from, holidayType: "public", name: "", note: "" });
  const set = (k: keyof typeof f, v: string) => setF((s) => ({ ...s, [k]: v }));

  const save = async () => {
    if (!f.holidayDate) { notify.error("Chọn ngày nghỉ."); return; }
    try {
      await api.post("/api/shifts/holidays", {
        holidayDate: f.holidayDate,
        holidayType: f.holidayType,
        name: f.name.trim(),
        note: f.note.trim(),
      });
      setF((s) => ({ ...s, name: "", note: "" }));
      reload({ silent: true });
      notify.success("Đã lưu ngày nghỉ.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được ngày nghỉ.");
    }
  };

  const remove = async (r: Holiday) => {
    const ok = await confirm({
      title: `Xóa ${r.name || "ngày nghỉ"}?`,
      description: `${date(r.holidayDate)} · ${holidayTypeLabel(r.holidayType)}`,
      confirmLabel: "Xóa",
      tone: "danger",
    });
    if (!ok) return;
    await api.del(`/api/shifts/holidays/${r.id}`);
    reload({ silent: true });
    notify.success("Đã xóa ngày nghỉ.");
  };

  return (
    <div className="space-y-4">
      <GlassPanel strong className="rounded-[20px] p-5">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <h2 className="font-bold text-[var(--text)]">Lịch nghỉ nhà nước & công ty</h2>
          <Field label="Tháng hiển thị">
            <Input
              type="month"
              value={month}
              onChange={(e) => {
                const next = e.target.value;
                setMonth(next);
                setF((s) => ({ ...s, holidayDate: monthRange(next).from }));
              }}
              className="w-auto"
            />
          </Field>
        </div>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-5">
          <Field label="Ngày nghỉ">
            <Input type="date" value={f.holidayDate} onChange={(e) => set("holidayDate", e.target.value)} />
          </Field>
          <Field label="Loại lịch">
            <Select value={f.holidayType} onChange={(e) => set("holidayType", e.target.value)} className="w-full">
              <option value="public">Lịch nhà nước</option>
              <option value="company">Nghỉ công ty</option>
            </Select>
          </Field>
          <div className="sm:col-span-2">
            <Field label="Tên ngày nghỉ">
              <Input value={f.name} onChange={(e) => set("name", e.target.value)} placeholder="Ví dụ: Quốc khánh, nghỉ du lịch công ty" />
            </Field>
          </div>
          <div className="flex items-end">
            <Button onClick={save} className="w-full"><Plus className="h-4 w-4" /> Thêm / cập nhật</Button>
          </div>
          <div className="sm:col-span-5">
            <Field label="Ghi chú">
              <Input value={f.note} onChange={(e) => set("note", e.target.value)} placeholder="Thông tin thêm nếu cần" />
            </Field>
          </div>
        </div>
      </GlassPanel>

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        <div className="border-b border-[var(--gc-border)] px-5 py-4">
          <h2 className="font-bold text-[var(--text)]">Danh sách ngày nghỉ trong tháng</h2>
        </div>
        <Table<Holiday> loading={loading} rows={data ?? []} keyOf={(r) => r.id} empty="Chưa có ngày nghỉ trong tháng này"
          columns={[
            { header: "Ngày", cell: (r) => <span className="font-semibold">{date(r.holidayDate)}</span> },
            { header: "Loại", cell: (r) => <Badge color={r.holidayType === "public" ? "purple" : "accent"}>{holidayTypeLabel(r.holidayType)}</Badge> },
            { header: "Tên ngày nghỉ", cell: (r) => <span>{r.name || "—"}</span> },
            { header: "Ghi chú", cell: (r) => <span className="text-[var(--text-secondary)]">{r.note || "—"}</span> },
            { header: "", align: "right", cell: (r) => <button onClick={() => remove(r)} className="rounded-lg p-2 text-red-600 hover:bg-red-500/10"><Trash2 className="h-4 w-4" /></button> },
          ]} />
      </GlassPanel>
    </div>
  );
}

// ---------------- Ca làm ----------------
function ShiftsTab() {
  const { notify, confirm } = useAppNotifications();
  const { data, loading, reload } = useApi<Shift[]>("/api/shifts");
  const [edit, setEdit] = useState<Shift | "new" | null>(null);
  const remove = async (r: Shift) => {
    const ok = await confirm({ title: `Xóa ca ${r.name}?`, description: "Các phân ca dùng ca này cũng sẽ bị xóa.", confirmLabel: "Xóa", tone: "danger" });
    if (!ok) return;
    await api.del(`/api/shifts/${r.id}`); reload({ silent: true }); notify.success("Đã xóa.");
  };
  return (
    <GlassPanel strong className="overflow-hidden rounded-[20px]">
      {toolbar("Ca làm việc", () => setEdit("new"), "Thêm ca")}
      <Table<Shift> loading={loading} rows={data ?? []} keyOf={(r) => r.id} empty="Chưa có ca"
        columns={[
          { header: "Mã", cell: (r) => <span className="font-mono text-xs">{r.code || "—"}</span> },
          { header: "Tên ca", cell: (r) => <span className="font-semibold">{r.name}</span> },
          { header: "Giờ vào", cell: (r) => <span className="font-mono">{r.startTime}</span> },
          { header: "Giờ ra", cell: (r) => <span className="font-mono">{r.endTime}</span> },
          { header: "Nghỉ trưa", align: "right", cell: (r) => `${r.breakMinutes}′` },
          { header: "Trễ cho phép", align: "right", cell: (r) => `${r.lateGraceMinutes}′` },
          { header: "Giờ chuẩn", align: "right", cell: (r) => `${r.standardHours}h` },
          { header: "", align: "right", cell: (r) => (
            <div className="flex justify-end gap-1.5">
              <button onClick={() => setEdit(r)} className="rounded-lg p-2 text-[var(--accent)] hover:bg-[var(--accent-soft)]"><Pencil className="h-4 w-4" /></button>
              <button onClick={() => remove(r)} className="rounded-lg p-2 text-red-600 hover:bg-red-500/10"><Trash2 className="h-4 w-4" /></button>
            </div>
          ) },
        ]} />
      {edit && <ShiftModal value={edit === "new" ? null : edit} onClose={() => setEdit(null)} onSaved={() => { setEdit(null); reload({ silent: true }); notify.success("Đã lưu."); }} />}
    </GlassPanel>
  );
}

function ShiftModal({ value, onClose, onSaved }: { value: Shift | null; onClose: () => void; onSaved: () => void }) {
  const { notify } = useAppNotifications();
  const [f, setF] = useState({
    code: value?.code ?? "", name: value?.name ?? "", startTime: value?.startTime ?? "08:00", endTime: value?.endTime ?? "17:00",
    breakMinutes: String(value?.breakMinutes ?? 60), lateGraceMinutes: String(value?.lateGraceMinutes ?? 5), standardHours: String(value?.standardHours ?? 8),
  });
  const [saving, setSaving] = useState(false);
  const set = (k: keyof typeof f, v: string) => setF((s) => ({ ...s, [k]: v }));
  const save = async () => {
    if (!f.name.trim()) { notify.error("Nhập tên ca."); return; }
    setSaving(true);
    try {
      const body = { code: f.code, name: f.name, startTime: f.startTime, endTime: f.endTime,
        breakMinutes: Number(f.breakMinutes) || 0, lateGraceMinutes: Number(f.lateGraceMinutes) || 0, standardHours: Number(f.standardHours) || 8, isOvernight: false };
      if (value) await api.put(`/api/shifts/${value.id}`, body);
      else await api.post("/api/shifts", body);
      onSaved();
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi."); } finally { setSaving(false); }
  };
  return (
    <Modal open onClose={onClose} title={value ? "Sửa ca làm" : "Thêm ca làm"} panel
      footer={<><Button variant="ghost" onClick={onClose}>Hủy</Button><Button onClick={save} loading={saving}>Lưu</Button></>}>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Mã ca"><Input value={f.code} onChange={(e) => set("code", e.target.value)} /></Field>
        <Field label="Tên ca *"><Input value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <Field label="Giờ vào"><Input type="time" value={f.startTime} onChange={(e) => set("startTime", e.target.value)} /></Field>
        <Field label="Giờ ra"><Input type="time" value={f.endTime} onChange={(e) => set("endTime", e.target.value)} /></Field>
        <Field label="Nghỉ trưa (phút)"><Input type="number" value={f.breakMinutes} onChange={(e) => set("breakMinutes", e.target.value)} /></Field>
        <Field label="Trễ cho phép (phút)"><Input type="number" value={f.lateGraceMinutes} onChange={(e) => set("lateGraceMinutes", e.target.value)} /></Field>
        <Field label="Giờ công chuẩn"><Input type="number" value={f.standardHours} onChange={(e) => set("standardHours", e.target.value)} /></Field>
      </div>
    </Modal>
  );
}

// ---------------- Phân ca ----------------
function AssignmentsTab() {
  const { notify, confirm } = useAppNotifications();
  const today = new Date();
  const first = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, "0")}-01`;
  const last = new Date(today.getFullYear(), today.getMonth() + 1, 0).toISOString().slice(0, 10);
  const [range, setRange] = useState({ from: first, to: last });
  const { data, loading, reload } = useApi<ShiftAssignment[]>(`/api/shifts/assignments?from=${range.from}&to=${range.to}`, [range.from, range.to]);
  const { data: employees } = useApi<EmployeeCard[]>("/api/hr/employees");
  const { data: shifts } = useApi<Shift[]>("/api/shifts");
  const [f, setF] = useState({ employeeId: "", shiftId: "", workDate: first });
  const set = (k: keyof typeof f, v: string) => setF((s) => ({ ...s, [k]: v }));

  const assign = async () => {
    if (!f.employeeId || !f.shiftId) { notify.error("Chọn nhân viên và ca."); return; }
    try {
      await api.post("/api/shifts/assignments", { employeeId: f.employeeId, shiftId: f.shiftId, workDate: f.workDate, note: "" });
      reload({ silent: true });
      notify.success("Đã phân ca.");
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi."); }
  };
  const del = async (r: ShiftAssignment) => {
    const ok = await confirm({ title: "Hủy phân ca này?", description: "Phân ca sẽ được gỡ khỏi lịch.", confirmLabel: "Hủy", tone: "warning" });
    if (!ok) return;
    await api.del(`/api/shifts/assignments/${r.id}`); reload({ silent: true });
  };

  return (
    <div className="space-y-4">
      <GlassPanel strong className="rounded-[20px] p-5">
        <h2 className="mb-3 font-bold text-[var(--text)]">Phân ca cho nhân viên</h2>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Field label="Nhân viên">
            <Select value={f.employeeId} onChange={(e) => set("employeeId", e.target.value)} className="w-full">
              <option value="">— Chọn —</option>
              {(employees ?? []).map((e) => <option key={e.id} value={e.id}>{e.fullName}</option>)}
            </Select>
          </Field>
          <Field label="Ca làm">
            <Select value={f.shiftId} onChange={(e) => set("shiftId", e.target.value)} className="w-full">
              <option value="">— Chọn —</option>
              {(shifts ?? []).map((s) => <option key={s.id} value={s.id}>{s.name} ({s.startTime}-{s.endTime})</option>)}
            </Select>
          </Field>
          <Field label="Ngày làm"><Input type="date" value={f.workDate} onChange={(e) => set("workDate", e.target.value)} /></Field>
          <div className="flex items-end"><Button onClick={assign} className="w-full"><Plus className="h-4 w-4" /> Phân ca</Button></div>
        </div>
      </GlassPanel>

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--gc-border)] px-5 py-4">
          <h2 className="font-bold text-[var(--text)]">Lịch phân ca</h2>
          <div className="flex items-center gap-2 text-sm">
            <Input type="date" value={range.from} onChange={(e) => setRange((s) => ({ ...s, from: e.target.value }))} className="w-auto" />
            <span className="text-[var(--text-muted)]">→</span>
            <Input type="date" value={range.to} onChange={(e) => setRange((s) => ({ ...s, to: e.target.value }))} className="w-auto" />
          </div>
        </div>
        <Table<ShiftAssignment> loading={loading} rows={data ?? []} keyOf={(r) => r.id} empty="Chưa phân ca trong khoảng này"
          columns={[
            { header: "Ngày", cell: (r) => <span className="font-semibold">{date(r.workDate)}</span> },
            { header: "Nhân viên", cell: (r) => <span>{r.employeeName} <span className="text-xs text-[var(--text-muted)]">({r.employeeCode})</span></span> },
            { header: "Ca", cell: (r) => <span>{r.shiftName}</span> },
            { header: "Giờ", cell: (r) => <span className="font-mono text-xs">{r.startTime}-{r.endTime}</span> },
            { header: "", align: "right", cell: (r) => <button onClick={() => del(r)} className="rounded-lg p-2 text-red-600 hover:bg-red-500/10"><Trash2 className="h-4 w-4" /></button> },
          ]} />
      </GlassPanel>
    </div>
  );
}
