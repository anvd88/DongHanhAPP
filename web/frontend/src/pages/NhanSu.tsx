import { useState } from "react";
import { Plus, Search, Trash2, Check, Lock, Unlock, KeyRound, KeySquare, Wifi, WifiOff, BadgeCheck } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Table } from "../components/Table";
import { Modal } from "../components/Modal";
import { useAppNotifications } from "../components/app-notifications-context";
import { DiamondLabel, VerifiedBadge } from "../components/VerifiedBadge";
import { Button, Input, Select, Field, Badge } from "../components/ui";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { date, dateTime } from "../lib/format";
import type { UserAdmin } from "../lib/types";

const ROLES = [
  { key: "", label: "Tất cả vai trò hệ thống" },
  { key: "Admin", label: "Admin" },
  { key: "Executive", label: "Ban giám đốc" },
  { key: "ChiefAccountant", label: "Kế toán trưởng" },
  { key: "Accounting", label: "Kế toán viên" },
  { key: "Payroll", label: "Kế toán tiền lương" },
  { key: "Cashier", label: "Thủ quỹ" },
  { key: "HR", label: "Nhân sự (HR)" },
  { key: "Manager", label: "Trưởng phòng" },
  { key: "Warehouse", label: "Thủ kho" },
  { key: "Driver", label: "Lái xe" },
  { key: "Employee", label: "Nhân viên" },
  { key: "Pending", label: "Chờ duyệt" },
  { key: "Locked", label: "Đã khóa" },
];

/**
 * Vai trò gán được cho một tài khoản. Phải khớp AppRoles ở backend — riêng "Kế toán" là điều kiện bắt
 * buộc (cùng với việc thuộc phòng ban kế toán) để lập/duyệt phiếu chi tiền mặt.
 */
export const ASSIGNABLE_ROLES = [
  { key: "Admin", label: "Admin" },
  { key: "Executive", label: "Ban giám đốc" },
  { key: "ChiefAccountant", label: "Kế toán trưởng" },
  { key: "Accounting", label: "Kế toán viên" },
  { key: "Payroll", label: "Kế toán tiền lương" },
  { key: "Cashier", label: "Thủ quỹ" },
  { key: "HR", label: "Nhân sự (HR)" },
  { key: "Manager", label: "Trưởng phòng" },
  { key: "Warehouse", label: "Thủ kho" },
  { key: "Driver", label: "Lái xe" },
  // "User" là bí danh cũ của Employee (backend tự quy đổi) nên không đưa vào đây cho khỏi trùng.
  { key: "Employee", label: "Nhân viên" },
];

/**
 * Vai trò cấp THÊM (ngoài vai trò chính) — một người có thể vừa là Kế toán vừa là Thủ kho.
 * Quyền đi kèm mỗi vai trò được định nghĩa MỘT CHỖ ở backend (Security/Permissions.cs); ở đây chỉ là
 * nhãn hiển thị, nên thêm vai trò mới không phải sửa logic phân quyền nào trong frontend.
 */
const SECONDARY_ROLES = [
  { key: "Warehouse", label: "Thủ kho", hint: "Giao việc & nghiệm thu" },
  { key: "Manager", label: "Trưởng phòng", hint: "Duyệt đơn của phòng + giao việc" },
  { key: "ChiefAccountant", label: "Kế toán trưởng", hint: "Duyệt chứng từ kế toán" },
  { key: "Accounting", label: "Kế toán", hint: "Nghiệp vụ kế toán" },
  { key: "Payroll", label: "Kế toán tiền lương", hint: "Lập và phát hành phiếu lương" },
  { key: "Cashier", label: "Thủ quỹ", hint: "Thực hiện chi phiếu đã duyệt" },
  { key: "HR", label: "Nhân sự", hint: "Quản lý nghiệp vụ nhân sự" },
  { key: "Driver", label: "Lái xe", hint: "Nhận và xử lý lệnh thu tiền được giao" },
];

