import { useState, type FormEvent } from "react";
import { KeySquare, Lock, User, X } from "lucide-react";
import { GlassCard } from "./Glass";
import { Button } from "./ui";
import { api } from "../lib/api";

/**
 * Khôi phục mật khẩu bằng MÃ do admin cấp (thay cho reset bằng khuôn mặt). Người dùng nhập tên đăng
 * nhập + mã khôi phục (admin đưa) + mật khẩu mới. Thành công thì mọi phiên cũ bị thu hồi.
 */
export function RecoveryResetModal({ onClose }: { onClose: () => void }) {
  const [username, setUsername] = useState("");
  const [code, setCode] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState("");
  const [done, setDone] = useState(false);
  const [loading, setLoading] = useState(false);

  const inputCls =
    "w-full rounded-xl border border-[var(--glass-border)] bg-white/55 dark:bg-white/5 py-3 pl-10 pr-3 text-sm outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]";

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError("");
    if (password.length < 6) { setError("Mật khẩu mới cần ít nhất 6 ký tự."); return; }
    if (password !== confirm) { setError("Xác nhận mật khẩu không khớp."); return; }
    setLoading(true);
    try {
      await api.post("/api/auth/reset-with-recovery-code", { username: username.trim(), code, newPassword: password });
      setDone(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không đặt lại được mật khẩu.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div className="w-full max-w-md" onClick={(e) => e.stopPropagation()}>
      <GlassCard strong className="fade-in w-full p-7">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="flex items-center gap-2 text-lg font-bold text-[var(--text)]">
            <KeySquare className="h-5 w-5 text-[var(--accent)]" /> Quên mật khẩu
          </h2>
          <button type="button" onClick={onClose} className="text-[var(--text-muted)] hover:text-[var(--text)]">
            <X className="h-5 w-5" />
          </button>
        </div>

        {done ? (
          <div className="space-y-4">
            <div className="rounded-xl bg-green-500/10 px-3 py-3 text-sm font-medium text-[var(--success,#16a34a)]">
              Đã đặt lại mật khẩu. Hãy đăng nhập bằng mật khẩu mới.
            </div>
            <Button type="button" className="w-full py-3" onClick={onClose}>Đóng</Button>
          </div>
        ) : (
          <form onSubmit={submit} className="space-y-4">
            <p className="text-xs text-[var(--text-secondary)]">
              Nhập mã khôi phục do quản trị viên cấp cho bạn để đặt lại mật khẩu.
            </p>
            <div className="relative">
              <User className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
              <input value={username} onChange={(e) => setUsername(e.target.value)} className={inputCls} placeholder="Tên đăng nhập" autoFocus />
            </div>
            <div className="relative">
              <KeySquare className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
              <input value={code} onChange={(e) => setCode(e.target.value)} className={`${inputCls} tracking-widest`} placeholder="Mã khôi phục (VD: ABCD-EFGH-JKMN)" />
            </div>
            <div className="relative">
              <Lock className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
              <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} className={inputCls} placeholder="Mật khẩu mới" />
            </div>
            <div className="relative">
              <Lock className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
              <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} className={inputCls} placeholder="Xác nhận mật khẩu mới" />
            </div>

            {error && <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>}

            <Button type="submit" loading={loading} className="w-full py-3">Đặt lại mật khẩu</Button>
          </form>
        )}
      </GlassCard>
      </div>
    </div>
  );
}
