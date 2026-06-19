export interface User {
  id: string;
  username: string;
  fullName: string;
  role: string;
  isActive: boolean;
  approvalStatus: string;
  createdAt?: string;
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
  customerName: string;
  content: string;
  total: number;
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
  role: string;
  isActive: boolean;
  approvalStatus: string;
  createdAt?: string;
}

export interface GiaCongLine {
  id: number;
  loaiDong: string;
  maHang: string;
  tenHang: string;
  donViTinh: string;
  soLuong: number;
  donGiaGiaCong: number;
  trangThaiDong: string;
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
  trangThai: string;
  tienDo: number;
  buocHienTai: number;
  soMatHang: number;
  tongGiaTri: number;
}
export interface GiaCongDetail extends Omit<GiaCongListItem, "soMatHang" | "tongGiaTri"> {
  ghiChu: string;
  lines: GiaCongLine[];
}

export interface Release {
  id: number;
  version: string;
  releaseNotes: string;
  isMandatory: boolean;
  isPublished: boolean;
  publishedAt: string;
  publishedBy: string;
}
