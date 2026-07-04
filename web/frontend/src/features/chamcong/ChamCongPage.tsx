import { useState } from "react";
import { Check, MapPin, ShieldAlert, Trash2, UserPlus, Wifi, WifiOff, X } from "lucide-react";
import { ConfirmDialog } from "../../components/ConfirmDialog";
import { useApi } from "../../lib/useApi";
import { api } from "../../lib/api";
import type {
  ChamCongLog,
  ChamCongOffline,
  FaceRegistrationLog,
  FaceNguoiDung,
  OfflineConfig,
  UserAdmin,
} from "../../lib/types";
import { CameraPanel } from "./CameraPanel";
import { EnrollWizard } from "./EnrollWizard";
import "./chamcong.css";

type Tab = "dangky" | "khuonmat" | "nhatky" | "ngoaituyen";

interface FaceDeleteConfirm {
  title: string;
  description: string;
  detail?: string;
  confirmLabel: string;
  onConfirm: () => Promise<void>;
}

/**
 * Trang quản lý chấm công (admin): đăng ký khuôn mặt, dữ liệu sinh trắc và nhật ký.
 * Giao diện chấm công cho nhân viên nằm ở trang riêng /chamcong.
 */
export function ChamCongPage() {
  const [tab, setTab] = useState<Tab>("dangky");

  return (
    <div className="cc-root space-y-4 pb-6">
      <div>
        <h1 className="cc-title">Quản lý chấm công</h1>
        <p className="cc-subtitle">Đăng ký khuôn mặt, dữ liệu sinh trắc và nhật ký chấm công</p>
      </div>
      <div className="cc-tabs">
        <button className="cc-tab" data-on={tab === "dangky"} onClick={() => setTab("dangky")} type="button">
          <UserPlus className="h-4 w-4" /> Đăng ký khuôn mặt
        </button>
        <button className="cc-tab" data-on={tab === "khuonmat"} onClick={() => setTab("khuonmat")} type="button">
          Dữ liệu khuôn mặt
        </button>
        <button className="cc-tab" data-on={tab === "nhatky"} onClick={() => setTab("nhatky")} type="button">
          Nhật ký chấm công
        </button>
        <button className="cc-tab" data-on={tab === "ngoaituyen"} onClick={() => setTab("ngoaituyen")} type="button">
          <ShieldAlert className="h-4 w-4" /> Ngoại tuyến chờ duyệt
        </button>
      </div>

      {tab === "dangky" && <RegisterTab />}
      {tab === "khuonmat" && <FaceDataTab />}
      {tab === "nhatky" && <LogTab />}
      {tab === "ngoaituyen" && <OfflineTab />}
    </div>
  );
}

