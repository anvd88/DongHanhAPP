const WATER_LOGIN_PREFIX = "aqualife:first-login";
const WATER_ENABLED_PREFIX = "aqualife:water-reminders-enabled";
const WATER_ENABLED_EVENT = "aqualife:water-reminders-enabled-change";

const waterEnabledKey = (userId: string) => `${WATER_ENABLED_PREFIX}:${userId}`;

export function waterReminderDayKey(date = new Date()) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function ensureWaterDailyLogin(userId: string, date = new Date()) {
  const key = `${WATER_LOGIN_PREFIX}:${userId}:${waterReminderDayKey(date)}`;
  const current = localStorage.getItem(key);
  if (current && Number.isFinite(new Date(current).getTime())) return current;

  const value = date.toISOString();
  localStorage.setItem(key, value);
  return value;
}

export function restartWaterDailyLogin(userId: string, date = new Date()) {
  const key = `${WATER_LOGIN_PREFIX}:${userId}:${waterReminderDayKey(date)}`;
  const value = date.toISOString();
  localStorage.setItem(key, value);
  return value;
}

export function isWaterReminderEnabled(userId: string) {
  return localStorage.getItem(waterEnabledKey(userId)) !== "false";
}

export function setWaterReminderEnabled(userId: string, enabled: boolean) {
  localStorage.setItem(waterEnabledKey(userId), enabled ? "true" : "false");
  window.dispatchEvent(new CustomEvent(WATER_ENABLED_EVENT, { detail: { userId, enabled } }));
}

export function subscribeWaterReminderEnabled(userId: string, onChange: () => void) {
  const key = waterEnabledKey(userId);

  const onLocalChange = (event: Event) => {
    const detail = event instanceof CustomEvent ? event.detail : undefined;
    if (!detail || detail.userId === userId) onChange();
  };

  const onStorage = (event: StorageEvent) => {
    if (event.key === key) onChange();
  };

  window.addEventListener(WATER_ENABLED_EVENT, onLocalChange);
  window.addEventListener("storage", onStorage);
  return () => {
    window.removeEventListener(WATER_ENABLED_EVENT, onLocalChange);
    window.removeEventListener("storage", onStorage);
  };
}
