import { useEffect, useState } from "react";
import { AlertTriangle, CloudOff, RefreshCw, Send, WifiOff } from "lucide-react";
import { Modal } from "../../components/Modal";
import { Button, Field, Input } from "../../components/ui";
import { useAppNotifications } from "../../components/AppNotifications";
import { api } from "../../lib/api";
import {
  getOfflineCount,
  subscribeOfflineCount,
  syncOfflineAttendance,
} from "../../lib/offlineAttendance";
import { CheckInScanner } from "./CheckInScanner";
import "./chamcong.css";

/**
 * Trang chấm công riêng (/chamcong): giao diện nhận diện khuôn mặt.
 * Phần quản lý nằm ở /ql-chamcong.
 */
export function ChamCongScannerPage() {
  const { notify } = useAppNotifications();
  const [reportOpen, setReportOpen] = useState(false);
  const [targetName, setTargetName] = useState("");
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [pending, setPending] = useState(0);
  const [online, setOnline] = useState(navigator.onLine);
  const [syncing, setSyncing] = useState(false);

  // Theo dõi số bản chờ đồng bộ + trạng thái mạng để hiển thị chỉ báo. Việc TỰ đồng bộ khi có mạng
  // lại được đăng ký toàn cục ở App (RealtimeBoot) nên chạy trên mọi trang, kể cả sau khi reload.
  useEffect(() => {
    void getOfflineCount().then(setPending);
    const unsub = subscribeOfflineCount(setPending);
    const on = () => setOnline(true);
    const off = () => setOnline(false);
    window.addEventListener("online", on);
    window.addEventListener("offline", off);
    return () => {
      unsub();
      window.removeEventListener("online", on);
      window.removeEventListener("offline", off);
    };
  }, []);

  const syncNow = async () => {
    setSyncing(true);
    try {
      const s = await syncOfflineAttendance();
      if (s.synced > 0) notify.success(`Đã đồng bộ ${s.synced} lượt chấm công ngoại tuyến.`);
      else if (s.remaining > 0) notify.warning("Chưa đồng bộ được — vui lòng kiểm tra kết nối mạng.");
    } finally {
      setSyncing(false);
    }
  };

  const closeReport = () => {
    if (submitting) return;
    setReportOpen(false);
  };

  const submitReport = async () => {
    const name = targetName.trim();
    if (!name) {
      notify.warning("Vui lòng nhập tên người bị chấm lỗi.");
      return;
    }

    setSubmitting(true);
    try {
      await api.post("/api/feedback/attendance", { targetName: name, reason: reason.trim() });
      setTargetName("");
      setReason("");
      setReportOpen(false);
      notify.success("Đã gửi báo lỗi chấm công.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không gửi được báo lỗi chấm công.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="cc-root cc-root--fit">
      <div className="cc-page-head">
        <div>
          <h1 className="cc-title">Chấm công khuôn mặt</h1>
          <p className="cc-subtitle">Nhìn vào camera để ghi nhận giờ vào / ra</p>
        </div>
        <div className="flex items-center gap-2">
          {!online && (
            <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-500/15 px-3 py-1.5 text-xs font-semibold text-amber-600 dark:text-amber-400">
              <WifiOff className="h-3.5 w-3.5" /> Ngoại tuyến
            </span>
          )}
          {pending > 0 && (
            <button
              type="button"
              onClick={syncNow}
              disabled={syncing || !online}
              className="inline-flex items-center gap-1.5 rounded-full bg-[var(--accent-soft)] px-3 py-1.5 text-xs font-semibold text-[var(--accent)] transition disabled:opacity-60"
              title={online ? "Bấm để đồng bộ ngay" : "Sẽ tự đồng bộ khi có mạng"}
            >
              {syncing ? <RefreshCw className="h-3.5 w-3.5 animate-spin" /> : <CloudOff className="h-3.5 w-3.5" />}
              {pending} chờ đồng bộ
            </button>
          )}
          <Button variant="soft" className="cc-report-btn" onClick={() => setReportOpen(true)}>
            <AlertTriangle className="h-4 w-4" />
            Báo lỗi
          </Button>
        </div>
      </div>

      <div className="cc-checkin-tab">
        <CheckInScanner />
      </div>

      <Modal
        open={reportOpen}
        onClose={closeReport}
        title="Báo lỗi chấm công"
        panel
        footer={
          <>
            <Button variant="ghost" onClick={closeReport} disabled={submitting}>Huỷ</Button>
            <Button onClick={submitReport} loading={submitting}>
              <Send className="h-4 w-4" />
              Gửi báo lỗi
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <Field label="Tên người bị chấm lỗi">
            <Input
              value={targetName}
              onChange={(e) => setTargetName(e.target.value)}
              placeholder="Ví dụ: Nguyễn Văn A"
              maxLength={256}
              autoFocus
            />
          </Field>
          <Field label="Nguyên nhân">
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Có thể để trống"
              maxLength={500}
              rows={5}
              className="w-full resize-none rounded-xl border border-[var(--glass-border)] bg-white/55 px-3.5 py-2.5 text-sm text-[var(--text)] outline-none transition placeholder:text-[var(--text-muted)] focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)] dark:bg-white/5"
            />
          </Field>
        </div>
      </Modal>
    </div>
  );
}
