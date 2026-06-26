import { useEffect } from "react";
import { BrowserRouter, Routes, Route, Navigate, useLocation } from "react-router-dom";
import { startRealtime } from "./lib/realtime";
import { AuthProvider, useAuth } from "./lib/auth";
import { ThemeProvider } from "./lib/theme";
import { Layout } from "./components/Layout";
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
import { NhanSu } from "./pages/NhanSu";
import { BaoCao } from "./pages/BaoCao";
import { CongCu } from "./pages/CongCu";
import { SaoLuu } from "./pages/SaoLuu";
import { CapNhat } from "./pages/CapNhat";
import { StubPage } from "./pages/StubPage";
import { SystemSettings } from "./pages/SystemSettings";
import { isAdmin } from "./lib/types";
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
  if (admin && !isAdmin(user)) return <Navigate to="/dashboard" replace />;
  return (
    <>
      <Layout>{children}</Layout>
      <WaterReminderPopup user={user} />
      <EyeReminderPopup user={user} />
    </>
  );
}

function RealtimeBoot() {
  const location = useLocation();

  useEffect(() => {
    if (location.pathname !== "/tam-biet" && location.pathname !== "/tinh-toan") startRealtime();
  }, [location.pathname]);

  return null;
}

export default function App() {
  return (
    <ThemeProvider>
      <div className="liquid-bg"><div className="orb" /></div>
      <BrowserRouter>
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
            <Route path="/chamcong" element={<Protected><ChamCongPage /></Protected>} />
            <Route path="/tinhtoan" element={<Protected><CongCu /></Protected>} />
            <Route path="/nhansu" element={<Protected admin><NhanSu /></Protected>} />
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

            <Route path="*" element={<Navigate to="/dashboard" replace />} />
          </Routes>
        </AuthProvider>
      </BrowserRouter>
    </ThemeProvider>
  );
}
