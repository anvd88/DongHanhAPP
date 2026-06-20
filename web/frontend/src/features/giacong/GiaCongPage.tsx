import { useEffect, useMemo, useRef, useState } from "react";
import { MotionConfig, motion } from "motion/react";
import { Plus, RefreshCw } from "lucide-react";
import { TooltipProvider } from "../../shadcn/tooltip";
import { Button } from "../../shadcn/button";
import { useApi } from "../../lib/useApi";
import { api } from "../../lib/api";
import type { GiaCongListItem } from "../../lib/types";
import { StatsRow } from "./StatsRow";
import { Toolbar } from "./Toolbar";
import { PhieuTable } from "./PhieuTable";
import { EditorDialog } from "./EditorDialog";
import "./giacong.css";

const EASE_IOS = [0.22, 1, 0.36, 1] as const;

/**
 * Chế độ debug (chỉ DEV): cho xem animation đầy đủ dù trình duyệt/OS báo reduced-motion.
 * Bật:  localStorage.setItem("force-full-motion", "true"); location.reload();
 * Tắt:  localStorage.removeItem("force-full-motion"); location.reload();
 * Production luôn tôn trọng prefers-reduced-motion (accessibility).
 */
const FORCE_FULL_MOTION =
  import.meta.env.DEV &&
  typeof localStorage !== "undefined" &&
  localStorage.getItem("force-full-motion") === "true";

export function GiaCongPage() {
  const [filter, setFilter] = useState("all");
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("all");
  const [editing, setEditing] = useState<number | "new" | null>(null);
  const [seedId, setSeedId] = useState<number | undefined>(undefined);
  const [refreshing, setRefreshing] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);

  const { data, loading, error, reload } = useApi<GiaCongListItem[]>("/api/giacong/?filter=all&search=");
  const rows = useMemo(() => data ?? [], [data]);

  // Lọc + tìm kiếm phía client → tab/tìm tức thì, chỉ 1 lần gọi API.
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return rows.filter((r) => {
      if (filter === "xuat" && !r.loaiPhieu.toLowerCase().includes("xuất")) return false;
      if (filter === "nhap" && !r.loaiPhieu.toLowerCase().includes("nhập")) return false;
      if (filter === "dangxuly" && r.trangThai !== "Đang xử lý") return false;
      if (status !== "all" && r.trangThai !== status) return false;
      if (q && !`${r.maPhieu} ${r.doiTac}`.toLowerCase().includes(q)) return false;
      return true;
    });
  }, [rows, filter, search, status]);

  // Ctrl/Cmd + K → focus ô tìm kiếm.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        searchRef.current?.focus();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const handleRefresh = () => {
    setRefreshing(true);
    reload();
    window.setTimeout(() => setRefreshing(false), 650);
  };

  const openNew = () => {
    setSeedId(undefined);
    setEditing("new");
  };
  const openEdit = (id: number) => {
    setSeedId(undefined);
    setEditing(id);
  };
  const openDuplicate = (id: number) => {
    setSeedId(id);
    setEditing("new");
  };
  const closeEditor = () => setEditing(null);

  const handleDelete = async (id: number) => {
    if (!confirm("Xóa phiếu gia công này?")) return;
    await api.del(`/api/giacong/${id}`);
    reload();
  };

  return (
    <MotionConfig reducedMotion={FORCE_FULL_MOTION ? "never" : "user"}>
      <TooltipProvider delayDuration={200}>
        <div className="gc-root space-y-4 pb-6">
          {/* Tiêu đề trang */}
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div>
              <motion.h1
                initial={{ opacity: 0, y: 18, scale: 0.985, filter: "blur(10px)" }}
                animate={{ opacity: 1, y: 0, scale: 1, filter: "blur(0px)" }}
                transition={{ duration: 0.48, ease: EASE_IOS }}
                className="text-[1.6rem] font-black leading-tight text-[var(--gc-text)]"
              >
                Gia công
              </motion.h1>
              <motion.p
                initial={{ opacity: 0, y: 12, filter: "blur(8px)" }}
                animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
                transition={{ duration: 0.44, delay: 0.08, ease: EASE_IOS }}
                className="mt-1 text-sm font-semibold text-[var(--gc-text-soft)]"
              >
                Quản lý phiếu gia công xuất / nhập
              </motion.p>
            </div>
            <motion.div
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.4, delay: 0.14, ease: EASE_IOS }}
              className="flex items-center gap-2.5"
            >
              <Button variant="ghost" onClick={handleRefresh}>
                <RefreshCw className={refreshing ? "h-4 w-4 animate-spin" : "h-4 w-4"} /> Làm mới
              </Button>
              <Button onClick={openNew}>
                <Plus className="h-4 w-4" /> Tạo phiếu
              </Button>
            </motion.div>
          </div>

          {/* Thẻ thống kê */}
          <StatsRow rows={rows} />

          {/* Thanh công cụ */}
          <Toolbar
            filter={filter}
            onFilter={setFilter}
            search={search}
            onSearch={setSearch}
            status={status}
            onStatus={setStatus}
            searchRef={searchRef}
          />

          {/* Bảng phiếu */}
          <PhieuTable
            rows={filtered}
            loading={loading}
            error={error}
            onOpen={openEdit}
            onEdit={openEdit}
            onDelete={handleDelete}
            onDuplicate={openDuplicate}
          />
        </div>

        <EditorDialog
          open={editing !== null}
          id={editing ?? "new"}
          seedId={seedId}
          onClose={closeEditor}
          onSaved={() => {
            closeEditor();
            reload();
          }}
        />
      </TooltipProvider>
    </MotionConfig>
  );
}
