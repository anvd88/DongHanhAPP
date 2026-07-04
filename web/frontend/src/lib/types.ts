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
  /** Tích xanh (giống Facebook): Admin luôn có, hoặc được admin cấp thủ công. */
  verified?: boolean;
  isDiamond?: boolean;
}
export const isAdmin = (u?: User | null) => u?.role?.toLowerCase() === "admin";

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
  /** "text" (mặc định) hoặc "file" — tin nhắn ghi lại một tệp đã gửi qua LAN (chỉ metadata). */
  kind?: "text" | "file";
  fileName?: string | null;
  fileSize?: number | null;
  fileMime?: string | null;
  /** Server đang giữ tạm nội dung tệp (người nhận offline lúc gửi) → có thể bấm Tải xuống. */
  hasBlob?: boolean;
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
