import { useMemo, useState } from 'react'
import { Plus } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { dateTime } from '@/lib/format'
import { matches } from '@/lib/text'
import {
  SURVEY_TYPE_LABELS,
  useCloseSurvey,
  useCreateSurvey,
  useDeleteFaq,
  useDeletePost,
  useDeleteSurvey,
  useFaqs,
  useFeedback,
  useHelpStatus,
  useMyGeneralFeedback,
  useMySupportTickets,
  useOpenSurveys,
  usePortalAbout,
  usePortalFeed,
  usePortalPosts,
  useResolveFeedback,
  useRespondSurvey,
  useSaveAbout,
  useSaveFaq,
  useSavePost,
  useSendGeneralFeedback,
  useSendSupportTicket,
  useSurvey,
  useSurveyResults,
  useSurveys,
  type CreateSurveyRequest,
  type Faq,
  type FeedbackItem,
  type PortalPost,
  type SurveyOpen,
  type SurveyRow,
} from '@/api/portal'
import {
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  DatePicker,
  Drawer,
  Field,
  Figure,
  FigureStrip,
  FormGrid,
  InlineAlert,
  Input,
  KeyValue,
  Modal,
  NumberInput,
  Panel,
  SearchInput,
  Select,
  Stack,
  StatusBadge,
  Textarea,
  useToast,
  type Column,
} from '@/ui'
import { ModuleScreen, errorMessage } from './_shared'

/* ============================================================================
   Tin tức & sự kiện

   Cùng một sổ bài viết hiển thị trên web lẫn ứng dụng. Bài chưa đăng chỉ người quản trị nhìn thấy.
   ========================================================================== */

export function PortalPage() {
  const auth = useAuth()
  const toast = useToast()
  const canManage = auth.can(PERM.portalManage)
  const [kind, setKind] = useState('news')
  const [search, setSearch] = useState('')
  const [openId, setOpenId] = useState<number | null>(null)
  const [composer, setComposer] = useState<null | { post?: PortalPost }>(null)
  const [removing, setRemoving] = useState<PortalPost | null>(null)
  const [editingAbout, setEditingAbout] = useState(false)

  const posts = usePortalPosts(kind, canManage)
  const about = usePortalAbout()
  const remove = useDeletePost()

  const all = posts.data ?? []
  const rows = useMemo(
    () => all.filter((p) => !search || matches(`${p.title} ${p.summary} ${p.authorName} ${p.location}`, search)),
    [all, search],
  )

  const columns: Column<PortalPost>[] = [
    {
      key: 'title',
      priority: 1,
      header: 'Tiêu đề',
      cell: (row) => (
        <span className="flex flex-col">
          <span className="font-medium">
            {row.pinned && <span className="mr-1 text-brand">★</span>}
            {row.title}
          </span>
          {row.summary && <span className="text-xs text-ink-3">{row.summary}</span>}
        </span>
      ),
      sortValue: (r) => r.title,
      truncate: true,
    },
    { key: 'author', priority: 2, header: 'Người đăng', width: '10rem', cell: (row) => row.authorName || row.authorUsername, sortValue: (r) => r.authorName },
    {
      key: 'when',
      priority: 1,
      header: kind === 'event' ? 'Diễn ra lúc' : 'Đăng lúc',
      width: '10rem',
      cell: (row) => dateTime(kind === 'event' ? row.eventAt : row.createdAt),
      sortValue: (r) => (kind === 'event' ? r.eventAt ?? '' : r.createdAt),
    },
    { key: 'location', priority: 3, header: 'Địa điểm', cell: (row) => row.location, hidden: kind !== 'event' },
    {
      key: 'status',
      priority: 1,
      header: 'Trạng thái',
      width: '8rem',
      cell: (row) => (row.published ? <StatusBadge tone="ok">Đã đăng</StatusBadge> : <StatusBadge>Bản nháp</StatusBadge>),
      sortValue: (r) => (r.published ? 0 : 1),
    },
  ]

  if (!canManage) {
    return <PortalReaderView />
  }

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Bài đã đăng" value={posts.data ? all.filter((p) => p.published).length : '…'} />
            <Figure label="Bản nháp" value={posts.data ? all.filter((p) => !p.published).length : '…'} />
            <Figure label="Bài ghim" value={posts.data ? all.filter((p) => p.pinned).length : '…'} />
          </FigureStrip>
        }
        tabs={[
          { id: 'news', label: 'Tin tức' },
          { id: 'event', label: 'Sự kiện' },
        ]}
        tab={kind}
        onTabChange={setKind}
        actions={
          <>
            <Button size="sm" onClick={() => setEditingAbout(true)}>
              Sửa giới thiệu
            </Button>
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposer({})}>
              Viết bài
            </Button>
          </>
        }
        filters={
          <SearchInput
            size="sm"
            className="w-56"
            placeholder="Tiêu đề, người đăng"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onClear={() => setSearch('')}
          />
        }
        columns={columns}
        rows={rows}
        getKey={(row) => row.id}
        loading={posts.isLoading}
        error={posts.error}
        onRefresh={() => posts.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'when', dir: 'desc' }}
        emptyTitle={kind === 'event' ? 'Chưa có sự kiện nào' : 'Chưa có tin nào'}
      />

      <Drawer
        open={openId !== null}
        onClose={() => setOpenId(null)}
        width="lg"
        title={all.find((p) => p.id === openId)?.title ?? 'Bài viết'}
        actions={(() => {
          const post = all.find((p) => p.id === openId)
          if (!post) return null
          return (
            <>
              <Button size="sm" onClick={() => { setOpenId(null); setComposer({ post }) }}>
                Sửa
              </Button>
              <Button size="sm" variant="ghost" className="text-danger" onClick={() => setRemoving(post)}>
                Xoá
              </Button>
            </>
          )
        })()}
      >
        {(() => {
          const post = all.find((p) => p.id === openId)
          if (!post) return null
          return (
            <div className="flex flex-col gap-3 p-3">
              {post.coverImage && <img src={post.coverImage} alt="" className="rounded-sm border border-line" />}
              <Panel title="Thông tin bài" padded>
                <KeyValue
                  rows={[
                    ['Loại', post.kind === 'event' ? 'Sự kiện' : 'Tin tức'],
                    ['Tóm tắt', post.summary || null],
                    ['Địa điểm', post.location || null],
                    ['Diễn ra lúc', post.eventAt ? dateTime(post.eventAt) : null],
                    ['Người đăng', post.authorName || post.authorUsername],
                    ['Cập nhật', dateTime(post.updatedAt)],
                  ]}
                />
              </Panel>
              <Panel title="Nội dung" padded>
                <p className="whitespace-pre-wrap text-sm text-ink-2">{post.body}</p>
              </Panel>
            </div>
          )
        })()}
      </Drawer>

      {composer && <PostComposer initial={composer.post} defaultKind={kind} onClose={() => setComposer(null)} />}
      {editingAbout && about.data && <AboutModal initial={about.data} onClose={() => setEditingAbout(false)} />}

      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        title={`Xoá bài "${removing?.title ?? ''}"`}
        message="Bài biến mất khỏi cả web lẫn ứng dụng."
        confirmLabel="Xoá bài"
        tone="danger"
        busy={remove.isPending}
        onConfirm={async () => {
          if (!removing) return
          try {
            await remove.mutateAsync(removing.id)
            toast.success('Đã xoá bài')
            setRemoving(null)
            setOpenId(null)
          } catch (e) {
            toast.error('Không xoá được bài', errorMessage(e))
          }
        }}
      />
    </>
  )
}

