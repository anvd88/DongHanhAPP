import { useMemo, useState } from 'react'
import { Plus } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { dateTime, todayISO, toISODate, vnd } from '@/lib/format'
import { matches } from '@/lib/text'
import { useEmployees } from '@/api/hr'
import {
  TASK_EVENT_LABELS,
  TASK_PRIORITY_LABELS,
  TASK_PRIORITY_TONES,
  requestStatus,
  taskStage,
  useCancelRequest,
  useDecideRequest,
  useDeleteTask,
  useRemindRequest,
  useRequest,
  useRequestTypes,
  useRequests,
  useSaveDelegation,
  useSaveRequest,
  useSaveTask,
  useTask,
  useTaskAction,
  useTaskBoard,
  useTaskHistory,
  useTaskMeta,
  type RequestDetail,
  type RequestField,
  type RequestRow,
  type SaveTaskRequest,
  type WorkTask,
} from '@/api/work'
import {
  Button,
  Checkbox,
  Combobox,
  ConfirmDialog,
  DataTable,
  DatePicker,
  DateRangePicker,
  Drawer,
  Field,
  Figure,
  FigureStrip,
  FormGrid,
  InlineAlert,
  Input,
  KeyValue,
  Modal,
  Money,
  NumberInput,
  Panel,
  SearchInput,
  Select,
  StatusBadge,
  Textarea,
  useToast,
  type Column,
  type DateRange,
} from '@/ui'
import { ModuleScreen, errorMessage } from './_shared'

/* ============================================================================
   Giao việc

   Vòng đời: giao → đang làm → đã nộp → nghiệm thu. Việc giao hàng đi đường ngắn hơn, không có
   chặng nghiệm thu — nó đóng ở màn Phiếu khi kế toán nhận lại tờ phiếu ký nhận.
   ========================================================================== */

const monthAgo = () => {
  const d = new Date()
  d.setDate(d.getDate() - 30)
  return toISODate(d)
}

