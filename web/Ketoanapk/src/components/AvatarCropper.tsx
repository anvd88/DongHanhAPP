import { useRef, useState } from "react";
import { Check, X, ZoomIn, ZoomOut, Trash2 } from "lucide-react";
import { Button } from "./ui";

const VIEW = 300; // kích thước khung xem (px)
const OUT = 256; // kích thước ảnh xuất (px)
const MAX_ZOOM = 3;

interface Point {
  x: number;
  y: number;
}

/**
 * Khung chỉnh ảnh đại diện kiểu Facebook: kéo để di chuyển ảnh, kéo thanh trượt
 * (hoặc cuộn chuột) để phóng to/thu nhỏ bên trong vòng tròn, rồi xuất ảnh vuông
 * đã cắt thành data URL JPEG.
 */
export function AvatarCropper({
  src,
  onCancel,
  onDone,
  onDelete,
}: {
  src: string;
  onCancel: () => void;
  onDone: (dataUrl: string) => void;
  /** Xóa ảnh đại diện (hiển thị nút "Xóa ảnh" trong khung căn chỉnh). */
  onDelete?: () => void;
}) {
  const imgRef = useRef<HTMLImageElement>(null);
  const natural = useRef({ w: 0, h: 0 });
  const coverScale = useRef(1); // tỉ lệ tối thiểu để ảnh phủ kín khung (zoom = 1)
  const drag = useRef<{ x: number; y: number; ox: number; oy: number } | null>(null);

  const [ready, setReady] = useState(false);
  const [grabbing, setGrabbing] = useState(false);
  const [zoom, setZoom] = useState(1);
  const [offset, setOffset] = useState<Point>({ x: 0, y: 0 });

  const scale = coverScale.current * zoom; // px hiển thị / px gốc

  // Giữ ảnh luôn phủ kín khung: góc trên-trái không vượt quá 0 và không lùi quá mép phải/dưới.
  const clamp = (o: Point, s: number): Point => {
    const dispW = natural.current.w * s;
    const dispH = natural.current.h * s;
    return {
      x: Math.min(0, Math.max(VIEW - dispW, o.x)),
      y: Math.min(0, Math.max(VIEW - dispH, o.y)),
    };
  };

  const onImgLoad = () => {
    const el = imgRef.current;
    if (!el) return;
    natural.current = { w: el.naturalWidth, h: el.naturalHeight };
    coverScale.current = Math.max(VIEW / el.naturalWidth, VIEW / el.naturalHeight);
    const s = coverScale.current;
    setZoom(1);
    setOffset({ x: (VIEW - el.naturalWidth * s) / 2, y: (VIEW - el.naturalHeight * s) / 2 });
    setReady(true);
  };

  // Phóng to/thu nhỏ quanh tâm khung (điểm gốc dưới tâm giữ nguyên).
  const applyZoom = (nextZoom: number) => {
    const z = Math.min(MAX_ZOOM, Math.max(1, nextZoom));
    const sOld = coverScale.current * zoom;
    const sNew = coverScale.current * z;
    const cx = (VIEW / 2 - offset.x) / sOld;
    const cy = (VIEW / 2 - offset.y) / sOld;
    setZoom(z);
    setOffset(clamp({ x: VIEW / 2 - cx * sNew, y: VIEW / 2 - cy * sNew }, sNew));
  };

  const onPointerDown = (e: React.PointerEvent) => {
    (e.currentTarget as Element).setPointerCapture(e.pointerId);
    drag.current = { x: e.clientX, y: e.clientY, ox: offset.x, oy: offset.y };
    setGrabbing(true);
  };
  const onPointerMove = (e: React.PointerEvent) => {
    if (!drag.current) return;
    setOffset(
      clamp(
        { x: drag.current.ox + (e.clientX - drag.current.x), y: drag.current.oy + (e.clientY - drag.current.y) },
        scale,
      ),
    );
  };
  const endDrag = (e: React.PointerEvent) => {
    drag.current = null;
    setGrabbing(false);
    try {
      (e.currentTarget as Element).releasePointerCapture(e.pointerId);
    } catch {
      /* con trỏ đã nhả */
    }
  };

  const onWheel = (e: React.WheelEvent) => {
    applyZoom(zoom - e.deltaY * 0.0016 * MAX_ZOOM);
  };

  const confirm = () => {
    const el = imgRef.current;
    if (!el) return;
    const canvas = document.createElement("canvas");
    canvas.width = OUT;
    canvas.height = OUT;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    const srcSize = VIEW / scale; // vùng ảnh gốc nằm trong khung
    ctx.drawImage(el, -offset.x / scale, -offset.y / scale, srcSize, srcSize, 0, 0, OUT, OUT);
    onDone(canvas.toDataURL("image/jpeg", 0.85));
  };

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center p-4"
      style={{ background: "rgba(8,12,20,0.6)", backdropFilter: "blur(4px)", WebkitBackdropFilter: "blur(4px)" }}
      onClick={onCancel}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="glass glass-strong profile-modal-strong fade-in flex w-full max-w-md flex-col gap-4 rounded-2xl p-5"
      >
        <div className="flex items-center justify-between">
          <h3 className="text-base font-bold text-[var(--text)]">Chỉnh ảnh đại diện</h3>
          <button
            type="button"
            onClick={onCancel}
            className="rounded-lg p-1.5 text-[var(--text-muted)] transition-colors hover:bg-black/5 dark:hover:bg-white/10"
            aria-label="Đóng"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div
          className="relative mx-auto touch-none select-none overflow-hidden rounded-xl bg-black/40"
          style={{ width: VIEW, height: VIEW, cursor: grabbing ? "grabbing" : "grab" }}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={endDrag}
          onPointerCancel={endDrag}
          onWheel={onWheel}
        >
          <img
            ref={imgRef}
            src={src}
            alt=""
            draggable={false}
            onLoad={onImgLoad}
            className="pointer-events-none absolute max-w-none select-none"
            style={{
              left: offset.x,
              top: offset.y,
              width: natural.current.w * scale,
              height: natural.current.h * scale,
              opacity: ready ? 1 : 0,
            }}
          />
          {/* Làm mờ vùng ngoài vòng tròn + viền trắng (khung bị cha overflow-hidden cắt gọn). */}
          <div
            className="pointer-events-none absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 rounded-full"
            style={{ width: VIEW, height: VIEW, boxShadow: "0 0 0 9999px rgba(0,0,0,0.5)", border: "2px solid rgba(255,255,255,0.9)" }}
          />
        </div>

        <div className="flex items-center gap-3">
          <ZoomOut className="h-4 w-4 shrink-0 text-[var(--text-muted)]" />
          <input
            type="range"
            min={1}
            max={MAX_ZOOM}
            step={0.01}
            value={zoom}
            onChange={(e) => applyZoom(parseFloat(e.target.value))}
            className="flex-1 accent-[var(--accent)]"
            aria-label="Phóng to ảnh"
          />
          <ZoomIn className="h-4 w-4 shrink-0 text-[var(--text-muted)]" />
        </div>

        <p className="text-center text-xs text-[var(--text-muted)]">Kéo ảnh để chỉnh vị trí · cuộn chuột hoặc kéo thanh để phóng to</p>

        <div className="flex items-center gap-2">
          {onDelete && (
            <button
              type="button"
              onClick={onDelete}
              className="flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium text-[var(--danger)] transition-colors hover:bg-red-500/10"
            >
              <Trash2 className="h-4 w-4" />
              Xóa ảnh
            </button>
          )}
          <div className="ml-auto flex gap-2">
            <Button variant="ghost" onClick={onCancel}>Hủy</Button>
            <Button onClick={confirm}>
              <Check className="h-4 w-4" />
              Áp dụng
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