/** Người không quản trị cổng thông tin chỉ đọc: giới thiệu công ty, tin tức và sự kiện sắp tới. */
function PortalReaderView() {
  const [openId, setOpenId] = useState<number | null>(null)
  const feed = usePortalFeed()
  const posts = [...(feed.data?.news ?? []), ...(feed.data?.events ?? [])]
  const open = posts.find((p) => p.id === openId)

  return (
    <Stack>
      {feed.data?.about?.title && (
        <Panel title={feed.data.about.title} padded>
          <p className="whitespace-pre-wrap text-sm text-ink-2">{feed.data.about.content}</p>
          <div className="mt-3">
            <KeyValue
              rows={[
                ['Địa chỉ', feed.data.about.address || null],
                ['Đường dây nóng', feed.data.about.hotline || null],
                ['Thư điện tử', feed.data.about.email || null],
                ['Trang mạng', feed.data.about.website || null],
              ]}
            />
          </div>
        </Panel>
      )}

      <Panel title="Sự kiện sắp tới" meta={feed.data ? `${feed.data.events.length} sự kiện` : undefined}>
        <DataTable
          columns={[
            { key: 'title', priority: 1, header: 'Sự kiện', cell: (row) => row.title },
            { key: 'eventAt', priority: 1, header: 'Diễn ra lúc', width: '12rem', cell: (row) => dateTime(row.eventAt) },
            { key: 'location', priority: 2, header: 'Địa điểm', cell: (row) => row.location },
          ]}
          rows={feed.data?.events ?? []}
          getKey={(row) => row.id}
          loading={feed.isLoading}
          onRowClick={(row) => setOpenId(row.id)}
          emptyTitle="Chưa có sự kiện nào sắp tới"
          density="compact"
        />
      </Panel>

      <Panel title="Tin công ty" meta={feed.data ? `${feed.data.news.length} tin` : undefined}>
        <DataTable
          columns={[
            {
              key: 'title',
              priority: 1,
              header: 'Tiêu đề',
              cell: (row) => (
                <span className="flex flex-col">
                  <span className="font-medium">{row.title}</span>
                  {row.summary && <span className="text-xs text-ink-3">{row.summary}</span>}
                </span>
              ),
            },
            { key: 'author', priority: 2, header: 'Người đăng', width: '10rem', cell: (row) => row.authorName || row.authorUsername },
            { key: 'createdAt', priority: 1, header: 'Đăng lúc', width: '10rem', cell: (row) => dateTime(row.createdAt) },
          ]}
          rows={feed.data?.news ?? []}
          getKey={(row) => row.id}
          loading={feed.isLoading}
          error={feed.error ? errorMessage(feed.error) : undefined}
          onRowClick={(row) => setOpenId(row.id)}
          emptyTitle="Chưa có tin nào"
          density="compact"
        />
      </Panel>

      <Drawer open={!!open} onClose={() => setOpenId(null)} width="lg" title={open?.title ?? 'Bài viết'}>
        {open && (
          <div className="flex flex-col gap-3 p-3">
            {open.coverImage && <img src={open.coverImage} alt="" className="rounded-sm border border-line" />}
            <p className="text-xs text-ink-3">
              {open.authorName || open.authorUsername} · {dateTime(open.eventAt ?? open.createdAt)}
              {open.location ? ` · ${open.location}` : ''}
            </p>
            <p className="whitespace-pre-wrap text-sm text-ink-2">{open.body}</p>
          </div>
        )}
      </Drawer>
    </Stack>
  )
}

