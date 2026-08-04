export interface User {
  id: string;
  username: string;
  fullName: string;
  email: string;
  role: string;
  isActive: boolean;
  approvalStatus: string;
  createdAt?: string;
  /** Ảnh đại diện (data URL) lưu riêng cho bản web; rỗng/null → hiển thị chữ cái đầu. */
  avatarUrl?: string | null;
  /** Tích xanh của tài khoản, do máy chủ quyết định. */
  verified?: boolean;
  isDiamond?: boolean;
  /** MỌI vai trò (vai trò chính + vai trò phụ như "Warehouse"/Thủ kho). */
  roles?: string[];
  /** Cờ tương thích API cũ; giao diện mới dùng permission tasks.assign từ access profile. */
  canAssignTasks?: boolean;
}

export interface Dashboard {
  activeCustomers: number;
  totalDocuments: number;
  totalPayments: number;
  monthRevenue: number;
  month: number;
  year: number;
  recent: DocumentListItem[];
}

export interface DocumentListItem {
  id: string;
  voucherNo: string;
  date: string;
  documentType?: string;
  customerName: string;
  content: string;
  total: number;
  createdBy?: string;
  issuedAt?: string | null;
  cancelledAt?: string | null;
  cancelledBy?: string;
  cancelReason?: string;
}

export interface AccountingSystemStatus {
  server: {
    online: boolean;
    name: string;
    printServiceReady: boolean;
    message: string;
    uptimeSeconds: number;
  };
  printer: {
    available: boolean;
    ready: boolean;
    name?: string | null;
    message: string;
    queuedJobs: number;
    statusCode: number;
  };
  checkedAt: string;
}

export interface DocumentLine {
  lineContent: string;
  spec: string;
  quantity: number;
  unitPrice: number;
  note: string;
}
export interface DocumentDetail {
  id: string;
  voucherNo: string;
  date: string;
  customerName: string;
  content: string;
  note: string;
  lines: DocumentLine[];
  issuedAt?: string | null;
  cancelledAt?: string | null;
  cancelledBy?: string;
  cancelReason?: string;
}

export interface Customer {
  id: string;
  name: string;
  taxCode: string;
  phone: string;
  address: string;
  isActive: boolean;
}

export interface CustomerReport {
  customer: Customer;
  documentCount: number;
  total: number;
  receiptTotal: number;
  paymentTotal: number;
  salesTotal: number;
  documents: DocumentListItem[];
}

export interface DebtSummary {
  customer: Customer;
  openingBalance: number;
  openingDate?: string | null;
  openingNote: string;
  salesTotal: number;
  collectedTotal: number;
  balance: number;
  invoiceCount: number;
  lastActivityDate?: string | null;
}

export interface DebtOverview {
  totalOpeningBalance: number;
  totalSales: number;
  totalCollected: number;
  totalReceivable: number;
  debtorCount: number;
  customers: DebtSummary[];
}

export type DebtTransactionKind = "opening" | "sale" | "receipt" | "payment";

export interface DebtTransaction {
  id: string;
  date: string;
  reference: string;
  kind: DebtTransactionKind;
  description: string;
  debit: number;
  credit: number;
  runningBalance: number;
  cancelled: boolean;
}

export interface DebtDetail {
  customer: Customer;
  summary: DebtSummary;
  transactions: DebtTransaction[];
}

export interface Reports {
  totalPayments: number;
  monthRevenue: number;
  totalDocuments: number;
  activeCustomers: number;
  monthly: { year: number; month: number; documentCount: number; paymentCount: number; total: number }[];
}

export interface AuditEntry {
  occurredAt: string;
  username: string;
  action: string;
  entity: string;
  entityName: string;
  details: string;
}

export interface UserAdmin {
  id: string;
  username: string;
  fullName: string;
  email: string;
  role: string;
  isActive: boolean;
  approvalStatus: string;
  createdAt?: string;
  isOnline: boolean;
  lastSeen?: string;
  verified: boolean;
  isDiamond: boolean;
  /** Vai trò phụ đã cấp thêm (vd "Warehouse" = Thủ kho). */
  secondaryRoles: string[];
  /** Vai trò được đồng bộ từ chức vụ chính/kiêm nhiệm; client không được sửa vai trò trực tiếp. */
  rolesManagedByPositions?: boolean;
}

export interface FeedbackItem {
  id: number;
  type: "ChatReport" | "AttendanceIssue" | string;
  typeLabel: string;
  reporterUsername: string;
  reporterName: string;
  targetName: string;
  reason: string;
  conversationId?: string | null;
  createdAt: string;
}