/* ------------------ Tab: Chấm công ngoại tuyến chờ duyệt ------------------ */
function OfflineTab() {
  const [showAll, setShowAll] = useState(false);
  const { data: rows, reload } = useApi<ChamCongOffline[]>(
    `/api/chamcong/offline?status=${showAll ? "all" : "pending"}`,
  );
  const [busyId, setBusyId] = useState<number | null>(null);
  const [reject, setReject] = useState<ChamCongOffline | null>(null);

  const approve = async (r: ChamCongOffline) => {
    setBusyId(r.id);
    try {
      await api.post(`/api/chamcong/offline/${r.id}/approve`, {});
      reload();
    } finally {
      setBusyId(null);
    }
  };

  const doReject = async () => {
    if (!reject) return;
    await api.post(`/api/chamcong/offline/${reject.id}/reject`, {});
    reload();
  };

  const list = rows ?? [];

  return (
    <>
      <OfflineConfigPanel />

      <div className="cc-result glass">
        <div className="cc-list-title" style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <ShieldAlert className="h-4 w-4" />
          Chấm công ngoại tuyến {showAll ? "(tất cả)" : "chờ duyệt"} ({list.length})
          <button className="cc-tab" style={{ marginLeft: "auto" }} onClick={() => setShowAll((v) => !v)} type="button">
            {showAll ? "Chỉ chờ duyệt" : "Hiện tất cả"}
          </button>
        </div>
        <p className="cc-subtitle" style={{ margin: "4px 0 10px" }}>
          Bản chấm khi mất mạng/điện — chưa tính vào bảng công. Kiểm tra cờ rủi ro rồi duyệt hoặc từ chối.
        </p>
        <table className="cc-table">
          <thead>
            <tr>
              <th>Nhân viên</th>
              <th>Loại</th>
              <th>Giờ chấm</th>
              <th>Đồng bộ</th>
              <th>Lùi giờ</th>
              <th>LAN</th>
              <th>Vị trí</th>
              <th>Cờ rủi ro</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {list.map((r) => (
              <tr key={r.id}>
                <td>{r.fullName || r.username}</td>
                <td><span className="cc-result-badge" data-loai={r.loai}>{r.loai}</span></td>
                <td>{new Date(r.occurredAt).toLocaleString("vi-VN")}</td>
                <td>{new Date(r.syncedAt).toLocaleString("vi-VN")}</td>
                <td>{r.backdateMinutes} phút</td>
                <td>
                  {r.onCompanyLan
                    ? <Wifi className="h-4 w-4" style={{ color: "#16a34a" }} />
                    : <WifiOff className="h-4 w-4" style={{ color: "#dc2626" }} />}
                </td>
                <td>
                  {r.gpsLat == null || r.gpsLng == null ? (
                    "—"
                  ) : (
                    <span style={{ display: "inline-flex", alignItems: "center", gap: 4, color: r.inGeofence === false ? "#dc2626" : undefined }}>
                      <MapPin className="h-3.5 w-3.5" />
                      {r.distanceM != null ? `${Math.round(r.distanceM)} m` : `${r.gpsLat.toFixed(4)}, ${r.gpsLng.toFixed(4)}`}
                    </span>
                  )}
                </td>
                <td style={{ maxWidth: 220, color: r.flags ? "#dc2626" : "#16a34a", fontSize: 12 }}>
                  {r.flags || "Không có bất thường"}
                </td>
                <td>
                  {r.status === "pending" ? (
                    <div style={{ display: "flex", gap: 6 }}>
                      <button className="cc-icon-btn" title="Duyệt" type="button" disabled={busyId === r.id} onClick={() => approve(r)}>
                        <Check className="h-4 w-4" style={{ color: "#16a34a" }} />
                      </button>
                      <button className="cc-icon-btn" title="Từ chối" type="button" onClick={() => setReject(r)}>
                        <X className="h-4 w-4" style={{ color: "#dc2626" }} />
                      </button>
                    </div>
                  ) : (
                    <span className="cc-list-sub">{r.status === "approved" ? "Đã duyệt" : "Đã từ chối"}</span>
                  )}
                </td>
              </tr>
            ))}
            {list.length === 0 && (
              <tr>
                <td colSpan={9} className="cc-empty-cell">Không có bản chấm công ngoại tuyến.</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <ConfirmDialog
        open={Boolean(reject)}
        title="Từ chối chấm công ngoại tuyến?"
        description={reject ? `Bản chấm ${reject.loai} của "${reject.fullName || reject.username}" lúc ${new Date(reject.occurredAt).toLocaleString("vi-VN")} sẽ KHÔNG được ghi công.` : ""}
        detail="Nhân viên cần chấm công lại khi có mặt tại công ty."
        confirmLabel="Từ chối"
        busyLabel="Đang xử lý..."
        tone="danger"
        icon={<X className="h-6 w-6" />}
        onClose={() => setReject(null)}
        onConfirm={doReject}
      />
    </>
  );
}

/* -------- Cấu hình geofence + ngưỡng lùi giờ cho chấm công ngoại tuyến -------- */
function OfflineConfigPanel() {
  const { data, reload } = useApi<OfflineConfig>("/api/chamcong/offline-config");
  const [form, setForm] = useState<OfflineConfig | null>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const cfg = form ?? data;

  const set = (patch: Partial<OfflineConfig>) => cfg && setForm({ ...cfg, ...patch });

  const useCurrentLocation = () => {
    if (!navigator.geolocation) {
      setMsg("Trình duyệt không hỗ trợ định vị.");
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (pos) => set({ geofenceLat: +pos.coords.latitude.toFixed(6), geofenceLng: +pos.coords.longitude.toFixed(6) }),
      () => setMsg("Không lấy được vị trí. Hãy cho phép truy cập vị trí."),
      { enableHighAccuracy: true, timeout: 10000 },
    );
  };

  const save = async () => {
    if (!cfg) return;
    setBusy(true);
    setMsg(null);
    try {
      await api.put("/api/chamcong/offline-config", cfg);
      setMsg("Đã lưu cấu hình.");
      setForm(null);
      reload();
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Lỗi lưu cấu hình.");
    } finally {
      setBusy(false);
    }
  };

  const num = (v: number | null | undefined) => (v == null ? "" : String(v));
  const parse = (s: string): number | null => (s.trim() === "" ? null : Number(s));

  return (
    <div className="cc-result glass" style={{ marginBottom: 12 }}>
      <div className="cc-list-title" style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <MapPin className="h-4 w-4" /> Cấu hình chấm công ngoại tuyến
      </div>
      <div className="cc-note" style={{ marginBottom: 10 }}>
        💡 Khuyến nghị: cắm <b>máy chủ + router WiFi vào một UPS (bộ lưu điện)</b> để LAN luôn sống khi mất điện
        → chấm công luôn trực tuyến, không cần offline. Offline chỉ là phương án dự phòng có kiểm soát bên dưới.
      </div>
      {cfg && (
        <>
          <div className="cc-grid" style={{ gridTemplateColumns: "repeat(2, minmax(0,1fr))", gap: 12 }}>
            <label className="cc-field">
              <span>Vĩ độ công ty (lat)</span>
              <input className="cc-select" inputMode="decimal" placeholder="Bỏ trống = tắt geofence"
                value={num(cfg.geofenceLat)} onChange={(e) => set({ geofenceLat: parse(e.target.value) })} />
            </label>
            <label className="cc-field">
              <span>Kinh độ công ty (lng)</span>
              <input className="cc-select" inputMode="decimal" placeholder="Bỏ trống = tắt geofence"
                value={num(cfg.geofenceLng)} onChange={(e) => set({ geofenceLng: parse(e.target.value) })} />
            </label>
            <label className="cc-field">
              <span>Bán kính cho phép (mét)</span>
              <input className="cc-select" inputMode="numeric"
                value={num(cfg.geofenceRadiusM)} onChange={(e) => set({ geofenceRadiusM: Number(e.target.value) || 0 })} />
            </label>
            <label className="cc-field">
              <span>Lùi giờ tối đa (phút)</span>
              <input className="cc-select" inputMode="numeric"
                value={num(cfg.maxBackdateMinutes)} onChange={(e) => set({ maxBackdateMinutes: Number(e.target.value) || 0 })} />
            </label>
          </div>
          <div style={{ display: "flex", gap: 8, marginTop: 10, flexWrap: "wrap" }}>
            <button className="cc-tab" type="button" onClick={useCurrentLocation}>
              <MapPin className="h-4 w-4" /> Dùng vị trí hiện tại
            </button>
            <button className="cc-tab" type="button" data-on onClick={save} disabled={busy}>
              {busy ? "Đang lưu…" : "Lưu cấu hình"}
            </button>
            {msg && <span className="cc-note" style={{ alignSelf: "center" }}>{msg}</span>}
          </div>
        </>
      )}
    </div>
  );
}

/* -------------------------- Tab: Đăng ký khuôn mặt -------------------------- */
function RegisterTab() {
  const { data: users } = useApi<UserAdmin[]>("/api/users/");
  const { data: faces, reload } = useApi<FaceNguoiDung[]>("/api/chamcong/dadangky");
  const [username, setUsername] = useState("");
  const [mode, setMode] = useState<"auto" | "manual">("auto");
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<FaceDeleteConfirm | null>(null);

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
    setConfirmDelete({
      title: "Xóa mẫu khuôn mặt?",
      description: `Toàn bộ mẫu khuôn mặt của "${u}" sẽ bị xóa khỏi dữ liệu chấm công.`,
      detail: "Thao tác này không thể hoàn tác.",
      confirmLabel: "Xóa tất cả",
      onConfirm: async () => {
        await api.del(`/api/chamcong/dangky/${encodeURIComponent(u)}`);
        reload();
      },
    });
  };

  return (
    <>
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
    <ConfirmDialog
      open={Boolean(confirmDelete)}
      title={confirmDelete?.title ?? ""}
      description={confirmDelete?.description ?? ""}
      detail={confirmDelete?.detail}
      confirmLabel={confirmDelete?.confirmLabel}
      busyLabel="Đang xóa..."
      tone="danger"
      icon={<Trash2 className="h-6 w-6" />}
      onClose={() => setConfirmDelete(null)}
      onConfirm={() => confirmDelete?.onConfirm()}
    />
    </>
  );
}

/* --------------------- Tab: Dữ liệu khuôn mặt --------------------- */
function FaceDataTab() {
  const { data: faces, reload: reloadFaces } = useApi<FaceNguoiDung[]>("/api/chamcong/dadangky");
  const { data: logs, reload: reloadLogs } = useApi<FaceRegistrationLog[]>("/api/chamcong/dangky/log");
  const [search, setSearch] = useState("");
  const [confirmDelete, setConfirmDelete] = useState<FaceDeleteConfirm | null>(null);

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
    setConfirmDelete({
      title: "Xóa dữ liệu khuôn mặt?",
      description: `Toàn bộ mẫu đã đăng ký của "${u}" sẽ bị xóa khỏi hệ thống.`,
      detail: "Nhân viên này cần đăng ký lại khuôn mặt để chấm công.",
      confirmLabel: "Xóa tất cả",
      onConfirm: async () => {
        await api.del(`/api/chamcong/dangky/${encodeURIComponent(u)}`);
        reloadAll();
      },
    });
  };

  const removeSample = async (id: number) => {
    setConfirmDelete({
      title: "Xóa mẫu khuôn mặt?",
      description: "Mẫu khuôn mặt này sẽ bị xóa khỏi nhật ký đăng ký.",
      detail: "Các mẫu còn lại của nhân viên vẫn được giữ nguyên.",
      confirmLabel: "Xóa mẫu",
      onConfirm: async () => {
        await api.del(`/api/chamcong/dangky/mau/${id}`);
        reloadAll();
      },
    });
  };

  return (
    <>
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
    <ConfirmDialog
      open={Boolean(confirmDelete)}
      title={confirmDelete?.title ?? ""}
      description={confirmDelete?.description ?? ""}
      detail={confirmDelete?.detail}
      confirmLabel={confirmDelete?.confirmLabel}
      busyLabel="Đang xóa..."
      tone="danger"
      icon={<Trash2 className="h-6 w-6" />}
      onClose={() => setConfirmDelete(null)}
      onConfirm={() => confirmDelete?.onConfirm()}
    />
    </>
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