function PostComposer({
  initial,
  defaultKind,
  onClose,
}: {
  initial?: PortalPost
  defaultKind: string
  onClose: () => void
}) {
  const toast = useToast()
  const save = useSavePost()
  const [kind, setKind] = useState(initial?.kind ?? defaultKind)
  const [title, setTitle] = useState(initial?.title ?? '')
  const [summary, setSummary] = useState(initial?.summary ?? '')
  const [body, setBody] = useState(initial?.body ?? '')
  const [location, setLocation] = useState(initial?.location ?? '')
  const [eventDate, setEventDate] = useState(initial?.eventAt ? initial.eventAt.slice(0, 10) : '')
  const [eventTime, setEventTime] = useState(initial?.eventAt ? initial.eventAt.slice(11, 16) : '08:00')
  const [pinned, setPinned] = useState(initial?.pinned ?? false)
  const [published, setPublished] = useState(initial?.published ?? true)
  const [error, setError] = useState<string | null>(null)

  const valid = title.trim().length > 0 && (kind !== 'event' || !!eventDate)

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      size="lg"
      title={initial ? 'Sửa bài viết' : 'Viết bài mới'}
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            disabled={!valid}
            loading={save.isPending}
            onClick={async () => {
              setError(null)
              try {
                await save.mutateAsync({
                  id: initial?.id,
                  body: {
                    kind,
                    title: title.trim(),
                    summary: summary.trim(),
                    body: body.trim(),
                    coverImage: initial?.coverImage ?? null,
                    location: location.trim(),
                    eventAt: kind === 'event' && eventDate ? `${eventDate}T${eventTime || '08:00'}:00` : null,
                    pinned,
                    published,
                  },
                })
                toast.success(initial ? 'Đã cập nhật bài' : 'Đã đăng bài')
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không lưu được bài.'))
              }
            }}
          >
            {initial ? 'Lưu thay đổi' : 'Đăng bài'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <FormGrid cols={2}>
          <Field label="Loại bài" required>
            <Select value={kind} onChange={(e) => setKind(e.target.value)}>
              <option value="news">Tin tức</option>
              <option value="event">Sự kiện</option>
            </Select>
          </Field>
          <Field label="Tiêu đề" required>
            <Input value={title} onChange={(e) => setTitle(e.target.value)} autoFocus />
          </Field>
        </FormGrid>
        <Field label="Tóm tắt" hint="Một hai câu hiện ở danh sách trên ứng dụng.">
          <Textarea rows={2} value={summary} onChange={(e) => setSummary(e.target.value)} />
        </Field>
        <Field label="Nội dung">
          <Textarea rows={8} value={body} onChange={(e) => setBody(e.target.value)} />
        </Field>
        {kind === 'event' && (
          <FormGrid cols={3}>
            <Field label="Ngày diễn ra" required>
              <DatePicker value={eventDate} onChange={setEventDate} />
            </Field>
            <Field label="Giờ diễn ra">
              <Input type="time" value={eventTime} onChange={(e) => setEventTime(e.target.value)} />
            </Field>
            <Field label="Địa điểm">
              <Input value={location} onChange={(e) => setLocation(e.target.value)} />
            </Field>
          </FormGrid>
        )}
        <div className="flex flex-wrap gap-4">
          <Checkbox label="Đăng ngay" checked={published} onChange={(e) => setPublished(e.target.checked)} />
          <Checkbox label="Ghim lên đầu" checked={pinned} onChange={(e) => setPinned(e.target.checked)} />
        </div>
      </div>
    </Modal>
  )
}

function AboutModal({ initial, onClose }: { initial: { title: string; content: string; coverImage: string | null; address: string; hotline: string; email: string; website: string }; onClose: () => void }) {
  const toast = useToast()
  const save = useSaveAbout()
  const [draft, setDraft] = useState(initial)
  const patch = (change: Partial<typeof initial>) => setDraft((d) => ({ ...d, ...change }))

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      size="lg"
      title="Giới thiệu công ty"
      description="Phần này hiện ở đầu cổng thông tin trên ứng dụng."
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            loading={save.isPending}
            onClick={async () => {
              try {
                await save.mutateAsync(draft)
                toast.success('Đã lưu giới thiệu')
                onClose()
              } catch (e) {
                toast.error('Không lưu được', errorMessage(e))
              }
            }}
          >
            Lưu
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        <Field label="Tiêu đề">
          <Input value={draft.title} onChange={(e) => patch({ title: e.target.value })} />
        </Field>
        <Field label="Nội dung giới thiệu">
          <Textarea rows={6} value={draft.content} onChange={(e) => patch({ content: e.target.value })} />
        </Field>
        <FormGrid cols={2}>
          <Field label="Địa chỉ">
            <Input value={draft.address} onChange={(e) => patch({ address: e.target.value })} />
          </Field>
          <Field label="Đường dây nóng">
            <Input value={draft.hotline} onChange={(e) => patch({ hotline: e.target.value })} />
          </Field>
          <Field label="Thư điện tử">
            <Input value={draft.email} onChange={(e) => patch({ email: e.target.value })} />
          </Field>
          <Field label="Trang mạng">
            <Input value={draft.website} onChange={(e) => patch({ website: e.target.value })} />
          </Field>
        </FormGrid>
      </div>
    </Modal>
  )
}

/* ============================================================================
   Khảo sát & bình chọn

   Trả lời ẩn danh thực sự: máy chủ không lưu tên đăng nhập, chỉ giữ dấu vân một chiều để chặn
   gửi trùng.
   ========================================================================== */

