import { useEffect, lazy, Suspense } from "react";
import { BrowserRouter, HashRouter, Routes, Route, Navigate, useLocation } from "react-router-dom";
import { startRealtime, subscribeFeedbackResolved } from "./lib/realtime";
import { initFileTransfers } from "./lib/filetransfer";
import { initOfflineAttendanceSync } from "./lib/offlineAttendance";
import { AuthProvider, useAuth } from "./lib/auth";
import { ThemeProvider } from "./lib/theme";
import { Layout } from "./components/Layout";
import { ChatNotificationProvider } from "./components/ChatNotifications";
import { AppNotificationProvider, useAppNotifications } from "./components/AppNotifications";
import { NAV } from "./components/nav";
import { AppUpdatePrompt } from "./components/AppUpdatePrompt";
import { WaterReminderPopup } from "./components/WaterReminderPopup";
import { EyeReminderPopup } from "./components/EyeReminderPopup";
// ─────────────────────────────────────────────────────────────────────────────
// Lazy-load (code splitting): mỗi trang là một chunk riêng, chỉ tải khi điều hướng
// tới. Giảm mạnh dung lượng bundle khởi động → mở app / đăng nhập nhanh hơn nhiều,
// nhất là các trang nặng (Chấm công + nhận diện khuôn mặt, Chat, Cài đặt, HR...).
// Suspense fallback bên dưới hiển thị loader trong lúc chunk đang tải.
// ─────────────────────────────────────────────────────────────────────────────
const Login = lazy(() => import("./pages/Login").then((m) => ({ default: m.Login })));
const KioskPage = lazy(() => import("./pages/KioskPage").then((m) => ({ default: m.KioskPage })));
const FarewellPage = lazy(() => import("./pages/FarewellPage").then((m) => ({ default: m.FarewellPage })));
const TinhToan = lazy(() => import("./pages/TinhToan").then((m) => ({ default: m.TinhToan })));
const ApkDownload = lazy(() => import("./pages/ApkDownload").then((m) => ({ default: m.ApkDownload })));
const Dashboard = lazy(() => import("./pages/Dashboard").then((m) => ({ default: m.Dashboard })));
const KeToan = lazy(() => import("./pages/KeToan").then((m) => ({ default: m.KeToan })));
const PhieuChi = lazy(() => import("./pages/PhieuChi").then((m) => ({ default: m.PhieuChi })));
const KhachHang = lazy(() => import("./pages/KhachHang").then((m) => ({ default: m.KhachHang })));
const GiaCongPage = lazy(() => import("./features/giacong/GiaCongPage").then((m) => ({ default: m.GiaCongPage })));
const ChamCongPage = lazy(() => import("./features/chamcong/ChamCongPage").then((m) => ({ default: m.ChamCongPage })));
const ChamCongScannerPage = lazy(() =>
  import("./features/chamcong/ChamCongScannerPage").then((m) => ({ default: m.ChamCongScannerPage })),
);
const NhanSuPortal = lazy(() => import("./pages/NhanSuPortal").then((m) => ({ default: m.NhanSuPortal })));
const NhanSu = lazy(() => import("./pages/NhanSu").then((m) => ({ default: m.NhanSu })));
const HoSo = lazy(() => import("./pages/HoSo").then((m) => ({ default: m.HoSo })));
const DonTu = lazy(() => import("./pages/DonTu").then((m) => ({ default: m.DonTu })));
const PheDuyet = lazy(() => import("./pages/PheDuyet").then((m) => ({ default: m.PheDuyet })));
const QuanLyDonTu = lazy(() => import("./pages/QuanLyDonTu").then((m) => ({ default: m.QuanLyDonTu })));
const BangCong = lazy(() => import("./pages/BangCong").then((m) => ({ default: m.BangCong })));
const QuanLyBangCong = lazy(() => import("./pages/QuanLyBangCong").then((m) => ({ default: m.QuanLyBangCong })));
const QuanLyNhanSu = lazy(() => import("./pages/QuanLyNhanSu").then((m) => ({ default: m.QuanLyNhanSu })));
const TaiKhoanNganHang = lazy(() => import("./pages/TaiKhoanNganHang").then((m) => ({ default: m.TaiKhoanNganHang })));
const HRAttendanceAdminPage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRAttendanceAdminPage })));
const HRAttendancePage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRAttendancePage })));
const HRApprovalPage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRApprovalPage })));
const HRHomePage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRHomePage })));
const HRManagerPage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRManagerPage })));
const HRPayrollPage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRPayrollPage })));
const HRPenaltyPage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRPenaltyPage })));
const HRProfilePage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRProfilePage })));
const HRRequestsPage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRRequestsPage })));
const HRSystemSettingsPage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRSystemSettingsPage })));
const HRTimesheetPage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRTimesheetPage })));
const BaoCao = lazy(() => import("./pages/BaoCao").then((m) => ({ default: m.BaoCao })));
const CongCu = lazy(() => import("./pages/CongCu").then((m) => ({ default: m.CongCu })));
const Chats = lazy(() => import("./pages/Chats").then((m) => ({ default: m.Chats })));
const CallPage = lazy(() => import("./pages/CallPage").then((m) => ({ default: m.CallPage })));
const SaoLuu = lazy(() => import("./pages/SaoLuu").then((m) => ({ default: m.SaoLuu })));
const PhanHoi = lazy(() => import("./pages/PhanHoi").then((m) => ({ default: m.PhanHoi })));
const StubPage = lazy(() => import("./pages/StubPage").then((m) => ({ default: m.StubPage })));
const SystemSettings = lazy(() => import("./pages/SystemSettings").then((m) => ({ default: m.SystemSettings })));
const CongThongTin = lazy(() => import("./pages/CongThongTin").then((m) => ({ default: m.CongThongTin })));
const CongViec = lazy(() => import("./pages/CongViec").then((m) => ({ default: m.CongViec })));
import { isAdmin } from "./lib/types";
import { DEFAULT_AUTH_PATH, IS_HR_APK, isHrModulePath } from "./lib/appConfig";
import { Loader2 } from "lucide-react";