// ----- Chat (Trò chuyện) -----
export interface ChatContact {
  username: string;
  displayName: string;
  avatarUrl?: string | null;
  isOnline: boolean;
  verified: boolean;
  isDiamond: boolean;
  role: string;
}
export interface ChatConversation {
  id: string;
  isGroup: boolean;
  title: string;
  username?: string | null;
  avatarUrl?: string | null;
  isOnline: boolean;
  verified: boolean;
  isDiamond: boolean;
  preview: string;
  lastAt?: string | null;
  unread: number;
  /** Thời điểm hoạt động cuối của người kia (UTC); null nếu chưa từng online. */
  lastSeen?: string | null;
  pinned?: boolean;
  supportConversation?: boolean;
}
export interface ChatReaction {
  emoji: string;
  count: number;
  /** Người đang đăng nhập có thả biểu cảm này không. */
  mine: boolean;
}
export interface ChatMessage {
  id: number;
  senderUsername: string;
  senderName: string;
  mine: boolean;
  body: string;
  createdAt: string;
  editedAt?: string | null;
  removed: boolean;
  forwarded: boolean;
  reactions?: ChatReaction[] | null;
  /** "voice" có blob bền vững; "file" là tệp LAN/store-and-forward; "text" là mặc định. */
  kind?: "text" | "file" | "voice";
  fileName?: string | null;
  fileSize?: number | null;
  fileMime?: string | null;
  /** Nội dung đang sẵn sàng tải; với voice cờ này không bị tắt sau lượt tải đầu. */
  hasBlob?: boolean;
  /** Ít nhất một thành viên khác đã đọc tin nhắn (cùng contract với Ketoanapk). */
  read?: boolean;
}

// Dung lượng DB mục Trò chuyện (admin xem trong trang Hệ thống). Đơn vị KB.
export interface ChatTableUsage {
  table: string;
  label: string;
  rows: number;
  dataKb: number;
  indexKb: number;
  totalKb: number;
}
export interface ChatDbUsage {
  totalKb: number;
  dataKb: number;
  indexKb: number;
  messageCount: number;
  conversationCount: number;
  memberCount: number;
  databaseTotalKb: number;
  tables: ChatTableUsage[];
}

export interface GiaCongLine {
  id: number;
  loaiDong: string;
  maHang: string;
  tenHang: string;
  quyCach: string;
  donViTinh: string;
  soLuong: number;
  donGiaGiaCong: number;
  ghiChu: string;
}
export interface GiaCongListItem {
  id: number;
  maPhieu: string;
  loaiPhieu: string;
  doiTac: string;
  nhanVienPhuTrach: string;
  ngayLap: string;
  hanHoanThanh?: string;
  soMatHang: number;
  tongGiaTri: number;
  soLuongXuat: number;
  soLuongNhap: number;
  soLuongConTaiCongTy: number;
  tienGiaCongPhaiTra: number;
}
export interface GiaCongDetail {
  id: number;
  maPhieu: string;
  loaiPhieu: string;
  doiTac: string;
  nhanVienPhuTrach: string;
  ngayLap: string;
  hanHoanThanh?: string;
  ghiChu: string;
  lines: GiaCongLine[];
}

export interface GiaCongReportPartner {
  doiTac: string;
  soLuongXuat: number;
  soLuongNhap: number;
  soLuongConTaiCongTy: number;
  tienGiaCongPhaiTra: number;
}

export interface GiaCongReportItem extends GiaCongReportPartner {
  tenHang: string;
  quyCach: string;
  donViTinh: string;
}

export interface GiaCongReport {
  soLuongXuat: number;
  soLuongNhap: number;
  soLuongConTaiCongTy: number;
  tienGiaCongPhaiTra: number;
  partners: GiaCongReportPartner[];
  items: GiaCongReportItem[];
}

export interface FaceEngineStatus {
  engine: string;
  matchThreshold: number;
}
export interface FaceNguoiDung {
  username: string;
  fullName: string;
  soMau: number;
  createdAt?: string;
}
export interface FaceRegistrationLog {
  id: number;
  username: string;
  fullName: string;
  createdAt: string;
  createdBy: string;
}
export interface NhanDienResult {
  matched: boolean;
  username?: string;
  fullName?: string;
  similarity: number;
  loai?: string;
  occurredAt?: string;
  message: string;
}
export interface ChamCongLog {
  id: number;
  username: string;
  fullName: string;
  loai: string;
  similarity: number;
  occurredAt: string;
  ghiChu: string;
}