export function TasksPage() {
  const auth = useAuth()
  const toast = useToast()
  const board = useTaskBoard()
  const [tab, setTab] = useState('mine')
  const [search, setSearch] = useState('')
  const [source, setSource] = useState('')
  const [range, setRange] = useState<DateRange>({ from: monthAgo(), to: todayISO() })
  const [person, setPerson] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [composer, setComposer] = useState<null | { task?: WorkTask }>(null)
  const [removing, setRemoving] = useState<WorkTask | null>(null)
  const remove = useDeleteTask()

  const history = useTaskHistory(
    { from: range.from || monthAgo(), to: range.to || todayISO(), assignee: person || undefined },
    tab === 'history',
  )

  const data = board.data
  const summary = data?.summary
  const canAssign = !!data?.canAssign && auth.can(PERM.tasksAssign)

  const sourceOf = (task: WorkTask) => (task.delivery ? 'delivery' : 'manual')

  const rows = useMemo(() => {
    let list: WorkTask[] = []
    if (tab === 'mine') list = data?.inbox ?? []
    else if (tab === 'assigned') list = data?.outbox ?? []
    else if (tab === 'review') list = (data?.outbox ?? []).filter((t) => t.status === 'submitted')
    else list = history.data?.items ?? []
    return list.filter((t) => {
      if (source && sourceOf(t) !== source) return false
      if (search && !matches(`${t.taskNo} ${t.title} ${t.assigneeName} ${t.assignerName} ${t.delivery?.voucherNo ?? ''}`, search))
        return false
      return true
    })
  }, [tab, data, history.data, source, search])

  const loading = tab === 'history' ? history.isLoading : board.isLoading
  const error = tab === 'history' ? history.error : board.error

  const columns: Column<WorkTask>[] = [
    {
      key: 'title',
      priority: 1,
      header: 'Nội dung',
      cell: (row) => (
        <span className="flex flex-col">
          <span className="font-medium">{row.title}</span>
          <span className="text-xs text-ink-3">
            {row.taskNo}
            {row.delivery ? ` · Phiếu ${row.delivery.voucherNo}` : ''}
          </span>
        </span>
      ),
      sortValue: (r) => r.title,
      truncate: true,
    },
    {
      key: 'assignee',
      priority: 1,
      header: 'Người nhận',
      cell: (row) => row.assigneeName || row.assigneeUsername,
      sortValue: (r) => r.assigneeName,
    },
    {
      key: 'assigner',
      priority: 3,
      header: 'Người giao',
      cell: (row) => row.assignerName || row.assignerUsername,
      hidden: true,
    },
    {
      key: 'assignedAt',
      priority: 2,
      header: 'Giao lúc',
      width: '9rem',
      cell: (row) => dateTime(row.createdAt),
      sortValue: (r) => r.createdAt,
    },
    {
      key: 'due',
      priority: 1,
      header: 'Hạn',
      width: '9rem',
      cell: (row) =>
        row.dueAt ? <span className={row.overdue ? 'text-danger' : undefined}>{dateTime(row.dueAt)}</span> : null,
      sortValue: (r) => r.dueAt ?? '',
    },
    {
      key: 'priority',
      priority: 2,
      header: 'Ưu tiên',
      width: '6.5rem',
      cell: (row) => (
        <StatusBadge tone={TASK_PRIORITY_TONES[row.priority] ?? 'neutral'}>
          {TASK_PRIORITY_LABELS[row.priority] ?? row.priority}
        </StatusBadge>
      ),
      sortValue: (r) => r.priority,
    },
    {
      key: 'stage',
      priority: 1,
      header: 'Chặng',
      width: '9rem',
      cell: (row) => <StatusBadge tone={taskStage(row).tone}>{taskStage(row).label}</StatusBadge>,
      sortValue: (r) => taskStage(r).label,
    },
    {
      key: 'source',
      priority: 3,
      header: 'Nguồn',
      width: '7rem',
      cell: (row) => (row.delivery ? 'Giao hàng' : 'Giao tay'),
      hidden: true,
    },
  ]

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Việc của tôi cần làm" value={summary ? summary.inboxActionable : '…'} tone={summary?.inboxActionable ? 'warn' : undefined} />
            <Figure label="Chờ tôi nghiệm thu" value={summary ? summary.outboxReview : '…'} tone={summary?.outboxReview ? 'warn' : undefined} />
            <Figure label="Chờ phiếu giao hàng về" value={summary ? summary.outboxAwaitingVoucher : '…'} />
            <Figure label="Lệnh thu tiền đang giữ" value={summary ? summary.collections : '…'} to="/lenh-thu-tien" />
          </FigureStrip>
        }
        tabs={[
          { id: 'mine', label: 'Việc của tôi', count: data?.inbox.length },
          { id: 'assigned', label: 'Tôi đã giao', count: data?.outbox.length },
          { id: 'review', label: 'Chờ nghiệm thu', count: summary ? summary.outboxReview + summary.outboxAwaitingVoucher : undefined },
          { id: 'history', label: 'Lịch sử', count: tab === 'history' ? history.data?.total : undefined },
        ]}
        tab={tab}
        onTabChange={setTab}
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Nội dung việc, người nhận"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
            {tab === 'history' ? (
              <>
                <DateRangePicker value={range} onChange={setRange} size="sm" />
                <Select size="sm" className="w-44" value={person} onChange={(e) => setPerson(e.target.value)}>
                  <option value="">Mọi người nhận</option>
                  {(history.data?.people ?? []).map((p) => (
                    <option key={p.username} value={p.username}>
                      {p.fullName} ({p.count})
                    </option>
                  ))}
                </Select>
              </>
            ) : (
              <Select size="sm" className="w-40" value={source} onChange={(e) => setSource(e.target.value)}>
                <option value="">Mọi nguồn việc</option>
                <option value="manual">Giao tay</option>
                <option value="delivery">Giao hàng</option>
              </Select>
            )}
          </>
        }
        actions={
          canAssign && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposer({})}>
              Giao việc mới
            </Button>
          )
        }
        columns={columns}
        rows={rows}
        loading={loading}
        error={error}
        onRefresh={() => (tab === 'history' ? history.refetch() : board.refetch())}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'assignedAt', dir: 'desc' }}
        emptyTitle="Không có việc nào trong bộ lọc này"
      >
        {tab === 'mine' && (data?.collections.length ?? 0) > 0 && (
          <Panel title="Lệnh thu tiền bạn đang giữ" meta={`${data?.collections.length} lệnh`}>
            <DataTable
              columns={[
                { key: 'orderNo', priority: 1, header: 'Lệnh', cell: (row) => <span className="font-medium">{row.orderNo}</span> },
                { key: 'customer', priority: 1, header: 'Khách hàng', cell: (row) => row.customerName },
                { key: 'amount', priority: 1, header: 'Phải thu', align: 'right', cell: (row) => <Money value={row.expectedAmount} /> },
                { key: 'due', priority: 2, header: 'Hạn nộp về', cell: (row) => dateTime(row.handoverDueAt) },
              ]}
              rows={data?.collections ?? []}
              getKey={(row) => row.id}
              density="compact"
            />
          </Panel>
        )}
      </ModuleScreen>

      <TaskDrawer
        taskId={openId}
        onClose={() => setOpenId(null)}
        onEdit={(task) => {
          setOpenId(null)
          setComposer({ task })
        }}
        onDelete={(task) => setRemoving(task)}
      />

      {composer && <TaskComposer initial={composer.task} onClose={() => setComposer(null)} />}

      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        title={`Xoá việc ${removing?.taskNo ?? ''}`}
        message="Việc và toàn bộ dòng thời gian của nó biến mất khỏi hệ thống."
        confirmLabel="Xoá"
        tone="danger"
        busy={remove.isPending}
        onConfirm={async () => {
          if (!removing) return
          try {
            await remove.mutateAsync(removing.id)
            toast.success('Đã xoá việc')
            setRemoving(null)
            setOpenId(null)
          } catch (e) {
            toast.error('Không xoá được việc', errorMessage(e))
          }
        }}
      />
    </>
  )
}

