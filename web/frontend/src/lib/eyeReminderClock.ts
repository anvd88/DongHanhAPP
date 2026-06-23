const EYE_LOGIN_PREFIX = "aqualife:eye:first-login";
const EYE_ENABLED_PREFIX = "aqualife:eye-reminders-enabled";
const EYE_ENABLED_EVENT = "aqualife:eye-reminders-enabled-change";

const eyeEnabledKey = (userId: string) => `${EYE_ENABLED_PREFIX}:${userId}`;

export function eyeReminderDayKey(date = new Date()) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function ensureEyeDailyLogin(userId: string, date = new Date()) {
  const key = `${EYE_LOGIN_PREFIX}:${userId}:${eyeReminderDayKey(date)}`;
  const current = localStorage.getItem(key);
  if (current && Number.isFinite(new Date(current).getTime())) return current;

  const value = date.toISOString();
  localStorage.setItem(key, value);
  return value;
}

export function restartEyeDailyLogin(userId: string, date = new Date()) {
  const key = `${EYE_LOGIN_PREFIX}:${userId}:${eyeReminderDayKey(date)}`;
  const value = date.toISOString();
  localStorage.setItem(key, value);
  return value;
}

export function isEyeReminderEnabled(userId: string) {
  return localStorage.getItem(eyeEnabledKey(userId)) !== "false";
}

export function setEyeReminderEnabled(userId: string, enabled: boolean) {
  localStorage.setItem(eyeEnabledKey(userId), enabled ? "true" : "false");
  window.dispatchEvent(new CustomEvent(EYE_ENABLED_EVENT, { detail: { userId, enabled } }));
}

export function subscribeEyeReminderEnabled(userId: string, onChange: () => void) {
  const key = eyeEnabledKey(userId);

  const onLocalChange = (event: Event) => {
    const detail = event instanceof CustomEvent ? event.detail : undefined;
    if (!detail || detail.userId === userId) onChange();
  };

  const onStorage = (event: StorageEvent) => {
    if (event.key === key) onChange();
  };

  window.addEventListener(EYE_ENABLED_EVENT, onLocalChange);
  window.addEventListener("storage", onStorage);
  return () => {
    window.removeEventListener(EYE_ENABLED_EVENT, onLocalChange);
    window.removeEventListener("storage", onStorage);
  };
}
