import { AnimatePresence, motion } from "framer-motion";
import { BellRing, CheckCircle2, Droplet, TimerReset, X } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { useGlow } from "./Glass";
import type { User } from "../lib/types";
import {
  ensureWaterDailyLogin,
  isWaterReminderEnabled,
  subscribeWaterReminderEnabled,
  waterReminderDayKey,
} from "../lib/waterReminderClock";
import "./water-reminder-popup.css";

const HOUR_MS = 60 * 60 * 1000;
const SNOOZE_MS = 10 * 60 * 1000;
const CUP_ML = 250;

interface DrinkLog {
  time: string;
  amountMl: number;
}

interface WaterReminderDayState {
  completed: Record<string, true>;
  dismissed: Record<string, true>;
  snoozedUntil: Record<string, string>;
  drinks: DrinkLog[];
}

const emptyDayState = (): WaterReminderDayState => ({
  completed: {},
  dismissed: {},
  snoozedUntil: {},
  drinks: [],
});

const stateKey = (userId: string, dayKey: string) => `aqualife:day-state:${userId}:${dayKey}`;

function loadDayState(userId: string, dayKey: string) {
  const raw = localStorage.getItem(stateKey(userId, dayKey));
  if (!raw) return emptyDayState();

  try {
    const parsed = JSON.parse(raw) as Partial<WaterReminderDayState>;
    return {
      completed: parsed.completed ?? {},
      dismissed: parsed.dismissed ?? {},
      snoozedUntil: parsed.snoozedUntil ?? {},
      drinks: Array.isArray(parsed.drinks) ? parsed.drinks : [],
    };
  } catch {
    return emptyDayState();
  }
}

