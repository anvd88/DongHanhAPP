import { Capacitor, registerPlugin } from "@capacitor/core";
import { ANDROID_VERSION_CODE, appUrl, IS_HR_APK } from "./appConfig";
import { api, tokenStore } from "./api";
import type { AppUpdateInfo } from "./types";

type ApkUpdaterPlugin = {
  install: (options: { url: string; token?: string; fileName?: string }) => Promise<{ started: boolean }>;
  openInstallSettings: () => Promise<void>;
};

const ApkUpdater = registerPlugin<ApkUpdaterPlugin>("ApkUpdater");

export async function checkForAppUpdate(): Promise<AppUpdateInfo | null> {
  if (!IS_HR_APK) return null;

  const update = await api.get<AppUpdateInfo>(
    `/api/releases/latest?appTarget=hr-apk&currentVersionCode=${encodeURIComponent(String(ANDROID_VERSION_CODE))}`,
  );

  return update.hasUpdate ? update : null;
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
  });
}
