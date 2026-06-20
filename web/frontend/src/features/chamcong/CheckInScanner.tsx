import { useCallback, useEffect, useRef, useState } from "react";
import { CheckCircle2, ScanFace, XCircle } from "lucide-react";
import { api } from "../../lib/api";
import type { NhanDienResult } from "../../lib/types";
import { CameraPanel } from "./CameraPanel";

/**
 * Khối quét chấm công dùng chung cho tab "Chấm công" trong app và màn hình kiosk
 * ngoài trang đăng nhập. Tự động chụp + thông báo người vừa được nhận diện (Vào/Ra).
 */
export function CheckInScanner() {
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<NhanDienResult | null>(null);
  const [auto, setAuto] = useState(true);
  const [cooldown, setCooldown] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);

  const recognize = useCallback(async (image: string) => {
    setBusy(true);
    try {
      const res = await api.post<NhanDienResult>("/api/chamcong/nhandien", { imageBase64: image });
      setResult(res);
      clearTimeout(timer.current);
      if (res.matched) {
        // Tạm dừng tự động chụp để hiện kết quả + tránh chấm trùng liên tục.
        setCooldown(true);
        timer.current = setTimeout(() => {
          setCooldown(false);
          setResult(null);
        }, 4000);
      } else {
        // Không khớp / không thấy mặt: xóa thông báo sau giây lát rồi quét tiếp.
        timer.current = setTimeout(() => setResult(null), 2500);
      }
    } catch (e) {
      setResult({ matched: false, similarity: 0, message: e instanceof Error ? e.message : "Lỗi nhận diện." });
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => () => clearTimeout(timer.current), []);

  return (
    <div className="cc-grid">
      <div className="space-y-3">
        <CameraPanel
          onCapture={recognize}
          busy={busy}
          auto={auto}
          paused={cooldown}
          captureLabel="Chụp & chấm công"
        />
        <label className="cc-auto-toggle">
          <input type="checkbox" checked={auto} onChange={(e) => setAuto(e.target.checked)} />
          Tự động chụp khi có người trước camera
        </label>
      </div>

      <div className="cc-result glass">
        {!result ? (
          <div className="cc-result-empty">
            <ScanFace className="h-10 w-10" />
            <p>
              {auto
                ? "Bật camera — hệ thống tự nhận diện khi bạn nhìn vào."
                : "Bật camera và bấm “Chụp & chấm công”."}
            </p>
          </div>
        ) : result.matched ? (
          <div className="cc-result-ok">
            <CheckCircle2 className="h-12 w-12" />
            <div className="cc-result-name">{result.fullName || result.username}</div>
            <div className="cc-result-badge" data-loai={result.loai}>{result.loai}</div>
            <div className="cc-result-meta">Độ khớp {(result.similarity * 100).toFixed(1)}%</div>
          </div>
        ) : (
          <div className="cc-result-fail">
            <XCircle className="h-12 w-12" />
            <p>{result.message}</p>
          </div>
        )}
      </div>
    </div>
  );
}
