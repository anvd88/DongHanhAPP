import { useEffect, useState } from "react";
import { Activity, Radio, RefreshCw, ScanFace, ToggleLeft, ToggleRight, Trash2, UserPlus, Wifi, WifiOff } from "lucide-react";
import { useApi } from "../../lib/useApi";
import { api } from "../../lib/api";
import { useAuth } from "../../lib/auth";
import { isAdmin } from "../../lib/types";
import type {
  ChamCongLog,
  FaceRegistrationLog,
  FaceNguoiDung,
  RtspAttendanceStatus,
  UserAdmin,
} from "../../lib/types";
import { CameraPanel } from "./CameraPanel";
import { CheckInScanner } from "./CheckInScanner";
import { EnrollWizard } from "./EnrollWizard";
import "./chamcong.css";

type Tab = "chamcong" | "dangky" | "khuonmat" | "nhatky";

export function ChamCongPage() {
  const { user } = useAuth();
  const admin = isAdmin(user);
  const [tab, setTab] = useState<Tab>("chamcong");

  return (
    <div className="cc-root space-y-4 pb-6">
      <div>
        <h1 className="cc-title">Chấm công khuôn mặt</h1>
        <p className="cc-subtitle">Nhận diện nhân viên qua camera để ghi nhận giờ vào / ra</p>
      </div>

      <div className="cc-tabs">
        <button className="cc-tab" data-on={tab === "chamcong"} onClick={() => setTab("chamcong")} type="button">
          <ScanFace className="h-4 w-4" /> Chấm công
        </button>
        {admin && (
          <button className="cc-tab" data-on={tab === "dangky"} onClick={() => setTab("dangky")} type="button">
            <UserPlus className="h-4 w-4" /> Đăng ký khuôn mặt
          </button>
        )}
        {admin && (
          <button className="cc-tab" data-on={tab === "khuonmat"} onClick={() => setTab("khuonmat")} type="button">
            Dữ liệu khuôn mặt
          </button>
        )}
        {admin && (
          <button className="cc-tab" data-on={tab === "nhatky"} onClick={() => setTab("nhatky")} type="button">
            Nhật ký chấm công
          </button>
        )}
      </div>

      {tab === "chamcong" && <CheckInTab admin={admin} />}
      {tab === "dangky" && admin && <RegisterTab />}
      {tab === "khuonmat" && admin && <FaceDataTab />}
      {tab === "nhatky" && admin && <LogTab />}
    </div>
  );
}

/* ----------------------------- Tab: Chấm công ----------------------------- */
function CheckInTab({ admin }: { admin: boolean }) {
  return (
    <div className="space-y-4">
      <CheckInScanner />
      {admin && <RtspStatusPanel />}
    </div>
  );
}

