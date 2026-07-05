import { useEffect, useRef, useState } from "react";
import { CheckCircle2, Clock, Eraser, PenLine, XCircle } from "lucide-react";
import { Modal } from "../Modal";
import { Badge, Button, Field, Input } from "../ui";
import { api } from "../../lib/api";
import { dateTime } from "../../lib/format";
import { useApi } from "../../lib/useApi";
import { useAppNotifications } from "../AppNotifications";
import { useAuth } from "../../lib/auth";
import { isAdmin } from "../../lib/types";
import {
  fieldDisplayValue,
  fieldLabel,
  requestFields,
  requestStatusColor,
  requestStatusLabel,
  type RequestApproval,
  type RequestDetail,
} from "../../lib/hr";

/** Bảng ký xác nhận điện tử bằng chuột / cảm ứng (dùng chung cho Phê duyệt & Quản lý đơn từ). */
export function SignaturePad({ onChange }: { onChange: (dataUrl: string | null) => void }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const drawing = useRef(false);
  const dirty = useRef(false);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.lineWidth = 2.2;
    ctx.lineCap = "round";
    ctx.strokeStyle = "#1e293b";

    const pos = (e: PointerEvent) => {
      const rect = canvas.getBoundingClientRect();
      return { x: (e.clientX - rect.left) * (canvas.width / rect.width), y: (e.clientY - rect.top) * (canvas.height / rect.height) };
    };
    const down = (e: PointerEvent) => {
      drawing.current = true;
      dirty.current = true;
      const { x, y } = pos(e);
      ctx.beginPath();
      ctx.moveTo(x, y);
      canvas.setPointerCapture(e.pointerId);
    };
    const move = (e: PointerEvent) => {
      if (!drawing.current) return;
      const { x, y } = pos(e);
      ctx.lineTo(x, y);
      ctx.stroke();
    };
    const up = () => {
      if (!drawing.current) return;
      drawing.current = false;
      onChange(dirty.current ? canvas.toDataURL("image/png") : null);
    };
    canvas.addEventListener("pointerdown", down);
    canvas.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
    return () => {
      canvas.removeEventListener("pointerdown", down);
      canvas.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
    };
  }, [onChange]);

  const clear = () => {
    const canvas = canvasRef.current;
    const ctx = canvas?.getContext("2d");
    if (canvas && ctx) ctx.clearRect(0, 0, canvas.width, canvas.height);
    dirty.current = false;
    onChange(null);
  };

  return (
    <div>
      <div className="relative rounded-xl border border-dashed border-[var(--glass-border)] bg-white">
        <canvas ref={canvasRef} width={520} height={150} className="h-[150px] w-full touch-none rounded-xl" />
        <button
          type="button"
          onClick={clear}
          className="absolute right-2 top-2 grid h-7 w-7 place-items-center rounded-lg bg-black/5 text-slate-500 hover:bg-black/10"
          aria-label="Xóa chữ ký"
        >
          <Eraser className="h-4 w-4" />
        </button>
      </div>
      <p className="mt-1 text-xs text-[var(--text-muted)]">Ký xác nhận bằng chuột hoặc cảm ứng (tùy chọn).</p>
    </div>
  );
}

