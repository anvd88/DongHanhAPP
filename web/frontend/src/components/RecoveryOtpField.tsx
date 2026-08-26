import { useEffect, useLayoutEffect, useRef, useState, type ClipboardEvent, type KeyboardEvent } from "react";
import { motion } from "motion/react";
import "./recovery-otp.css";

/**
 * Ô nhập mã khôi phục kiểu OTP: mỗi ký tự một ô, gõ xong tự nhảy ô, đủ ký tự thì tự xác thực
 * (không có nút Xác nhận). Đang xác thực: các ô rời hàng ngang, xếp quanh tâm và quay chậm một
 * vòng (chữ số xoay ngược lại nên luôn đứng thẳng). Sai mã: rung rồi VỠ thành mảnh văng ra, chỗ
 * đó hiện lại hàng ô trống mới.
 */
export type OtpStatus = "idle" | "verifying" | "success" | "error";

const GAP = 12;
const MAX_BOX = 58;
const MIN_BOX = 40;
/** Một vòng quay của cụm ô lúc đang xác thực (giây) — chậm rãi, không gây cảm giác giục giã. */
const ORBIT_SECONDS = 7.5;
const SHAKE_MS = 380;

/**
 * 5 mảnh vỡ KHÔNG đều nhau, cắt từ một điểm nứt lệch tâm (44%, 52%) và có nếp gãy trên đường nứt —
 * ghép lại vẫn kín đúng hình vuông ban đầu, nhưng nhìn ra vết nứt kính chứ không phải "chia làm 4".
 * drift = dạt ngang, lift = nảy lên lúc vỡ, fall = rơi xuống (nhanh dần), rotate = xoay khi rơi.
 */
const SHARDS = [
  { clip: "polygon(44% 52%, 26% 22%, 0% 0%, 0% 30%, 20% 46%)", drift: -16, lift: -21, fall: 134, rotate: -54 },
  { clip: "polygon(44% 52%, 26% 22%, 0% 0%, 38% 0%, 36% 27%)", drift: -4, lift: -24, fall: 142, rotate: -28 },
  { clip: "polygon(44% 52%, 36% 27%, 38% 0%, 100% 0%, 100% 42%, 74% 42%)", drift: 15, lift: -11, fall: 116, rotate: 33 },
  { clip: "polygon(44% 52%, 74% 42%, 100% 42%, 100% 100%, 56% 100%, 54% 74%)", drift: 21, lift: -8, fall: 122, rotate: 45 },
  { clip: "polygon(44% 52%, 54% 74%, 56% 100%, 0% 100%, 0% 30%, 20% 46%)", drift: -11, lift: -14, fall: 128, rotate: -24 },
];

type Props = {
  digits: string[];
  onDigitsChange: (next: string[]) => void;
  status: OtpStatus;
  /** Đổi giá trị này để ép con trỏ về ô trống đầu tiên (vào bước nhập mã, sau khi nhập sai…). */
  focusKey?: number;
  disabled?: boolean;
};

/** Ký tự hợp lệ của mã khôi phục (chữ HOA + số, xem Security/RecoveryCodes.cs). */
const sanitize = (raw: string) => raw.toUpperCase().replace(/[^0-9A-Z]/g, "");