export function SurveysPage() {
  const auth = useAuth()
  const toast = useToast()
  const canManage = auth.can(PERM.portalManage)
  const [tab, setTab] = useState(canManage ? 'all' : 'open')
  const surveys = useSurveys(canManage && tab === 'all')
  const open = useOpenSurveys()
  const close = useCloseSurvey()
  const remove = useDeleteSurvey()
  const [resultsId, setResultsId] = useState<string | null>(null)
  const [answering, setAnswering] = useState<SurveyOpen | null>(null)
  const [creating, setCreating] = useState(false)
  const [removing, setRemoving] = useState<SurveyRow | null>(null)

  const adminColumns: Column<SurveyRow>[] = [
    {
      key: 'title',
      priority: 1,
      header: 'Khảo sát',
      cell: (row) => (
        <span className="flex flex-col">
          <span className="font-medium">{row.title}</span>
          {row.description && <span className="text-xs text-ink-3">{row.description}</span>}
        </span>
      ),
      sortValue: (r) => r.title,
      truncate: true,
    },
    { key: 'openedAt', priority: 1, header: 'Mở từ', width: '10rem', cell: (row) => dateTime(row.createdAt), sortValue: (r) => r.createdAt },
    { key: 'closesAt', priority: 1, header: 'Đóng lúc', width: '10rem', cell: (row) => (row.closesAt ? dateTime(row.closesAt) : 'Không hẹn'), sortValue: (r) => r.closesAt ?? '' },
    { key: 'anonymous', priority: 2, header: 'Ẩn danh', width: '6rem', cell: (row) => (row.isAnonymous ? 'Có' : 'Không') },
    { key: 'responses', priority: 1, header: 'Lượt trả lời', align: 'right', width: '8rem', cell: (row) => row.responses, sortValue: (r) => r.responses },
    {
      key: 'status',
      priority: 1,
      header: 'Trạng thái',
      width: '8rem',
      cell: (row) => (row.isActive ? <StatusBadge tone="ok">Đang mở</StatusBadge> : <StatusBadge>Đã đóng</StatusBadge>),
      sortValue: (r) => (r.isActive ? 0 : 1),
    },
    {
      key: 'action',
      priority: 1,
      header: '',
      align: 'right',
      locked: true,
      cell: (row) => (
        <span className="row-actions flex justify-end gap-1">
          {row.isActive && (
            <Button
              size="sm"
              variant="ghost"
              onClick={async (e) => {
                e.stopPropagation()
                try {
                  await close.mutateAsync(row.id)
                  toast.success('Đã đóng khảo sát')
                } catch (err) {
                  toast.error('Không đóng được', errorMessage(err))
                }
              }}
            >
              Đóng
            </Button>
          )}
          <Button size="sm" variant="ghost" className="text-danger" onClick={(e) => { e.stopPropagation(); setRemoving(row) }}>
            Xoá
          </Button>
        </span>
      ),
    },
  ]

  const openColumns: Column<SurveyOpen>[] = [
    {
      key: 'title',
      priority: 1,
      header: 'Khảo sát',
      cell: (row) => (
        <span className="flex flex-col">
          <span className="font-medium">{row.title}</span>
          {row.description && <span className="text-xs text-ink-3">{row.description}</span>}
        </span>
      ),
      truncate: true,
    },
    { key: 'closesAt', priority: 1, header: 'Đóng lúc', width: '10rem', cell: (row) => (row.closesAt ? dateTime(row.closesAt) : 'Không hẹn') },
    { key: 'anonymous', priority: 2, header: 'Ẩn danh', width: '6rem', cell: (row) => (row.isAnonymous ? 'Có' : 'Không') },
    {
      key: 'state',
      priority: 1,
      header: 'Trạng thái',
      width: '9rem',
      cell: (row) => (row.responded ? <StatusBadge tone="ok">Bạn đã trả lời</StatusBadge> : <StatusBadge tone="brand">Chờ bạn trả lời</StatusBadge>),
    },
  ]

  return (
    <>
      {tab === 'all' && canManage ? (
        <ModuleScreen
          figures={
            <FigureStrip>
              <Figure label="Khảo sát đang mở" value={surveys.data ? surveys.data.filter((s) => s.isActive).length : '…'} />
              <Figure label="Tổng lượt trả lời" value={surveys.data ? surveys.data.reduce((s, x) => s + x.responses, 0) : '…'} />
              <Figure label="Chờ bạn trả lời" value={open.data ? open.data.filter((s) => !s.responded).length : '…'} />
            </FigureStrip>
          }
          tabs={[
            { id: 'all', label: 'Quản trị khảo sát', count: surveys.data?.length },
            { id: 'open', label: 'Đang mở cho tôi', count: open.data?.filter((s) => !s.responded).length },
          ]}
          tab={tab}
          onTabChange={setTab}
          actions={
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setCreating(true)}>
              Tạo khảo sát
            </Button>
          }
          columns={adminColumns}
          rows={surveys.data ?? []}
          getKey={(row) => row.id}
          loading={surveys.isLoading}
          error={surveys.error}
          onRefresh={() => surveys.refetch()}
          onRowClick={(row) => setResultsId(row.id)}
          activeKey={resultsId}
          defaultSort={{ key: 'openedAt', dir: 'desc' }}
          emptyTitle="Chưa có khảo sát nào"
        />
      ) : (
        <ModuleScreen
          tabs={
            canManage
              ? [
                  { id: 'all', label: 'Quản trị khảo sát' },
                  { id: 'open', label: 'Đang mở cho tôi', count: open.data?.filter((s) => !s.responded).length },
                ]
              : undefined
          }
          tab={tab}
          onTabChange={setTab}
          columns={openColumns}
          rows={open.data ?? []}
          getKey={(row) => row.id}
          loading={open.isLoading}
          error={open.error}
          onRefresh={() => open.refetch()}
          onRowClick={(row) => !row.responded && setAnswering(row)}
          emptyTitle="Không có khảo sát nào đang mở"
        />
      )}

      {resultsId && <SurveyResultsDrawer id={resultsId} onClose={() => setResultsId(null)} />}
      {answering && <SurveyAnswerModal survey={answering} onClose={() => setAnswering(null)} />}
      {creating && <SurveyComposer onClose={() => setCreating(false)} />}

      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        title={`Xoá khảo sát "${removing?.title ?? ''}"`}
        message="Câu hỏi và toàn bộ phản hồi biến mất theo."
        confirmLabel="Xoá khảo sát"
        tone="danger"
        busy={remove.isPending}
        onConfirm={async () => {
          if (!removing) return
          try {
            await remove.mutateAsync(removing.id)
            toast.success('Đã xoá khảo sát')
            setRemoving(null)
          } catch (e) {
            toast.error('Không xoá được', errorMessage(e))
          }
        }}
      />
    </>
  )
}

function SurveyResultsDrawer({ id, onClose }: { id: string; onClose: () => void }) {
  const results = useSurveyResults(id)

  return (
    <Drawer open onClose={onClose} width="lg" title="Kết quả khảo sát" meta={results.data ? `${results.data.total} lượt trả lời` : undefined}>
      <div className="flex flex-col gap-3 p-3">
        {results.isLoading && <p className="text-sm text-ink-3">Đang tổng hợp…</p>}
        {results.error && <InlineAlert tone="danger">{errorMessage(results.error)}</InlineAlert>}
        {(results.data?.results ?? []).map((q) => (
          <Panel key={q.questionId} title={q.question} meta={SURVEY_TYPE_LABELS[q.qtype] ?? q.qtype} padded>
            {q.qtype === 'rating' ? (
              <p className="text-sm text-ink-2">Điểm trung bình: {q.ratingAvg ?? 'chưa có'}</p>
            ) : q.qtype === 'text' ? (
              <ul className="flex flex-col gap-1 text-sm text-ink-2">
                {q.texts.length === 0 && <li className="text-ink-3">Chưa có câu trả lời.</li>}
                {q.texts.map((t, i) => (
                  <li key={i}>{t}</li>
                ))}
              </ul>
            ) : (
              <ul className="flex flex-col gap-1.5 text-sm">
                {q.options.map((option, i) => {
                  const count = q.optionCounts[i] ?? 0
                  const share = results.data?.total ? Math.round((count / results.data.total) * 100) : 0
                  return (
                    <li key={i} className="flex items-center gap-2">
                      <span className="min-w-40 flex-1 text-ink-2">{option}</span>
                      <span className="h-1.5 flex-1 rounded-sm bg-panel-3">
                        <span className="block h-full rounded-sm bg-brand" style={{ width: `${share}%` }} />
                      </span>
                      <span className="w-20 text-right tnum text-ink-3">
                        {count} · {share}%
                      </span>
                    </li>
                  )
                })}
              </ul>
            )}
          </Panel>
        ))}
      </div>
    </Drawer>
  )
}

