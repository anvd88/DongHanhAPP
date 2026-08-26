import { Download } from "lucide-react";
import { ActionProgressButton, type ActionProgressButtonProps } from "./ActionProgressButton";

export type { ProgressReport } from "./ActionProgressButton";

type ExportExcelButtonProps = Omit<
  ActionProgressButtonProps,
  "onRun" | "icon" | "idleLabel" | "busyLabel" | "doneLabel"
> & {
  /** Việc xuất file thật; nhận hàm báo tiến trình. Trả về `false` = bỏ qua (không có gì để xuất / lỗi). */
  onExport: ActionProgressButtonProps["onRun"];
  idleLabel?: string;
  busyLabel?: string;
  doneLabel?: string;
};

/** Nút xuất Excel: Download → spinner "Đang xuất..." + thanh tiến trình → ✓ "Đã xuất" → thu về. */
export function ExportExcelButton({
  onExport,
  idleLabel = "Xuất Excel",
  busyLabel = "Đang xuất...",
  doneLabel = "Đã xuất",
  ...rest
}: ExportExcelButtonProps) {
  return (
    <ActionProgressButton
      {...rest}
      onRun={onExport}
      icon={Download}
      idleLabel={idleLabel}
      busyLabel={busyLabel}
      doneLabel={doneLabel}
    />
  );
}
