import { useState } from "react";
import { Plus, Search, Trash2, Check, Lock, Unlock, KeyRound, KeySquare, UserPlus, Wifi, WifiOff, BadgeCheck } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Table } from "../components/Table";
import { Modal } from "../components/Modal";
import { useAppNotifications } from "../components/AppNotifications";
import { DiamondLabel, VerifiedBadge } from "../components/VerifiedBadge";
import { Button, Input, Select, Field, Badge } from "../components/ui";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { date, dateTime } from "../lib/format";
import type { UserAdmin } from "../lib/types";

const ROLES = [
  { key: "", label: "Tất cả vai trò" },
  { key: "Admin", label: "Admin" },
  { key: "Accounting", label: "Kế toán" },
  { key: "HR", label: "Nhân sự (HR)" },
  { key: "User", label: "User" },
  { key: "Pending", label: "Chờ duyệt" },
  { key: "Locked", label: "Đã khóa" },
];

/**
 * Vai trò gán được cho một tài khoản. Phải khớp AppRoles ở backend — riêng "Kế toán" là điều kiện bắt
 * buộc (cùng với việc thuộc phòng ban kế toán) để lập/duyệt phiếu chi tiền mặt.
 */
const ASSIGNABLE_ROLES = [
  { key: "Admin", label: "Admin" },
  { key: "Accounting", label: "Kế toán" },
  { key: "HR", label: "Nhân sự (HR)" },
  // "User" là bí danh cũ của Employee (backend tự quy đổi) nên không đưa vào đây cho khỏi trùng.
  { key: "Employee", label: "Nhân viên" },
];

