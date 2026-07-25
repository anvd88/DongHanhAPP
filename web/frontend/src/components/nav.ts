import {
  Banknote,
  BookOpen,
  Boxes,
  Building2,
  Calculator,
  CalendarCheck,
  Newspaper,
  CircleDollarSign,
  ClipboardCheck,
  Download,
  MessageCircle,
  ClipboardList,
  FileText,
  Gavel,
  History,
  IdCard,
  LayoutDashboard,
  MessageSquareWarning,
  ReceiptText,
  ScanFace,
  Send,
  Settings,
  ShoppingBag,
  ShoppingCart,
  Tags,
  UserCog,
  Users,
  Wallet,
  type LucideIcon,
} from "lucide-react";
import { PERM, type Permission } from "../lib/access";

/**
 * KHÔNG GIAN LÀM VIỆC của một mục menu:
 *  • "work"  — không gian làm việc của nhân viên (việc của chính mình).
 *  • "admin" — khu quản trị & nghiệp vụ (dữ liệu của người khác, cấu hình hệ thống).
 * Một tài khoản có cả hai thì được chuyển qua lại; chuyển chỉ đổi MENU, không đổi quyền.
 */
export type NavArea = "work" | "admin";

export interface NavItem {
  key: string;
  label: string;
  icon: LucideIcon;
  path: string;
  area: NavArea;
  /**
   * Quyền cần có để THẤY mục này. Bỏ trống = ai đăng nhập cũng thấy.
   * Đây chỉ là lọc hiển thị cho đỡ rối mắt — backend vẫn chốt lại ở mỗi request, nên gõ thẳng URL
   * của mục bị ẩn cũng không vào được.
   */
  permission?: Permission;
  /** Có ÍT NHẤT MỘT quyền trong danh sách thì thấy mục này. */
  permissionsAny?: Permission[];
  ready?: boolean;
}

export interface NavSection {
  title?: string;
  area: NavArea;
  items: NavItem[];
}

