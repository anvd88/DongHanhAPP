import { useState } from "react";
import { Check, MapPin, ShieldAlert, Smile, Trash2, UserCheck, UserPlus, Wifi, WifiOff, X } from "lucide-react";
import { ConfirmDialog } from "../../components/ConfirmDialog";
import { useApi } from "../../lib/useApi";
import { api } from "../../lib/api";
import type {
  ChamCongLog,
  ChamCongOffline,
  FaceRegistrationLog,
  FaceEnrollmentRequest,
  FaceNguoiDung,
  EyeOpenConfig,
  LivenessPanel,
  MotionConfig,
  SmileConfig,
  OfflineConfig,
  UserAdmin,
} from "../../lib/types";
import { CameraPanel } from "./CameraPanel";
import { EnrollWizard } from "./EnrollWizard";
import "./chamcong.css";

type Tab = "dangky" | "xetduyet" | "khuonmat" | "nhatky" | "ngoaituyen";

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
        <button className="cc-tab" data-on={tab === "xetduyet"} onClick={() => setTab("xetduyet")} type="button">
          <UserCheck className="h-4 w-4" /> Yêu cầu khuôn mặt
        </button>
        <button className="cc-tab" data-on={tab === "nhatky"} onClick={() => setTab("nhatky")} type="button">
          Nhật ký chấm công
        </button>
        <button className="cc-tab" data-on={tab === "ngoaituyen"} onClick={() => setTab("ngoaituyen")} type="button">
          <ShieldAlert className="h-4 w-4" /> Ngoại tuyến chờ duyệt
        </button>
      </div>

      {tab === "dangky" && <RegisterTab />}
      {tab === "xetduyet" && <FaceEnrollmentTab />}
      {tab === "khuonmat" && <FaceDataTab />}
      {tab === "nhatky" && <LogTab />}
      {tab === "ngoaituyen" && <OfflineTab />}
    </div>
  );
}

