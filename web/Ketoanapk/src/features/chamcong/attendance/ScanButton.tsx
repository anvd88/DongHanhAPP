import { Camera } from "lucide-react";

type ScanButtonProps = {
  onClick: () => void;
};

/** Nút kính lỏng xanh chính — bắt đầu quét khuôn mặt. */
export function ScanButton({ onClick }: ScanButtonProps) {
  return (
    <button type="button" className="att-scan-btn" onClick={onClick}>
      <Camera className="att-scan-cam" aria-hidden="true" />
      <span>Bắt đầu quét</span>
    </button>
  );
}