/** Vai trò + danh sách quyền nó cấp, lấy từ /api/roles/catalog (nguồn duy nhất: backend Permissions.cs). */
type RoleCatalogEntry = { role: string; label: string; permissions: { key: string; label: string }[] };

function useRoleCatalog(): RoleCatalogEntry[] {
  return useApi<RoleCatalogEntry[]>("/api/roles/catalog").data ?? [];
}

/** "User" là bí danh cũ của "Employee" — quy về một tên để khớp với catalog. */
const normalizeRole = (role?: string | null) => (role === "User" ? "Employee" : role ?? "");

/**
 * Liệt kê (gọn) các quyền mà một TẬP vai trò cấp — để người quản trị thấy rõ "gán vai trò này thì
 * tài khoản làm được gì". Chỉ hiển thị; backend vẫn là nơi chốt quyền ở mỗi request.
 */
function RolePermissions({ catalog, roles, title }: { catalog: RoleCatalogEntry[]; roles: (string | null | undefined)[]; title: string }) {
  const wanted = roles.map(normalizeRole).filter(Boolean);
  if (wanted.length === 0) return null;
  const box = (body: React.ReactNode) => (
    <div className="rounded-xl border border-[var(--gc-border)] bg-[var(--accent-soft)]/30 p-3">
      <p className="mb-1.5 text-[11px] font-bold uppercase tracking-wide text-[var(--text-muted)]">{title}</p>
      {body}
    </div>
  );
  if (wanted.includes("Admin")) return box(<p className="text-sm font-semibold text-[var(--accent)]">Toàn quyền hệ thống</p>);
  const perms = new Map<string, string>();
  for (const entry of catalog) {
    if (!wanted.includes(entry.role)) continue;
    for (const p of entry.permissions) perms.set(p.key, p.label);
  }
  const labels = [...perms.values()].sort((a, b) => a.localeCompare(b, "vi"));
  if (labels.length === 0)
    return box(<p className="text-xs text-[var(--text-muted)]">{catalog.length === 0 ? "Đang tải quyền…" : "Không có quyền đặc biệt."}</p>);
  return box(
    <div className="flex flex-wrap gap-1.5">
      {labels.map((label) => (
        <span key={label} className="rounded-md border border-[var(--gc-border)] bg-[var(--surface)] px-2 py-1 text-[11px] font-medium text-[var(--text-secondary)]">{label}</span>
      ))}
    </div>,
  );
}

