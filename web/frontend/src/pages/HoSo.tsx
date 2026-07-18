import { useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import { Award, BadgeCheck, CalendarDays, FileText, IdCard, Save, Wallet } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Badge, Button, Field, Input } from "../components/ui";
import { Table } from "../components/Table";
import { api } from "../lib/api";
import { date, initials, moneyVnd } from "../lib/format";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/AppNotifications";
import {
  docTypeLabel,
  leaveTypeLabel,
  type Contract,
  type EmployeeDetail,
  type EmployeeDoc,
  type LeaveBalance,
  type Payslip,
} from "../lib/hr";

type Tab = "info" | "card" | "contract" | "payslip" | "leave" | "docs";

const TABS: { key: Tab; label: string; icon: React.ReactNode }[] = [
  { key: "info", label: "Thông tin", icon: <IdCard className="h-4 w-4" /> },
  { key: "card", label: "Thẻ nhân viên", icon: <BadgeCheck className="h-4 w-4" /> },
  { key: "contract", label: "Hợp đồng", icon: <FileText className="h-4 w-4" /> },
  { key: "payslip", label: "Phiếu lương", icon: <Wallet className="h-4 w-4" /> },
  { key: "leave", label: "Ngày phép", icon: <CalendarDays className="h-4 w-4" /> },
  { key: "docs", label: "Bằng cấp", icon: <Award className="h-4 w-4" /> },
];

export function HoSo() {
  const { data: me, loading, reload } = useApi<EmployeeDetail>("/api/hr/me");
  const [tab, setTab] = useState<Tab>("info");

  if (loading && !me) {
    return (
      <div className="gc-root">
        <PageHeader title="Hồ sơ của tôi" subtitle="Thông tin cá nhân, hợp đồng, lương và quyền lợi" />
        <GlassPanel strong className="rounded-[20px] p-10 text-center text-sm text-[var(--text-muted)]">Đang tải hồ sơ…</GlassPanel>
      </div>
    );
  }
  if (!me) return null;

  return (
    <div className="gc-root">
      <PageHeader title="Hồ sơ của tôi" subtitle="Thông tin cá nhân, hợp đồng, lương và quyền lợi" />

      <GlassPanel strong className="mb-4 flex flex-col items-center gap-4 rounded-[20px] p-5 sm:flex-row sm:items-center">
        <span className="grid h-20 w-20 shrink-0 place-items-center overflow-hidden rounded-2xl text-2xl font-bold text-white"
          style={{ background: "linear-gradient(135deg, var(--accent), var(--purple))" }}>
          {me.avatar ? <img src={me.avatar} alt="" className="h-full w-full object-cover" /> : initials(me.fullName)}
        </span>
        <div className="min-w-0 flex-1 text-center sm:text-left">
          <div className="flex flex-wrap items-center justify-center gap-2 sm:justify-start">
            <h2 className="text-xl font-bold text-[var(--text)]">{me.fullName}</h2>
            <Badge color={me.status === "Active" ? "success" : "muted"}>{me.status === "Active" ? "Đang làm việc" : me.status}</Badge>
          </div>
          <div className="mt-1 text-sm text-[var(--text-secondary)]">
            {me.position || "Chưa cập nhật chức vụ"}{me.departmentName ? ` · ${me.departmentName}` : ""}
          </div>
          <div className="mt-1 font-mono text-xs text-[var(--accent)]">{me.employeeCode}</div>
        </div>
      </GlassPanel>

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

      {tab === "info" && <InfoTab me={me} onSaved={() => reload({ silent: true })} />}
      {tab === "card" && <CardTab me={me} />}
      {tab === "contract" && <ContractTab empId={me.id} />}
      {tab === "payslip" && <PayslipTab empId={me.id} />}
      {tab === "leave" && <LeaveTab empId={me.id} />}
      {tab === "docs" && <DocsTab empId={me.id} />}
    </div>
  );
}

