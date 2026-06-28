import { useEffect, useState } from "react";
import { Droplet, Eye, FilePlus2, Power, ScanFace, ShieldCheck } from "lucide-react";
import { GlassCard } from "../components/Glass";
import { PageHeader } from "../components/Layout";
import { Badge } from "../components/ui";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "../shadcn/tooltip";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { isAdmin, type RtspAttendanceStatus } from "../lib/types";
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

export function SystemSettings() {
  const { user } = useAuth();
  const admin = isAdmin(user);
  const [now, setNow] = useState(() => new Date());
  const [waterEnabled, setWaterEnabled] = useState(() => (user ? isWaterReminderEnabled(user.id) : true));
  const [eyeEnabled, setEyeEnabled] = useState(() => (user ? isEyeReminderEnabled(user.id) : true));
  const [keepCreateVoucherOpen, setKeepCreateVoucherOpen] = useState(() =>
    user ? isKeepCreateVoucherOpenEnabled(user.id) : false,
  );
  const [preferenceError, setPreferenceError] = useState<string | null>(null);
  const [attendanceToggling, setAttendanceToggling] = useState(false);
  const [attendanceError, setAttendanceError] = useState<string | null>(null);
  const {
    data: rtspStatus,
    loading: rtspLoading,
    error: rtspError,
    reload: reloadRtspStatus,
    setData: setRtspStatus,
  } = useApi<RtspAttendanceStatus>(admin ? "/api/chamcong/rtsp/status" : null, [admin]);

  const waterCountdown = user && waterEnabled
    ? formatHHMMSS(countdownToNextReminder(ensureWaterDailyLogin(user.id, now), WATER_INTERVAL_MS, now))
    : formatHHMMSS(WATER_INTERVAL_MS);
  const eyeCountdown = user && eyeEnabled
    ? formatHHMMSS(countdownToNextReminder(ensureEyeDailyLogin(user.id, now), EYE_INTERVAL_MS, now))
    : formatHHMMSS(EYE_INTERVAL_MS);
  const autoAttendanceEnabled = Boolean(rtspStatus?.autoAttendanceEnabled ?? rtspStatus?.enabled);

  useEffect(() => {
    if (!user) return;

    setWaterEnabled(isWaterReminderEnabled(user.id));
    setEyeEnabled(isEyeReminderEnabled(user.id));
    setKeepCreateVoucherOpen(isKeepCreateVoucherOpenEnabled(user.id));
    setPreferenceError(null);

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

    return () => {
      unsubscribeWater();
      unsubscribeEye();
      unsubscribeAccounting();
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

  const toggleAutoAttendance = async () => {
    if (!admin || attendanceToggling || !rtspStatus) return;

    const next = !autoAttendanceEnabled;
    setAttendanceToggling(true);
    setAttendanceError(null);
    try {
      const res = await api.post<{ status: RtspAttendanceStatus }>("/api/chamcong/rtsp/auto-attendance", { enabled: next });
      setRtspStatus(res.status);
      reloadRtspStatus({ silent: true });
    } catch (e) {
      setAttendanceError(e instanceof Error ? e.message : "Không đổi được chế độ chấm công tự động.");
    } finally {
      setAttendanceToggling(false);
    }
  };

  return (
    <div className="system-settings-page gc-root">
      <PageHeader
        title="Hệ thống"
        subtitle="Cài đặt thông báo và tuỳ chọn trải nghiệm web"
      />

      {preferenceError && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-2 text-sm font-semibold text-red-700">
          {preferenceError}
        </div>
      )}

      <section className="system-settings-grid">
        <GlassCard className="system-settings-card p-5">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="flex min-w-0 gap-4">
              <div className="system-settings-icon">
                <Droplet className="h-7 w-7" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-lg font-black text-[var(--text)]">Nhắc nhở uống nước</h2>
                  <Badge color={waterEnabled ? "success" : "muted"}>
                    {waterEnabled ? "Đang bật" : "Đang tắt"}
                  </Badge>
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
              className={`water-toggle ${waterEnabled ? "is-on" : ""}`}
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

        </GlassCard>

        <GlassCard className="system-settings-card p-5">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="flex min-w-0 gap-4">
              <div className="system-settings-icon is-soft">
                <Eye className="h-7 w-7" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-lg font-black text-[var(--text)]">Nhắc bảo vệ mắt 20-20-20</h2>
                  <Badge color={eyeEnabled ? "success" : "muted"}>
                    {eyeEnabled ? "Đang bật" : "Đang tắt"}
                  </Badge>
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
              className={`water-toggle eye-toggle ${eyeEnabled ? "is-on" : ""}`}
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
        </GlassCard>

        <GlassCard className="system-settings-card p-5">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="flex min-w-0 gap-4">
              <div className="system-settings-icon is-accounting">
                <FilePlus2 className="h-7 w-7" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-lg font-black text-[var(--text)]">Giữ form tạo phiếu</h2>
                  <Badge color={keepCreateVoucherOpen ? "success" : "muted"}>
                    {keepCreateVoucherOpen ? "Đang bật" : "Đang tắt"}
                  </Badge>
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
              className={`water-toggle accounting-toggle ${keepCreateVoucherOpen ? "is-on" : ""}`}
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
        </GlassCard>

        {admin && (
          <GlassCard className="system-settings-card p-5">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="flex min-w-0 gap-4">
                <div className="system-settings-icon is-attendance">
                  <ScanFace className="h-7 w-7" />
                </div>
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="text-lg font-black text-[var(--text)]">Chấm công tự động</h2>
                    <Badge color={rtspError || attendanceError ? "warning" : autoAttendanceEnabled ? "success" : "muted"}>
                      {rtspLoading && !rtspStatus
                        ? "Đang tải"
                        : autoAttendanceEnabled
                          ? "Đang bật"
                          : "Đang tắt"}
                    </Badge>
                    <TooltipProvider delayDuration={120}>
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <button className="system-rules-hint" type="button" aria-label="Quy tắc camera IP">
                            <ShieldCheck className="h-4 w-4" />
                            <span>Camera IP</span>
                          </button>
                        </TooltipTrigger>
                        <TooltipContent side="bottom" align="start" className="system-rules-tooltip">
                          Công tắc này điều khiển luồng tự nhận diện và ghi chấm công từ camera IP. Khi tắt, camera vẫn giữ kết nối để admin xem trạng thái và test scan.
                        </TooltipContent>
                      </Tooltip>
                    </TooltipProvider>
                  </div>
                </div>
              </div>

              <button
                className={`water-toggle attendance-toggle ${autoAttendanceEnabled ? "is-on" : ""}`}
                type="button"
                role="switch"
                aria-checked={autoAttendanceEnabled}
                aria-label={`${autoAttendanceEnabled ? "Tắt" : "Bật"} chấm công nhận diện tự động`}
                disabled={attendanceToggling || rtspLoading || !rtspStatus}
                onClick={toggleAutoAttendance}
              >
                <span className="water-toggle-icon">
                  <Power className="h-4 w-4" />
                </span>
                <span className="reminder-toggle-countdown" aria-hidden="true">
                  {attendanceToggling ? "..." : autoAttendanceEnabled ? "AUTO" : "OFF"}
                </span>
                <span className="water-toggle-track">
                  <span className="water-toggle-thumb" />
                </span>
              </button>
            </div>
          </GlassCard>
        )}
      </section>
    </div>
  );
}
