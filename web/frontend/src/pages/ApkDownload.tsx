import { useEffect, useRef, useState } from "react";
import type { LucideIcon } from "lucide-react";
import {
  BarChart3,
  BellRing,
  CalendarCheck2,
  Camera,
  ClipboardCheck,
  Download,
  FileCheck2,
  Fingerprint,
  Gauge,
  PackageCheck,
  RefreshCw,
  ShieldCheck,
  Smartphone,
  UsersRound,
  WalletCards,
} from "lucide-react";
import { QRCodeCanvas } from "qrcode.react";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Badge, Button, Skeleton } from "../components/ui";
import { useAppNotifications } from "../components/AppNotifications";
import { appUrl } from "../lib/appConfig";
import { dateTime } from "../lib/format";
import type { AppUpdateInfo } from "../lib/types";
import { useApi } from "../lib/useApi";
import "./apk-download.css";

const RELEASE_PATH = "/api/releases/public/latest?appTarget=hr-apk";
const HERO_SLOGANS = [
  "trên từng ngày làm việc",
  "để mỗi nỗ lực được ghi nhận",
  "bằng sự minh bạch và tin cậy",
  "vì một tập thể gắn kết",
  "trên mọi chặng đường phát triển",
];
const FEATURE_ROTATION_MS = 5200;
const SCREEN_KEYS = ["download", "features", "install"] as const;

type ScreenKey = (typeof SCREEN_KEYS)[number];

type FeatureCard = {
  title: string;
  description: string;
  icon: LucideIcon;
};

type FeatureGroup = {
  key: string;
  label: string;
  intro: string;
  features: FeatureCard[];
};

const FEATURE_GROUPS: FeatureGroup[] = [
  {
    key: "employee",
    label: "Nhân viên",
    intro: "Một ứng dụng gọn trên điện thoại để nhân viên tự theo dõi ngày công, đơn từ và thông tin cá nhân.",
    features: [
      {
        title: "Chấm công nhanh",
        description: "Ghi nhận giờ vào ra bằng camera, vị trí hoặc mã xác nhận theo cấu hình của công ty.",
        icon: Fingerprint,
      },
      {
        title: "Hồ sơ cá nhân",
        description: "Xem thông tin phòng ban, chức danh, hợp đồng và dữ liệu liên hệ ngay trên điện thoại.",
        icon: UsersRound,
      },
      {
        title: "Đơn từ một chạm",
        description: "Gửi nghỉ phép, đi muộn, đổi ca hoặc công tác với trạng thái xử lý rõ ràng.",
        icon: FileCheck2,
      },
      {
        title: "Thông báo chủ động",
        description: "Nhắc ca làm, lịch duyệt đơn, thông báo bảng công và các cập nhật quan trọng.",
        icon: BellRing,
      },
    ],
  },
  {
    key: "operation",
    label: "Quản lý",
    intro: "Bộ công cụ theo dõi vận hành giúp quản lý nắm tình hình nhân sự nhanh hơn mỗi ngày.",
    features: [
      {
        title: "Bảng công thời gian thực",
        description: "Tổng hợp đi muộn, về sớm, thiếu công và tăng ca theo từng bộ phận.",
        icon: BarChart3,
      },
      {
        title: "Duyệt đơn linh hoạt",
        description: "Xử lý nhiều loại đề xuất với lịch sử duyệt, ghi chú và phân quyền theo vai trò.",
        icon: ClipboardCheck,
      },
      {
        title: "Quản lý ca kíp",
        description: "Theo dõi lịch làm việc, ca thay thế và cảnh báo khi có điểm bất thường.",
        icon: CalendarCheck2,
      },
      {
        title: "Bảo vệ dữ liệu",
        description: "Giới hạn quyền xem, ghi nhận lịch sử thao tác và đồng bộ an toàn với hệ thống.",
        icon: ShieldCheck,
      },
    ],
  },
  {
    key: "utility",
    label: "Tiện ích",
    intro: "Các tiện ích mở rộng giúp ứng dụng sẵn sàng cho nhiều quy trình nội bộ trong tương lai.",
    features: [
      {
        title: "Làm việc khi mạng yếu",
        description: "Lưu tạm thao tác quan trọng và tự đồng bộ lại khi kết nối ổn định.",
        icon: Smartphone,
      },
      {
        title: "Phiếu lương cá nhân",
        description: "Cho phép xem lương, phụ cấp, khấu trừ và lịch sử thanh toán theo kỳ.",
        icon: WalletCards,
      },
      {
        title: "Xác thực hình ảnh",
        description: "Hỗ trợ quy trình xác minh bằng camera cho các điểm chấm công cần kiểm soát cao.",
        icon: Camera,
      },
      {
        title: "Tối ưu hiệu năng",
        description: "Giao diện nhẹ, phản hồi nhanh và phù hợp với thiết bị Android phổ biến.",
        icon: Gauge,
      },
    ],
  },
];