export function NhanSu({ embedded = false }: { embedded?: boolean } = {}) {
  const { notify, confirm } = useAppNotifications();
  const { user } = useAuth();
  const [search, setSearch] = useState("");
  const [role, setRole] = useState("");
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
  // Cấp/thu một vai trò PHỤ. Admin đã có sẵn mọi quyền nên không cần cấp thêm gì.
  // Backend ghi lịch sử "quyền cũ → quyền mới" và bắn tín hiệu để phía người bị đổi cập nhật ngay.
  const toggleSecondary = async (u: UserAdmin, role: string, grant: boolean) => {
    if (u.role === "Admin") return;
    const label = SECONDARY_ROLES.find((x) => x.key === role)?.label ?? role;
    const ok = await confirm({
      title: `${grant ? "Cấp" : "Thu hồi"} vai trò "${label}"?`,
      description: grant
        ? `"${u.username}" sẽ có thêm quyền của ${label} ngay từ thao tác kế tiếp, không cần đăng nhập lại.`
        : `"${u.username}" sẽ mất quyền của ${label} ngay từ thao tác kế tiếp.`,
      confirmLabel: grant ? "Cấp quyền" : "Thu hồi",
      tone: grant ? "info" : "warning",
    });
    if (!ok) return;
    void act(() => api.post(`/api/users/${u.id}/secondary-role`, { role, grant }));
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
    <div className={embedded ? "space-y-4" : "gc-root"}>
      {embedded ? (
        <GlassPanel strong className="flex flex-wrap items-center justify-between gap-3 rounded-[20px] px-5 py-4">
          <div>
            <h2 className="font-bold text-[var(--text)]">Tài khoản đăng nhập</h2>
            <p className="mt-1 text-xs text-[var(--text-muted)]">
              Quản lý đăng nhập, vai trò và trạng thái tài khoản. Tên đăng nhập cần trùng với tên đăng nhập trong hồ sơ nhân viên.
            </p>
          </div>
          <span className="rounded-full bg-[var(--accent-soft)] px-3 py-1.5 text-xs font-semibold text-[var(--accent)]">Tài khoản được tạo từ hồ sơ nhân sự</span>
        </GlassPanel>
      ) : (
        <PageHeader
          title="Quản lý người dùng"
          subtitle="Quản lý tài khoản và thông tin người dùng trong hệ thống"
          actions={<span className="rounded-full bg-[var(--accent-soft)] px-3 py-1.5 text-xs font-semibold text-[var(--accent)]">Tạo tại mục Hồ sơ nhân sự</span>}
        />
      )}

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
              { header: "Vai trò hệ thống chính", cell: (r) => (
                <Select
                  value={r.role}
                  disabled={r.rolesManagedByPositions || r.username === user?.username}
                  title={r.rolesManagedByPositions ? "Vai trò được hợp nhất từ các chức vụ trong hồ sơ" : r.username === user?.username ? "Không thể tự đổi vai trò của chính mình" : "Đổi vai trò"}
                  onChange={(e) => changeRole(r, e.target.value)}
                  className="min-w-[132px] py-1.5 text-xs font-semibold"
                >
                  {ASSIGNABLE_ROLES.map((x) => <option key={x.key} value={x.key}>{x.label}</option>)}
                </Select>
              ) },
              { header: "Vai trò hệ thống bổ sung", cell: (r) => (
                <div className="flex flex-wrap gap-1">
                  {r.rolesManagedByPositions ? (
                    <span className="text-xs font-medium text-[var(--accent)]">Quản lý theo chức vụ</span>
                  ) : r.role === "Admin" ? (
                    <span className="text-xs opacity-60">Admin có sẵn mọi quyền</span>
                  ) : (
                    SECONDARY_ROLES.filter((x) => x.key !== r.role).map((x) => {
                      const on = r.secondaryRoles?.includes(x.key) ?? false;
                      return (
                        <button
                          key={x.key}
                          type="button"
                          title={`${x.hint} — bấm để ${on ? "thu hồi" : "cấp"}`}
                          onClick={() => void toggleSecondary(r, x.key, !on)}
                          className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold transition ${
                            on
                              ? "border-[var(--accent)] bg-[var(--accent)]/15 text-[var(--accent)]"
                              : "border-[var(--border)] opacity-55 hover:opacity-100"
                          }`}
                        >
                          {x.label}
                        </button>
                      );
                    })
                  )}
                </div>
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

export type NewUserInitial = { username?: string; fullName?: string; email?: string };

/**
 * Biểu mẫu tạo tài khoản đăng nhập, KHÔNG bọc Modal — để nhúng vào mục con "Tài khoản" của một người
 * trong bảng Nhân viên (khi người đó có hồ sơ nhưng chưa có tài khoản).
 */
export function AccountCreateForm({ initial, onSaved }: { initial?: NewUserInitial; onSaved: () => void }) {
  const [username, setUsername] = useState(initial?.username ?? "");
  const [fullName, setFullName] = useState(initial?.fullName ?? "");
  const [email, setEmail] = useState(initial?.email ?? "");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  const save = async () => {
    setSaving(true); setError("");
    try {
      // Máy chủ chỉ chấp nhận tài khoản cho hồ sơ đã tồn tại và tự suy ra toàn bộ vai trò từ chức vụ.
      await api.post("/api/users", { username, fullName, email, password, role: "Employee" });
      onSaved();
    } catch (e) { setError(e instanceof Error ? e.message : "Lỗi"); } finally { setSaving(false); }
  };

  return (
    <div className="space-y-4">
      <Field label="Tên đăng nhập *"><Input value={username} disabled={Boolean(initial?.username)} onChange={(e) => setUsername(e.target.value)} /></Field>
      <Field label="Họ tên"><Input value={fullName} onChange={(e) => setFullName(e.target.value)} /></Field>
      <Field label="Email"><Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
      <Field label="Mật khẩu *"><Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
      <div className="rounded-xl border border-[var(--gc-border)] bg-[var(--accent-soft)]/30 p-3 text-sm text-[var(--text-secondary)]">
        Vai trò và quyền được máy chủ hợp nhất từ chức vụ chính cùng các chức vụ kiêm nhiệm trong hồ sơ; không thể chọn quyền trực tiếp tại đây.
      </div>
      {error && <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>}
      <div className="flex justify-end border-t border-[var(--gc-border)] pt-4">
        <Button onClick={save} loading={saving}><Plus className="h-4 w-4" /> Tạo tài khoản</Button>
      </div>
    </div>
  );
}

export function AddUserModal({ initial, onClose, onSaved }: { initial?: NewUserInitial; onClose: () => void; onSaved: () => void }) {
  return (
    <Modal open onClose={onClose} title="Thêm tài khoản" footer={<Button variant="ghost" onClick={onClose}>Đóng</Button>}>
      <AccountCreateForm initial={initial} onSaved={onSaved} />
    </Modal>
  );
}

/**
 * Nội dung quản trị một tài khoản (vai trò, hội viên, duyệt, khóa, mật khẩu, xóa) — KHÔNG bọc Modal,
 * để nhúng làm mục con "Tài khoản" của một người trong bảng Nhân viên. Kèm hiển thị QUYỀN mỗi vai trò
 * cấp để người quản trị thấy rõ đang trao gì.
 */
export function AccountManagePanel({ value: u, onChanged, onClose, rolesManagedByPositions = false }: {
  value: UserAdmin;
  onChanged: () => void;
  onClose: () => void;
  /** Hồ sơ đã gắn chức vụ: vai trò được suy từ chức vụ, không chỉnh tay ở tab tài khoản. */
  rolesManagedByPositions?: boolean;
}) {
  const { notify, confirm } = useAppNotifications();
  const { user } = useAuth();
  const catalog = useRoleCatalog();
  const [saving, setSaving] = useState(false);

  const act = async (fn: () => Promise<unknown>, success?: string) => {
    setSaving(true);
    try {
      await fn();
      onChanged();
      if (success) notify.success(success);
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Lỗi");
    } finally {
      setSaving(false);
    }
  };

  const changeRole = async (newRole: string) => {
    if (newRole === u.role) return;
    const label = ASSIGNABLE_ROLES.find((x) => x.key === newRole)?.label ?? newRole;
    const ok = await confirm({
      title: `Đổi vai trò thành "${label}"?`,
      description: `Vai trò hệ thống chính của tài khoản "${u.username}" sẽ đổi thành ${label}.`,
      confirmLabel: "Đổi vai trò",
      tone: newRole === "Admin" ? "warning" : "info",
    });
    if (ok) await act(() => api.post(`/api/users/${u.id}/role`, { role: newRole }), "Đã cập nhật vai trò.");
  };

  const toggleSecondary = async (role: string, grant: boolean) => {
    const label = SECONDARY_ROLES.find((x) => x.key === role)?.label ?? role;
    const ok = await confirm({
      title: `${grant ? "Cấp" : "Thu hồi"} vai trò "${label}"?`,
      description: `Thay đổi có hiệu lực với "${u.username}" ngay từ thao tác kế tiếp.`,
      confirmLabel: grant ? "Cấp quyền" : "Thu hồi",
      tone: grant ? "info" : "warning",
    });
    if (ok) await act(() => api.post(`/api/users/${u.id}/secondary-role`, { role, grant }));
  };

  const showResetPassword = async () => {
    try {
      const result = await api.post<{ code: string }>(`/api/users/${u.id}/reset-password`);
      notify.show({ title: "Mật khẩu mới", message: `Tài khoản "${u.username}":\n${result.code}\nHãy gửi cho người dùng.`, tone: "info", duration: 20000 });
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi"); }
  };

  const showRecoveryCode = async () => {
    try {
      const result = await api.post<{ code: string }>(`/api/users/${u.id}/recovery-code`);
      notify.show({ title: "Mã khôi phục mật khẩu", message: `Tài khoản "${u.username}":\n${result.code}\nMã hết hạn sau 7 ngày và chỉ dùng một lần.`, tone: "info", duration: 30000 });
    } catch (e) { notify.error(e instanceof Error ? e.message : "Lỗi"); }
  };

  const remove = async () => {
    const ok = await confirm({
      title: `Xóa tài khoản ${u.username}?`,
      description: "Hồ sơ nhân viên không bị xóa, nhưng người này sẽ không thể đăng nhập.",
      confirmLabel: "Xóa tài khoản",
      tone: "danger",
    });
    if (!ok) return;
    setSaving(true);
    try {
      await api.del(`/api/users/${u.id}`);
      onChanged();
      onClose();
      notify.success("Đã xóa tài khoản.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được tài khoản.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-5">
        <div className="grid gap-3 sm:grid-cols-3">
          <div className="rounded-2xl border border-[var(--gc-border)] p-4 sm:col-span-2">
            <p className="flex items-center gap-2 font-bold text-[var(--text)]">
              {u.fullName || u.username}
              {u.verified && <VerifiedBadge size={16} />}
              {u.isDiamond && <DiamondLabel />}
            </p>
            <p className="mt-1 text-sm text-[var(--text-muted)]">{u.email || "Chưa có email"}</p>
            <p className="mt-1 text-xs text-[var(--text-muted)]">Ngày tạo: {date(u.createdAt)}</p>
          </div>
          <div className="flex flex-wrap content-center gap-2 rounded-2xl border border-[var(--gc-border)] p-4">
            <Badge color={u.approvalStatus === "Pending" ? "warning" : u.isActive ? "success" : "danger"}>
              {u.approvalStatus === "Pending" ? "Chờ duyệt" : u.isActive ? "Hoạt động" : "Đã khóa"}
            </Badge>
            <Badge color={u.isOnline ? "success" : "muted"}>{u.isOnline ? "Online" : "Offline"}</Badge>
            {u.lastSeen && <span className="w-full text-xs text-[var(--text-muted)]">Lần cuối: {dateTime(u.lastSeen)}</span>}
          </div>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Vai trò hệ thống chính">
            <Select value={u.role} disabled={saving || rolesManagedByPositions || u.username === user?.username} onChange={(e) => void changeRole(e.target.value)} className="w-full">
              {ASSIGNABLE_ROLES.map((x) => <option key={x.key} value={x.key}>{x.label}</option>)}
            </Select>
            <p className="mt-1 text-[11px] text-[var(--text-muted)]">
              {rolesManagedByPositions
                ? "Vai trò được đồng bộ tự động từ chức vụ chính và các chức vụ kiêm nhiệm trong Hồ sơ."
                : "Quyết định các chức năng tài khoản được dùng; độc lập với phạm vi dữ liệu HR trong hồ sơ nhân viên."}
            </p>
          </Field>
          <Field label="Hạng tài khoản">
            <Select value={u.isDiamond ? "diamond" : "normal"} disabled={saving || u.role === "Admin"}
              onChange={(e) => void act(() => api.post(`/api/users/${u.id}/diamond`, { isDiamond: e.target.value === "diamond" }))} className="w-full">
              <option value="normal">Thường</option>
              <option value="diamond">Kim cương</option>
            </Select>
          </Field>
        </div>

        <RolePermissions catalog={catalog} roles={[u.role]} title="Vai trò chính cấp các quyền" />

        <div>
          <p className="mb-2 text-xs font-semibold text-[var(--text-secondary)]">Vai trò hệ thống bổ sung</p>
          <div className="flex flex-wrap gap-2">
            {rolesManagedByPositions ? <span className="text-sm text-[var(--text-muted)]">Hãy đổi danh sách chức vụ trong mục Hồ sơ để cấp hoặc thu hồi vai trò.</span> :
              u.role === "Admin" ? <span className="text-sm text-[var(--text-muted)]">Admin có sẵn mọi quyền.</span> :
              SECONDARY_ROLES.filter((x) => x.key !== u.role).map((x) => {
                const active = u.secondaryRoles?.includes(x.key) ?? false;
                return <button key={x.key} type="button" disabled={saving} onClick={() => void toggleSecondary(x.key, !active)}
                  className={`rounded-full border px-3 py-1.5 text-xs font-semibold transition ${active ? "border-[var(--accent)] bg-[var(--accent-soft)] text-[var(--accent)]" : "border-[var(--gc-border)] text-[var(--text-muted)]"}`}>
                  {x.label}
                </button>;
              })}
          </div>
          {u.role !== "Admin" && (u.secondaryRoles?.length ?? 0) > 0 && (
            <div className="mt-2"><RolePermissions catalog={catalog} roles={u.secondaryRoles ?? []} title="Vai trò bổ sung cấp thêm" /></div>
          )}
        </div>

        <div className="flex flex-wrap gap-2 border-t border-[var(--gc-border)] pt-4">
          {u.approvalStatus === "Pending" && <Button disabled={saving} onClick={() => void act(() => api.post(`/api/users/${u.id}/approve`), "Đã phê duyệt tài khoản.")}><Check className="h-4 w-4" /> Phê duyệt</Button>}
          <Button variant="soft" disabled={saving || u.role === "Admin"} onClick={() => void act(() => api.post(`/api/users/${u.id}/verify`, { verified: !u.verified }))}><BadgeCheck className="h-4 w-4" /> {u.verified ? "Thu hồi tích xanh" : "Cấp tích xanh"}</Button>
          <Button variant="soft" disabled={saving} onClick={() => void showResetPassword()}><KeyRound className="h-4 w-4" /> Đặt lại mật khẩu</Button>
          <Button variant="soft" disabled={saving} onClick={() => void showRecoveryCode()}><KeySquare className="h-4 w-4" /> Mã khôi phục</Button>
          {u.role !== "Admin" && <Button variant="ghost" disabled={saving} onClick={() => void act(() => api.post(`/api/users/${u.id}/lock`, { locked: u.isActive }))}>
            {u.isActive ? <Lock className="h-4 w-4" /> : <Unlock className="h-4 w-4" />} {u.isActive ? "Khóa tài khoản" : "Mở khóa"}
          </Button>}
          <Button variant="danger" disabled={saving || u.username === user?.username} onClick={() => void remove()}><Trash2 className="h-4 w-4" /> Xóa tài khoản</Button>
        </div>
    </div>
  );
}

export function UserAccountModal({ value, onClose, onChanged }: {
  value: UserAdmin;
  onClose: () => void;
  onChanged: () => void;
}) {
  return (
    <Modal open onClose={onClose} title={`Quản lý tài khoản · ${value.username}`} panel wide
      footer={<Button variant="ghost" onClick={onClose}>Đóng</Button>}>
      <AccountManagePanel value={value} onChanged={onChanged} onClose={onClose} />
    </Modal>
  );
}
