import { useRef, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  ArrowRight,
  Calculator,
  CheckCircle2,
  Clock3,
  Eye,
  EyeOff,
  LockKeyhole,
  LogIn,
  Moon,
  QrCode,
  ShieldCheck,
  Smartphone,
  Sparkles,
  Sun,
  UserRound,
} from "lucide-react";
import { RecoveryResetModal } from "../components/RecoveryResetModal";
import { AppLoginModal } from "../components/AppLoginModal";
import { QrLoginModal } from "../components/QrLoginModal";
import { useAuth, IDLE_LOGOUT_FLAG } from "../lib/auth";
import { DEFAULT_AUTH_PATH } from "../lib/appConfig";
import { APP_BRAND_NAME } from "../lib/branding";
import { useTheme } from "../lib/theme-context";
import "./login.css";

export function Login() {
  const { login } = useAuth();
  const { theme, toggle } = useTheme();
  const nav = useNavigate();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const passwordRef = useRef<HTMLInputElement>(null);
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState(() => {
    try {
      if (sessionStorage.getItem(IDLE_LOGOUT_FLAG)) {
        sessionStorage.removeItem(IDLE_LOGOUT_FLAG);
        return "Bạn đã được tự động đăng xuất do không hoạt động một thời gian.";
      }
    } catch { /* Trình duyệt không cho phép dùng session storage. */ }
    return "";
  });
  const [loading, setLoading] = useState(false);
  const [qrOpen, setQrOpen] = useState(false);
  const [appLoginOpen, setAppLoginOpen] = useState(false);
  const [recoverOpen, setRecoverOpen] = useState(false);
  const requestedClientMode = typeof window !== "undefined"
    ? new URLSearchParams(window.location.search).get("client_mode")
    : null;
  const isAndroidMobile = requestedClientMode === "mobile_app"
    || (requestedClientMode !== "desktop_qr" && typeof navigator !== "undefined" && /Android/i.test(navigator.userAgent));
  const clientMode = isAndroidMobile ? "mobile_app" : "desktop_qr";

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError("");
    setLoading(true);
    try {
      await login(username.trim(), password);
      nav(DEFAULT_AUTH_PATH);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Đăng nhập thất bại.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="login-page" data-client-mode={clientMode}>
      <button
        type="button"
        className="login-theme-toggle"
        onClick={toggle}
        aria-label={`Chuyển sang giao diện ${theme === "light" ? "tối" : "sáng"}`}
        title={`Chuyển sang giao diện ${theme === "light" ? "tối" : "sáng"}`}
      >
        {theme === "light" ? <Moon aria-hidden="true" /> : <Sun aria-hidden="true" />}
      </button>

      <section className="login-shell" aria-label="Đăng nhập hệ thống">
        <aside className="login-intro">
          <div className="login-intro-glow" aria-hidden="true" />
          <div className="login-brand">
            <span className="login-brand-mark" aria-hidden="true">CP</span>
            <span>
              <strong>{APP_BRAND_NAME}</strong>
              <small>Nền tảng vận hành doanh nghiệp</small>
            </span>
          </div>

          <div className="login-intro-copy">
            <span className="login-eyebrow"><Sparkles aria-hidden="true" /> Không gian làm việc tập trung</span>
            <h1>Mọi công việc quan trọng, trong một hệ thống.</h1>
            <p>Theo dõi vận hành, xử lý nghiệp vụ và truy cập công cụ hằng ngày nhanh chóng, an toàn.</p>
          </div>

          <div className="login-benefits" aria-label="Lợi ích hệ thống">
            <span><CheckCircle2 aria-hidden="true" /> Dữ liệu đồng bộ theo thời gian thực</span>
            <span><ShieldCheck aria-hidden="true" /> Phiên đăng nhập được bảo vệ</span>
          </div>
        </aside>

        <section className="login-card">
          {qrOpen ? (
            <>
              <div className="login-card-head">
                <span className="login-welcome-icon"><QrCode aria-hidden="true" /></span>
                <div>
                  <p>Đăng nhập nhanh</p>
                  <h2>Đăng nhập bằng mã QR</h2>
                </div>
              </div>
              <p className="login-card-subtitle">Dùng ứng dụng Nhân sự đã đăng nhập để quét mã bên dưới và vào hệ thống.</p>
              <QrLoginModal
                embedded
                onClose={() => setQrOpen(false)}
                onSuccess={() => nav(DEFAULT_AUTH_PATH)}
              />
            </>
          ) : (
            <>
          <div className="login-card-head">
            <span className="login-welcome-icon"><LogIn aria-hidden="true" /></span>
            <div>
              <p>Chào mừng trở lại</p>
              <h2>Đăng nhập tài khoản</h2>
            </div>
          </div>
          <p className="login-card-subtitle">Nhập thông tin được cấp để tiếp tục vào hệ thống.</p>

          <form onSubmit={submit} className="login-form">
            <div className="login-field">
              <label htmlFor="login-username">Tên đăng nhập</label>
              <span className="login-input-wrap">
                <UserRound aria-hidden="true" />
                <input
                  id="login-username"
                  autoFocus
                  autoComplete="username"
                  value={username}
                  onChange={(event) => setUsername(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === "Tab" && !event.shiftKey) {
                      event.preventDefault();
                      passwordRef.current?.focus();
                    }
                  }}
                  placeholder="Nhập tên đăng nhập"
                  required
                />
              </span>
            </div>

            <div className="login-field login-password-field">
              <label htmlFor="login-password">Mật khẩu</label>
              <span className="login-input-wrap">
                <LockKeyhole aria-hidden="true" />
                <input
                  ref={passwordRef}
                  id="login-password"
                  type={showPassword ? "text" : "password"}
                  autoComplete="current-password"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  placeholder="Nhập mật khẩu"
                  required
                />
                <button
                  type="button"
                  className="login-password-toggle"
                  onClick={() => setShowPassword((value) => !value)}
                  aria-label={showPassword ? "Ẩn mật khẩu" : "Hiện mật khẩu"}
                >
                  {showPassword ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
                </button>
              </span>
              <button
                type="button"
                className="login-forgot-button"
                onClick={() => setRecoverOpen(true)}
              >
                Quên mật khẩu?
              </button>
            </div>

            {error && <div className="login-error" role="alert">{error}</div>}

            <button type="submit" className="login-submit" disabled={loading}>
              <span>{loading ? "Đang đăng nhập…" : "Đăng nhập"}</span>
              <ArrowRight aria-hidden="true" />
            </button>
          </form>

          <>
              <div className="login-divider"><span>hoặc</span></div>
              <button
                type="button"
                className="login-qr-button"
                onClick={() => isAndroidMobile ? setAppLoginOpen(true) : setQrOpen(true)}
              >
                {isAndroidMobile ? <Smartphone aria-hidden="true" /> : <QrCode aria-hidden="true" />}
                <span>
                  <strong>{isAndroidMobile ? "Đăng nhập bằng ứng dụng Nhân sự" : "Đăng nhập bằng mã QR"}</strong>
                  <small>{isAndroidMobile ? "Mở ứng dụng Android để xác nhận" : "Dùng ứng dụng đã đăng nhập để quét mã"}</small>
                </span>
                <ArrowRight aria-hidden="true" />
              </button>

              <div className="login-quick-head">
                <span>Truy cập nhanh</span>
                <small>Không cần đăng nhập</small>
              </div>
              <nav className="login-quick-grid" aria-label="Truy cập nhanh">
                <Link to="/kiosk" className="login-quick-link login-quick-attendance">
                  <span><Clock3 aria-hidden="true" /></span>
                  <div><strong>Chấm công</strong><small>Ghi nhận vào / ra</small></div>
                  <ArrowRight aria-hidden="true" />
                </Link>
                <Link to="/tinh-toan" className="login-quick-link login-quick-calculator">
                  <span><Calculator aria-hidden="true" /></span>
                  <div><strong>Tính toán</strong><small>Công cụ nghiệp vụ</small></div>
                  <ArrowRight aria-hidden="true" />
                </Link>
              </nav>
          </>
            </>
          )}

          <p className="login-security-note"><ShieldCheck aria-hidden="true" /> Kết nối bảo mật · Không chia sẻ thông tin đăng nhập</p>
        </section>
      </section>

      {appLoginOpen && (
        <AppLoginModal
          onClose={() => setAppLoginOpen(false)}
          onSuccess={() => nav(DEFAULT_AUTH_PATH)}
        />
      )}
      {recoverOpen && <RecoveryResetModal onClose={() => setRecoverOpen(false)} />}
    </main>
  );
}
