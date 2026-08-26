// Kiểu dữ liệu & tiện ích dùng chung cho khối Nhân sự (hồ sơ, đơn từ, chấm công/ca làm, quyền lợi).

export interface Department {
  id: string;
  code: string;
  name: string;
  parentId?: string | null;
  parentName: string;
  managerEmployeeId?: string | null;
  managerName: string;
  isAccounting: boolean;
  employeeCount: number;
}

// Địa điểm/chi nhánh làm việc (phục vụ phân quyền theo địa điểm).
export interface Location {
  id: string;
  code: string;
  name: string;
  address: string;
  employeeCount: number;
}

// Phạm vi dữ liệu nhân sự. Đây không phải vai trò hệ thống của tài khoản.
export type AccessRole = "staff" | "dept_manager" | "location_manager";

/** Chức vụ chuẩn do backend seed; defaultRole quyết định quyền tài khoản gắn với hồ sơ. */
export interface JobPosition {
  id: string;
  code: string;
  name: string;
  defaultRole: string;
  defaultRoleLabel: string;
  defaultAccessRole: AccessRole;
  isSystem: boolean;
  isActive: boolean;
  sortOrder: number;
}

/** Chức vụ đã gắn vào một hồ sơ. Một người có một chức vụ chính và có thể kiêm nhiệm nhiều chức vụ. */
export interface EmployeePosition {
  id: string;
  code: string;
  name: string;
  defaultRole: string;
  defaultRoleLabel: string;
  isPrimary: boolean;
}
export const ACCESS_ROLES: { value: AccessRole; label: string }[] = [
  { value: "staff", label: "Chỉ hồ sơ của mình" },
  { value: "dept_manager", label: "Toàn bộ phòng ban" },
  { value: "location_manager", label: "Toàn bộ địa điểm" },
];
export function accessRoleLabel(role?: string | null): string {
  return ACCESS_ROLES.find((r) => r.value === role)?.label ?? "Chỉ hồ sơ của mình";
}

export interface EmployeeCard {
  id: string;
  employeeCode: string;
  username: string;
  fullName: string;
  position: string;
  positionId?: string | null;
  positionCode?: string | null;
  /** Danh sách đầy đủ; positionId/position vẫn là chức vụ chính để tương thích ứng dụng cũ. */
  positionIds?: string[];
  positions?: EmployeePosition[];
  hireDate?: string | null;
  status: string;
  phone: string;
  email: string;
  avatar?: string | null;
  departmentId?: string | null;
  departmentName: string;
  locationId?: string | null;
  locationName?: string;
  accessRole?: string;
  managerName: string;
}

export interface EmployeeDetail extends EmployeeCard {
  dob?: string | null;
  gender: string;
  address: string;
  managerId?: string | null;
  isAccounting: boolean;
}

export interface Contract {
  id: string;
  contractNo: string;
  contractType: string;
  startDate?: string | null;
  endDate?: string | null;
  baseSalary: number;
  allowance: number;
  status: string;
  note: string;
  /** Tổng các lần tăng lương đã ghi nhận cho hợp đồng này. */
  raiseTotal: number;
  raiseCount: number;
  /** baseSalary + raiseTotal — lương cứng đang hưởng theo hợp đồng. */
  currentSalary: number;
}

/** Loại hợp đồng chọn được. Hợp đồng "Không xác định thời hạn" không có ngày kết thúc. */
export const CONTRACT_TYPES = [
  "Xác định thời hạn",
  "Không xác định thời hạn",
  "Thử việc",
  "Thời vụ",
  "Khoán việc",
] as const;

export const INDEFINITE_CONTRACT_TYPE = "Không xác định thời hạn";

/** Một lần tăng lương: áp dụng từ tháng nào, tăng thêm bao nhiêu. */
export interface SalaryRaise {
  id: string;
  contractId?: string | null;
  contractNo: string;
  /** yyyy-MM */
  effectivePeriod: string;
  amount: number;
  decisionNo: string;
  reason: string;
  createdBy: string;
  createdAt: string;
}