/** Tiến trình phê duyệt nhiều bước — ai duyệt, trạng thái, thời điểm, ghi chú. */
function ApprovalTimeline({ approvals }: { approvals: RequestApproval[] }) {
  return (
    <div>
      <div className="mb-2 text-sm font-bold text-[var(--text)]">Tiến trình phê duyệt</div>
      <ol className="space-y-2.5">
        {approvals.map((a) => (
          <li key={a.stepNo} className="flex gap-3 rounded-2xl border border-[var(--glass-border)] p-3">
            <span
              className={`grid h-7 w-7 shrink-0 place-items-center rounded-full text-xs font-bold ${
                a.status === "Approved"
                  ? "bg-emerald-500/15 text-emerald-600"
                  : a.status === "Rejected"
                    ? "bg-red-500/15 text-red-600"
                    : "bg-amber-500/15 text-amber-600"
              }`}
            >
              {a.stepNo}
            </span>
            <div className="min-w-0 flex-1">
              <div className="flex items-center justify-between gap-2">
                <span className="truncate text-sm font-semibold text-[var(--text)]">
                  {a.approverName || (a.approverRole === "Admin" ? "Quản trị viên / HR" : a.approverUsername)}
                </span>
                <Badge color={a.status === "Approved" ? "success" : a.status === "Rejected" ? "danger" : "warning"}>
                  {requestStatusLabel(a.status)}
                </Badge>
              </div>
              {a.status !== "Pending" && (
                <div className="mt-1 text-xs text-[var(--text-muted)]">
                  {a.decidedBy} · {a.decidedAt ? dateTime(a.decidedAt) : ""}
                  {a.hasSignature && <span className="ml-1 text-[var(--accent)]">· đã ký</span>}
                </div>
              )}
              {a.comment && <div className="mt-1 text-xs text-[var(--text-secondary)]">“{a.comment}”</div>}
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}

/**
 * Modal xem chi tiết + duyệt/từ chối một đơn. Dùng chung cho:
 *  - Phê duyệt (mode "act"): người trong hộp thư luôn có nút duyệt.
 *  - Quản lý đơn từ của admin (mode "manage"): chỉ hiện nút khi người dùng thực sự được duyệt
 *    bước hiện tại (là người duyệt của bước đó, hoặc là admin ở bước Admin); còn lại chỉ xem.
 */
export function RequestReviewModal({
  id,
  mode = "act",
  onClose,
  onDecided,
}: {
  id: string;
  mode?: "act" | "manage";
  onClose: () => void;
  onDecided: (message: string) => void;
}) {
  const { notify } = useAppNotifications();
  const { user } = useAuth();
  const { data, loading } = useApi<RequestDetail>(`/api/requests/${id}`);
  const [comment, setComment] = useState("");
  const [signature, setSignature] = useState<string | null>(null);
  const [busy, setBusy] = useState<"approve" | "reject" | null>(null);
  const [penaltyOutcome, setPenaltyOutcome] = useState<"waive" | "reduce">("waive");
  const [newAmount, setNewAmount] = useState("");

  const me = user?.username ?? "";
  const admin = isAdmin(user);
  const curStep = data?.approvals.find((a) => a.stepNo === data.request.currentStep);
  // Được quyết định khi: đơn đang chờ, bước hiện tại còn chờ, và tôi là người duyệt bước đó
  // (hoặc là admin ở bước "Admin"). Ở hộp thư (act) mặc định tin phần lọc của máy chủ.
  const canDecide =
    !!data &&
    data.request.status === "Pending" &&
    !!curStep &&
    curStep.status === "Pending" &&
    (curStep.approverUsername === me || (curStep.approverRole === "Admin" && admin));
  const actionable = mode === "act" ? data?.request.status === "Pending" : canDecide;

  // Khiếu nại án phạt: ở bước duyệt cuối, người duyệt chọn xử lý phạt (bác bỏ / giảm tiền).
  const isPenaltyAppeal = data?.request.type === "penalty_appeal";
  const isFinalStep = !!data && data.request.currentStep >= data.approvals.length;
  const showPenaltyDecision = isPenaltyAppeal && isFinalStep && actionable;
  const oldAmount = Number(data?.request.payload?.penaltyAmount ?? 0) || 0;

  const fieldTypeOf = (k: string) => (data ? requestFields[data.request.type]?.find((f) => f.key === k)?.type : undefined);
  const displayVal = (k: string, v: unknown) =>
    fieldTypeOf(k) === "money" && v != null && v !== ""
      ? `${Number(v).toLocaleString("en-US")}₫`
      : data
        ? fieldDisplayValue(data.request.type, k, v)
        : String(v);

  const decide = async (approve: boolean) => {
    if (approve && showPenaltyDecision && penaltyOutcome === "reduce") {
      const n = Number(newAmount);
      if (!(n > 0) || n >= oldAmount) {
        notify.error("Số tiền phạt mới phải lớn hơn 0 và nhỏ hơn mức phạt hiện tại.");
        return;
      }
    }
    setBusy(approve ? "approve" : "reject");
    try {
      const body: Record<string, unknown> = { comment: comment.trim(), signature };
      if (approve && showPenaltyDecision) {
        body.penaltyOutcome = penaltyOutcome;
        if (penaltyOutcome === "reduce") body.newAmount = Number(newAmount);
      }
      await api.post(`/api/requests/${id}/${approve ? "approve" : "reject"}`, body);
      onDecided(approve ? "Đã duyệt đơn." : "Đã từ chối đơn.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không thực hiện được.");
    } finally {
      setBusy(null);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={data ? `${data.request.typeLabel} · ${data.request.requestNo}` : "Chi tiết đơn"}
      panel
      wide
      footer={
        actionable ? (
          <>
            <Button variant="danger" onClick={() => decide(false)} loading={busy === "reject"}>
              <XCircle className="h-4 w-4" /> {isPenaltyAppeal ? "Từ chối khiếu nại" : "Từ chối"}
            </Button>
            <Button onClick={() => decide(true)} loading={busy === "approve"}>
              <CheckCircle2 className="h-4 w-4" /> Duyệt
            </Button>
          </>
        ) : (
          <Button variant="ghost" onClick={onClose}>Đóng</Button>
        )
      }
    >
      {loading || !data ? (
        <div className="py-10 text-center text-sm text-[var(--text-muted)]">Đang tải…</div>
      ) : (
        <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <Badge color={requestStatusColor(data.request.status)}>
                {data.request.status === "Pending" && <Clock className="h-3.5 w-3.5" />}
                {requestStatusLabel(data.request.status)}
              </Badge>
              <span className="text-xs text-[var(--text-muted)]">Gửi lúc {dateTime(data.request.createdAt)}</span>
            </div>
            <div className="rounded-2xl border border-[var(--glass-border)] p-4">
              <div className="mb-2 text-sm font-bold text-[var(--text)]">{data.request.title}</div>
              <dl className="space-y-2 text-sm">
                <div className="flex justify-between gap-3">
                  <dt className="text-[var(--text-secondary)]">Người gửi</dt>
                  <dd className="font-medium text-[var(--text)]">
                    {data.request.employeeName} ({data.request.employeeCode})
                  </dd>
                </div>
                {data.request.departmentName && (
                  <div className="flex justify-between gap-3">
                    <dt className="text-[var(--text-secondary)]">Phòng ban</dt>
                    <dd className="font-medium text-[var(--text)]">{data.request.departmentName}</dd>
                  </div>
                )}
                {Object.entries(data.request.payload ?? {}).map(([k, v]) => (
                  <div key={k} className="flex justify-between gap-3">
                    <dt className="text-[var(--text-secondary)]">{fieldLabel(data.request.type, k)}</dt>
                    <dd className="text-right font-medium text-[var(--text)]">{displayVal(k, v)}</dd>
                  </div>
                ))}
              </dl>
            </div>
            <ApprovalTimeline approvals={data.approvals} />
          </div>

          <div className="space-y-4">
            {!actionable && (
              <div className="rounded-2xl border border-[var(--glass-border)] bg-[var(--accent-soft)]/40 p-4 text-sm text-[var(--text-secondary)]">
                {data.request.status !== "Pending"
                  ? "Đơn đã được xử lý xong. Bạn đang xem ở chế độ theo dõi."
                  : `Đơn đang chờ ${curStep?.approverName || (curStep?.approverRole === "Admin" ? "Quản trị viên / HR" : curStep?.approverUsername) || "người duyệt"} duyệt ở bước ${data.request.currentStep}. Bạn chỉ có thể theo dõi.`}
              </div>
            )}
            {actionable && (
              <>
                {showPenaltyDecision && (
                  <div className="rounded-2xl border border-[var(--glass-border)] bg-amber-500/5 p-4">
                    <div className="mb-2 text-sm font-bold text-[var(--text)]">Xử lý án phạt khi duyệt</div>
                    <div className="space-y-2">
                      <label className="flex items-center gap-2 text-sm">
                        <input type="radio" name="penaltyOutcome" checked={penaltyOutcome === "waive"} onChange={() => setPenaltyOutcome("waive")} />
                        <span>Bác bỏ phạt (miễn toàn bộ)</span>
                      </label>
                      <label className="flex items-center gap-2 text-sm">
                        <input type="radio" name="penaltyOutcome" checked={penaltyOutcome === "reduce"} onChange={() => setPenaltyOutcome("reduce")} />
                        <span>Giảm tiền phạt</span>
                      </label>
                      {penaltyOutcome === "reduce" && (
                        <Field label={`Số tiền phạt mới (hiện tại ${oldAmount.toLocaleString("en-US")}₫)`}>
                          <Input
                            inputMode="numeric"
                            value={newAmount ? Number(newAmount).toLocaleString("en-US") : ""}
                            onChange={(e) => setNewAmount(e.target.value.replace(/[^\d]/g, ""))}
                            placeholder="Ví dụ 200000"
                          />
                        </Field>
                      )}
                    </div>
                    <p className="mt-2 text-xs text-[var(--text-muted)]">
                      Nếu tiền phạt đã bị trừ vào lương, hệ thống sẽ tạo khoản hoàn tương ứng và chuyển phòng Kế toán duyệt chi.
                    </p>
                  </div>
                )}
                <Field label="Ý kiến / ghi chú">
                  <textarea
                    value={comment}
                    onChange={(e) => setComment(e.target.value)}
                    rows={3}
                    placeholder="Nhập ý kiến khi duyệt hoặc từ chối…"
                    className="w-full rounded-xl border border-[var(--glass-border)] bg-white/55 px-3.5 py-2.5 text-sm text-[var(--text)] outline-none focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)] dark:bg-white/5"
                  />
                </Field>
                <div>
                  <span className="mb-1.5 flex items-center gap-1.5 text-xs font-semibold text-[var(--text-secondary)]">
                    <PenLine className="h-3.5 w-3.5" /> Chữ ký xác nhận điện tử
                  </span>
                  <SignaturePad onChange={setSignature} />
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </Modal>
  );
}
