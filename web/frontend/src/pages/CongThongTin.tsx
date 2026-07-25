import { useEffect, useRef, useState } from "react";
import {
  Building2,
  CalendarClock,
  Eye,
  EyeOff,
  ImagePlus,
  Loader2,
  MapPin,
  Megaphone,
  Newspaper,
  Pencil,
  Pin,
  Plus,
  RefreshCw,
  Save,
  Trash2,
  X,
} from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Badge, Button, EmptyState, Field, Input } from "../components/ui";
import { api } from "../lib/api";
import { dateTime } from "../lib/format";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/app-notifications-context";

type PortalKind = "news" | "event";
type Tab = PortalKind | "about";

interface PortalPost {
  id: number;
  kind: PortalKind;
  title: string;
  summary: string;
  body: string;
  coverImage: string | null;
  location: string;
  eventAt: string | null;
  pinned: boolean;
  published: boolean;
  authorUsername: string;
  authorName: string;
  createdAt: string;
  updatedAt: string;
}

interface PortalAbout {
  title: string;
  content: string;
  coverImage: string | null;
  address: string;
  hotline: string;
  email: string;
  website: string;
  updatedAt: string;
}

const MAX_IMAGE_BYTES = 2_000_000; // ~2MB: ảnh lưu base64 trong DB nên giữ nhỏ.

const TABS: { key: Tab; label: string; icon: typeof Newspaper }[] = [
  { key: "news", label: "Tin tức / Thông báo", icon: Newspaper },
  { key: "event", label: "Sự kiện / Lịch", icon: CalendarClock },
  { key: "about", label: "Giới thiệu công ty", icon: Building2 },
];

function pad(n: number) {
  return String(n).padStart(2, "0");
}

/** ISO (UTC) → chuỗi cho <input type="datetime-local"> theo giờ địa phương. */
function toLocalInput(iso: string | null): string {
  if (!iso) return "";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

async function fileToDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(new Error("Không đọc được ảnh."));
    reader.readAsDataURL(file);
  });
}