export type ChamCongStatus =
  | "ok"
  | "posture"
  | "lowquality"
  | "noface"
  | "spoof"
  | "unknown"
  | "proxy" // khớp khuôn mặt nhân viên KHÁC khi chấm công cho chính mình → bị chặn
  | "offline"
  | "pending"; // đồng bộ ngoại tuyến → chờ quản lý duyệt

/** Bản chấm công ngoại tuyến chờ duyệt (kèm cờ rủi ro) — màn quản lý. */
export interface ChamCongOffline {
  id: number;
  username: string;
  fullName: string;
  loai: string;
  similarity: number;
  quality: number;
  occurredAt: string;
  syncedAt: string;
  backdateMinutes: number;
  clientIp: string;
  onCompanyLan: boolean;
  gpsLat?: number | null;
  gpsLng?: number | null;
  distanceM?: number | null;
  inGeofence?: boolean | null;
  flags: string;
  status: "pending" | "approved" | "rejected";
  reviewedBy: string;
  reviewedAt?: string | null;
  reviewNote: string;
}

/** Kết quả chấm công theo loạt ảnh (server tự chọn khung tốt nhất). */
export interface ChamCongResult {
  status: ChamCongStatus;
  matched: boolean;
  username?: string;
  fullName?: string;
  similarity: number;
  loai?: string;
  occurredAt?: string;
  quality: number;
  message: string;
  guidance?: string;
}

/** Cấu hình chính sách chấm công ngoại tuyến (geofence công ty + ngưỡng lùi giờ). */
export interface OfflineConfig {
  geofenceLat: number | null;
  geofenceLng: number | null;
  geofenceRadiusM: number;
  maxBackdateMinutes: number;
}

/** Cấu hình liveness quay đầu (challenge-response). */
export interface MotionConfig {
  /** Bật = app yêu cầu quay đầu lúc quét. */
  enabled: boolean;
  /** Chặn nếu biên độ quay quá nhỏ (nghi ảnh tĩnh) hay chỉ ghi log. */
  enforce: boolean;
}

/** Một lượt đo Silent-Face (chống ảnh/màn hình): điểm P(real) cao nhất/trung bình/nhì trong các khung. */
export interface LivenessMetric {
  atUtc: string;
  user: string;
  best: number;
  mean: number;
  second: number;
  frames: number;
  threshold: number;
  passed: boolean;
  /** Biên độ góc quay đầu (yaw span) của lượt; < 0 nghĩa là lượt không kiểm tra chuyển động. */
  motionSpan: number;
  /** Độ mở mắt cao nhất của loạt (0..1); < 0 nghĩa là không đo được. */
  eyeOpen?: number;
}

/** Cấu hình kiểm tra MỞ MẮT phía server: chặn khi độ mở mắt < ngưỡng. Mặc định chỉ đo (enforce=false). */
export interface EyeOpenConfig {
  enforce: boolean;
  threshold: number;
}

/**
 * Mức chống giả mạo ĐANG chạy thật trên máy chủ.
 * "None" = không nạp được model nào ⇒ mọi ảnh đều được coi là người thật (chấm công vẫn chạy bình
 * thường, không có triệu chứng gì khác) — panel phải cảnh báo đỏ.
 */
export interface AntiSpoofStatus {
  level: "Full" | "Basic" | "None";
  detail: string;
}

/** Dữ liệu panel "Chống ảnh/màn hình giả": trạng thái model + cấu hình mở mắt + các lượt đo gần nhất. */
export interface LivenessPanel {
  antiSpoof: AntiSpoofStatus;
  metrics: LivenessMetric[];
  eyeOpen?: EyeOpenConfig | null;
}

export interface Release {
  id: number;
  appTarget: string;
  version: string;
  versionCode: number;
  releaseNotes: string;
  isMandatory: boolean;
  isPublished: boolean;
  publishedAt: string;
  publishedBy: string;
  apkFileName: string;
  apkSize: number;
  apkSha256: string;
}

export interface AppUpdateInfo {
  hasUpdate: boolean;
  id?: number;
  appTarget?: string;
  currentVersionCode?: number;
  version?: string;
  versionCode?: number;
  releaseNotes?: string;
  isMandatory?: boolean;
  publishedAt?: string;
  apkFileName?: string;
  apkSize?: number;
  apkSha256?: string;
  downloadUrl?: string;
}
