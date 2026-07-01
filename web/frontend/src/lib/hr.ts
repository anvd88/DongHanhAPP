// Kiểu dữ liệu & tiện ích dùng chung cho khối Nhân sự (hồ sơ, đơn từ, chấm công/ca làm, quyền lợi).

export interface Department {
  id: string;
  code: string;
  name: string;
  parentId?: string | null;
  parentName: string;
  managerEmployeeId?: string | null;
  managerName: string;
  employeeCount: number;
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
  managerName: string;
}

export interface EmployeeDetail extends EmployeeCard {
  dob?: string | null;
  gender: string;
  address: string;
  managerId?: string | null;
  hireDate?: string | null;
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
  published: boolean;
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

export interface TimesheetDay {
  date: string;
  shiftName: string;
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
  if (s.includes("Đủ") || s === "Không phân ca") return "success";
  if (s.includes("muộn") || s.includes("sớm") || s.includes("Thiếu")) return "warning";
  if (s === "Vắng") return "danger";
  return "muted";
};

export const leaveTypeLabel = (t: string) =>
  ({ annual: "Phép năm", sick: "Nghỉ ốm", unpaid: "Không lương" } as Record<string, string>)[t] ?? t;

export const docTypeLabel = (t: string) =>
  ({ degree: "Bằng cấp", certificate: "Chứng chỉ", reward: "Khen thưởng" } as Record<string, string>)[t] ?? t;

/** Cấu hình trường nhập cho từng loại đơn — frontend dựng form động. */
export interface RequestField {
  key: string;
  label: string;
  type: "text" | "date" | "number" | "textarea" | "time" | "money";
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
};

export const fieldLabel = (type: string, key: string) =>
  requestFields[type]?.find((f) => f.key === key)?.label ?? key;