/* ------------------ Tab: yêu cầu đăng ký khuôn mặt chờ xác minh ------------------ */
function FaceEnrollmentTab() {
  const [showAll, setShowAll] = useState(false);
  const { data, reload } = useApi<FaceEnrollmentRequest[]>(
    `/api/chamcong/face-enrollments?status=${showAll ? "all" : "pending"}`,
  );
  const [approveRow, setApproveRow] = useState<FaceEnrollmentRequest | null>(null);
  const [identityChecked, setIdentityChecked] = useState(false);
  const [approveNote, setApproveNote] = useState("");
  const [verificationImages, setVerificationImages] = useState<string[]>([]);
  const [rejectRow, setRejectRow] = useState<FaceEnrollmentRequest | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const rows = data ?? [];

  const openApprove = (row: FaceEnrollmentRequest) => {
    setIdentityChecked(false);
    setApproveNote("");
    setVerificationImages([]);
    setApproveRow(row);
  };
  const closeApprove = () => {
    setApproveRow(null);
    setIdentityChecked(false);
    setApproveNote("");
    setVerificationImages([]);
  };
  const captureVerificationImage = (image: string) => {
    setVerificationImages((current) => current.length >= 3 ? current : [...current, image]);
  };
  const openReject = (row: FaceEnrollmentRequest) => {
    setRejectReason("");
    setRejectRow(row);
  };
  const approve = async () => {
    if (!approveRow || !identityChecked || verificationImages.length < 2) return;
    await api.post(`/api/chamcong/face-enrollments/${approveRow.id}/approve`, {
      identityVerified: true,
      verificationMethod: "in_person",
      note: approveNote.trim(),
      verificationImages,
    });
    // Ảnh xác minh chỉ tồn tại trong bộ nhớ của dialog và được bỏ ngay sau khi máy chủ xử lý.
    setVerificationImages([]);
    reload();
  };
  const reject = async () => {
    if (!rejectRow || rejectReason.trim().length < 5) return;
    await api.post(`/api/chamcong/face-enrollments/${rejectRow.id}/reject`, {
      reason: rejectReason.trim(),
    });
    reload();
  };

  const statusLabel = (status: FaceEnrollmentRequest["status"]) => ({
    pending: "Chờ xác minh",
    approved: "Đã kích hoạt",
    rejected: "Đã từ chối",
    expired: "Đã hết hạn",
  })[status];

  return (
    <>
      <div className="cc-result glass">
        <div className="cc-list-title" style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <UserCheck className="h-4 w-4" />
          Yêu cầu khuôn mặt {showAll ? "(tất cả)" : "chờ xác minh"} ({rows.length})
          <button className="cc-tab" style={{ marginLeft: "auto" }} onClick={() => setShowAll((v) => !v)} type="button">
            {showAll ? "Chỉ chờ duyệt" : "Hiện lịch sử"}
          </button>
        </div>
        <div className="cc-note" style={{ margin: "8px 0 14px" }}>
          Chỉ vector đặc trưng đã mã hóa được lưu tạm; hệ thống không lưu ảnh camera. Chỉ duyệt khi HR
          đã gặp và đối chiếu trực tiếp đúng nhân viên với tài khoản. Không được duyệt thay hoặc duyệt từ xa.
        </div>
        <table className="cc-table">
          <thead>
            <tr>
              <th>Nhân viên</th>
              <th>Gửi lúc</th>
              <th>Hết hạn</th>
              <th>Số mẫu</th>
              <th>Trạng thái</th>
              <th>Người xử lý</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <td><b>{row.fullName || row.username}</b><div className="cc-list-sub">{row.username}</div></td>
                <td>{new Date(row.requestedAt).toLocaleString("vi-VN")}</td>
                <td>{new Date(row.expiresAt).toLocaleString("vi-VN")}</td>
                <td>{row.sampleCount}</td>
                <td>
                  <span className="cc-result-badge" data-loai={row.status === "approved" ? "Ra" : "Vào"}>
                    {statusLabel(row.status)}
                  </span>
                  {row.reviewNote && <div className="cc-list-sub" style={{ maxWidth: 260 }}>{row.reviewNote}</div>}
                </td>
                <td>{row.reviewedBy || "—"}</td>
                <td>
                  {row.status === "pending" && (
                    <div style={{ display: "flex", gap: 6 }}>
                      <button className="cc-icon-btn" title="Đối chiếu và duyệt" type="button" onClick={() => openApprove(row)}>
                        <Check className="h-4 w-4" style={{ color: "#16a34a" }} />
                      </button>
                      <button className="cc-icon-btn" title="Từ chối" type="button" onClick={() => openReject(row)}>
                        <X className="h-4 w-4" style={{ color: "#dc2626" }} />
                      </button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr><td colSpan={7} className="cc-empty-cell">Không có yêu cầu đăng ký khuôn mặt chờ xử lý.</td></tr>
            )}
          </tbody>
        </table>
      </div>

      <ConfirmDialog
        open={Boolean(approveRow)}
        title="Xác minh và kích hoạt khuôn mặt?"
        description={approveRow ? `Nhân viên “${approveRow.fullName || approveRow.username}” (${approveRow.username}) sẽ được kích hoạt ${approveRow.sampleCount} mẫu.` : ""}
        detail={
          <div style={{ display: "grid", gap: 10 }}>
            <div style={{ display: "grid", gap: 8 }}>
              <div>
                <b>Camera xác minh trực tiếp</b>
                <div style={{ marginTop: 3, fontWeight: 600 }}>
                  Chụp 2–3 ảnh mới khi nhân viên đang đứng trước HR. Mời nhân viên nhìn thẳng, giữ máy
                  ổn định và chụp các khung liên tiếp cách nhau một nhịp.
                </div>
              </div>
              {verificationImages.length < 3 ? (
                <CameraPanel
                  key={approveRow?.id ?? "face-enrollment-verification"}
                  onCapture={captureVerificationImage}
                  captureLabel={`Chụp ảnh ${verificationImages.length + 1}/3`}
                />
              ) : (
                <div className="cc-note" role="status">
                  Đã thu đủ 3 ảnh xác minh. Camera đã được tắt.
                </div>
              )}
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8 }}>
                <span aria-live="polite">
                  Đã thu <b>{verificationImages.length}/3</b> ảnh
                  {verificationImages.length < 2 ? " · cần ít nhất 2 ảnh" : " · đã đủ để xác minh"}
                </span>
                {verificationImages.length > 0 && (
                  <button className="cc-btn" type="button" onClick={() => setVerificationImages([])}>
                    Chụp lại
                  </button>
                )}
              </div>
              <div style={{ fontWeight: 600 }}>
                Ảnh chỉ được gửi một lần cùng quyết định duyệt, không lưu trong trình duyệt và không hiển thị lại sau khi gửi.
              </div>
            </div>
            <label style={{ display: "flex", gap: 8, alignItems: "flex-start", cursor: "pointer" }}>
              <input type="checkbox" checked={identityChecked} onChange={(e) => setIdentityChecked(e.target.checked)} />
              <span>Tôi đã trực tiếp gặp và đối chiếu đúng nhân viên với tài khoản này.</span>
            </label>
            <textarea
              value={approveNote}
              maxLength={500}
              rows={3}
              placeholder="Ghi chú đối chiếu (không bắt buộc)"
              onChange={(e) => setApproveNote(e.target.value)}
              style={{ width: "100%", borderRadius: 8, padding: 8, border: "1px solid currentColor", background: "transparent" }}
            />
          </div>
        }
        confirmLabel="Đã đối chiếu — Kích hoạt"
        busyLabel="Đang kích hoạt..."
        tone="info"
        icon={<UserCheck className="h-6 w-6" />}
        confirmDisabled={!identityChecked || verificationImages.length < 2}
        onClose={closeApprove}
        onConfirm={approve}
      />

      <ConfirmDialog
        open={Boolean(rejectRow)}
        title="Từ chối yêu cầu khuôn mặt?"
        description={rejectRow ? `Toàn bộ vector tạm của “${rejectRow.fullName || rejectRow.username}” sẽ bị xóa ngay.` : ""}
        detail={
          <div style={{ display: "grid", gap: 6 }}>
            <label htmlFor="face-reject-reason">Lý do bắt buộc (ít nhất 5 ký tự)</label>
            <textarea
              id="face-reject-reason"
              value={rejectReason}
              maxLength={500}
              rows={3}
              onChange={(e) => setRejectReason(e.target.value)}
              style={{ width: "100%", borderRadius: 8, padding: 8, border: "1px solid currentColor", background: "transparent" }}
            />
          </div>
        }
        confirmLabel="Từ chối và xóa vector"
        busyLabel="Đang xử lý..."
        tone="danger"
        icon={<X className="h-6 w-6" />}
        confirmDisabled={rejectReason.trim().length < 5}
        onClose={() => setRejectRow(null)}
        onConfirm={reject}
      />
    </>
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
      <MotionConfigPanel />
      <SmileConfigPanel />
      <EyeOpenConfigPanel />
      <LivenessMetricsPanel />
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

