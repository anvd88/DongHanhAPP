import type { LucideIcon } from 'lucide-react'
import {
  Building2,
  Calculator,
  CalendarDays,
  Clock,
  FileText,
  Fingerprint,
  HandCoins,
  LayoutDashboard,
  LifeBuoy,
  ListChecks,
  Megaphone,
  MessageSquare,
  MonitorSmartphone,
  Newspaper,
  PackageSearch,
  ReceiptText,
  ScrollText,
  Settings,
  ShieldCheck,
  Smartphone,
  Truck,
  UserCog,
  Users,
  Wallet,
} from 'lucide-react'
import { PERM } from '@/lib/permissions'
import type { Scope } from '@/lib/realtime'

/**
 * Bản đồ màn hình, dựng theo các module của backend (docs/backend-inventory.md, phần C).
 *
 * Mỗi mục khai báo ba thuộc tính:
 *   · `requires` — quyền backend áp cho nhóm route tương ứng. Thiếu quyền thì mục không hiện
 *     trong menu và route trả về màn hình không đủ quyền; chốt thật vẫn nằm ở endpoint.
 *   · `scope`   — phạm vi realtime màn hình lắng nghe, khớp bảng Watched của
 *     Realtime/DatabaseChangePublisher.cs.
 *   · `api`     — nhóm endpoint màn hình sử dụng.
 *
 * Các đường dẫn sau là hợp đồng với backend vì PushService đã gắn vào thông báo, đổi sẽ làm hỏng
 * liên kết từ chuông: /dashboard /ban-hang /nhan-su /cong-viec /chamcong /dontu /pheduyet
 * /phat /lenh-thu-tien /phieu-chi /quanly-nhansu /caidat /tai-apk.
 */
export interface NavRoute {
  path: string
  label: string
  description: string
  api: string
  scope: Scope
  requires?: string
  requiresAny?: string[]
  /** Route tồn tại nhưng không hiện trong menu, ví dụ trang chi tiết. */
  hidden?: boolean
  /**
   * Loại máy mà màn hình này có nghĩa. Mặc định là `all`.
   *
   *   handheld  chỉ khi cầm máy trên tay, ví dụ trạm chấm công bằng khuôn mặt
   *   desktop   chỉ trên máy tính, ví dụ màn nhập liệu nhiều cột không dùng được bằng ngón tay
   *
   * Mục không hợp máy sẽ biến khỏi menu và bảng lệnh, nhưng ROUTE VẪN CÒN — đường dẫn từ chuông
   * thông báo phải sống sót — và mở ra sẽ là màn hướng dẫn đổi sang máy phù hợp.
   */
  deviceScope?: 'all' | 'handheld' | 'desktop'
  /** Từ khoá không dấu phục vụ bảng lệnh Ctrl+K. */
  keywords?: string
}

export interface NavGroup {
  id: string
  label: string
  icon: LucideIcon
  routes: NavRoute[]
}

