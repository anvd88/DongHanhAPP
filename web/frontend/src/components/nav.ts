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
  History,
  LayoutDashboard,
  MessageSquareWarning,
  ReceiptText,
  Settings,
  ShoppingBag,
  ShoppingCart,
  Tags,
  UserCog,
  Users,
  Wallet,
  type LucideIcon,
} from "lucide-react";

export interface NavItem {
  key: string;
  label: string;
  icon: LucideIcon;
  path: string;
  adminOnly?: boolean;
  /** Chỉ hiện với role kế toán (và admin). Quyền thật vẫn do server chốt — đây chỉ là lọc menu. */
  accountingOnly?: boolean;
  ready?: boolean;
}

export interface NavSection {
  title?: string;
  items: NavItem[];
}

export const NAV: NavSection[] = [
  {
    items: [{ key: "dashboard", label: "Tổng quan", icon: LayoutDashboard, path: "/dashboard", ready: true }],
  },
  {
    title: "NGHIỆP VỤ",
    items: [
      { key: "giacong", label: "Gia công", icon: Building2, path: "/giacong", ready: true },
      { key: "ketoan", label: "Kế toán", icon: FileText, path: "/ketoan", ready: true },
      // "Phiếu chi tiền mặt" chứ không phải "Phiếu chi" — trang Kế toán đã có tab chứng từ tên Phiếu chi.
      { key: "phieu-chi", label: "Phiếu chi tiền mặt", icon: ReceiptText, path: "/phieu-chi", accountingOnly: true, ready: true },
      { key: "khachhang", label: "Khách hàng", icon: Users, path: "/khachhang", ready: true },
      { key: "muahang", label: "Mua hàng", icon: ShoppingCart, path: "/muahang" },
      { key: "kho", label: "Kho hàng", icon: ShoppingBag, path: "/kho" },
      { key: "nganhang", label: "Ngân hàng", icon: Banknote, path: "/nganhang" },
      { key: "congno", label: "Công nợ", icon: Wallet, path: "/congno" },
      { key: "taisan", label: "Tài sản cố định", icon: Boxes, path: "/taisan" },
      { key: "chiphi", label: "Chi phí", icon: CircleDollarSign, path: "/chiphi" },
    ],
  },
  {
    title: "CÔNG CỤ",
    items: [
      { key: "congcu", label: "Công cụ", icon: Calculator, path: "/tinhtoan", ready: true },
      { key: "cong-viec", label: "Việc được giao", icon: ClipboardCheck, path: "/cong-viec", ready: true },
      { key: "chats", label: "Trò chuyện", icon: MessageCircle, path: "/chats", ready: true },
      { key: "tai-apk", label: "Tải APK", icon: Download, path: "/tai-apk", ready: true },
    ],
  },
  {
    title: "QUẢN LÝ",
    items: [
      { key: "nhansu", label: "Tài khoản", icon: Users, path: "/nhansu", adminOnly: true, ready: true },
      { key: "chamcong-ql", label: "Quản lý chấm công", icon: ClipboardList, path: "/ql-chamcong", adminOnly: true, ready: true },
      { key: "quanly-nhansu", label: "Quản lý nhân sự", icon: UserCog, path: "/quanly-nhansu", adminOnly: true, ready: true },
      { key: "cong-thong-tin", label: "Cổng thông tin", icon: Newspaper, path: "/cong-thong-tin", adminOnly: true, ready: true },
      { key: "quanly-dontu", label: "Quản lý đơn từ", icon: ClipboardCheck, path: "/quanly-dontu", adminOnly: true, ready: true },
      { key: "quanly-bangcong", label: "Quản lý bảng công", icon: CalendarCheck, path: "/quanly-bangcong", adminOnly: true, ready: true },
      { key: "bang-luong", label: "Bảng lương", icon: Wallet, path: "/bang-luong", adminOnly: true, ready: true },
      { key: "baocao", label: "Báo cáo", icon: BookOpen, path: "/baocao", ready: true },
      { key: "danhmuc", label: "Danh mục", icon: Tags, path: "/danhmuc" },
      // Admin xem toàn bộ; kế toán vào cùng trang nhưng server chỉ trả phần thu chi tiền mặt.
      { key: "nhat-ky", label: "Nhật ký hoạt động", icon: History, path: "/saoluu", accountingOnly: true, ready: true },
      { key: "hethong", label: "Hệ thống", icon: Settings, path: "/caidat", ready: true },
      { key: "phanhoi", label: "Phản hồi", icon: MessageSquareWarning, path: "/phanhoi", ready: true },
    ],
  },
];