function InfoTab({ me, onSaved }: { me: EmployeeDetail; onSaved: () => void }) {
  const { notify } = useAppNotifications();
  const [form, setForm] = useState({ phone: me.phone, email: me.email, address: me.address, dob: me.dob ?? "", gender: me.gender });
  const [saving, setSaving] = useState(false);
  const set = (k: keyof typeof form, v: string) => setForm((s) => ({ ...s, [k]: v }));

  const save = async () => {
    setSaving(true);
    try {
      await api.put(`/api/hr/employees/${me.id}`, {
        employeeCode: me.employeeCode,
        username: me.username,
        fullName: me.fullName,
        departmentId: me.departmentId ?? null,
        position: me.position ?? "",
        managerId: me.managerId ?? null,
        hireDate: me.hireDate ?? null,
        status: me.status ?? "Active",
        avatar: me.avatar ?? null,
        ...form,
      });
      notify.success("Đã lưu thông tin liên hệ.");
      onSaved();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <GlassPanel strong className="rounded-[20px] p-5">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <ReadOnly label="Mã nhân viên" value={me.employeeCode} />
        <ReadOnly label="Tài khoản" value={me.username || "—"} />
        <ReadOnly label="Phòng ban" value={me.departmentName || "—"} />
        <ReadOnly label="Chức vụ" value={me.position || "—"} />
        <ReadOnly label="Quản lý trực tiếp" value={me.managerName || "—"} />
        <ReadOnly label="Ngày vào làm" value={me.hireDate ? date(me.hireDate) : "—"} />
        <Field label="Ngày sinh"><Input type="date" value={form.dob ?? ""} onChange={(e) => set("dob", e.target.value)} /></Field>
        <Field label="Giới tính"><Input value={form.gender} onChange={(e) => set("gender", e.target.value)} placeholder="Nam / Nữ" /></Field>
        <Field label="Số điện thoại"><Input value={form.phone} onChange={(e) => set("phone", e.target.value)} /></Field>
        <Field label="Email"><Input value={form.email} onChange={(e) => set("email", e.target.value)} /></Field>
        <div className="sm:col-span-2">
          <Field label="Địa chỉ"><Input value={form.address} onChange={(e) => set("address", e.target.value)} /></Field>
        </div>
      </div>
      <div className="mt-4 flex justify-end">
        <Button onClick={save} loading={saving}><Save className="h-4 w-4" /> Lưu thông tin</Button>
      </div>
    </GlassPanel>
  );
}

function ReadOnly({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span className="mb-1.5 block text-xs font-semibold text-[var(--text-secondary)]">{label}</span>
      <div className="rounded-xl border border-[var(--glass-border)] bg-black/5 px-3.5 py-2.5 text-sm text-[var(--text)] dark:bg-white/5">{value}</div>
    </div>
  );
}

function CardTab({ me }: { me: EmployeeDetail }) {
  const qr = JSON.stringify({ code: me.employeeCode, id: me.id, name: me.fullName });
  return (
    <div className="flex justify-center">
      <div className="w-full max-w-sm overflow-hidden rounded-3xl shadow-2xl"
        style={{ background: "linear-gradient(150deg, var(--accent), var(--purple))" }}>
        <div className="flex items-center justify-between px-5 py-4 text-white">
          <span className="text-sm font-bold tracking-wide">THẺ NHÂN VIÊN</span>
          <IdCard className="h-6 w-6 opacity-90" />
        </div>
        <div className="mx-4 mb-4 rounded-2xl bg-white p-5">
          <div className="flex items-center gap-4">
            <span className="grid h-16 w-16 shrink-0 place-items-center overflow-hidden rounded-2xl text-xl font-bold text-white"
              style={{ background: "linear-gradient(135deg, var(--accent), var(--purple))" }}>
              {me.avatar ? <img src={me.avatar} alt="" className="h-full w-full object-cover" /> : initials(me.fullName)}
            </span>
            <div className="min-w-0">
              <div className="truncate text-lg font-bold text-slate-800">{me.fullName}</div>
              <div className="truncate text-sm text-slate-500">{me.position || "Nhân viên"}</div>
              <div className="mt-0.5 font-mono text-xs font-bold text-[var(--accent)]">{me.employeeCode}</div>
            </div>
          </div>
          <div className="mt-4 flex items-end justify-between gap-3">
            <dl className="space-y-1 text-xs text-slate-600">
              <div><dt className="inline text-slate-400">Phòng ban: </dt><dd className="inline font-medium">{me.departmentName || "—"}</dd></div>
              <div><dt className="inline text-slate-400">Vào làm: </dt><dd className="inline font-medium">{me.hireDate ? date(me.hireDate) : "—"}</dd></div>
            </dl>
            <div className="rounded-lg bg-white p-1 ring-1 ring-slate-200">
              <QRCodeSVG value={qr} size={116} level="M" marginSize={4} />
            </div>
          </div>
        </div>
        <div className="px-5 pb-4 text-center text-[0.7rem] text-white/80">Quét mã QR để nhận dạng &amp; chấm công</div>
      </div>
    </div>
  );
}

function Panel({ children }: { children: React.ReactNode }) {
  return <GlassPanel strong className="overflow-hidden rounded-[20px]">{children}</GlassPanel>;
}

function ContractTab({ empId }: { empId: string }) {
  const { data, loading } = useApi<Contract[]>(`/api/hr/employees/${empId}/contracts`, [empId]);
  return (
    <Panel>
      <Table<Contract>
        loading={loading}
        rows={data ?? []}
        keyOf={(r) => r.id}
        empty="Chưa có hợp đồng"
        columns={[
          { header: "Số HĐ", cell: (r) => <span className="font-semibold">{r.contractNo || "—"}</span> },
          { header: "Loại", cell: (r) => <span>{r.contractType || "—"}</span> },
          { header: "Từ ngày", cell: (r) => <span>{r.startDate ? date(r.startDate) : "—"}</span> },
          { header: "Đến ngày", cell: (r) => <span>{r.endDate ? date(r.endDate) : "Không thời hạn"}</span> },
          { header: "Lương cơ bản", align: "right", cell: (r) => <span className="font-medium">{moneyVnd(r.baseSalary)}</span> },
          { header: "Trạng thái", align: "right", cell: (r) => <Badge color={r.status === "Active" ? "success" : "muted"}>{r.status === "Active" ? "Hiệu lực" : r.status}</Badge> },
        ]}
      />
    </Panel>
  );
}

function PayslipTab({ empId }: { empId: string }) {
  const { data, loading } = useApi<Payslip[]>(`/api/hr/employees/${empId}/payslips`, [empId]);
  return (
    <Panel>
      <Table<Payslip>
        loading={loading}
        rows={data ?? []}
        keyOf={(r) => r.id}
        empty="Chưa có phiếu lương được phát hành"
        columns={[
          { header: "Kỳ lương", cell: (r) => <span className="font-semibold">{r.period}</span> },
          { header: "Ngày công", align: "right", cell: (r) => <span>{r.workDays}</span> },
          { header: "Lương CB", align: "right", cell: (r) => <span>{moneyVnd(r.baseSalary)}</span> },
          { header: "Phụ cấp", align: "right", cell: (r) => <span>{moneyVnd(r.allowance)}</span> },
          { header: "Tăng ca", align: "right", cell: (r) => <span>{moneyVnd(r.overtimePay)}</span> },
          { header: "Khấu trừ", align: "right", cell: (r) => <span className="text-red-600">-{moneyVnd(r.deductions)}</span> },
          { header: "Thực nhận", align: "right", cell: (r) => <span className="font-bold text-emerald-600">{moneyVnd(r.netPay)}</span> },
        ]}
      />
    </Panel>
  );
}

function LeaveTab({ empId }: { empId: string }) {
  const { data, loading } = useApi<LeaveBalance[]>(`/api/hr/employees/${empId}/leave-balances`, [empId]);
  return (
    <Panel>
      <Table<LeaveBalance>
        loading={loading}
        rows={data ?? []}
        keyOf={(r) => r.id}
        empty="Chưa thiết lập số ngày phép"
        columns={[
          { header: "Năm", cell: (r) => <span className="font-semibold">{r.year}</span> },
          { header: "Loại phép", cell: (r) => <span>{leaveTypeLabel(r.leaveType)}</span> },
          { header: "Tổng", align: "right", cell: (r) => <span>{r.totalDays} ngày</span> },
          { header: "Đã dùng", align: "right", cell: (r) => <span className="text-amber-600">{r.usedDays} ngày</span> },
          { header: "Còn lại", align: "right", cell: (r) => <Badge color={r.remainingDays > 0 ? "success" : "muted"}>{r.remainingDays} ngày</Badge> },
        ]}
      />
    </Panel>
  );
}

function DocsTab({ empId }: { empId: string }) {
  const { data, loading } = useApi<EmployeeDoc[]>(`/api/hr/employees/${empId}/documents`, [empId]);
  return (
    <Panel>
      <Table<EmployeeDoc>
        loading={loading}
        rows={data ?? []}
        keyOf={(r) => r.id}
        empty="Chưa có bằng cấp / chứng chỉ / khen thưởng"
        columns={[
          { header: "Loại", cell: (r) => <Badge color="purple">{docTypeLabel(r.docType)}</Badge> },
          { header: "Tên", cell: (r) => <span className="font-semibold">{r.title}</span> },
          { header: "Nơi cấp", cell: (r) => <span className="text-[var(--text-secondary)]">{r.issuedBy || "—"}</span> },
          { header: "Ngày cấp", align: "right", cell: (r) => <span>{r.issuedDate ? date(r.issuedDate) : "—"}</span> },
        ]}
      />
    </Panel>
  );
}