export function ApkDownload({ standalone = false }: { standalone?: boolean }) {
  const { data, loading, error, reload } = useApi<AppUpdateInfo>(RELEASE_PATH);
  const { notify } = useAppNotifications();
  const pageRef = useRef<HTMLDivElement | null>(null);
  const [sloganIndex, setSloganIndex] = useState(0);
  const [featureGroupIndex, setFeatureGroupIndex] = useState(0);
  const [activeScreen, setActiveScreen] = useState<ScreenKey>("download");
  const [revealedScreens, setRevealedScreens] = useState<Set<ScreenKey>>(() => new Set(["download"]));
  const activeFeatureGroup = FEATURE_GROUPS[featureGroupIndex];
  const release = data?.hasUpdate ? data : null;
  const downloadUrl = release?.downloadUrl ? appUrl(release.downloadUrl) : "";
  const pageUrl = typeof window === "undefined" ? "" : window.location.href;
  const shouldRenderInstall = revealedScreens.has("install");

  const screenClass = (screenKey: ScreenKey) =>
    `apk-scroll-screen ${activeScreen === screenKey ? "is-active" : ""} ${revealedScreens.has(screenKey) ? "is-revealed" : ""}`;

  useEffect(() => {
    if (activeScreen !== "download") return;

    const intervalId = window.setInterval(() => {
      setSloganIndex((current) => (current + 1) % HERO_SLOGANS.length);
    }, 2600);

    return () => window.clearInterval(intervalId);
  }, [activeScreen]);

  useEffect(() => {
    if (activeScreen !== "features") return;

    const timeoutId = window.setTimeout(() => {
      setFeatureGroupIndex((current) => (current + 1) % FEATURE_GROUPS.length);
    }, FEATURE_ROTATION_MS);

    return () => window.clearTimeout(timeoutId);
  }, [activeScreen, featureGroupIndex]);

  useEffect(() => {
    const page = pageRef.current;
    if (!page || !("IntersectionObserver" in window)) {
      setRevealedScreens(new Set(SCREEN_KEYS));
      return;
    }

    const screens = Array.from(page.querySelectorAll<HTMLElement>("[data-apk-screen]"));
    const scrollRoot = page.closest(".apk-public-page");
    const observer = new IntersectionObserver(
      (entries) => {
        const visibleEntries = entries.filter((entry) => entry.isIntersecting);
        if (visibleEntries.length === 0) return;

        setRevealedScreens((current) => {
          let next = current;
          for (const entry of visibleEntries) {
            const screenKey = entry.target.getAttribute("data-apk-screen") as ScreenKey | null;
            if (screenKey && !next.has(screenKey)) {
              next = new Set(next);
              next.add(screenKey);
            }
          }
          return next;
        });

        const activeEntry = visibleEntries.reduce((best, entry) =>
          entry.intersectionRatio > best.intersectionRatio ? entry : best,
        );
        const nextActive = activeEntry.target.getAttribute("data-apk-screen") as ScreenKey | null;
        if (nextActive && activeEntry.intersectionRatio >= 0.35) {
          setActiveScreen(nextActive);
        }
      },
      {
        root: scrollRoot,
        rootMargin: "-12% 0px -18% 0px",
        threshold: [0.18, 0.35, 0.55, 0.72],
      },
    );

    screens.forEach((screen) => observer.observe(screen));
    return () => observer.disconnect();
  }, []);

  const copyLink = async () => {
    if (!pageUrl) return;
    try {
      await navigator.clipboard.writeText(pageUrl);
      notify.success("Đã sao chép liên kết tải APK.");
    } catch {
      notify.error("Không sao chép được liên kết.");
    }
  };

  const content = (
    <div className="apk-download-page gc-root" ref={pageRef}>
      <header className="apk-landing-nav">
        <div className="apk-brand">
          <span className="apk-brand-mark">ĐH</span>
          <span>Đồng Hành</span>
        </div>
        <nav aria-label="Thông tin tải ứng dụng">
          <a href="#download">Tải ứng dụng</a>
          <a href="#features">Tính năng</a>
          <a href="#install">Hướng dẫn</a>
          <a href="#install">Hỗ trợ</a>
        </nav>
        <button type="button" className="apk-refresh-btn" onClick={() => reload()} aria-label="Làm mới thông tin APK">
          <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
          Làm mới
        </button>
      </header>

      <section className={`apk-hero ${screenClass("download")}`} id="download" data-apk-screen="download">
        <p className="apk-hero-kicker">Đồng Hành Mobile</p>
        <h1>
          Đồng hành cùng đội ngũ
          <span
            className="apk-rotating-line"
            aria-label={HERO_SLOGANS.join(", ")}
          >
            <span key={HERO_SLOGANS[sloganIndex]}>{HERO_SLOGANS[sloganIndex]}</span>
          </span>
        </h1>
        <p className="apk-hero-copy">
          Ứng dụng giúp đội ngũ chấm công, gửi đơn từ và theo dõi thông tin cá nhân nhanh chóng,
          an toàn, luôn đồng bộ với hệ thống công ty.
        </p>
        {release ? (
          <a className="apk-hero-button" href={downloadUrl} download="DongHanh.apk">
            <Download className="h-5 w-5" />
            Tải ứng dụng
          </a>
        ) : (
          <button className="apk-hero-button" type="button" disabled>
            <Download className="h-5 w-5" />
            Chưa có bản tải
          </button>
        )}
      </section>

      <section
        className={`apk-features-section ${screenClass("features")}`}
        id="features"
        data-apk-screen="features"
        aria-labelledby="apk-features-title"
      >
        <div className="apk-section-heading">
          <p className="apk-section-kicker">Điểm mạnh ứng dụng</p>
          <h2 id="apk-features-title">Tính năng nổi bật</h2>
          <p className="apk-feature-lead">Thiết kế cho đội ngũ nhân sự cần thao tác nhanh, dữ liệu rõ và đồng bộ với hệ thống công ty.</p>
        </div>

        <div className="apk-feature-tabs" role="tablist" aria-label="Nhóm tính năng">
          {FEATURE_GROUPS.map((group, index) => (
            <button
              key={group.key}
              type="button"
              role="tab"
              aria-selected={index === featureGroupIndex}
              className={`apk-feature-tab ${index === featureGroupIndex ? "is-active" : ""}`}
              onClick={() => setFeatureGroupIndex(index)}
            >
              {group.label}
            </button>
          ))}
        </div>

        <p key={`${activeFeatureGroup.key}-intro`} className="apk-feature-summary">
          {activeFeatureGroup.intro}
        </p>

        <div key={activeFeatureGroup.key} className="apk-feature-grid">
          {activeFeatureGroup.features.map((feature, index) => {
            const Icon = feature.icon;
            return (
              <article key={feature.title} className="apk-feature-card" style={{ animationDelay: `${index * 80}ms` }}>
                <div className="apk-feature-icon" aria-hidden="true">
                  <Icon className="h-7 w-7" />
                </div>
                <h3>{feature.title}</h3>
                <p>{feature.description}</p>
              </article>
            );
          })}
        </div>
      </section>

      <section className={`apk-install-section ${screenClass("install")}`} id="install" data-apk-screen="install" aria-label="Tải và cài đặt ứng dụng">
        {shouldRenderInstall ? (
          <>
            {error && (
              <GlassPanel strong className="apk-alert-panel">
                <ShieldCheck className="h-5 w-5" />
                <span>Không tải được thông tin APK: {error}</span>
              </GlassPanel>
            )}

            {loading && !data ? (
              <ApkLoadingState />
            ) : release ? (
              <section className="apk-download-grid">
                <GlassPanel strong className="apk-release-panel">
                  <div className="apk-release-heading">
                    <div className="apk-app-icon" aria-hidden="true">
                      <AndroidMark />
                    </div>
                    <div className="min-w-0">
                      <p className="apk-kicker">Sẵn sàng cài đặt</p>
                      <h2>Bản ứng dụng mới nhất</h2>
                      <div className="apk-badge-row">
                        <Badge color="success">Đã phát hành</Badge>
                        {release.isMandatory ? <Badge color="danger">Bắt buộc</Badge> : <Badge color="muted">Tùy chọn</Badge>}
                      </div>
                    </div>
                  </div>

                  <div className="apk-notes" id="guide">
                    <span>Ghi chú phát hành</span>
                    <p>{release.releaseNotes?.trim() || "Chưa có ghi chú cho bản phát hành này."}</p>
                  </div>

                  <div className="apk-release-footer">
                    <div>
                      <span>Ngày phát hành</span>
                      <strong>{dateTime(release.publishedAt)}</strong>
                    </div>
                    <a className="apk-download-button" href={downloadUrl} download="DongHanh.apk">
                      <Download className="h-5 w-5" />
                      Tải ứng dụng
                    </a>
                  </div>
                </GlassPanel>

                <GlassPanel strong className="apk-qr-panel" id="support">
                  <div className="apk-qr-icon" aria-hidden="true">
                    <ShieldCheck className="h-5 w-5" />
                  </div>
                  <h2>Mở trên điện thoại</h2>
                  <div className="apk-qr-box">
                    {pageUrl ? <QRCodeCanvas value={pageUrl} size={172} marginSize={4} /> : <Skeleton className="h-[172px] w-[172px]" />}
                  </div>
                  <Button type="button" variant="soft" className="w-full" onClick={copyLink}>
                    Sao chép link
                  </Button>
                </GlassPanel>
              </section>
            ) : (
              <GlassPanel strong className="apk-empty-panel">
                <div className="apk-app-icon is-muted" aria-hidden="true">
                  <PackageCheck className="h-8 w-8" />
                </div>
                <h2>Chưa có APK được phát hành</h2>
                <p>Khi có bản APK được phát hành, người dùng sẽ thấy nút tải ở đây.</p>
              </GlassPanel>
            )}
          </>
        ) : (
          <div className="apk-install-placeholder" aria-hidden="true" />
        )}
      </section>
    </div>
  );

  if (standalone) return <div className="apk-public-page scroll-thin">{content}</div>;
  return content;
}

