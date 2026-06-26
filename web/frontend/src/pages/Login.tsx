import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Calculator, LogIn, Lock, ScanFace, User } from "lucide-react";
import { GlassCard } from "../components/Glass";
import { Button } from "../components/ui";
import { useAuth } from "../lib/auth";
import { useTheme } from "../lib/theme";
import { APP_BRAND_NAME } from "../lib/branding";

export function Login() {
  const { login } = useAuth();
  const { theme, toggle } = useTheme();
  const nav = useNavigate();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError("");
    setLoading(true);
    try {
      await login(username, password);
      nav("/dashboard");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Đăng nhập thất bại.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex h-screen items-center justify-center p-4">
      <GlassCard strong className="fade-in w-full max-w-md p-8">
        <div className="mb-7 text-center">
          <div
            className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-3xl text-2xl font-black text-white shadow-xl"
            style={{ background: "linear-gradient(135deg, var(--accent), var(--purple))" }}
          >
            CP
          </div>
          <h1 className="km-login-brand-title text-2xl font-bold text-[var(--text)]">{APP_BRAND_NAME}</h1>
          <p className="mt-1 text-sm text-[var(--text-secondary)]">Phần mềm kế toán</p>
        </div>

        <form onSubmit={submit} className="space-y-4">
          <div>
            <label className="mb-1.5 block text-xs font-semibold text-[var(--text-secondary)]">Tên đăng nhập</label>
            <div className="relative">
              <User className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
              <input
                autoFocus
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                className="w-full rounded-xl border border-[var(--glass-border)] bg-white/55 dark:bg-white/5 py-3 pl-10 pr-3 text-sm outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
                placeholder="Nhập tên đăng nhập"
              />
            </div>
          </div>
          <div>
            <label className="mb-1.5 block text-xs font-semibold text-[var(--text-secondary)]">Mật khẩu</label>
            <div className="relative">
              <Lock className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="w-full rounded-xl border border-[var(--glass-border)] bg-white/55 dark:bg-white/5 py-3 pl-10 pr-3 text-sm outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
                placeholder="Nhập mật khẩu"
              />
            </div>
          </div>

          {error && (
            <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>
          )}

          <Button type="submit" loading={loading} className="w-full py-3">
            <LogIn className="h-4 w-4" /> Đăng nhập
          </Button>
        </form>

        <Link
          to="/kiosk"
          className="mt-3 flex w-full items-center justify-center gap-2 rounded-xl border border-[var(--glass-border)] bg-white/40 py-3 text-sm font-semibold text-[var(--text)] transition-all hover:border-[var(--accent)] dark:bg-white/5"
        >
          <ScanFace className="h-4 w-4" /> Chấm công
        </Link>

        <Link
          to="/tinh-toan"
          className="mt-3 flex w-full items-center justify-center gap-2 rounded-xl border border-[var(--glass-border)] bg-white/40 py-3 text-sm font-semibold text-[var(--text)] transition-all hover:border-[var(--accent)] dark:bg-white/5"
        >
          <Calculator className="h-4 w-4" /> Tính toán
        </Link>

        <button
          onClick={toggle}
          className="mx-auto mt-6 block text-xs text-[var(--text-muted)] hover:text-[var(--accent)]"
        >
          Chuyển sang giao diện {theme === "light" ? "tối" : "sáng"}
        </button>
      </GlassCard>
    </div>
  );
}
