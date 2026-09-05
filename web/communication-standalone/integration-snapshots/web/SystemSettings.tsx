import { useEffect, useState, type FormEvent } from "react";
import {
  Database,
  Droplet,
  Eye,
  FilePlus2,
  Gauge,
  HardDrive,
  Megaphone,
  MessageCircle,
  MessageSquare,
  Power,
  RefreshCw,
  Rocket,
  Settings2,
  ShieldCheck,
  Trash2,
  Upload,
  Users2,
  Banknote,
  Bell,
  FileText,
  Truck,
  UserCheck,
} from "lucide-react";
import { GlassCard } from "../components/Glass";
import { GlassPanel } from "../components/glass/GlassPanel";
import { PageHeader } from "../components/Layout";
import { Table } from "../components/Table";
import { Badge, Button, Field, Input } from "../components/ui";
import { useAppNotifications } from "../components/app-notifications-context";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "../shadcn/tooltip";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { PERM, useAccess } from "../lib/access";
import { dateTime } from "../lib/format";
import { type ChatDbUsage, type Release } from "../lib/types";
import { useApi } from "../lib/useApi";
import {
  isKeepCreateVoucherOpenEnabled,
  subscribeKeepCreateVoucherOpenEnabled,
} from "../lib/accountingPreferences";
import {
  ensureEyeDailyLogin,
  isEyeReminderEnabled,
  restartEyeDailyLogin,
  subscribeEyeReminderEnabled,
} from "../lib/eyeReminderClock";
import {
  ensureWaterDailyLogin,
  isWaterReminderEnabled,
  restartWaterDailyLogin,
  subscribeWaterReminderEnabled,
} from "../lib/waterReminderClock";
import { loadUserPreferences, saveUserPreferencesPatch } from "../lib/userPreferences";
import { isLiteMode, isPerfModeExplicit, setLiteMode, subscribePerfMode } from "../lib/perfMode";
import {
  isMessagePreviewEnabled,
  subscribeMessagePreviewEnabled,
} from "../lib/messagePreviewPreference";
import {
  NOTIFICATION_GROUPS,
  loadNotificationGroups,
  saveNotificationGroup,
  type NotificationGroupState,
} from "../lib/notificationGroups";
import "./system-settings.css";

const WATER_INTERVAL_MS = 60 * 60 * 1000;
const EYE_INTERVAL_MS = 20 * 60 * 1000;

function countdownToNextReminder(firstLoginIso: string, intervalMs: number, now: Date) {
  const firstLoginMs = new Date(firstLoginIso).getTime();
  if (!Number.isFinite(firstLoginMs)) return 0;

  const elapsedMs = Math.max(0, now.getTime() - firstLoginMs);
  const nextIndex = Math.floor(elapsedMs / intervalMs) + 1;
  const nextReminderAt = firstLoginMs + nextIndex * intervalMs;
  return Math.max(0, nextReminderAt - now.getTime());
}