function SurveyAnswerModal({ survey, onClose }: { survey: SurveyOpen; onClose: () => void }) {
  const toast = useToast()
  const detail = useSurvey(survey.id)
  const respond = useRespondSurvey()
  const [answers, setAnswers] = useState<Record<string, { answer: string; optionIndices: number[] }>>({})
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const questions = detail.data?.questions ?? []
  const valueOf = (id: string) => answers[id] ?? { answer: '', optionIndices: [] }
  const missing = (q: { id: string; required: boolean; qtype: string }) => {
    if (!q.required) return null
    const value = valueOf(q.id)
    const empty = q.qtype === 'single' || q.qtype === 'multi' ? value.optionIndices.length === 0 : !value.answer.trim()
    return empty ? 'Câu này bắt buộc' : null
  }
  const valid = questions.every((q) => !missing(q))

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      size="lg"
      title={survey.title}
      description={survey.isAnonymous ? 'Câu trả lời ẩn danh: hệ thống không lưu tên bạn.' : survey.description}
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={respond.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            loading={respond.isPending}
            onClick={async () => {
              setTouched(true)
              if (!valid) return
              setError(null)
              try {
                await respond.mutateAsync({
                  id: survey.id,
                  answers: questions.map((q) => ({ questionId: q.id, ...valueOf(q.id) })),
                })
                toast.success('Đã gửi câu trả lời')
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không gửi được câu trả lời.'))
              }
            }}
          >
            Gửi câu trả lời
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        {detail.isLoading && <p className="text-sm text-ink-3">Đang tải câu hỏi…</p>}
        {questions.map((q) => (
          <Field key={q.id} label={q.question} required={q.required} error={touched ? missing(q) : null}>
            {q.qtype === 'text' ? (
              <Textarea
                rows={2}
                value={valueOf(q.id).answer}
                onChange={(e) => setAnswers((a) => ({ ...a, [q.id]: { answer: e.target.value, optionIndices: [] } }))}
              />
            ) : q.qtype === 'rating' ? (
              <NumberInput
                value={valueOf(q.id).answer ? Number(valueOf(q.id).answer) : null}
                onChange={(v) => setAnswers((a) => ({ ...a, [q.id]: { answer: v == null ? '' : String(v), optionIndices: [] } }))}
              />
            ) : q.qtype === 'multi' ? (
              <div className="flex flex-wrap gap-3 pt-1">
                {q.options.map((option, i) => (
                  <Checkbox
                    key={i}
                    label={option}
                    checked={valueOf(q.id).optionIndices.includes(i)}
                    onChange={(e) =>
                      setAnswers((a) => {
                        const current = valueOf(q.id).optionIndices
                        const next = e.target.checked ? [...current, i] : current.filter((x) => x !== i)
                        return { ...a, [q.id]: { answer: '', optionIndices: next } }
                      })
                    }
                  />
                ))}
              </div>
            ) : (
              <Select
                value={valueOf(q.id).optionIndices[0] ?? ''}
                onChange={(e) =>
                  setAnswers((a) => ({ ...a, [q.id]: { answer: '', optionIndices: e.target.value === '' ? [] : [Number(e.target.value)] } }))
                }
              >
                <option value="">Chọn…</option>
                {q.options.map((option, i) => (
                  <option key={i} value={i}>
                    {option}
                  </option>
                ))}
              </Select>
            )}
          </Field>
        ))}
      </div>
    </Modal>
  )
}

interface DraftQuestion {
  key: number
  question: string
  qtype: string
  options: string
  required: boolean
}