function TaskDrawer({
  taskId,
  onClose,
  onEdit,
  onDelete,
}: {
  taskId: string | null
  onClose: () => void
  onEdit: (task: WorkTask) => void
  onDelete: (task: WorkTask) => void
}) {
  const toast = useToast()
  const detail = useTask(taskId)
  const act = useTaskAction()
  const [note, setNote] = useState('')
  const [progress, setProgress] = useState<number | null>(null)
  const [rejecting, setRejecting] = useState(false)
  const [cancelling, setCancelling] = useState(false)

  const task = detail.data?.task
  const flags = detail.data?.flags
  const stage = task ? taskStage(task) : null

  const run = async (
    action: 'start' | 'progress' | 'submit' | 'accept' | 'comment',
    body?: { note?: string; progress?: number },
    message = 'Đã ghi nhận',
  ) => {
    if (!taskId) return
    try {
      await act.mutateAsync({ id: taskId, action, body })
      toast.success(message)
      setNote('')
      setProgress(null)
    } catch (e) {
      toast.error('Không thực hiện được', errorMessage(e))
    }
  }

  return (
    <>
      <Drawer
        open={!!taskId}
        onClose={onClose}
        width="lg"
        title={task ? task.title : 'Công việc'}
        meta={
          task && (
            <>
              <span>{task.taskNo}</span>
              <span>{task.assigneeName || task.assigneeUsername}</span>
              {stage && <StatusBadge tone={stage.tone}>{stage.label}</StatusBadge>}
            </>
          )
        }
        actions={
          task && (
            <>
              {flags?.canEdit && (
                <Button size="sm" onClick={() => onEdit(task)}>
                  Sửa
                </Button>
              )}
              {flags?.canCancel && (
                <Button size="sm" variant="danger" onClick={() => setCancelling(true)}>
                  Huỷ việc
                </Button>
              )}
              {flags?.assignedByMe && !flags.canCancel && (
                <Button size="sm" variant="ghost" className="text-danger" onClick={() => onDelete(task)}>
                  Xoá
                </Button>
              )}
            </>
          )
        }
      >
        <div className="flex flex-col gap-3 p-3">
          {task?.delivery && (
            <InlineAlert tone="info" title={`Việc giao hàng theo phiếu ${task.delivery.voucherNo}`}>
              Việc giao hàng không qua nghiệm thu. Nó đóng khi kế toán xác nhận tờ phiếu ký nhận về kho.
              {task.delivery.collection && (
                <>
                  {' '}Kèm lệnh thu {task.delivery.collection.orderNo} trị giá {vnd(task.delivery.collection.expectedAmount)}.
                </>
              )}
            </InlineAlert>
          )}

          <Panel title="Thông tin việc" padded>
            <KeyValue
              rows={[
                ['Người giao', task?.assignerName || task?.assignerUsername || null],
                ['Người nhận', task?.assigneeName || task?.assigneeUsername || null],
                ['Ưu tiên', task ? TASK_PRIORITY_LABELS[task.priority] ?? task.priority : null],
                ['Hạn hoàn thành', task?.dueAt ? dateTime(task.dueAt) : null],
                ['Tiến độ', task ? `${task.progress}%` : null],
                ['Mô tả', task?.description || null],
                ['Ghi chú khi nộp', task?.submitNote || null],
                ['Nhận xét nghiệm thu', task?.reviewNote || null],
              ]}
            />
          </Panel>

          {(flags?.canStart || flags?.canSubmit || flags?.canReview || flags?.canReject) && (
            <Panel title="Việc bạn có thể làm" padded>
              <div className="flex flex-col gap-2.5">
                <Field label="Ghi chú">
                  <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} placeholder="Nội dung trao đổi hoặc ghi chú khi nộp" />
                </Field>
                {flags?.canSubmit && (
                  <Field label="Tiến độ (%)">
                    <NumberInput value={progress} onChange={setProgress} placeholder={String(task?.progress ?? 0)} />
                  </Field>
                )}
                <div className="flex flex-wrap gap-2">
                  {flags?.canStart && (
                    <Button size="sm" variant="primary" loading={act.isPending} onClick={() => run('start', undefined, 'Đã nhận việc')}>
                      Nhận việc
                    </Button>
                  )}
                  {flags?.canSubmit && (
                    <>
                      <Button size="sm" loading={act.isPending} onClick={() => run('progress', { note, progress: progress ?? undefined }, 'Đã cập nhật tiến độ')}>
                        Cập nhật tiến độ
                      </Button>
                      <Button size="sm" variant="primary" loading={act.isPending} onClick={() => run('submit', { note }, 'Đã nộp việc')}>
                        Nộp việc
                      </Button>
                    </>
                  )}
                  {flags?.canReview && (
                    <Button size="sm" variant="primary" loading={act.isPending} onClick={() => run('accept', { note }, 'Đã nghiệm thu')}>
                      Nghiệm thu đạt
                    </Button>
                  )}
                  {flags?.canReject && (
                    <Button size="sm" variant="danger" onClick={() => setRejecting(true)}>
                      Trả lại
                    </Button>
                  )}
                  <Button size="sm" variant="ghost" disabled={!note.trim()} loading={act.isPending} onClick={() => run('comment', { note }, 'Đã gửi trao đổi')}>
                    Gửi trao đổi
                  </Button>
                </div>
              </div>
            </Panel>
          )}

          <Panel title="Dòng thời gian" meta={detail.data ? `${detail.data.events.length} mốc` : undefined}>
            <DataTable
              columns={[
                { key: 'at', priority: 1, header: 'Thời điểm', width: '10rem', cell: (row) => dateTime(row.createdAt) },
                { key: 'actor', priority: 1, header: 'Người thực hiện', cell: (row) => row.actorName || row.actorUsername },
                { key: 'kind', priority: 1, header: 'Việc', width: '10rem', cell: (row) => TASK_EVENT_LABELS[row.kind] ?? row.kind },
                { key: 'note', priority: 2, header: 'Nội dung', cell: (row) => row.note },
              ]}
              rows={detail.data?.events ?? []}
              getKey={(row) => row.id}
              loading={detail.isLoading}
              density="compact"
              emptyTitle="Chưa có mốc nào"
            />
          </Panel>
        </div>
      </Drawer>

      <ConfirmDialog
        open={rejecting}
        onClose={() => setRejecting(false)}
        title="Trả lại việc"
        message="Việc quay về chặng đang làm, người nhận có thể nộp lại."
        confirmLabel="Trả lại"
        tone="danger"
        requireReason
        reasonLabel="Lý do trả lại"
        busy={act.isPending}
        onConfirm={async (reason) => {
          if (!taskId) return
          try {
            await act.mutateAsync({ id: taskId, action: 'reject', body: { note: reason } })
            toast.success('Đã trả lại việc')
            setRejecting(false)
          } catch (e) {
            toast.error('Không trả lại được', errorMessage(e))
          }
        }}
      />

      <ConfirmDialog
        open={cancelling}
        onClose={() => setCancelling(false)}
        title="Huỷ việc"
        message="Việc đóng lại và không thao tác được nữa."
        confirmLabel="Huỷ việc"
        tone="danger"
        requireReason
        reasonLabel="Lý do huỷ"
        busy={act.isPending}
        onConfirm={async (reason) => {
          if (!taskId) return
          try {
            await act.mutateAsync({ id: taskId, action: 'cancel', body: { note: reason } })
            toast.success('Đã huỷ việc')
            setCancelling(false)
            onClose()
          } catch (e) {
            toast.error('Không huỷ được việc', errorMessage(e))
          }
        }}
      />
    </>
  )
}

