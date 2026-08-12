import { useEffect, lazy, Suspense } from "react";
import { BrowserRouter, Routes, Route, Navigate, useLocation } from "react-router-dom";
import { startRealtime, subscribeFeedbackResolved } from "./lib/realtime";
import { initFileTransfers } from "./lib/filetransfer";
import { initOfflineAttendanceSync } from "./lib/offlineAttendance";
import { AuthProvider, useAuth } from "./lib/auth";
import { AccessProvider, useAccess, PERM, type Permission } from "./lib/access";
import { ThemeProvider } from "./lib/theme";
import { Layout } from "./components/Layout";
import { ChatNotificationProvider } from "./components/ChatNotifications";
import { AppNotificationProvider } from "./components/AppNotifications";
import { useAppNotifications } from "./components/app-notifications-context";
import { NAV } from "./components/nav";
import { WaterReminderPopup } from "./components/WaterReminderPopup";
import { EyeReminderPopup } from "./components/EyeReminderPopup";
// ─────────────────────────────────────────────────────────────────────────────
// Lazy-load (code splitting): mỗi trang là một chunk riêng, chỉ tải khi điều hướng
// tới. Giảm mạnh dung lượng bundle khởi động → mở app / đăng nhập nhanh hơn nhiều,
// nhất là các trang nặng (Chấm công + nhận diện khuôn mặt, Chat, Cài đặt, HR...).
// Suspense fallback bên dưới hiển thị skeleton trong lúc chunk đang tải.
// ─────────────────────────────────────────────────────────────────────────────
const Login = lazy(() => import("./pages/Login").then((m) => ({ default: m.Login })));
// Kiosk tạm ngừng phát triển: giữ nguyên pages/KioskPage.tsx nhưng không đưa vào route/bundle.
const FarewellPage = lazy(() => import("./pages/FarewellPage").then((m) => ({ default: m.FarewellPage })));
const TinhToan = lazy(() => import("./pages/TinhToan").then((m) => ({ default: m.TinhToan })));
const ApkDownload = lazy(() => import("./pages/ApkDownload").then((m) => ({ default: m.ApkDownload })));
const Dashboard = lazy(() => import("./pages/Dashboard").then((m) => ({ default: m.Dashboard })));
const KeToan = lazy(() => import("./pages/KeToan").then((m) => ({ default: m.KeToan })));
const CoreAccounting = lazy(() => import("./pages/CoreAccounting").then((m) => ({ default: m.CoreAccounting })));
const ThuChi = lazy(() => import("./pages/ThuChi").then((m) => ({ default: m.ThuChi })));
const CongNo = lazy(() => import("./pages/CongNo").then((m) => ({ default: m.CongNo })));
const PhieuChi = lazy(() => import("./pages/PhieuChi").then((m) => ({ default: m.PhieuChi })));
const KhachHang = lazy(() => import("./pages/KhachHang").then((m) => ({ default: m.KhachHang })));
const GiaCongPage = lazy(() => import("./features/giacong/GiaCongPage").then((m) => ({ default: m.GiaCongPage })));
const ChamCongPage = lazy(() => import("./features/chamcong/ChamCongPage").then((m) => ({ default: m.ChamCongPage })));
const ChamCongScannerPage = lazy(() =>
  import("./features/chamcong/ChamCongScannerPage").then((m) => ({ default: m.ChamCongScannerPage })),
);
const NhanSuPortal = lazy(() => import("./pages/NhanSuPortal").then((m) => ({ default: m.NhanSuPortal })));
const HoSo = lazy(() => import("./pages/HoSo").then((m) => ({ default: m.HoSo })));
const DonTu = lazy(() => import("./pages/DonTu").then((m) => ({ default: m.DonTu })));
const PheDuyet = lazy(() => import("./pages/PheDuyet").then((m) => ({ default: m.PheDuyet })));
const QuanLyDonTu = lazy(() => import("./pages/QuanLyDonTu").then((m) => ({ default: m.QuanLyDonTu })));
const BangCong = lazy(() => import("./pages/BangCong").then((m) => ({ default: m.BangCong })));
const QuanLyBangCong = lazy(() => import("./pages/QuanLyBangCong").then((m) => ({ default: m.QuanLyBangCong })));
const QuanLyNhanSu = lazy(() => import("./pages/QuanLyNhanSu").then((m) => ({ default: m.QuanLyNhanSu })));
const TaiKhoanNganHang = lazy(() => import("./pages/TaiKhoanNganHang").then((m) => ({ default: m.TaiKhoanNganHang })));
// Chỉ còn HAI trang HR dùng trên web (/phat và /bang-luong). Chín trang HR* còn lại từng là giao diện
// riêng cho vỏ APK Capacitor — đã xoá cùng vỏ đó.
const HRPayrollPage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRPayrollPage })));
const HRPenaltyPage = lazy(() => import("./pages/hr/HRPages").then((m) => ({ default: m.HRPenaltyPage })));
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
import { DEFAULT_AUTH_PATH, isHrModulePath } from "./lib/appConfig";
import { BootSkeleton } from "./components/PageSkeleton";
import { Loader2 } from "lucide-react";