export function NhanSu() {
  const { notify, confirm } = useAppNotifications();
  const { user } = useAuth();
  const [search, setSearch] = useState("");
  const [role, setRole] = useState("");
  const [adding, setAdding] = useState(false);
  const { data, loading, error, reload } = useApi<UserAdmin[]>(
    `/api/users/?search=${encodeURIComponent(search)}&role=${role}`,
    [search, role]
  );

  const act = async (fn: () => Promise<unknown>) => {
    try { await fn(); reload({ silent: true }); } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi"); }
  };
  const setDiamond = (u: UserAdmin, isDiamond: boolean) => {
    if (u.role === "Admin") return;
    void act(() => api.post(`/api/users/${u.id}/diamond`, { isDiamond }));
  };
  // Cấp/thu vai trò phụ "Thủ kho" (quyền giao việc & nghiệm thu). Admin luôn có sẵn quyền này.
  const setWarehouse = (u: UserAdmin, grant: boolean) => {
    if (u.role === "Admin") return;
    void act(() => api.post(`/api/users/${u.id}/secondary-role`, { role: "Warehouse", grant }));
  };
  const changeRole = async (u: UserAdmin, newRole: string) => {
    if (newRole === u.role) return;
    const label = ASSIGNABLE_ROLES.find((x) => x.key === newRole)?.label ?? newRole;
    const ok = await confirm({
      title: `Đổi vai trò thành "${label}"?`,
      description:
        newRole === "Accounting"
          ? `"${u.username}" sẽ lập & duyệt được phiếu chi tiền mặt — với điều kiện hồ sơ nhân sự của họ thuộc phòng ban được đánh dấu là phòng kế toán.`
          : `Vai trò của "${u.username}" sẽ đổi thành ${label}.`,
      confirmLabel: "Đổi vai trò",
      tone: newRole === "Admin" ? "warning" : "info",
    });
    if (!ok) return;
    void act(() => api.post(`/api/users/${u.id}/role`, { role: newRole }));
  };
  const resetPw = async (u: UserAdmin) => {
    try {
      const r = await api.post<{ code: string }>(`/api/users/${u.id}/reset-password`);
      notify.show({
        title: "Mật khẩu mới",
        message: `Tài khoản "${u.username}":\n${r.code}\nHãy gửi cho người dùng.`,
        tone: "info",
        duration: 20000,
      });
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi"); }
  };
  const recoveryCode = async (u: UserAdmin) => {
    try {
      const r = await api.post<{ code: string }>(`/api/users/${u.id}/recovery-code`);
      notify.show({
        title: "Mã khôi phục mật khẩu",
        message: `Tài khoản "${u.username}":\n${r.code}\nĐưa mã cho người dùng để tự đặt lại mật khẩu (mục "Quên mật khẩu?"). Hết hạn sau 7 ngày, dùng một lần.`,
        tone: "info",
        duration: 30000,
      });
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi"); }
  };

  const deleteUser = async (u: UserAdmin) => {
    const ok = await confirm({
      title: "Xóa người dùng?",
      description: `Xóa người dùng "${u.username}"?`,
      confirmLabel: "Xóa",
      tone: "danger",
    });
    if (ok) void act(() => api.del(`/api/users/${u.id}`));
  };

  return (
    <div className="gc-root">
      <PageHeader
        title="Quản lý người dùng"
        subtitle="Quản lý tài khoản và thông tin người dùng trong hệ thống"
        actions={<Button onClick={() => setAdding(true)}><UserPlus className="h-4 w-4" /> Thêm người dùng</Button>}
      />

      <GlassPanel className="mb-4 flex flex-wrap items-center gap-3 rounded-[20px] p-3">
        <div className="relative max-w-xs flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Tìm theo tên đăng nhập hoặc họ tên…" className="pl-9" />
        </div>
        <Select value={role} onChange={(e) => setRole(e.target.value)}>
          {ROLES.map((r) => <option key={r.key} value={r.key}>{r.label}</option>)}
        </Select>
      </GlassPanel>

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        {error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <Table<UserAdmin>
            loading={loading}
            rows={data ?? []}
            keyOf={(r) => r.id}
            empty="Không có người dùng"
            columns={[
              { header: "Tên đăng nhập", cell: (r) => <span className="font-semibold">{r.username}</span> },
              { header: "Họ tên", cell: (r) => (
                <span className="inline-flex items-center gap-1.5">
                  {r.fullName || "—"}
                  {r.verified && <VerifiedBadge size={15} />}
                  {r.isDiamond && <DiamondLabel />}
                </span>
              ) },
              { header: "Email", cell: (r) => r.email || "—" },
              { header: "Vai trò", cell: (r) => (
                <Select
                  value={r.role}
                  disabled={r.username === user?.username}
                  title={r.username === user?.username ? "Không thể tự đổi vai trò của chính mình" : "Đổi vai trò"}
                  onChange={(e) => changeRole(r, e.target.value)}
                  className="min-w-[132px] py-1.5 text-xs font-semibold"
                >
                  {ASSIGNABLE_ROLES.map((x) => <option key={x.key} value={x.key}>{x.label}</option>)}
                </Select>
              ) },
              { header: "Thủ kho", cell: (r) => (
                <Select
                  value={r.role === "Admin" || r.secondaryRoles?.includes("Warehouse") ? "yes" : "no"}
                  disabled={r.role === "Admin"}
                  title={r.role === "Admin" ? "Admin luôn có quyền giao việc & nghiệm thu" : "Cấp quyền giao việc & nghiệm thu"}
                  onChange={(e) => setWarehouse(r, e.target.value === "yes")}
                  className="min-w-[104px] py-1.5 text-xs font-semibold"
                >
                  <option value="no">Không</option>
                  <option value="yes">Thủ kho</option>
                </Select>
              ) },
              { header: "Hội viên", cell: (r) => (
                <Select
                  value={r.isDiamond ? "diamond" : "normal"}
                  disabled={r.role === "Admin"}
                  title={r.role === "Admin" ? "Admin luôn có đầy đủ đặc quyền" : "Chọn hạng hội viên"}
                  onChange={(e) => setDiamond(r, e.target.value === "diamond")}
                  className="min-w-[128px] py-1.5 text-xs font-semibold"
                >
                  <option value="normal">Thường</option>
                  <option value="diamond">Kim cương</option>
                </Select>
              ) },
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
                  {r.role === "Admin" ? (
                    <IconBtn title="Admin luôn có tích xanh" color="accent" onClick={() => {}}><BadgeCheck className="h-4 w-4 text-[#1d9bf0]" /></IconBtn>
                  ) : r.verified ? (
                    <IconBtn title="Thu hồi tích xanh" color="accent" onClick={() => act(() => api.post(`/api/users/${r.id}/verify`, { verified: false }))}><BadgeCheck className="h-4 w-4 text-[#1d9bf0]" /></IconBtn>
                  ) : (
                    <IconBtn title="Cấp tích xanh" onClick={() => act(() => api.post(`/api/users/${r.id}/verify`, { verified: true }))}><BadgeCheck className="h-4 w-4" /></IconBtn>
                  )}
                  <IconBtn title="Đặt lại mật khẩu" onClick={() => resetPw(r)}><KeyRound className="h-4 w-4" /></IconBtn>
                  <IconBtn title="Tạo mã khôi phục (người dùng tự đặt lại)" onClick={() => recoveryCode(r)}><KeySquare className="h-4 w-4" /></IconBtn>
                  {r.role !== "Admin" && (r.isActive ? (
                    <IconBtn title="Khóa" color="warning" onClick={() => act(() => api.post(`/api/users/${r.id}/lock`, { locked: true }))}><Lock className="h-4 w-4" /></IconBtn>
                  ) : (
                    <IconBtn title="Mở khóa" color="success" onClick={() => act(() => api.post(`/api/users/${r.id}/lock`, { locked: false }))}><Unlock className="h-4 w-4" /></IconBtn>
                  ))}
                  <IconBtn title="Xóa" color="danger" onClick={() => void deleteUser(r)}><Trash2 className="h-4 w-4" /></IconBtn>
                </div>
              ) },
            ]}
          />
        )}
      </GlassPanel>

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
  const [role, setRole] = useState("Employee");
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
        <Field label="Vai trò"><Select value={role} onChange={(e) => setRole(e.target.value)} className="w-full">{ASSIGNABLE_ROLES.map((x) => <option key={x.key} value={x.key}>{x.label}</option>)}</Select></Field>
        {error && <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>}
      </div>
    </Modal>
  );
}
