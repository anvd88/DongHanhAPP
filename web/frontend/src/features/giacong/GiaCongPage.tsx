import { useEffect, useMemo, useRef, useState } from "react";
import { MotionConfig, motion } from "motion/react";
import { ArrowDownToLine, ArrowUpFromLine, RefreshCw } from "lucide-react";
import { TooltipProvider } from "../../shadcn/tooltip";
import { Button } from "../../shadcn/button";
import { useAppNotifications } from "../../components/AppNotifications";
import { useApi } from "../../lib/useApi";
import { api } from "../../lib/api";
import type { GiaCongListItem } from "../../lib/types";
import { StatsRow } from "./StatsRow";
import { Toolbar } from "./Toolbar";
import { PhieuTable } from "./PhieuTable";
import { EditorDialog, LOAI_NHAP, LOAI_XUAT, type LoaiGiaCong } from "./EditorDialog";
import "./giacong.css";

const EASE_IOS = [0.22, 1, 0.36, 1] as const;

const FORCE_FULL_MOTION =
  import.meta.env.DEV &&
  typeof localStorage !== "undefined" &&
  localStorage.getItem("force-full-motion") === "true";

export function GiaCongPage() {
  const { notify, confirm } = useAppNotifications();
  const [filter, setFilter] = useState("all");
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<number | "new" | null>(null);
  const [seedId, setSeedId] = useState<number | undefined>(undefined);
  const [initialLoaiPhieu, setInitialLoaiPhieu] = useState<LoaiGiaCong>(LOAI_XUAT);
  const [refreshing, setRefreshing] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);

  const { data, loading, error, reload } = useApi<GiaCongListItem[]>("/api/giacong/?filter=all&search=");
  const rows = useMemo(() => data ?? [], [data]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return rows.filter((row) => {
      const loai = row.loaiPhieu.toLowerCase();
      if (filter === "xuat" && !loai.includes("xuất") && !row.soLuongXuat) return false;
      if (filter === "nhap" && !loai.includes("nhập") && !row.soLuongNhap) return false;
      if (q && !`${row.maPhieu} ${row.doiTac}`.toLowerCase().includes(q)) return false;
      return true;
    });
  }, [rows, filter, search]);

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

  const openNew = (loai: LoaiGiaCong) => {
    setInitialLoaiPhieu(loai);
    setSeedId(undefined);
    setEditing("new");
  };

  const openEdit = (id: number) => {
    setSeedId(undefined);
    setEditing(id);
  };

  const openDuplicate = (id: number) => {
    setInitialLoaiPhieu(LOAI_XUAT);
    setSeedId(id);
    setEditing("new");
  };

  const closeEditor = () => setEditing(null);

  const handleDelete = async (id: number) => {
    const ok = await confirm({
      title: "Xóa phiếu gia công?",
      description: "Phiếu gia công này sẽ bị xóa khỏi danh sách.",
      confirmLabel: "Xóa",
      tone: "danger",
    });
    if (!ok) return;
    try {
      await api.del(`/api/giacong/${id}`);
      reload();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được phiếu gia công");
    }
  };

  return (
    <MotionConfig reducedMotion={FORCE_FULL_MOTION ? "never" : "user"}>
      <TooltipProvider delayDuration={200}>
        <div className="gc-root space-y-4 pb-6">
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
                Theo dõi xuất đi, nhập về và phí gia công phải trả
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
              <Button variant="soft" onClick={() => openNew(LOAI_XUAT)}>
                <ArrowUpFromLine className="h-4 w-4" /> Xuất gia công
              </Button>
              <Button onClick={() => openNew(LOAI_NHAP)}>
                <ArrowDownToLine className="h-4 w-4" /> Nhập gia công
              </Button>
            </motion.div>
          </div>

          <StatsRow rows={rows} />

          <Toolbar filter={filter} onFilter={setFilter} search={search} onSearch={setSearch} searchRef={searchRef} />

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
          initialLoaiPhieu={initialLoaiPhieu}
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