/** Lương cứng của một kỳ được dựng lại từ hợp đồng + các lần tăng lương. */
export interface HardSalary {
  amount: number;
  /** false = nhân viên chưa có hợp đồng nào, số đang dùng là mức lương nhập tay cũ. */
  fromContract: boolean;
  contractId?: string | null;
  contractNo: string;
  contractType: string;
  contractBase: number;
  raiseTotal: number;
  /** Hợp đồng còn hiệu lực trong kỳ đang tính hay không. */
  contractEffective: boolean;
  contractEndDate?: string | null;
  raises: { period: string; amount: number; decisionNo: string; reason: string }[];
}

export interface PayLine {
  label: string;
  amount: number;
}

export interface PayslipDetails {
  earnings?: PayLine[];
  deductions?: PayLine[];
  timesheet?: { workedDays: number; absentDays: number; lateDays: number; overtimeHours: number; totalWorkedHours: number };
  penaltyTotal?: number;
  totalEarnings?: number;
  totalDeductions?: number;
  netPay?: number;
}

export interface Payslip {
  id: string;
  period: string;
  workDays: number;
  overtimeHours: number;
  baseSalary: number;
  allowance: number;
  overtimePay: number;
  deductions: number;
  netPay: number;
  note: string;
  details?: PayslipDetails;
  published: boolean;
}

export type PayslipLifecycleStatus = "Draft" | "Published" | "Acknowledged" | "Deleted";

export interface PayslipHistoryCurrent {
  id: string;
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  period: string;
  status: PayslipLifecycleStatus;
  published: boolean;
  netPay: number;
  note: string;
  createdAt: string;
  updatedAt: string;
  acknowledgedAt?: string | null;
  createdBy: string;
  updatedBy: string;
}

export interface PayslipHistoryEvent {
  id: string;
  payslipId: string;
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  period: string;
  revision: number;
  action: string;
  statusBefore?: PayslipLifecycleStatus | null;
  statusAfter: PayslipLifecycleStatus;
  actor: string;
  occurredAt: string;
  summary: {
    netPay?: number;
    totalEarnings?: number;
    totalDeductions?: number;
    published?: boolean;
    note?: string;
  };
  snapshot: PayslipDetails & {
    workDays?: number;
    overtimeHours?: number;
    baseSalary?: number;
    allowance?: number;
    overtimePay?: number;
    netPay?: number;
    note?: string;
    published?: boolean;
    acknowledgedAt?: string | null;
  };
}

export interface PayslipHistoryEnvelope {
  payslip?: PayslipHistoryCurrent | null;
  history: PayslipHistoryEvent[];
}

export interface PublishedPayslipMonthItem {
  id: string;
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  departmentName: string;
  locationName: string;
  period: string;
  overtimeHours: number;
  totalEarnings: number;
  totalDeductions: number;
  netPay: number;
  status: "Published" | "Acknowledged";
  acknowledgedAt?: string | null;
  updatedAt: string;
}

export interface PublishedPayslipMonthPage {
  period: string;
  search: string;
  status: "all" | "pending" | "acknowledged";
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  summary: {
    activeEmployeeCount: number;
    publishedCount: number;
    acknowledgedCount: number;
    pendingAcknowledgementCount: number;
    totalEarnings: number;
    totalDeductions: number;
    totalNetPay: number;
  };
  items: PublishedPayslipMonthItem[];
}

export interface SalaryComponent {
  label: string;
  amount: number;
  kind: "earning" | "deduction";
}

export interface SalaryListItem {
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  departmentName: string;
  hasSalary: boolean;
  baseSalary: number;
  allowance: number;
  overtimeRate: number;
  extraCount: number;
  hardSalary: HardSalary;
}

export interface SalaryDetail {
  employeeId: string;
  hasSalary: boolean;
  /** Lương cứng đã dẫn xuất (hợp đồng + tăng lương). Chỉ đọc. */
  baseSalary: number;
  /** Mức nhập tay cũ ở hr_salaries — chỉ dùng khi nhân viên chưa có hợp đồng nào. */
  legacyBaseSalary: number;
  allowance: number;
  overtimeRate: number;
  components: SalaryComponent[];
  note: string;
  hardSalary: HardSalary;
}