const APP_TITLE = "Đồng Hành";

const EXTRA_PAGE_TITLES: Record<string, string> = {
  "/": "Bản web",
  "/login": "Đăng nhập",
  "/kiosk": "Kiosk chấm công",
  "/tam-biet": "Tạm biệt",
  "/tinh-toan": "Tính toán",
  "/tai-apk": "Tải APK",
  "/nhan-su": "Nhân sự",
  "/nhansu": "Tài khoản",
  "/hoso": "Hồ sơ",
  "/dontu": "Đơn từ",
  "/pheduyet": "Phê duyệt",
  "/quanly-dontu": "Quản lý đơn từ",
  "/bangcong": "Bảng công",
  "/phat": "Phạt",
  "/tai-khoan-ngan-hang": "Tài khoản ngân hàng",
  "/lichhen": "Lịch hẹn",
  "/tichhop": "Tích hợp",
  "/call": "Cuộc gọi",
};

const NAV_PAGE_TITLES = Object.fromEntries(
  NAV.flatMap((section) => section.items).map((item) => [item.path, item.label]),
) as Record<string, string>;

const PAGE_TITLES = { ...NAV_PAGE_TITLES, ...EXTRA_PAGE_TITLES };

function titleForPath(pathname: string) {
  const normalized = (pathname.split(/[?#]/)[0] || "/").replace(/\/+$/, "") || "/";
  const exact = PAGE_TITLES[normalized];
  if (exact) return exact;

  const match = Object.entries(PAGE_TITLES)
    .filter(([path]) => path !== "/" && normalized.startsWith(`${path}/`))
    .sort((a, b) => b[0].length - a[0].length)[0];
  return match?.[1] ?? "Bản web";
}

function DocumentTitle() {
  const location = useLocation();

  useEffect(() => {
    document.title = `${titleForPath(location.pathname)} · ${APP_TITLE}`;
  }, [location.pathname]);

  return null;
}

function Protected({
  children,
  admin,
  publicFallback,
  standalone,
}: {
  children: React.ReactNode;
  admin?: boolean;
  standalone?: boolean;
  /** Nếu có: khi chưa đăng nhập (hoặc đang tải) sẽ render nội dung công khai này thay vì
   *  chuyển hướng về /login — dùng cho trang vừa công khai vừa nằm trong app (vd Tải APK). */
  publicFallback?: React.ReactNode;
}) {
  const { user, loading } = useAuth();
  const loc = useLocation();
  const suppressMainWebSystem = IS_HR_APK || isHrModulePath(loc.pathname);
  if (loading) {
    if (publicFallback !== undefined) return <>{publicFallback}</>;
    return (
      <div className="flex h-screen items-center justify-center">
        <Loader2 className="h-7 w-7 animate-spin text-[var(--accent)]" />
      </div>
    );
  }
  if (!user) {
    if (publicFallback !== undefined) return <>{publicFallback}</>;
    return <Navigate to="/login" state={{ from: loc }} replace />;
  }
  if (admin && !isAdmin(user)) return <Navigate to={DEFAULT_AUTH_PATH} replace />;
  if (standalone) return <>{children}</>;
  const content = (
    <>
      {!suppressMainWebSystem && <FeedbackResolvedToasts />}
      <Layout suppressMainWebSystem={suppressMainWebSystem}>{children}</Layout>
      {!suppressMainWebSystem && <WaterReminderPopup user={user} />}
      {!suppressMainWebSystem && <EyeReminderPopup user={user} />}
    </>
  );
  // Giữ ChatNotificationProvider mount ổn định cho toàn web chính (kể cả trang module nhân sự)
  // để Layout/Sidebar KHÔNG bị remount khi chuyển giữa trang HR và trang thường (gây "nháy như F5"
  // + reset khung chọn sidebar). Trên HR chỉ ẩn phần nổi qua prop suppress. APK vẫn không dùng provider.
  if (IS_HR_APK) return content;
  return <ChatNotificationProvider suppress={suppressMainWebSystem}>{content}</ChatNotificationProvider>;
}

/** Loader hiển thị trong lúc chunk của trang (lazy) đang được tải về. */
function RouteFallback() {
  return (
    <div className="flex min-h-[60vh] items-center justify-center">
      <Loader2 className="h-7 w-7 animate-spin text-[var(--accent)]" />
    </div>
  );
}

function FeedbackResolvedToasts() {
  const { notify } = useAppNotifications();

  useEffect(() => {
    return subscribeFeedbackResolved((message) => {
      notify.success(message, "Phản hồi đã xử lý");
    });
  }, [notify]);

  return null;
}

function RealtimeBoot() {
  const location = useLocation();
  const { notify } = useAppNotifications();

  useEffect(() => {
    if (location.pathname !== "/tam-biet" && location.pathname !== "/tinh-toan") {
      startRealtime();
      initFileTransfers(); // lắng nghe tín hiệu gửi tệp P2P qua LAN
    }
  }, [location.pathname]);

  // Đồng bộ chấm công ngoại tuyến TOÀN CỤC: đăng ký ngay khi app khởi động (kể cả sau khi reload
  // ở bất kỳ trang nào) để hàng đợi trong IndexedDB tự gửi lên ngay khi có mạng lại — không cần
  // người dùng phải mở đúng trang Chấm công.
  useEffect(() => {
    initOfflineAttendanceSync((s) => {
      if (s.synced > 0)
        notify.success(
          `Đã đồng bộ ${s.synced} lượt chấm công ngoại tuyến${s.recognized < s.synced ? ` (${s.recognized} nhận diện được)` : ""}.`,
          "Đồng bộ chấm công",
        );
    });
  }, [notify]);

  return null;
}

export default function App() {
  const Router = IS_HR_APK ? HashRouter : BrowserRouter;

  return (
    <ThemeProvider>
      <AppNotificationProvider>
        <div className={`liquid-bg ${IS_HR_APK ? "liquid-bg--plain" : ""}`}><div className="orb" /></div>
        <Router>
        <DocumentTitle />
        <RealtimeBoot />
        <AuthProvider>
          <AppUpdatePrompt />
          <Suspense fallback={<RouteFallback />}>
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route path="/kiosk" element={<KioskPage />} />
            <Route path="/tam-biet" element={<FarewellPage />} />
            <Route path="/tinh-toan" element={<TinhToan />} />
            <Route path="/tai-apk" element={<Protected publicFallback={<ApkDownload standalone />}><ApkDownload /></Protected>} />
            <Route path="/dashboard" element={<Protected><Dashboard /></Protected>} />
            <Route path="/ketoan" element={<Protected><KeToan /></Protected>} />
            {/* Không đặt admin: kế toán (không phải admin) mới là người lập/duyệt chi; server chốt quyền.
                Nhân viên thường vào đây chỉ thấy phiếu chi của chính mình. */}
            <Route path="/phieu-chi" element={<Protected><PhieuChi /></Protected>} />
            <Route path="/khachhang" element={<Protected><KhachHang /></Protected>} />
            <Route path="/giacong" element={<Protected><GiaCongPage /></Protected>} />
            <Route path="/baocao" element={<Protected><BaoCao /></Protected>} />
            {/* Không đặt admin: kế toán cũng vào được, nhưng server chỉ trả phần thu chi tiền mặt
                (xem AuditEndpoints.ResolveScopeAsync). Người không có quyền nào sẽ nhận 403. */}
            <Route path="/saoluu" element={<Protected><SaoLuu /></Protected>} />
            <Route path="/chamcong" element={<Protected>{IS_HR_APK ? <HRAttendancePage /> : <ChamCongScannerPage />}</Protected>} />
            <Route path="/ql-chamcong" element={<Protected admin>{IS_HR_APK ? <HRAttendanceAdminPage /> : <ChamCongPage />}</Protected>} />
            <Route path="/tinhtoan" element={<Protected><CongCu /></Protected>} />
            <Route path="/cong-viec" element={<Protected><CongViec /></Protected>} />
            <Route path="/chats" element={<Protected><Chats /></Protected>} />
            <Route path="/call" element={<Protected standalone><CallPage /></Protected>} />
            <Route path="/phanhoi" element={<Protected><PhanHoi /></Protected>} />
            <Route path="/nhan-su" element={<Protected>{IS_HR_APK ? <HRHomePage /> : <NhanSuPortal />}</Protected>} />
            <Route path="/nhansu" element={<Protected admin><NhanSu /></Protected>} />
            <Route path="/hoso" element={<Protected>{IS_HR_APK ? <HRProfilePage /> : <HoSo />}</Protected>} />
            <Route path="/dontu" element={<Protected>{IS_HR_APK ? <HRRequestsPage /> : <DonTu />}</Protected>} />
            <Route path="/pheduyet" element={<Protected>{IS_HR_APK ? <HRApprovalPage /> : <PheDuyet />}</Protected>} />
            <Route path="/quanly-dontu" element={<Protected admin><QuanLyDonTu /></Protected>} />
            <Route path="/bangcong" element={<Protected>{IS_HR_APK ? <HRTimesheetPage /> : <BangCong />}</Protected>} />
            <Route path="/quanly-bangcong" element={<Protected admin><QuanLyBangCong /></Protected>} />
            <Route path="/quanly-nhansu" element={<Protected admin>{IS_HR_APK ? <HRManagerPage /> : <QuanLyNhanSu />}</Protected>} />
            <Route path="/phat" element={<Protected><HRPenaltyPage /></Protected>} />
            <Route path="/tai-khoan-ngan-hang" element={<Protected><TaiKhoanNganHang /></Protected>} />
            <Route path="/cong-thong-tin" element={<Protected admin><CongThongTin /></Protected>} />
            <Route path="/bang-luong" element={<Protected admin><HRPayrollPage /></Protected>} />
            {/* Module chưa hiện thực — hiển thị "đang phát triển" giống bản desktop */}
            <Route path="/kho" element={<Protected><StubPage title="Hàng tồn kho" /></Protected>} />
            <Route path="/muahang" element={<Protected><StubPage title="Mua hàng" /></Protected>} />
            <Route path="/taisan" element={<Protected><StubPage title="Tài sản cố định" /></Protected>} />
            <Route path="/danhmuc" element={<Protected><StubPage title="Danh mục" /></Protected>} />
            <Route path="/congno" element={<Protected><StubPage title="Công nợ" /></Protected>} />
            <Route path="/nganhang" element={<Protected><StubPage title="Ngân hàng" /></Protected>} />
            <Route path="/chiphi" element={<Protected><StubPage title="Chi phí" /></Protected>} />
            <Route path="/caidat" element={<Protected>{IS_HR_APK ? <HRSystemSettingsPage /> : <SystemSettings />}</Protected>} />
            <Route path="/lichhen" element={<Protected><StubPage title="Lịch hẹn" /></Protected>} />
            <Route path="/tichhop" element={<Protected><StubPage title="Tích hợp" /></Protected>} />

            <Route path="*" element={<Navigate to={DEFAULT_AUTH_PATH} replace />} />
          </Routes>
          </Suspense>
        </AuthProvider>
        </Router>
      </AppNotificationProvider>
    </ThemeProvider>
  );
}