/* -------- Công tắc liveness QUAY ĐẦU (challenge-response) -------- */
function MotionConfigPanel() {
  const { data, reload } = useApi<MotionConfig>("/api/chamcong/motion-config");
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);

  const save = async (patch: Partial<MotionConfig>) => {
    if (!data) return;
    setBusy(true);
    setMsg(null);
    try {
      await api.put("/api/chamcong/motion-config", { ...data, ...patch });
      setMsg("Đã lưu — áp dụng ngay, không cần build lại app.");
      reload();
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Lỗi lưu cấu hình.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="cc-result glass" style={{ marginBottom: 12 }}>
      <div className="cc-list-title" style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <ShieldAlert className="h-4 w-4" /> Liveness quay đầu (chống chấm bằng ảnh)
      </div>
      <div className="cc-note" style={{ marginBottom: 10 }}>
        💡 Lúc quét, app yêu cầu nhân viên <b>nhìn thẳng rồi quay đầu sang hai bên</b>. Ảnh tĩnh không quay
        đầu được nên bị loại. Rẻ, chạy trên mọi camera. Bật thử ở chế độ <b>chỉ ghi log</b> để xem biên độ
        quay (cột <code>span</code> ở bảng dưới) trước khi bật chặn.
      </div>
      {data && (
        <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
          <label style={{ display: "flex", alignItems: "center", gap: 10, cursor: "pointer" }}>
            <input type="checkbox" checked={data.enabled} disabled={busy}
              onChange={(e) => save({ enabled: e.target.checked })} />
            <span>
              <b>Bật yêu cầu quay đầu khi quét</b>
              <div className="cc-note">Tắt = quét giữ khung tĩnh như cũ.</div>
            </span>
          </label>
          <label style={{ display: "flex", alignItems: "center", gap: 10, cursor: data.enabled ? "pointer" : "not-allowed", opacity: data.enabled ? 1 : 0.5 }}>
            <input type="checkbox" checked={data.enforce} disabled={busy || !data.enabled}
              onChange={(e) => save({ enforce: e.target.checked })} />
            <span>
              <b>Chặn nếu không quay đầu (nghi ảnh tĩnh)</b>
              <div className="cc-note">Tắt = chỉ ghi log biên độ quay để hiệu chỉnh, KHÔNG chặn chấm công.</div>
            </span>
          </label>
          {msg && <span className="cc-note">{msg}</span>}
        </div>
      )}
    </div>
  );
}