export interface PayrollCompute {
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  period: string;
  baseSalary: number;
  allowance: number;
  overtimeRate: number;
  overtimePay: number;
  workedDays: number;
  absentDays: number;
  lateDays: number;
  overtimeHours: number;
  earnings: PayLine[];
  deductions: PayLine[];
  totalEarnings: number;
  totalDeductions: number;
  netPay: number;
  overtimeDays: OvertimeDay[];
  hardSalary: HardSalary;
}

export interface OvertimeDay {
  date: string;
  checkIn: string;
  checkOut: string | null;
  minutes: number;
}

export interface LeaveBalance {
  id: string;
  year: number;
  leaveType: string;
  totalDays: number;
  usedDays: number;
  remainingDays: number;
}

export interface EmployeeDoc {
  id: string;
  docType: string;
  title: string;
  issuedBy: string;
  issuedDate?: string | null;
  fileUrl: string;
  note: string;
}

export interface Shift {
  id: string;
  code: string;
  name: string;
  startTime: string;
  endTime: string;
  breakMinutes: number;
  lateGraceMinutes: number;
  standardHours: number;
  isOvernight: boolean;
}

export interface ShiftAssignment {
  id: string;
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  shiftId: string;
  shiftName: string;
  workDate: string;
  startTime: string;
  endTime: string;
  note: string;
}

export type HolidayType = "public" | "company";

export interface Holiday {
  id: string;
  holidayDate: string;
  name: string;
  holidayType: HolidayType | string;
  note: string;
  createdBy: string;
  createdAt: string;
}

export const holidayTypeLabel = (type: string) =>
  ({ public: "Lịch nhà nước", company: "Nghỉ công ty", weekly: "Chủ nhật" } as Record<string, string>)[type] ?? type;

export interface RequestType {
  type: string;
  label: string;
  category: string;
  /** Định nghĩa các trường nhập do server trả về (nguồn chuẩn). Dùng để dựng form động. */
  fields?: RequestField[];
}

export interface RequestListItem {
  id: string;
  requestNo: string;
  type: string;
  typeLabel: string;
  title: string;
  requesterUsername: string;
  employeeName: string;
  employeeCode: string;
  status: string;
  currentStep: number;
  totalSteps: number;
  createdAt: string;
}

export interface RequestApproval {
  stepNo: number;
  approverRole: string;
  approverUsername: string;
  approverName: string;
  status: string;
  decidedAt?: string | null;
  decidedBy: string;
  comment: string;
  hasSignature: boolean;
}

export interface RequestDetail {
  request: {
    id: string;
    requestNo: string;
    type: string;
    typeLabel: string;
    title: string;
    requesterUsername: string;
    employeeName: string;
    employeeCode: string;
    departmentName: string;
    payload: Record<string, unknown>;
    status: string;
    currentStep: number;
    createdAt: string;
  };
  approvals: RequestApproval[];
}

/** Một kỳ trong lịch khấu trừ phạt: kỳ "yyyy-MM", số tiền đợt đó, đã trừ (có phiếu lương) hay chưa. */
export interface PenaltyProgressPeriod {
  period: string;
  amount: number;
  paid: boolean;
  installmentNo: number;
}

/** Tiến trình khấu trừ phạt tiền (chỉ có ở phạt "fine" chưa miễn). */
export interface PenaltyProgress {
  total: number;
  deducted: number;
  remaining: number;
  /** Đã thu đủ (remaining ≤ 0) → đã tất toán. */
  settled: boolean;
  totalMonths: number;
  paidMonths: number;
  remainingMonths: number;
  nextPeriod?: string | null;
  nextAmount: number;
  periods: PenaltyProgressPeriod[];
}

export interface Penalty {
  id: string;
  penaltyNo: string;
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  penaltyType: string;
  penaltyTypeLabel: string;
  penaltyDate?: string | null;
  amount: number;
  installments: number;
  startPeriod: string;
  reason: string;
  note: string;
  status: string;
  createdBy: string;
  createdAt: string;
  progress?: PenaltyProgress | null;
}

export interface PenaltyType {
  type: string;
  label: string;
}

export interface PenaltyDeductionItem {
  penaltyNo: string;
  reason: string;
  amount: number;
  installments: number;
  installmentNo: number;
  monthAmount: number;
}

export interface PenaltyDeductions {
  total: number;
  items: PenaltyDeductionItem[];
}