function TaskComposer({ initial, onClose }: { initial?: WorkTask; onClose: () => void }) {
  const toast = useToast()
  const meta = useTaskMeta()
  const save = useSaveTask()

  const [title, setTitle] = useState(initial?.title ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [assignee, setAssignee] = useState(initial?.assigneeUsername ?? '')
  const [priority, setPriority] = useState(initial?.priority ?? 'normal')
  const [dueDate, setDueDate] = useState(initial?.dueAt ? initial.dueAt.slice(0, 10) : '')
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const people = meta.data?.assignees ?? []
  const chosen = people.find((p) => p.username === assignee)
  const problems = {
    title: !title.trim() ? 'Nhập tên công việc' : null,
    assignee: !assignee ? 'Chọn người nhận việc' : chosen && !chosen.selectable ? chosen.attendanceNote : null,
  }
  const valid = !problems.title && !problems.assignee

  const submit = async () => {
    setTouched(true)
    if (!valid) return
    setError(null)
    const body: SaveTaskRequest = {
      title: title.trim(),
      description: description.trim(),
      assigneeUsername: assignee,
      priority,
      dueAt: dueDate ? `${dueDate}T17:00:00` : null,
    }
    try {
      await save.mutateAsync({ id: initial?.id, body })
      toast.success(initial ? 'Đã cập nhật việc' : 'Đã giao việc')
      onClose()
    } catch (e) {
      setError(errorMessage(e, 'Không giao được việc.'))
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      title={initial ? `Sửa việc ${initial.taskNo}` : 'Giao việc mới'}
      description="Người chưa chấm công hoặc đang nghỉ phép hôm nay không nhận việc được."
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={save.isPending} onClick={submit}>
            {initial ? 'Lưu thay đổi' : 'Giao việc'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <Field label="Tên công việc" required error={touched ? problems.title : null}>
          <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Ví dụ: Kiểm kê kho hàng tầng 2" autoFocus />
        </Field>
        <Field label="Mô tả chi tiết">
          <Textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Yêu cầu cụ thể, tài liệu cần dùng…" />
        </Field>
        <FormGrid cols={3}>
          <Field label="Người nhận việc" required error={touched ? problems.assignee : null} hint={chosen && !chosen.selectable ? chosen.attendanceNote : undefined}>
            <Combobox
              value={assignee}
              onChange={setAssignee}
              loading={meta.isLoading}
              placeholder="Chọn nhân viên"
              options={people.map((p) => ({
                value: p.username,
                label: p.fullName,
                description: [p.position, p.department, p.selectable ? '' : p.attendanceNote].filter(Boolean).join(' · '),
                disabled: !p.selectable,
              }))}
            />
          </Field>
          <Field label="Mức ưu tiên">
            <Select value={priority} onChange={(e) => setPriority(e.target.value)}>
              {(meta.data?.priorities ?? ['low', 'normal', 'high', 'urgent']).map((p) => (
                <option key={p} value={p}>
                  {TASK_PRIORITY_LABELS[p] ?? p}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Hạn hoàn thành">
            <DatePicker value={dueDate} onChange={setDueDate} />
          </Field>
        </FormGrid>
      </div>
    </Modal>
  )
}

/* ============================================================================
   Đơn từ

   Một engine cho mọi loại đơn. Danh mục loại đơn và các trường nhập do máy chủ trả về
   (/api/requests/types), nên thêm loại đơn mới không phải sửa giao diện.
   ========================================================================== */

const REQUEST_TABS: Array<{ id: string; label: string; status?: string }> = [
  { id: 'open', label: 'Đang xử lý', status: 'Pending' },
  { id: 'approved', label: 'Đã duyệt', status: 'Approved' },
  { id: 'rejected', label: 'Từ chối / huỷ' },
]

function requestColumns(showEmployee: boolean): Column<RequestRow>[] {
  return [
    {
      key: 'code',
      priority: 1,
      header: 'Mã đơn',
      width: '7rem',
      cell: (row) => <span className="font-medium tnum">{row.requestNo}</span>,
      sortValue: (r) => r.requestNo,
    },
    ...(showEmployee
      ? ([
          {
            key: 'employee',
            priority: 1,
            header: 'Người gửi',
            cell: (row) => (
              <span className="flex flex-col">
                <span>{row.employeeName || row.requesterUsername}</span>
                <span className="text-xs text-ink-3">{row.employeeCode}</span>
              </span>
            ),
            sortValue: (r) => r.employeeName,
          },
        ] as Column<RequestRow>[])
      : []),
    { key: 'type', priority: 1, header: 'Loại đơn', width: '11rem', cell: (row) => row.typeLabel, sortValue: (r) => r.typeLabel },
    { key: 'title', priority: 2, header: 'Nội dung', cell: (row) => row.title, truncate: true },
    { key: 'createdAt', priority: 1, header: 'Ngày gửi', width: '9rem', cell: (row) => dateTime(row.createdAt), sortValue: (r) => r.createdAt },
    {
      key: 'step',
      priority: 2,
      header: 'Đang ở',
      width: '7rem',
      align: 'right',
      cell: (row) => (row.status === 'Pending' ? `Bước ${row.currentStep}/${row.totalSteps}` : `${row.totalSteps} bước`),
    },
    {
      key: 'status',
      priority: 1,
      header: 'Trạng thái',
      width: '8rem',
      cell: (row) => <StatusBadge tone={requestStatus(row.status).tone}>{requestStatus(row.status).label}</StatusBadge>,
      sortValue: (r) => r.status,
    },
  ]
}

export function MyRequestsPage() {
  const list = useRequests('mine')
  const types = useRequestTypes()
  const [tab, setTab] = useState('open')
  const [type, setType] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [composing, setComposing] = useState(false)

  const all = list.data ?? []
  const rows = useMemo(
    () =>
      all.filter((r) => {
        if (tab === 'open' && r.status !== 'Pending') return false
        if (tab === 'approved' && r.status !== 'Approved') return false
        if (tab === 'rejected' && r.status !== 'Rejected' && r.status !== 'Cancelled') return false
        if (type && r.type !== type) return false
        return true
      }),
    [all, tab, type],
  )

  const count = (id: string) =>
    all.filter((r) =>
      id === 'open' ? r.status === 'Pending' : id === 'approved' ? r.status === 'Approved' : r.status === 'Rejected' || r.status === 'Cancelled',
    ).length

  return (
    <>
      <ModuleScreen
        tabs={REQUEST_TABS.map((t) => ({ id: t.id, label: t.label, count: count(t.id) }))}
        tab={tab}
        onTabChange={setTab}
        actions={
          <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposing(true)}>
            Tạo đơn
          </Button>
        }
        filters={
          <Select size="sm" className="w-48" value={type} onChange={(e) => setType(e.target.value)}>
            <option value="">Mọi loại đơn</option>
            {(types.data ?? []).map((t) => (
              <option key={t.type} value={t.type}>
                {t.label}
              </option>
            ))}
          </Select>
        }
        columns={requestColumns(false)}
        rows={rows}
        loading={list.isLoading}
        error={list.error}
        onRefresh={() => list.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'createdAt', dir: 'desc' }}
        emptyTitle="Bạn chưa có đơn nào trong mục này"
      />

      <RequestDrawer requestId={openId} onClose={() => setOpenId(null)} />
      {composing && <RequestComposer onClose={() => setComposing(false)} onSaved={(id) => setOpenId(id)} />}
    </>
  )
}

export function ApprovalsPage() {
  const auth = useAuth()
  const canManage = auth.can(PERM.requestsManage)
  const [tab, setTab] = useState('pending')
  const [search, setSearch] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [delegating, setDelegating] = useState(false)

  const inbox = useRequests('inbox', undefined, tab === 'pending')
  const done = useRequests('all', undefined, tab === 'done' && canManage)

  const source = tab === 'pending' ? inbox : done
  const rows = useMemo(() => {
    const list = (source.data ?? []).filter((r) => (tab === 'done' ? r.status !== 'Pending' : true))
    if (!search) return list
    return list.filter((r) => matches(`${r.requestNo} ${r.employeeName} ${r.typeLabel} ${r.title}`, search))
  }, [source.data, tab, search])

  const columns = requestColumns(true)
  const waiting: Column<RequestRow> = {
    key: 'waitingSince',
    priority: 1,
    header: 'Chờ từ',
    width: '9rem',
    cell: (row) => dateTime(row.createdAt),
    sortValue: (r) => r.createdAt,
  }

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Đơn chờ tôi duyệt" value={inbox.data ? inbox.data.length : '…'} tone={inbox.data?.length ? 'warn' : undefined} />
          </FigureStrip>
        }
        tabs={[
          { id: 'pending', label: 'Chờ tôi duyệt', count: inbox.data?.length },
          ...(canManage ? [{ id: 'done', label: 'Đã xử lý' }] : []),
        ]}
        tab={tab}
        onTabChange={setTab}
        actions={
          <Button size="sm" onClick={() => setDelegating(true)}>
            Uỷ quyền duyệt
          </Button>
        }
        filters={
          <SearchInput
            size="sm"
            className="w-56"
            placeholder="Người gửi, loại đơn"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onClear={() => setSearch('')}
          />
        }
        columns={columns.map((c) => (c.key === 'createdAt' ? waiting : c))}
        rows={rows}
        loading={source.isLoading}
        error={source.error}
        onRefresh={() => source.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'waitingSince', dir: 'asc' }}
        emptyTitle={tab === 'pending' ? 'Không có đơn nào đang chờ bạn' : 'Chưa có đơn đã xử lý'}
      />

      <RequestDrawer requestId={openId} onClose={() => setOpenId(null)} />
      {delegating && <DelegationModal onClose={() => setDelegating(false)} />}
    </>
  )
}

export function RequestsAdminPage() {
  const types = useRequestTypes()
  const [tab, setTab] = useState('all')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)

  const list = useRequests('all', status || undefined, tab === 'all')

  const rows = useMemo(() => {
    const items = list.data ?? []
    if (!search) return items
    return items.filter((r) => matches(`${r.requestNo} ${r.employeeName} ${r.employeeCode} ${r.typeLabel} ${r.title}`, search))
  }, [list.data, search])

  if (tab === 'types') {
    return (
      <ModuleScreen
        tabs={[
          { id: 'all', label: 'Toàn bộ đơn', count: list.data?.length },
          { id: 'types', label: 'Loại đơn', count: types.data?.length },
        ]}
        tab={tab}
        onTabChange={setTab}
        columns={[
          { key: 'label', priority: 1, header: 'Loại đơn', cell: (row) => <span className="font-medium">{row.label}</span>, sortValue: (r) => r.label },
          { key: 'category', priority: 1, header: 'Nhóm', width: '9rem', cell: (row) => row.category, sortValue: (r) => r.category },
          { key: 'code', priority: 2, header: 'Mã loại', width: '10rem', cell: (row) => <span className="tnum text-ink-3">{row.type}</span> },
          {
            key: 'fields',
            priority: 1,
            header: 'Các ô người gửi phải điền',
            cell: (row) => row.fields.map((f) => f.label).join(', '),
            truncate: true,
          },
        ]}
        rows={types.data ?? []}
        getKey={(row) => row.type}
        loading={types.isLoading}
        error={types.error}
        onRefresh={() => types.refetch()}
      />
    )
  }

  return (
    <>
      <ModuleScreen
        tabs={[
          { id: 'all', label: 'Toàn bộ đơn', count: list.data?.length },
          { id: 'types', label: 'Loại đơn', count: types.data?.length },
        ]}
        tab={tab}
        onTabChange={setTab}
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Người gửi, mã đơn"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
            <Select size="sm" className="w-40" value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">Mọi trạng thái</option>
              <option value="Pending">Đang xử lý</option>
              <option value="Approved">Đã duyệt</option>
              <option value="Rejected">Từ chối</option>
              <option value="Cancelled">Đã huỷ</option>
            </Select>
          </>
        }
        columns={requestColumns(true)}
        rows={rows}
        loading={list.isLoading}
        error={list.error}
        onRefresh={() => list.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'createdAt', dir: 'desc' }}
        emptyTitle="Không có đơn nào trong bộ lọc này"
      />
      <RequestDrawer requestId={openId} onClose={() => setOpenId(null)} />
    </>
  )
}

