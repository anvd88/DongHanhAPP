import {
  Banknote,
  BarChart3,
  BookOpen,
  Boxes,
  Building2,
  CircleDollarSign,
  FileText,
  LayoutDashboard,
  ScanFace,
  Settings,
  ShoppingBag,
  ShoppingCart,
  Tags,
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
      { key: "banhang", label: "Bán hàng", icon: BarChart3, path: "/banhang", ready: true },
      { key: "muahang", label: "Mua hàng", icon: ShoppingCart, path: "/muahang" },
      { key: "kho", label: "Kho hàng", icon: ShoppingBag, path: "/kho" },
      { key: "nganhang", label: "Ngân hàng", icon: Banknote, path: "/nganhang" },
      { key: "congno", label: "Công nợ", icon: Wallet, path: "/congno" },
      { key: "taisan", label: "Tài sản cố định", icon: Boxes, path: "/taisan" },
      { key: "chiphi", label: "Chi phí", icon: CircleDollarSign, path: "/chiphi" },
    ],
  },
  {
    title: "QUẢN LÝ",
    items: [
      { key: "nhansu", label: "Nhân sự", icon: Users, path: "/nhansu", adminOnly: true, ready: true },
      { key: "chamcong", label: "Chấm công", icon: ScanFace, path: "/chamcong", ready: true },
      { key: "baocao", label: "Báo cáo", icon: BookOpen, path: "/baocao", ready: true },
      { key: "danhmuc", label: "Danh mục", icon: Tags, path: "/danhmuc" },
      { key: "hethong", label: "Hệ thống", icon: Settings, path: "/caidat" },
    ],
  },
];
