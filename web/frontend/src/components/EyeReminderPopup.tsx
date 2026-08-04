import { AnimatePresence, motion } from "framer-motion";
import { BellRing, CheckCircle2, Clock3, Eye, TimerReset, X } from "lucide-react";
import { useCallback, useEffect, useState, useSyncExternalStore } from "react";
import { useGlow } from "./useGlow";
import type { User } from "../lib/types";
import {
  ensureEyeDailyLogin,
  eyeReminderDayKey,
  isEyeReminderEnabled,
  subscribeEyeReminderEnabled,
} from "../lib/eyeReminderClock";
import "./water-reminder-popup.css";
import "./eye-reminder-popup.css";

const EYE_INTERVAL_MS = 20 * 60 * 1000;
const SNOOZE_MS = 5 * 60 * 1000;
const REST_SECONDS = 20;

interface EyeRestLog {
  time: string;
  durationSeconds: number;
}

interface EyeReminderDayState {
  completed: Record<string, true>;
  dismissed: Record<string, true>;
  snoozedUntil: Record<string, string>;
  rests: EyeRestLog[];
}

const emptyDayState = (): EyeReminderDayState => ({
  completed: {},
  dismissed: {},
  snoozedUntil: {},
  rests: [],
});

const stateKey = (userId: string, dayKey: string) => `aqualife:eye-day-state:${userId}:${dayKey}`;

function loadDayState(userId: string, dayKey: string) {
  const raw = localStorage.getItem(stateKey(userId, dayKey));
  if (!raw) return emptyDayState();

  try {
    const parsed = JSON.parse(raw) as Partial<EyeReminderDayState>;
    return {
      completed: parsed.completed ?? {},
      dismissed: parsed.dismissed ?? {},
      snoozedUntil: parsed.snoozedUntil ?? {},
      rests: Array.isArray(parsed.rests) ? parsed.rests : [],
    };
  } catch {
    return emptyDayState();
  }
}

function saveDayState(userId: string, dayKey: string, value: EyeReminderDayState) {
  localStorage.setItem(stateKey(userId, dayKey), JSON.stringify(value));
}

function formatTime(date: Date) {
  return date.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" });
}

function formatCountdown(ms: number) {
  const safeMs = Math.max(0, ms);
  const minutes = Math.ceil(safeMs / 60_000);
  if (minutes < 60) return `${minutes} phút`;

  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest ? `${hours} giờ ${rest} phút` : `${hours} giờ`;
}

function EyeReminderArtwork() {
  return (
    <svg className="eye-reminder-art" viewBox="0 0 268 172" role="img" aria-label="Bảo vệ mắt">
      <defs>
        <radialGradient id="er-iris" cx="0" cy="0" r="1" gradientTransform="matrix(25 0 0 25 134 88)" gradientUnits="userSpaceOnUse">
          <stop stopColor="#dffdf7" />
          <stop offset="0.48" stopColor="#129887" />
          <stop offset="1" stopColor="#3457d5" />
        </radialGradient>
        <linearGradient id="er-lid" x1="42" x2="226" y1="52" y2="122" gradientUnits="userSpaceOnUse">
          <stop stopColor="#ffffff" stopOpacity="0.9" />
          <stop offset="0.48" stopColor="#d7fff2" stopOpacity="0.72" />
          <stop offset="1" stopColor="#b8d8ff" stopOpacity="0.62" />
        </linearGradient>
        <linearGradient id="er-horizon" x1="54" x2="214" y1="142" y2="142" gradientUnits="userSpaceOnUse">
          <stop stopColor="#129887" stopOpacity="0.18" />
          <stop offset="0.5" stopColor="#3457d5" stopOpacity="0.3" />
          <stop offset="1" stopColor="#129887" stopOpacity="0.18" />
        </linearGradient>
      </defs>

      <g className="eye-reminder-rays">
        <path d="M55 48L37 34" />
        <path d="M134 32V11" />
        <path d="M213 48L232 34" />
        <path d="M68 128L48 146" />
        <path d="M200 128L220 146" />
      </g>

      <path className="eye-reminder-horizon" d="M52 139C86 125 111 126 134 139C157 152 183 153 216 139" />
      <path className="eye-reminder-lid" d="M33 88C57 49 91 34 134 34C177 34 211 49 235 88C211 127 177 142 134 142C91 142 57 127 33 88Z" />
      <circle className="eye-reminder-iris" cx="134" cy="88" r="31" />
      <circle className="eye-reminder-pupil" cx="134" cy="88" r="13" />
      <circle className="eye-reminder-glint" cx="123" cy="76" r="6" />
      <path className="eye-reminder-focus" d="M101 88C101 69 115 55 134 55M167 88C167 107 153 121 134 121" />
    </svg>
  );
}