/** Khoản hoàn tiền phạt (sinh khi khiếu nại được duyệt & tiền đã trừ) — kế toán duyệt trước khi trả. */
export interface PenaltyRefund {
  id: string;
  refundNo: string;
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  penaltyNo: string;
  appealRequestNo: string;
  amount: number;
  reason: string;
  status: string; // PendingAccounting | Approved | Paid | Rejected
  payoutMethod: string; // '' | payroll | cash
  appliedPeriod: string;
  createdBy: string;
  approvedBy: string;
  note: string;
  createdAt: string;
  decidedAt?: string | null;
}

/**
 * Chia số tiền phạt thành lịch trừ theo tháng: mỗi tháng (trừ tháng cuối) trừ phần đã làm tròn LÊN
 * đến hàng chục của (tổng / số tháng); tháng cuối trừ nốt phần còn lại. Khớp với backend BuildSchedule.
 */
export function penaltySchedule(amount: number, installments: number): number[] {
  if (!(amount > 0)) return [];
  const n = Math.min(60, Math.max(1, Math.floor(installments) || 1));
  const monthly = Math.ceil(amount / n / 10) * 10;
  const result: number[] = [];
  let remaining = amount;
  for (let i = 1; i <= n && remaining > 0; i++) {
    const take = i === n ? remaining : Math.min(monthly, remaining);
    result.push(take);
    remaining -= take;
  }
  return result;
}

export interface BankAccount {
  id: string;
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  bank: string;
  accountNumber: string;
  accountHolder: string;
  branch: string;
  isDefault: boolean;
  note: string;
}

/** Thương hiệu ngân hàng: nền thẻ tự đồng bộ theo mã ngân hàng của từng tài khoản nhân viên. */
export interface BankBrand {
  code: string;
  name: string;
  shortName: string;
  /** Gradient nền thẻ (theo bộ nhận diện thương hiệu). */
  gradient: string;
  /** Màu quầng sáng đổ bóng dưới thẻ. */
  glow: string;
}

export const BANK_BRANDS: BankBrand[] = [
  {
    code: "vietcombank",
    name: "Ngân hàng TMCP Ngoại thương Việt Nam",
    shortName: "Vietcombank",
    gradient: "linear-gradient(135deg, #21b5a3 0%, #129887 45%, #0b665f 100%)",
    glow: "rgba(9, 143, 74, 0.45)",
  },
  {
    code: "sacombank",
    name: "Ngân hàng TMCP Sài Gòn Thương Tín",
    shortName: "Sacombank",
    gradient: "linear-gradient(135deg, #6f88ee 0%, #3457d5 45%, #102f66 100%)",
    glow: "rgba(15, 95, 189, 0.45)",
  },
];

export const bankBrand = (code: string): BankBrand =>
  BANK_BRANDS.find((b) => b.code === code) ?? BANK_BRANDS[0];

/** Che số tài khoản, chỉ hiện 4 số cuối theo nhóm 4 (giống thẻ ngân hàng). */
export const maskAccountNumber = (num: string): string => {
  const digits = (num || "").replace(/\s+/g, "");
  if (digits.length <= 4) return digits;
  const masked = "•".repeat(digits.length - 4) + digits.slice(-4);
  return masked.replace(/(.{4})/g, "$1 ").trim();
};

/** Nhóm số tài khoản theo cụm 4 để dễ đọc. */
export const groupAccountNumber = (num: string): string =>
  (num || "").replace(/\s+/g, "").replace(/(.{4})/g, "$1 ").trim();

export interface TimesheetDay {
  date: string;
  shiftName: string;
  holidayName?: string;
  holidayType?: string;
  checkIn?: string | null;
  checkOut?: string | null;
  lateMinutes: number;
  earlyMinutes: number;
  overtimeMinutes: number;
  workedHours: number;
  status: string;
}

export interface Timesheet {
  period: string;
  summary: {
    workedDays: number;
    absentDays: number;
    lateDays: number;
    earlyDays: number;
    totalLateMinutes: number;
    totalEarlyMinutes: number;
    totalOvertimeMinutes: number;
    totalWorkedHours: number;
  };
  days: TimesheetDay[];
}

// ----- Tiện ích hiển thị -----