/** Nhãn tiếng Việt cho các khoá trong phần chi tiết linh hoạt của đơn. */
function payloadRows(detail: RequestDetail | undefined, fields: RequestField[] | undefined) {
  const payload = detail?.request.payload
  if (!payload) return []
  const labels = new Map((fields ?? []).map((f) => [f.key, f.label]))
  return Object.entries(payload)
    .filter(([, value]) => value !== null && value !== '' && value !== undefined)
    .map(([key, value]) => [labels.get(key) ?? key, Array.isArray(value) ? value.join(', ') : String(value)] as [string, string])
}

function RequestDrawer({ requestId, onClose }: { requestId: string | null; onClose: () => void }) {
  const auth = useAuth()
  const toast = useToast()
  const detail = useRequest(requestId)
  const types = useRequestTypes()
  const decide = useDecideRequest()
  const cancel = useCancelRequest()
  const remind = useRemindRequest()
  const [comment, setComment] = useState('')
  const [rejecting, setRejecting] = useState(false)

  const head = detail.data?.request
  const status = head ? requestStatus(head.status) : null
  const fields = types.data?.find((t) => t.type === head?.type)?.fields
  const mine = head?.requesterUsername?.toLowerCase() === auth.profile?.username?.toLowerCase()
  const myStep = detail.data?.approvals.find(
    (a) => a.status === 'Pending' && a.stepNo === head?.currentStep && a.approverUsername.toLowerCase() === (auth.profile?.username ?? '').toLowerCase(),
  )
  const canDecide = head?.status === 'Pending' && (!!myStep || auth.can(PERM.requestsManage))

  return (
    <>
      <Drawer
        open={!!requestId}
        onClose={onClose}
        width="lg"
        title={head ? `${head.typeLabel} · ${head.requestNo}` : 'Đơn từ'}
        meta={
          head && (
            <>
              <span>{head.employeeName || head.requesterUsername}</span>
              <span>{dateTime(head.createdAt)}</span>
              {status && <StatusBadge tone={status.tone}>{status.label}</StatusBadge>}
            </>
          )
        }
        actions={
          head && (
            <>
              {head.status === 'Pending' && mine && (
                <>
                  <Button size="sm" loading={remind.isPending} onClick={async () => {
                    try {
                      await remind.mutateAsync(head.id)
                      toast.success('Đã nhắc người duyệt')
                    } catch (e) {
                      toast.error('Không nhắc được', errorMessage(e))
                    }
                  }}>
                    Nhắc duyệt
                  </Button>
                  <Button size="sm" variant="danger" loading={cancel.isPending} onClick={async () => {
                    try {
                      await cancel.mutateAsync(head.id)
                      toast.success('Đã huỷ đơn')
                      onClose()
                    } catch (e) {
                      toast.error('Không huỷ được đơn', errorMessage(e))
                    }
                  }}>
                    Huỷ đơn
                  </Button>
                </>
              )}
            </>
          )
        }
      >
        <div className="flex flex-col gap-3 p-3">
          <Panel title="Nội dung đơn" padded>
            <KeyValue
              rows={[
                ['Người gửi', head?.employeeName || head?.requesterUsername || null],
                ['Mã nhân viên', head?.employeeCode || null],
                ['Phòng ban', head?.departmentName || null],
                ['Tiêu đề', head?.title || null],
                ['Hạn xử lý', head?.dueAt ? dateTime(head.dueAt) : null],
                ...payloadRows(detail.data, fields),
              ]}
            />
          </Panel>

          <Panel title="Chuỗi duyệt" meta={detail.data ? `${detail.data.approvals.length} bước` : undefined}>
            <DataTable
              columns={[
                { key: 'step', priority: 1, header: 'Bước', width: '4rem', align: 'center', cell: (row) => row.stepNo },
                {
                  key: 'approver',
                  priority: 1,
                  header: 'Người duyệt',
                  cell: (row) => row.approverName || row.approverUsername || row.approverRole,
                },
                {
                  key: 'status',
                  priority: 1,
                  header: 'Kết quả',
                  width: '8rem',
                  cell: (row) => <StatusBadge tone={requestStatus(row.status).tone}>{requestStatus(row.status).label}</StatusBadge>,
                },
                { key: 'decidedAt', priority: 2, header: 'Lúc', width: '10rem', cell: (row) => dateTime(row.decidedAt) },
                { key: 'comment', priority: 2, header: 'Ý kiến', cell: (row) => row.comment },
              ]}
              rows={detail.data?.approvals ?? []}
              getKey={(row) => row.stepNo}
              loading={detail.isLoading}
              density="compact"
            />
          </Panel>

          {(detail.data?.attachments.length ?? 0) > 0 && (
            <Panel title="Tệp đính kèm" padded>
              <ul className="flex flex-col gap-1.5 text-sm">
                {detail.data?.attachments.map((a) => (
                  <li key={a.id}>
                    <a className="text-brand hover:underline" href={`/api/requests/${head?.id}/attachments/${a.id}`} target="_blank" rel="noreferrer">
                      {a.fileName}
                    </a>
                    <span className="ml-2 text-xs text-ink-3">{Math.round(a.fileSize / 1024)} KB</span>
                  </li>
                ))}
              </ul>
            </Panel>
          )}

          {canDecide && (
            <Panel title="Quyết định của bạn" padded>
              <div className="flex flex-col gap-2.5">
                <Field label="Ý kiến">
                  <Textarea rows={2} value={comment} onChange={(e) => setComment(e.target.value)} placeholder="Ghi chú kèm quyết định" />
                </Field>
                <div className="flex flex-wrap gap-2">
                  <Button
                    size="sm"
                    variant="primary"
                    loading={decide.isPending}
                    onClick={async () => {
                      if (!head) return
                      try {
                        await decide.mutateAsync({ id: head.id, decision: 'approve', comment })
                        toast.success('Đã duyệt đơn')
                        setComment('')
                      } catch (e) {
                        toast.error('Không duyệt được', errorMessage(e))
                      }
                    }}
                  >
                    Duyệt
                  </Button>
                  <Button size="sm" variant="danger" onClick={() => setRejecting(true)}>
                    Từ chối
                  </Button>
                </div>
              </div>
            </Panel>
          )}
        </div>
      </Drawer>

      <ConfirmDialog
        open={rejecting}
        onClose={() => setRejecting(false)}
        title="Từ chối đơn"
        message="Đơn đóng lại ngay, người gửi nhận được thông báo kèm lý do."
        confirmLabel="Từ chối"
        tone="danger"
        requireReason
        reasonLabel="Lý do từ chối"
        busy={decide.isPending}
        onConfirm={async (reason) => {
          if (!head) return
          try {
            await decide.mutateAsync({ id: head.id, decision: 'reject', comment: reason })
            toast.success('Đã từ chối đơn')
            setRejecting(false)
          } catch (e) {
            toast.error('Không từ chối được', errorMessage(e))
          }
        }}
      />
    </>
  )
}

