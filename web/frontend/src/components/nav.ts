import {
  LayoutDashboard, BookOpen, Boxes, ShoppingCart, ShoppingBag, Factory, Building2,
  Users, FileBarChart, Tags, Wallet, Settings, DatabaseBackup, CalendarClock, Plug, RefreshCw,
  type LucideIcon,
} from "lucide-react";

export interface NavItem {
  key: string;
  label: string;
  icon: LucideIcon;
  path: string;
  adminOnly?: boolean;
  ready?: boolean; // đã hiện thực; false = "Module đang phát triển"
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
      { key: "ketoan", label: "Kế toán", icon: BookOpen, path: "/ketoan", ready: true },
      { key: "kho", label: "Hàng tồn kho", icon: Boxes, path: "/kho" },
      { key: "banhang", label: "Bán hàng", icon: ShoppingCart, path: "/banhang", ready: true },
      { key: "muahang", label: "Mua hàng", icon: ShoppingBag, path: "/muahang" },
      { key: "giacong", label: "Gia công", icon: Factory, path: "/giacong", ready: true },
      { key: "taisan", label: "Tài sản cố định", icon: Building2, path: "/taisan" },
    ],
  },
  {
    title: "QUẢN LÝ",
    items: [
      { key: "nhansu", label: "Nhân sự", icon: Users, path: "/nhansu", adminOnly: true, ready: true },
      { key: "baocao", label: "Báo cáo", icon: FileBarChart, path: "/baocao", ready: true },
      { key: "danhmuc", label: "Danh mục", icon: Tags, path: "/danhmuc" },
      { key: "congno", label: "Công nợ", icon: Wallet, path: "/congno" },
    ],
  },
  {
    title: "HỆ THỐNG",
    items: [
      { key: "caidat", label: "Cài đặt", icon: Settings, path: "/caidat" },
      { key: "saoluu", label: "Sao lưu", icon: DatabaseBackup, path: "/saoluu", ready: true },
      { key: "lichhen", label: "Lịch hẹn", icon: CalendarClock, path: "/lichhen" },
      { key: "tichhop", label: "Tích hợp", icon: Plug, path: "/tichhop" },
      { key: "capnhat", label: "Cập nhật", icon: RefreshCw, path: "/capnhat", adminOnly: true, ready: true },
    ],
  },
];