export const requestStatusLabel = (s: string) =>
  ({ Pending: "Chờ duyệt", Approved: "Đã duyệt", Rejected: "Từ chối", Cancelled: "Đã hủy" } as Record<string, string>)[s] ?? s;

export const requestStatusColor = (s: string) =>
  ({ Pending: "warning", Approved: "success", Rejected: "danger", Cancelled: "muted" } as Record<string, string>)[s] ?? "muted";

export const timesheetStatusColor = (s: string) => {
  const normalized = (s || "").toLowerCase();
  if (normalized.includes("nghỉ") || normalized.includes("nghi ")) return "muted";
  if (s.includes("Đủ") || s === "Không phân ca") return "success";
  if (s.includes("muộn") || s.includes("sớm") || s.includes("Thiếu")) return "warning";
  if (s === "Vắng") return "danger";
  return "muted";
};

export const leaveTypeLabel = (t: string) =>
  ({ annual: "Phép năm", sick: "Nghỉ ốm", unpaid: "Không lương" } as Record<string, string>)[t] ?? t;

export const penaltyTypeLabel = (t: string) =>
  ({ reminder: "Nhắc nhở", warning: "Cảnh cáo", fine: "Phạt tiền", suspension: "Đình chỉ", other: "Khác" } as Record<string, string>)[t] ?? t;

export const penaltyTypeColor = (t: string) =>
  ({ reminder: "muted", warning: "warning", fine: "danger", suspension: "danger", other: "muted" } as Record<string, string>)[t] ?? "muted";

export const penaltyStatusLabel = (s: string) =>
  ({ Active: "Còn hiệu lực", Waived: "Đã miễn", Settled: "Đã tất toán" } as Record<string, string>)[s] ?? s;

export const penaltyStatusColor = (s: string) =>
  ({ Active: "warning", Waived: "muted", Settled: "success" } as Record<string, string>)[s] ?? "muted";

export const refundStatusLabel = (s: string) =>
  ({ PendingAccounting: "Chờ kế toán duyệt", Approved: "Đã duyệt", Paid: "Đã chi trả", Rejected: "Từ chối" } as Record<string, string>)[s] ?? s;

export const refundStatusColor = (s: string) =>
  ({ PendingAccounting: "warning", Approved: "success", Paid: "success", Rejected: "danger" } as Record<string, string>)[s] ?? "muted";

export const payoutMethodLabel = (m: string) =>
  ({ payroll: "Cộng vào lương", cash: "Chi tiền mặt" } as Record<string, string>)[m] ?? "";

/** Loại chi (danh mục quản trị tự thêm/sửa được). */
export interface PayoutCategory {
  id: string;
  code: string;
  name: string;
  description: string;
  isActive: boolean;
  /** Danh mục lõi (lương, hoàn tiền phạt) — hệ thống tự sinh phiếu nên không xóa/tắt được. */
  isSystem: boolean;
  sortOrder: number;
}

/** Phiếu chi: kế toán lập → người nhận xác nhận (nếu cần) → kế toán trưởng duyệt → thủ quỹ hoàn tất. */
export interface PayoutVoucher {
  id: string;
  voucherNo: string;
  categoryId?: string | null;
  categoryName: string;
  categoryCode: string;
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  amount: number;
  sourceKind: string; // manual | refund | payslip
  sourceNo: string;
  reason: string;
  note: string;
  status: string; // AwaitingScan | AwaitingApproval | Confirmed | Approved | Paid | Rejected | Cancelled
  createdBy: string;
  requiresRecipientConfirmation: boolean;
  confirmedAt?: string | null;
  confirmedBy: string;
  approvedBy: string;
  approvedAt?: string | null;
  paidAt?: string | null;
  completedBy: string;
  completedAt?: string | null;
  rejectedBy: string;
  rejectedAt?: string | null;
  rejectReason: string;
  cancelledBy: string;
  cancelledAt?: string | null;
  cancelReason: string;
  createdAt: string;
  /** Chỉ kế toán mới nhận được (server ẩn với người khác) — nội dung để vẽ mã QR. */
  qrValue?: string | null;
  qrExpiresAt?: string | null;
}