export function RecoveryOtpField({ digits, onDigitsChange, status, focusKey = 0, disabled = false }: Props) {
  const length = digits.length;
  const stageRef = useRef<HTMLDivElement>(null);
  const inputsRef = useRef<Array<HTMLInputElement | null>>([]);
  const [stageWidth, setStageWidth] = useState(length * MAX_BOX + (length - 1) * GAP);
  // Sai mã đi hai nhịp: rung tại chỗ → vỡ thành mảnh. Tách riêng để hai nhịp không đè lên nhau.
  const [errorStage, setErrorStage] = useState<"none" | "shake" | "shatter">("none");
  const [poppedIndex, setPoppedIndex] = useState(-1);
  const locked = disabled || status !== "idle";
  const shattered = errorStage === "shatter";

  useLayoutEffect(() => {
    const el = stageRef.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    // Các ô định vị tuyệt đối nên không có vòng lặp đo — bề rộng sân khấu chỉ phụ thuộc thẻ cha.
    const observer = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect.width ?? 0;
      if (width > 0) setStageWidth(width);
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (status !== "error") { setErrorStage("none"); return; }
    setErrorStage("shake");
    const id = window.setTimeout(() => setErrorStage("shatter"), SHAKE_MS);
    return () => window.clearTimeout(id);
  }, [status]);

  const box = Math.max(MIN_BOX, Math.min(MAX_BOX, (stageWidth - GAP * (length - 1)) / length));
  const radius = box * 1.28;

  const focusAt = (index: number) => {
    const target = inputsRef.current[Math.max(0, Math.min(length - 1, index))];
    target?.focus({ preventScroll: true });
    target?.select();
  };

  useEffect(() => {
    if (locked) return;
    const firstEmpty = digits.findIndex((d) => !d);
    focusAt(firstEmpty === -1 ? length - 1 : firstEmpty);
    // focusKey đổi = parent muốn đưa con trỏ về đầu (mở bước nhập mã / vừa xóa mã sai).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusKey, locked]);

  const writeAt = (index: number, chars: string) => {
    const next = [...digits];
    let cursor = index;
    for (const ch of chars) {
      if (cursor >= length) break;
      next[cursor] = ch;
      cursor += 1;
    }
    onDigitsChange(next);
    setPoppedIndex(cursor - 1);
    focusAt(cursor >= length ? length - 1 : cursor);
  };

  const handleChange = (index: number, raw: string) => {
    const chars = sanitize(raw);
    if (!chars) {
      const next = [...digits];
      next[index] = "";
      onDigitsChange(next);
      return;
    }
    // Gõ đè lên ô đã có ký tự: lấy ký tự MỚI (ký tự cuối) chứ không giữ ký tự cũ.
    writeAt(index, chars.length > 1 ? chars.slice(-(length - index)) : chars);
  };

  const handleKeyDown = (index: number, event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Backspace") {
      event.preventDefault();
      const next = [...digits];
      if (next[index]) {
        next[index] = "";
        onDigitsChange(next);
        return;
      }
      if (index > 0) {
        next[index - 1] = "";
        onDigitsChange(next);
        focusAt(index - 1);
      }
      return;
    }
    if (event.key === "ArrowLeft") { event.preventDefault(); focusAt(index - 1); }
    else if (event.key === "ArrowRight") { event.preventDefault(); focusAt(index + 1); }
  };

  const handlePaste = (index: number, event: ClipboardEvent<HTMLInputElement>) => {
    const chars = sanitize(event.clipboardData.getData("text"));
    if (!chars) return;
    event.preventDefault();
    writeAt(index, chars.slice(0, length - index));
  };

  const rowX = (index: number) => (index - (length - 1) / 2) * (box + GAP);

  const positionOf = (index: number) => {
    if (status === "success") return { x: 0, y: 0 };
    if (status === "verifying") {
      // Xếp đều quanh tâm: ô đầu tiên lên đỉnh, các ô còn lại chia đều vòng tròn.
      const angle = (-90 + (index * 360) / length) * (Math.PI / 180);
      return { x: Math.cos(angle) * radius, y: Math.sin(angle) * radius };
    }
    return { x: rowX(index), y: 0 };
  };

  const clustered = status === "verifying" || status === "success";
  const spinning = status === "verifying";
  const cursorIndex = digits.findIndex((d) => !d);
  const orbitTransition = spinning
    ? { duration: ORBIT_SECONDS, ease: "linear" as const, repeat: Infinity }
    : { duration: 0.5, ease: [0.22, 1, 0.36, 1] as const };

  return (
    <div className="recovery-otp" data-status={status} data-stage={errorStage}>
      <motion.div
        ref={stageRef}
        className="recovery-otp-stage"
        animate={{ height: clustered ? radius * 2 + box + 10 : box + 6 }}
        transition={{ type: "spring", stiffness: 210, damping: 28 }}
      >
        <motion.div
          className="recovery-otp-orbit"
          animate={{ rotate: spinning || status === "success" ? 360 : 0 }}
          transition={orbitTransition}
        >
          {digits.map((digit, index) => {
            const active = status === "idle" && index === cursorIndex;
            return (
              <motion.div
                key={index}
                className="recovery-otp-cell"
                data-filled={digit ? "true" : "false"}
                data-active={active ? "true" : "false"}
                style={{ width: box, height: box, marginLeft: -box / 2, marginTop: -box / 2 }}
                animate={{
                  ...positionOf(index),
                  rotate: spinning || status === "success" ? -360 : 0,
                  scale: status === "success"
                    ? 0.34
                    : shattered
                      ? 0.9
                      : poppedIndex === index
                        ? [1, 1.07, 1]
                        : active ? 1.04 : 1,
                  opacity: status === "success" || shattered ? 0 : 1,
                }}
                transition={{
                  x: { type: "spring", stiffness: 240, damping: 24, delay: clustered ? index * 0.045 : 0 },
                  y: { type: "spring", stiffness: 240, damping: 24, delay: clustered ? index * 0.045 : 0 },
                  rotate: orbitTransition,
                  scale: { type: "spring", stiffness: 420, damping: 26 },
                  opacity: {
                    duration: shattered ? 0.01 : 0.42,
                    delay: !clustered && !shattered ? index * 0.05 : 0,
                  },
                }}
              >
                <input
                  ref={(el) => { inputsRef.current[index] = el; }}
                  className="recovery-otp-input"
                  style={{ fontSize: Math.round(box * 0.46) }}
                  value={digit}
                  onChange={(event) => handleChange(index, event.target.value)}
                  onKeyDown={(event) => handleKeyDown(index, event)}
                  onPaste={(event) => handlePaste(index, event)}
                  onFocus={(event) => event.currentTarget.select()}
                  inputMode="text"
                  autoComplete={index === 0 ? "one-time-code" : "off"}
                  autoCapitalize="characters"
                  autoCorrect="off"
                  spellCheck={false}
                  maxLength={2}
                  disabled={locked}
                  aria-label={`Ký tự thứ ${index + 1} của mã khôi phục`}
                />
              </motion.div>
            );
          })}
        </motion.div>

        {/* Mảnh vỡ: mỗi ô tách thành 4 mảnh tam giác văng ra rồi tan. */}
        {shattered && (
          <div className="recovery-otp-debris" aria-hidden="true">
            {digits.map((digit, index) => SHARDS.map((shard, part) => (
              <motion.span
                key={`${index}-${part}`}
                className="recovery-otp-shard"
                style={{
                  width: box,
                  height: box,
                  marginLeft: -box / 2,
                  marginTop: -box / 2,
                  clipPath: shard.clip,
                  fontSize: Math.round(box * 0.46),
                }}
                initial={{ x: rowX(index), y: 0, rotate: 0, opacity: 1, scale: 1 }}
                animate={{
                  // Nảy lên một nhịp rồi rơi xuống — đoạn rơi dùng easeIn nên nhanh dần như có trọng lực.
                  x: [rowX(index), rowX(index) + shard.drift * 0.4, rowX(index) + shard.drift],
                  y: [0, shard.lift, shard.fall],
                  rotate: [0, shard.rotate * 0.3, shard.rotate],
                  scale: [1, 0.97, 0.86],
                  opacity: [1, 1, 0],
                }}
                transition={{
                  duration: 0.82,
                  times: [0, 0.2, 1],
                  ease: ["easeOut", "easeIn"],
                  // Nứt xong đứng yên một nhịp rồi mới rơi — mắt kịp thấy vết nứt trước khi mảnh rời ra.
                  delay: 0.1 + index * 0.035,
                }}
              >
                {digit}
              </motion.span>
            )))}
          </div>
        )}

        {status === "verifying" && (
          <span className="recovery-otp-core" aria-hidden="true">
            <svg viewBox="0 0 44 44">
              <circle className="recovery-otp-core-track" cx="22" cy="22" r="17" />
              <circle className="recovery-otp-core-arc" cx="22" cy="22" r="17" />
            </svg>
          </span>
        )}

        {status === "success" && (
          <span className="recovery-otp-seal" aria-hidden="true">
            <svg viewBox="0 0 56 56">
              <circle className="recovery-otp-seal-disc" cx="28" cy="28" r="26" />
              <path className="recovery-otp-seal-check" d="M17 28.5 L24.5 36 L39 21" />
            </svg>
          </span>
        )}
      </motion.div>
    </div>
  );
}
