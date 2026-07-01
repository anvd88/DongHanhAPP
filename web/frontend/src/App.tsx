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
import { WaterReminderPopup } from "./components/WaterReminderPopup";
import { EyeReminderPopup } from "./components/EyeReminderPopup";
import { Login } from "./pages/Login";
import { KioskPage } from "./pages/KioskPage";
import { FarewellPage } from "./pages/FarewellPage";
import { TinhToan } from "./pages/TinhToan";
import { Dashboard } from "./pages/Dashboard";
import { KeToan } from "./pages/KeToan";
import { KhachHang } from "./pages/KhachHang";
import { GiaCongPage } from "./features/giacong/GiaCongPage";
import { ChamCongPage } from "./features/chamcong/ChamCongPage";
import { ChamCongScannerPage } from "./features/chamcong/ChamCongScannerPage";
import { NhanSu } from "./pages/NhanSu";
import { HoSo } from "./pages/HoSo";
import { DonTu } from "./pages/DonTu";
import { PheDuyet } from "./pages/PheDuyet";
import { BangCong } from "./pages/BangCong";
import { QuanLyNhanSu } from "./pages/QuanLyNhanSu";
import { BaoCao } from "./pages/BaoCao";
import { CongCu } from "./pages/CongCu";
import { Chats } from "./pages/Chats";
import { SaoLuu } from "./pages/SaoLuu";
import { PhanHoi } from "./pages/PhanHoi";
import { CapNhat } from "./pages/CapNhat";
import { StubPage } from "./pages/StubPage";
import { SystemSettings } from "./pages/SystemSettings";
import { isAdmin } from "./lib/types";
import { DEFAULT_AUTH_PATH, IS_HR_APK } from "./lib/appConfig";
import { Loader2 } from "lucide-react";

function Protected({ children, admin }: { children: React.ReactNode; admin?: boolean }) {
  const { user, loading } = useAuth();
  const loc = useLocation();
  if (loading)
    return (
      <div className="flex h-screen items-center justify-center">
        <Loader2 className="h-7 w-7 animate-spin text-[var(--accent)]" />
      </div>
    );
  if (!user) return <Navigate to="/login" state={{ from: loc }} replace />;
  if (admin && !isAdmin(user)) return <Navigate to={DEFAULT_AUTH_PATH} replace />;
  return (
    <>
      <ChatNotificationProvider>
        <FeedbackResolvedToasts />
        <Layout>{children}</Layout>
        <WaterReminderPopup user={user} />
        <EyeReminderPopup user={user} />
      </ChatNotificationProvider>
    </>
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
        <div className="liquid-bg"><div className="orb" /></div>
        <Router>
        <RealtimeBoot />
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route path="/kiosk" element={<KioskPage />} />
            <Route path="/tam-biet" element={<FarewellPage />} />
            <Route path="/tinh-toan" element={<TinhToan />} />
            <Route path="/dashboard" element={<Protected><Dashboard /></Protected>} />
            <Route path="/ketoan" element={<Protected><KeToan /></Protected>} />
            <Route path="/khachhang" element={<Protected><KhachHang /></Protected>} />
            <Route path="/giacong" element={<Protected><GiaCongPage /></Protected>} />
            <Route path="/baocao" element={<Protected><BaoCao /></Protected>} />
            <Route path="/saoluu" element={<Protected><SaoLuu /></Protected>} />
            <Route path="/chamcong" element={<Protected><ChamCongScannerPage /></Protected>} />
            <Route path="/ql-chamcong" element={<Protected admin><ChamCongPage /></Protected>} />
            <Route path="/tinhtoan" element={<Protected><CongCu /></Protected>} />
            <Route path="/chats" element={<Protected><Chats /></Protected>} />
            <Route path="/phanhoi" element={<Protected><PhanHoi /></Protected>} />
            <Route path="/nhansu" element={<Protected admin><NhanSu /></Protected>} />
            <Route path="/hoso" element={<Protected><HoSo /></Protected>} />
            <Route path="/dontu" element={<Protected><DonTu /></Protected>} />
            <Route path="/pheduyet" element={<Protected><PheDuyet /></Protected>} />
            <Route path="/bangcong" element={<Protected><BangCong /></Protected>} />
            <Route path="/quanly-nhansu" element={<Protected admin><QuanLyNhanSu /></Protected>} />
            <Route path="/capnhat" element={<Protected admin><CapNhat /></Protected>} />

            {/* Module chưa hiện thực — hiển thị "đang phát triển" giống bản desktop */}
            <Route path="/kho" element={<Protected><StubPage title="Hàng tồn kho" /></Protected>} />
            <Route path="/muahang" element={<Protected><StubPage title="Mua hàng" /></Protected>} />
            <Route path="/taisan" element={<Protected><StubPage title="Tài sản cố định" /></Protected>} />
            <Route path="/danhmuc" element={<Protected><StubPage title="Danh mục" /></Protected>} />
            <Route path="/congno" element={<Protected><StubPage title="Công nợ" /></Protected>} />
            <Route path="/nganhang" element={<Protected><StubPage title="Ngân hàng" /></Protected>} />
            <Route path="/chiphi" element={<Protected><StubPage title="Chi phí" /></Protected>} />
            <Route path="/caidat" element={<Protected><SystemSettings /></Protected>} />
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