export const NAV: NavGroup[] = [
  {
    id: 'dieu-hanh',
    label: 'Điều hành',
    icon: LayoutDashboard,
    routes: [
      {
        path: '/dashboard',
        label: 'Bảng điều hành',
        description: 'Số liệu tổng hợp: doanh thu, công nợ, tồn quỹ, nhân sự có mặt hôm nay.',
        api: 'GET /api/dashboard',
        scope: 'sales',
        requiresAny: [PERM.accountingAccess, PERM.companyScopeAll],
        keywords: 'dashboard tong quan bang dieu hanh',
      },
      {
        path: '/viec-can-lam',
        label: 'Việc cần làm',
        description:
          'Năm nguồn gộp lại: đơn chờ mình duyệt, phiếu lương chưa xác nhận, giấy tờ và hợp đồng sắp hết hạn, thông báo bắt buộc.',
        api: 'GET /api/worklist',
        scope: 'hr',
        requires: PERM.requestsSelf,
        keywords: 'viec can lam worklist todo',
      },
      {
        path: '/bao-cao',
        label: 'Báo cáo',
        description: 'Báo cáo bán hàng, công nợ, tồn quỹ; xuất Excel theo kỳ.',
        api: 'GET /api/reports',
        scope: 'sales',
        requires: PERM.reportRead,
        keywords: 'bao cao report excel',
      },
    ],
  },

  {
    id: 'ban-hang',
    label: 'Bán hàng & Kho',
    icon: ReceiptText,
    routes: [
      {
        path: '/ban-hang',
        label: 'Phiếu bán hàng',
        description:
          'Sổ phiếu xuất kho / bán hàng. Phiếu đã phát hành không xoá vật lý được — CSDL chặn bằng trigger, chỉ chuyển sang trạng thái huỷ.',
        api: 'GET/POST /api/documents',
        scope: 'sales',
        requires: PERM.accountingAccess,
        keywords: 'ban hang phieu xuat kho chung tu',
      },
      {
        path: '/ban-hang/:id',
        label: 'Chi tiết phiếu',
        description: 'Dòng hàng, phát hành, in kho, giao hàng và đối soát của một phiếu.',
        api: 'GET /api/documents/{id}',
        scope: 'sales',
        requires: PERM.accountingAccess,
        hidden: true,
      },
      {
        path: '/giao-hang',
        label: 'Giao hàng',
        description:
          'Gán phiếu đã phát hành cho lái xe. Mỗi phiếu chỉ có đúng một việc giao hàng còn sống; đổi lái xe bắt buộc có lý do.',
        api: 'GET /api/delivery-assignments',
        scope: 'tasks',
        requires: PERM.accountingAccess,
        keywords: 'giao hang lai xe delivery',
      },
      {
        path: '/hang-tra-ve',
        label: 'Hàng trả về',
        description:
          'Khách không nhận hoặc trả lại một phần. Truy đơn nguồn để lấy đúng đơn giá đã bán; tổng đã trả không bao giờ vượt số đã bán.',
        api: 'GET/POST /api/returns',
        scope: 'sales',
        requires: PERM.accountingAccess,
        keywords: 'hang tra ve returns hoan hang',
      },
      {
        path: '/khach-hang',
        label: 'Khách hàng',
        description: 'Danh sách khách, bí danh và báo cáo mua hàng theo từng khách.',
        api: 'GET /api/customers',
        scope: 'debts',
        requires: PERM.accountingAccess,
        keywords: 'khach hang customer doi tac',
      },
      {
        path: '/cong-no',
        label: 'Công nợ phải thu',
        description: 'Số dư đầu kỳ, phát sinh và thanh toán của từng khách hàng.',
        api: 'GET /api/debts',
        scope: 'debts',
        requires: PERM.accountingAccess,
        keywords: 'cong no phai thu debt',
      },
      {
        path: '/danh-muc-hang',
        label: 'Danh mục hàng hoá',
        description:
          'Mã hàng dùng chung. Nguyên tắc: GỢI Ý chứ không CHẶN — ô nhập trên phiếu vẫn gõ tay được.',
        api: 'GET/POST /api/products',
        scope: 'catalog',
        requires: PERM.accountingAccess,
        keywords: 'danh muc hang hoa san pham product',
      },
      {
        path: '/gia-cong',
        label: 'Gia công',
        description: 'Phiếu xuất/nhập gia công và báo cáo tổng hợp theo đối tác, theo hàng hoá.',
        api: 'GET /api/giacong',
        scope: 'sales',
        requires: PERM.accountingAccess,
        keywords: 'gia cong outsourcing',
      },
    ],
  },

  {
    id: 'mua-hang',
    label: 'Mua hàng',
    icon: PackageSearch,
    routes: [
      {
        path: '/mua-hang',
        label: 'Phiếu nhập mua',
        description:
          'Vế nhập của kho. Bảng riêng purchases/purchase_lines, không dùng chung documents.',
        api: 'GET/POST /api/purchases',
        scope: 'purchases',
        requires: PERM.accountingAccess,
        keywords: 'mua hang nhap kho purchase',
      },
      {
        path: '/nha-cung-cap',
        label: 'Nhà cung cấp',
        description: 'Đối tác nhập hàng và công nợ phải trả ở mức đã thanh toán.',
        api: 'GET/POST /api/suppliers',
        scope: 'purchases',
        requires: PERM.accountingAccess,
        keywords: 'nha cung cap supplier ncc',
      },
    ],
  },

  {
    id: 'ke-toan',
    label: 'Kế toán',
    icon: Calculator,
    routes: [
      {
        path: '/thu-chi',
        label: 'Phiếu thu / chi',
        description: 'Chứng từ thu chi tiền mặt gắn với khách hàng và sổ quỹ.',
        api: 'GET /api/cash-vouchers',
        scope: 'cash',
        requires: PERM.accountingAccess,
        keywords: 'phieu thu chi cash voucher',
      },
      {
        path: '/quy-tien-mat',
        label: 'Quỹ tiền mặt',
        description:
          'Sổ quỹ là VIEW hợp nhất bốn nguồn: lệnh thu hoàn tất, phiếu chi đã chi, phiếu thu/chi còn hiệu lực, bút toán tay.',
        api: 'GET /api/cash-fund',
        scope: 'cash',
        requires: PERM.cashFundRead,
        keywords: 'quy tien mat cash fund so quy',
      },
      {
        path: '/lenh-thu-tien',
        label: 'Lệnh thu tiền',
        description:
          'Giao → tài xế nhận → đếm theo mệnh giá → thủ quỹ nhận lại. Sai lệch phải được duyệt mới ghi "đã nộp đủ tiền".',
        api: 'GET /api/cash-collections',
        scope: 'cash',
        requiresAny: [PERM.collectionsReadAll, PERM.collectionsSelf],
        keywords: 'lenh thu tien collection thu ho',
      },
      {
        path: '/phieu-chi',
        label: 'Phiếu chi tiền mặt',
        description:
          'Chưa quét QR ký nhận thì không duyệt chi được — đây là chốt chống gian lận của cả luồng.',
        api: 'GET /api/payout-vouchers',
        scope: 'cash',
        requires: PERM.payoutRead,
        keywords: 'phieu chi payout qr ky nhan',
      },
    ],
  },

  {
    id: 'nhan-su',
    label: 'Nhân sự',
    icon: Users,
    routes: [
      {
        path: '/nhan-su',
        label: 'Không gian của tôi',
        description: 'Hồ sơ, hợp đồng, ngày phép, quyền lợi và phiếu lương của chính mình.',
        api: 'GET /api/hr/me',
        scope: 'hr',
        requires: PERM.hrSelfAccess,
        keywords: 'nhan su cua toi ho so',
      },
      {
        path: '/danh-ba',
        label: 'Danh bạ & Sơ đồ',
        description:
          'Tìm theo tên/chức vụ không dấu, trạng thái online, cây tổ chức theo quản lý trực tiếp.',
        api: 'GET /api/directory',
        scope: 'presence',
        requires: PERM.hrSelfAccess,
        keywords: 'danh ba directory so do to chuc',
      },
      {
        path: '/quanly-nhansu',
        label: 'Quản lý nhân sự',
        description: 'Hồ sơ nhân viên, hợp đồng, tăng lương, giấy tờ, phòng ban và địa điểm.',
        api: 'GET /api/hr/employees',
        scope: 'hr',
        requiresAny: [PERM.hrManage, PERM.usersManage],
        keywords: 'quan ly nhan su employee hr',
      },
      {
        path: '/bang-luong',
        label: 'Bảng lương',
        description:
          'Lương cứng dẫn xuất từ hợp đồng cộng các kỳ tăng lương, ghép bảng công và phạt khấu trừ trong kỳ.',
        api: 'GET /api/payroll',
        scope: 'hr',
        requires: PERM.payrollRead,
        keywords: 'bang luong payroll phieu luong',
      },
      {
        path: '/phat',
        label: 'Phạt & kỷ luật',
        description:
          'Sổ cái hr_penalty_ledger: cap theo lương, phần thiếu chuyển kỳ sau, thu đủ thì tất toán.',
        api: 'GET /api/penalties',
        scope: 'hr',
        requires: PERM.penaltyRead,
        keywords: 'phat ky luat penalty khau tru',
      },
      {
        path: '/phat-trien',
        label: 'Phát triển nhân sự',
        description: 'Hội nhập, mục tiêu và đánh giá, khoá đào tạo, quyền lợi.',
        api: 'GET /api/talent',
        scope: 'talent',
        requires: PERM.hrSelfAccess,
        keywords: 'phat trien talent dao tao onboarding',
      },
      {
        path: '/tai-khoan-ngan-hang',
        label: 'Tài khoản ngân hàng',
        description: 'Tài khoản nhận lương của chính mình, đặt một tài khoản mặc định.',
        api: 'GET /api/bank-accounts',
        scope: 'hr',
        requires: PERM.hrSelfAccess,
        hidden: true,
        keywords: 'tai khoan ngan hang bank',
      },
    ],
  },

  {
    id: 'cham-cong',
    label: 'Chấm công',
    icon: Fingerprint,
    routes: [
      {
        path: '/chamcong',
        label: 'Trạm chấm công',
        description:
          'Nhận diện khuôn mặt hai bước: /nhandien cấp vé xem trước, /cham dùng vé đó chứ không suy luận lại. Lần đầu trong ngày là Vào, lần sau là Ra lấy muộn nhất.',
        api: 'POST /api/chamcong/nhandien · /cham',
        scope: 'attendance',
        requires: PERM.attendanceSelf,
        deviceScope: 'handheld',
        keywords: 'cham cong khuon mat attendance kiosk',
      },
      {
        path: '/bang-cong',
        label: 'Bảng công của tôi',
        description:
          'Đối chiếu log chấm công với ca được phân: đi muộn, về sớm, tăng ca (mỗi vế chỉ tính khi từ 15 phút trở lên).',
        api: 'GET /api/timesheet/me',
        scope: 'attendance',
        requires: PERM.attendanceSelf,
        keywords: 'bang cong timesheet cua toi',
      },
      {
        path: '/ca-lam',
        label: 'Ca làm & ngày nghỉ',
        description: 'Định nghĩa ca, phân ca cho nhân viên, ngày nghỉ lễ.',
        api: 'GET /api/shifts',
        scope: 'hr',
        requires: PERM.attendanceSelf,
        keywords: 'ca lam shift ngay nghi le',
      },
      {
        path: '/quanly-bangcong',
        label: 'Bảng công toàn công ty',
        description: 'Lịch tháng từng nhân viên, xuất Excel mỗi người một sheet kèm phiếu lương.',
        api: 'GET /api/timesheet/employee/{id}',
        scope: 'attendance',
        requires: PERM.attendanceRead,
        keywords: 'quan ly bang cong excel thang',
      },
      {
        path: '/ql-chamcong',
        label: 'Quản trị chấm công',
        description:
          'Duyệt chấm công ngoại tuyến (kèm cờ rủi ro lùi giờ máy / khác LAN / ngoài geofence), duyệt mẫu khuôn mặt, cấu hình nhận diện.',
        api: 'GET /api/chamcong/offline · /face-enrollments',
        scope: 'attendance',
        requires: PERM.attendanceManage,
        keywords: 'quan tri cham cong offline khuon mat',
      },
    ],
  },

  {
    id: 'cong-viec',
    label: 'Công việc',
    icon: ListChecks,
    routes: [
      {
        path: '/cong-viec',
        label: 'Giao việc',
        description:
          'Vòng đời: giao → đang làm → đã nộp → nghiệm thu. Riêng việc giao hàng đi đường ngắn, không có chặng nghiệm thu.',
        api: 'GET /api/tasks',
        scope: 'tasks',
        requires: PERM.tasksSelf,
        keywords: 'cong viec task giao viec nghiem thu',
      },
      {
        path: '/dontu',
        label: 'Đơn từ của tôi',
        description:
          'Một engine cho mọi loại đơn (nghỉ phép, tăng ca, tạm ứng, đổi ca…), chi tiết linh hoạt nằm trong cột jsonb.',
        api: 'GET/POST /api/requests',
        scope: 'hr',
        requires: PERM.requestsSelf,
        keywords: 'don tu nghi phep tam ung request',
      },
      {
        path: '/pheduyet',
        label: 'Chờ tôi duyệt',
        description: 'Hàng đợi duyệt nhiều cấp, có ký xác nhận điện tử và uỷ quyền duyệt.',
        api: 'POST /api/requests/{id}/approve',
        scope: 'hr',
        requires: PERM.requestsApprove,
        keywords: 'phe duyet approve hang doi',
      },
      {
        path: '/quanly-dontu',
        label: 'Quản trị đơn từ',
        description: 'Loại đơn, luồng duyệt, uỷ quyền và toàn bộ đơn của công ty.',
        api: 'GET /api/requests',
        scope: 'hr',
        requires: PERM.requestsManage,
        keywords: 'quan tri don tu loai don',
      },
    ],
  },

  {
    id: 'cong-thong-tin',
    label: 'Cổng thông tin',
    icon: Megaphone,
    routes: [
      {
        path: '/cong-thong-tin',
        label: 'Tin & sự kiện',
        description: 'Bảng tin công ty, hiển thị trên cả web lẫn ứng dụng.',
        api: 'GET /api/portal/feed',
        scope: 'portal',
        requires: PERM.portalRead,
        keywords: 'cong thong tin tin tuc portal',
      },
      {
        path: '/khao-sat',
        label: 'Khảo sát & bình chọn',
        description:
          'Trả lời ẩn danh thực sự: không lưu username, chống gửi trùng bằng HMAC một chiều.',
        api: 'GET /api/surveys',
        scope: 'portal',
        requires: PERM.portalRead,
        keywords: 'khao sat survey binh chon',
      },
      {
        path: '/tro-giup',
        label: 'Trung tâm trợ giúp',
        description: 'Câu hỏi thường gặp và trạng thái hệ thống.',
        api: 'GET /api/help/faqs',
        scope: 'config',
        requires: PERM.portalRead,
        keywords: 'tro giup help faq',
      },
      {
        path: '/phan-hoi',
        label: 'Phản hồi & hỗ trợ',
        description: 'Góp ý, khiếu nại chấm công và yêu cầu hỗ trợ kèm mã theo dõi.',
        api: 'GET /api/feedback',
        scope: 'feedback',
        requires: PERM.portalRead,
        keywords: 'phan hoi ho tro feedback support',
      },
    ],
  },

  {
    id: 'he-thong',
    label: 'Hệ thống',
    icon: Settings,
    routes: [
      {
        path: '/nguoi-dung',
        label: 'Tài khoản & phân quyền',
        description:
          'Vai trò chính, vai trò phụ (user_roles), duyệt/khoá tài khoản, mã khôi phục một lần.',
        api: 'GET /api/users',
        scope: 'presence',
        requires: PERM.usersManage,
        keywords: 'nguoi dung tai khoan phan quyen user',
      },
      {
        path: '/caidat',
        label: 'Cấu hình hệ thống',
        description:
          'Cấu hình chung của web và cấu hình app từ xa (đổi không cần phát hành APK mới).',
        api: 'GET/PUT /api/app-config',
        scope: 'config',
        requires: PERM.systemSettingsManage,
        keywords: 'cai dat cau hinh setting',
      },
      {
        path: '/tai-apk',
        label: 'Bản cập nhật APK',
        description: 'Đăng bản mới, phát hành, gỡ bản cũ. Tệp nằm trên đĩa, CSDL chỉ giữ metadata.',
        api: 'GET/POST /api/releases',
        scope: 'release',
        requires: PERM.systemReleasesManage,
        keywords: 'apk release ban cap nhat tai app',
      },
      {
        path: '/saoluu',
        label: 'Nhật ký hoạt động',
        description:
          'Trước/sau của mỗi thay đổi, đã che mật khẩu và token. Phạm vi do server chốt: kế toán chỉ xem được phần tiền.',
        api: 'GET /api/audit',
        scope: 'audit',
        requires: PERM.auditRead,
        keywords: 'nhat ky audit log lich su sao luu',
      },
      {
        path: '/thiet-bi',
        label: 'Thiết bị & phiên',
        description: 'Các máy đang đăng nhập vào tài khoản này và nút thu hồi từ xa.',
        api: 'GET /api/auth/devices',
        scope: 'presence',
        keywords: 'thiet bi phien device session',
      },
      {
        path: '/thong-bao',
        label: 'Thông báo',
        description: 'Hộp thư web_notifications và năm nhóm thông báo tắt được (chốt ở máy chủ).',
        api: 'GET /api/notifications',
        scope: 'notify',
        keywords: 'thong bao notification chuong',
      },
      {
        path: '/ho-so',
        label: 'Hồ sơ & bảo mật',
        description: 'Đổi mật khẩu, ảnh đại diện, bật/tắt đăng nhập web cho chính tài khoản này.',
        api: 'PUT /api/auth/profile',
        scope: 'presence',
        hidden: true,
        keywords: 'ho so mat khau bao mat profile',
      },
    ],
  },
]

