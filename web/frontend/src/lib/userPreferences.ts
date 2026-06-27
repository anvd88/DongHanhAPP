import { api } from "./api";
import {
  isKeepCreateVoucherOpenEnabled,
  setKeepCreateVoucherOpenEnabled,
} from "./accountingPreferences";
import {
  isEyeReminderEnabled,
  setEyeReminderEnabled,
} from "./eyeReminderClock";
import {
  isWaterReminderEnabled,
  setWaterReminderEnabled,
} from "./waterReminderClock";

export interface UserPreferences {
  waterReminderEnabled: boolean;
  eyeReminderEnabled: boolean;
  keepCreateVoucherOpen: boolean;
}

export type UserPreferencePatch = Partial<UserPreferences>;

export function readLocalUserPreferences(userId: string): UserPreferences {
  return {
    waterReminderEnabled: isWaterReminderEnabled(userId),
    eyeReminderEnabled: isEyeReminderEnabled(userId),
    keepCreateVoucherOpen: isKeepCreateVoucherOpenEnabled(userId),
  };
}

export function applyUserPreferencesToLocal(userId: string, preferences: UserPreferences) {
  setWaterReminderEnabled(userId, preferences.waterReminderEnabled);
  setEyeReminderEnabled(userId, preferences.eyeReminderEnabled);
  setKeepCreateVoucherOpenEnabled(userId, preferences.keepCreateVoucherOpen);
}

export async function loadUserPreferences(userId: string) {
  const preferences = await api.get<UserPreferences>("/api/preferences");
  applyUserPreferencesToLocal(userId, preferences);
  return preferences;
}

export async function saveUserPreferencesPatch(userId: string, patch: UserPreferencePatch) {
  const before = readLocalUserPreferences(userId);
  const optimistic = { ...before, ...patch };
  applyUserPreferencesToLocal(userId, optimistic);

  try {
    const saved = await api.put<UserPreferences>("/api/preferences", patch);
    applyUserPreferencesToLocal(userId, saved);
    return saved;
  } catch (error) {
    applyUserPreferencesToLocal(userId, before);
    throw error;
  }
}
