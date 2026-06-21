import { motion, useReducedMotion } from "framer-motion";
import { FaceScanIcon } from "./FaceScanIcon";
import { ScanButton } from "./ScanButton";

type AttendanceGlassCardProps = {
  onStart: () => void;
};

/** Card kính lỏng trung tâm: icon khuôn mặt + tiêu đề 2 dòng + mô tả + nút quét. */
export function AttendanceGlassCard({ onStart }: AttendanceGlassCardProps) {
  const reduceMotion = useReducedMotion();

  return (
    <motion.section
      className="att-card"
      initial={reduceMotion ? false : { opacity: 0, y: 26 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.6, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className="att-card-inner">
        <span className="att-face-badge" aria-hidden="true">
          <FaceScanIcon className="att-face-badge-icon" />
        </span>
        <h2 className="att-card-title">
          Chấm công
          <br />
          khuôn mặt
        </h2>
        <p className="att-card-desc">Sẵn sàng ghi nhận công</p>
        <ScanButton onClick={onStart} />
      </div>
    </motion.section>
  );
}