/** Biểu tượng riêng của một số màn hình trong bảng lệnh; mục không khai báo dùng biểu tượng của phân hệ. */
export const ROUTE_ICONS: Record<string, LucideIcon> = {
  '/ban-hang': ReceiptText,
  '/giao-hang': Truck,
  '/khach-hang': Building2,
  '/cong-no': HandCoins,
  '/quy-tien-mat': Wallet,
  '/bang-cong': Clock,
  '/ca-lam': CalendarDays,
  '/danh-ba': Users,
  '/quanly-nhansu': UserCog,
  '/cong-thong-tin': Newspaper,
  '/tro-giup': LifeBuoy,
  '/phan-hoi': MessageSquare,
  '/saoluu': ScrollText,
  '/thiet-bi': MonitorSmartphone,
  '/tai-apk': Smartphone,
  '/nguoi-dung': ShieldCheck,
  '/viec-can-lam': ListChecks,
  '/bao-cao': FileText,
}

export const ALL_ROUTES: NavRoute[] = NAV.flatMap((group) => group.routes)

export function findRoute(pathname: string): NavRoute | undefined {
  const exact = ALL_ROUTES.find((route) => route.path === pathname)
  if (exact) return exact
  // Khớp route có tham số, ví dụ /ban-hang/:id nhận /ban-hang/abc123.
  return ALL_ROUTES.find((route) => {
    if (!route.path.includes(':')) return false
    return new RegExp(`^${route.path.replace(/:[^/]+/g, '[^/]+')}$`).test(pathname)
  })
}

export function groupOf(route: NavRoute) {
  return NAV.find((group) => group.routes.includes(route))
}