function formatHHMMSS(ms: number) {
  const totalSeconds = Math.max(0, Math.ceil(ms / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  return [hours, minutes, seconds].map((value) => String(value).padStart(2, "0")).join(":");
}

/**
 * Hiệu ứng chào trang: lần đầu mở trang Cài đặt, các công tắc ĐANG BẬT sẽ tự trượt từ tắt sang bật
 * thay vì hiện sẵn ở trạng thái bật. Hook trả về `false` ở nhịp vẽ đầu (công tắc vẽ ở trạng thái
 * tắt) rồi đổi sang `true` để CSS chạy transition. Máy đặt "giảm chuyển động" thì bỏ qua hiệu ứng.
 * Truyền `ready = false` khi trạng thái còn đang tải để hiệu ứng chỉ chạy sau khi có dữ liệu thật.
 */
function useToggleIntro(ready = true) {
  const [played, setPlayed] = useState(
    () => typeof window !== "undefined"
      && typeof window.matchMedia === "function"
      && window.matchMedia("(prefers-reduced-motion: reduce)").matches,
  );

  useEffect(() => {
    if (played || !ready) return;
    const timerId = window.setTimeout(() => setPlayed(true), 240);
    return () => window.clearTimeout(timerId);
  }, [played, ready]);

  return played;
}

/** Lớp cho hàng cài đặt: cái nào tắt thì mờ đi (thay cho tag "Đang bật / Đang tắt" trước đây). */
function rowClass(on: boolean) {
  return `system-settings-row${on ? "" : " is-off"}`;
}

export function SystemSettings() {
  const { user } = useAuth();
  const { can } = useAccess();
  const canReadDatabaseUsage = can(PERM.usersManage);
  const canManageAppConfig = can(PERM.systemSettingsManage);
  const canManageReleases = can(PERM.systemReleasesManage);
  const hasManagementTabs = canReadDatabaseUsage || canManageAppConfig || canManageReleases;
  const [tab, setTab] = useState<"settings" | "db" | "updates">("settings");
  const [now, setNow] = useState(() => new Date());
  const [waterEnabled, setWaterEnabled] = useState(() => (user ? isWaterReminderEnabled(user.id) : true));
  const [eyeEnabled, setEyeEnabled] = useState(() => (user ? isEyeReminderEnabled(user.id) : true));
  const [keepCreateVoucherOpen, setKeepCreateVoucherOpen] = useState(() =>
    user ? isKeepCreateVoucherOpenEnabled(user.id) : false,
  );
  const [messagePreviewEnabled, setMessagePreviewEnabled] = useState(() =>
    user ? isMessagePreviewEnabled(user.id) : true,
  );
  const [preferenceError, setPreferenceError] = useState<string | null>(null);
  const waterCountdown = user && waterEnabled
    ? formatHHMMSS(countdownToNextReminder(ensureWaterDailyLogin(user.id, now), WATER_INTERVAL_MS, now))
    : formatHHMMSS(WATER_INTERVAL_MS);
  const eyeCountdown = user && eyeEnabled
    ? formatHHMMSS(countdownToNextReminder(ensureEyeDailyLogin(user.id, now), EYE_INTERVAL_MS, now))
    : formatHHMMSS(EYE_INTERVAL_MS);
  // Nạp lại tuỳ chọn (nhắc nước / nhắc mắt / giữ form phiếu / xem trước tin nhắn) từ localStorage khi
  // ĐỔI tài khoản. Gán lúc render thay vì trong useEffect: React Compiler cấm setState đồng bộ trong
  // effect, và các giá trị khởi tạo useState bên trên vốn đã đọc đúng localStorage cho lần đầu — nên
  // mốc `prefsSeededFor` chỉ cần bắt đúng lúc `user` đổi (giữ nguyên bộ deps [user] cũ).
  const [prefsSeededFor, setPrefsSeededFor] = useState(user);
  if (user && prefsSeededFor !== user) {
    setPrefsSeededFor(user);
    setWaterEnabled(isWaterReminderEnabled(user.id));
    setEyeEnabled(isEyeReminderEnabled(user.id));
    setKeepCreateVoucherOpen(isKeepCreateVoucherOpenEnabled(user.id));
    setMessagePreviewEnabled(isMessagePreviewEnabled(user.id));
    setPreferenceError(null);
  }

  // Việc CÒN LẠI vẫn đúng là của effect: nạp tuỳ chọn theo tài khoản từ server và đăng ký lắng nghe
  // thay đổi từ nơi khác (đồng bộ với thứ NGOÀI React).
  useEffect(() => {
    if (!user) return;

    loadUserPreferences(user.id).catch(() => {
      setPreferenceError("Không tải được tuỳ chọn đã lưu theo tài khoản.");
    });

    const unsubscribeWater = subscribeWaterReminderEnabled(user.id, () => {
      setWaterEnabled(isWaterReminderEnabled(user.id));
    });
    const unsubscribeEye = subscribeEyeReminderEnabled(user.id, () => {
      setEyeEnabled(isEyeReminderEnabled(user.id));
    });
    const unsubscribeAccounting = subscribeKeepCreateVoucherOpenEnabled(user.id, () => {
      setKeepCreateVoucherOpen(isKeepCreateVoucherOpenEnabled(user.id));
    });
    const unsubscribeMessagePreview = subscribeMessagePreviewEnabled(user.id, () => {
      setMessagePreviewEnabled(isMessagePreviewEnabled(user.id));
    });

    return () => {
      unsubscribeWater();
      unsubscribeEye();
      unsubscribeAccounting();
      unsubscribeMessagePreview();
    };
  }, [user]);

  useEffect(() => {
    const tick = () => setNow(new Date());
    const intervalId = window.setInterval(tick, 1000);
    const onVisible = () => {
      if (document.visibilityState === "visible") tick();
    };

    document.addEventListener("visibilitychange", onVisible);
    return () => {
      window.clearInterval(intervalId);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, []);

  const toggleWaterReminder = async () => {
    if (!user) return;

    const next = !waterEnabled;
    if (next) {
      const restartedAt = new Date();
      restartWaterDailyLogin(user.id, restartedAt);
      setNow(restartedAt);
    }
    setWaterEnabled(next);
    setPreferenceError(null);
    try {
      await saveUserPreferencesPatch(user.id, { waterReminderEnabled: next });
    } catch {
      setWaterEnabled(!next);
      setPreferenceError("Không lưu được tuỳ chọn nhắc uống nước theo tài khoản.");
    }
  };

  const toggleEyeReminder = async () => {
    if (!user) return;

    const next = !eyeEnabled;
    if (next) {
      const restartedAt = new Date();
      restartEyeDailyLogin(user.id, restartedAt);
      setNow(restartedAt);
    }
    setEyeEnabled(next);
    setPreferenceError(null);
    try {
      await saveUserPreferencesPatch(user.id, { eyeReminderEnabled: next });
    } catch {
      setEyeEnabled(!next);
      setPreferenceError("Không lưu được tuỳ chọn nhắc bảo vệ mắt theo tài khoản.");
    }
  };

  const toggleKeepCreateVoucherOpen = async () => {
    if (!user) return;

    const next = !keepCreateVoucherOpen;
    setKeepCreateVoucherOpen(next);
    setPreferenceError(null);
    try {
      await saveUserPreferencesPatch(user.id, { keepCreateVoucherOpen: next });
    } catch {
      setKeepCreateVoucherOpen(!next);
      setPreferenceError("Không lưu được tuỳ chọn giữ form tạo phiếu theo tài khoản.");
    }
  };

  const toggleMessagePreview = async () => {
    if (!user) return;

    const next = !messagePreviewEnabled;
    setMessagePreviewEnabled(next);
    setPreferenceError(null);
    try {
      await saveUserPreferencesPatch(user.id, { messagePreviewEnabled: next });
    } catch {
      setMessagePreviewEnabled(!next);
      setPreferenceError("Không lưu được tuỳ chọn đọc trước tin nhắn theo tài khoản.");
    }
  };

  // Chế độ nhẹ cho máy yếu (lưu theo thiết bị, không đồng bộ tài khoản).
  const [liteMode, setLiteModeState] = useState(isLiteMode());
  useEffect(() => subscribePerfMode(() => setLiteModeState(isLiteMode())), []);
  const liteAuto = !isPerfModeExplicit();

  // Chỉ dùng cho lần vẽ đầu: công tắc bật sẽ trượt từ tắt sang bật cho người dùng thấy.
  const introPlayed = useToggleIntro();
  const onClass = (enabled: boolean) => (enabled && introPlayed ? "is-on" : "");

  return (
    <div className="system-settings-page gc-root">
      <PageHeader
        title="Hệ thống"
        subtitle="Cài đặt thông báo và tuỳ chọn trải nghiệm web"
      />

      {/* Các phần quản trị hiện theo quyền chi tiết do backend cấp. */}
      {hasManagementTabs && (
        <div className="system-segment" data-active={tab} role="tablist">
          <span className="system-segment-thumb" aria-hidden="true" />
          <button
            type="button"
            role="tab"
            aria-selected={tab === "settings"}
            data-on={tab === "settings"}
            className="system-segment-btn"
            onClick={() => setTab("settings")}
          >
            <Settings2 className="h-4 w-4" />
            Hệ thống
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === "db"}
            aria-disabled={!canReadDatabaseUsage}
            disabled={!canReadDatabaseUsage}
            data-on={tab === "db"}
            className="system-segment-btn"
            onClick={() => setTab("db")}
          >
            <Database className="h-4 w-4" />
            Cơ sở dữ liệu
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === "updates"}
            aria-disabled={!canManageAppConfig && !canManageReleases}
            disabled={!canManageAppConfig && !canManageReleases}
            data-on={tab === "updates"}
            className="system-segment-btn"
            onClick={() => setTab("updates")}
          >
            <Rocket className="h-4 w-4" />
            Cập nhật
          </button>
        </div>
      )}

      {canReadDatabaseUsage && tab === "db" ? (
        <ChatDbUsagePanel />
      ) : (canManageAppConfig || canManageReleases) && tab === "updates" ? (
        <div className="space-y-4">
          {canManageAppConfig && <AppConfigPanel />}
          {canManageReleases && <ReleaseUpdatePanel />}
        </div>
      ) : (
      <>
      {preferenceError && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-2 text-sm font-semibold text-red-700">
          {preferenceError}
        </div>
      )}

      <section className="system-settings-shell">
        <GlassCard className="system-settings-card system-settings-list p-5">
          <div className={rowClass(waterEnabled)}>
            <div className="flex min-w-0 gap-4">
              <div className="system-settings-icon">
                <Droplet className="h-5 w-5" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-sm font-black text-[var(--text)]">Nhắc nhở uống nước</h2>
                  <TooltipProvider delayDuration={120}>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <button className="system-rules-hint" type="button" aria-label="Quy tắc hoạt động">
                          <ShieldCheck className="h-4 w-4" />
                          <span>Quy tắc hoạt động</span>
                        </button>
                      </TooltipTrigger>
                      <TooltipContent side="bottom" align="start" className="system-rules-tooltip">
                        Cài đặt này chỉ điều khiển popup nhắc uống nước trên web. Khi tắt, các lần nhắc tới hạn sẽ không hiện.
                        Khi bật lại, đồng hồ bắt đầu lại từ 01:00:00.
                      </TooltipContent>
                    </Tooltip>
                  </TooltipProvider>
                </div>
              </div>
            </div>

            <button
              className={`water-toggle ${onClass(waterEnabled)}`}
              type="button"
              role="switch"
              aria-checked={waterEnabled}
              aria-label={`Bật tắt nhắc uống nước, còn ${waterCountdown} tới lần nhắc tiếp theo`}
              onClick={toggleWaterReminder}
            >
              <span className="water-toggle-icon">
                <Power className="h-4 w-4" />
              </span>
              <span className="reminder-toggle-countdown" aria-hidden="true">
                {waterCountdown}
              </span>
              <span className="water-toggle-track">
                <span className="water-toggle-thumb" />
              </span>
            </button>
          </div>

          <div className={rowClass(eyeEnabled)}>
            <div className="flex min-w-0 gap-4">
              <div className="system-settings-icon is-soft">
                <Eye className="h-5 w-5" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-sm font-black text-[var(--text)]">Nhắc bảo vệ mắt 20-20-20</h2>
                  <TooltipProvider delayDuration={120}>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <button className="system-rules-hint" type="button" aria-label="Quy tắc 20-20-20">
                          <ShieldCheck className="h-4 w-4" />
                          <span>Quy tắc 20-20-20</span>
                        </button>
                      </TooltipTrigger>
                      <TooltipContent side="bottom" align="start" className="system-rules-tooltip">
                        Cài đặt này điều khiển popup nhắc bảo vệ mắt trên web. Khi bật, hệ thống nhắc sau mỗi 20 phút
                        làm việc để bạn nhìn ra xa khoảng 6 mét trong 20 giây. Khi bật lại, đồng hồ bắt đầu lại từ 00:20:00.
                      </TooltipContent>
                    </Tooltip>
                  </TooltipProvider>
                </div>
              </div>
            </div>

            <button
              className={`water-toggle eye-toggle ${onClass(eyeEnabled)}`}
              type="button"
              role="switch"
              aria-checked={eyeEnabled}
              aria-label={`Bật tắt nhắc bảo vệ mắt 20-20-20, còn ${eyeCountdown} tới lần nhắc tiếp theo`}
              onClick={toggleEyeReminder}
            >
              <span className="water-toggle-icon">
                <Power className="h-4 w-4" />
              </span>
              <span className="reminder-toggle-countdown" aria-hidden="true">
                {eyeCountdown}
              </span>
              <span className="water-toggle-track">
                <span className="water-toggle-thumb" />
              </span>
            </button>
          </div>
          <div className={rowClass(keepCreateVoucherOpen)}>
            <div className="flex min-w-0 gap-4">
              <div className="system-settings-icon is-accounting">
                <FilePlus2 className="h-5 w-5" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-sm font-black text-[var(--text)]">Giữ form tạo phiếu</h2>
                  <TooltipProvider delayDuration={120}>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <button className="system-rules-hint" type="button" aria-label="Quy tắc tạo phiếu">
                          <ShieldCheck className="h-4 w-4" />
                          <span>Tạo phiếu</span>
                        </button>
                      </TooltipTrigger>
                      <TooltipContent side="bottom" align="start" className="system-rules-tooltip">
                        Khi bật, sau khi lưu phiếu mới ở trang Kế toán, cửa sổ tạo phiếu không đóng và tự làm mới để nhập phiếu tiếp theo.
                        Khi tắt, hệ thống đóng cửa sổ sau khi lưu như hiện tại.
                      </TooltipContent>
                    </Tooltip>
                  </TooltipProvider>
                </div>
              </div>
            </div>

            <button
              className={`water-toggle accounting-toggle ${onClass(keepCreateVoucherOpen)}`}
              type="button"
              role="switch"
              aria-checked={keepCreateVoucherOpen}
              aria-label={`${keepCreateVoucherOpen ? "Tắt" : "Bật"} giữ form tạo phiếu sau khi lưu`}
              onClick={toggleKeepCreateVoucherOpen}
            >
              <span className="water-toggle-icon">
                <Power className="h-4 w-4" />
              </span>
              <span className="reminder-toggle-countdown" aria-hidden="true">
                {keepCreateVoucherOpen ? "GIỮ" : "ĐÓNG"}
              </span>
              <span className="water-toggle-track">
                <span className="water-toggle-thumb" />
              </span>
            </button>
          </div>
          <div className={rowClass(messagePreviewEnabled)}>
            <div className="flex min-w-0 gap-4">
              <div className="system-settings-icon is-chat">
                <MessageCircle className="h-5 w-5" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-sm font-black text-[var(--text)]">{"\u0110\u1ecdc tr\u01b0\u1edbc tin nh\u1eafn"}</h2>
                  <TooltipProvider delayDuration={120}>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <button className="system-rules-hint" type="button" aria-label="Quy tắc thông báo tin nhắn">
                          <ShieldCheck className="h-4 w-4" />
                          <span>{"Tin nh\u1eafn"}</span>
                        </button>
                      </TooltipTrigger>
                      <TooltipContent side="bottom" align="start" className="system-rules-tooltip">
                        {"Khi b\u1eadt, th\u00f4ng b\u00e1o tin nh\u1eafn m\u1edbi hi\u1ec7n n\u1ed9i dung xem tr\u01b0\u1edbc. Khi t\u1eaft, th\u00f4ng b\u00e1o ch\u1ec9 hi\u1ec7n \"B\u1ea1n c\u00f3 th\u00f4ng b\u00e1o m\u1edbi\"."}
                      </TooltipContent>
                    </Tooltip>
                  </TooltipProvider>
                </div>
              </div>
            </div>

            <button
              className={`water-toggle chat-toggle ${onClass(messagePreviewEnabled)}`}
              type="button"
              role="switch"
              aria-checked={messagePreviewEnabled}
              aria-label={`${messagePreviewEnabled ? "T\u1eaft" : "B\u1eadt"} \u0111\u1ecdc tr\u01b0\u1edbc n\u1ed9i dung tin nh\u1eafn trong th\u00f4ng b\u00e1o`}
              onClick={toggleMessagePreview}
            >
              <span className="water-toggle-icon">
                <Power className="h-4 w-4" />
              </span>
              <span className="reminder-toggle-countdown" aria-hidden="true">
                {messagePreviewEnabled ? "XEM" : "\u1ea8N"}
              </span>
              <span className="water-toggle-track">
                <span className="water-toggle-thumb" />
              </span>
            </button>
          </div>

          <div className={rowClass(liteMode)}>
            <div className="flex min-w-0 gap-4">
              <div className="system-settings-icon is-perf">
                <Gauge className="h-5 w-5" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-sm font-black text-[var(--text)]">Chế độ nhẹ (máy yếu)</h2>
                  {/* Chỉ còn nhãn "tự động" — trạng thái bật/tắt đã thấy ngay ở công tắc và độ mờ của hàng. */}
                  {liteAuto && <Badge color="muted">Tự động</Badge>}
                  <TooltipProvider delayDuration={120}>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <button className="system-rules-hint" type="button" aria-label="Quy tắc chế độ nhẹ">
                          <ShieldCheck className="h-4 w-4" />
                          <span>Hiệu năng</span>
                        </button>
                      </TooltipTrigger>
                      <TooltipContent side="bottom" align="start" className="system-rules-tooltip">
                        Khi bật, giao diện bỏ hiệu ứng kính mờ (blur) và các chuyển động nền trang trí để chạy
                        mượt hơn trên máy cấu hình yếu; nền các thẻ chuyển sang màu đục. Mặc định hệ thống tự bật
                        cho máy ít RAM/CPU; bạn có thể ép bật/tắt tại đây (lưu riêng cho thiết bị này).
                      </TooltipContent>
                    </Tooltip>
                  </TooltipProvider>
                </div>
              </div>
            </div>

            <button
              className={`water-toggle perf-toggle ${onClass(liteMode)}`}
              type="button"
              role="switch"
              aria-checked={liteMode}
              aria-label={`${liteMode ? "Tắt" : "Bật"} chế độ nhẹ cho máy yếu`}
              onClick={() => setLiteMode(!liteMode)}
            >
              <span className="water-toggle-icon">
                <Power className="h-4 w-4" />
              </span>
              <span className="reminder-toggle-countdown" aria-hidden="true">
                {liteMode ? "NHẸ" : "ĐẦY ĐỦ"}
              </span>
              <span className="water-toggle-track">
                <span className="water-toggle-thumb" />
              </span>
            </button>
          </div>
        </GlassCard>

        <NotificationGroupsCard />
      </section>
      </>
      )}
    </div>
  );
}

const GROUP_ICON: Record<string, typeof Bell> = {
  delivery: Truck,
  collection: Banknote,
  accounting: FileText,
  work: FilePlus2,
  people: UserCheck,
};

/**
 * Bật/tắt từng nhóm thông báo. Lưu ở MÁY CHỦ (không phải localStorage) vì chính máy chủ dùng nó để
 * quyết định có ghi thông báo và có bắn push hay không — nhờ vậy tắt trên web thì điện thoại cũng im.
 */
function NotificationGroupsCard() {
  const { notify } = useAppNotifications();
  const [groups, setGroups] = useState<NotificationGroupState | null>(null);
  const [saving, setSaving] = useState<string | null>(null);
  // Đợi có trạng thái thật rồi mới chạy hiệu ứng trượt, tránh bật lên theo giá trị mặc định.
  const introPlayed = useToggleIntro(groups !== null);

  useEffect(() => {
    let alive = true;
    loadNotificationGroups()
      .then((next) => { if (alive) setGroups(next); })
      // Không đọc được thì coi như bật hết: đó cũng là mặc định của máy chủ, và hiện một bảng
      // "tắt hết" trong khi thật ra đang bật sẽ khiến người dùng tưởng mình đã tắt.
      .catch(() => { if (alive) setGroups(Object.fromEntries(NOTIFICATION_GROUPS.map((g) => [g.key, true]))); });
    return () => { alive = false; };
  }, []);

  const toggle = async (key: string) => {
    if (!groups || saving) return;
    const next = !groups[key];
    setSaving(key);
    setGroups({ ...groups, [key]: next });
    try {
      setGroups(await saveNotificationGroup(key, next));
    } catch {
      setGroups({ ...groups, [key]: !next });
      notify.error("Không lưu được tuỳ chọn thông báo. Vui lòng thử lại.");
    } finally {
      setSaving(null);
    }
  };

  return (
    <GlassCard className="system-settings-card system-settings-list p-5">
      <div className="system-settings-row">
        <div className="flex min-w-0 gap-4">
          <div className="system-settings-icon is-chat">
            <Bell className="h-5 w-5" />
          </div>
          <div className="min-w-0">
            <h2 className="text-sm font-black text-[var(--text)]">Nhóm thông báo</h2>
          </div>
        </div>
      </div>

      {NOTIFICATION_GROUPS.map((group) => {
        const Icon = GROUP_ICON[group.key] ?? Bell;
        const on = groups?.[group.key] ?? true;
        return (
          <div className={rowClass(on)} key={group.key}>
            <div className="flex min-w-0 gap-4">
              <div className="system-settings-icon is-soft">
                <Icon className="h-5 w-5" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-sm font-black text-[var(--text)]">{group.label}</h2>
                </div>
                <p className="mt-1 text-xs font-semibold text-[var(--text-muted)]">{group.hint}</p>
              </div>
            </div>

            <button
              className={`water-toggle chat-toggle ${on && introPlayed ? "is-on" : ""}`}
              type="button"
              role="switch"
              aria-checked={on}
              disabled={!groups || saving === group.key}
              aria-label={`${on ? "Tắt" : "Bật"} thông báo nhóm ${group.label}`}
              onClick={() => void toggle(group.key)}
            >
              <span className="water-toggle-icon">
                <Power className="h-4 w-4" />
              </span>
              <span className="reminder-toggle-countdown" aria-hidden="true">
                {on ? "BẬT" : "TẮT"}
              </span>
              <span className="water-toggle-track">
                <span className="water-toggle-thumb" />
              </span>
            </button>
          </div>
        );
      })}
    </GlassCard>
  );
}

type AppConfig = {
  announcement: string;
  announcementLevel: string;
  faceEnrollBannerEnabled: boolean;
  foregroundPollSeconds: number;
  portraitHeightFactor: number;
  portraitVerticalNudge: number;
  portraitAspect: number;
  portraitMinWidthFactor: number;
  notices: string[];
};

/**
 * Cấu hình ứng dụng điều khiển từ xa (remote config): người có quyền đổi mà KHÔNG cần phát hành APK mới.
 * App đọc lúc đăng nhập + khi quay lại foreground rồi áp dụng: thông báo chạy chữ, bật/tắt banner
 * nhắc đăng ký khuôn mặt, nhịp tự làm mới nền.
 */
function AppConfigPanel() {
  const { data, loading, reload } = useApi<AppConfig>("/api/app-config");
  const { notify } = useAppNotifications();
  const [announcement, setAnnouncement] = useState("");
  const [level, setLevel] = useState("info");
  const [notices, setNotices] = useState("");
  const [faceBanner, setFaceBanner] = useState(true);
  const [pollSeconds, setPollSeconds] = useState("20");
  const [portraitHeight, setPortraitHeight] = useState("1.85");
  const [portraitNudge, setPortraitNudge] = useState("0.15");
  const [portraitAspect, setPortraitAspect] = useState("0.75");
  const [portraitMinWidth, setPortraitMinWidth] = useState("1.35");
  const [saving, setSaving] = useState(false);

  // Nạp cấu hình đã lưu vào form. Gán lúc render thay vì trong useEffect: bỏ được khung hình trung
  // gian hiện form rỗng trước khi dữ liệu về, và React Compiler cấm setState đồng bộ trong effect.
  // Mốc `seededData` giữ ĐÚNG bộ deps cũ [data]: nạp lại mỗi lần dữ liệu được tải lại (kể cả sau khi
  // lưu), chứ không phải mỗi lần render.
  const [seededData, setSeededData] = useState<AppConfig | null>(null);
  if (data && seededData !== data) {
    setSeededData(data);
    setAnnouncement(data.announcement);
    setLevel(data.announcementLevel || "info");
    setNotices((data.notices || []).join("\n"));
    setFaceBanner(data.faceEnrollBannerEnabled);
    setPollSeconds(String(data.foregroundPollSeconds || 20));
    setPortraitHeight(String(data.portraitHeightFactor ?? 1.85));
    setPortraitNudge(String(data.portraitVerticalNudge ?? 0.15));
    setPortraitAspect(String(data.portraitAspect ?? 0.75));
    setPortraitMinWidth(String(data.portraitMinWidthFactor ?? 1.35));
  }

  const save = async (event: FormEvent) => {
    event.preventDefault();
    const secs = Number(pollSeconds);
    if (!Number.isInteger(secs) || secs < 5 || secs > 3600) {
      notify.warning("Nhịp làm mới phải là số nguyên trong khoảng 5–3600 giây.");
      return;
    }
    const nums = {
      portraitHeightFactor: Number(portraitHeight),
      portraitVerticalNudge: Number(portraitNudge),
      portraitAspect: Number(portraitAspect),
      portraitMinWidthFactor: Number(portraitMinWidth),
    };
    if (Object.values(nums).some((n) => !Number.isFinite(n))) {
      notify.warning("Các thông số cắt ảnh chân dung phải là số.");
      return;
    }
    setSaving(true);
    try {
      await api.put<AppConfig>("/api/app-config", {
        announcement: announcement.trim(),
        announcementLevel: level,
        notices: notices
          .split("\n")
          .map((s) => s.trim())
          .filter(Boolean),
        faceEnrollBannerEnabled: faceBanner,
        foregroundPollSeconds: secs,
        ...nums,
      });
      notify.success("Đã lưu cấu hình ứng dụng. App sẽ áp dụng ở lần mở/đăng nhập kế tiếp.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được cấu hình.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <GlassPanel strong className="rounded-[20px] p-4">
      <div className="mb-3 flex items-center gap-2">
        <Megaphone className="h-5 w-5 text-[var(--accent)]" />
        <h2 className="text-sm font-black text-[var(--text)]">Cấu hình ứng dụng (điều khiển từ xa)</h2>
      </div>
      <p className="mb-4 text-xs font-semibold text-[var(--text-muted)]">
        Đổi các mục dưới đây để áp dụng cho app di động mà không cần phát hành APK mới.
      </p>
      <form onSubmit={save} className="grid gap-3 lg:grid-cols-2">
        <Field label="Thông báo chạy trong app (để trống = ẩn)">
          <Input
            value={announcement}
            onChange={(e) => setAnnouncement(e.target.value)}
            placeholder="VD: Bảo trì hệ thống 22h tối nay"
          />
        </Field>
        <Field label="Mức độ thông báo">
          <select
            value={level}
            onChange={(e) => setLevel(e.target.value)}
            className="h-10 w-full rounded-xl border border-[var(--glass-border)] bg-white/55 px-3 text-sm text-[var(--text)] outline-none focus:border-[var(--accent)] dark:bg-white/5"
          >
            <option value="info">Thông tin (xanh)</option>
            <option value="warning">Cảnh báo (vàng)</option>
            <option value="critical">Quan trọng (đỏ)</option>
          </select>
        </Field>
        <div className="lg:col-span-2">
          <Field label="Lời nhắc chạy chữ trên Trang chủ app (mỗi dòng một lời nhắc)">
            <textarea
              value={notices}
              onChange={(e) => setNotices(e.target.value)}
              rows={4}
              className="km-form-control w-full rounded-xl border px-3.5 py-2.5 text-sm outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
              placeholder={"VD:\nNhớ nộp báo cáo tuần trước 17h thứ Sáu\nGiữ gìn vệ sinh nơi làm việc"}
            />
          </Field>
          <p className="mt-1 text-xs font-semibold text-[var(--text-muted)]">
            Các lời nhắc này luân phiên cùng dải gõ chữ với lời chào theo buổi và nhắc chấm công. Để trống = chỉ còn lời nhắc gắn sẵn của app.
          </p>
        </div>
        <Field label="Nhịp tự làm mới khi mở app (giây)">
          <Input
            value={pollSeconds}
            onChange={(e) => setPollSeconds(e.target.value.replace(/[^\d]/g, ""))}
            inputMode="numeric"
            placeholder="20"
          />
        </Field>
        <label className="flex items-center gap-2 self-end text-sm font-semibold text-[var(--text-secondary)]">
          <input type="checkbox" checked={faceBanner} onChange={(e) => setFaceBanner(e.target.checked)} />
          Hiện banner nhắc đăng ký khuôn mặt
        </label>

        <div className="lg:col-span-2 mt-1 border-t border-black/5 pt-3 dark:border-white/10">
          <p className="text-xs font-bold text-[var(--text-secondary)]">Ảnh chân dung trên app (cách cắt — kiểu ảnh thẻ)</p>
          <p className="mt-1 text-xs font-semibold text-[var(--text-muted)]">
            Chỉnh mấy số này để đổi khung cắt mà không cần phát hành APK mới. Áp dụng cho lần chụp kế tiếp trên app.
          </p>
        </div>
        <Field label="Chiều cao khung (× cao mặt) — lớn = lấy rộng hơn">
          <Input value={portraitHeight} onChange={(e) => setPortraitHeight(e.target.value)} inputMode="decimal" placeholder="1.85" />
        </Field>
        <Field label="Nhích khung lên (× cao mặt) — dương = lấy nhiều đỉnh đầu">
          <Input value={portraitNudge} onChange={(e) => setPortraitNudge(e.target.value)} inputMode="decimal" placeholder="0.15" />
        </Field>
        <Field label="Tỉ lệ ngang/dọc (0.75 = 3:4)">
          <Input value={portraitAspect} onChange={(e) => setPortraitAspect(e.target.value)} inputMode="decimal" placeholder="0.75" />
        </Field>
        <Field label="Bề rộng tối thiểu (× rộng mặt)">
          <Input value={portraitMinWidth} onChange={(e) => setPortraitMinWidth(e.target.value)} inputMode="decimal" placeholder="1.35" />
        </Field>

        <div className="lg:col-span-2">
          <Button type="submit" loading={saving} disabled={loading}>
            <Settings2 className="h-4 w-4" />
            Lưu cấu hình
          </Button>
        </div>
      </form>
    </GlassPanel>
  );
}

function ReleaseUpdatePanel() {
  const { data, loading, error, reload } = useApi<Release[]>("/api/releases/");
  const { confirm, notify } = useAppNotifications();
  const [version, setVersion] = useState("1.0");
  const [versionCode, setVersionCode] = useState("1");
  const [versionCodeTouched, setVersionCodeTouched] = useState(false);
  const [releaseNotes, setReleaseNotes] = useState("");
  const [isMandatory, setIsMandatory] = useState(true);
  const [isPublished, setIsPublished] = useState(true);
  const [apk, setApk] = useState<File | null>(null);
  const [saving, setSaving] = useState(false);
  const [selected, setSelected] = useState<Set<number>>(new Set());

  const rows = data ?? [];
  const allSelected = rows.length > 0 && rows.every((r) => selected.has(r.id));
  const toggleOne = (id: number) =>
    setSelected((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  const toggleAll = () => setSelected(allSelected ? new Set() : new Set(rows.map((r) => r.id)));

  // Mốc chuẩn: app chỉ nhận cập nhật khi mã bản MỚI > mã bản đã phát hành cao nhất.
  const highestPublishedVersionCode = Math.max(
    0,
    ...(data ?? []).filter((r) => r.isPublished).map((r) => r.versionCode || 0),
  );
  const highestVersionCode = Math.max(0, ...(data ?? []).map((r) => r.versionCode || 0));
  const codeNum = Number(versionCode);
  const codeTooLow = isPublished && (!Number.isInteger(codeNum) || codeNum <= highestPublishedVersionCode);

  // Gợi ý mã phiên bản kế tiếp = mốc cao nhất + 1. Gán lúc render thay vì trong useEffect: effect làm
  // ô mã hiện số cũ thêm một khung hình sau khi danh sách bản phát hành đã về (dễ đọc nhầm số), và
  // React Compiler cấm setState đồng bộ trong effect. Mốc `codeSeed` giữ ĐÚNG bộ deps cũ — gồm cả
  // `versionCodeTouched`, nên vẫn không bao giờ đè lên số người dùng tự sửa.
  const suggestedVersionCode = Math.max(highestVersionCode, highestPublishedVersionCode) + 1;
  const versionCodeSeedKey = `${suggestedVersionCode}|${versionCodeTouched}`;
  const [codeSeed, setCodeSeed] = useState<string | null>(null);
  if (!versionCodeTouched && codeSeed !== versionCodeSeedKey) {
    setCodeSeed(versionCodeSeedKey);
    setVersionCode(String(suggestedVersionCode));
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!apk) {
      notify.warning("Vui lòng chọn file APK trước khi phát hành.");
      return;
    }

    if (!Number.isInteger(codeNum) || codeNum <= 0) {
      notify.warning("Mã bản phải là số nguyên dương.");
      return;
    }
    if (isPublished && codeNum <= highestPublishedVersionCode) {
      notify.warning(
        `Mã bản (${codeNum}) phải lớn hơn ${highestPublishedVersionCode} thì app mới nhận được cập nhật. ` +
          `Hãy đặt "Mã bản" đúng bằng versionCode trong build.gradle của APK vừa build (phải > ${highestPublishedVersionCode}).`,
        "Mã bản chưa đủ lớn",
      );
      return;
    }

    const form = new FormData();
    form.append("appTarget", "hr-apk");
    form.append("version", version);
    form.append("versionCode", versionCode);
    form.append("releaseNotes", releaseNotes);
    form.append("isMandatory", String(isMandatory));
    form.append("isPublished", String(isPublished));
    form.append("apk", apk);

    setSaving(true);
    try {
      await api.postForm<Release>("/api/releases/", form);
      notify.success(isPublished ? "Đã phát hành bản APK mới." : "Đã lưu bản APK nháp.");
      setApk(null);
      setReleaseNotes("");
      setVersionCodeTouched(false);
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không đăng được bản cập nhật.");
    } finally {
      setSaving(false);
    }
  };

  const publish = async (release: Release, mandatory: boolean) => {
    try {
      await api.post(`/api/releases/${release.id}/publish`, { isPublished: true, isMandatory: mandatory });
      notify.success(mandatory ? "Đã yêu cầu tất cả app cập nhật." : "Đã phát hành bản cập nhật.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không phát hành được bản cập nhật.");
    }
  };

  const unpublish = async (release: Release) => {
    try {
      await api.post(`/api/releases/${release.id}/publish`, { isPublished: false });
      notify.info("Đã gỡ phát hành bản cập nhật.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không gỡ phát hành được.");
    }
  };

  const remove = async (release: Release) => {
    const ok = await confirm({
      title: `Xóa bản ${release.version}?`,
      description: "File APK lưu trong DB của bản này cũng sẽ bị xóa.",
      confirmLabel: "Xóa",
      tone: "danger",
    });
    if (!ok) return;

    try {
      await api.del(`/api/releases/${release.id}`);
      notify.success("Đã xóa bản phát hành.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được bản phát hành.");
    }
  };

  // Gỡ HÀNG LOẠT các bản đã tích chọn trong một lần (thay vì xóa từng cái).
  const removeSelected = async () => {
    const ids = [...selected];
    if (ids.length === 0) return;
    const ok = await confirm({
      title: `Gỡ ${ids.length} bản đã chọn?`,
      description: "File APK lưu trong DB của các bản này cũng sẽ bị xóa vĩnh viễn.",
      confirmLabel: "Gỡ hàng loạt",
      tone: "danger",
    });
    if (!ok) return;

    try {
      await api.post("/api/releases/bulk-delete", { ids });
      notify.success(`Đã xóa ${ids.length} bản phát hành.`);
      setSelected(new Set());
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được các bản đã chọn.");
    }
  };

  return (
    <section className="space-y-4">
      <GlassPanel strong className="rounded-[20px] p-4">
        <form onSubmit={submit} className="grid gap-3 lg:grid-cols-[1fr_120px_1.4fr_auto] lg:items-end">
          <Field label="Phiên bản">
            <Input value={version} onChange={(e) => setVersion(e.target.value)} placeholder="1.0" required />
          </Field>
          <Field label="Mã bản">
            <Input
              value={versionCode}
              onChange={(e) => {
                setVersionCode(e.target.value);
                setVersionCodeTouched(true);
              }}
              inputMode="numeric"
              required
            />
          </Field>
          <Field label="Ghi chú">
            <Input value={releaseNotes} onChange={(e) => setReleaseNotes(e.target.value)} placeholder="Nội dung thay đổi" />
          </Field>
          <div className="flex flex-wrap items-center gap-3">
            <label className="inline-flex items-center gap-2 text-sm font-semibold text-[var(--text-secondary)]">
              <input type="checkbox" checked={isMandatory} onChange={(e) => setIsMandatory(e.target.checked)} />
              Bắt buộc
            </label>
            <label className="inline-flex items-center gap-2 text-sm font-semibold text-[var(--text-secondary)]">
              <input type="checkbox" checked={isPublished} onChange={(e) => setIsPublished(e.target.checked)} />
              Phát hành
            </label>
          </div>
          <Field label="File APK">
            <Input type="file" accept=".apk,application/vnd.android.package-archive" onChange={(e) => setApk(e.target.files?.[0] ?? null)} required />
          </Field>
          <div className="text-xs font-semibold text-[var(--text-muted)] lg:col-span-2">
            {apk ? `${apk.name} · ${fmtBytes(apk.size)}` : "APK sẽ được lưu trực tiếp trong PostgreSQL."}
          </div>
          <div className="text-xs font-semibold text-[var(--text-muted)] lg:col-span-4">
            Bản đã phát hành cao nhất: mã <b>{highestPublishedVersionCode || "—"}</b> ·{" "}
            <span className={codeTooLow ? "text-[var(--danger)]" : undefined}>
              Mã bản mới phải lớn hơn {highestPublishedVersionCode} (khớp versionCode trong build.gradle của APK)
            </span>
          </div>
          <Button type="submit" loading={saving} className="lg:col-start-4">
            <Upload className="h-4 w-4" />
            Đăng bản APK
          </Button>
        </form>
      </GlassPanel>

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        {error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <>
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-black/5 px-4 py-3 dark:border-white/10">
              <label className="inline-flex items-center gap-2 text-sm font-semibold text-[var(--text-secondary)]">
                <input type="checkbox" checked={allSelected} disabled={rows.length === 0} onChange={toggleAll} />
                {selected.size > 0 ? `Đã chọn ${selected.size} bản` : "Chọn tất cả"}
              </label>
              {selected.size > 0 && (
                <Button type="button" variant="danger" className="px-3 py-2" onClick={removeSelected}>
                  <Trash2 className="h-4 w-4" />
                  Gỡ {selected.size} bản đã chọn
                </Button>
              )}
            </div>
            <Table<Release>
              loading={loading}
              rows={rows}
              keyOf={(r) => r.id}
              empty="Chưa có bản phát hành"
              columns={[
              {
                header: "",
                cell: (r) => (
                  <input
                    type="checkbox"
                    checked={selected.has(r.id)}
                    onChange={() => toggleOne(r.id)}
                    aria-label={`Chọn bản ${r.version}`}
                  />
                ),
              },
              { header: "Ứng dụng", cell: (r) => <span className="font-semibold">{r.appTarget}</span> },
              { header: "Phiên bản", cell: (r) => <span className="font-semibold">{r.version}</span> },
              { header: "Mã", align: "right", cell: (r) => r.versionCode },
              { header: "Bắt buộc", cell: (r) => (r.isMandatory ? <Badge color="danger">Bắt buộc</Badge> : <Badge color="muted">Tùy chọn</Badge>) },
              { header: "Trạng thái", cell: (r) => (r.isPublished ? <Badge color="success">Đã phát hành</Badge> : <Badge color="muted">Nháp</Badge>) },
              { header: "APK", cell: (r) => <span className="text-[var(--text-secondary)]">{r.apkFileName || "—"}{r.apkSize ? ` · ${fmtBytes(r.apkSize)}` : ""}</span> },
              { header: "Ngày", cell: (r) => dateTime(r.publishedAt) },
              { header: "Người phát hành", cell: (r) => r.publishedBy || "—" },
              { header: "Ghi chú", cell: (r) => <span className="text-[var(--text-secondary)]">{r.releaseNotes}</span> },
              {
                header: "Thao tác",
                align: "right",
                cell: (r) => (
                  <div className="flex flex-wrap justify-end gap-2">
                    <Button type="button" variant="soft" className="px-3 py-2" onClick={() => publish(r, true)}>
                      <Rocket className="h-4 w-4" />
                      Yêu cầu
                    </Button>
                    {r.isPublished ? (
                      <Button type="button" variant="ghost" className="px-3 py-2" onClick={() => unpublish(r)}>
                        Gỡ
                      </Button>
                    ) : (
                      <Button type="button" variant="ghost" className="px-3 py-2" onClick={() => publish(r, r.isMandatory)}>
                        Phát hành
                      </Button>
                    )}
                    <Button type="button" variant="danger" className="px-3 py-2" onClick={() => remove(r)} aria-label="Xóa bản phát hành">
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                ),
              },
            ]}
            />
          </>
        )}
      </GlassPanel>
    </section>
  );
}

/** Định dạng KB → đơn vị đọc được (KB/MB/GB). */
function fmtSize(kb: number): string {
  if (!kb || kb < 0) return "0 KB";
  if (kb < 1024) return `${kb.toLocaleString("vi-VN")} KB`;
  const mb = kb / 1024;
  if (mb < 1024) return `${mb.toLocaleString("vi-VN", { maximumFractionDigits: 1 })} MB`;
  return `${(mb / 1024).toLocaleString("vi-VN", { maximumFractionDigits: 2 })} GB`;
}

function fmtBytes(value: number): string {
  if (!value) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  let size = value;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit += 1;
  }
  return `${size.toLocaleString("vi-VN", { maximumFractionDigits: unit === 0 ? 0 : 1 })} ${units[unit]}`;
}

/** Tab "Cơ sở dữ liệu": dung lượng mục Trò chuyện trong DB (người có quyền quản lý người dùng). */
function ChatDbUsagePanel() {
  const { data, loading, error, reload } = useApi<ChatDbUsage>("/api/chat/db-usage");

  const ratio =
    data && data.databaseTotalKb > 0
      ? Math.min(100, (data.totalKb / data.databaseTotalKb) * 100)
      : 0;

  return (
    <section className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm font-semibold text-[var(--text-secondary)]">
          Dung lượng dữ liệu mục Trò chuyện đang chiếm trong cơ sở dữ liệu.
        </p>
        <button
          type="button"
          onClick={() => reload()}
          className="inline-flex items-center gap-2 rounded-full border border-[var(--glass-border)] bg-[var(--glass-bg-strong)] px-3.5 py-1.5 text-xs font-bold text-[var(--text-secondary)] transition hover:text-[var(--accent)]"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} />
          Làm mới
        </button>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-2 text-sm font-semibold text-red-700">
          Không tải được dung lượng DB: {error}
        </div>
      )}

      <div className="system-db-grid">
        <GlassCard className="system-settings-card system-db-span2 p-5">
          <div className="flex items-start gap-4">
            <div className="system-settings-icon">
              <HardDrive className="h-7 w-7" />
            </div>
            <div className="min-w-0 flex-1">
              <div className="db-stat">
                <span className="db-stat-label">Tổng dung lượng mục Trò chuyện</span>
                <span className="db-stat-value">{data ? fmtSize(data.totalKb) : "—"}</span>
                <span className="db-stat-sub">
                  Dữ liệu {data ? fmtSize(data.dataKb) : "—"} · Chỉ mục {data ? fmtSize(data.indexKb) : "—"}
                </span>
              </div>
              {data && data.databaseTotalKb > 0 && (
                <div className="mt-4">
                  <div className="mb-1.5 flex items-center justify-between text-xs font-semibold text-[var(--text-secondary)]">
                    <span>Tỉ lệ trong toàn DB</span>
                    <span>
                      {ratio.toLocaleString("vi-VN", { maximumFractionDigits: 2 })}% · tổng {fmtSize(data.databaseTotalKb)}
                    </span>
                  </div>
                  <div className="db-bar">
                    <span className="db-bar-fill" style={{ width: `${Math.max(ratio, 0.5)}%` }} />
                  </div>
                </div>
              )}
            </div>
          </div>
        </GlassCard>

        <GlassCard className="system-settings-card p-5">
          <div className="db-stat">
            <span className="db-stat-label inline-flex items-center gap-1.5">
              <MessageSquare className="h-4 w-4" /> Số tin nhắn
            </span>
            <span className="db-stat-value">{data ? data.messageCount.toLocaleString("vi-VN") : "—"}</span>
            <span className="db-stat-sub inline-flex items-center gap-1.5">
              <Users2 className="h-3.5 w-3.5" />
              {data ? data.conversationCount.toLocaleString("vi-VN") : "—"} cuộc trò chuyện
            </span>
          </div>
        </GlassCard>
      </div>

      <GlassCard className="system-settings-card p-5">
        <h2 className="mb-3 text-base font-black text-[var(--text)]">Chi tiết theo bảng</h2>
        <div className="overflow-x-auto">
          <table className="db-table">
            <thead>
              <tr>
                <th>Bảng</th>
                <th className="db-num">Số dòng</th>
                <th className="db-num">Dữ liệu</th>
                <th className="db-num">Chỉ mục</th>
                <th className="db-num">Tổng</th>
              </tr>
            </thead>
            <tbody>
              {(data?.tables ?? []).map((t) => (
                <tr key={t.table}>
                  <td>
                    <span className="font-semibold">{t.label}</span>
                    <span className="ml-2 text-xs text-[var(--text-muted)]">{t.table}</span>
                  </td>
                  <td className="db-num">{t.rows.toLocaleString("vi-VN")}</td>
                  <td className="db-num">{fmtSize(t.dataKb)}</td>
                  <td className="db-num">{fmtSize(t.indexKb)}</td>
                  <td className="db-num font-bold">{fmtSize(t.totalKb)}</td>
                </tr>
              ))}
              {!loading && (data?.tables?.length ?? 0) === 0 && (
                <tr>
                  <td colSpan={5} className="py-6 text-center text-[var(--text-muted)]">
                    Chưa có dữ liệu.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </GlassCard>
    </section>
  );
}
