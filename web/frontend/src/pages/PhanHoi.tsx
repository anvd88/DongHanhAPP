import { useEffect, useMemo, useRef, useState } from "react";
import { CheckCircle2, Flag, Loader2, MessageCircle, RefreshCw, Send, TimerReset } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Badge, Button } from "../components/ui";
import { Table } from "../components/Table";
import { api } from "../lib/api";
import { dateTime, initials } from "../lib/format";
import { useApi } from "../lib/useApi";
import { type ChatConversation, type ChatMessage, type FeedbackItem } from "../lib/types";
import { PERM, useAccess } from "../lib/access";
import { useAppNotifications } from "../components/app-notifications-context";

type FeedbackFilter = "all" | "ChatReport" | "AttendanceIssue";
type FeedbackPage = "feedback" | "support";

const filters: { key: FeedbackFilter; label: string }[] = [
  { key: "all", label: "Tất cả" },
  { key: "ChatReport", label: "Báo xấu trò chuyện" },
  { key: "AttendanceIssue", label: "Báo lỗi chấm công" },
];

function TypeBadge({ row }: { row: FeedbackItem }) {
  if (row.type === "ChatReport") {
    return (
      <Badge color="warning">
        <Flag className="h-3.5 w-3.5" /> {row.typeLabel}
      </Badge>
    );
  }
  if (row.type === "AttendanceIssue") {
    return (
      <Badge color="purple">
        <TimerReset className="h-3.5 w-3.5" /> {row.typeLabel}
      </Badge>
    );
  }
  return <Badge>{row.typeLabel}</Badge>;
}

function fmtClock(iso?: string | null) {
  if (!iso) return "";
  const d = new Date(iso);
  return isNaN(d.getTime()) ? "" : d.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" });
}

function SupportAvatar({ name, url }: { name: string; url?: string | null }) {
  return (
    <span
      className="grid h-10 w-10 shrink-0 place-items-center overflow-hidden rounded-full text-sm font-bold text-white"
      style={{ background: "linear-gradient(135deg, var(--accent), var(--purple))" }}
    >
      {url ? <img src={url} alt="" className="h-full w-full object-cover" /> : initials(name)}
    </span>
  );
}

function SupportBubble({ msg }: { msg: ChatMessage }) {
  const mine = msg.mine;
  return (
    <div className={`flex flex-col ${mine ? "items-end" : "items-start"}`}>
      {!mine && <div className="mb-1 px-1 text-xs font-semibold text-[var(--text-secondary)]">{msg.senderName}</div>}
      <div
        className="max-w-[82%] whitespace-pre-wrap break-words rounded-2xl px-3.5 py-2.5 text-sm leading-relaxed shadow-sm"
        style={
          mine
            ? { background: "var(--accent)", color: "#fff", borderTopRightRadius: 6 }
            : {
                background: "var(--glass-bg-strong)",
                border: "1px solid var(--glass-border)",
                color: "var(--text)",
                borderTopLeftRadius: 6,
              }
        }
      >
        {msg.removed ? <span className="italic opacity-75">Tin nhắn đã được gỡ</span> : msg.body}
        <div className={`mt-1 text-right text-[0.68rem] ${mine ? "text-white/75" : "text-[var(--text-muted)]"}`}>
          {fmtClock(msg.createdAt)}
        </div>
      </div>
    </div>
  );
}

