import { useEffect, useState, type ReactNode } from "react";
import { Truck, Store, UserX, AlertTriangle } from "lucide-react";
import { Button, Field, Select } from "./ui";
import { useAppNotifications } from "./app-notifications-context";
import { api } from "../lib/api";
import type { DeliveryDriver, DeliveryState } from "../lib/types";

/**
 * Gán phiếu xuất kho ĐÃ IN cho một đường giao hàng.
 *
 * Phiếu là giấy tờ vật lý: in xong mà không ghi ai đang cầm thì cuối ngày thiếu phiếu không truy
 * được. Chọn "Lái xe" còn đẩy luôn một việc vào mục "Việc được giao" trên máy của lái xe đó.
 *
 * Là một TẤM (panel) chứ không phải hộp thoại riêng: nó nằm trong tab "Giao hàng" của màn Phiếu
 * gộp, nên nút Lưu nằm ngay trong thân chứ không ở chân hộp thoại.
 *
 * ĐỔI NGƯỜI GIỮA CHỪNG: lái xe đã nhận chuyến vẫn đổi được (xe hỏng, ốm, đổi tuyến) — lúc đó tấm
 * này đòi LÝ DO, vì tờ phiếu giấy đang nằm trong tay lái xe cũ và phải thu về.
 */
export function DeliveryAssignPanel({
  documentId,
  voucherNo,
  customerName,
  reloadToken = 0,
  onSaved,
}: {
  documentId: string;
  voucherNo: string;
  customerName: string;
  /** Đổi giá trị ⇒ đọc lại trạng thái giao hàng. Trang cha dùng khi có việc gì đó vừa đổi ở nơi
   *  khác (nghiệm thu, xác nhận về kho) — không có nó thì thẻ này treo trạng thái cũ. */
  reloadToken?: number;
  onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const [mode, setMode] = useState<string>("");
  const [driverUsername, setDriverUsername] = useState("");
  const [note, setNote] = useState("");
  const [reason, setReason] = useState("");
  const [drivers, setDrivers] = useState<DeliveryDriver[]>([]);
  const [state, setState] = useState<DeliveryState | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const [driverList, current] = await Promise.all([
          api.get<{ drivers: DeliveryDriver[] }>("/api/delivery-assignments/drivers"),
          api.get<DeliveryState>(`/api/documents/${documentId}/delivery`),
        ]);
        if (cancelled) return;
        setDrivers(driverList.drivers);
        setState(current);
        setMode(current.mode);
        setDriverUsername(current.driverUsername);
        setNote(current.note);
        setReason("");
      } catch (cause) {
        if (!cancelled) setError(cause instanceof Error ? cause.message : "Không tải được thông tin giao hàng.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [documentId, reloadToken]);

  // Hàng đã tới khách (lái xe nộp nghiệm thu trở đi) thì khoá. Máy chủ chốt, đây chỉ nghe theo.
  const locked = !!state && state.canChange === false;
  // Đang thu chuyến khỏi tay một lái xe ĐÃ NHẬN CHUYẾN ⇒ phải nêu lý do.
  // Chọn "khách lấy tại kho"/"chưa gán" cũng là thu chuyến, nên phải quy về lái xe RỖNG chứ không
  // đọc thẳng ô chọn (ô đó vẫn giữ tên người cũ khi bị ẩn đi).
  const nextDriver = mode === "driver" ? driverUsername : "";
  const liveTask = !!state && state.taskStatus !== "" && state.taskStatus !== "cancelled";
  const takingFromDriver =
    liveTask && !!state && state.driverUsername !== "" && nextDriver !== state.driverUsername;
  const needsReason = !!state?.changeNeedsReason && takingFromDriver;

  const save = async () => {
    if (mode === "driver" && !driverUsername) {
      setError("Vui lòng chọn lái xe nhận phiếu.");
      return;
    }
    if (needsReason && !reason.trim()) {
      setError(`Lái xe ${state?.driverName} đã nhận chuyến này — vui lòng nhập lý do đổi người.`);
      return;
    }
    setSaving(true);
    setError("");
    try {
      await api.post(`/api/documents/${documentId}/delivery`, { mode, driverUsername, note, reason: reason.trim() });
      const takenFrom = takingFromDriver ? ` (thu chuyến khỏi ${state?.driverName})` : "";
      const label =
        mode === "driver"
          ? `Đã gán phiếu ${voucherNo} cho ${drivers.find((d) => d.username === driverUsername)?.fullName ?? driverUsername}${takenFrom}.`
          : mode === "pickup"
            ? `Phiếu ${voucherNo}: khách tự lấy tại kho${takenFrom}.`
            : `Đã gỡ gán giao hàng phiếu ${voucherNo}${takenFrom}.`;
      notify.success(label, "Giao hàng");
      setReason("");
      // Tấm này ở lại trên màn sau khi lưu (không còn là hộp thoại tự đóng), nên phải đọc lại trạng
      // thái: số việc giao hàng vừa sinh ra và cờ khoá đổi-người đều đến từ máy chủ.
      try {
        setState(await api.get<DeliveryState>(`/api/documents/${documentId}/delivery`));
      } catch {
        /* Đọc lại hỏng không ảnh hưởng việc vừa lưu; lần mở sau sẽ đúng. */
      }
      onSaved();
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : "Không gán được giao hàng.";
      setError(message);
      notify.error(message);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-4">
      <div className="rounded-2xl border border-[var(--gc-border)] p-4">
        <div className="text-xs font-bold text-[var(--gc-text-muted)]">Khách hàng</div>
        <div className="mt-1 text-lg font-black">{customerName || "(không rõ)"}</div>
        {state?.taskNo && (
          <>
            <div className="mt-3 text-xs font-bold text-[var(--gc-text-muted)]">Việc giao hàng</div>
            <div className="mt-1 font-bold">
              {state.taskNo} · {taskStatusText(state.taskStatus)}
            </div>
          </>
        )}
      </div>

      {locked && (
        <div className="rounded-xl bg-amber-500/10 p-3 text-sm font-semibold text-amber-700 dark:text-amber-300">
          {state?.lockMessage || `${state?.driverName} đã giao xong phiếu này nên không đổi được nữa.`}
        </div>
      )}

      {!locked && state?.changeNeedsReason && (
        <div className="flex gap-2 rounded-xl bg-amber-500/10 p-3 text-sm font-semibold text-amber-700 dark:text-amber-300">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            {state.driverName} đã nhận chuyến này. Vẫn đổi được người, nhưng phải nêu lý do — hệ thống
            sẽ thu việc khỏi máy {state.driverName} và nhắc {state.driverName} bàn giao lại tờ phiếu.
          </span>
        </div>
      )}

      <Field label="Hình thức giao hàng">
        <div className="grid gap-2 sm:grid-cols-3">
          <ModeButton active={mode === "driver"} disabled={locked} onClick={() => setMode("driver")} icon={<Truck className="h-4 w-4" />} label="Lái xe giao" />
          <ModeButton active={mode === "pickup"} disabled={locked} onClick={() => setMode("pickup")} icon={<Store className="h-4 w-4" />} label="Khách lấy tại kho" />
          <ModeButton active={mode === ""} disabled={locked} onClick={() => setMode("")} icon={<UserX className="h-4 w-4" />} label="Chưa gán" />
        </div>
      </Field>

      {mode === "driver" && (
        <Field label="Lái xe nhận phiếu *">
          <Select
            className="w-full"
            value={driverUsername}
            disabled={locked || loading}
            onChange={(event) => setDriverUsername(event.target.value)}
          >
            <option value="">— Chọn lái xe —</option>
            {/* Lái xe chưa chấm công / đang nghỉ phép bị khoá kèm chú thích, đúng như ô chọn người
                nhận việc. Máy chủ cũng từ chối nếu vẫn cố gửi lên. */}
            {drivers.map((driver) => (
              <option
                key={driver.username}
                value={driver.username}
                disabled={driver.selectable === false && driver.username !== state?.driverUsername}
              >
                {driver.fullName}
                {driver.department ? ` · ${driver.department}` : ""}
                {driver.attendanceNote ? ` — ${driver.attendanceNote}` : ""}
              </option>
            ))}
          </Select>
        </Field>
      )}

      {mode !== "" && (
        <Field label="Ghi chú giao hàng">
          <textarea
            className="km-form-control min-h-20 w-full rounded-xl border p-3 text-sm outline-none"
            maxLength={1000}
            value={note}
            disabled={locked}
            onChange={(event) => setNote(event.target.value)}
            placeholder="Ví dụ: giao trước 17h, gọi trước khi tới"
          />
        </Field>
      )}

      {needsReason && (
        <Field label={`Lý do đổi người giao hàng * (đang thu chuyến của ${state?.driverName})`}>
          <div className="mb-2 flex flex-wrap gap-1.5">
            {REASON_PRESETS.map((preset) => (
              <button
                key={preset}
                type="button"
                onClick={() => setReason(preset)}
                className="rounded-full border border-[var(--gc-border)] px-2.5 py-1 text-xs font-bold text-[var(--gc-text-muted)] transition hover:border-[var(--gc-accent)] hover:text-[var(--gc-accent)]"
              >
                {preset}
              </button>
            ))}
          </div>
          <textarea
            className="km-form-control min-h-16 w-full rounded-xl border p-3 text-sm outline-none"
            maxLength={500}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Ví dụ: xe hỏng giữa đường, chuyển sang xe khác"
          />
        </Field>
      )}

      {mode === "driver" && (
        <p className="text-xs font-semibold leading-relaxed text-[var(--gc-text-muted)]">
          Lái xe sẽ thấy phiếu này trong mục “Việc được giao”. Nếu lái xe cũng đang có lệnh thu tiền
          của chính khách này, hai việc được gộp thành một dòng.
          {takingFromDriver && " Lệnh thu tiền KHÔNG tự chuyển theo — đổi riêng ở màn hình thu tiền nếu cần."}
        </p>
      )}

      {error && (
        <div className="rounded-xl bg-rose-500/10 p-3 text-sm font-semibold text-rose-600 dark:text-rose-400">{error}</div>
      )}

      <Button loading={saving} disabled={loading || locked} onClick={() => void save()}>
        {takingFromDriver ? "Đổi người giao hàng" : "Lưu hình thức giao hàng"}
      </Button>
    </div>
  );
}

/** Lý do hay gặp khi phải rút chuyến khỏi lái xe — bấm một cái là xong, đỡ phải gõ. */
const REASON_PRESETS = ["Xe hỏng giữa đường", "Lái xe bận việc khác", "Gộp chuyến / đổi tuyến", "Lái xe nghỉ đột xuất"];

function ModeButton({
  active,
  disabled,
  onClick,
  icon,
  label,
}: {
  active: boolean;
  disabled?: boolean;
  onClick: () => void;
  icon: ReactNode;
  label: string;
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className={`flex items-center justify-center gap-2 rounded-xl border p-3 text-sm font-bold transition disabled:opacity-50 ${
        active
          ? "border-[var(--gc-accent)] bg-[var(--gc-accent)]/10 text-[var(--gc-accent)]"
          : "border-[var(--gc-border)] text-[var(--gc-text-muted)] hover:border-[var(--gc-accent)]/50"
      }`}
    >
      {icon}
      {label}
    </button>
  );
}

export function taskStatusText(status: string) {
  switch (status) {
    case "assigned":
      return "Chờ lái xe nhận";
    case "in_progress":
      return "Đang giao";
    case "submitted":
      return "Đã giao, chờ nộp phiếu";
    // Chỉ còn ở việc cũ: từ 2026-08-24 việc giao hàng đi thẳng từ "đã giao" sang "đã hoàn thành".
    case "accepted":
      return "Đã nghiệm thu";
    case "completed":
      return "Đã hoàn thành";
    case "rejected":
      return "Bị trả lại";
    case "cancelled":
      return "Đã huỷ";
    default:
      return status || "—";
  }
}

/** Nhãn ngắn hiển thị trong bảng chứng từ. */
export function deliveryModeText(mode?: string, driverName?: string) {
  if (mode === "driver") return driverName ? `Lái xe: ${driverName}` : "Lái xe giao";
  if (mode === "pickup") return "Khách lấy tại kho";
  return "Chưa gán";
}