const APP_TITLE = "KetoanMini";

const EXTRA_PAGE_TITLES: Record<string, string> = {
  "/": "Đăng nhập",
  "/tam-biet": "Tạm biệt",
  "/tinh-toan": "Tính toán",
  "/tai-apk": "Tải APK",
  "/nhan-su": "Nhân sự",
  "/nhansu": "Quản lý nhân sự",
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
  requires,
  requiresAny,
  publicFallback,
  standalone,
}: {
  children: React.ReactNode;
  /** Quyền BẮT BUỘC để xem trang. Thiếu quyền → đưa về trang mặc định của tài khoản. */
  requires?: Permission;
  /** Có ÍT NHẤT MỘT trong các quyền này là xem được (vd trang dùng chung cho kế toán và nhân sự). */
  requiresAny?: Permission[];
  standalone?: boolean;
  /** Nếu có: khi chưa đăng nhập (hoặc đang tải) sẽ render nội dung công khai này thay vì
   *  chuyển hướng về "/" (màn hình đăng nhập) — dùng cho trang vừa công khai vừa nằm trong app (vd Tải APK). */
  publicFallback?: React.ReactNode;
}) {
  const { user, loading } = useAuth();
  const access = useAccess();
  const loc = useLocation();
  const suppressMainWebSystem = isHrModulePath(loc.pathname);
  // Vẫn đang hỏi backend xem tài khoản này có quyền gì thì PHẢI CHỜ. Đoán bừa lúc chưa biết sẽ đá
  // người có quyền ra khỏi trang họ vừa mở (nháy một cái rồi văng về trang mặc định).
  const waiting = loading || (Boolean(user) && access.loading);
  if (waiting) {
    if (publicFallback !== undefined) return <>{publicFallback}</>;
    return (
      <div className="flex h-screen items-center justify-center">
        <Loader2 className="h-7 w-7 animate-spin text-[var(--accent)]" />
      </div>
    );
  }
  if (!user) {
    if (publicFallback !== undefined) return <>{publicFallback}</>;
    // Chưa đăng nhập → về TRANG GỐC "/", nơi màn hình đăng nhập hiển thị thẳng (không đẩy sang "/login").
    return <Navigate to="/" state={{ from: loc }} replace />;
  }
  // Chặn route ở client CHỈ để khỏi hiện ra trang trống kèm lỗi 403 — nó không phải lớp bảo mật.
  // Người dùng gõ thẳng URL của trang không có quyền vẫn bị backend từ chối ở từng lời gọi API.
  const allowed =
    (!requires || access.can(requires)) &&
    (!requiresAny || access.canAny(...requiresAny));
  if (!allowed) {
    const target = access.landingPath || DEFAULT_AUTH_PATH;
    // Trang mặc định CŨNG không vào được (quyền quá hẹp, hoặc không hỏi được backend) → hiện lời giải
    // thích. Nếu cứ chuyển hướng thì sẽ chuyển vòng tròn về chính trang này.
    if (target === loc.pathname) return <NoAccessScreen failed={access.failed} />;
    return <Navigate to={target} replace />;
  }
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
  // + reset khung chọn sidebar). Trên trang HR chỉ ẩn phần nổi qua prop suppress.
  return <ChatNotificationProvider suppress={suppressMainWebSystem}>{content}</ChatNotificationProvider>;
}

/**
 * Màn hình "không có quyền" — chỉ hiện khi ngay cả trang mặc định của tài khoản cũng không vào được.
 * Phân biệt rõ hai tình huống vì cách xử lý khác hẳn nhau: chưa được cấp quyền (đi hỏi quản trị) và
 * không hỏi được máy chủ (thử lại). Cố ý KHÔNG âm thầm dùng quyền cũ trong trường hợp thứ hai.
 */