function RtspStatusPanel() {
  const { data: status, loading, error, reload } = useApi<RtspAttendanceStatus>("/api/chamcong/rtsp/status");
  const [reconnecting, setReconnecting] = useState(false);
  const [testToggling, setTestToggling] = useState(false);
  const [reconnectError, setReconnectError] = useState<string | null>(null);

  useEffect(() => {
    const id = window.setInterval(() => reload({ silent: true }), 2500);
    return () => window.clearInterval(id);
  }, [reload]);

  const reconnect = async () => {
    if (reconnecting) return;
    setReconnecting(true);
    setReconnectError(null);
    try {
      await api.post("/api/chamcong/rtsp/reconnect");
      reload();
    } catch (e) {
      setReconnectError(e instanceof Error ? e.message : "Không kết nối lại được camera.");
    } finally {
      setReconnecting(false);
    }
  };

  const testScanEnabled = Boolean(status?.testScanEnabled);
  const toggleTestScan = async () => {
    if (testToggling) return;
    const next = !testScanEnabled;
    setTestToggling(true);
    setReconnectError(null);
    try {
      await api.post("/api/chamcong/rtsp/test-scan", { enabled: next });
      reload();
    } catch (e) {
      setReconnectError(e instanceof Error ? e.message : "Không đổi được chế độ test scan.");
    } finally {
      setTestToggling(false);
    }
  };

  const tone = !status?.enabled ? "off" : status.cameraConnected ? "on" : "warn";
  const cameraText = !status?.enabled
    ? "Đang tắt"
    : status.cameraConnected
      ? "Đã kết nối"
      : "Chưa kết nối";
  const matchedName = status?.lastMatchedName || status?.lastMatchedUser || "--";

  return (
    <section className="cc-rtsp glass" aria-live="polite">
      <div className="cc-rtsp-header">
        <div className="cc-rtsp-title">
          <Radio className="h-4 w-4" />
          <span>RTSP kiosk{status?.source ? ` - ${status.source}` : ""}</span>
        </div>
        <div className="cc-rtsp-actions">
          <span className="cc-rtsp-pill" data-tone={tone}>{reconnecting ? "Đang kết nối lại" : loading && !status ? "Đang tải" : status?.mode ?? "Không rõ"}</span>
          <button
            aria-pressed={testScanEnabled}
            className="cc-icon-btn cc-rtsp-test"
            data-on={testScanEnabled}
            disabled={testToggling}
            onClick={toggleTestScan}
            title={testScanEnabled ? "Tắt test scan 3 giây/lần" : "Bật test scan 3 giây/lần"}
            type="button"
          >
            {testScanEnabled ? <ToggleRight className="h-4 w-4" /> : <ToggleLeft className="h-4 w-4" />}
          </button>
          <button
            className="cc-icon-btn cc-rtsp-refresh"
            disabled={reconnecting}
            onClick={reconnect}
            title="Kết nối lại camera"
            type="button"
          >
            <RefreshCw className={loading || reconnecting ? "h-4 w-4 animate-spin" : "h-4 w-4"} />
          </button>
        </div>
      </div>

      {error || reconnectError ? (
        <div className="cc-rtsp-message" data-warn="true">{reconnectError ?? error}</div>
      ) : (
        <>
          <div className="cc-rtsp-grid">
            <div className="cc-rtsp-item">
              {status?.cameraConnected ? <Wifi className="h-4 w-4" /> : <WifiOff className="h-4 w-4" />}
              <div>
                <span>Camera</span>
                <strong>{cameraText}</strong>
              </div>
            </div>
            <div className="cc-rtsp-item">
              <Activity className="h-4 w-4" />
              <div>
                <span>Chuyển động</span>
                <strong>{formatPercent(status?.lastMotionScore)}</strong>
              </div>
            </div>
            <div className="cc-rtsp-item">
              <ScanFace className="h-4 w-4" />
              <div>
                <span>Burst</span>
                <strong>{status?.scanBurstCount ?? 0} scan</strong>
              </div>
            </div>
            <div className="cc-rtsp-item">
              <ScanFace className="h-4 w-4" />
              <div>
                <span>Mẫu</span>
                <strong>{status?.enrolledTemplates ?? 0}</strong>
              </div>
            </div>
            <div className="cc-rtsp-item">
              <span className="cc-rtsp-dot" />
              <div>
                <span>Lần scan</span>
                <strong>{formatTime(status?.lastScanAt)}</strong>
              </div>
            </div>
            <div className="cc-rtsp-item">
              <span className="cc-rtsp-dot" data-ok="true" />
              <div>
                <span>Nhận diện</span>
                <strong>{matchedName}</strong>
              </div>
            </div>
          </div>
          <div className="cc-rtsp-message">
            {status?.lastMessage ?? "Chưa có trạng thái."}
            {status?.lastSimilarity ? ` Độ khớp ${(status.lastSimilarity * 100).toFixed(1)}%.` : ""}
          </div>
        </>
      )}
    </section>
  );
}

function formatPercent(value?: number) {
  if (value === undefined || Number.isNaN(value)) return "--";
  return `${(value * 100).toFixed(2)}%`;
}

function formatTime(value?: string) {
  if (!value) return "--";
  return new Date(value).toLocaleTimeString("vi-VN");
}