function SurveyComposer({ onClose }: { onClose: () => void }) {
  const toast = useToast()
  const create = useCreateSurvey()
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [anonymous, setAnonymous] = useState(true)
  const [allowMultiple, setAllowMultiple] = useState(false)
  const [closesAt, setClosesAt] = useState('')
  const [questions, setQuestions] = useState<DraftQuestion[]>([
    { key: 1, question: '', qtype: 'single', options: '', required: true },
  ])
  const [error, setError] = useState<string | null>(null)

  const patch = (key: number, change: Partial<DraftQuestion>) =>
    setQuestions((list) => list.map((q) => (q.key === key ? { ...q, ...change } : q)))
  const filled = questions.filter((q) => q.question.trim())
  const valid = title.trim().length > 0 && filled.length > 0

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      size="lg"
      title="Tạo khảo sát"
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={create.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            disabled={!valid}
            loading={create.isPending}
            onClick={async () => {
              setError(null)
              const body: CreateSurveyRequest = {
                title: title.trim(),
                description: description.trim(),
                isAnonymous: anonymous,
                allowMultiple,
                closesAt: closesAt ? `${closesAt}T23:59:00` : null,
                questions: filled.map((q) => ({
                  question: q.question.trim(),
                  qtype: q.qtype,
                  options: q.options
                    .split('\n')
                    .map((o) => o.trim())
                    .filter(Boolean),
                  required: q.required,
                })),
              }
              try {
                await create.mutateAsync(body)
                toast.success('Đã tạo khảo sát')
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không tạo được khảo sát.'))
              }
            }}
          >
            Tạo khảo sát
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <FormGrid cols={2}>
          <Field label="Tiêu đề" required>
            <Input value={title} onChange={(e) => setTitle(e.target.value)} autoFocus />
          </Field>
          <Field label="Đóng vào ngày" hint="Để trống là mở tới khi bạn tự đóng.">
            <DatePicker value={closesAt} onChange={setClosesAt} />
          </Field>
        </FormGrid>
        <Field label="Mô tả">
          <Textarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <div className="flex flex-wrap gap-4">
          <Checkbox label="Trả lời ẩn danh" checked={anonymous} onChange={(e) => setAnonymous(e.target.checked)} />
          <Checkbox label="Cho phép trả lời nhiều lần" checked={allowMultiple} onChange={(e) => setAllowMultiple(e.target.checked)} />
        </div>

        <Panel
          title="Câu hỏi"
          meta={`${filled.length} câu`}
          actions={
            <Button
              size="sm"
              onClick={() => setQuestions((list) => [...list, { key: Date.now(), question: '', qtype: 'single', options: '', required: true }])}
            >
              Thêm câu hỏi
            </Button>
          }
          padded
        >
          <div className="flex flex-col gap-4">
            {questions.map((q, index) => (
              <div key={q.key} className="flex flex-col gap-2.5 border-b border-line-2 pb-3 last:border-0 last:pb-0">
                <FormGrid cols={3}>
                  <Field label={`Câu ${index + 1}`} className="md:col-span-2">
                    <Input value={q.question} onChange={(e) => patch(q.key, { question: e.target.value })} />
                  </Field>
                  <Field label="Kiểu trả lời">
                    <Select value={q.qtype} onChange={(e) => patch(q.key, { qtype: e.target.value })}>
                      {Object.entries(SURVEY_TYPE_LABELS).map(([value, label]) => (
                        <option key={value} value={value}>
                          {label}
                        </option>
                      ))}
                    </Select>
                  </Field>
                </FormGrid>
                {(q.qtype === 'single' || q.qtype === 'multi') && (
                  <Field label="Các lựa chọn" hint="Mỗi dòng một lựa chọn.">
                    <Textarea rows={3} value={q.options} onChange={(e) => patch(q.key, { options: e.target.value })} />
                  </Field>
                )}
                <div className="flex items-center gap-4">
                  <Checkbox label="Bắt buộc trả lời" checked={q.required} onChange={(e) => patch(q.key, { required: e.target.checked })} />
                  {questions.length > 1 && (
                    <Button size="sm" variant="ghost" className="text-danger" onClick={() => setQuestions((list) => list.filter((x) => x.key !== q.key))}>
                      Bỏ câu này
                    </Button>
                  )}
                </div>
              </div>
            ))}
          </div>
        </Panel>
      </div>
    </Modal>
  )
}

/* ============================================================================
   Trung tâm trợ giúp
   ========================================================================== */

export function HelpPage() {
  const auth = useAuth()
  const toast = useToast()
  const canManage = auth.can(PERM.portalManage)
  const faqs = useFaqs()
  const status = useHelpStatus()
  const remove = useDeleteFaq()
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [composer, setComposer] = useState<null | { faq?: Faq }>(null)
  const [removing, setRemoving] = useState<Faq | null>(null)

  const all = faqs.data ?? []
  const categories = [...new Set(all.map((f) => f.category).filter(Boolean))]
  const rows = useMemo(
    () =>
      all.filter((f) => {
        if (category && f.category !== category) return false
        if (search && !matches(`${f.question} ${f.answer} ${f.category}`, search)) return false
        return true
      }),
    [all, category, search],
  )

  const columns: Column<Faq>[] = [
    {
      key: 'question',
      priority: 1,
      header: 'Câu hỏi',
      cell: (row) => (
        <span className="flex flex-col">
          <span className="font-medium">{row.question}</span>
          <span className="text-xs text-ink-3">{row.answer}</span>
        </span>
      ),
      sortValue: (r) => r.question,
      truncate: true,
    },
    { key: 'category', priority: 1, header: 'Nhóm', width: '10rem', cell: (row) => row.category || '—', sortValue: (r) => r.category },
    { key: 'orderNo', priority: 3, header: 'Thứ tự', align: 'right', width: '5rem', cell: (row) => row.orderNo, hidden: true },
    ...(canManage
      ? ([
          {
            key: 'published',
            priority: 1,
            header: 'Trạng thái',
            width: '8rem',
            cell: (row) => (row.isPublished ? <StatusBadge tone="ok">Đang hiện</StatusBadge> : <StatusBadge>Ẩn</StatusBadge>),
          },
          {
            key: 'action',
            priority: 1,
            header: '',
            align: 'right',
            locked: true,
            cell: (row) => (
              <span className="row-actions flex justify-end gap-1">
                <Button size="sm" variant="ghost" onClick={(e) => { e.stopPropagation(); setComposer({ faq: row }) }}>
                  Sửa
                </Button>
                <Button size="sm" variant="ghost" className="text-danger" onClick={(e) => { e.stopPropagation(); setRemoving(row) }}>
                  Xoá
                </Button>
              </span>
            ),
          },
        ] as Column<Faq>[])
      : []),
  ]

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Câu hỏi thường gặp" value={faqs.data ? all.length : '…'} />
            <Figure
              label="Cơ sở dữ liệu"
              value={status.data ? (status.data.db === 'ok' ? 'Bình thường' : 'Có sự cố') : '…'}
              tone={status.data?.db === 'ok' ? 'ok' : status.data ? 'danger' : undefined}
            />
            <Figure label="Giờ máy chủ" value={status.data ? dateTime(status.data.serverTime) : '…'} />
          </FigureStrip>
        }
        actions={
          canManage && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposer({})}>
              Thêm câu hỏi
            </Button>
          )
        }
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Câu hỏi, nội dung"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
            <Select size="sm" className="w-44" value={category} onChange={(e) => setCategory(e.target.value)}>
              <option value="">Mọi nhóm</option>
              {categories.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </Select>
          </>
        }
        columns={columns}
        rows={rows}
        getKey={(row) => row.id}
        loading={faqs.isLoading}
        error={faqs.error}
        onRefresh={() => faqs.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        emptyTitle="Chưa có câu hỏi nào"
      />

      <Drawer
        open={!!openId}
        onClose={() => setOpenId(null)}
        title={all.find((f) => f.id === openId)?.question ?? 'Câu hỏi'}
        meta={all.find((f) => f.id === openId)?.category}
      >
        <div className="p-3">
          <p className="whitespace-pre-wrap text-sm text-ink-2">{all.find((f) => f.id === openId)?.answer}</p>
        </div>
      </Drawer>

      {composer && <FaqModal initial={composer.faq} onClose={() => setComposer(null)} />}

      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        title="Xoá câu hỏi"
        message={removing?.question}
        confirmLabel="Xoá"
        tone="danger"
        busy={remove.isPending}
        onConfirm={async () => {
          if (!removing) return
          try {
            await remove.mutateAsync(removing.id)
            toast.success('Đã xoá câu hỏi')
            setRemoving(null)
          } catch (e) {
            toast.error('Không xoá được', errorMessage(e))
          }
        }}
      />
    </>
  )
}

