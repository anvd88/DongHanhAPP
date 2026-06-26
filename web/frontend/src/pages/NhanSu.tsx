import { useEffect, useState } from "react";
import { Plus, Search, Trash2, Check, Lock, Unlock, KeyRound, UserPlus, Wifi, WifiOff } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassCard } from "../components/Glass";
import { Table } from "../components/Table";
import { Modal } from "../components/Modal";
import { Button, Input, Select, Field, Spinner, Badge } from "../components/ui";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import { date, dateTime } from "../lib/format";
import type { UserAdmin } from "../lib/types";

const ROLES = [
  { key: "", label: "Tất cả vai trò" },
  { key: "Admin", label: "Admin" },
  { key: "User", label: "User" },
  { key: "Pending", label: "Chờ duyệt" },
  { key: "Locked", label: "Đã khóa" },
];

export function NhanSu() {
  const [search, setSearch] = useState("");
  const [role, setRole] = useState("");
  const [adding, setAdding] = useState(false);
  const { data, loading, error, reload } = useApi<UserAdmin[]>(
    `/api/users/?search=${encodeURIComponent(search)}&role=${role}`,
    [search, role]
  );

  useEffect(() => {
    const id = window.setInterval(() => reload({ silent: true }), 15_000);
    return () => window.clearInterval(id);
  }, [reload]);

  const act = async (fn: () => Promise<unknown>) => {
    try { await fn(); reload({ silent: true }); } catch (e) { alert(e instanceof Error ? e.message : "Lỗi"); }
  };
  const resetPw = async (u: UserAdmin) => {
    try {
      const r = await api.post<{ code: string }>(`/api/users/${u.id}/reset-password`);
      alert(`Mật khẩu mới của "${u.username}":\n\n${r.code}\n\nHãy gửi cho người dùng.`);
    } catch (e) { alert(e instanceof Error ? e.message : "Lỗi"); }
  };

  return (
    <div>
      <PageHeader
        title="Quản lý người dùng"
        subtitle="Quản lý tài khoản và thông tin người dùng trong hệ thống"
        actions={<Button onClick={() => setAdding(true)}><UserPlus className="h-4 w-4" /> Thêm người dùng</Button>}
      />

      <GlassCard className="mb-4 flex flex-wrap items-center gap-3 p-3">
        <div className="relative max-w-xs flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Tìm theo tên đăng nhập hoặc họ tên…" className="pl-9" />
        </div>
        <Select value={role} onChange={(e) => setRole(e.target.value)}>
          {ROLES.map((r) => <option key={r.key} value={r.key}>{r.label}</option>)}
        </Select>
      </GlassCard>

      <GlassCard className="overflow-hidden p-0">
        {loading ? (
          <Spinner />
        ) : error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <Table<UserAdmin>
            rows={data ?? []}
            keyOf={(r) => r.id}
            empty="Không có người dùng"
            columns={[
              { header: "Tên đăng nhập", cell: (r) => <span className="font-semibold">{r.username}</span> },
              { header: "Họ tên", cell: (r) => r.fullName || "—" },
              { header: "Email", cell: (r) => r.email || "—" },
              { header: "Vai trò", cell: (r) => <Badge color={r.role === "Admin" ? "purple" : "muted"}>{r.role}</Badge> },
              { header: "Online", cell: (r) => (
                <div className="inline-flex min-w-[112px] flex-col gap-1">
                  <Badge color={r.isOnline ? "success" : "muted"}>
                    {r.isOnline ? <Wifi className="h-3.5 w-3.5" /> : <WifiOff className="h-3.5 w-3.5" />}
                    {r.isOnline ? "Online" : "Offline"}
                  </Badge>
                  {r.lastSeen && <span className="text-xs text-[var(--text-muted)]">{dateTime(r.lastSeen)}</span>}
                </div>
              ) },
              { header: "Trạng thái", cell: (r) =>
                r.approvalStatus === "Pending" ? <Badge color="warning">Chờ duyệt</Badge>
                : !r.isActive ? <Badge color="danger">Đã khóa</Badge>
                : <Badge color="success">Hoạt động</Badge> },
              { header: "Ngày tạo", cell: (r) => date(r.createdAt) },
              { header: "", align: "right", cell: (r) => (
                <div className="flex justify-end gap-1">
                  {r.approvalStatus === "Pending" && (
                    <IconBtn title="Phê duyệt" color="success" onClick={() => act(() => api.post(`/api/users/${r.id}/approve`))}><Check className="h-4 w-4" /></IconBtn>
                  )}
                  <IconBtn title="Đặt lại mật khẩu" onClick={() => resetPw(r)}><KeyRound className="h-4 w-4" /></IconBtn>
                  {r.isActive ? (
                    <IconBtn title="Khóa" color="warning" onClick={() => act(() => api.post(`/api/users/${r.id}/lock`, { locked: true }))}><Lock className="h-4 w-4" /></IconBtn>
                  ) : (
                    <IconBtn title="Mở khóa" color="success" onClick={() => act(() => api.post(`/api/users/${r.id}/lock`, { locked: false }))}><Unlock className="h-4 w-4" /></IconBtn>
                  )}
                  <IconBtn title="Xóa" color="danger" onClick={() => confirm(`Xóa người dùng "${r.username}"?`) && act(() => api.del(`/api/users/${r.id}`))}><Trash2 className="h-4 w-4" /></IconBtn>
                </div>
              ) },
            ]}
          />
        )}
      </GlassCard>

      {adding && <AddUser onClose={() => setAdding(false)} onSaved={() => { setAdding(false); reload({ silent: true }); }} />}
    </div>
  );
}

function IconBtn({ children, title, color = "accent", onClick }: { children: React.ReactNode; title: string; color?: string; onClick: () => void }) {
  const hover: Record<string, string> = {
    accent: "hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]",
    success: "hover:bg-emerald-500/10 hover:text-emerald-600",
    warning: "hover:bg-amber-500/10 hover:text-amber-600",
    danger: "hover:bg-red-500/10 hover:text-[var(--danger)]",
  };
  return (
    <button title={title} onClick={onClick} className={`rounded-lg p-1.5 text-[var(--text-muted)] transition-colors ${hover[color]}`}>
      {children}
    </button>
  );
}

function AddUser({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [username, setUsername] = useState("");
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState("User");
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  const save = async () => {
    setSaving(true); setError("");
    try {
      await api.post("/api/users", { username, fullName, email, password, role });
      onSaved();
    } catch (e) { setError(e instanceof Error ? e.message : "Lỗi"); } finally { setSaving(false); }
  };

  return (
    <Modal open onClose={onClose} title="Thêm người dùng"
      footer={<><Button variant="ghost" onClick={onClose}>Hủy</Button><Button onClick={save} loading={saving}><Plus className="h-4 w-4" />Tạo</Button></>}>
      <div className="space-y-4">
        <Field label="Tên đăng nhập *"><Input value={username} onChange={(e) => setUsername(e.target.value)} /></Field>
        <Field label="Họ tên"><Input value={fullName} onChange={(e) => setFullName(e.target.value)} /></Field>
        <Field label="Email"><Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
        <Field label="Mật khẩu *"><Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
        <Field label="Vai trò"><Select value={role} onChange={(e) => setRole(e.target.value)} className="w-full"><option value="User">User</option><option value="Admin">Admin</option></Select></Field>
        {error && <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>}
      </div>
    </Modal>
  );
}