function NoAccessScreen({ failed }: { failed: boolean }) {
  const { logout } = useAuth();
  const { reload } = useAccess();
  return (
    <div
      className="flex h-screen flex-col items-center justify-center gap-3 px-6 text-center"
      data-login-route-ready="true"
    >
      <h1 className="text-lg font-bold">
        {failed ? "Chưa xác định được quyền truy cập" : "Tài khoản chưa được cấp quyền"}
      </h1>
      <p className="max-w-md text-sm opacity-70">
        {failed
          ? "Máy chủ chưa trả về được thông tin phân quyền. Vui lòng thử lại; nếu vẫn lỗi, báo quản trị viên."
          : "Tài khoản của bạn hiện chưa được cấp quyền vào mục nào. Vui lòng liên hệ quản trị viên."}
      </p>
      <div className="flex gap-2">
        {failed && (
          <button type="button" className="km-btn" onClick={reload}>
            Thử lại
          </button>
        )}
        <button type="button" className="km-btn" onClick={() => logout()}>
          Đăng xuất
        </button>
      </div>
    </div>
  );
}

/** Chuyển về trang mặc định CỦA TÀI KHOẢN (do backend quyết định trong hồ sơ truy cập), không phải
 *  một hằng số cứng: admin về trang tổng quan, kế toán về khu kế toán, nhân viên về không gian cá nhân. */
function LandingRedirect() {
  const { user, loading } = useAuth();
  const access = useAccess();
  const { search } = useLocation();
  if (loading || (user && access.loading)) return <RouteFallback />;
  // Chưa đăng nhập → về "/" (nơi màn hình đăng nhập hiển thị). GIỮ NGUYÊN query để đường dẫn cũ kiểu
  // "/login?client_mode=..." (nay khớp catch-all này) vẫn chuyển thành "/?client_mode=..." — không mất
  // chức năng ép chế độ đăng nhập QR/ứng dụng.
  if (!user) return <Navigate to={{ pathname: "/", search }} replace />;
  return <Navigate to={access.landingPath || DEFAULT_AUTH_PATH} replace />;
}

/** Trang gốc "/": CHƯA đăng nhập thì hiển thị THẲNG màn hình đăng nhập ngay tại "/" (địa chỉ giữ nguyên
 *  là domain gốc); ĐÃ đăng nhập thì đưa về trang mặc định của tài khoản. */
function RootGate() {
  const { user, loading } = useAuth();
  const access = useAccess();
  if (loading || (user && access.loading)) return <RouteFallback />;
  if (!user) return <Login />;
  return <Navigate to={access.landingPath || DEFAULT_AUTH_PATH} replace />;
}

/** Skeleton toàn màn hình trong lúc chunk cấp cao (trang đăng nhập / lần tải đầu) đang tải về, và
 *  trong lúc còn đang hỏi backend xem đã đăng nhập chưa. */
function RouteFallback() {
  return <BootSkeleton />;
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

function LoginTransitionCoordinator() {
  const { user, loginTransitionPhase, revealLoginTransition } = useAuth();
  const access = useAccess();
  const location = useLocation();

  useEffect(() => {
    if (!user || loginTransitionPhase !== "waiting" || access.loading || location.pathname === "/") return;

    let firstFrame = 0;
    let secondFrame = 0;
    let scheduled = false;
    let cancelled = false;
    const observer = new MutationObserver(() => {
      scheduleRevealWhenReady();
    });

    function isVisible(element: HTMLElement) {
      const style = window.getComputedStyle(element);
      return style.display !== "none"
        && style.visibility !== "hidden"
        && element.getClientRects().length > 0;
    }

    function routeIsReady() {
      const routeSurface = Array
        .from(document.querySelectorAll<HTMLElement>("[data-login-route-ready]"))
        .find((element) => {
          const readyFor = element.dataset.loginRouteReady;
          return readyFor === "true" || readyFor === location.pathname;
        });
      if (!routeSurface) return false;

      const scope = routeSurface.closest<HTMLElement>(".km-page") ?? routeSurface;
      const blockers = scope.querySelectorAll<HTMLElement>(
        ".km-page-skeleton, .km-skeleton, .gc-skeleton, [data-login-blocking='true']",
      );
      return !Array.from(blockers).some(isVisible);
    }

    function scheduleRevealWhenReady() {
      if (cancelled) return;
      if (!routeIsReady()) {
        if (scheduled) {
          window.cancelAnimationFrame(firstFrame);
          window.cancelAnimationFrame(secondFrame);
          firstFrame = 0;
          secondFrame = 0;
          scheduled = false;
        }
        return;
      }
      if (scheduled) return;
      scheduled = true;
      // Hai frame bảo đảm nội dung thật đã qua layout + paint đầu tiên. Kiểm tra lại ở frame cuối
      // vì một effect của trang có thể vừa bắt đầu thêm trạng thái tải dữ liệu mới.
      firstFrame = window.requestAnimationFrame(() => {
        secondFrame = window.requestAnimationFrame(() => {
          scheduled = false;
          if (cancelled || !routeIsReady()) return;
          observer.disconnect();
          revealLoginTransition();
        });
      });
    }

    observer.observe(document.body, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ["class", "style", "hidden", "data-login-blocking"],
    });
    scheduleRevealWhenReady();

    return () => {
      cancelled = true;
      observer.disconnect();
      window.cancelAnimationFrame(firstFrame);
      window.cancelAnimationFrame(secondFrame);
    };
  }, [access.loading, location.pathname, loginTransitionPhase, revealLoginTransition, user]);

  return null;
}