/* -------- Công tắc yêu cầu CƯỜI khi quét -------- */
function SmileConfigPanel() {
  const { data, reload } = useApi<SmileConfig>("/api/chamcong/smile-config");
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [draft, setDraft] = useState<number | null>(null);
  const threshold = draft ?? data?.threshold ?? 0.65;

  const save = async (patch: Partial<SmileConfig>) => {
    if (!data) return;
    setBusy(true);
    setMsg(null);
    try {
      await api.put("/api/chamcong/smile-config", {
        enabled: patch.enabled ?? data.enabled,
        threshold: patch.threshold ?? threshold,
      });
      setMsg("Đã lưu — áp dụng cho lượt quét tiếp theo.");
      setDraft(null);
      reload();
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Lỗi lưu cấu hình.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="cc-result glass" style={{ marginBottom: 12 }}>
      <div className="cc-list-title" style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <Smile className="h-4 w-4" /> Yêu cầu cười khi quét khuôn mặt
      </div>
      <div className="cc-note" style={{ marginBottom: 10 }}>
        Khi bật, app hướng dẫn người dùng mỉm cười và chỉ lấy hình khi độ cười đạt ngưỡng. Máy chủ tự
        kiểm tra lại nụ cười từ ảnh trước khi nhận diện và ghi chấm công.
      </div>
      {data && (
        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <label style={{ display: "flex", alignItems: "center", gap: 10, cursor: "pointer" }}>
            <input type="checkbox" checked={data.enabled} disabled={busy}
              onChange={(e) => save({ enabled: e.target.checked })} />
            <span><b>Bật yêu cầu cười</b><div className="cc-note">Tắt = quét bình thường như hiện tại.</div></span>
          </label>
          <div style={{ opacity: data.enabled ? 1 : 0.5 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <span style={{ minWidth: 110 }}>Ngưỡng cười</span>
              <input type="range" min={0.35} max={0.95} step={0.05} value={threshold}
                disabled={busy || !data.enabled}
                onChange={(e) => setDraft(Number(e.target.value))}
                onPointerUp={() => draft != null && save({ threshold: draft })}
                onKeyUp={() => draft != null && save({ threshold: draft })}
                style={{ flex: 1 }} />
              <b>{threshold.toFixed(2)}</b>
            </div>
          </div>
          {msg && <span className="cc-note">{msg}</span>}
        </div>
      )}
    </div>
  );
}

/* -------- Cấu hình + hiệu chỉnh kiểm tra MỞ MẮT phía server -------- */
function EyeOpenConfigPanel() {
  const { data, reload } = useApi<LivenessPanel>("/api/chamcong/liveness-metrics");
  const cfg: EyeOpenConfig | null = data?.eyeOpen ?? null;
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  // Ngưỡng đang kéo (chưa lưu); null = dùng giá trị máy chủ. Lưu khi thả chuột/phím.
  const [draft, setDraft] = useState<number | null>(null);
  const th = draft ?? cfg?.threshold ?? 0.35;

  const save = async (patch: Partial<EyeOpenConfig>) => {
    if (!cfg) return;
    setBusy(true);
    setMsg(null);
    try {
      await api.put("/api/chamcong/eyeopen-config", {
        enforce: patch.enforce ?? cfg.enforce,
        threshold: patch.threshold ?? th,
      });
      setMsg("Đã lưu — áp dụng ngay.");
      reload();
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Lỗi lưu cấu hình.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="cc-result glass" style={{ marginBottom: 12 }}>
      <div className="cc-list-title" style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <ShieldAlert className="h-4 w-4" /> Kiểm tra mở mắt (chống nhắm mắt / giơ ảnh mắt nhắm)
      </div>
      <div className="cc-note" style={{ marginBottom: 10 }}>
        💡 Máy chủ đo độ <b>mở mắt</b> trên khung app gửi lên (cột <code>mắt</code> ở bảng dưới, 0–1). Bật thử ở
        chế độ <b>chỉ ghi log</b> trước: quét khi mở mắt và khi nhắm/lim dim vài lần, xem cột <code>mắt</code> để
        chọn ngưỡng (thường ~0.30–0.40), rồi mới bật chặn. Đây là ước lượng hình học (chưa phải model) nên chỉ
        chặn khi đã hiệu chỉnh — <b>fail-open</b> để không khoá nhầm nhân viên.
      </div>
      {cfg ? (
        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <label style={{ display: "flex", alignItems: "center", gap: 10, cursor: "pointer" }}>
            <input type="checkbox" checked={cfg.enforce} disabled={busy}
              onChange={(e) => save({ enforce: e.target.checked })} />
            <span>
              <b>Chặn nếu không mở mắt</b>
              <div className="cc-note">Tắt = chỉ đo &amp; ghi log độ mở mắt để hiệu chỉnh, KHÔNG chặn chấm công.</div>
            </span>
          </label>
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <span style={{ minWidth: 110 }}>Ngưỡng mở mắt</span>
              <input type="range" min={0} max={1} step={0.01} value={th} disabled={busy}
                onChange={(e) => setDraft(Number(e.target.value))}
                onPointerUp={() => draft != null && save({ threshold: draft })}
                onKeyUp={() => draft != null && save({ threshold: draft })}
                style={{ flex: 1 }} />
              <b style={{ fontVariantNumeric: "tabular-nums", minWidth: 42, textAlign: "right" }}>{th.toFixed(2)}</b>
            </div>
            <div className="cc-note">Độ mở mắt của lượt chấm &lt; ngưỡng ⇒ bị chặn (báo "Chưa mở mắt").</div>
          </div>
          {msg && <span className="cc-note">{msg}</span>}
        </div>
      ) : (
        <div className="cc-note">Đang tải cấu hình…</div>
      )}
    </div>
  );
}

/* -------- Số đo Silent-Face (chống ảnh/màn hình) — hiệu chỉnh ngưỡng có số liệu -------- */
function LivenessMetricsPanel() {
  const { data: panel } = useApi<LivenessPanel>("/api/chamcong/liveness-metrics");
  const data = panel?.metrics;
  const antiSpoof = panel?.antiSpoof;

  const hhmmss = (iso: string) => {
    const d = new Date(iso);
    return isNaN(d.getTime()) ? "—" : d.toLocaleTimeString("vi-VN", { hour12: false });
  };
  const numCell = { textAlign: "right" as const, fontVariantNumeric: "tabular-nums" as const };

  return (
    <div className="cc-result glass" style={{ marginBottom: 12 }}>
      <div className="cc-list-title" style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <ShieldAlert className="h-4 w-4" /> Chống ảnh/màn hình giả (Silent-Face)
      </div>
      {antiSpoof && antiSpoof.level !== "Full" && (
        // Model chống giả mạo hỏng thì chấm công vẫn chạy y như bình thường — không có triệu chứng nào
        // khác ngoài dòng cảnh báo này. Mức None nghĩa là MỌI ảnh đều được coi là người thật.
        <div
          className="cc-note"
          style={{
            marginBottom: 8,
            padding: "8px 10px",
            borderRadius: 8,
            fontWeight: 600,
            border: "1px solid",
            borderColor: antiSpoof.level === "None" ? "#ef4444" : "#f59e0b",
            color: antiSpoof.level === "None" ? "#b91c1c" : "#b45309",
          }}
        >
          {antiSpoof.level === "None"
            ? `⛔ KHÔNG có model chống giả mạo — giơ ảnh/màn hình vẫn chấm công được, và KHÔNG còn lớp nào gác thay. Đặt lại model trên máy chủ rồi khởi động lại. ${antiSpoof.detail}.`
            : `⚠️ Chống giả mạo đang chạy ở mức yếu hơn thiết kế: ${antiSpoof.detail}.`}
        </div>
      )}
      <div className="cc-note" style={{ marginBottom: 8 }}>
        📊 Điểm "người thật" (P_real) mỗi lượt quét. Hiện quét <b>qua</b> nếu <b>khung cao nhất</b> ≥ ngưỡng.
        Quét <b>mặt thật</b> và <b>ảnh giả</b> vài lần rồi so <code>best</code>/<code>mean</code>: nếu ảnh vẫn
        <code> best</code> cao thì cần nâng ngưỡng hoặc đổi sang xét <code>mean</code> (báo tôi số liệu để chỉnh).
      </div>
      {(!data || data.length === 0) ? (
        <div className="cc-note">Chưa có số đo. Hãy quét mặt trên app để bắt đầu ghi.</div>
      ) : (
        <div style={{ overflowX: "auto" }}>
          <table className="cc-table" style={{ width: "100%", fontSize: 13 }}>
            <thead>
              <tr>
                <th style={{ textAlign: "left" }}>Giờ</th>
                <th style={{ textAlign: "left" }}>Nhân viên</th>
                <th style={{ textAlign: "right" }}>best</th>
                <th style={{ textAlign: "right" }}>mean</th>
                <th style={{ textAlign: "right" }}>nhì</th>
                <th style={{ textAlign: "right" }}>khung</th>
                <th style={{ textAlign: "right" }}>ngưỡng</th>
                <th style={{ textAlign: "right" }}>span</th>
                <th style={{ textAlign: "right" }}>mắt</th>
                <th style={{ textAlign: "left" }}>Kết luận</th>
              </tr>
            </thead>
            <tbody>
              {data.map((m, i) => (
                <tr key={i}>
                  <td>{hhmmss(m.atUtc)}</td>
                  <td>{m.user || "—"}</td>
                  <td style={numCell}>{m.best.toFixed(3)}</td>
                  <td style={numCell}>{m.mean.toFixed(3)}</td>
                  <td style={numCell}>{m.second.toFixed(3)}</td>
                  <td style={{ textAlign: "right" }}>{m.frames}</td>
                  <td style={numCell}>{m.threshold.toFixed(2)}</td>
                  <td style={numCell}>{m.motionSpan < 0 ? "—" : m.motionSpan.toFixed(3)}</td>
                  <td style={numCell}>{m.eyeOpen == null || m.eyeOpen < 0 ? "—" : m.eyeOpen.toFixed(2)}</td>
                  <td style={{ color: m.passed ? "#16a34a" : "#dc2626", fontWeight: 600 }}>
                    {m.passed ? "Qua (thật)" : "Chặn (giả)"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
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
