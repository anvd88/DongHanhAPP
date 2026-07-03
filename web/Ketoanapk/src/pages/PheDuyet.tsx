import { useEffect, useRef, useState } from "react";
import { CheckCircle2, Eraser, Inbox, PenLine, RefreshCw, XCircle } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Modal } from "../components/Modal";
import { Badge, Button, Field, Input } from "../components/ui";
import { Table } from "../components/Table";
import { api } from "../lib/api";
import { dateTime } from "../lib/format";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/AppNotifications";
import { fieldLabel, type RequestDetail, type RequestListItem } from "../lib/hr";

export function PheDuyet() {
  const { notify } = useAppNotifications();
  const { data, loading, error, reload } = useApi<RequestListItem[]>("/api/requests?scope=inbox");
  const [activeId, setActiveId] = useState<string | null>(null);
  const rows = data ?? [];

  return (
    <div className="gc-root">
      <PageHeader title="Phê duyệt" subtitle="Các đơn đang chờ bạn duyệt ở bước hiện tại" />

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        <div className="flex items-center justify-between border-b border-[var(--gc-border)] px-5 py-4">
          <div>
            <h2 className="font-bold text-[var(--text)]">Hộp thư duyệt</h2>
            <p className="text-xs text-[var(--text-secondary)]">Bấm “Xem &amp; duyệt” để xem chi tiết, ký xác nhận và quyết định.</p>
          </div>
          <button
            type="button"
            onClick={() => reload()}
            className="grid h-8 w-8 place-items-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]"
            aria-label="Làm mới"
          >
            <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
          </button>
        </div>

        {error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <Table<RequestListItem>
            loading={loading}
            rows={rows}
            keyOf={(r) => r.id}
            empty="Không có đơn nào chờ bạn duyệt 🎉"
            columns={[
              { header: "Mã đơn", cell: (r) => <span className="font-mono text-xs font-bold text-[var(--accent)]">{r.requestNo}</span> },
              { header: "Loại đơn", cell: (r) => <span className="font-semibold">{r.typeLabel}</span> },
              { header: "Người gửi", cell: (r) => <span>{r.employeeName} <span className="text-xs text-[var(--text-muted)]">({r.employeeCode})</span></span> },
              { header: "Bước", cell: (r) => <Badge color="warning">Bước {r.currentStep}/{r.totalSteps}</Badge> },
              { header: "Ngày gửi", cell: (r) => <span className="whitespace-nowrap text-xs text-[var(--text-secondary)]">{dateTime(r.createdAt)}</span> },
              {
                header: "",
                align: "right",
                cell: (r) => (
                  <Button variant="soft" onClick={() => setActiveId(r.id)}>
                    <Inbox className="h-4 w-4" /> Xem &amp; duyệt
                  </Button>
                ),
              },
            ]}
          />
        )}
      </GlassPanel>

      {activeId && (
        <ApproveModal
          id={activeId}
          onClose={() => setActiveId(null)}
          onDecided={(msg) => {
            setActiveId(null);
            reload({ silent: true });
            notify.success(msg);
          }}
        />
      )}
    </div>
  );
}

function SignaturePad({ onChange }: { onChange: (dataUrl: string | null) => void }) {
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

function ApproveModal({
  id,
  onClose,
  onDecided,
}: {
  id: string;
  onClose: () => void;
  onDecided: (message: string) => void;
}) {
  const { notify } = useAppNotifications();
  const { data, loading } = useApi<RequestDetail>(`/api/requests/${id}`);
  const [comment, setComment] = useState("");
  const [signature, setSignature] = useState<string | null>(null);
  const [busy, setBusy] = useState<"approve" | "reject" | null>(null);
  const [penaltyOutcome, setPenaltyOutcome] = useState<"waive" | "reduce">("waive");
  const [newAmount, setNewAmount] = useState("");

  // Khiếu nại án phạt: ở bước duyệt cuối, người duyệt chọn xử lý phạt (bác bỏ / giảm tiền).
  const isPenaltyAppeal = data?.request.type === "penalty_appeal";
  const isFinalStep = !!data && data.request.currentStep >= data.approvals.length;
  const showPenaltyDecision = isPenaltyAppeal && isFinalStep;
  const oldAmount = Number(data?.request.payload?.penaltyAmount ?? 0) || 0;

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
      title={data ? `${data.request.typeLabel} · ${data.request.requestNo}` : "Duyệt đơn"}
      panel
      wide
      footer={
        <>
          <Button variant="danger" onClick={() => decide(false)} loading={busy === "reject"}>
            <XCircle className="h-4 w-4" /> {isPenaltyAppeal ? "Từ chối khiếu nại" : "Từ chối"}
          </Button>
          <Button onClick={() => decide(true)} loading={busy === "approve"}>
            <CheckCircle2 className="h-4 w-4" /> Duyệt
          </Button>
        </>
      }
    >
      {loading || !data ? (
        <div className="py-10 text-center text-sm text-[var(--text-muted)]">Đang tải…</div>
      ) : (
        <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
          <div className="rounded-2xl border border-[var(--glass-border)] p-4">
            <div className="mb-2 text-sm font-bold text-[var(--text)]">{data.request.title}</div>
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between gap-3">
                <dt className="text-[var(--text-secondary)]">Người gửi</dt>
                <dd className="font-medium text-[var(--text)]">{data.request.employeeName} ({data.request.employeeCode})</dd>
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
                  <dd className="text-right font-medium text-[var(--text)]">{String(v)}</dd>
                </div>
              ))}
            </dl>
          </div>

          <div className="space-y-4">
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
          </div>
        </div>
      )}
    </Modal>
  );
}