/** Ô nhập dựng động theo định nghĩa trường máy chủ trả về. */
function DynamicField({
  field,
  value,
  onChange,
  error,
}: {
  field: RequestField
  value: unknown
  onChange: (value: unknown) => void
  error?: string | null
}) {
  const text = value == null ? '' : String(value)
  return (
    <Field label={field.label} hint={field.hint || undefined} required={field.required} error={error}>
      {field.type === 'textarea' ? (
        <Textarea rows={3} value={text} onChange={(e) => onChange(e.target.value)} />
      ) : field.type === 'date' ? (
        <DatePicker value={text} onChange={onChange} />
      ) : field.type === 'time' ? (
        <Input type="time" value={text} onChange={(e) => onChange(e.target.value)} />
      ) : field.type === 'number' || field.type === 'money' ? (
        <NumberInput value={typeof value === 'number' ? value : text ? Number(text) : null} onChange={onChange} />
      ) : field.type === 'select' ? (
        <Select value={text} onChange={(e) => onChange(e.target.value)}>
          <option value="">Chọn…</option>
          {field.options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </Select>
      ) : field.type === 'checkboxes' ? (
        <div className="flex flex-wrap gap-3 pt-1">
          {field.options.map((o) => {
            const list = Array.isArray(value) ? (value as string[]) : []
            return (
              <Checkbox
                key={o.value}
                label={o.label}
                checked={list.includes(o.value)}
                onChange={(e) =>
                  onChange(e.target.checked ? [...list, o.value] : list.filter((v) => v !== o.value))
                }
              />
            )
          })}
        </div>
      ) : (
        <Input value={text} onChange={(e) => onChange(e.target.value)} />
      )}
    </Field>
  )
}

function RequestComposer({ onClose, onSaved }: { onClose: () => void; onSaved: (id: string) => void }) {
  const toast = useToast()
  const types = useRequestTypes()
  const save = useSaveRequest()
  const [type, setType] = useState('')
  const [title, setTitle] = useState('')
  const [payload, setPayload] = useState<Record<string, unknown>>({})
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const chosen = types.data?.find((t) => t.type === type)
  const missing = (field: RequestField) => {
    const value = payload[field.key]
    if (!field.required) return null
    const empty = value == null || value === '' || (Array.isArray(value) && value.length === 0)
    return empty ? 'Ô này bắt buộc' : null
  }
  const valid = !!chosen && chosen.fields.every((f) => !missing(f))

  const submit = async () => {
    setTouched(true)
    if (!chosen || !valid) return
    setError(null)
    try {
      const created = await save.mutateAsync({
        body: { type: chosen.type, title: title.trim() || chosen.label, payload },
      })
      toast.success(`Đã gửi đơn ${created?.requestNo ?? ''}`.trim())
      onClose()
      if (created?.id) onSaved(created.id)
    } catch (e) {
      setError(errorMessage(e, 'Không gửi được đơn.'))
    }
  }

  const grouped = useMemo(() => {
    const map = new Map<string, typeof types.data>()
    for (const t of types.data ?? []) {
      const list = map.get(t.category) ?? []
      map.set(t.category, [...(list ?? []), t])
    }
    return [...map.entries()]
  }, [types.data])

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      size="lg"
      title="Tạo đơn"
      description="Chọn loại đơn, các ô cần điền sẽ hiện theo đúng loại bạn chọn."
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={save.isPending} disabled={!chosen} onClick={submit}>
            Gửi đơn
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <FormGrid cols={2}>
          <Field label="Loại đơn" required>
            <Select
              value={type}
              onChange={(e) => {
                setType(e.target.value)
                setPayload({})
                setTouched(false)
              }}
            >
              <option value="">Chọn loại đơn…</option>
              {grouped.map(([category, items]) => (
                <optgroup key={category} label={category}>
                  {(items ?? []).map((t) => (
                    <option key={t.type} value={t.type}>
                      {t.label}
                    </option>
                  ))}
                </optgroup>
              ))}
            </Select>
          </Field>
          <Field label="Tiêu đề" hint="Để trống thì lấy tên loại đơn">
            <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder={chosen?.label} />
          </Field>
        </FormGrid>

        {chosen && (
          <FormGrid cols={2}>
            {chosen.fields.map((f) => (
              <DynamicField
                key={f.key}
                field={f}
                value={payload[f.key]}
                onChange={(value) => setPayload((p) => ({ ...p, [f.key]: value }))}
                error={touched ? missing(f) : null}
              />
            ))}
          </FormGrid>
        )}
      </div>
    </Modal>
  )
}

