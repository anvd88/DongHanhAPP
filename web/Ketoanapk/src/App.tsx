import { useEffect } from "react";
import { HashRouter, Navigate, Route, Routes, useLocation } from "react-router-dom";
import { Loader2 } from "lucide-react";
import { AppNotificationProvider, useAppNotifications } from "./components/AppNotifications";
import { AppUpdatePrompt } from "./components/AppUpdatePrompt";
import { Layout } from "./components/Layout";
import { Login } from "./pages/Login";
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
import { TaiKhoanNganHang } from "./pages/TaiKhoanNganHang";
import { SaoLuu } from "./pages/SaoLuu";
import { AuthProvider, useAuth } from "./lib/auth";
import { DEFAULT_AUTH_PATH } from "./lib/appConfig";
import { initOfflineAttendanceSync } from "./lib/offlineAttendance";
import { startRealtime } from "./lib/realtime";
import { ThemeProvider } from "./lib/theme";
import { isAdmin } from "./lib/types";

function Protected({ children, admin }: { children: React.ReactNode; admin?: boolean }) {
  const { user, loading } = useAuth();
  const loc = useLocation();

  if (loading) {
    return (
      <div className="flex h-screen items-center justify-center">
        <Loader2 className="h-7 w-7 animate-spin text-[var(--accent)]" />
      </div>
    );
  }

  if (!user) return <Navigate to="/login" state={{ from: loc }} replace />;
  if (admin && !isAdmin(user)) return <Navigate to={DEFAULT_AUTH_PATH} replace />;

  return <Layout suppressMainWebSystem>{children}</Layout>;
}

function RealtimeBoot() {
  const { notify } = useAppNotifications();

  useEffect(() => {
    startRealtime();
  }, []);

  useEffect(() => {
    initOfflineAttendanceSync((s) => {
      if (s.synced > 0) {
        notify.success(
          `Đã đồng bộ ${s.synced} lượt chấm công ngoại tuyến${s.recognized < s.synced ? ` (${s.recognized} nhận diện được)` : ""}.`,
          "Đồng bộ chấm công",
        );
      }
    });
  }, [notify]);

  return null;
}

export default function App() {
  return (
    <ThemeProvider>
      <AppNotificationProvider>
        <div className="liquid-bg liquid-bg--plain"><div className="orb" /></div>
        <HashRouter>
          <RealtimeBoot />
          <AuthProvider>
            <AppUpdatePrompt />
            <Routes>
              <Route path="/login" element={<Login />} />
              <Route path="/nhan-su" element={<Protected><HRHomePage /></Protected>} />
              <Route path="/chamcong" element={<Protected><HRAttendancePage /></Protected>} />
              <Route path="/ql-chamcong" element={<Protected admin><HRAttendanceAdminPage /></Protected>} />
              <Route path="/hoso" element={<Protected><HRProfilePage /></Protected>} />
              <Route path="/dontu" element={<Protected><HRRequestsPage /></Protected>} />
              <Route path="/pheduyet" element={<Protected><HRApprovalPage /></Protected>} />
              <Route path="/bangcong" element={<Protected><HRTimesheetPage /></Protected>} />
              <Route path="/quanly-nhansu" element={<Protected admin><HRManagerPage /></Protected>} />
              <Route path="/phat" element={<Protected><HRPenaltyPage /></Protected>} />
              <Route path="/tai-khoan-ngan-hang" element={<Protected><TaiKhoanNganHang /></Protected>} />
              <Route path="/bang-luong" element={<Protected admin><HRPayrollPage /></Protected>} />
              <Route path="/caidat" element={<Protected><HRSystemSettingsPage /></Protected>} />
              <Route path="/saoluu" element={<Protected admin><SaoLuu /></Protected>} />
              <Route path="*" element={<Navigate to={DEFAULT_AUTH_PATH} replace />} />
            </Routes>
          </AuthProvider>
        </HashRouter>
      </AppNotificationProvider>
    </ThemeProvider>
  );
}