function FaqModal({ initial, onClose }: { initial?: Faq; onClose: () => void }) {
  const toast = useToast()
  const save = useSaveFaq()
  const [category, setCategory] = useState(initial?.category ?? '')
  const [question, setQuestion] = useState(initial?.question ?? '')
  const [answer, setAnswer] = useState(initial?.answer ?? '')
  const [orderNo, setOrderNo] = useState<number | null>(initial?.orderNo ?? 0)
  const [isPublished, setIsPublished] = useState(initial?.isPublished ?? true)
  const [error, setError] = useState<string | null>(null)

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      title={initial ? 'Sửa câu hỏi' : 'Thêm câu hỏi'}
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            disabled={!question.trim()}
            loading={save.isPending}
            onClick={async () => {
              setError(null)
              try {
                await save.mutateAsync({
                  id: initial?.id,
                  body: { category: category.trim(), question: question.trim(), answer, orderNo: orderNo ?? 0, isPublished },
                })
                toast.success(initial ? 'Đã cập nhật câu hỏi' : 'Đã thêm câu hỏi')
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không lưu được câu hỏi.'))
              }
            }}
          >
            Lưu
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <FormGrid cols={2}>
          <Field label="Nhóm" hint="Ví dụ: Chấm công, Lương, Đơn từ">
            <Input value={category} onChange={(e) => setCategory(e.target.value)} />
          </Field>
          <Field label="Thứ tự hiện" hint="Số nhỏ đứng trước.">
            <NumberInput value={orderNo} onChange={setOrderNo} />
          </Field>
        </FormGrid>
        <Field label="Câu hỏi" required>
          <Input value={question} onChange={(e) => setQuestion(e.target.value)} autoFocus />
        </Field>
        <Field label="Trả lời">
          <Textarea rows={6} value={answer} onChange={(e) => setAnswer(e.target.value)} />
        </Field>
        <Checkbox label="Hiện cho mọi người" checked={isPublished} onChange={(e) => setIsPublished(e.target.checked)} />
      </div>
    </Modal>
  )
}

/* ============================================================================
   Phản hồi & hỗ trợ
   ========================================================================== */

