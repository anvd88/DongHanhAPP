/**
 * Bản sao danh sách quyền của backend (Security/Permissions.cs — 45 quyền, 12 vai trò).
 *
 * Dữ liệu này chỉ phục vụ dựng giao diện. Backend tính lại quyền từ CSDL ở mỗi request, nên sửa
 * ở đây chỉ đổi phần hiển thị. Chốt quyền thật nằm ở endpoint.
 */

export const PERM = {
  usersRead: 'users.read',
  usersManage: 'users.manage',
  rolesManage: 'roles.manage',

  systemSettingsManage: 'system.settings.manage',
  systemReleasesManage: 'system.releases.manage',
  companyScopeAll: 'scope.company.all',

  auditRead: 'audit.read',

  accountingAccess: 'accounting.access',
  vouchersRead: 'vouchers.read',
  vouchersCreate: 'vouchers.create',
  vouchersUpdate: 'vouchers.update',
  vouchersApprove: 'vouchers.approve',
  vouchersCancel: 'vouchers.cancel',

  payoutRead: 'payout.read',
  payoutCreate: 'payout.create',
  payoutApprove: 'payout.approve',
  payoutPay: 'payout.pay',

  collectionsSelf: 'collections.self',
  collectionsReadAll: 'collections.read.all',
  collectionsCreate: 'collections.create',
  collectionsReceive: 'collections.receive',
  collectionsResolve: 'collections.resolve',

  cashFundRead: 'cashfund.read',
  cashFundManage: 'cashfund.manage',

  reportRead: 'report.read',
  reportExport: 'report.export',

  attendanceSelf: 'attendance.self',
  attendanceRead: 'attendance.read',
  attendanceManage: 'attendance.manage',
  attendanceKiosk: 'attendance.kiosk',

  payrollRead: 'payroll.read',
  payrollManage: 'payroll.manage',

  hrSelfAccess: 'hr.self.access',
  hrRead: 'hr.read',
  hrManage: 'hr.manage',

  requestsSelf: 'requests.self',
  requestsApprove: 'requests.approve',
  requestsManage: 'requests.manage',

  penaltyRead: 'penalty.read',
  penaltyManage: 'penalty.manage',

  tasksSelf: 'tasks.self',
  tasksAssign: 'tasks.assign',

  portalRead: 'portal.read',
  portalManage: 'portal.manage',
} as const

export type Permission = (typeof PERM)[keyof typeof PERM]

/** 12 vai trò của backend (AppRoles) kèm nhãn tiếng Việt dùng cho phần hiển thị. */
export const ROLE_LABELS: Record<string, string> = {
  Admin: 'Quản trị viên',
  Executive: 'Ban giám đốc',
  ChiefAccountant: 'Kế toán trưởng',
  Accounting: 'Kế toán',
  Payroll: 'Nhân viên tiền lương',
  Cashier: 'Thủ quỹ',
  Warehouse: 'Thủ kho',
  Hr: 'Nhân sự',
  Manager: 'Quản lý',
  Driver: 'Lái xe',
  Employee: 'Nhân viên',
  Kiosk: 'Máy chấm công',
}

/** Phạm vi dữ liệu do backend quyết định (AccessScope). */
export const SCOPE_LABELS: Record<string, string> = {
  self: 'Chỉ dữ liệu của tôi',
  department: 'Phòng ban của tôi',
  branch: 'Chi nhánh của tôi',
  all: 'Toàn công ty',
}

/** Hồ sơ truy cập, ánh xạ đúng AccessProfileDto của backend. */
export interface AccessProfile {
  username: string
  fullName: string
  primaryRole: string
  roles: string[]
  roleLabels: string[]
  permissions: string[]
  scope: string
  departmentId: string | null
  locationId: string | null
  uiProfile: string
  landingPath: string
  authorizationVersion: number
}

export function can(profile: AccessProfile | null, permission?: string) {
  if (!permission) return true
  return !!profile?.permissions.includes(permission)
}

export function canAny(profile: AccessProfile | null, permissions?: readonly string[]) {
  if (!permissions?.length) return true
  return permissions.some((p) => can(profile, p))
}