/**
 * Uỷ quyền duyệt trong một khoảng ngày. Máy chủ chỉ có đường ghi (/requests/delegations/me), nên
 * đây là biểu mẫu đặt lại chứ không phải danh sách.
 */
function DelegationModal({ onClose }: { onClose: () => void }) {
  const toast = useToast()
  const employees = useEmployees()
  const save = useSaveDelegation()
  const [to, setTo] = useState('')
  const [from, setFrom] = useState(todayISO())
  const [until, setUntil] = useState(todayISO())
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    if (!to) {
      setError('Chọn người nhận uỷ quyền.')
      return
    }
    setError(null)
    try {
      await save.mutateAsync({ toUsername: to, fromDate: from, toDate: until })
      toast.success('Đã đặt uỷ quyền duyệt')
      onClose()
    } catch (e) {
      setError(errorMessage(e, 'Không đặt được uỷ quyền.'))
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Uỷ quyền duyệt đơn"
      description="Trong khoảng ngày này, đơn đáng lẽ vào hàng đợi của bạn sẽ chuyển sang người được uỷ quyền."
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={save.isPending} onClick={submit}>
            Đặt uỷ quyền
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <Field label="Người được uỷ quyền" required hint="Người này phải có quyền duyệt đơn">
          <Combobox
            value={to}
            onChange={setTo}
            loading={employees.isLoading}
            placeholder="Chọn đồng nghiệp"
            options={(employees.data ?? [])
              .filter((e) => e.username)
              .map((e) => ({ value: e.username, label: e.fullName, description: [e.position, e.departmentName].filter(Boolean).join(' · ') }))}
          />
        </Field>
        <FormGrid cols={2}>
          <Field label="Từ ngày" required>
            <DatePicker value={from} onChange={setFrom} />
          </Field>
          <Field label="Đến ngày" required>
            <DatePicker value={until} onChange={setUntil} />
          </Field>
        </FormGrid>
      </div>
    </Modal>
  )
}