export function EyeReminderPopup({ user }: { user: User }) {
  const [now, setNow] = useState(() => new Date());
  const dayKey = eyeReminderDayKey(now);
  const firstLoginIso = ensureEyeDailyLogin(user.id, now);
  const firstLoginMs = new Date(firstLoginIso).getTime();
  const elapsedMs = Math.max(0, now.getTime() - firstLoginMs);
  const reminderNumber = Math.max(1, Math.floor(elapsedMs / EYE_INTERVAL_MS));
  const scheduleId = `${dayKey}:${firstLoginMs}`;
  const reminderId = `${scheduleId}:${reminderNumber}`;
  const reminderAt = firstLoginMs + reminderNumber * EYE_INTERVAL_MS;
  const nextReminderAt = reminderAt + EYE_INTERVAL_MS;
  const [dayState, setDayState] = useState(() => loadDayState(user.id, dayKey));
  // Cờ bật/tắt nằm ở localStorage — một NGUỒN NGOÀI React. Đọc bằng useSyncExternalStore thay vì
  // useState + useEffect(setState): React tự đăng ký/huỷ đăng ký và đọc lại đúng lúc render, nên không
  // còn cảnh "render một nhịp bằng giá trị cũ rồi mới setState sửa lại" (chính là lỗi set-state-in-effect).
  const enabled = useSyncExternalStore(
    useCallback((onChange: () => void) => subscribeEyeReminderEnabled(user.id, onChange), [user.id]),
    () => isEyeReminderEnabled(user.id),
  );
  const [resting, setResting] = useState(false);
  const [restSecondsLeft, setRestSecondsLeft] = useState(REST_SECONDS);
  const { ref, onMouseMove } = useGlow();

  // Nạp lại trạng thái của NGÀY khi sang ngày mới (hoặc đổi tài khoản). Gán lúc render thay vì trong
  // effect: React Compiler cấm setState đồng bộ trong effect, và giá trị khởi tạo useState ở trên đã
  // lo lần đầu — nên mốc `dayStateFor` chỉ cần bắt đúng lúc cặp (user.id, dayKey) đổi, đúng bộ deps cũ.
  const dayStateKey = `${user.id}:${dayKey}`;
  const [dayStateFor, setDayStateFor] = useState(dayStateKey);
  if (dayStateFor !== dayStateKey) {
    setDayStateFor(dayStateKey);
    setDayState(loadDayState(user.id, dayKey));
  }

  useEffect(() => {
    const tick = () => setNow(new Date());
    const intervalId = window.setInterval(tick, 15_000);
    const onVisible = () => {
      if (document.visibilityState === "visible") tick();
    };

    document.addEventListener("visibilitychange", onVisible);
    return () => {
      window.clearInterval(intervalId);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, []);

  const updateState = useCallback(
    (recipe: (current: EyeReminderDayState) => EyeReminderDayState) => {
      setDayState((current) => {
        const next = recipe(current);
        saveDayState(user.id, dayKey, next);
        return next;
      });
    },
    [dayKey, user.id],
  );

  const completeReminder = useCallback(() => {
    updateState((current) => {
      const nextSnoozed = { ...current.snoozedUntil };
      delete nextSnoozed[reminderId];

      return {
        ...current,
        completed: { ...current.completed, [reminderId]: true },
        snoozedUntil: nextSnoozed,
        rests: [{ time: new Date().toISOString(), durationSeconds: REST_SECONDS }, ...current.rests].slice(0, 48),
      };
    });
  }, [reminderId, updateState]);

  const snoozedMs = Object.entries(dayState.snoozedUntil).reduce((latest, [id, value]) => {
    if (!id.startsWith(`${scheduleId}:`)) return latest;
    const time = new Date(value).getTime();
    return Number.isFinite(time) ? Math.max(latest, time) : latest;
  }, 0);
  const isSnoozed = snoozedMs > now.getTime();
  const isHandled = Boolean(dayState.completed[reminderId] || dayState.dismissed[reminderId]);
  const visible = enabled && now.getTime() >= reminderAt && !isHandled && !isSnoozed;

  // Popup ẩn đi thì bỏ trạng thái "đang nghỉ"; rời chế độ nghỉ thì nạp lại đồng hồ. Gán lúc render
  // thay vì trong effect (React Compiler cấm setState đồng bộ trong effect). Hai mốc dưới chỉ chạy
  // đúng lúc `visible` / `resting` ĐỔI, nên không cắt ngang một phiên nghỉ đang chạy.
  const [restSeenVisible, setRestSeenVisible] = useState(visible);
  if (restSeenVisible !== visible) {
    setRestSeenVisible(visible);
    if (!visible) setResting(false);
  }
  const [restSeenResting, setRestSeenResting] = useState(resting);
  if (restSeenResting !== resting) {
    setRestSeenResting(resting);
    if (!resting) setRestSecondsLeft(REST_SECONDS);
  }

  useEffect(() => {
    if (!visible || !resting) return;

    // Đếm lùi mỗi giây. Giây cuối cùng vừa hạ đồng hồ về 0 vừa ghi nhận "đã nghỉ" NGAY TRONG hàm của
    // setTimeout — không phải trong thân effect — nên hợp luật mà tổng thời lượng nghỉ không đổi
    // (vẫn đúng REST_SECONDS nhịp). Rời chế độ nghỉ xong, đồng hồ tự nạp lại nhờ `restSeenResting`.
    const timeoutId = window.setTimeout(() => {
      const next = Math.max(0, restSecondsLeft - 1);
      setRestSecondsLeft(next);
      if (next <= 0) {
        completeReminder();
        setResting(false);
      }
    }, 1000);

    return () => window.clearTimeout(timeoutId);
  }, [completeReminder, resting, restSecondsLeft, visible]);

  const startRest = () => {
    setResting(true);
    setRestSecondsLeft(REST_SECONDS);
  };

  const snoozeReminder = () => {
    setResting(false);
    updateState((current) => ({
      ...current,
      snoozedUntil: {
        ...current.snoozedUntil,
        [reminderId]: new Date(Date.now() + SNOOZE_MS).toISOString(),
      },
    }));
  };

  const dismissReminder = () => {
    setResting(false);
    updateState((current) => ({
      ...current,
      dismissed: { ...current.dismissed, [reminderId]: true },
    }));
  };

  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          className="water-reminder-layer eye-reminder-layer"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.22 }}
        >
          <motion.div
            ref={ref}
            onMouseMove={onMouseMove}
            className="water-reminder-popup eye-reminder-popup"
            role="dialog"
            aria-modal="true"
            aria-labelledby="eye-reminder-title"
            initial={{ opacity: 0, y: 20, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 12, scale: 0.985 }}
            transition={{ type: "spring", stiffness: 300, damping: 30, mass: 0.75 }}
          >
            <button className="water-reminder-close eye-reminder-close" type="button" onClick={dismissReminder} aria-label="Bỏ qua lần nhắc này">
              <X className="h-5 w-5" />
            </button>

            <span className="water-reminder-liquid-shine eye-reminder-liquid-shine" aria-hidden="true" />
            <span className="water-reminder-edge-glow eye-reminder-edge-glow" aria-hidden="true" />
            <EyeReminderArtwork />

            <div className="water-reminder-copy eye-reminder-copy">
              <div className="water-reminder-kicker eye-reminder-kicker">
                <BellRing className="h-4 w-4" />
                Nhắc bảo vệ mắt
              </div>
              <h2 id="eye-reminder-title">Quy tắc 20-20-20</h2>
              <p>Nhìn ra xa khoảng 6 mét trong 20 giây để mắt được nghỉ sau mỗi 20 phút làm việc.</p>
            </div>

            <div className="water-reminder-meta eye-reminder-meta" aria-label="Thông tin lịch nhắc">
              <span>
                <Eye className="h-4 w-4" />
                Hôm nay {dayState.rests.length} lần
              </span>
              <span>Nhắc lúc {formatTime(new Date(reminderAt))}</span>
              <span>Còn {formatCountdown(nextReminderAt - now.getTime())} tới lần sau</span>
            </div>

            <div className="water-reminder-actions eye-reminder-actions">
              <button className="water-reminder-primary eye-reminder-primary" type="button" onClick={startRest} disabled={resting}>
                {resting ? <Clock3 className="h-5 w-5" /> : <CheckCircle2 className="h-5 w-5" />}
                {resting ? `Còn ${restSecondsLeft} giây` : "Bắt đầu 20 giây"}
              </button>
              <button className="water-reminder-secondary eye-reminder-secondary" type="button" onClick={snoozeReminder}>
                <TimerReset className="h-5 w-5" />
                Nhắc lại sau 5 phút
              </button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