export function PhanHoi() {
  const navigate = useNavigate();
  const { can } = useAccess();
  const supportAgent = can(PERM.usersManage);
  const { notify, confirm } = useAppNotifications();
  const { data, loading, error, reload, setData } = useApi<FeedbackItem[]>("/api/feedback");
  const {
    data: conversationData,
    loading: conversationsLoading,
    reload: reloadConversations,
  } = useApi<ChatConversation[]>(supportAgent ? "/api/chat/conversations" : null, [supportAgent]);
  const [filter, setFilter] = useState<FeedbackFilter>("all");
  const [page, setPage] = useState<FeedbackPage>("feedback");
  const [resolvingId, setResolvingId] = useState<number | null>(null);
  const [openingChatId, setOpeningChatId] = useState<number | null>(null);
  const [activeSupportId, setActiveSupportId] = useState<string | null>(null);
  const [supportDraft, setSupportDraft] = useState("");
  const [supportSending, setSupportSending] = useState(false);
  const supportScrollRef = useRef<HTMLDivElement>(null);
  const supportConversations = useMemo(
    () => (conversationData ?? []).filter((item) => item.supportConversation),
    [conversationData],
  );
  const activeSupport = supportConversations.find((item) => item.id === activeSupportId) ?? null;
  const {
    data: supportMessageData,
    loading: supportMessagesLoading,
    reload: reloadSupportMessages,
  } = useApi<ChatMessage[]>(
    supportAgent && activeSupportId ? `/api/chat/conversations/${activeSupportId}/messages` : null,
    [supportAgent, activeSupportId],
  );
  const supportMessages = supportMessageData ?? [];

  // Chọn sẵn cuộc hỗ trợ đầu tiên khi nhân sự hỗ trợ mở trang mà chưa chọn gì. Gán lúc render thay vì trong
  // useEffect: effect vẽ thừa một khung hình "chưa chọn cuộc nào" rồi mới nhảy, và React Compiler
  // cấm setState đồng bộ trong effect. Điều kiện tự chặn — chọn xong thì `activeSupportId` có giá
  // trị nên lần render sau không vào nhánh này nữa.
  if (supportAgent && !activeSupportId && supportConversations.length > 0) {
    setActiveSupportId(supportConversations[0].id);
  }

  useEffect(() => {
    requestAnimationFrame(() => {
      supportScrollRef.current?.scrollTo({ top: supportScrollRef.current.scrollHeight, behavior: "smooth" });
    });
  }, [activeSupportId, supportMessages.length]);

  const rows = useMemo(() => {
    const items = data ?? [];
    return filter === "all" ? items : items.filter((item) => item.type === filter);
  }, [data, filter]);

  const resolveFeedback = async (row: FeedbackItem) => {
    const ok = await confirm({
      title: "Đánh dấu đã giải quyết?",
      description: "Phản hồi này sẽ được xoá khỏi cơ sở dữ liệu và người gửi sẽ nhận thông báo.",
      confirmLabel: "Đã giải quyết",
      tone: "warning",
    });
    if (!ok) return;

    setResolvingId(row.id);
    try {
      await api.post(`/api/feedback/${row.id}/resolve`);
      setData((data ?? []).filter((item) => item.id !== row.id));
      notify.success("Đã xử lý và xoá phản hồi.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xử lý được phản hồi.");
    } finally {
      setResolvingId(null);
    }
  };

  const openFeedbackChat = async (row: FeedbackItem) => {
    setOpeningChatId(row.id);
    try {
      let conversationId = row.conversationId;
      if (supportAgent) {
        const res = await api.post<{ id: string }>(`/api/chat/support/${encodeURIComponent(row.reporterUsername)}`);
        conversationId = res.id;
        setActiveSupportId(conversationId);
        setPage("support");
        reloadConversations({ silent: true });
      } else if (!conversationId) {
        const res = await api.post<{ id: string }>("/api/chat/direct/__support__");
        conversationId = res.id;
      }
      if (!conversationId) throw new Error("Không tìm thấy cuộc trò chuyện phản hồi.");
      if (!supportAgent) navigate(`/chats?conversation=${encodeURIComponent(conversationId)}`);
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không mở được cuộc trò chuyện phản hồi.");
    } finally {
      setOpeningChatId(null);
    }
  };

  const sendSupportMessage = async () => {
    const text = supportDraft.trim();
    if (!activeSupportId || !text || supportSending) return;
    setSupportSending(true);
    setSupportDraft("");
    try {
      await api.post(`/api/chat/conversations/${activeSupportId}/messages`, { body: text, sendAsSupport: true });
      reloadSupportMessages({ silent: true });
      reloadConversations({ silent: true });
    } catch (e) {
      setSupportDraft(text);
      notify.error(e instanceof Error ? e.message : "Không gửi được tin nhắn hỗ trợ.");
    } finally {
      setSupportSending(false);
    }
  };

  return (
    <div className="gc-root">
      <PageHeader
        title="Phản hồi"
        subtitle={supportAgent ? "Theo dõi báo xấu trò chuyện và báo lỗi chấm công" : "Theo dõi các phản hồi đã gửi"}
      />

      {supportAgent && (
        <div
          className="mb-4 inline-grid grid-cols-2 rounded-2xl p-1"
          style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
        >
          {[
            { key: "feedback" as const, label: "Danh sách phản hồi" },
            { key: "support" as const, label: "Chat hỗ trợ người dùng" },
          ].map((item) => {
            const active = page === item.key;
            return (
              <button
                key={item.key}
                type="button"
                onClick={() => setPage(item.key)}
                className="rounded-xl px-4 py-2 text-sm font-bold transition"
                style={
                  active
                    ? { background: "var(--accent)", color: "#fff", boxShadow: "0 10px 24px rgba(var(--accent-rgb),0.24)" }
                    : { color: "var(--text-secondary)" }
                }
              >
                {item.label}
              </button>
            );
          })}
        </div>
      )}

      {supportAgent && page === "support" && (
        <GlassPanel strong className="mb-4 overflow-hidden rounded-[20px]">
          <div className="flex flex-col gap-3 border-b border-[var(--gc-border)] px-5 py-4 md:flex-row md:items-center md:justify-between">
            <div>
              <h2 className="font-bold text-[var(--text)]">Quản lý chat Hỗ Trợ Người Dùng</h2>
              <p className="text-xs text-[var(--text-secondary)]">Trả lời và theo dõi các cuộc chat hỗ trợ ngay tại trang phản hồi.</p>
            </div>
            <button
              type="button"
              onClick={() => reloadConversations()}
              className="grid h-8 w-8 place-items-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]"
              aria-label="Làm mới hội thoại hỗ trợ"
            >
              <RefreshCw className={`h-4 w-4 ${conversationsLoading ? "animate-spin" : ""}`} />
            </button>
          </div>
          <div className="grid h-[760px] max-h-[calc(100vh-180px)] min-h-[560px] min-w-0 grid-cols-1 grid-rows-[220px_minmax(0,1fr)] lg:h-[620px] lg:min-h-[460px] lg:grid-cols-[330px_minmax(0,1fr)] lg:grid-rows-none">
            <div className="min-h-0 border-b border-[var(--gc-border)] lg:border-b-0 lg:border-r">
              <div className="h-full min-h-0 overflow-y-auto p-3">
                {conversationsLoading && supportConversations.length === 0 ? (
                  <div className="grid h-40 place-items-center text-[var(--text-muted)]">
                    <Loader2 className="h-5 w-5 animate-spin" />
                  </div>
                ) : supportConversations.length === 0 ? (
                  <div className="px-3 py-10 text-center text-sm text-[var(--text-muted)]">Chưa có hội thoại hỗ trợ</div>
                ) : (
                  <div className="space-y-2">
                    {supportConversations.map((c) => {
                      const active = c.id === activeSupportId;
                      return (
                        <button
                          key={c.id}
                          type="button"
                          onClick={() => setActiveSupportId(c.id)}
                          className="flex w-full items-center gap-3 rounded-2xl p-3 text-left transition"
                          style={
                            active
                              ? { background: "var(--accent-soft)", border: "1px solid var(--accent)" }
                              : { background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }
                          }
                        >
                          <SupportAvatar name={c.title} url={c.avatarUrl} />
                          <span className="min-w-0 flex-1">
                            <span className="flex items-center gap-2">
                              <span className="truncate text-sm font-bold text-[var(--text)]">{c.title}</span>
                              {c.unread > 0 && <Badge color="warning">{c.unread}</Badge>}
                            </span>
                            <span className="mt-1 block truncate text-xs text-[var(--text-secondary)]">
                              {c.preview || "Chưa có tin nhắn."}
                            </span>
                            {c.lastAt && <span className="mt-1 block text-[0.7rem] text-[var(--text-muted)]">{dateTime(c.lastAt)}</span>}
                          </span>
                        </button>
                      );
                    })}
                  </div>
                )}
              </div>
            </div>

            <div className="flex min-h-0 min-w-0 flex-col">
              {activeSupport ? (
                <>
                  <div className="flex items-center gap-3 border-b border-[var(--gc-border)] px-4 py-3">
                    <SupportAvatar name={activeSupport.title} url={activeSupport.avatarUrl} />
                    <div className="min-w-0 flex-1">
                      <div className="truncate font-bold text-[var(--text)]">{activeSupport.title}</div>
                      <div className="text-xs text-[var(--text-secondary)]">
                        {activeSupport.username ? `@${activeSupport.username}` : "Hội thoại hỗ trợ"}
                      </div>
                    </div>
                    <Badge color={activeSupport.unread > 0 ? "warning" : "muted"}>
                      {activeSupport.unread > 0 ? `${activeSupport.unread} chưa đọc` : "Đã đọc"}
                    </Badge>
                  </div>

                  <div ref={supportScrollRef} className="min-h-0 flex-1 overscroll-contain overflow-y-auto p-4">
                    {supportMessagesLoading ? (
                      <div className="grid h-full min-h-[260px] place-items-center text-[var(--text-muted)]">
                        <Loader2 className="h-5 w-5 animate-spin" />
                      </div>
                    ) : supportMessages.length === 0 ? (
                      <div className="grid h-full min-h-[260px] place-items-center text-center text-sm text-[var(--text-muted)]">
                        Chưa có tin nhắn hỗ trợ.
                      </div>
                    ) : (
                      <div className="flex min-h-full flex-col justify-end space-y-3">
                        {supportMessages.map((msg) => <SupportBubble key={msg.id} msg={msg} />)}
                      </div>
                    )}
                  </div>

                  <div className="border-t border-[var(--gc-border)] p-3">
                    <div
                      className="flex items-center gap-2 rounded-2xl px-3 py-2"
                      style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
                    >
                      <input
                        value={supportDraft}
                        onChange={(e) => setSupportDraft(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === "Enter") void sendSupportMessage();
                        }}
                        placeholder="Trả lời bằng tài khoản Hỗ Trợ Người Dùng..."
                        className="min-w-0 flex-1 bg-transparent text-sm text-[var(--text)] outline-none"
                      />
                      <button
                        type="button"
                        onClick={() => void sendSupportMessage()}
                        disabled={supportSending || !supportDraft.trim()}
                        className="grid h-9 w-9 shrink-0 place-items-center rounded-full text-white transition disabled:opacity-50"
                        style={{ background: "var(--accent)" }}
                        aria-label="Gửi tin nhắn hỗ trợ"
                      >
                        {supportSending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
                      </button>
                    </div>
                  </div>
                </>
              ) : (
                <div className="grid min-h-[360px] place-items-center p-8 text-center text-sm text-[var(--text-muted)]">
                  <div>
                    <MessageCircle className="mx-auto mb-2 h-8 w-8 opacity-50" />
                    Chọn một hội thoại hỗ trợ để xem và trả lời.
                  </div>
                </div>
              )}
            </div>
          </div>
        </GlassPanel>
      )}

      {(!supportAgent || page === "feedback") && (
      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        <div className="flex flex-col gap-3 border-b border-[var(--gc-border)] px-5 py-4 md:flex-row md:items-center md:justify-between">
          <div>
            <h2 className="font-bold text-[var(--text)]">{supportAgent ? "Danh sách phản hồi" : "Phản hồi của tôi"}</h2>
            <p className="text-xs text-[var(--text-secondary)]">Thời gian hiển thị đầy đủ ngày, giờ và phút.</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {filters.map((item) => (
              <button
                key={item.key}
                type="button"
                onClick={() => setFilter(item.key)}
                className={`rounded-full px-3 py-1.5 text-xs font-bold transition ${
                  filter === item.key
                    ? "bg-[var(--accent)] text-white shadow-lg shadow-[rgba(var(--accent-rgb),0.25)]"
                    : "bg-[var(--accent-soft)] text-[var(--text-secondary)] hover:text-[var(--accent)]"
                }`}
              >
                {item.label}
              </button>
            ))}
            <button
              type="button"
              onClick={() => reload()}
              className="grid h-8 w-8 place-items-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]"
              aria-label="Làm mới"
            >
              <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
            </button>
          </div>
        </div>

        {error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <Table<FeedbackItem>
            loading={loading}
            rows={rows}
            keyOf={(row) => row.id}
            empty="Chưa có phản hồi"
            columns={[
              { header: "Thời gian", cell: (r) => <span className="whitespace-nowrap text-[var(--text-secondary)]">{dateTime(r.createdAt)}</span> },
              { header: "Loại", cell: (r) => <TypeBadge row={r} /> },
              { header: "Người gửi", cell: (r) => <span className="font-semibold">{r.reporterName || r.reporterUsername}</span> },
              {
                header: "Nội dung",
                cell: (r) => (
                  <div className="max-w-[520px]">
                    <div className="font-semibold text-[var(--text)]">{r.targetName || "Cuộc trò chuyện"}</div>
                    <div className="mt-1 text-sm text-[var(--text-secondary)]">{r.reason || "Không ghi nguyên nhân."}</div>
                  </div>
                ),
              },
              {
                header: "Xử lý",
                align: "right",
                cell: (r) => supportAgent ? (
                  <div className="flex justify-end gap-2">
                    <Button variant="soft" loading={openingChatId === r.id} onClick={() => openFeedbackChat(r)}>
                      <MessageCircle className="h-4 w-4" />
                      Nhắn
                    </Button>
                    <Button variant="soft" loading={resolvingId === r.id} onClick={() => resolveFeedback(r)}>
                      <CheckCircle2 className="h-4 w-4" />
                      Đã giải quyết
                    </Button>
                  </div>
                ) : (
                  <div className="flex justify-end gap-2">
                    <Button variant="soft" loading={openingChatId === r.id} onClick={() => openFeedbackChat(r)}>
                      <MessageCircle className="h-4 w-4" />
                      Mở chat
                    </Button>
                    <Badge color="muted">Đang chờ</Badge>
                  </div>
                ),
              },
            ]}
          />
        )}
      </GlassPanel>
      )}
    </div>
  );
}
