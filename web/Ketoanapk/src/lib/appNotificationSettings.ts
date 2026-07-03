import { Capacitor, registerPlugin } from "@capacitor/core";

type NativeNotificationPermission = {
  granted?: boolean;
  systemEnabled?: boolean;
  permission?: string;
  supported?: boolean;
};

type AppNotificationPermissionPlugin = {
  check: () => Promise<NativeNotificationPermission>;
  request: () => Promise<NativeNotificationPermission>;
  openSettings: () => Promise<void>;
};

export type AppNotificationSettingStatus = {
  enabled: boolean;
  granted: boolean;
  localEnabled: boolean;
  permission: string;
  supported: boolean;
  systemEnabled: boolean;
};

const AppNotificationPermission = registerPlugin<AppNotificationPermissionPlugin>("AppNotificationPermission");

const NOTIFICATION_ENABLED_PREFIX = "km:app-notifications-enabled";

const notificationEnabledKey = (userId?: string) => `${NOTIFICATION_ENABLED_PREFIX}:${userId || "guest"}`;

function isLocallyEnabled(userId?: string) {
  return localStorage.getItem(notificationEnabledKey(userId)) !== "false";
}

function setLocallyEnabled(userId: string | undefined, enabled: boolean) {
  localStorage.setItem(notificationEnabledKey(userId), enabled ? "true" : "false");
}

function normalizeStatus(native: NativeNotificationPermission, userId?: string): AppNotificationSettingStatus {
  const granted = Boolean(native.granted);
  const supported = native.supported !== false;
  const localEnabled = isLocallyEnabled(userId);

  return {
    enabled: supported && granted && localEnabled,
    granted,
    localEnabled,
    permission: native.permission || (granted ? "granted" : "prompt"),
    supported,
    systemEnabled: native.systemEnabled !== false,
  };
}

async function readSystemNotificationStatus(): Promise<NativeNotificationPermission> {
  if (Capacitor.getPlatform() === "android") {
    try {
      return await AppNotificationPermission.check();
    } catch {
      return { granted: false, permission: "unsupported", supported: false, systemEnabled: false };
    }
  }

  if (typeof Notification === "undefined") {
    return { granted: false, permission: "unsupported", supported: false, systemEnabled: false };
  }

  return {
    granted: Notification.permission === "granted",
    permission: Notification.permission,
    supported: true,
    systemEnabled: Notification.permission === "granted",
  };
}

async function requestSystemNotificationStatus(): Promise<NativeNotificationPermission> {
  if (Capacitor.getPlatform() === "android") {
    try {
      return await AppNotificationPermission.request();
    } catch {
      return { granted: false, permission: "denied", supported: true, systemEnabled: false };
    }
  }

  if (typeof Notification === "undefined") {
    return { granted: false, permission: "unsupported", supported: false, systemEnabled: false };
  }

  const permission = await Notification.requestPermission();
  return {
    granted: permission === "granted",
    permission,
    supported: true,
    systemEnabled: permission === "granted",
  };
}

export async function getAppNotificationSettingStatus(userId?: string) {
  return normalizeStatus(await readSystemNotificationStatus(), userId);
}

export async function setAppNotificationSettingEnabled(userId: string | undefined, enabled: boolean) {
  if (!enabled) {
    setLocallyEnabled(userId, false);
    return getAppNotificationSettingStatus(userId);
  }

  const requested = await requestSystemNotificationStatus();
  setLocallyEnabled(userId, Boolean(requested.granted));
  return normalizeStatus(requested, userId);
}

export async function openAppNotificationSystemSettings() {
  if (Capacitor.getPlatform() === "android") {
    await AppNotificationPermission.openSettings();
  }
}
