/* eslint-disable react-refresh/only-export-components -- Provider và hook dùng chung phải ở cùng module. */
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api } from "./api";
import { useAuth } from "./auth";
import { subscribeRealtime } from "./realtime";

/**
 * HỒ SƠ TRUY CẬP do BACKEND tính từ cơ sở dữ liệu. Đây là thứ DUY NHẤT frontend được dùng để dựng
 * giao diện (chọn layout, menu, nút, trang đích, chặn route).
 *
 * QUAN TRỌNG — vì sao không tự suy quyền ở client:
 * người dùng sửa được URL, localStorage và cả mã JavaScript trên trình duyệt. Nên mọi thứ ở đây chỉ
 * quyết định NHÌN THẤY GÌ, không quyết định LÀM ĐƯỢC GÌ. Ẩn nút chỉ để đỡ rối mắt; backend mới là nơi
 * chốt, và nó chốt lại từ đầu ở mỗi request (xem Security/AccessProfileService.cs).
 */
export interface AccessProfile {
  username: string;
  fullName: string;
  primaryRole: string;
  roles: string[];
  roleLabels: string[];
  permissions: string[];
  /** "self" | "department" | "branch" | "all" — phạm vi dữ liệu, server vẫn ép lại ở mỗi truy vấn. */
  scope: string;
  departmentId?: string | null;
  locationId?: string | null;
  /** "admin" | "hr" | "accounting" | "workspace" | "kiosk" — quyết định layout nào được dựng. */
  uiProfile: string;
  landingPath: string;
  authorizationVersion: number;
}

/**
 * Tên quyền chuẩn — phải KHỚP TỪNG CHỮ với Security/Permissions.cs bên backend. Không viết chuỗi
 * quyền trực tiếp trong component: gõ sai một chữ thì `can()` lặng lẽ trả false và menu biến mất mà
 * không có lỗi nào, rất khó lần ra.
 */
export const PERM = {
  usersRead: "users.read",
  usersManage: "users.manage",
  rolesManage: "roles.manage",
  systemSettingsManage: "system.settings.manage",
  systemReleasesManage: "system.releases.manage",
  auditRead: "audit.read",
  accountingAccess: "accounting.access",
  vouchersRead: "vouchers.read",
  vouchersCreate: "vouchers.create",
  vouchersUpdate: "vouchers.update",
  vouchersApprove: "vouchers.approve",
  vouchersCancel: "vouchers.cancel",
  payoutRead: "payout.read",
  payoutCreate: "payout.create",
  payoutApprove: "payout.approve",
  payoutPay: "payout.pay",
  collectionsSelf: "collections.self",
  collectionsReadAll: "collections.read.all",
  collectionsCreate: "collections.create",
  collectionsReceive: "collections.receive",
  collectionsResolve: "collections.resolve",
  cashFundRead: "cashfund.read",
  cashFundManage: "cashfund.manage",
  reportRead: "report.read",
  reportExport: "report.export",
  attendanceSelf: "attendance.self",
  attendanceRead: "attendance.read",
  attendanceManage: "attendance.manage",
  payrollRead: "payroll.read",
  payrollManage: "payroll.manage",
  hrSelfAccess: "hr.self.access",
  hrRead: "hr.read",
  hrManage: "hr.manage",
  requestsSelf: "requests.self",
  requestsApprove: "requests.approve",
  requestsManage: "requests.manage",
  penaltyRead: "penalty.read",
  penaltyManage: "penalty.manage",
  tasksSelf: "tasks.self",
  tasksAssign: "tasks.assign",
  portalRead: "portal.read",
  portalManage: "portal.manage",
  chatAccess: "chat.access",
} as const;

export type Permission = (typeof PERM)[keyof typeof PERM];

interface AccessCtx {
  profile: AccessProfile | null;
  /** Chưa biết quyền (đang tải hoặc gọi hỏng). Route phải CHỜ, không được vội chuyển hướng. */
  loading: boolean;
  /** true = đã hỏi backend nhưng không lấy được hồ sơ (mạng/DB). Giao diện nên báo thay vì im lặng. */
  failed: boolean;
  can: (permission: Permission) => boolean;
  canAny: (...permissions: Permission[]) => boolean;
  landingPath: string;
  uiProfile: string;
  reload: () => void;
}

const FALLBACK_LANDING = "/nhan-su";

const Ctx = createContext<AccessCtx>({
  profile: null,
  loading: true,
  failed: false,
  can: () => false,
  canAny: () => false,
  landingPath: FALLBACK_LANDING,
  uiProfile: "workspace",
  reload: () => {},
});

export const useAccess = () => useContext(Ctx);

export function AccessProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const [nonce, setNonce] = useState(0);
  const reload = useCallback(() => setNonce((n) => n + 1), []);

  // Khóa nhận dạng "lần tải này là của ai, lần thứ mấy". Kết quả được lưu KÈM khóa để chỉ dùng khi
  // đúng khóa hiện tại: hồ sơ của tài khoản trước về muộn sẽ không ghi đè hồ sơ của tài khoản sau,
  // và trong lúc chờ tải lại thì trạng thái là "chưa biết" chứ không phải "quyền cũ".
  const key = `${user?.username ?? ""}#${nonce}`;
  const [result, setResult] = useState<{ key: string; profile: AccessProfile | null; failed: boolean } | null>(null);

  useEffect(() => {
    if (!user) return;
    let cancelled = false;
    api
      .get<AccessProfile>("/api/auth/access-profile")
      .then((p) => {
        if (!cancelled) setResult({ key, profile: p, failed: false });
      })
      .catch(() => {
        // Không xác định được quyền ⇒ KHÔNG giữ hồ sơ cũ. Giữ lại chỉ để "đỡ nháy giao diện" nghĩa là
        // tiếp tục hiện menu của quyền đã bị thu hồi — đúng thứ đợt nâng cấp này muốn dẹp.
        if (!cancelled) setResult({ key, profile: null, failed: true });
      });
    return () => { cancelled = true; };
  }, [user, key]);

  const ready = result?.key === key;
  const profile = user && ready ? result.profile : null;
  const loading = Boolean(user) && !ready;
  const failed = Boolean(user) && ready && result.failed;

  // Admin đổi quyền → backend bắn tín hiệu "access" tới ĐÚNG người đó → nạp lại hồ sơ ngay, không cần
  // đăng xuất/đăng nhập lại. (Quyền thật đã đổi từ request kế tiếp rồi; đây chỉ là cập nhật giao diện.)
  useEffect(() => {
    if (!user) return;
    return subscribeRealtime(() => reload(), ["access"]);
  }, [user, reload]);

  const value = useMemo<AccessCtx>(() => {
    const granted = new Set(profile?.permissions ?? []);
    return {
      profile,
      loading,
      failed,
      can: (permission) => granted.has(permission),
      canAny: (...permissions) => permissions.some((p) => granted.has(p)),
      landingPath: profile?.landingPath || FALLBACK_LANDING,
      uiProfile: profile?.uiProfile || "workspace",
      reload,
    };
  }, [profile, loading, failed, reload]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