function ApkLoadingState() {
  return (
    <section className="apk-download-grid">
      <GlassPanel strong className="apk-release-panel">
        <div className="apk-release-heading">
          <Skeleton className="h-16 w-16 rounded-2xl" />
          <div className="grid flex-1 gap-3">
            <Skeleton className="h-4 w-32" />
            <Skeleton className="h-8 w-56" />
            <Skeleton className="h-7 w-40" />
          </div>
        </div>
        <Skeleton className="h-28 w-full" />
      </GlassPanel>
      <GlassPanel strong className="apk-qr-panel">
        <Skeleton className="h-8 w-36" />
        <Skeleton className="h-[172px] w-[172px]" />
        <Skeleton className="h-10 w-full" />
      </GlassPanel>
    </section>
  );
}

function AndroidMark() {
  return (
    <svg className="apk-android-mark" viewBox="0 0 64 64" role="img" aria-label="Android">
      <path d="M21.3 12.4 16.8 5a1.7 1.7 0 0 1 2.9-1.8l4.8 7.7a22 22 0 0 1 15 0l4.8-7.7a1.7 1.7 0 0 1 2.9 1.8l-4.5 7.4A18.8 18.8 0 0 1 51 27.3H13a18.8 18.8 0 0 1 8.3-14.9Z" />
      <path d="M13 30h38v21.7A8.3 8.3 0 0 1 42.7 60H21.3a8.3 8.3 0 0 1-8.3-8.3V30Z" />
      <path d="M4 32.8a4.3 4.3 0 0 1 8.6 0v13.6a4.3 4.3 0 0 1-8.6 0V32.8ZM51.4 32.8a4.3 4.3 0 0 1 8.6 0v13.6a4.3 4.3 0 0 1-8.6 0V32.8ZM22.2 60h6.7v-9.2h-6.7V60ZM35.1 60h6.7v-9.2h-6.7V60Z" />
      <circle cx="24.7" cy="20.8" r="2.2" />
      <circle cx="39.3" cy="20.8" r="2.2" />
    </svg>
  );
}
