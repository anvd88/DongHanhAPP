import { useMemo, useRef, useState } from "react";
import { Eye, Maximize2, Printer, ZoomIn } from "lucide-react";
import { Modal } from "./Modal";
import { ActionProgressButton, type ProgressReport } from "./ActionProgressButton";
import { Button, buttonClasses, buttonInlineStyle } from "./ui";

export function PrintPreviewModal({
  title,
  html,
  src,
  printing,
  printLabel = "In phiếu",
  onClose,
  onPrint,
}: {
  title: string;
  html?: string;
  src?: string;
  printing?: boolean;
  printLabel?: string;
  onClose: () => void;
  /**
   * Việc in thật. Nhận thêm hàm báo tiến trình: gọi `report(đã_xong, tổng)` ở từng mốc CÓ THẬT,
   * không gọi thì thanh chạy kiểu không xác định. Trả về `false` khi in hỏng để nút không báo xong.
   */
  onPrint: (frame: HTMLIFrameElement | null, report: ProgressReport) => unknown | Promise<unknown>;
}) {
  const frameRef = useRef<HTMLIFrameElement>(null);
  const [pdfView, setPdfView] = useState<"page" | "width">("page");
  const displayedSrc = useMemo(() => {
    if (!src) return undefined;
    const baseSrc = src.split("#", 1)[0];
    return `${baseSrc}#view=${pdfView === "page" ? "Fit" : "FitH"}&toolbar=0&navpanes=0`;
  }, [pdfView, src]);

  return (
    <Modal
      open
      wide
      solid
      fullScreen
      title={title}
      onClose={onClose}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={printing}>
            Đóng
          </Button>
          <ActionProgressButton
            onRun={(report) => onPrint(frameRef.current, report)}
            icon={Printer}
            idleLabel={printLabel}
            busyLabel="Đang in..."
            doneLabel="Đã in"
            // Không truyền `printing` vào `disabled`: nút tự khoá khi đang chạy, còn `disabled` sẽ
            // làm mờ nút 50% ngay giữa lúc hiệu ứng đang chạy.
            className={buttonClasses("primary")}
            style={buttonInlineStyle("primary")}
          />
        </>
      }
    >
      <div className="flex h-full min-h-0 flex-col gap-3">
        <div className="flex shrink-0 flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2 text-sm font-semibold text-[var(--text-secondary)]">
            <Eye className="h-4 w-4" />
            {src
              ? pdfView === "page"
                ? "Đang hiển thị trọn toàn bộ phiếu."
                : "Đang phóng vừa chiều ngang để đọc rõ chữ; cuộn xuống để xem tiếp."
              : "Kiểm tra nội dung bên dưới trước khi in."}
          </div>
          {src && (
            <div className="flex rounded-xl border border-[var(--glass-border)] bg-[var(--surface)] p-1">
              <Button
                variant={pdfView === "page" ? "primary" : "ghost"}
                onClick={() => setPdfView("page")}
              >
                <Maximize2 className="h-4 w-4" />
                Toàn phiếu
              </Button>
              <Button
                variant={pdfView === "width" ? "primary" : "ghost"}
                onClick={() => setPdfView("width")}
              >
                <ZoomIn className="h-4 w-4" />
                Đọc rõ chữ
              </Button>
            </div>
          )}
        </div>
        <iframe
          ref={frameRef}
          title={title}
          src={displayedSrc}
          srcDoc={src ? undefined : html}
          className="min-h-0 w-full flex-1 rounded-xl border border-[var(--glass-border)] bg-white"
        />
      </div>
    </Modal>
  );
}
