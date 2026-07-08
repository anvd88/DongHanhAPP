import { useEffect } from "react";
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
import { Login } from "./pages/Login";
import { KioskPage } from "./pages/KioskPage";
import { FarewellPage } from "./pages/FarewellPage";
import { TinhToan } from "./pages/TinhToan";
import { ApkDownload } from "./pages/ApkDownload";
import { Dashboard } from "./pages/Dashboard";
import { KeToan } from "./pages/KeToan";
import { KhachHang } from "./pages/KhachHang";
import { GiaCongPage } from "./features/giacong/GiaCongPage";
import { ChamCongPage } from "./features/chamcong/ChamCongPage";
import { ChamCongScannerPage } from "./features/chamcong/ChamCongScannerPage";
import { NhanSuPortal } from "./pages/NhanSuPortal";
import { NhanSu } from "./pages/NhanSu";
import { HoSo } from "./pages/HoSo";
import { DonTu } from "./pages/DonTu";
import { PheDuyet } from "./pages/PheDuyet";
import { QuanLyDonTu } from "./pages/QuanLyDonTu";
import { BangCong } from "./pages/BangCong";
import { QuanLyBangCong } from "./pages/QuanLyBangCong";
import { QuanLyNhanSu } from "./pages/QuanLyNhanSu";
import { TaiKhoanNganHang } from "./pages/TaiKhoanNganHang";
import {
  HRAttendanceAdminPage,
  HRAttendancePage,
  HRApprovalPage,
  HRHomePage,
  HRManagerPage,
  HRPayrollPage,
  HRPenaltyPage,
  HRProfilePage,
  HRRequestsPage,
  HRSystemSettingsPage,
  HRTimesheetPage,
} from "./pages/hr/HRPages";
import { BaoCao } from "./pages/BaoCao";
import { CongCu } from "./pages/CongCu";
import { Chats } from "./pages/Chats";
import { SaoLuu } from "./pages/SaoLuu";
import { PhanHoi } from "./pages/PhanHoi";
import { StubPage } from "./pages/StubPage";
import { SystemSettings } from "./pages/SystemSettings";
import { isAdmin } from "./lib/types";
import { DEFAULT_AUTH_PATH, IS_HR_APK, isHrModulePath } from "./lib/appConfig";
import { Loader2 } from "lucide-react";

const APP_TITLE = "KetoanMini";

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
}: {
  children: React.ReactNode;
  admin?: boolean;
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
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route path="/kiosk" element={<KioskPage />} />
            <Route path="/tam-biet" element={<FarewellPage />} />
            <Route path="/tinh-toan" element={<TinhToan />} />
            <Route path="/tai-apk" element={<Protected publicFallback={<ApkDownload standalone />}><ApkDownload /></Protected>} />
            <Route path="/dashboard" element={<Protected><Dashboard /></Protected>} />
            <Route path="/ketoan" element={<Protected><KeToan /></Protected>} />
            <Route path="/khachhang" element={<Protected><KhachHang /></Protected>} />
            <Route path="/giacong" element={<Protected><GiaCongPage /></Protected>} />
            <Route path="/baocao" element={<Protected><BaoCao /></Protected>} />
            <Route path="/saoluu" element={<Protected admin><SaoLuu /></Protected>} />
            <Route path="/chamcong" element={<Protected>{IS_HR_APK ? <HRAttendancePage /> : <ChamCongScannerPage />}</Protected>} />
            <Route path="/ql-chamcong" element={<Protected admin>{IS_HR_APK ? <HRAttendanceAdminPage /> : <ChamCongPage />}</Protected>} />
            <Route path="/tinhtoan" element={<Protected><CongCu /></Protected>} />
            <Route path="/chats" element={<Protected><Chats /></Protected>} />
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
        </AuthProvider>
        </Router>
      </AppNotificationProvider>
    </ThemeProvider>
  );
}
