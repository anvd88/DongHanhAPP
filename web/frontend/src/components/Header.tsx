import { useState } from "react";
import { Search, Moon, Sun, Bell, LogOut, ChevronDown, Menu } from "lucide-react";
import { useAuth } from "../lib/auth";
import { useTheme } from "../lib/theme";
import { initials } from "../lib/format";
import { isAdmin } from "../lib/types";

export function Header({ onMenu }: { onMenu: () => void }) {
  const { user, logout } = useAuth();
  const { theme, toggle } = useTheme();
  const [menuOpen, setMenuOpen] = useState(false);
  const now = new Date();

  return (
    <header className="glass glass-strong relative z-20 mx-4 mt-4 flex items-center gap-3 rounded-2xl px-4 py-3">
      <button onClick={onMenu} className="rounded-lg p-2 text-[var(--text-secondary)] hover:bg-black/5 dark:hover:bg-white/10 lg:hidden">
        <Menu className="h-5 w-5" />
      </button>

      {/* Tìm kiếm */}
      <div className="relative hidden flex-1 md:block">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
        <input
          placeholder="Nhập để tìm kiếm…"
          className="w-full max-w-md rounded-xl border border-[var(--glass-border)] bg-white/40 dark:bg-white/5 py-2 pl-9 pr-3 text-sm outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
        />
      </div>
      <div className="flex-1 md:hidden" />

      <div className="hidden text-right sm:block">
        <div className="text-xs font-semibold text-[var(--text-secondary)]">Kỳ kế toán</div>
        <div className="text-sm font-bold text-[var(--text)]">{String(now.getMonth() + 1).padStart(2, "0")}/{now.getFullYear()}</div>
      </div>

      <button onClick={toggle} className="rounded-xl p-2.5 text-[var(--text-secondary)] transition-colors hover:bg-black/5 dark:hover:bg-white/10" title="Đổi giao diện">
        {theme === "light" ? <Moon className="h-5 w-5" /> : <Sun className="h-5 w-5" />}
      </button>

      <button className="relative rounded-xl p-2.5 text-[var(--text-secondary)] transition-colors hover:bg-black/5 dark:hover:bg-white/10">
        <Bell className="h-5 w-5" />
      </button>

      {/* Người dùng */}
      <div className="relative">
        <button
          onClick={() => setMenuOpen((v) => !v)}
          className="flex items-center gap-2.5 rounded-xl py-1 pl-1 pr-2 transition-colors hover:bg-black/5 dark:hover:bg-white/10"
        >
          <div
            className="flex h-9 w-9 items-center justify-center rounded-full text-sm font-bold text-white"
            style={{ background: "linear-gradient(135deg, var(--accent), var(--purple))" }}
          >
            {initials(user?.fullName || user?.username || "?")}
          </div>
          <div className="hidden text-left leading-tight sm:block">
            <div className="text-sm font-semibold text-[var(--text)]">{user?.fullName || user?.username}</div>
            <div className="text-[11px] text-[var(--text-muted)]">{isAdmin(user) ? "Quản trị viên" : "Nhân viên"}</div>
          </div>
          <ChevronDown className="h-4 w-4 text-[var(--text-muted)]" />
        </button>

        {menuOpen && (
          <>
            <div className="fixed inset-0 z-10" onClick={() => setMenuOpen(false)} />
            <div className="glass glass-strong fade-in absolute right-0 z-20 mt-2 w-52 overflow-hidden rounded-2xl p-1.5">
              <div className="px-3 py-2 text-xs text-[var(--text-muted)]">
                Đăng nhập với <span className="font-semibold text-[var(--text-secondary)]">{user?.username}</span>
              </div>
              <button
                onClick={logout}
                className="flex w-full items-center gap-2 rounded-xl px-3 py-2.5 text-sm font-medium text-[var(--danger)] transition-colors hover:bg-red-500/10"
              >
                <LogOut className="h-4 w-4" /> Đăng xuất
              </button>
            </div>
          </>
        )}
      </div>
    </header>
  );
}