export function FeedbackPage() {
  const auth = useAuth()
  const toast = useToast()
  const canResolve = auth.can(PERM.usersManage)
  const [tab, setTab] = useState('attendance')
  const [search, setSearch] = useState('')
  const [sending, setSending] = useState<null | 'general' | 'support'>(null)
  const [resolving, setResolving] = useState<FeedbackItem | null>(null)

  const attendance = useFeedback()
  const general = useMyGeneralFeedback()
  const support = useMySupportTickets()
  const resolve = useResolveFeedback()

  const attendanceRows = useMemo(
    () =>
      (attendance.data ?? []).filter(
        (f) => !search || matches(`${f.reporterName} ${f.targetName} ${f.reason} ${f.typeLabel}`, search),
      ),
    [attendance.data, search],
  )

  const attendanceColumns: Column<FeedbackItem>[] = [
    { key: 'createdAt', priority: 1, header: 'Gửi lúc', width: '10rem', cell: (row) => dateTime(row.createdAt), sortValue: (r) => r.createdAt },
    { key: 'sender', priority: 1, header: 'Người gửi', width: '11rem', cell: (row) => row.reporterName || row.reporterUsername, sortValue: (r) => r.reporterName },
    { key: 'type', priority: 2, header: 'Loại', width: '11rem', cell: (row) => row.typeLabel },
    { key: 'target', priority: 1, header: 'Người bị chấm nhầm', cell: (row) => row.targetName },
    { key: 'reason', priority: 1, header: 'Nội dung', cell: (row) => row.reason, truncate: true },
    ...(canResolve
      ? ([
          {
            key: 'action',
            priority: 1,
            header: '',
            align: 'right',
            locked: true,
            cell: (row) => (
              <span className="row-actions">
                <Button size="sm" variant="ghost" onClick={() => setResolving(row)}>
                  Đã xử lý
                </Button>
              </span>
            ),
          },
        ] as Column<FeedbackItem>[])
      : []),
  ]

  return (
    <>
      {tab === 'attendance' && (
        <ModuleScreen
          figures={
            <FigureStrip>
              <Figure label="Khiếu nại chấm công" value={attendance.data ? attendance.data.length : '…'} tone={attendance.data?.length ? 'warn' : undefined} />
              <Figure label="Góp ý bạn đã gửi" value={general.data ? general.data.length : '…'} />
              <Figure label="Yêu cầu hỗ trợ của bạn" value={support.data ? support.data.length : '…'} />
            </FigureStrip>
          }
          tabs={[
            { id: 'attendance', label: 'Khiếu nại chấm công', count: attendance.data?.length },
            { id: 'general', label: 'Góp ý của tôi', count: general.data?.length },
            { id: 'support', label: 'Yêu cầu hỗ trợ', count: support.data?.length },
          ]}
          tab={tab}
          onTabChange={setTab}
          actions={
            <>
              <Button size="sm" onClick={() => setSending('general')}>
                Gửi góp ý
              </Button>
              <Button variant="primary" size="sm" onClick={() => setSending('support')}>
                Yêu cầu hỗ trợ
              </Button>
            </>
          }
          filters={
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Người gửi, nội dung"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
          }
          columns={attendanceColumns}
          rows={attendanceRows}
          getKey={(row) => row.id}
          loading={attendance.isLoading}
          error={attendance.error}
          onRefresh={() => attendance.refetch()}
          defaultSort={{ key: 'createdAt', dir: 'desc' }}
          emptyTitle="Không có khiếu nại nào"
        />
      )}

      {tab === 'general' && (
        <ModuleScreen
          tabs={[
            { id: 'attendance', label: 'Khiếu nại chấm công', count: attendance.data?.length },
            { id: 'general', label: 'Góp ý của tôi', count: general.data?.length },
            { id: 'support', label: 'Yêu cầu hỗ trợ', count: support.data?.length },
          ]}
          tab={tab}
          onTabChange={setTab}
          actions={
            <Button variant="primary" size="sm" onClick={() => setSending('general')}>
              Gửi góp ý
            </Button>
          }
          columns={[
            { key: 'createdAt', priority: 1, header: 'Gửi lúc', width: '10rem', cell: (row) => dateTime(row.createdAt), sortValue: (r) => r.createdAt },
            { key: 'message', priority: 1, header: 'Nội dung', cell: (row) => row.message, truncate: true },
            { key: 'response', priority: 1, header: 'Trả lời', cell: (row) => row.response || '—', truncate: true },
            {
              key: 'status',
              priority: 1,
              header: 'Trạng thái',
              width: '8rem',
              cell: (row) => (row.status === 'open' ? <StatusBadge tone="warn">Đang xem xét</StatusBadge> : <StatusBadge tone="ok">Đã xử lý</StatusBadge>),
            },
          ]}
          rows={general.data ?? []}
          getKey={(row) => row.id}
          loading={general.isLoading}
          error={general.error}
          onRefresh={() => general.refetch()}
          defaultSort={{ key: 'createdAt', dir: 'desc' }}
          emptyTitle="Bạn chưa gửi góp ý nào"
          emptyDescription="Góp ý gửi ẩn danh không hiện ở đây vì hệ thống không lưu tên bạn."
        />
      )}

      {tab === 'support' && (
        <ModuleScreen
          tabs={[
            { id: 'attendance', label: 'Khiếu nại chấm công', count: attendance.data?.length },
            { id: 'general', label: 'Góp ý của tôi', count: general.data?.length },
            { id: 'support', label: 'Yêu cầu hỗ trợ', count: support.data?.length },
          ]}
          tab={tab}
          onTabChange={setTab}
          actions={
            <Button variant="primary" size="sm" onClick={() => setSending('support')}>
              Yêu cầu hỗ trợ
            </Button>
          }
          columns={[
            { key: 'code', priority: 1, header: 'Mã theo dõi', width: '9rem', cell: (row) => <span className="font-medium tnum">{row.code}</span> },
            { key: 'createdAt', priority: 1, header: 'Gửi lúc', width: '10rem', cell: (row) => dateTime(row.createdAt), sortValue: (r) => r.createdAt },
            { key: 'message', priority: 1, header: 'Nội dung', cell: (row) => row.message, truncate: true },
            { key: 'response', priority: 1, header: 'Trả lời', cell: (row) => row.response || '—', truncate: true },
            {
              key: 'status',
              priority: 1,
              header: 'Trạng thái',
              width: '8rem',
              cell: (row) => (row.status === 'open' ? <StatusBadge tone="warn">Đang xử lý</StatusBadge> : <StatusBadge tone="ok">Đã xong</StatusBadge>),
            },
          ]}
          rows={support.data ?? []}
          getKey={(row) => row.id}
          loading={support.isLoading}
          error={support.error}
          onRefresh={() => support.refetch()}
          defaultSort={{ key: 'createdAt', dir: 'desc' }}
          emptyTitle="Bạn chưa gửi yêu cầu hỗ trợ nào"
        />
      )}

      {sending && <SendFeedbackModal mode={sending} onClose={() => setSending(null)} />}

      <ConfirmDialog
        open={!!resolving}
        onClose={() => setResolving(null)}
        title="Đánh dấu đã xử lý"
        message={resolving ? `Khiếu nại của ${resolving.reporterName || resolving.reporterUsername} sẽ được gỡ khỏi danh sách.` : undefined}
        confirmLabel="Đã xử lý"
        busy={resolve.isPending}
        onConfirm={async () => {
          if (!resolving) return
          try {
            await resolve.mutateAsync(resolving.id)
            toast.success('Đã đánh dấu xử lý')
            setResolving(null)
          } catch (e) {
            toast.error('Không cập nhật được', errorMessage(e))
          }
        }}
      />
    </>
  )
}

function SendFeedbackModal({ mode, onClose }: { mode: 'general' | 'support'; onClose: () => void }) {
  const toast = useToast()
  const sendGeneral = useSendGeneralFeedback()
  const sendSupport = useSendSupportTicket()
  const [message, setMessage] = useState('')
  const [anonymous, setAnonymous] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const busy = sendGeneral.isPending || sendSupport.isPending

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      title={mode === 'general' ? 'Gửi góp ý' : 'Yêu cầu hỗ trợ'}
      description={
        mode === 'general'
          ? 'Góp ý về quy trình, giao diện hay bất cứ điều gì bạn thấy nên cải thiện.'
          : 'Mô tả sự cố bạn gặp. Bạn sẽ nhận được một mã để theo dõi.'
      }
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={busy}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            disabled={!message.trim()}
            loading={busy}
            onClick={async () => {
              setError(null)
              try {
                if (mode === 'general') {
                  await sendGeneral.mutateAsync({ message: message.trim(), anonymous })
                  toast.success(anonymous ? 'Đã gửi góp ý ẩn danh' : 'Đã gửi góp ý')
                } else {
                  const result = await sendSupport.mutateAsync({
                    message: message.trim(),
                    appVersion: 'web',
                    deviceModel: navigator.userAgent.slice(0, 120),
                  })
                  toast.success(`Đã gửi yêu cầu, mã theo dõi ${result.code}`)
                }
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không gửi được.'))
              }
            }}
          >
            Gửi
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <Field label={mode === 'general' ? 'Nội dung góp ý' : 'Mô tả sự cố'} required>
          <Textarea rows={5} value={message} onChange={(e) => setMessage(e.target.value)} autoFocus />
        </Field>
        {mode === 'general' && (
          <Field hint="Gửi ẩn danh thì hệ thống không lưu tên bạn, và bạn cũng không tra lại được góp ý này.">
            <Checkbox label="Gửi ẩn danh" checked={anonymous} onChange={(e) => setAnonymous(e.target.checked)} />
          </Field>
        )}
      </div>
    </Modal>
  )
}