export function CongThongTin() {
  const { notify, confirm } = useAppNotifications();
  const [tab, setTab] = useState<Tab>("news");
  const [editing, setEditing] = useState<PortalPost | "new" | null>(null);

  const postsPath = tab === "about" ? null : `/api/portal/posts?kind=${tab}`;
  const { data: posts, loading, error, reload } = useApi<PortalPost[]>(postsPath, [tab]);

  const remove = async (post: PortalPost) => {
    const ok = await confirm({
      title: "Xóa bài viết?",
      description: `"${post.title}" sẽ bị xóa vĩnh viễn khỏi cổng thông tin.`,
      confirmLabel: "Xóa",
      tone: "danger",
    });
    if (!ok) return;
    try {
      await api.del(`/api/portal/posts/${post.id}`);
      notify.success("Đã xóa bài viết.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được bài viết.");
    }
  };

  const togglePublished = async (post: PortalPost) => {
    try {
      await api.put(`/api/portal/posts/${post.id}`, { ...post, published: !post.published });
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không cập nhật được trạng thái.");
    }
  };

  return (
    <div className="gc-root">
      <PageHeader
        title="Cổng thông tin công ty"
        subtitle="Đăng tin tức nội bộ, sự kiện và giới thiệu công ty — hiển thị trên app KetoanAPK"
      />

      <div
        className="mb-4 inline-flex flex-wrap gap-1 rounded-2xl p-1"
        style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
      >
        {TABS.map((item) => {
          const active = tab === item.key;
          const Icon = item.icon;
          return (
            <button
              key={item.key}
              type="button"
              onClick={() => setTab(item.key)}
              className="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-bold transition"
              style={active ? { background: "var(--accent)", color: "#fff" } : { color: "var(--text-secondary)" }}
            >
              <Icon className="h-4 w-4" />
              {item.label}
            </button>
          );
        })}
      </div>

      {tab === "about" ? (
        <AboutEditor notify={notify} />
      ) : (
        <GlassPanel strong className="overflow-hidden rounded-[20px]">
          <div className="flex flex-col gap-3 border-b border-[var(--gc-border)] px-5 py-4 md:flex-row md:items-center md:justify-between">
            <div>
              <h2 className="font-bold text-[var(--text)]">
                {tab === "news" ? "Tin tức & thông báo" : "Sự kiện & lịch"}
              </h2>
              <p className="text-xs text-[var(--text-secondary)]">
                {tab === "news"
                  ? "Ghim bài quan trọng lên đầu. Bài chưa đăng sẽ ẩn khỏi app."
                  : "Sự kiện sắp diễn ra hiển thị trước theo thời gian."}
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => reload()}
                className="grid h-9 w-9 place-items-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]"
                aria-label="Làm mới"
              >
                <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
              </button>
              <Button onClick={() => setEditing("new")}>
                <Plus className="h-4 w-4" />
                {tab === "news" ? "Thêm tin" : "Thêm sự kiện"}
              </Button>
            </div>
          </div>

          {error ? (
            <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
          ) : loading && !posts ? (
            <div className="grid h-40 place-items-center text-[var(--text-muted)]">
              <Loader2 className="h-5 w-5 animate-spin" />
            </div>
          ) : !posts || posts.length === 0 ? (
            <div className="p-6">
              <EmptyState
                icon={tab === "news" ? <Megaphone className="h-8 w-8" /> : <CalendarClock className="h-8 w-8" />}
                title={tab === "news" ? "Chưa có tin tức" : "Chưa có sự kiện"}
                hint="Bấm nút thêm ở góc phải để tạo bài đầu tiên."
              />
            </div>
          ) : (
            <ul className="divide-y divide-[var(--gc-border)]">
              {posts.map((post) => (
                <li key={post.id} className="flex items-start gap-4 px-5 py-4">
                  {post.coverImage ? (
                    <img
                      src={post.coverImage}
                      alt=""
                      className="h-16 w-24 shrink-0 rounded-xl object-cover"
                    />
                  ) : (
                    <span className="grid h-16 w-24 shrink-0 place-items-center rounded-xl bg-[var(--accent-soft)] text-[var(--accent)]">
                      {post.kind === "news" ? <Newspaper className="h-6 w-6" /> : <CalendarClock className="h-6 w-6" />}
                    </span>
                  )}
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-bold text-[var(--text)]">{post.title}</span>
                      {post.pinned && (
                        <Badge color="warning">
                          <Pin className="h-3 w-3" /> Ghim
                        </Badge>
                      )}
                      {!post.published && <Badge color="muted">Nháp (ẩn)</Badge>}
                    </div>
                    {post.summary && (
                      <p className="mt-1 line-clamp-2 text-sm text-[var(--text-secondary)]">{post.summary}</p>
                    )}
                    <div className="mt-1.5 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-[var(--text-muted)]">
                      {post.kind === "event" && post.eventAt && (
                        <span className="inline-flex items-center gap-1 font-semibold text-[var(--accent)]">
                          <CalendarClock className="h-3.5 w-3.5" />
                          {dateTime(post.eventAt)}
                        </span>
                      )}
                      {post.kind === "event" && post.location && (
                        <span className="inline-flex items-center gap-1">
                          <MapPin className="h-3.5 w-3.5" />
                          {post.location}
                        </span>
                      )}
                      <span>Cập nhật {dateTime(post.updatedAt)}</span>
                      {post.authorName && <span>bởi {post.authorName}</span>}
                    </div>
                  </div>
                  <div className="flex shrink-0 items-center gap-1">
                    <button
                      type="button"
                      onClick={() => togglePublished(post)}
                      className="grid h-9 w-9 place-items-center rounded-lg text-[var(--text-secondary)] hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
                      title={post.published ? "Đang hiển thị — bấm để ẩn" : "Đang ẩn — bấm để hiển thị"}
                    >
                      {post.published ? <Eye className="h-4 w-4" /> : <EyeOff className="h-4 w-4" />}
                    </button>
                    <button
                      type="button"
                      onClick={() => setEditing(post)}
                      className="grid h-9 w-9 place-items-center rounded-lg text-[var(--text-secondary)] hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
                      title="Sửa"
                    >
                      <Pencil className="h-4 w-4" />
                    </button>
                    <button
                      type="button"
                      onClick={() => remove(post)}
                      className="grid h-9 w-9 place-items-center rounded-lg text-[var(--text-secondary)] hover:bg-red-500/10 hover:text-[var(--danger)]"
                      title="Xóa"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </GlassPanel>
      )}

      {editing !== null && tab !== "about" && (
        <PostEditor
          kind={tab}
          post={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            reload({ silent: true });
          }}
          notify={notify}
        />
      )}
    </div>
  );
}

type Notify = ReturnType<typeof useAppNotifications>["notify"];

function CoverPicker({
  value,
  onChange,
  notify,
}: {
  value: string | null;
  onChange: (v: string | null) => void;
  notify: Notify;
}) {
  const inputRef = useRef<HTMLInputElement>(null);

  const pick = async (file: File | undefined) => {
    if (!file) return;
    if (file.size > MAX_IMAGE_BYTES) {
      notify.error("Ảnh quá lớn (tối đa ~2MB). Vui lòng chọn ảnh nhỏ hơn.");
      return;
    }
    try {
      onChange(await fileToDataUrl(file));
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không đọc được ảnh.");
    }
  };

  return (
    <div>
      <input
        ref={inputRef}
        type="file"
        accept="image/*"
        className="hidden"
        onChange={(e) => void pick(e.target.files?.[0])}
      />
      {value ? (
        <div className="relative w-full overflow-hidden rounded-xl">
          <img src={value} alt="" className="max-h-52 w-full object-cover" />
          <div className="absolute right-2 top-2 flex gap-2">
            <button
              type="button"
              onClick={() => inputRef.current?.click()}
              className="rounded-lg bg-black/55 px-2.5 py-1.5 text-xs font-semibold text-white backdrop-blur"
            >
              Đổi ảnh
            </button>
            <button
              type="button"
              onClick={() => onChange(null)}
              className="rounded-lg bg-black/55 px-2.5 py-1.5 text-xs font-semibold text-white backdrop-blur"
            >
              Xóa
            </button>
          </div>
        </div>
      ) : (
        <button
          type="button"
          onClick={() => inputRef.current?.click()}
          className="flex w-full flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-[var(--glass-border)] py-8 text-[var(--text-secondary)] hover:border-[var(--accent)] hover:text-[var(--accent)]"
        >
          <ImagePlus className="h-7 w-7" />
          <span className="text-sm font-semibold">Chọn ảnh bìa (tùy chọn)</span>
        </button>
      )}
    </div>
  );
}

function PostEditor({
  kind,
  post,
  onClose,
  onSaved,
  notify,
}: {
  kind: PortalKind;
  post: PortalPost | null;
  onClose: () => void;
  onSaved: () => void;
  notify: Notify;
}) {
  const [title, setTitle] = useState(post?.title ?? "");
  const [summary, setSummary] = useState(post?.summary ?? "");
  const [body, setBody] = useState(post?.body ?? "");
  const [cover, setCover] = useState<string | null>(post?.coverImage ?? null);
  const [location, setLocation] = useState(post?.location ?? "");
  const [eventLocal, setEventLocal] = useState(toLocalInput(post?.eventAt ?? null));
  const [pinned, setPinned] = useState(post?.pinned ?? false);
  const [published, setPublished] = useState(post?.published ?? true);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  const save = async () => {
    if (!title.trim()) {
      notify.error("Vui lòng nhập tiêu đề.");
      return;
    }
    if (kind === "event" && !eventLocal) {
      notify.error("Sự kiện cần có thời gian diễn ra.");
      return;
    }
    setSaving(true);
    const payload = {
      kind,
      title: title.trim(),
      summary: summary.trim(),
      body: body.trim(),
      coverImage: cover,
      location: location.trim(),
      eventAt: kind === "event" && eventLocal ? new Date(eventLocal).toISOString() : null,
      pinned,
      published,
    };
    try {
      if (post) await api.put(`/api/portal/posts/${post.id}`, payload);
      else await api.post("/api/portal/posts", payload);
      notify.success(post ? "Đã lưu thay đổi." : "Đã đăng bài.");
      onSaved();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được bài viết.");
    } finally {
      setSaving(false);
    }
  };

  const heading = post
    ? kind === "news"
      ? "Sửa tin tức"
      : "Sửa sự kiện"
    : kind === "news"
      ? "Thêm tin tức"
      : "Thêm sự kiện";

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/45 p-4 backdrop-blur-sm">
      <GlassPanel strong className="my-6 w-full max-w-2xl overflow-hidden rounded-[22px]">
        <div className="flex items-center justify-between border-b border-[var(--gc-border)] px-5 py-4">
          <h2 className="text-lg font-bold text-[var(--text)]">{heading}</h2>
          <button
            type="button"
            onClick={onClose}
            className="grid h-9 w-9 place-items-center rounded-full text-[var(--text-secondary)] hover:bg-black/5 dark:hover:bg-white/10"
            aria-label="Đóng"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="max-h-[70vh] space-y-4 overflow-y-auto px-5 py-5">
          <Field label="Tiêu đề">
            <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Nhập tiêu đề..." autoFocus />
          </Field>

          {kind === "event" && (
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Thời gian diễn ra">
                <Input type="datetime-local" value={eventLocal} onChange={(e) => setEventLocal(e.target.value)} />
              </Field>
              <Field label="Địa điểm">
                <Input value={location} onChange={(e) => setLocation(e.target.value)} placeholder="VD: Hội trường tầng 3" />
              </Field>
            </div>
          )}

          <Field label="Tóm tắt ngắn">
            <Input value={summary} onChange={(e) => setSummary(e.target.value)} placeholder="1–2 câu tóm tắt hiển thị ở danh sách" />
          </Field>

          <Field label="Nội dung chi tiết">
            <textarea
              value={body}
              onChange={(e) => setBody(e.target.value)}
              rows={7}
              placeholder="Nội dung đầy đủ của tin/sự kiện..."
              className="w-full resize-y rounded-xl border border-[var(--glass-border)] bg-[var(--glass-bg-strong)] px-3.5 py-2.5 text-sm text-[var(--text)] outline-none focus:border-[var(--accent)]"
            />
          </Field>

          <Field label="Ảnh bìa">
            <CoverPicker value={cover} onChange={setCover} notify={notify} />
          </Field>

          <div className="flex flex-wrap gap-3 pt-1">
            {kind === "news" && (
              <label className="inline-flex cursor-pointer items-center gap-2 text-sm font-semibold text-[var(--text-secondary)]">
                <input type="checkbox" checked={pinned} onChange={(e) => setPinned(e.target.checked)} className="h-4 w-4 accent-[var(--accent)]" />
                <Pin className="h-4 w-4" /> Ghim lên đầu
              </label>
            )}
            <label className="inline-flex cursor-pointer items-center gap-2 text-sm font-semibold text-[var(--text-secondary)]">
              <input type="checkbox" checked={published} onChange={(e) => setPublished(e.target.checked)} className="h-4 w-4 accent-[var(--accent)]" />
              <Eye className="h-4 w-4" /> Hiển thị trên app
            </label>
          </div>
        </div>

        <div className="flex justify-end gap-2 border-t border-[var(--gc-border)] px-5 py-4">
          <Button variant="ghost" onClick={onClose} disabled={saving}>
            Hủy
          </Button>
          <Button onClick={save} loading={saving}>
            <Save className="h-4 w-4" />
            {post ? "Lưu thay đổi" : "Đăng bài"}
          </Button>
        </div>
      </GlassPanel>
    </div>
  );
}

function AboutEditor({ notify }: { notify: Notify }) {
  const { data, loading, reload } = useApi<PortalAbout>("/api/portal/about");
  // Bản NHÁP chỉ tồn tại khi người dùng đã sửa gì đó; chưa sửa thì hiển thị thẳng dữ liệu máy chủ.
  // Nhờ vậy không cần useEffect chép dữ liệu sang state, và dữ liệu mới tải về (sau khi lưu, hoặc do
  // tín hiệu realtime) tự hiện ra — thay vì bị bản sao cũ trong state che mất.
  const [draft, setDraft] = useState<PortalAbout | null>(null);
  const [saving, setSaving] = useState(false);
  const form = draft ?? data ?? null;

  const set = <K extends keyof PortalAbout>(key: K, value: PortalAbout[K]) =>
    setDraft((f) => {
      const base = f ?? data;
      return base ? { ...base, [key]: value } : base ?? null;
    });

  const save = async () => {
    if (!form) return;
    setSaving(true);
    try {
      await api.put("/api/portal/about", form);
      notify.success("Đã lưu giới thiệu công ty.");
      // Lưu xong thì bỏ bản nháp: từ đây lại bám theo dữ liệu máy chủ (kể cả phần máy chủ tự chuẩn hoá).
      setDraft(null);
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được giới thiệu.");
    } finally {
      setSaving(false);
    }
  };

  if (loading && !form) {
    return (
      <GlassPanel strong className="grid h-40 place-items-center rounded-[20px] text-[var(--text-muted)]">
        <Loader2 className="h-5 w-5 animate-spin" />
      </GlassPanel>
    );
  }
  if (!form) return null;

  return (
    <GlassPanel strong className="overflow-hidden rounded-[20px]">
      <div className="border-b border-[var(--gc-border)] px-5 py-4">
        <h2 className="font-bold text-[var(--text)]">Giới thiệu công ty</h2>
        <p className="text-xs text-[var(--text-secondary)]">Thông tin này hiển thị ở đầu cổng thông tin trong app.</p>
      </div>
      <div className="space-y-4 px-5 py-5">
        <Field label="Ảnh bìa / logo">
          <CoverPicker value={form.coverImage} onChange={(v) => set("coverImage", v)} notify={notify} />
        </Field>
        <Field label="Tên / tiêu đề công ty">
          <Input value={form.title} onChange={(e) => set("title", e.target.value)} placeholder="VD: Công ty TNHH ..." />
        </Field>
        <Field label="Giới thiệu">
          <textarea
            value={form.content}
            onChange={(e) => set("content", e.target.value)}
            rows={6}
            placeholder="Mô tả về công ty, tầm nhìn, lĩnh vực hoạt động..."
            className="w-full resize-y rounded-xl border border-[var(--glass-border)] bg-[var(--glass-bg-strong)] px-3.5 py-2.5 text-sm text-[var(--text)] outline-none focus:border-[var(--accent)]"
          />
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Địa chỉ">
            <Input value={form.address} onChange={(e) => set("address", e.target.value)} placeholder="Số nhà, đường, quận, thành phố" />
          </Field>
          <Field label="Hotline">
            <Input value={form.hotline} onChange={(e) => set("hotline", e.target.value)} placeholder="VD: 1900 xxxx" />
          </Field>
          <Field label="Email">
            <Input value={form.email} onChange={(e) => set("email", e.target.value)} placeholder="lienhe@congty.vn" />
          </Field>
          <Field label="Website">
            <Input value={form.website} onChange={(e) => set("website", e.target.value)} placeholder="https://..." />
          </Field>
        </div>
      </div>
      <div className="flex items-center justify-between gap-2 border-t border-[var(--gc-border)] px-5 py-4">
        <span className="text-xs text-[var(--text-muted)]">
          {data?.updatedAt ? `Cập nhật lần cuối: ${dateTime(data.updatedAt)}` : "Chưa lưu lần nào"}
        </span>
        <Button onClick={save} loading={saving}>
          <Save className="h-4 w-4" />
          Lưu giới thiệu
        </Button>
      </div>
    </GlassPanel>
  );
}
