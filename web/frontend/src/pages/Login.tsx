import { useCallback, useEffect, useRef, useState, type CSSProperties, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { AnimatePresence, LayoutGroup, MotionConfig, motion } from "motion/react";
import {
  ArrowRight,
  Calculator,
  CheckCircle2,
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
import { useAuth, IDLE_LOGOUT_FLAG, webSessionId } from "../lib/auth";
import { api, ApiError } from "../lib/api";
import { APP_BRAND_NAME } from "../lib/branding";
import { useTheme } from "../lib/theme-context";
import "./login.css";

const INTRO_SCENES = [
  {
    id: "focus",
    eyebrow: "Không gian làm việc tập trung",
    title: "Mọi công việc quan trọng, trong một hệ thống.",
    description: "Theo dõi vận hành, xử lý nghiệp vụ và truy cập công cụ hằng ngày nhanh chóng, an toàn.",
    benefits: ["Dữ liệu đồng bộ theo thời gian thực", "Phiên đăng nhập được bảo vệ"],
  },
  {
    id: "realtime",
    eyebrow: "Nhịp vận hành trực tiếp",
    title: "Mọi thay đổi được nhìn thấy ngay khi xảy ra.",
    description: "Theo dõi tiến độ, nhận cập nhật và phối hợp công việc trên cùng một luồng dữ liệu.",
    benefits: ["Cập nhật trạng thái tức thời", "Không bỏ sót việc cần xử lý"],
  },
  {
    id: "secure",
    eyebrow: "Bảo mật theo từng lớp",
    title: "Đúng người truy cập, đúng dữ liệu cần thiết.",
    description: "Kiểm soát phiên đăng nhập và quyền truy cập để mỗi thao tác luôn rõ ràng, an toàn.",
    benefits: ["Quyền truy cập được kiểm soát", "Phiên làm việc được bảo vệ"],
  },
] as const;

type LoginMode = "account" | "qr";
type LoginBootstrapState = "initializing" | "ready" | "error";

type LoginBootstrapResponse = {
  ready: boolean;
  expiresAt: string;
  protocol: string;
  secureTransport: boolean;
};

type LoginModeTransition = {
  id: number;
  direction: 1 | -1;
  originX: number;
  originY: number;
};

const LOGIN_MODE_EASE = [0.16, 1, 0.3, 1] as const;
const LOGIN_MODE_CUE_SPRING = { type: "spring", stiffness: 270, damping: 28, mass: 0.78 } as const;

const LOGIN_MODE_PANEL_VARIANTS = {
  enter: (direction: number) => ({
    opacity: 0,
    x: direction * 52,
    y: 8,
    scale: 0.985,
  }),
  center: {
    opacity: 1,
    x: 0,
    y: 0,
    scale: 1,
    transition: {
      duration: 0.56,
      delay: 0.08,
      ease: LOGIN_MODE_EASE,
      delayChildren: 0.12,
      staggerChildren: 0.055,
    },
  },
  exit: (direction: number) => ({
    opacity: 0,
    x: direction * -34,
    y: -5,
    scale: 0.992,
    transition: { duration: 0.34, ease: [0.4, 0, 1, 1] as const },
  }),
};

const LOGIN_MODE_GROUP_VARIANTS = {
  enter: { opacity: 0, y: 14 },
  center: { opacity: 1, y: 0, transition: { duration: 0.46, ease: LOGIN_MODE_EASE } },
  exit: { opacity: 0, y: -5, transition: { duration: 0.2, ease: [0.4, 0, 1, 1] as const } },
};

export function Login() {
  const { login, completeLoginWithTransition, loginTransitionPhase } = useAuth();
  const { theme, toggle } = useTheme();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const usernameRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const qrHeadingRef = useRef<HTMLHeadingElement>(null);
  const submitButtonRef = useRef<HTMLButtonElement>(null);
  const loginModeViewportRef = useRef<HTMLDivElement>(null);
  const [introScene, setIntroScene] = useState(0);
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
  const [loginSuccess, setLoginSuccess] = useState(false);
  const [showEntrance, setShowEntrance] = useState(true);
  const [bootstrapState, setBootstrapState] = useState<LoginBootstrapState>("initializing");
  const [bootstrapError, setBootstrapError] = useState("");
  const [secureTransport, setSecureTransport] = useState(false);
  const bootstrapExpiresAtRef = useRef(0);
  const bootstrapRequestIdRef = useRef(0);
  const [loginMode, setLoginMode] = useState<LoginMode>("account");
  const [loginModeTransition, setLoginModeTransition] = useState<LoginModeTransition>({
    id: 0,
    direction: 1,
    originX: 0,
    originY: 0,
  });
  const [loginModeTransitioning, setLoginModeTransitioning] = useState(false);
  const [appLoginOpen, setAppLoginOpen] = useState(false);
  const [recoverOpen, setRecoverOpen] = useState(false);
  const requestedClientMode = typeof window !== "undefined"
    ? new URLSearchParams(window.location.search).get("client_mode")
    : null;
  const isAndroidMobile = requestedClientMode === "mobile_app"
    || (requestedClientMode !== "desktop_qr" && typeof navigator !== "undefined" && /Android/i.test(navigator.userAgent));
  const clientMode = isAndroidMobile ? "mobile_app" : "desktop_qr";
  const shouldAutoFocusUsername = typeof window !== "undefined"
    && window.matchMedia("(hover: hover) and (pointer: fine)").matches;
  const activeIntro = INTRO_SCENES[introScene];
  const transitionActive = loginSuccess || loginTransitionPhase !== null;

  const initializeSecureSession = useCallback(async (signal?: AbortSignal) => {
    const requestId = bootstrapRequestIdRef.current + 1;
    bootstrapRequestIdRef.current = requestId;
    setBootstrapState("initializing");
    setBootstrapError("");

    const requestController = new AbortController();
    let timedOut = false;
    const abortFromCaller = () => requestController.abort();
    signal?.addEventListener("abort", abortFromCaller, { once: true });
    const timeoutId = window.setTimeout(() => {
      timedOut = true;
      requestController.abort();
    }, 10_000);

    try {
      const response = await api.postPublic<LoginBootstrapResponse>(
        "/api/auth/bootstrap",
        { sid: webSessionId() },
        requestController.signal,
      );
      if (signal?.aborted || requestId !== bootstrapRequestIdRef.current) return false;
      const expiresAt = Date.parse(response.expiresAt);
      if (!response.ready || response.protocol !== "preauth-v1" || !Number.isFinite(expiresAt)) {
        throw new Error("Máy chủ trả về phiên khởi tạo không hợp lệ.");
      }

      bootstrapExpiresAtRef.current = expiresAt;
      setSecureTransport(response.secureTransport);
      setBootstrapState("ready");
      return true;
    } catch (err) {
      if (signal?.aborted || requestId !== bootstrapRequestIdRef.current) return false;
      bootstrapExpiresAtRef.current = 0;
      setBootstrapState("error");
      setBootstrapError(timedOut
        ? "Máy chủ phản hồi quá lâu. Vui lòng kiểm tra kết nối và thử lại."
        : err instanceof Error
          ? err.message
          : "Không thể kết nối tới máy chủ để khởi tạo phiên.");
      return false;
    } finally {
      window.clearTimeout(timeoutId);
      signal?.removeEventListener("abort", abortFromCaller);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // Đẩy sang microtask để React hoàn tất commit trước khi chuyển trạng thái; lần effect thăm dò
    // của StrictMode sẽ cleanup/abort trước microtask nên không phát hai vé bootstrap ở môi trường dev.
    queueMicrotask(() => {
      if (!controller.signal.aborted) void initializeSecureSession(controller.signal);
    });
    return () => controller.abort();
  }, [initializeSecureSession]);

  const switchLoginMode = (nextMode: LoginMode, trigger?: HTMLElement | null) => {
    if (nextMode === loginMode || loginModeTransitioning) return false;

    const viewportRect = loginModeViewportRef.current?.getBoundingClientRect();
    const triggerRect = trigger?.getBoundingClientRect();
    const fallbackX = (viewportRect?.width ?? 480) / 2;
    const fallbackY = (viewportRect?.height ?? 560) * (nextMode === "qr" ? 0.72 : 0.58);
    const originX = viewportRect && triggerRect
      ? triggerRect.left + triggerRect.width / 2 - viewportRect.left
      : fallbackX;
    const originY = viewportRect && triggerRect
      ? triggerRect.top + triggerRect.height / 2 - viewportRect.top
      : fallbackY;

    setLoginModeTransition((current) => ({
      id: current.id + 1,
      direction: nextMode === "qr" ? 1 : -1,
      originX,
      originY,
    }));
    setLoginModeTransitioning(true);
    setLoginMode(nextMode);
    return true;
  };

  const finishLoginModeTransition = () => {
    setLoginModeTransitioning(false);
    window.requestAnimationFrame(() => {
      if (loginMode === "account") usernameRef.current?.focus({ preventScroll: true });
      else qrHeadingRef.current?.focus({ preventScroll: true });
    });
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError("");
    setLoading(true);
    let authenticated = false;
    try {
      // Vé bootstrap sống ngắn. Nếu người dùng để trang mở lâu, làm mới thật với máy chủ ngay trước
      // khi gửi mật khẩu; không phát lại cinematic vì đây chỉ là gia hạn kỹ thuật trong nền.
      if (bootstrapExpiresAtRef.current <= Date.now() + 10_000) {
        const refreshed = await initializeSecureSession();
        if (!refreshed) throw new Error("Không thể khởi tạo phiên bảo mật. Vui lòng thử lại.");
      }

      let loginResult;
      try {
        loginResult = await login(username.trim(), password);
      } catch (err) {
        // Máy chủ có thể vừa khởi động lại hoặc key bảo vệ vừa đổi. Làm mới handshake đúng một lần
        // rồi gửi lại yêu cầu; 428 xảy ra trước bước kiểm tra mật khẩu nên không nhân đôi đăng nhập.
        if (!(err instanceof ApiError) || err.status !== 428 || !(await initializeSecureSession())) throw err;
        loginResult = await login(username.trim(), password);
      }
      authenticated = true;
      const rect = submitButtonRef.current?.getBoundingClientRect();
      const width = rect?.width ?? Math.min(460, window.innerWidth - 36);
      const height = rect?.height ?? 50;
      const left = rect?.left ?? (window.innerWidth - width) / 2;
      const top = rect?.top ?? (window.innerHeight - height) / 2;
      setLoginSuccess(true);
      completeLoginWithTransition(loginResult, {
        left,
        top,
        width,
        height,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Đăng nhập thất bại.");
    } finally {
      if (!authenticated) setLoading(false);
    }
  };

  return (
    <main
      className="login-page"
      data-client-mode={clientMode}
      data-login-success={transitionActive ? "true" : undefined}
      inert={transitionActive ? true : undefined}
      aria-busy={transitionActive || (showEntrance && bootstrapState === "initializing")}
    >
      {showEntrance && (
        <div
          className="login-entry-sequence"
          data-init-state={bootstrapState}
          onAnimationEnd={(event) => {
            if (event.target === event.currentTarget && event.animationName === "login-entry-dismiss") {
              setShowEntrance(false);
            }
          }}
        >
          <span className="login-entry-folio login-entry-folio-left" aria-hidden="true" />
          <span className="login-entry-folio login-entry-folio-right" aria-hidden="true" />
          <span className="login-entry-ledger" aria-hidden="true" />
          <span className="login-entry-fold" aria-hidden="true" />
          <span className="login-entry-paper-light" aria-hidden="true" />

          <div className="login-entry-meta login-entry-meta-top" aria-hidden="true">
            <span>SỔ CÁI DOANH NGHIỆP</span>
            <strong>
              {bootstrapState === "error"
                ? "CẦN KẾT NỐI"
                : bootstrapState === "ready"
                  ? "ĐÃ SẴN SÀNG"
                  : "ĐANG CHUẨN BỊ"}
            </strong>
          </div>
          <div className="login-entry-meta login-entry-meta-bottom" aria-hidden="true">
            <span>{secureTransport ? "KẾT NỐI ĐƯỢC MÃ HÓA" : "KẾT NỐI MÁY CHỦ"}</span>
            <strong>
              {bootstrapState === "error"
                ? "GIÁN ĐOẠN"
                : bootstrapState === "ready"
                  ? "ĐÃ XÁC MINH"
                  : "ĐANG KIỂM TRA"}
            </strong>
          </div>

          <div className="login-entry-seal" aria-hidden="true">
            <span className="login-entry-seal-frame login-entry-seal-frame-outer" />
            <span className="login-entry-seal-frame login-entry-seal-frame-inner" />
            <span className="login-entry-seal-rule login-entry-seal-rule-top" />
            <span className="login-entry-monogram">CP</span>
            <span className="login-entry-seal-caption">QUẢN TRỊ TÀI CHÍNH</span>
            <span className="login-entry-seal-rule login-entry-seal-rule-bottom" />
          </div>

          <div
            className="login-entry-status"
            role={bootstrapState === "error" ? "alert" : "status"}
            aria-live="polite"
          >
            <span className="login-entry-status-line">
              <i aria-hidden="true" />
              {bootstrapState === "error"
                ? "Không thể mở phiên làm việc bảo mật"
                : bootstrapState === "ready"
                  ? "Phiên làm việc đã sẵn sàng"
                  : "Đang mở phiên làm việc bảo mật"}
            </span>
            <strong>{APP_BRAND_NAME}</strong>
            <span className="login-entry-progress" aria-hidden="true"><i /></span>
            {bootstrapState === "error" && (
              <>
                <small>{bootstrapError}</small>
                <button type="button" onClick={() => void initializeSecureSession()}>
                  Thử mở lại phiên
                </button>
              </>
            )}
          </div>

          <span className="login-entry-page-mark login-entry-page-mark-tl" aria-hidden="true" />
          <span className="login-entry-page-mark login-entry-page-mark-tr" aria-hidden="true" />
          <span className="login-entry-page-mark login-entry-page-mark-bl" aria-hidden="true" />
          <span className="login-entry-page-mark login-entry-page-mark-br" aria-hidden="true" />
        </div>
      )}

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
        <aside
          className="login-intro"
          data-scene={activeIntro.id}
          data-active={introScene === 0 ? undefined : "true"}
        >
          <div className="login-intro-palette" aria-hidden="true">
            <span className="login-intro-palette-focus" />
            <span className="login-intro-palette-realtime" />
            <span className="login-intro-palette-secure" />
          </div>
          <div className="login-intro-glow" aria-hidden="true" />
          <div className="login-motion-field" aria-hidden="true">
            <span className="login-motion-shine" />
            <span className="login-motion-orbit login-motion-orbit-primary">
              <i />
            </span>
            <span className="login-motion-orbit login-motion-orbit-secondary">
              <i />
            </span>
            <span className="login-motion-particle login-motion-particle-one" />
            <span className="login-motion-particle login-motion-particle-two" />
            <span className="login-motion-particle login-motion-particle-three" />
          </div>
          <div className="login-orbit-hotspots">
            <button
              type="button"
              aria-label="Khám phá vận hành thời gian thực"
              className="login-orbit-hotspot login-orbit-hotspot-realtime"
              onPointerEnter={() => setIntroScene(1)}
              onPointerLeave={() => setIntroScene(0)}
              onFocus={() => setIntroScene(1)}
              onBlur={() => setIntroScene(0)}
              onClick={() => setIntroScene(1)}
            />
            <button
              type="button"
              aria-label="Khám phá bảo mật dữ liệu"
              className="login-orbit-hotspot login-orbit-hotspot-secure"
              onPointerEnter={() => setIntroScene(2)}
              onPointerLeave={() => setIntroScene(0)}
              onFocus={() => setIntroScene(2)}
              onBlur={() => setIntroScene(0)}
              onClick={() => setIntroScene(2)}
            />
          </div>
          <div className="login-intro-content">
          <div className="login-brand">
            <span className="login-brand-mark" aria-hidden="true">CP</span>
            <span>
              <strong>{APP_BRAND_NAME}</strong>
              <small>Nền tảng vận hành doanh nghiệp</small>
            </span>
          </div>

          <div key={activeIntro.id} className="login-intro-copy">
            <span className="login-eyebrow"><Sparkles aria-hidden="true" /> {activeIntro.eyebrow}</span>
            <h1>{activeIntro.title}</h1>
            <p>{activeIntro.description}</p>
          </div>

          <div key={`${activeIntro.id}-benefits`} className="login-benefits" aria-label="Lợi ích hệ thống">
            <span><CheckCircle2 aria-hidden="true" /> {activeIntro.benefits[0]}</span>
            <span><ShieldCheck aria-hidden="true" /> {activeIntro.benefits[1]}</span>
          </div>
          </div>
        </aside>

        <section
          className="login-card"
          data-mode={loginMode}
          data-mode-transitioning={loginModeTransitioning ? "true" : undefined}
        >
          <MotionConfig reducedMotion="never">
            <LayoutGroup id="login-auth-method">
              <motion.div
                ref={loginModeViewportRef}
                className="login-mode-viewport"
                style={{
                  "--login-mode-origin-x": `${loginModeTransition.originX}px`,
                  "--login-mode-origin-y": `${loginModeTransition.originY}px`,
                } as CSSProperties}
                inert={loginModeTransitioning ? true : undefined}
                aria-busy={loginModeTransitioning}
              >
                <AnimatePresence initial={false} custom={loginModeTransition.direction} mode="sync">
                  <motion.div
                    key={loginMode}
                    className="login-mode-panel"
                    data-mode={loginMode}
                    custom={loginModeTransition.direction}
                    variants={LOGIN_MODE_PANEL_VARIANTS}
                    initial="enter"
                    animate="center"
                    exit="exit"
                    layoutScroll
                  >
                    {loginMode === "qr" ? (
                      <>
                        <motion.div className="login-mode-group login-mode-group--intro" variants={LOGIN_MODE_GROUP_VARIANTS}>
                          <div className="login-card-head">
                            <motion.span
                              layoutId="login-auth-method-cue"
                              className="login-welcome-icon login-mode-cue login-mode-cue--header"
                              transition={LOGIN_MODE_CUE_SPRING}
                              aria-hidden="true"
                            >
                              <QrCode />
                            </motion.span>
                            <div>
                              <p>Đăng nhập nhanh</p>
                              <h2 ref={qrHeadingRef} tabIndex={-1}>Đăng nhập bằng mã QR</h2>
                            </div>
                          </div>
                          <p className="login-card-subtitle">Dùng ứng dụng Nhân sự đã đăng nhập để quét mã bên dưới và vào hệ thống.</p>
                        </motion.div>

                        <motion.div className="login-mode-group login-mode-group--primary" variants={LOGIN_MODE_GROUP_VARIANTS}>
                          <QrLoginModal
                            embedded
                            className="login-mode-qr-content"
                            onClose={(trigger) => switchLoginMode("account", trigger)}
                          />
                        </motion.div>
                      </>
                    ) : (
                      <>
                        <motion.div className="login-mode-group login-mode-group--intro" variants={LOGIN_MODE_GROUP_VARIANTS}>
                          <div className="login-card-head">
                            <span className="login-welcome-icon"><LogIn aria-hidden="true" /></span>
                            <div>
                              <p>Chào mừng trở lại</p>
                              <h2>Đăng nhập tài khoản</h2>
                            </div>
                          </div>
                          <p className="login-card-subtitle">Nhập thông tin được cấp để tiếp tục vào hệ thống.</p>
                        </motion.div>

                        <motion.div className="login-mode-group login-mode-group--primary" variants={LOGIN_MODE_GROUP_VARIANTS}>
                          <form onSubmit={submit} className="login-form">
                            <div className="login-field">
                              <label htmlFor="login-username">Tên đăng nhập</label>
                              <span className="login-input-wrap">
                                <UserRound aria-hidden="true" />
                                <input
                                  ref={usernameRef}
                                  id="login-username"
                                  autoFocus={loginModeTransition.id === 0 && shouldAutoFocusUsername}
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

                            <button
                              ref={submitButtonRef}
                              data-logout-transition-target="true"
                              type="submit"
                              className="login-submit"
                              disabled={loading || Boolean(loginSuccess) || bootstrapState !== "ready"}
                            >
                              <span>{loginSuccess ? "Đăng nhập thành công" : loading ? "Đang đăng nhập…" : "Đăng nhập"}</span>
                              {loginSuccess ? <CheckCircle2 aria-hidden="true" /> : <ArrowRight aria-hidden="true" />}
                            </button>
                          </form>
                        </motion.div>

                        <motion.div className="login-mode-group login-mode-group--secondary" variants={LOGIN_MODE_GROUP_VARIANTS}>
                          <div className="login-divider"><span>hoặc</span></div>
                          <button
                            type="button"
                            className="login-qr-button"
                            onClick={(event) => isAndroidMobile
                              ? setAppLoginOpen(true)
                              : switchLoginMode("qr", event.currentTarget)}
                          >
                            <motion.span
                              layoutId={isAndroidMobile ? undefined : "login-auth-method-cue"}
                              className="login-mode-cue login-mode-cue--button"
                              transition={LOGIN_MODE_CUE_SPRING}
                              aria-hidden="true"
                            >
                              {isAndroidMobile ? <Smartphone /> : <QrCode />}
                            </motion.span>
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
                          <nav className="login-quick-grid login-quick-grid--single" aria-label="Truy cập nhanh">
                            <Link to="/tinh-toan" className="login-quick-link login-quick-calculator">
                              <span><Calculator aria-hidden="true" /></span>
                              <div><strong>Tính toán</strong><small>Công cụ nghiệp vụ</small></div>
                              <ArrowRight aria-hidden="true" />
                            </Link>
                          </nav>
                        </motion.div>
                      </>
                    )}
                  </motion.div>
                </AnimatePresence>

                <AnimatePresence initial={false}>
                  {loginModeTransitioning && (
                    <motion.span
                      key={loginModeTransition.id}
                      className="login-mode-lens"
                      data-direction={loginModeTransition.direction}
                      initial={{ opacity: 0, scale: 0.16, rotate: loginModeTransition.direction * -5 }}
                      animate={{
                        opacity: [0, 0.82, 0.42, 0],
                        scale: [0.16, 0.74, 1.68, 2.7],
                        rotate: [
                          loginModeTransition.direction * -5,
                          loginModeTransition.direction * -1,
                          loginModeTransition.direction * 2,
                          loginModeTransition.direction * 4,
                        ],
                      }}
                      transition={{ duration: 0.9, times: [0, 0.2, 0.58, 1], ease: LOGIN_MODE_EASE }}
                      onAnimationComplete={finishLoginModeTransition}
                      aria-hidden="true"
                    />
                  )}
                </AnimatePresence>
              </motion.div>

              <motion.p
                className="login-security-note"
                layout="position"
                transition={{ layout: { duration: 0.52, ease: LOGIN_MODE_EASE } }}
              >
                <ShieldCheck aria-hidden="true" /> Kết nối bảo mật · Không chia sẻ thông tin đăng nhập
              </motion.p>
            </LayoutGroup>
          </MotionConfig>
        </section>
      </section>

      {appLoginOpen && (
        <AppLoginModal
          onClose={() => setAppLoginOpen(false)}
        />
      )}
      {recoverOpen && <RecoveryResetModal onClose={() => setRecoverOpen(false)} />}
    </main>
  );
}
