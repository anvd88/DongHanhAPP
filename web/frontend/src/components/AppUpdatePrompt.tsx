import { useCallback, useEffect, useRef, useState } from "react";
import { Download, ShieldAlert } from "lucide-react";
import { useAuth } from "../lib/auth";
import { IS_HR_APK } from "../lib/appConfig";
import { checkForAppUpdate, installAppUpdate } from "../lib/apkUpdater";
import { subscribeRealtime } from "../lib/realtime";
import type { AppUpdateInfo } from "../lib/types";
import { Button } from "./ui";
import { useAppNotifications } from "./AppNotifications";
import "./app-update-prompt.css";

export function AppUpdatePrompt() {
  const { user } = useAuth();
  const { confirm, notify } = useAppNotifications();
  const [mandatoryUpdate, setMandatoryUpdate] = useState<AppUpdateInfo | null>(null);
  const [installing, setInstalling] = useState(false);
  const lastPromptedVersion = useRef<number | null>(null);

  const install = useCallback(async (update: AppUpdateInfo) => {
    setInstalling(true);
    try {
      await installAppUpdate(update);
      notify.info("Android sẽ mở màn hình cài đặt APK. Nếu máy hỏi quyền cài ứng dụng không rõ nguồn, hãy cho phép cho Ketoan.", "Đang mở bản cập nhật");
    } catch (error) {
      const message = error instanceof Error ? error.message : "Không tải/cài được bản cập nhật.";
      notify.error(message, "Cập nhật thất bại");
    } finally {
      setInstalling(false);
    }
  }, [notify]);

  const check = useCallback(async () => {
    if (!IS_HR_APK || !user) return;

    const update = await checkForAppUpdate();
    if (!update) return;

    if (update.isMandatory) {
      setMandatoryUpdate(update);
      return;
    }

    if (update.versionCode && lastPromptedVersion.current === update.versionCode) return;
    lastPromptedVersion.current = update.versionCode ?? null;

    const ok = await confirm({
      title: `Có bản cập nhật ${update.version ?? ""}`,
      description: update.releaseNotes || "Admin đã phát hành bản APK mới cho ứng dụng Nhân sự.",
      confirmLabel: "Cập nhật",
      cancelLabel: "Để sau",
      tone: "info",
    });
    if (ok) await install(update);
  }, [confirm, install, user]);

  useEffect(() => {
    if (!IS_HR_APK || !user) return;
    check().catch(() => {});
    const unsub = subscribeRealtime(() => check().catch(() => {}), ["release"]);
    const timer = window.setInterval(() => check().catch(() => {}), 10 * 60 * 1000);
    return () => {
      unsub();
      window.clearInterval(timer);
    };
  }, [check, user]);

  if (!mandatoryUpdate) return null;

  return (
    <div className="app-update-layer" role="dialog" aria-modal="true" aria-labelledby="app-update-title">
      <div className="app-update-card">
        <div className="app-update-icon" aria-hidden="true">
          <ShieldAlert className="h-7 w-7" />
        </div>
        <div>
          <p className="app-update-kicker">Admin yêu cầu cập nhật</p>
          <h2 id="app-update-title">Phiên bản {mandatoryUpdate.version} đã sẵn sàng</h2>
          <p className="app-update-copy">
            {mandatoryUpdate.releaseNotes || "Vui lòng cập nhật để tiếp tục dùng ứng dụng Nhân sự với dữ liệu đồng bộ mới nhất."}
          </p>
        </div>
        <Button loading={installing} onClick={() => install(mandatoryUpdate)} className="w-full">
          <Download className="h-4 w-4" />
          Tải và cài cập nhật
        </Button>
      </div>
    </div>
  );
}
