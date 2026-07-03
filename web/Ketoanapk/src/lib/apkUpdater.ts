import { Capacitor, registerPlugin } from "@capacitor/core";
import { ANDROID_VERSION_CODE, ANDROID_VERSION_NAME, appUrl, IS_HR_APK } from "./appConfig";
import { api, tokenStore } from "./api";
import type { AppUpdateInfo } from "./types";

type ApkUpdaterPlugin = {
  install: (options: { url: string; token?: string; fileName?: string; sha256?: string; size?: number }) => Promise<{ started: boolean }>;
  getInstalledVersion: () => Promise<{ versionCode?: number | string; versionName?: string }>;
  openInstallSettings: () => Promise<void>;
};

const ApkUpdater = registerPlugin<ApkUpdaterPlugin>("ApkUpdater");

export async function checkForAppUpdate(): Promise<AppUpdateInfo | null> {
  if (!IS_HR_APK) return null;

  const currentVersionCode = (await getCurrentAppVersion()).versionCode;
  const update = await api.get<AppUpdateInfo>(
    `/api/releases/latest?appTarget=hr-apk&currentVersionCode=${encodeURIComponent(String(currentVersionCode))}`,
  );

  return update.hasUpdate ? update : null;
}

export async function getCurrentAppVersion(): Promise<{ versionCode: number; versionName: string }> {
  if (Capacitor.getPlatform() !== "android") {
    return { versionCode: ANDROID_VERSION_CODE, versionName: ANDROID_VERSION_NAME };
  }

  try {
    const installed = await ApkUpdater.getInstalledVersion();
    const versionCode = Number(installed.versionCode);
    if (Number.isFinite(versionCode) && versionCode > 0) {
      return {
        versionCode,
        versionName: installed.versionName || ANDROID_VERSION_NAME,
      };
    }
  } catch {
    // Fall back to the value baked into the web bundle for older native shells.
  }

  return { versionCode: ANDROID_VERSION_CODE, versionName: ANDROID_VERSION_NAME };
}

export async function installAppUpdate(update: AppUpdateInfo) {
  if (Capacitor.getPlatform() !== "android") {
    throw new Error("Chức năng cài APK chỉ chạy trong app Android.");
  }
  if (!update.downloadUrl) {
    throw new Error("Bản cập nhật chưa có đường dẫn tải APK.");
  }

  await ApkUpdater.install({
    url: appUrl(update.downloadUrl),
    token: tokenStore.get() ?? "",
    fileName: update.apkFileName || `ketoan-hr-${update.version ?? "update"}.apk`,
    sha256: update.apkSha256 || undefined,
    size: update.apkSize || undefined,
  });
}