/** Một dòng sự kiện append-only của phiếu chi. before/after là snapshot server, không chứa mã QR. */
export interface PayoutVoucherEvent {
  id: string;
  action: string;
  actor: string;
  actorName: string;
  beforeStatus?: string | null;
  afterStatus?: string | null;
  note: string;
  before?: Record<string, unknown> | null;
  after?: Record<string, unknown> | null;
  occurredAt: string;
}

/** Khoản hoàn tiền phạt đang chờ chi — kế toán chọn là ra đúng số tiền phải chi. */
export interface PayoutRefundSource {
  id: string;
  refundNo: string;
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  penaltyNo: string;
  appealRequestNo: string;
  amount: number;
  reason: string;
  createdAt: string;
}

export interface PayoutSummary {
  month: string;
  totalPaid: number;
  totalPending: number;
  byCategory: {
    categoryId?: string | null;
    categoryName: string;
    count: number;
    paidAmount: number;
    pendingAmount: number;
  }[];
}

export const voucherStatusLabel = (s: string) =>
  ({
    AwaitingScan: "Chờ người nhận quét QR",
    AwaitingApproval: "Chờ duyệt chi",
    Confirmed: "Đã ký nhận · chờ duyệt chi",
    Approved: "Đã duyệt · chờ thủ quỹ",
    Paid: "Hoàn tất · đã chi",
    Rejected: "Đã từ chối",
    Cancelled: "Đã hủy",
  } as Record<string, string>)[s] ?? s;

export const voucherStatusColor = (s: string) =>
  ({ AwaitingScan: "warning", AwaitingApproval: "warning", Confirmed: "info", Approved: "purple", Paid: "success", Rejected: "danger", Cancelled: "muted" } as Record<string, string>)[s] ??
  "muted";

export const voucherEventLabel = (action: string) =>
  ({
    created: "Lập phiếu",
    qr_regenerated: "Tạo lại mã xác nhận",
    recipient_confirmed: "Người nhận xác nhận",
    amount_updated: "Cập nhật số tiền",
    approved: "Duyệt chi",
    rejected: "Từ chối",
    cancelled: "Hủy phiếu",
    completed: "Hoàn tất chi",
  } as Record<string, string>)[action] ?? action;

export const voucherSourceLabel = (k: string) =>
  ({ manual: "Nhập tay", refund: "Hoàn tiền phạt", payslip: "Phiếu lương" } as Record<string, string>)[k] ?? k;

export const docTypeLabel = (t: string) =>
  ({ degree: "Bằng cấp", certificate: "Chứng chỉ", reward: "Khen thưởng" } as Record<string, string>)[t] ?? t;

/** Cấu hình trường nhập cho từng loại đơn — frontend dựng form động. */
export interface RequestField {
  key: string;
  label: string;
  type: "text" | "date" | "number" | "textarea" | "time" | "money" | "select" | "checkboxes";
  hint?: string;
  required?: boolean;
  options?: { value: string; label: string }[];
}

/**
 * NGUỒN CHUẨN là server (`/api/requests/types` trả kèm `fields`). Bảng dưới chỉ là BẢN DỰ PHÒNG
 * dùng trước khi tải xong types / khi offline. Khi types về, {@link applyServerRequestFields} sẽ
 * GHI ĐÈ tại chỗ, nên mọi nơi đọc `requestFields[type]` tự động dùng định nghĩa mới nhất từ server.
 */
