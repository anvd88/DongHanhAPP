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

// Vai trò truy cập (chức vụ phân quyền) của nhân viên.
export type AccessRole = "staff" | "dept_manager" | "location_manager";
export const ACCESS_ROLES: { value: AccessRole; label: string }[] = [
  { value: "staff", label: "Nhân viên" },
  { value: "dept_manager", label: "Quản lý phòng ban" },
  { value: "location_manager", label: "Quản lý địa điểm" },
];
export function accessRoleLabel(role?: string | null): string {
  return ACCESS_ROLES.find((r) => r.value === role)?.label ?? "Nhân viên";
}

export interface EmployeeCard {
  id: string;
  employeeCode: string;
  username: string;
  fullName: string;
  position: string;
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
  hireDate?: string | null;
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
}

export interface SalaryDetail {
  employeeId: string;
  hasSalary: boolean;
  baseSalary: number;
  allowance: number;
  overtimeRate: number;
  components: SalaryComponent[];
  note: string;
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

/** Tiến trình khấu trừ phạt tiền (chỉ có ở phạt "fine" còn hiệu lực). */
export interface PenaltyProgress {
  total: number;
  deducted: number;
  remaining: number;
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
    gradient: "linear-gradient(135deg, #0aa14f 0%, #067a3a 45%, #024f27 100%)",
    glow: "rgba(9, 143, 74, 0.45)",
  },
  {
    code: "sacombank",
    name: "Ngân hàng TMCP Sài Gòn Thương Tín",
    shortName: "Sacombank",
    gradient: "linear-gradient(135deg, #1573d4 0%, #0d4ea3 45%, #082a63 100%)",
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
  ({ Active: "Còn hiệu lực", Waived: "Đã miễn" } as Record<string, string>)[s] ?? s;

export const refundStatusLabel = (s: string) =>
  ({ PendingAccounting: "Chờ kế toán duyệt", Approved: "Đã duyệt", Paid: "Đã chi trả", Rejected: "Từ chối" } as Record<string, string>)[s] ?? s;

export const refundStatusColor = (s: string) =>
  ({ PendingAccounting: "warning", Approved: "success", Paid: "success", Rejected: "danger" } as Record<string, string>)[s] ?? "muted";

export const payoutMethodLabel = (m: string) =>
  ({ payroll: "Cộng vào lương", cash: "Chi tiền mặt" } as Record<string, string>)[m] ?? "";

export const docTypeLabel = (t: string) =>
  ({ degree: "Bằng cấp", certificate: "Chứng chỉ", reward: "Khen thưởng" } as Record<string, string>)[t] ?? t;

/** Cấu hình trường nhập cho từng loại đơn — frontend dựng form động. */
export interface RequestField {
  key: string;
  label: string;
  type: "text" | "date" | "number" | "textarea" | "time" | "money" | "select" | "checkboxes";
  options?: { value: string; label: string }[];
}

export const requestFields: Record<string, RequestField[]> = {
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
    { key: "penaltyNo", label: "Mã quyết định phạt", type: "text" },
    { key: "penaltyType", label: "Hình thức phạt", type: "text" },
    { key: "penaltyAmount", label: "Số tiền phạt (₫)", type: "money" },
    { key: "reason", label: "Nội dung khiếu nại", type: "textarea" },
  ],
};

export const fieldLabel = (type: string, key: string) =>
  requestFields[type]?.find((f) => f.key === key)?.label ?? key;

/** Với trường dạng chọn, đổi giá trị lưu (vd "in") sang nhãn hiển thị (vd "Giờ vào"). */
export const fieldDisplayValue = (type: string, key: string, value: unknown) => {
  const f = requestFields[type]?.find((x) => x.key === key);
  if (f?.type === "select" || f?.type === "checkboxes") return f.options?.find((o) => o.value === String(value))?.label ?? String(value);
  return String(value);
};