export default function App() {
  return (
    <ThemeProvider>
      <AppNotificationProvider>
        <div className="liquid-bg"><div className="orb" /></div>
        <BrowserRouter>
        <DocumentTitle />
        <RealtimeBoot />
        <AuthProvider>
          {/* Hồ sơ truy cập phải nằm TRONG AuthProvider (cần biết đã đăng nhập chưa) và BAO NGOÀI
              Routes (mọi route đều hỏi quyền qua useAccess). */}
          <AccessProvider>
          <LoginTransitionCoordinator />
          <Suspense fallback={<RouteFallback />}>
          <Routes>
            <Route path="/" element={<RootGate />} />
            {/* Kiosk tạm ẩn. Mã nguồn vẫn được giữ để có thể bật lại khi tiếp tục phát triển. */}
            <Route path="/tam-biet" element={<FarewellPage />} />
            <Route path="/tinh-toan" element={<TinhToan />} />
            {/* ── Trang ai đăng nhập cũng vào được ────────────────────────────────────── */}
            <Route path="/tai-apk" element={<Protected publicFallback={<ApkDownload standalone />}><ApkDownload /></Protected>} />
            <Route path="/tinhtoan" element={<Protected><CongCu /></Protected>} />
            <Route path="/phanhoi" element={<Protected><PhanHoi /></Protected>} />
            <Route path="/caidat" element={<Protected><SystemSettings /></Protected>} />
            <Route path="/call" element={<Protected standalone><CallPage /></Protected>} />

            {/* ── Không gian làm việc cá nhân ─────────────────────────────────────────── */}
            <Route path="/nhan-su" element={<Protected requires={PERM.hrSelfAccess}><NhanSuPortal /></Protected>} />
            <Route path="/hoso" element={<Protected requires={PERM.hrSelfAccess}><HoSo /></Protected>} />
            <Route path="/chats" element={<Protected requires={PERM.chatAccess}><Chats /></Protected>} />
            <Route path="/cong-viec" element={<Protected requires={PERM.tasksSelf}><CongViec /></Protected>} />
            <Route path="/chamcong" element={<Protected requires={PERM.attendanceSelf}><ChamCongScannerPage /></Protected>} />
            <Route path="/bangcong" element={<Protected requires={PERM.attendanceSelf}><BangCong /></Protected>} />
            <Route path="/dontu" element={<Protected requires={PERM.requestsSelf}><DonTu /></Protected>} />
            <Route path="/pheduyet" element={<Protected requires={PERM.requestsApprove}><PheDuyet /></Protected>} />
            <Route path="/phat" element={<Protected requires={PERM.penaltyRead}><HRPenaltyPage /></Protected>} />
            <Route path="/tai-khoan-ngan-hang" element={<Protected requires={PERM.hrSelfAccess}><TaiKhoanNganHang /></Protected>} />

            {/* ── Nghiệp vụ kế toán ───────────────────────────────────────────────────── */}
            {/* /api/dashboard nằm trong nhóm đòi accounting.access, nên trang này cũng phải đòi đúng
                quyền đó — trước đây nhân viên vào được nhưng chỉ nhận về một trang trắng kèm 403. */}
            <Route path="/dashboard" element={<Protected requires={PERM.accountingAccess}><Dashboard /></Protected>} />
            <Route path="/ketoan" element={<Protected requires={PERM.accountingAccess}><CoreAccounting /></Protected>} />
            <Route path="/ban-hang" element={<Protected requires={PERM.accountingAccess}><KeToan /></Protected>} />
            <Route path="/thu-chi" element={<Protected requires={PERM.accountingAccess}><ThuChi /></Protected>} />
            {/* payout.read chứ không phải "chỉ admin": kế toán mới là người lập/duyệt chi (Admin cố ý
                không được đụng tiền mặt). Ai lập/duyệt được thì server chốt tiếp theo phòng ban. */}
            <Route path="/phieu-chi" element={<Protected requires={PERM.payoutRead}><PhieuChi /></Protected>} />
            <Route path="/khachhang" element={<Protected requires={PERM.accountingAccess}><KhachHang /></Protected>} />
            <Route path="/giacong" element={<Protected requires={PERM.accountingAccess}><GiaCongPage /></Protected>} />
            <Route path="/baocao" element={<Protected requires={PERM.reportRead}><BaoCao /></Protected>} />
            {/* Kế toán cũng vào được, nhưng server chỉ trả phần thu chi tiền mặt (AuditEndpoints). */}
            <Route path="/saoluu" element={<Protected requires={PERM.auditRead}><SaoLuu /></Protected>} />

            {/* ── Quản trị ────────────────────────────────────────────────────────────── */}
            {/* Đường dẫn quản lý tài khoản cũ được giữ để bookmark/liên kết không hỏng;
                toàn bộ chức năng đã gộp vào tab Nhân viên của Quản lý nhân sự. */}
            <Route path="/nhansu" element={<Navigate to="/quanly-nhansu" replace />} />
            <Route path="/ql-chamcong" element={<Protected requires={PERM.attendanceManage}><ChamCongPage /></Protected>} />
            <Route path="/quanly-nhansu" element={<Protected requiresAny={[PERM.hrManage, PERM.usersManage]}><QuanLyNhanSu /></Protected>} />
            <Route path="/quanly-dontu" element={<Protected requires={PERM.requestsManage}><QuanLyDonTu /></Protected>} />
            <Route path="/quanly-bangcong" element={<Protected requires={PERM.attendanceRead}><QuanLyBangCong /></Protected>} />
            <Route path="/cong-thong-tin" element={<Protected requires={PERM.portalManage}><CongThongTin /></Protected>} />
            <Route path="/bang-luong" element={<Protected requires={PERM.payrollRead}><HRPayrollPage /></Protected>} />

            {/* Module chưa hiện thực — hiển thị "đang phát triển" giống bản desktop */}
            <Route path="/kho" element={<Protected requires={PERM.accountingAccess}><StubPage title="Hàng tồn kho" /></Protected>} />
            <Route path="/muahang" element={<Protected requires={PERM.accountingAccess}><StubPage title="Mua hàng" /></Protected>} />
            <Route path="/taisan" element={<Protected requires={PERM.accountingAccess}><StubPage title="Tài sản cố định" /></Protected>} />
            <Route path="/danhmuc" element={<Protected requires={PERM.accountingAccess}><StubPage title="Danh mục" /></Protected>} />
            <Route path="/congno" element={<Protected requires={PERM.accountingAccess}><CongNo /></Protected>} />
            <Route path="/nganhang" element={<Protected requires={PERM.accountingAccess}><StubPage title="Ngân hàng" /></Protected>} />
            <Route path="/chiphi" element={<Protected requires={PERM.accountingAccess}><StubPage title="Chi phí" /></Protected>} />
            <Route path="/lichhen" element={<Protected><StubPage title="Lịch hẹn" /></Protected>} />
            <Route path="/tichhop" element={<Protected><StubPage title="Tích hợp" /></Protected>} />

            {/* Đường dẫn lạ → về trang mặc định CỦA TÀI KHOẢN, không phải một trang cứng cho mọi người. */}
            <Route path="*" element={<LandingRedirect />} />
          </Routes>
          </Suspense>
          </AccessProvider>
        </AuthProvider>
        </BrowserRouter>
      </AppNotificationProvider>
    </ThemeProvider>
  );
}