export const NAV: NavSection[] = [
  {
    area: "admin",
    items: [
      // Trang tổng quan đọc /api/dashboard — endpoint đòi quyền kế toán, nên nhân viên thường thấy
      // mục này chỉ để nhận về một trang trắng kèm 403. Lọc theo đúng quyền mà nó cần.
      { key: "dashboard", label: "Tổng quan", icon: LayoutDashboard, path: "/dashboard", area: "admin", permission: PERM.accountingAccess, ready: true },
    ],
  },
  {
    title: "CÁ NHÂN",
    area: "work",
    items: [
      { key: "nhan-su-portal", label: "Không gian của tôi", icon: IdCard, path: "/nhan-su", area: "work", permission: PERM.hrSelfAccess, ready: true },
      { key: "cong-viec", label: "Việc được giao", icon: ClipboardCheck, path: "/cong-viec", area: "work", permission: PERM.tasksSelf, ready: true },
      { key: "chamcong", label: "Chấm công", icon: ScanFace, path: "/chamcong", area: "work", permission: PERM.attendanceSelf, ready: true },
      { key: "bangcong", label: "Bảng công của tôi", icon: CalendarCheck, path: "/bangcong", area: "work", permission: PERM.attendanceSelf, ready: true },
      { key: "dontu", label: "Đơn từ", icon: Send, path: "/dontu", area: "work", permission: PERM.requestsSelf, ready: true },
      { key: "pheduyet", label: "Chờ tôi duyệt", icon: ClipboardCheck, path: "/pheduyet", area: "work", permission: PERM.requestsApprove, ready: true },
      { key: "phat", label: "Phạt & kỷ luật", icon: Gavel, path: "/phat", area: "work", permission: PERM.penaltyRead, ready: true },
      { key: "chats", label: "Trò chuyện", icon: MessageCircle, path: "/chats", area: "work", permission: PERM.chatAccess, ready: true },
      { key: "congcu", label: "Công cụ", icon: Calculator, path: "/tinhtoan", area: "work", ready: true },
      { key: "tai-apk", label: "Tải APK", icon: Download, path: "/tai-apk", area: "work", ready: true },
      { key: "phanhoi", label: "Phản hồi", icon: MessageSquareWarning, path: "/phanhoi", area: "work", ready: true },
      { key: "hethong", label: "Hệ thống", icon: Settings, path: "/caidat", area: "work", ready: true },
    ],
  },
  {
    title: "NGHIỆP VỤ",
    area: "admin",
    items: [
      { key: "giacong", label: "Gia công", icon: Building2, path: "/giacong", area: "admin", permission: PERM.accountingAccess, ready: true },
      { key: "ketoan", label: "Kế toán", icon: FileText, path: "/ketoan", area: "admin", permission: PERM.accountingAccess, ready: true },
      // "Phiếu chi tiền mặt" chứ không phải "Phiếu chi" — trang Kế toán đã có tab chứng từ tên Phiếu chi.
      { key: "phieu-chi", label: "Phiếu chi tiền mặt", icon: ReceiptText, path: "/phieu-chi", area: "admin", permission: PERM.payoutRead, ready: true },
      { key: "khachhang", label: "Khách hàng", icon: Users, path: "/khachhang", area: "admin", permission: PERM.accountingAccess, ready: true },
      { key: "muahang", label: "Mua hàng", icon: ShoppingCart, path: "/muahang", area: "admin", permission: PERM.accountingAccess },
      { key: "kho", label: "Kho hàng", icon: ShoppingBag, path: "/kho", area: "admin", permission: PERM.accountingAccess },
      { key: "nganhang", label: "Ngân hàng", icon: Banknote, path: "/nganhang", area: "admin", permission: PERM.accountingAccess },
      { key: "congno", label: "Công nợ", icon: Wallet, path: "/congno", area: "admin", permission: PERM.accountingAccess },
      { key: "taisan", label: "Tài sản cố định", icon: Boxes, path: "/taisan", area: "admin", permission: PERM.accountingAccess },
      { key: "chiphi", label: "Chi phí", icon: CircleDollarSign, path: "/chiphi", area: "admin", permission: PERM.accountingAccess },
    ],
  },
  {
    title: "QUẢN LÝ",
    area: "admin",
    items: [
      { key: "chamcong-ql", label: "Quản lý chấm công", icon: ClipboardList, path: "/ql-chamcong", area: "admin", permission: PERM.attendanceManage, ready: true },
      { key: "quanly-nhansu", label: "Quản lý nhân sự", icon: UserCog, path: "/quanly-nhansu", area: "admin", permissionsAny: [PERM.hrManage, PERM.usersManage], ready: true },
      { key: "cong-thong-tin", label: "Cổng thông tin", icon: Newspaper, path: "/cong-thong-tin", area: "admin", permission: PERM.portalManage, ready: true },
      { key: "quanly-dontu", label: "Quản lý đơn từ", icon: ClipboardCheck, path: "/quanly-dontu", area: "admin", permission: PERM.requestsManage, ready: true },
      { key: "quanly-bangcong", label: "Quản lý bảng công", icon: CalendarCheck, path: "/quanly-bangcong", area: "admin", permission: PERM.attendanceRead, ready: true },
      { key: "bang-luong", label: "Bảng lương", icon: Wallet, path: "/bang-luong", area: "admin", permission: PERM.payrollRead, ready: true },
      { key: "baocao", label: "Báo cáo", icon: BookOpen, path: "/baocao", area: "admin", permission: PERM.reportRead, ready: true },
      { key: "danhmuc", label: "Danh mục", icon: Tags, path: "/danhmuc", area: "admin", permission: PERM.accountingAccess },
      // Admin xem toàn bộ; kế toán vào cùng trang nhưng server chỉ trả phần thu chi tiền mặt.
      { key: "nhat-ky", label: "Nhật ký hoạt động", icon: History, path: "/saoluu", area: "admin", permission: PERM.auditRead, ready: true },
    ],
  },
];

/** Mục menu hiện ra cho một tập quyền, đã lọc theo không gian làm việc đang xem. */
export function visibleNav(area: NavArea, can: (p: Permission) => boolean): NavSection[] {
  return NAV
    .filter((section) => section.area === area)
    .map((section) => ({
      ...section,
      items: section.items.filter((it) =>
        (!it.permission || can(it.permission)) &&
        (!it.permissionsAny || it.permissionsAny.some(can))
      ),
    }))
    .filter((section) => section.items.length > 0);
}

/** Có mục nào trong không gian này mà tài khoản được thấy không (để biết có cần nút chuyển không gian). */
export function hasArea(area: NavArea, can: (p: Permission) => boolean): boolean {
  return visibleNav(area, can).length > 0;
}