/* -------------------------- Tab: Đăng ký khuôn mặt -------------------------- */
function RegisterTab() {
  const { data: users } = useApi<UserAdmin[]>("/api/users/");
  const { data: faces, reload } = useApi<FaceNguoiDung[]>("/api/chamcong/dadangky");
  const [username, setUsername] = useState("");
  const [mode, setMode] = useState<"auto" | "manual">("auto");
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);

  const selected = users?.find((u) => u.username === username);

  const register = async (image: string) => {
    if (!username) {
      setMsg("Hãy chọn nhân viên trước khi chụp.");
      return;
    }
    setBusy(true);
    setMsg(null);
    try {
      await api.post("/api/chamcong/dangky", {
        username,
        fullName: selected?.fullName ?? "",
        imageBase64: image,
      });
      setMsg("Đã lưu mẫu khuôn mặt. Nên chụp thêm 2–3 góc khác nhau.");
      reload();
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Lỗi lưu khuôn mặt.");
    } finally {
      setBusy(false);
    }
  };

  const remove = async (u: string) => {
    if (!confirm(`Xóa toàn bộ mẫu khuôn mặt của "${u}"?`)) return;
    await api.del(`/api/chamcong/dangky/${encodeURIComponent(u)}`);
    reload();
  };

  return (
    <div className="space-y-4">
      <div className="cc-grid">
        <div className="space-y-3">
          <label className="cc-field">
            <span>Nhân viên</span>
            <select className="cc-select" value={username} onChange={(e) => setUsername(e.target.value)}>
              <option value="">— Chọn nhân viên —</option>
              {users?.map((u) => (
                <option key={u.id} value={u.username}>
                  {u.fullName || u.username} ({u.username})
                </option>
              ))}
            </select>
          </label>
          <div className="cc-tabs">
            <button className="cc-tab" data-on={mode === "auto"} onClick={() => setMode("auto")} type="button">
              Tự chụp đúng góc
            </button>
            <button className="cc-tab" data-on={mode === "manual"} onClick={() => setMode("manual")} type="button">
              Chụp thủ công
            </button>
          </div>
        </div>

        <div className="cc-result glass cc-list">
          <div className="cc-list-title">Đã đăng ký ({faces?.length ?? 0})</div>
          {faces && faces.length > 0 ? (
            <ul>
              {faces.map((f) => (
                <li key={f.username} className="cc-list-row">
                  <div>
                    <div className="cc-list-name">{f.fullName || f.username}</div>
                    <div className="cc-list-sub">{f.soMau} mẫu</div>
                  </div>
                  <button className="cc-icon-btn" onClick={() => remove(f.username)} title="Xóa" type="button">
                    <Trash2 className="h-4 w-4" />
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <div className="cc-result-empty cc-result-empty--sm">Chưa có nhân viên nào.</div>
          )}
        </div>
      </div>

      {mode === "auto" ? (
        <EnrollWizard username={username} fullName={selected?.fullName ?? ""} onSaved={reload} />
      ) : (
        <div className="cc-grid">
          <div className="space-y-3">
            <CameraPanel onCapture={register} busy={busy} captureLabel="Chụp & lưu mẫu" />
            {msg && <div className="cc-note">{msg}</div>}
          </div>
          <div className="cc-result glass">
            <div className="cc-result-empty cc-result-empty--sm">
              Chọn nhân viên, bật camera rồi bấm “Chụp & lưu mẫu” cho từng góc.
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

/* --------------------- Tab: Dữ liệu khuôn mặt --------------------- */
function FaceDataTab() {
  const { data: faces, reload: reloadFaces } = useApi<FaceNguoiDung[]>("/api/chamcong/dadangky");
  const { data: logs, reload: reloadLogs } = useApi<FaceRegistrationLog[]>("/api/chamcong/dangky/log");
  const [search, setSearch] = useState("");

  const rows = (logs ?? []).filter(
    (l) =>
      !search ||
      `${l.username} ${l.fullName} ${l.createdBy}`.toLowerCase().includes(search.toLowerCase()),
  );

  const reloadAll = () => {
    reloadFaces();
    reloadLogs();
  };

  const removeUser = async (u: string) => {
    if (!confirm(`Xóa toàn bộ mẫu khuôn mặt của "${u}"?`)) return;
    await api.del(`/api/chamcong/dangky/${encodeURIComponent(u)}`);
    reloadAll();
  };

  const removeSample = async (id: number) => {
    if (!confirm("Xóa mẫu khuôn mặt này?")) return;
    await api.del(`/api/chamcong/dangky/mau/${id}`);
    reloadAll();
  };

  return (
    <div className="cc-grid">
      <div className="cc-result glass cc-list">
        <div className="cc-list-title">Đã đăng ký ({faces?.length ?? 0})</div>
        {faces && faces.length > 0 ? (
          <ul>
            {faces.map((f) => (
              <li key={f.username} className="cc-list-row">
                <div>
                  <div className="cc-list-name">{f.fullName || f.username}</div>
                  <div className="cc-list-sub">{f.soMau} mẫu</div>
                </div>
                <button className="cc-icon-btn" onClick={() => removeUser(f.username)} title="Xóa tất cả mẫu" type="button">
                  <Trash2 className="h-4 w-4" />
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <div className="cc-result-empty cc-result-empty--sm">Chưa có nhân viên nào.</div>
        )}
      </div>

      <div className="cc-result glass">
        <input
          className="cc-select"
          placeholder="Tìm theo tên / tài khoản / người tạo…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <table className="cc-table">
          <thead>
            <tr>
              <th>Nhân viên</th>
              <th>Người tạo</th>
              <th>Thời gian</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((l) => (
              <tr key={l.id}>
                <td>{l.fullName || l.username}</td>
                <td>{l.createdBy || "Admin"}</td>
                <td>{new Date(l.createdAt).toLocaleString("vi-VN")}</td>
                <td>
                  <button className="cc-icon-btn" onClick={() => removeSample(l.id)} title="Xóa mẫu" type="button">
                    <Trash2 className="h-4 w-4" />
                  </button>
                </td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr>
                <td colSpan={4} className="cc-empty-cell">Chưa có nhật ký đăng ký khuôn mặt.</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/* ------------------------------ Tab: Nhật ký ------------------------------ */
function LogTab() {
  const [search, setSearch] = useState("");
  const { data: logs } = useApi<ChamCongLog[]>("/api/chamcong/log");
  const rows = (logs ?? []).filter(
    (l) => !search || `${l.username} ${l.fullName}`.toLowerCase().includes(search.toLowerCase()),
  );

  return (
    <div className="cc-result glass">
      <input
        className="cc-select"
        placeholder="Tìm theo tên / tài khoản…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <table className="cc-table">
        <thead>
          <tr>
            <th>Nhân viên</th>
            <th>Loại</th>
            <th>Độ khớp</th>
            <th>Thời gian</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((l) => (
            <tr key={l.id}>
              <td>{l.fullName || l.username}</td>
              <td><span className="cc-result-badge" data-loai={l.loai}>{l.loai}</span></td>
              <td>{(l.similarity * 100).toFixed(1)}%</td>
              <td>{new Date(l.occurredAt).toLocaleString("vi-VN")}</td>
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={4} className="cc-empty-cell">Chưa có dữ liệu chấm công.</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