const fallbackRequestFields: Record<string, RequestField[]> = {
  leave: [
    { key: "fromDate", label: "Từ ngày", type: "date" },
    { key: "toDate", label: "Đến ngày", type: "date" },
    { key: "days", label: "Số ngày nghỉ", type: "number" },
    { key: "reason", label: "Lý do", type: "textarea" },
  ],
  sick: [
    { key: "fromDate", label: "Từ ngày", type: "date" },
    { key: "toDate", label: "Đến ngày", type: "date" },
    { key: "days", label: "Số ngày nghỉ", type: "number" },
    { key: "reason", label: "Lý do", type: "textarea" },
  ],
  overtime: [
    { key: "date", label: "Ngày tăng ca", type: "date" },
    { key: "fromTime", label: "Từ giờ", type: "time" },
    { key: "toTime", label: "Đến giờ", type: "time" },
    { key: "reason", label: "Nội dung công việc", type: "textarea" },
  ],
  attendance_fix: [
    { key: "date", label: "Ngày cần điều chỉnh", type: "date" },
    { key: "checkIn", label: "Giờ vào đúng", type: "time" },
    { key: "checkOut", label: "Giờ ra đúng", type: "time" },
    { key: "reason", label: "Lý do", type: "textarea" },
  ],
  forgot_checkin: [
    { key: "date", label: "Ngày quên chấm", type: "date" },
    {
      key: "direction",
      label: "Giờ chấm",
      type: "checkboxes",
      options: [
        { value: "in", label: "Giờ vào" },
        { value: "out", label: "Giờ ra" },
      ],
    },
    { key: "time", label: "Giờ thực tế", type: "time" },
    { key: "reason", label: "Lý do", type: "textarea" },
  ],
  shift_swap: [
    { key: "date", label: "Ngày đổi ca", type: "date" },
    { key: "withPerson", label: "Người nhận ca", type: "text" },
    { key: "reason", label: "Lý do", type: "textarea" },
  ],
  payment: [
    { key: "amount", label: "Số tiền (₫)", type: "money" },
    { key: "content", label: "Nội dung thanh toán", type: "textarea" },
  ],
  advance: [
    { key: "amount", label: "Số tiền tạm ứng (₫)", type: "money" },
    { key: "reason", label: "Lý do", type: "textarea" },
  ],
  purchase: [
    { key: "item", label: "Vật tư cần mua", type: "text" },
    { key: "quantity", label: "Số lượng", type: "number" },
    { key: "amount", label: "Dự trù chi phí (₫)", type: "money" },
    { key: "reason", label: "Mục đích", type: "textarea" },
  ],
  booking: [
    { key: "resource", label: "Xe / phòng họp", type: "text" },
    { key: "date", label: "Ngày sử dụng", type: "date" },
    { key: "fromTime", label: "Từ giờ", type: "time" },
    { key: "toTime", label: "Đến giờ", type: "time" },
    { key: "reason", label: "Mục đích", type: "textarea" },
  ],
  penalty_appeal: [
    {
      key: "appealKind",
      label: "Hình thức đề nghị",
      type: "select",
      options: [
        { value: "dispute", label: "Bỏ phạt" },
        { value: "reduce", label: "Giảm tiền" },
        { value: "installment", label: "Trả góp" },
      ],
    },
    { key: "penaltyNo", label: "Mã quyết định phạt", type: "text" },
    { key: "penaltyType", label: "Hình thức phạt", type: "text" },
    { key: "penaltyAmount", label: "Số tiền phạt hiện tại (₫)", type: "money" },
    { key: "requestedAmount", label: "Số tiền đề nghị còn lại (₫)", type: "money" },
    { key: "requestedMonths", label: "Số tháng muốn chia đóng", type: "number" },
    { key: "reason", label: "Lý do đề nghị", type: "textarea" },
  ],
};

/**
 * Registry định nghĩa field đang dùng: khởi tạo bằng bản dự phòng, được server ghi đè khi tải types.
 * Mọi nơi vẫn đọc `requestFields[type]` như cũ nên không phải sửa các nơi gọi.
 */
export const requestFields: Record<string, RequestField[]> = { ...fallbackRequestFields };

/** Nạp định nghĩa field từ server (gọi sau khi tải `/api/requests/types`). Ghi đè tại chỗ. */
export function applyServerRequestFields(types: RequestType[]): void {
  for (const t of types) {
    if (t.fields && t.fields.length > 0) requestFields[t.type] = t.fields;
  }
}

export const fieldLabel = (type: string, key: string) =>
  requestFields[type]?.find((f) => f.key === key)?.label ?? key;

/** Với trường dạng chọn, đổi giá trị lưu (vd "in") sang nhãn hiển thị (vd "Giờ vào"). */
export const fieldDisplayValue = (type: string, key: string, value: unknown) => {
  const f = requestFields[type]?.find((x) => x.key === key);
  if (f?.type === "select" || f?.type === "checkboxes") return f.options?.find((o) => o.value === String(value))?.label ?? String(value);
  return String(value);
};
