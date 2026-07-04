import { useState } from "react";
import { Building2, CalendarDays, CalendarRange, Clock, Gift, MapPin, Pencil, Plus, Trash2, Users } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Modal } from "../components/Modal";
import { Badge, Button, Field, Input, Select } from "../components/ui";
import { Table } from "../components/Table";
import { api } from "../lib/api";
import { date, moneyVnd } from "../lib/format";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/AppNotifications";
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

type Tab = "employees" | "departments" | "locations" | "shifts" | "assignments" | "holidays";
const TABS: { key: Tab; label: string; icon: React.ReactNode }[] = [
  { key: "employees", label: "Nhân viên", icon: <Users className="h-4 w-4" /> },
  { key: "departments", label: "Phòng ban", icon: <Building2 className="h-4 w-4" /> },
  { key: "locations", label: "Địa điểm", icon: <MapPin className="h-4 w-4" /> },
  { key: "shifts", label: "Ca làm", icon: <Clock className="h-4 w-4" /> },
  { key: "assignments", label: "Phân ca", icon: <CalendarRange className="h-4 w-4" /> },
  { key: "holidays", label: "Ngày nghỉ", icon: <CalendarDays className="h-4 w-4" /> },
];

export function QuanLyNhanSu() {
  const [tab, setTab] = useState<Tab>("employees");
  return (
    <div className="gc-root">
      <PageHeader title="Quản lý nhân sự" subtitle="Hồ sơ nhân viên, phòng ban, ca làm việc và phân ca" />
      <div className="mb-4 flex flex-wrap gap-2">
        {TABS.map((t) => (
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
    </div>
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
function EmployeesTab() {
  const { notify, confirm } = useAppNotifications();
  const { data, loading, reload } = useApi<EmployeeCard[]>("/api/hr/employees");
  const { data: departments } = useApi<Department[]>("/api/hr/departments");
  const { data: locations } = useApi<Location[]>("/api/hr/locations");
  const [edit, setEdit] = useState<EmployeeCard | "new" | null>(null);
  const [benefits, setBenefits] = useState<EmployeeCard | null>(null);

  const remove = async (r: EmployeeCard) => {
    const ok = await confirm({ title: `Xóa hồ sơ ${r.fullName}?`, description: "Toàn bộ hợp đồng, phiếu lương, phép của nhân viên này sẽ bị xóa.", confirmLabel: "Xóa", tone: "danger" });
    if (!ok) return;
    try {
      await api.del(`/api/hr/employees/${r.id}`);
      notify.success("Đã xóa hồ sơ.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được.");
    }
  };

  return (
    <GlassPanel strong className="overflow-hidden rounded-[20px]">
      {toolbar("Danh sách nhân viên", () => setEdit("new"), "Thêm nhân viên")}
      <Table<EmployeeCard>
        loading={loading}
        rows={data ?? []}
        keyOf={(r) => r.id}
        empty="Chưa có nhân viên"
        columns={[
          { header: "Mã", cell: (r) => <span className="font-mono text-xs font-bold text-[var(--accent)]">{r.employeeCode}</span> },
          { header: "Họ tên", cell: (r) => <span className="font-semibold">{r.fullName}</span> },
          { header: "Chức vụ", cell: (r) => <span className="text-[var(--text-secondary)]">{r.position || "—"}</span> },
          { header: "Phòng ban", cell: (r) => <span>{r.departmentName || "—"}</span> },
          { header: "Địa điểm", cell: (r) => <span className="text-[var(--text-secondary)]">{r.locationName || "—"}</span> },
          { header: "Phân quyền", cell: (r) => <Badge color={r.accessRole && r.accessRole !== "staff" ? "success" : "muted"}>{accessRoleLabel(r.accessRole)}</Badge> },
          { header: "Trạng thái", cell: (r) => <Badge color={r.status === "Active" ? "success" : "muted"}>{r.status === "Active" ? "Đang làm" : r.status}</Badge> },
          {
            header: "", align: "right",
            cell: (r) => (
              <div className="flex justify-end gap-1.5">
                <button onClick={() => setBenefits(r)} className="rounded-lg p-2 text-violet-600 hover:bg-violet-500/10" title="Quyền lợi"><Gift className="h-4 w-4" /></button>
                <button onClick={() => setEdit(r)} className="rounded-lg p-2 text-[var(--accent)] hover:bg-[var(--accent-soft)]" title="Sửa"><Pencil className="h-4 w-4" /></button>
                <button onClick={() => remove(r)} className="rounded-lg p-2 text-red-600 hover:bg-red-500/10" title="Xóa"><Trash2 className="h-4 w-4" /></button>
              </div>
            ),
          },
        ]}
      />
      {edit && (
        <EmployeeModal
          value={edit === "new" ? null : edit}
          departments={departments ?? []}
          locations={locations ?? []}
          employees={data ?? []}
          onClose={() => setEdit(null)}
          onSaved={() => { setEdit(null); reload({ silent: true }); notify.success("Đã lưu hồ sơ."); }}
        />
      )}
      {benefits && <BenefitsModal emp={benefits} onClose={() => setBenefits(null)} />}
    </GlassPanel>
  );
}

function EmployeeModal({ value, departments, locations, employees, onClose, onSaved }: {
  value: EmployeeCard | null; departments: Department[]; locations: Location[]; employees: EmployeeCard[]; onClose: () => void; onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const [detail] = useState(value);
  const [form, setForm] = useState({
    employeeCode: value?.employeeCode ?? "",
    username: value?.username ?? "",
    fullName: value?.fullName ?? "",
    position: value?.position ?? "",
    departmentId: value?.departmentId ?? "",
    locationId: value?.locationId ?? "",
    accessRole: value?.accessRole ?? "staff",
    status: value?.status ?? "Active",
    phone: value?.phone ?? "",
    email: value?.email ?? "",
    managerId: "",
    hireDate: "",
    dob: "",
    gender: "",
    address: "",
  });
  const [saving, setSaving] = useState(false);
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
      if (detail) await api.put(`/api/hr/employees/${detail.id}`, body);
      else await api.post("/api/hr/employees", body);
      onSaved();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal open onClose={onClose} title={detail ? "Sửa hồ sơ nhân viên" : "Thêm nhân viên"} panel wide
      footer={<><Button variant="ghost" onClick={onClose}>Hủy</Button><Button onClick={save} loading={saving}>Lưu</Button></>}>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Field label="Mã nhân viên"><Input value={form.employeeCode} onChange={(e) => set("employeeCode", e.target.value)} placeholder="Tự sinh nếu để trống" /></Field>
        <Field label="Tài khoản đăng nhập"><Input value={form.username} onChange={(e) => set("username", e.target.value)} placeholder="username (để chấm công/đơn từ)" /></Field>
        <Field label="Họ tên *"><Input value={form.fullName} onChange={(e) => set("fullName", e.target.value)} /></Field>
        <Field label="Chức vụ"><Input value={form.position} onChange={(e) => set("position", e.target.value)} /></Field>
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
        <Field label="Vai trò truy cập (phân quyền)">
          <Select value={form.accessRole} onChange={(e) => set("accessRole", e.target.value)} className="w-full">
            {ACCESS_ROLES.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
          </Select>
        </Field>
        <Field label="Quản lý trực tiếp">
          <Select value={form.managerId} onChange={(e) => set("managerId", e.target.value)} className="w-full">
            <option value="">— Không —</option>
            {employees.filter((e) => e.id !== detail?.id).map((e) => <option key={e.id} value={e.id}>{e.fullName}</option>)}
          </Select>
        </Field>
        <Field label="Ngày vào làm"><Input type="date" value={form.hireDate} onChange={(e) => set("hireDate", e.target.value)} /></Field>
        <Field label="Trạng thái">
          <Select value={form.status} onChange={(e) => set("status", e.target.value)} className="w-full">
            <option value="Active">Đang làm việc</option>
            <option value="Inactive">Đã nghỉ</option>
          </Select>
        </Field>
        <Field label="Số điện thoại"><Input value={form.phone} onChange={(e) => set("phone", e.target.value)} /></Field>
        <Field label="Email"><Input value={form.email} onChange={(e) => set("email", e.target.value)} /></Field>
        <Field label="Ngày sinh"><Input type="date" value={form.dob} onChange={(e) => set("dob", e.target.value)} /></Field>
        <Field label="Giới tính"><Input value={form.gender} onChange={(e) => set("gender", e.target.value)} placeholder="Nam / Nữ" /></Field>
        <div className="sm:col-span-2"><Field label="Địa chỉ"><Input value={form.address} onChange={(e) => set("address", e.target.value)} /></Field></div>
      </div>
      {detail && <p className="mt-3 text-xs text-[var(--text-muted)]">Lưu ý: ngày sinh/giới tính/địa chỉ đầy đủ sẽ ghi đè khi lưu ở chế độ quản trị.</p>}
    </Modal>
  );
}

// ---------------- Quyền lợi (hợp đồng / phiếu lương / phép / bằng cấp) ----------------
function BenefitsModal({ emp, onClose }: { emp: EmployeeCard; onClose: () => void }) {
  const [sub, setSub] = useState<"contract" | "payslip" | "leave" | "docs">("contract");
  const subs = [
    { k: "contract", l: "Hợp đồng" },
    { k: "payslip", l: "Phiếu lương" },
    { k: "leave", l: "Ngày phép" },
    { k: "docs", l: "Bằng cấp" },
  ] as const;
  return (
    <Modal open onClose={onClose} title={`Quyền lợi · ${emp.fullName}`} panel wide footer={<Button variant="ghost" onClick={onClose}>Đóng</Button>}>
      <div className="mb-4 flex flex-wrap gap-2">
        {subs.map((s) => (
          <button key={s.k} onClick={() => setSub(s.k)}
            className={`rounded-full px-3 py-1.5 text-xs font-bold ${sub === s.k ? "bg-[var(--accent)] text-white" : "bg-[var(--accent-soft)] text-[var(--text-secondary)]"}`}>
            {s.l}
          </button>
        ))}
      </div>
      {sub === "contract" && <ContractAdmin empId={emp.id} />}
      {sub === "payslip" && <PayslipAdmin empId={emp.id} />}
      {sub === "leave" && <LeaveAdmin empId={emp.id} />}
      {sub === "docs" && <DocsAdmin empId={emp.id} />}
    </Modal>
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
  // Tiền phạt phải khấu trừ cho kỳ đang lập (tự động cộng vào tổng khấu trừ).
  const { data: penalty } = useApi<PenaltyDeductions>(
    f.period ? `/api/penalties/deductions?employeeId=${empId}&period=${f.period}` : null,
    [empId, f.period],
  );
  const penaltyTotal = penalty?.total ?? 0;
  const totalDeductions = (Number(f.deductions) || 0) + penaltyTotal;
  const add = async () => {
    if (!f.period) { notify.error("Nhập kỳ lương (yyyy-MM)."); return; }
    try {
      await api.post(`/api/hr/employees/${empId}/payslips`, {
        period: f.period, workDays: Number(f.workDays) || 0, overtimeHours: 0,
        baseSalary: Number(f.baseSalary) || 0, allowance: Number(f.allowance) || 0,
        overtimePay: Number(f.overtimePay) || 0, deductions: totalDeductions,
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
          { header: "Tên", cell: (r) => <span className="font-semibold">{r.name}</span> },
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
  const [isAccounting, setIsAccounting] = useState(value?.isAccounting ?? false);
  const [saving, setSaving] = useState(false);
  const set = (k: keyof typeof f, v: string) => setF((s) => ({ ...s, [k]: v }));
  const save = async () => {
    if (!f.name.trim()) { notify.error("Nhập tên phòng ban."); return; }
    setSaving(true);
    try {
      const body = { code: f.code, name: f.name, parentId: f.parentId || null, managerEmployeeId: f.managerEmployeeId || null, isAccounting };
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
        <label className="flex cursor-pointer items-start gap-2.5 rounded-xl border border-[var(--glass-border)] bg-white/40 p-3 dark:bg-white/5">
          <input type="checkbox" checked={isAccounting} onChange={(e) => setIsAccounting(e.target.checked)} className="mt-0.5 h-4 w-4" />
          <span className="text-sm">
            <span className="font-semibold text-[var(--text)]">Phòng Kế toán</span>
            <span className="block text-xs text-[var(--text-secondary)]">Nhân viên phòng này được duyệt khoản chi hoàn tiền phạt.</span>
          </span>
        </label>
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