function saveDayState(userId: string, dayKey: string, value: WaterReminderDayState) {
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

function ReminderArtwork() {
  return (
    <svg className="water-reminder-art" viewBox="0 0 260 172" role="img" aria-label="Cốc nước">
      <defs>
        <linearGradient id="wr-cup" x1="65" x2="185" y1="22" y2="136" gradientUnits="userSpaceOnUse">
          <stop stopColor="#ffffff" stopOpacity="0.82" />
          <stop offset="0.46" stopColor="#cbecff" stopOpacity="0.5" />
          <stop offset="1" stopColor="#1e8dff" stopOpacity="0.16" />
        </linearGradient>
        <linearGradient id="wr-water" x1="90" x2="168" y1="78" y2="136" gradientUnits="userSpaceOnUse">
          <stop stopColor="#76ddff" stopOpacity="0.78" />
          <stop offset="1" stopColor="#1474ff" stopOpacity="0.82" />
        </linearGradient>
        <radialGradient id="wr-drop" cx="0" cy="0" r="1" gradientTransform="matrix(33 0 0 42 179 101)" gradientUnits="userSpaceOnUse">
          <stop stopColor="#ffffff" stopOpacity="0.94" />
          <stop offset="0.4" stopColor="#7ce3ff" stopOpacity="0.9" />
          <stop offset="1" stopColor="#1671ff" stopOpacity="0.86" />
        </radialGradient>
      </defs>

      <g className="water-reminder-bubbles">
        <circle cx="40" cy="46" r="8" />
        <circle cx="54" cy="78" r="5" />
        <circle cx="210" cy="62" r="6" />
        <circle cx="224" cy="90" r="9" />
      </g>

      <g>
        <path className="water-reminder-cup-back" d="M76 36C76 28 91 24 124 24C157 24 174 28 174 36L161 138C160 148 148 153 125 153C101 153 90 148 88 138L76 36Z" />
        <path className="water-reminder-water" d="M86 84C104 75 144 74 166 83L158 136C156 143 145 147 125 147C104 147 94 143 92 136L86 84Z" />
        <ellipse className="water-reminder-rim" cx="125" cy="36" rx="49" ry="10" />
        <ellipse className="water-reminder-water-rim" cx="126" cy="84" rx="40" ry="8" />
        <path className="water-reminder-cup-front" d="M76 36C76 28 91 24 124 24C157 24 174 28 174 36L161 138C160 148 148 153 125 153C101 153 90 148 88 138L76 36Z" />
        <path className="water-reminder-drop" d="M180 57C180 57 151 95 151 119C151 139 165 151 181 151C198 151 211 139 211 119C211 96 180 57 180 57Z" />
        <path className="water-reminder-drop-glint" d="M170 92C164 102 162 112 164 122" />
      </g>
    </svg>
  );
}

export function WaterReminderPopup({ user }: { user: User }) {
  const [now, setNow] = useState(() => new Date());
  const dayKey = waterReminderDayKey(now);
  const firstLoginIso = useMemo(() => ensureWaterDailyLogin(user.id, now), [dayKey, now, user.id]);
  const firstLoginMs = new Date(firstLoginIso).getTime();
  const reminderIndex = Math.max(0, Math.floor((now.getTime() - firstLoginMs) / HOUR_MS));
  const reminderId = `${dayKey}:${reminderIndex}`;
  const reminderAt = firstLoginMs + reminderIndex * HOUR_MS;
  const nextReminderAt = firstLoginMs + (reminderIndex + 1) * HOUR_MS;
  const [dayState, setDayState] = useState(() => loadDayState(user.id, dayKey));
  const [enabled, setEnabled] = useState(() => isWaterReminderEnabled(user.id));
  const { ref, onMouseMove } = useGlow();

  useEffect(() => {
    setDayState(loadDayState(user.id, dayKey));
  }, [dayKey, user.id]);

  useEffect(() => {
    setEnabled(isWaterReminderEnabled(user.id));
    return subscribeWaterReminderEnabled(user.id, () => {
      setEnabled(isWaterReminderEnabled(user.id));
    });
  }, [user.id]);

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

  const snoozedMs = dayState.snoozedUntil[reminderId] ? new Date(dayState.snoozedUntil[reminderId]).getTime() : 0;
  const isSnoozed = Number.isFinite(snoozedMs) && snoozedMs > now.getTime();
  const isHandled = Boolean(dayState.completed[reminderId] || dayState.dismissed[reminderId]);
  const visible = enabled && now.getTime() >= reminderAt && !isHandled && !isSnoozed;
  const todayAmount = dayState.drinks.reduce((total, drink) => total + drink.amountMl, 0);

  const updateState = (recipe: (current: WaterReminderDayState) => WaterReminderDayState) => {
    setDayState((current) => {
      const next = recipe(current);
      saveDayState(user.id, dayKey, next);
      return next;
    });
  };

  const completeReminder = () => {
    updateState((current) => {
      const nextSnoozed = { ...current.snoozedUntil };
      delete nextSnoozed[reminderId];

      return {
        ...current,
        completed: { ...current.completed, [reminderId]: true },
        snoozedUntil: nextSnoozed,
        drinks: [{ time: new Date().toISOString(), amountMl: CUP_ML }, ...current.drinks].slice(0, 24),
      };
    });
  };

  const snoozeReminder = () => {
    updateState((current) => ({
      ...current,
      snoozedUntil: {
        ...current.snoozedUntil,
        [reminderId]: new Date(Date.now() + SNOOZE_MS).toISOString(),
      },
    }));
  };

  const dismissReminder = () => {
    updateState((current) => ({
      ...current,
      dismissed: { ...current.dismissed, [reminderId]: true },
    }));
  };

  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          className="water-reminder-layer"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.22 }}
        >
          <motion.div
            ref={ref}
            onMouseMove={onMouseMove}
            className="water-reminder-popup"
            role="dialog"
            aria-modal="true"
            aria-labelledby="water-reminder-title"
            initial={{ opacity: 0, y: 20, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 12, scale: 0.985 }}
            transition={{ type: "spring", stiffness: 300, damping: 30, mass: 0.75 }}
          >
            <button className="water-reminder-close" type="button" onClick={dismissReminder} aria-label="Bỏ qua lần nhắc này">
              <X className="h-5 w-5" />
            </button>

            <span className="water-reminder-liquid-shine" aria-hidden="true" />
            <span className="water-reminder-edge-glow" aria-hidden="true" />
            <ReminderArtwork />

            <div className="water-reminder-copy">
              <div className="water-reminder-kicker">
                <BellRing className="h-4 w-4" />
                Nhắc uống nước
              </div>
              <h2 id="water-reminder-title">Đến giờ uống nước</h2>
              <p>Hãy uống một ly nước để cơ thể luôn đủ nước và tỉnh táo.</p>
            </div>

            <div className="water-reminder-meta" aria-label="Thông tin lịch nhắc">
              <span>
                <Droplet className="h-4 w-4" />
                Hôm nay {todayAmount}ml
              </span>
              <span>Nhắc lúc {formatTime(new Date(reminderAt))}</span>
              <span>Còn {formatCountdown(nextReminderAt - now.getTime())} tới lần sau</span>
            </div>

            <div className="water-reminder-actions">
              <button className="water-reminder-primary" type="button" onClick={completeReminder}>
                <CheckCircle2 className="h-5 w-5" />
                Uống xong
              </button>
              <button className="water-reminder-secondary" type="button" onClick={snoozeReminder}>
                <TimerReset className="h-5 w-5" />
                Nhắc lại sau 10 phút
              </button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
