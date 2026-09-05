import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Camera, CameraOff, Check, Plus, RotateCw, Smartphone, Trash2, X } from 'lucide-react'
import { QRCodeSVG } from 'qrcode.react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { api } from '@/lib/http'
import { cn } from '@/lib/cn'
import { useIsHandheld } from '@/lib/device'
import { date, dateTime, duration, hours, monthLabel, monthRange, num, time, todayISO } from '@/lib/format'
import { matches } from '@/lib/text'
import { useFiscal } from '@/shell/FiscalContext'
import { useEmployees } from '@/api/hr'
import {
  HOLIDAY_TYPE_LABELS,
  RISK_LABELS,
  dayStatus,
  enrollmentStatus,
  offlineStatus,
  riskFlags,
  useAssignShift,
  useAttendanceLog,
  useConfirmAttendance,
  useDeleteAssignment,
  useDeleteFace,
  useDeleteHoliday,
  useDeleteShift,
  useEmployeeTimesheet,
  useFaceEnrollments,
  useFaceEngineStatus,
  useHolidays,
  useMyFaceStatus,
  useMyTimesheet,
  useOfflineConfig,
  useOfflineRecords,
  usePreviewAttendance,
  useRegisteredFaces,
  useRejectEnrollment,
  useReviewOffline,
  useSaveHoliday,
  useSaveShift,
  useShiftAssignments,
  useShifts,
  type AttendanceResult,
  type FaceEnrollmentRequest,
  type Holiday,
  type OfflineRecord,
  type Shift,
  type Timesheet,
  type TimesheetDay,
} from '@/api/attendance'
import {
  Button,
  Checkbox,
  Combobox,
  ConfirmDialog,
  DataTable,
  DatePicker,
  Drawer,
  Field,
  Figure,
  FigureStrip,
  InlineAlert,
  Input,
  KeyValue,
  Modal,
  MonthPicker,
  NumberInput,
  Panel,
  SearchInput,
  Select,
  StatusBadge,
  Textarea,
  useToast,
  type Column,
} from '@/ui'
import { ModuleScreen, errorMessage } from './_shared'

/* ============================================================================
   Camera dùng chung cho trạm chấm công và bước xác minh khuôn mặt
   ========================================================================== */

/** Số khung gửi mỗi lượt quét. Máy chủ nhận tối đa 16 khung mỗi yêu cầu. */
const BURST_FRAMES = 12
const BURST_INTERVAL_MS = 110

type CameraState = 'off' | 'starting' | 'on' | 'error'

/**
 * Mở camera và chụp một loạt khung. Máy chủ tự chọn khung tốt nhất trong loạt, nên phía này
 * không cần dò khuôn mặt. Camera chỉ mở được trên HTTPS hoặc localhost.
 */
function useCamera() {
  const videoRef = useRef<HTMLVideoElement>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const [state, setState] = useState<CameraState>('off')
  const [error, setError] = useState('')

  const stop = useCallback(() => {
    streamRef.current?.getTracks().forEach((track) => track.stop())
    streamRef.current = null
    if (videoRef.current) videoRef.current.srcObject = null
    setState('off')
  }, [])

  const start = useCallback(async () => {
    setError('')
    if (!navigator.mediaDevices?.getUserMedia) {
      setState('error')
      setError('Trình duyệt không mở được camera. Trang phải chạy qua HTTPS hoặc localhost.')
      return
    }
    setState('starting')
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false,
      })
      streamRef.current = stream
      if (videoRef.current) {
        videoRef.current.srcObject = stream
        await videoRef.current.play().catch(() => undefined)
      }
      setState('on')
    } catch (err) {
      setState('error')
      setError(
        err instanceof DOMException && err.name === 'NotAllowedError'
          ? 'Bạn đã từ chối quyền dùng camera. Cấp lại quyền trong thanh địa chỉ rồi thử lại.'
          : err instanceof DOMException && err.name === 'NotFoundError'
            ? 'Máy này không có camera nào dùng được.'
            : 'Không mở được camera.',
      )
    }
  }, [])

  useEffect(() => stop, [stop])

  /** Chụp một khung thành chuỗi JPEG base64 kèm tiền tố data URL. */
  const grab = useCallback((maxWidth = 720) => {
    const video = videoRef.current
    if (!video || !video.videoWidth) return null
    const scale = Math.min(1, maxWidth / video.videoWidth)
    const canvas = document.createElement('canvas')
    canvas.width = Math.round(video.videoWidth * scale)
    canvas.height = Math.round(video.videoHeight * scale)
    const ctx = canvas.getContext('2d')
    if (!ctx) return null
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height)
    return canvas.toDataURL('image/jpeg', 0.82)
  }, [])

  /** Chụp liên tiếp `count` khung, cách nhau `interval` mili giây. */
  const burst = useCallback(
    async (count = BURST_FRAMES, interval = BURST_INTERVAL_MS) => {
      const frames: string[] = []
      for (let i = 0; i < count; i += 1) {
        const frame = grab()
        if (frame) frames.push(frame)
        if (i < count - 1) await new Promise((resolve) => window.setTimeout(resolve, interval))
      }
      return frames
    },
    [grab],
  )

  return { videoRef, state, error, start, stop, grab, burst }
}

/* ============================================================================
   Trạm chấm công
   ========================================================================== */

type StationPhase = 'idle' | 'scanning' | 'preview' | 'done'

/**
 * Trạm chấm công bằng khuôn mặt. Màn hình này CHỈ dành cho máy cầm tay: nhân viên đứng trước cửa và
 * tự chấm bằng điện thoại, nên bố cục là một cột dọc, khung hình đứng và nút bấm cỡ ngón tay. Mở
 * trên máy tính sẽ ra màn hướng dẫn chuyển sang điện thoại (xem `deviceScope` ở navigation.ts).
 *
 * Luồng hai bước: quét để xem trước ai và Vào hay Ra, rồi xác nhận để ghi. Bước xác nhận dùng vé
 * của bước xem trước nên máy chủ không nhận diện lại.
 */
export function AttendanceStationPage() {
  const handheld = useIsHandheld()
  if (!handheld) return <StationOnPhoneNotice />
  return <AttendanceStation />
}

/** Màn thay thế khi mở trạm chấm công trên máy tính. */
function StationOnPhoneNotice() {
  const url = window.location.href
  return (
    <div className="mx-auto max-w-lg">
      <Panel title="Chấm công bằng điện thoại">
        <div className="flex flex-col items-center gap-4 px-4 py-6 text-center">
          <span className="grid size-10 place-items-center rounded-sm bg-brand-wash text-brand">
            <Smartphone className="size-5" strokeWidth={1.7} />
          </span>
          <div>
            <p className="text-sm font-medium text-ink">Trạm chấm công chạy trên điện thoại</p>
            <p className="mt-1 text-xs text-ink-2">
              Quét khuôn mặt cần camera trước cầm trên tay. Quét mã dưới đây để mở màn hình này trên
              điện thoại của bạn, hoặc dùng ứng dụng Nhân sự.
            </p>
          </div>
          <span className="rounded-md border border-line bg-white p-2">
            <QRCodeSVG value={url} size={148} level="M" marginSize={0} title="Mở trạm chấm công trên điện thoại" />
          </span>
          <p className="tnum text-xs break-all text-ink-3">{url}</p>
        </div>
        <div className="border-t border-line-2 px-4 py-3">
          <Link to="/bang-cong" className="link text-sm">
            Xem bảng công của tôi
          </Link>
        </div>
      </Panel>
    </div>
  )
}

function AttendanceStation() {
  const auth = useAuth()
  const camera = useCamera()
  const engine = useFaceEngineStatus()
  const myFace = useMyFaceStatus()
  const preview = usePreviewAttendance()
  const confirm = useConfirmAttendance()
  const today = todayISO()
  const canManage = auth.can(PERM.attendanceManage)
  const log = useAttendanceLog({ date: today }, canManage)

  const [phase, setPhase] = useState<StationPhase>('idle')
  const [result, setResult] = useState<AttendanceResult | null>(null)
  const [error, setError] = useState('')

  const scan = async () => {
    setError('')
    setResult(null)
    setPhase('scanning')
    try {
      const frames = await camera.burst()
      if (frames.length === 0) {
        setError('Không lấy được khung hình từ camera.')
        setPhase('idle')
        return
      }
      const outcome = await preview.mutateAsync(frames)
      setResult(outcome)
      setPhase(outcome.previewToken ? 'preview' : 'idle')
    } catch (err) {
      setError(errorMessage(err, 'Không gửi được ảnh chấm công.'))
      setPhase('idle')
    }
  }

  const commit = async () => {
    if (!result?.previewToken) return
    setError('')
    try {
      const outcome = await confirm.mutateAsync(result.previewToken)
      setResult(outcome)
      setPhase('done')
      void log.refetch()
    } catch (err) {
      setError(errorMessage(err, 'Không ghi được lượt chấm công.'))
    }
  }

  const reset = () => {
    setResult(null)
    setError('')
    setPhase('idle')
  }

  const busy = phase === 'scanning' || preview.isPending || confirm.isPending

  return (
    <div className="mx-auto flex max-w-md flex-col gap-3">
      {myFace.data && !myFace.data.registered && (
        <InlineAlert
          tone={myFace.data.pending ? 'warn' : 'danger'}
          title={myFace.data.pending ? 'Mẫu khuôn mặt đang chờ duyệt' : 'Bạn chưa đăng ký khuôn mặt'}
        >
          {myFace.data.pending
            ? `Gửi lúc ${dateTime(myFace.data.requestedAt)}. Nhân sự phải đối chiếu trực tiếp rồi mới duyệt.`
            : 'Đăng ký khuôn mặt trong ứng dụng Nhân sự, mục Cài đặt, trước khi chấm công.'}
          {myFace.data.reviewNote ? ` ${myFace.data.reviewNote}` : ''}
        </InlineAlert>
      )}

      <Panel bodyClassName="p-0">
        {/* Khung đứng đúng tỉ lệ camera trước của điện thoại. */}
        <div className="relative grid aspect-[3/4] place-items-center overflow-hidden bg-[#0f1520]">
          <video
            ref={camera.videoRef}
            playsInline
            muted
            className={cn('size-full object-cover', camera.state !== 'on' && 'invisible')}
            style={{ transform: 'scaleX(-1)' }}
          />
          {camera.state === 'on' && (
            <span
              aria-hidden
              className={cn(
                'pointer-events-none absolute h-[52%] w-[62%] rounded-lg border-2 transition-colors',
                phase === 'scanning' ? 'border-warn' : phase === 'done' ? 'border-ok' : 'border-white/60',
              )}
            />
          )}
          {camera.state !== 'on' && (
            <p className="absolute px-6 text-center text-sm text-white/70">
              {camera.state === 'starting' ? 'Đang mở camera' : 'Bật camera để bắt đầu chấm công'}
            </p>
          )}
          {camera.state === 'on' && phase === 'idle' && (
            <p className="absolute bottom-3 rounded-sm bg-black/55 px-2.5 py-1 text-xs text-white">
              Nhìn thẳng, mở mắt, giữ mặt trong khung
            </p>
          )}
        </div>

        <div className="flex flex-col gap-2 p-3">
          {camera.state !== 'on' ? (
            <Button
              variant="primary"
              className="h-11 w-full text-sm"
              loading={camera.state === 'starting'}
              icon={<Camera className="size-4" strokeWidth={1.8} />}
              onClick={() => void camera.start()}
            >
              Bật camera
            </Button>
          ) : phase === 'preview' && result?.matched ? (
            <>
              <Button
                variant="primary"
                className="h-11 w-full text-sm"
                loading={confirm.isPending}
                icon={<Check className="size-4" strokeWidth={2} />}
                onClick={() => void commit()}
              >
                Xác nhận chấm {result.loai?.toLowerCase() ?? ''}
              </Button>
              <Button className="h-10 w-full" onClick={reset} disabled={confirm.isPending}>
                Quét lại
              </Button>
            </>
          ) : phase === 'done' ? (
            <Button variant="primary" className="h-11 w-full text-sm" onClick={reset}>
              Chấm cho lượt tiếp theo
            </Button>
          ) : (
            <>
              <Button
                variant="primary"
                className="h-11 w-full text-sm"
                loading={busy}
                icon={<Camera className="size-4" strokeWidth={1.8} />}
                onClick={() => void scan()}
              >
                {busy ? 'Đang nhận diện' : 'Quét khuôn mặt'}
              </Button>
              <Button className="h-10 w-full" icon={<CameraOff className="size-4" strokeWidth={1.7} />} onClick={camera.stop}>
                Tắt camera
              </Button>
            </>
          )}
        </div>
      </Panel>

      {camera.error && <InlineAlert tone="danger">{camera.error}</InlineAlert>}
      {error && <InlineAlert tone="danger">{error}</InlineAlert>}
      {result && (
        <Panel>
          <ScanResult result={result} phase={phase} />
        </Panel>
      )}

      <Panel title="Khuôn mặt của tôi" padded>
        <KeyValue
          rows={[
            [
              'Trạng thái',
              myFace.data
                ? myFace.data.registered
                  ? 'Đã đăng ký'
                  : myFace.data.pending
                    ? 'Chờ nhân sự duyệt'
                    : 'Chưa đăng ký'
                : null,
            ],
            ['Số mẫu', myFace.data?.sampleCount || null],
            ['Đăng ký lúc', myFace.data?.createdAt ? dateTime(myFace.data.createdAt) : null],
            ['Bộ nhận diện', engine.data ? `${engine.data.engine}, ngưỡng khớp ${num(engine.data.matchThreshold)}` : null],
          ]}
        />
      </Panel>

      {canManage && (
        <Panel title="Lượt chấm hôm nay" meta={log.data ? `${log.data.length} lượt` : undefined}>
          <DataTable
            columns={[
              { key: 'time', priority: 1, header: 'Giờ', cell: (row) => time(row.occurredAt) },
              { key: 'name', priority: 1, header: 'Nhân viên', cell: (row) => row.fullName || row.username, truncate: true },
              { key: 'loai', priority: 1, header: 'Chiều', cell: (row) => row.loai },
            ]}
            rows={(log.data ?? []).slice(0, 8)}
            getKey={(row) => row.id}
            loading={log.isLoading}
            density="compact"
            emptyTitle="Chưa có lượt chấm nào hôm nay"
          />
        </Panel>
      )}
    </div>
  )
}

function ScanResult({ result, phase }: { result: AttendanceResult; phase: StationPhase }) {
  const ok = result.status === 'ok'
  const tone = ok ? 'ok' : result.status === 'unknown' || result.status === 'spoof' ? 'danger' : 'warn'
  return (
    <div className="flex flex-col gap-3 px-3.5 py-3.5">
      <div className="flex items-center gap-3">
        <span
          className={cn(
            'grid size-9 shrink-0 place-items-center rounded-sm',
            tone === 'ok' ? 'bg-ok-wash text-ok' : tone === 'danger' ? 'bg-danger-wash text-danger' : 'bg-warn-wash text-warn',
          )}
        >
          {tone === 'ok' ? <Check className="size-5" strokeWidth={2} /> : <X className="size-5" strokeWidth={2} />}
        </span>
        <div className="min-w-0">
          <p className="truncate text-sm font-semibold text-ink">{result.fullName || result.username || 'Không nhận ra'}</p>
          <p className="text-xs text-ink-3">
            {phase === 'done' ? 'Đã ghi nhận' : phase === 'preview' ? 'Chờ bạn xác nhận' : result.message}
          </p>
        </div>
        {result.loai && <StatusBadge tone={result.loai === 'Vào' ? 'ok' : 'info'}>{result.loai}</StatusBadge>}
      </div>

      <KeyValue
        rows={[
          ['Thời điểm', result.occurredAt ? dateTime(result.occurredAt) : null],
          ['Độ khớp', result.matched ? num(result.similarity) : null],
          ['Chất lượng ảnh', result.quality ? num(result.quality) : null],
        ]}
        className="text-xs"
      />

      {result.message && phase !== 'idle' && <p className="text-xs text-ink-2">{result.message}</p>}
      {result.guidance && <InlineAlert tone="warn">{result.guidance}</InlineAlert>}
    </div>
  )
}

/* ============================================================================
   Bảng công
   ========================================================================== */

function timesheetColumns(): Column<TimesheetDay>[] {
  return [
    {
      key: 'date', priority: 1,
      header: 'Ngày',
      width: '7.5rem',
      cell: (row) => (
        <span className="flex items-center gap-2">
          <span className="tnum">{date(row.date)}</span>
          <span className="text-xs text-ink-3">{WEEKDAY[new Date(row.date).getDay()]}</span>
        </span>
      ),
      sortValue: (r) => r.date,
    },
    {
      key: 'shift', priority: 2,
      header: 'Ca làm',
      cell: (row) =>
        row.holidayName ? (
          <span className="text-ink-2">{row.holidayName}</span>
        ) : row.shiftName ? (
          <span>
            {row.shiftName} <span className="tnum text-xs text-ink-3">{row.shiftStart}-{row.shiftEnd}</span>
          </span>
        ) : null,
      sortValue: (r) => r.shiftName,
    },
    { key: 'in', priority: 1, header: 'Giờ vào', align: 'right', cell: (row) => <span className="tnum">{row.checkIn ?? ''}</span> },
    { key: 'out', priority: 1, header: 'Giờ ra', align: 'right', cell: (row) => <span className="tnum">{row.checkOut ?? ''}</span> },
    {
      key: 'late', priority: 2,
      header: 'Đi muộn',
      align: 'right',
      cell: (row) => (row.lateMinutes ? <span className="tnum text-warn">{duration(row.lateMinutes)}</span> : null),
      sortValue: (r) => r.lateMinutes,
    },
    {
      key: 'early', priority: 3,
      header: 'Về sớm',
      align: 'right',
      cell: (row) => (row.earlyMinutes ? <span className="tnum text-warn">{duration(row.earlyMinutes)}</span> : null),
      sortValue: (r) => r.earlyMinutes,
    },
    {
      key: 'overtime', priority: 2,
      header: 'Tăng ca',
      align: 'right',
      cell: (row) => (row.overtimeMinutes ? <span className="tnum">{duration(row.overtimeMinutes)}</span> : null),
      sortValue: (r) => r.overtimeMinutes,
    },
    {
      key: 'worked', priority: 2,
      header: 'Giờ làm',
      align: 'right',
      cell: (row) => (row.workedHours ? <span className="tnum">{num(row.workedHours)}</span> : null),
      sortValue: (r) => r.workedHours,
    },
    {
      key: 'status', priority: 1,
      header: 'Trạng thái',
      cell: (row) => {
        const s = dayStatus(row.status)
        return <StatusBadge tone={s.tone}>{s.label}</StatusBadge>
      },
      sortValue: (r) => r.status,
    },
  ]
}

const WEEKDAY = ['CN', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7']

function TimesheetFigures({ timesheet }: { timesheet?: Timesheet }) {
  const s = timesheet?.summary
  return (
    <FigureStrip>
      <Figure label="Ngày công" value={s ? num(s.workedDays) : '…'} />
      <Figure label="Ngày vắng" value={s ? num(s.absentDays) : '…'} tone={s?.absentDays ? 'danger' : undefined} />
      <Figure label="Lượt đi muộn" value={s ? num(s.lateDays) : '…'} tone={s?.lateDays ? 'warn' : undefined} />
      <Figure label="Tổng đi muộn" value={s ? duration(s.totalLateMinutes) : '…'} />
      <Figure label="Tổng tăng ca" value={s ? duration(s.totalOvertimeMinutes) : '…'} />
      <Figure label="Tổng giờ làm" value={s ? hours(s.totalWorkedHours) : '…'} />
    </FigureStrip>
  )
}

export function MyTimesheetPage() {
  const fiscal = useFiscal()
  const timesheet = useMyTimesheet(fiscal.period)
  const [status, setStatus] = useState('all')

  const rows = (timesheet.data?.days ?? []).filter((d) => {
    if (status === 'worked') return d.workedHours > 0
    if (status === 'issue') return d.lateMinutes > 0 || d.earlyMinutes > 0 || !d.checkOut || d.status === 'absent'
    if (status === 'overtime') return d.overtimeMinutes > 0
    return true
  })

  return (
    <ModuleScreen
      figures={<TimesheetFigures timesheet={timesheet.data} />}
      tabs={[
        { id: 'all', label: 'Cả tháng', count: timesheet.data?.days.length },
        { id: 'worked', label: 'Ngày có công' },
        { id: 'issue', label: 'Cần xem lại' },
        { id: 'overtime', label: 'Có tăng ca' },
      ]}
      tab={status}
      onTabChange={setStatus}
      filters={<MonthPicker value={fiscal.period} onChange={fiscal.setPeriod} size="sm" className="w-40" />}
      columns={timesheetColumns()}
      rows={rows}
      getKey={(row) => row.date}
      loading={timesheet.isLoading}
      error={timesheet.error}
      onRefresh={() => timesheet.refetch()}
      pageSize={50}
      defaultSort={{ key: 'date', dir: 'asc' }}
      emptyTitle={`Không có ngày công nào trong ${monthLabel(fiscal.period).toLowerCase()}`}
    />
  )
}

export function CompanyTimesheetPage() {
  const fiscal = useFiscal()
  const employees = useEmployees({})
  const [employeeId, setEmployeeId] = useState('')
  const timesheet = useEmployeeTimesheet(employeeId || undefined, fiscal.period)

  // Chọn sẵn người đầu tiên để màn hình không mở ra trống trơn.
  useEffect(() => {
    if (!employeeId && employees.data?.length) setEmployeeId(employees.data[0].id)
  }, [employees.data, employeeId])

  const employee = employees.data?.find((e) => e.id === employeeId)

  return (
    <ModuleScreen
      figures={<TimesheetFigures timesheet={timesheet.data} />}
      filters={
        <>
          <div className="w-64">
            <Combobox
              size="sm"
              value={employeeId}
              onChange={setEmployeeId}
              placeholder="Chọn nhân viên"
              loading={employees.isLoading}
              options={(employees.data ?? []).map((e) => ({
                value: e.id,
                label: e.fullName,
                description: e.departmentName,
                keywords: `${e.employeeCode} ${e.username}`,
              }))}
            />
          </div>
          <MonthPicker value={fiscal.period} onChange={fiscal.setPeriod} size="sm" className="w-40" />
          {employee && <span className="text-xs text-ink-3">{employee.employeeCode}</span>}
        </>
      }
      columns={timesheetColumns()}
      rows={timesheet.data?.days ?? []}
      getKey={(row) => row.date}
      loading={timesheet.isLoading}
      error={timesheet.error}
      onRefresh={() => timesheet.refetch()}
      pageSize={50}
      defaultSort={{ key: 'date', dir: 'asc' }}
      emptyTitle={employeeId ? 'Không có ngày công nào trong kỳ này' : 'Chọn một nhân viên để xem bảng công'}
    />
  )
}

/* ============================================================================
   Ca làm, phân ca, ngày nghỉ
   ========================================================================== */

export function ShiftsPage() {
  const auth = useAuth()
  const fiscal = useFiscal()
  const canManage = auth.can(PERM.attendanceManage)
  const [tab, setTab] = useState('shifts')
  const range = monthRange(fiscal.period)

  const shifts = useShifts()
  const assignments = useShiftAssignments(range)
  const holidays = useHolidays({ from: `${fiscal.year}-01-01`, to: `${fiscal.year}-12-31` })

  const [editShift, setEditShift] = useState<Shift | null | 'new'>(null)
  const [deleteShift, setDeleteShift] = useState<Shift | null>(null)
  const [assigning, setAssigning] = useState(false)
  const [addHoliday, setAddHoliday] = useState(false)
  const [deleteHoliday, setDeleteHoliday] = useState<Holiday | null>(null)
  const removeShift = useDeleteShift()
  const removeAssignment = useDeleteAssignment()
  const removeHoliday = useDeleteHoliday()
  const toast = useToast()

  const tabs = [
    { id: 'shifts', label: 'Ca làm', count: shifts.data?.length },
    { id: 'assignments', label: 'Phân ca', count: assignments.data?.length },
    { id: 'holidays', label: 'Ngày nghỉ', count: holidays.data?.length },
  ]

  if (tab === 'assignments')
    return (
      <>
        <ModuleScreen
          tabs={tabs}
          tab={tab}
          onTabChange={setTab}
          filters={<MonthPicker value={fiscal.period} onChange={fiscal.setPeriod} size="sm" className="w-40" />}
          actions={
            canManage && (
              <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setAssigning(true)}>
                Phân ca
              </Button>
            )
          }
          columns={[
            { key: 'date', priority: 1, header: 'Ngày', width: '7.5rem', cell: (row) => <span className="tnum">{date(row.workDate)}</span>, sortValue: (r) => r.workDate },
            { key: 'employee', priority: 1, header: 'Nhân viên', cell: (row) => <span className="font-medium">{row.employeeName}</span>, sortValue: (r) => r.employeeName },
            { key: 'code', priority: 3, header: 'Mã NV', cell: (row) => <span className="tnum">{row.employeeCode}</span>, hidden: true },
            { key: 'shift', priority: 3, header: 'Ca làm', cell: (row) => row.shiftName, sortValue: (r) => r.shiftName },
            { key: 'time', priority: 1, header: 'Giờ', cell: (row) => <span className="tnum">{row.startTime} - {row.endTime}</span> },
            { key: 'note', priority: 3, header: 'Ghi chú', cell: (row) => row.note, truncate: true },
            {
              key: 'actions', priority: 1,
              header: '',
              align: 'right',
              locked: true,
              cell: (row) =>
                canManage ? (
                  <span className="row-actions">
                    <Button
                      size="sm"
                      variant="ghost"
                      className="text-danger"
                      onClick={async (e) => {
                        e.stopPropagation()
                        try {
                          await removeAssignment.mutateAsync(row.id)
                          toast.success('Đã huỷ phân ca')
                        } catch (error) {
                          toast.error('Không huỷ được', errorMessage(error))
                        }
                      }}
                    >
                      Huỷ
                    </Button>
                  </span>
                ) : null,
            },
          ]}
          rows={assignments.data ?? []}
          loading={assignments.isLoading}
          error={assignments.error}
          onRefresh={() => assignments.refetch()}
          pageSize={50}
          defaultSort={{ key: 'date', dir: 'asc' }}
          emptyTitle={`Chưa phân ca cho ${monthLabel(fiscal.period).toLowerCase()}`}
        />
        {assigning && <AssignShiftModal shifts={shifts.data ?? []} onClose={() => setAssigning(false)} />}
      </>
    )

  if (tab === 'holidays')
    return (
      <>
        <ModuleScreen
          tabs={tabs}
          tab={tab}
          onTabChange={setTab}
          actions={
            canManage && (
              <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setAddHoliday(true)}>
                Thêm ngày nghỉ
              </Button>
            )
          }
          columns={[
            { key: 'date', priority: 1, header: 'Ngày', width: '7.5rem', cell: (row) => <span className="tnum">{date(row.holidayDate)}</span>, sortValue: (r) => r.holidayDate },
            { key: 'name', priority: 1, header: 'Tên', cell: (row) => <span className="font-medium">{row.name}</span> },
            {
              key: 'type', priority: 2,
              header: 'Loại',
              cell: (row) => (
                <StatusBadge tone={row.holidayType === 'public' ? 'info' : 'neutral'}>
                  {HOLIDAY_TYPE_LABELS[row.holidayType] ?? row.holidayType}
                </StatusBadge>
              ),
            },
            { key: 'note', priority: 3, header: 'Ghi chú', cell: (row) => row.note, truncate: true },
            { key: 'by', priority: 3, header: 'Người tạo', cell: (row) => row.createdBy, hidden: true },
            {
              key: 'actions', priority: 1,
              header: '',
              align: 'right',
              locked: true,
              cell: (row) =>
                canManage ? (
                  <span className="row-actions">
                    <Button size="sm" variant="ghost" className="text-danger" onClick={(e) => { e.stopPropagation(); setDeleteHoliday(row) }}>
                      Xoá
                    </Button>
                  </span>
                ) : null,
            },
          ]}
          rows={holidays.data ?? []}
          loading={holidays.isLoading}
          error={holidays.error}
          onRefresh={() => holidays.refetch()}
          pageSize={50}
          defaultSort={{ key: 'date', dir: 'asc' }}
          emptyTitle={`Chưa khai báo ngày nghỉ nào cho năm ${fiscal.year}`}
        />
        {addHoliday && <HolidayModal onClose={() => setAddHoliday(false)} />}
        <ConfirmDialog
          open={!!deleteHoliday}
          onClose={() => setDeleteHoliday(null)}
          title="Xoá ngày nghỉ"
          message={deleteHoliday ? `${date(deleteHoliday.holidayDate)} · ${deleteHoliday.name}` : undefined}
          confirmLabel="Xoá"
          tone="danger"
          busy={removeHoliday.isPending}
          onConfirm={async () => {
            if (!deleteHoliday) return
            try {
              await removeHoliday.mutateAsync(deleteHoliday.id)
              toast.success('Đã xoá ngày nghỉ')
              setDeleteHoliday(null)
            } catch (error) {
              toast.error('Không xoá được', errorMessage(error))
            }
          }}
        />
      </>
    )

  return (
    <>
      <ModuleScreen
        tabs={tabs}
        tab={tab}
        onTabChange={setTab}
        actions={
          canManage && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setEditShift('new')}>
              Thêm ca làm
            </Button>
          )
        }
        columns={[
          { key: 'name', priority: 1, header: 'Tên ca', cell: (row) => <span className="font-medium">{row.name}</span>, sortValue: (r) => r.name },
          { key: 'code', priority: 2, header: 'Mã', cell: (row) => <span className="tnum">{row.code}</span> },
          { key: 'start', priority: 1, header: 'Bắt đầu', align: 'right', cell: (row) => <span className="tnum">{row.startTime}</span>, sortValue: (r) => r.startTime },
          { key: 'end', priority: 1, header: 'Kết thúc', align: 'right', cell: (row) => <span className="tnum">{row.endTime}</span> },
          { key: 'break', priority: 3, header: 'Nghỉ giữa ca', align: 'right', cell: (row) => duration(row.breakMinutes) },
          { key: 'grace', priority: 3, header: 'Cho phép muộn', align: 'right', cell: (row) => duration(row.lateGraceMinutes) },
          { key: 'standard', priority: 2, header: 'Giờ chuẩn', align: 'right', cell: (row) => <span className="tnum">{num(row.standardHours)}</span> },
          { key: 'checkout', priority: 3, header: 'Chờ chấm ra', align: 'right', cell: (row) => duration(row.checkoutGraceMinutes), hidden: true },
          {
            key: 'overnight', priority: 3,
            header: 'Qua đêm',
            cell: (row) => (row.isOvernight ? <StatusBadge tone="info">Qua đêm</StatusBadge> : null),
          },
          {
            key: 'actions', priority: 1,
            header: '',
            align: 'right',
            locked: true,
            cell: (row) =>
              canManage ? (
                <span className="row-actions inline-flex gap-1">
                  <Button size="sm" variant="ghost" onClick={(e) => { e.stopPropagation(); setEditShift(row) }}>
                    Sửa
                  </Button>
                  <Button size="sm" variant="ghost" className="text-danger" onClick={(e) => { e.stopPropagation(); setDeleteShift(row) }}>
                    Xoá
                  </Button>
                </span>
              ) : null,
          },
        ]}
        rows={shifts.data ?? []}
        loading={shifts.isLoading}
        error={shifts.error}
        onRefresh={() => shifts.refetch()}
        defaultSort={{ key: 'start', dir: 'asc' }}
        emptyTitle="Chưa khai báo ca làm nào"
      />
      <ShiftModal shift={editShift} onClose={() => setEditShift(null)} />
      <ConfirmDialog
        open={!!deleteShift}
        onClose={() => setDeleteShift(null)}
        title={`Xoá ca ${deleteShift?.name ?? ''}`}
        message="Các phân ca đã gán cho ca này cũng bị ảnh hưởng."
        confirmLabel="Xoá"
        tone="danger"
        busy={removeShift.isPending}
        onConfirm={async () => {
          if (!deleteShift) return
          try {
            await removeShift.mutateAsync(deleteShift.id)
            toast.success('Đã xoá ca làm')
            setDeleteShift(null)
          } catch (error) {
            toast.error('Không xoá được', errorMessage(error))
          }
        }}
      />
    </>
  )
}

function ShiftModal({ shift, onClose }: { shift: Shift | null | 'new'; onClose: () => void }) {
  const toast = useToast()
  const save = useSaveShift()
  const open = shift !== null
  const editing = shift && shift !== 'new' ? shift : null
  const [form, setForm] = useState({
    code: '',
    name: '',
    startTime: '08:00',
    endTime: '17:00',
    breakMinutes: 60,
    lateGraceMinutes: 15,
    standardHours: 8,
    isOvernight: false,
    checkoutGraceMinutes: 120,
  })
  const [touched, setTouched] = useState(false)

  useEffect(() => {
    if (open)
      setForm({
        code: editing?.code ?? '',
        name: editing?.name ?? '',
        startTime: editing?.startTime ?? '08:00',
        endTime: editing?.endTime ?? '17:00',
        breakMinutes: editing?.breakMinutes ?? 60,
        lateGraceMinutes: editing?.lateGraceMinutes ?? 15,
        standardHours: editing?.standardHours ?? 8,
        isOvernight: editing?.isOvernight ?? false,
        checkoutGraceMinutes: editing?.checkoutGraceMinutes ?? 120,
      })
    setTouched(false)
  }, [open, editing])

  const timeOk = (v: string) => /^\d{2}:\d{2}$/.test(v)
  const problems = {
    name: !form.name.trim() ? 'Nhập tên ca' : null,
    start: !timeOk(form.startTime) ? 'Giờ dạng HH:mm' : null,
    end: !timeOk(form.endTime) ? 'Giờ dạng HH:mm' : null,
  }

  const submit = async () => {
    setTouched(true)
    if (problems.name || problems.start || problems.end) return
    try {
      await save.mutateAsync({ id: editing?.id, body: { ...form, name: form.name.trim(), code: form.code.trim() } })
      toast.success(editing ? 'Đã cập nhật ca làm' : 'Đã thêm ca làm')
      onClose()
    } catch (error) {
      toast.error('Không lưu được', errorMessage(error))
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? `Sửa ca ${editing.name}` : 'Thêm ca làm'}
      size="sm"
      dismissible={false}
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={save.isPending} onClick={() => void submit()}>
            Lưu
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Tên ca" required error={touched ? problems.name : null}>
            <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} data-autofocus="" />
          </Field>
          <Field label="Mã ca">
            <Input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} className="tnum" />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Giờ vào" required error={touched ? problems.start : null}>
            <Input value={form.startTime} onChange={(e) => setForm({ ...form, startTime: e.target.value })} placeholder="08:00" className="tnum" />
          </Field>
          <Field label="Giờ ra" required error={touched ? problems.end : null}>
            <Input value={form.endTime} onChange={(e) => setForm({ ...form, endTime: e.target.value })} placeholder="17:00" className="tnum" />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Nghỉ giữa ca (phút)">
            <NumberInput value={form.breakMinutes} onChange={(v) => setForm({ ...form, breakMinutes: v ?? 0 })} />
          </Field>
          <Field label="Cho phép muộn (phút)">
            <NumberInput value={form.lateGraceMinutes} onChange={(v) => setForm({ ...form, lateGraceMinutes: v ?? 0 })} />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Giờ công chuẩn">
            <NumberInput value={form.standardHours} decimals={2} onChange={(v) => setForm({ ...form, standardHours: v ?? 0 })} />
          </Field>
          <Field label="Chờ chấm ra (phút)" hint="Tối đa 720">
            <NumberInput value={form.checkoutGraceMinutes} onChange={(v) => setForm({ ...form, checkoutGraceMinutes: Math.min(720, v ?? 0) })} />
          </Field>
        </div>
        <Checkbox
          label="Ca qua đêm, giờ ra thuộc ngày hôm sau"
          checked={form.isOvernight}
          onChange={(e) => setForm({ ...form, isOvernight: e.target.checked })}
        />
      </div>
    </Modal>
  )
}

function AssignShiftModal({ shifts, onClose }: { shifts: Shift[]; onClose: () => void }) {
  const toast = useToast()
  const assign = useAssignShift()
  const employees = useEmployees({})
  const [form, setForm] = useState({ employeeId: '', shiftId: shifts[0]?.id ?? '', workDate: todayISO(), note: '' })
  const [touched, setTouched] = useState(false)
  const problems = {
    employee: !form.employeeId ? 'Chọn nhân viên' : null,
    shift: !form.shiftId ? 'Chọn ca làm' : null,
  }

  const submit = async () => {
    setTouched(true)
    if (problems.employee || problems.shift) return
    try {
      await assign.mutateAsync({ ...form, note: form.note.trim() })
      toast.success('Đã phân ca')
      onClose()
    } catch (error) {
      toast.error('Không phân ca được', errorMessage(error))
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Phân ca cho nhân viên"
      description="Mỗi nhân viên chỉ có một ca trong một ngày. Phân lại sẽ thay ca cũ."
      size="sm"
      dismissible={false}
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={assign.isPending}>
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={assign.isPending} onClick={() => void submit()}>
            Phân ca
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <Field label="Nhân viên" required error={touched ? problems.employee : null}>
          <Combobox
            value={form.employeeId}
            onChange={(v) => setForm({ ...form, employeeId: v })}
            placeholder="Chọn nhân viên"
            loading={employees.isLoading}
            options={(employees.data ?? []).map((e) => ({ value: e.id, label: e.fullName, description: e.departmentName, keywords: e.employeeCode }))}
          />
        </Field>
        <Field label="Ca làm" required error={touched ? problems.shift : null}>
          <Select value={form.shiftId} onChange={(e) => setForm({ ...form, shiftId: e.target.value })}>
            <option value="">Chọn ca</option>
            {shifts.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name} ({s.startTime} - {s.endTime})
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Ngày làm việc" required>
          <DatePicker value={form.workDate} onChange={(v) => setForm({ ...form, workDate: v })} clearable={false} />
        </Field>
        <Field label="Ghi chú">
          <Input value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} />
        </Field>
      </div>
    </Modal>
  )
}

function HolidayModal({ onClose }: { onClose: () => void }) {
  const toast = useToast()
  const save = useSaveHoliday()
  const [form, setForm] = useState({ holidayDate: todayISO(), name: '', holidayType: 'public', note: '' })

  return (
    <Modal
      open
      onClose={onClose}
      title="Thêm ngày nghỉ"
      size="sm"
      dismissible={false}
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
                await save.mutateAsync({ ...form, name: form.name.trim() })
                toast.success('Đã thêm ngày nghỉ')
                onClose()
              } catch (error) {
                toast.error('Không lưu được', errorMessage(error))
              }
            }}
          >
            Lưu
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <Field label="Ngày" required>
          <DatePicker value={form.holidayDate} onChange={(v) => setForm({ ...form, holidayDate: v })} clearable={false} />
        </Field>
        <Field label="Loại">
          <Select value={form.holidayType} onChange={(e) => setForm({ ...form, holidayType: e.target.value })}>
            <option value="public">Nghỉ lễ</option>
            <option value="company">Nghỉ công ty</option>
          </Select>
        </Field>
        <Field label="Tên" hint="Để trống thì lấy tên mặc định theo loại">
          <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} data-autofocus="" />
        </Field>
        <Field label="Ghi chú">
          <Input value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} />
        </Field>
      </div>
    </Modal>
  )
}

/* ============================================================================
   Quản trị chấm công
   ========================================================================== */

export function AttendanceAdminPage() {
  const toast = useToast()
  const [tab, setTab] = useState('offline')
  const [search, setSearch] = useState('')
  const [logDate, setLogDate] = useState(todayISO())
  const [offlineFilter, setOfflineFilter] = useState('pending')

  const offline = useOfflineRecords(offlineFilter, tab === 'offline')
  const config = useOfflineConfig(tab === 'offline')
  const enrollments = useFaceEnrollments('pending', tab === 'faces')
  const log = useAttendanceLog({ date: logDate, search: search.trim() || undefined }, tab === 'log')
  const faces = useRegisteredFaces(tab === 'registered')

  const review = useReviewOffline()
  const reject = useRejectEnrollment()
  const removeFace = useDeleteFace()

  const [reviewing, setReviewing] = useState<{ record: OfflineRecord; approve: boolean } | null>(null)
  const [rejecting, setRejecting] = useState<FaceEnrollmentRequest | null>(null)
  const [approving, setApproving] = useState<FaceEnrollmentRequest | null>(null)
  const [deletingFace, setDeletingFace] = useState<string | null>(null)
  const [detail, setDetail] = useState<OfflineRecord | null>(null)

  const tabs = [
    { id: 'offline', label: 'Chấm công ngoại tuyến', count: offline.data?.filter((r) => r.status === 'pending').length },
    { id: 'faces', label: 'Mẫu khuôn mặt chờ duyệt', count: enrollments.data?.length },
    { id: 'log', label: 'Nhật ký chấm công' },
    { id: 'registered', label: 'Khuôn mặt đã đăng ký', count: faces.data?.length },
  ]

  if (tab === 'faces')
    return (
      <>
        <ModuleScreen
          tabs={tabs}
          tab={tab}
          onTabChange={setTab}
          columns={[
            { key: 'name', priority: 1, header: 'Nhân viên', cell: (row) => <span className="font-medium">{row.fullName || row.username}</span>, sortValue: (r) => r.fullName },
            { key: 'username', priority: 3, header: 'Tài khoản', cell: (row) => `@${row.username}` },
            { key: 'samples', priority: 2, header: 'Số mẫu', align: 'right', cell: (row) => <span className="tnum">{row.sampleCount}</span> },
            { key: 'requested', priority: 2, header: 'Gửi lúc', cell: (row) => dateTime(row.requestedAt), sortValue: (r) => r.requestedAt },
            { key: 'expires', priority: 3, header: 'Hết hạn', cell: (row) => dateTime(row.expiresAt) },
            {
              key: 'status', priority: 1,
              header: 'Trạng thái',
              cell: (row) => <StatusBadge tone={enrollmentStatus(row.status).tone}>{enrollmentStatus(row.status).label}</StatusBadge>,
            },
            {
              key: 'actions', priority: 1,
              header: '',
              align: 'right',
              locked: true,
              cell: (row) =>
                row.status === 'pending' ? (
                  <span className="row-actions inline-flex gap-1">
                    <Button size="sm" variant="ghost" onClick={() => setApproving(row)}>
                      Duyệt
                    </Button>
                    <Button size="sm" variant="ghost" className="text-danger" onClick={() => setRejecting(row)}>
                      Từ chối
                    </Button>
                  </span>
                ) : null,
            },
          ]}
          rows={enrollments.data ?? []}
          loading={enrollments.isLoading}
          error={enrollments.error}
          onRefresh={() => enrollments.refetch()}
          defaultSort={{ key: 'requested', dir: 'desc' }}
          emptyTitle="Không có mẫu khuôn mặt nào chờ duyệt"
          emptyDescription="Nhân viên tự đăng ký trong ứng dụng Nhân sự, nhân sự đối chiếu trực tiếp rồi duyệt tại đây."
        />

        {approving && <ApproveEnrollmentModal request={approving} onClose={() => setApproving(null)} />}

        <ConfirmDialog
          open={!!rejecting}
          onClose={() => setRejecting(null)}
          title={`Từ chối mẫu của ${rejecting?.fullName || rejecting?.username || ''}`}
          confirmLabel="Từ chối"
          tone="danger"
          requireReason
          reasonLabel="Lý do từ chối"
          busy={reject.isPending}
          onConfirm={async (reason) => {
            if (!rejecting) return
            try {
              await reject.mutateAsync({ id: rejecting.id, reason })
              toast.success('Đã từ chối mẫu khuôn mặt')
              setRejecting(null)
            } catch (error) {
              toast.error('Không từ chối được', errorMessage(error))
            }
          }}
        />
      </>
    )

  if (tab === 'log')
    return (
      <ModuleScreen
        tabs={tabs}
        tab={tab}
        onTabChange={setTab}
        filters={
          <>
            <DatePicker value={logDate} onChange={setLogDate} clearable size="sm" className="w-36" />
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Tài khoản hoặc họ tên"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              onClear={() => setSearch('')}
            />
          </>
        }
        columns={[
          { key: 'time', priority: 1, header: 'Thời điểm', width: '10rem', cell: (row) => dateTime(row.occurredAt), sortValue: (r) => r.occurredAt },
          { key: 'name', priority: 1, header: 'Nhân viên', cell: (row) => <span className="font-medium">{row.fullName || row.username}</span>, sortValue: (r) => r.fullName },
          { key: 'username', priority: 3, header: 'Tài khoản', cell: (row) => `@${row.username}` },
          {
            key: 'loai', priority: 1,
            header: 'Chiều',
            cell: (row) => <StatusBadge tone={row.loai === 'Vào' ? 'ok' : 'info'}>{row.loai}</StatusBadge>,
            sortValue: (r) => r.loai,
          },
          { key: 'similarity', priority: 2, header: 'Độ khớp', align: 'right', cell: (row) => <span className="tnum">{num(row.similarity)}</span> },
          { key: 'note', priority: 3, header: 'Ghi chú', cell: (row) => row.ghiChu, truncate: true },
        ]}
        rows={log.data ?? []}
        loading={log.isLoading}
        error={log.error}
        onRefresh={() => log.refetch()}
        pageSize={50}
        defaultSort={{ key: 'time', dir: 'desc' }}
        emptyTitle="Không có lượt chấm công nào khớp bộ lọc"
      />
    )

  if (tab === 'registered')
    return (
      <>
        <ModuleScreen
          tabs={tabs}
          tab={tab}
          onTabChange={setTab}
          filters={
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Tài khoản hoặc họ tên"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              onClear={() => setSearch('')}
            />
          }
          columns={[
            { key: 'name', priority: 1, header: 'Nhân viên', cell: (row) => <span className="font-medium">{row.fullName || row.username}</span>, sortValue: (r) => r.fullName },
            { key: 'username', priority: 2, header: 'Tài khoản', cell: (row) => `@${row.username}` },
            { key: 'samples', priority: 2, header: 'Số mẫu', align: 'right', cell: (row) => <span className="tnum">{row.soMau}</span>, sortValue: (r) => r.soMau },
            { key: 'created', priority: 2, header: 'Đăng ký lúc', cell: (row) => dateTime(row.createdAt), sortValue: (r) => r.createdAt ?? '' },
            {
              key: 'actions', priority: 1,
              header: '',
              align: 'right',
              locked: true,
              cell: (row) => (
                <span className="row-actions">
                  <Button size="sm" variant="ghost" className="text-danger" onClick={() => setDeletingFace(row.username)}>
                    Xoá mẫu
                  </Button>
                </span>
              ),
            },
          ]}
          rows={(faces.data ?? []).filter((f) => !search || matches(`${f.username} ${f.fullName}`, search))}
          getKey={(row) => row.username}
          loading={faces.isLoading}
          error={faces.error}
          onRefresh={() => faces.refetch()}
          defaultSort={{ key: 'created', dir: 'desc' }}
          emptyTitle="Chưa có nhân viên nào đăng ký khuôn mặt"
        />
        <ConfirmDialog
          open={!!deletingFace}
          onClose={() => setDeletingFace(null)}
          title={`Xoá toàn bộ mẫu khuôn mặt của @${deletingFace ?? ''}`}
          message="Nhân viên sẽ phải đăng ký lại và chờ nhân sự duyệt trước khi chấm công bằng khuôn mặt."
          confirmLabel="Xoá mẫu"
          tone="danger"
          busy={removeFace.isPending}
          onConfirm={async () => {
            if (!deletingFace) return
            try {
              await removeFace.mutateAsync(deletingFace)
              toast.success('Đã xoá mẫu khuôn mặt')
              setDeletingFace(null)
            } catch (error) {
              toast.error('Không xoá được', errorMessage(error))
            }
          }}
        />
      </>
    )

  const pending = (offline.data ?? []).filter((r) => r.status === 'pending')
  const risky = pending.filter((r) => riskFlags(r.flags).length > 0)

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Chờ duyệt" value={offline.data ? pending.length : '…'} tone={pending.length ? 'warn' : undefined} />
            <Figure label="Có cờ rủi ro" value={offline.data ? risky.length : '…'} tone={risky.length ? 'danger' : undefined} />
            <Figure
              label="Ngưỡng lùi giờ"
              value={config.data ? duration(config.data.maxBackdateMinutes) : '…'}
            />
            <Figure
              label="Bán kính công ty"
              value={config.data?.geofenceRadiusM ? `${num(config.data.geofenceRadiusM)} m` : 'Chưa đặt'}
            />
          </FigureStrip>
        }
        tabs={tabs}
        tab={tab}
        onTabChange={setTab}
        filters={
          <Select size="sm" value={offlineFilter} onChange={(event) => setOfflineFilter(event.target.value)} className="w-40">
            <option value="pending">Chờ duyệt</option>
            <option value="all">Tất cả</option>
            <option value="approved">Đã duyệt</option>
            <option value="rejected">Từ chối</option>
          </Select>
        }
        columns={[
          { key: 'name', priority: 1, header: 'Nhân viên', cell: (row) => <span className="font-medium">{row.fullName || row.username}</span>, sortValue: (r) => r.fullName },
          { key: 'loai', priority: 1, header: 'Chiều', cell: (row) => row.loai },
          { key: 'occurred', priority: 1, header: 'Giờ khai báo', cell: (row) => dateTime(row.occurredAt), sortValue: (r) => r.occurredAt },
          { key: 'synced', priority: 2, header: 'Giờ đồng bộ', cell: (row) => dateTime(row.syncedAt), sortValue: (r) => r.syncedAt },
          {
            key: 'backdate', priority: 2,
            header: 'Lùi giờ',
            align: 'right',
            cell: (row) => (row.backdateMinutes ? <span className="tnum text-warn">{duration(row.backdateMinutes)}</span> : null),
            sortValue: (r) => r.backdateMinutes,
          },
          {
            key: 'risk', priority: 2,
            header: 'Cờ rủi ro',
            cell: (row) => {
              const flags = riskFlags(row.flags)
              if (flags.length === 0) return <span className="text-xs text-ink-3">Không</span>
              return (
                <span className="flex flex-wrap gap-1">
                  {flags.map((f) => (
                    <StatusBadge key={f} tone="danger">
                      {RISK_LABELS[f] ?? f}
                    </StatusBadge>
                  ))}
                </span>
              )
            },
          },
          { key: 'lan', priority: 3, header: 'Mạng công ty', cell: (row) => (row.onCompanyLan ? 'Có' : 'Không'), hidden: true },
          {
            key: 'geofence', priority: 3,
            header: 'Trong phạm vi',
            cell: (row) => (row.inGeofence == null ? null : row.inGeofence ? 'Trong' : 'Ngoài'),
            hidden: true,
          },
          {
            key: 'status', priority: 1,
            header: 'Trạng thái',
            cell: (row) => <StatusBadge tone={offlineStatus(row.status).tone}>{offlineStatus(row.status).label}</StatusBadge>,
            sortValue: (r) => r.status,
          },
          {
            key: 'actions', priority: 1,
            header: '',
            align: 'right',
            locked: true,
            cell: (row) =>
              row.status === 'pending' ? (
                <span className="row-actions inline-flex gap-1">
                  <Button size="sm" variant="ghost" onClick={(e) => { e.stopPropagation(); setReviewing({ record: row, approve: true }) }}>
                    Duyệt
                  </Button>
                  <Button size="sm" variant="ghost" className="text-danger" onClick={(e) => { e.stopPropagation(); setReviewing({ record: row, approve: false }) }}>
                    Từ chối
                  </Button>
                </span>
              ) : null,
          },
        ]}
        rows={offline.data ?? []}
        getKey={(row) => row.id}
        loading={offline.isLoading}
        error={offline.error}
        onRefresh={() => offline.refetch()}
        onRowClick={(row) => setDetail(row)}
        activeKey={detail?.id}
        defaultSort={{ key: 'synced', dir: 'desc' }}
        emptyTitle="Không có bản chấm công ngoại tuyến nào"
      />

      <Drawer
        open={!!detail}
        onClose={() => setDetail(null)}
        width="sm"
        title={detail ? `${detail.fullName || detail.username} · ${detail.loai}` : ''}
        meta={detail && <StatusBadge tone={offlineStatus(detail.status).tone}>{offlineStatus(detail.status).label}</StatusBadge>}
      >
        {detail && (
          <div className="p-3">
            <Panel padded>
              <KeyValue
                rows={[
                  ['Tài khoản', `@${detail.username}`],
                  ['Giờ khai báo', dateTime(detail.occurredAt)],
                  ['Giờ đồng bộ', dateTime(detail.syncedAt)],
                  ['Lùi giờ máy', detail.backdateMinutes ? duration(detail.backdateMinutes) : 'Không'],
                  ['Mạng công ty', detail.onCompanyLan ? 'Có' : 'Không'],
                  ['Địa chỉ máy', detail.clientIp || null],
                  ['Vị trí', detail.gpsLat != null && detail.gpsLng != null ? `${detail.gpsLat.toFixed(5)}, ${detail.gpsLng.toFixed(5)}` : 'Không gửi'],
                  ['Khoảng cách tới công ty', detail.distanceM != null ? `${num(detail.distanceM)} m` : null],
                  ['Trong phạm vi', detail.inGeofence == null ? null : detail.inGeofence ? 'Trong' : 'Ngoài'],
                  ['Độ khớp khuôn mặt', num(detail.similarity)],
                  ['Chất lượng ảnh', num(detail.quality)],
                  ['Người duyệt', detail.reviewedBy || null],
                  ['Duyệt lúc', detail.reviewedAt ? dateTime(detail.reviewedAt) : null],
                  ['Ghi chú duyệt', detail.reviewNote || null],
                ]}
              />
            </Panel>
          </div>
        )}
      </Drawer>

      <ConfirmDialog
        open={!!reviewing}
        onClose={() => setReviewing(null)}
        title={
          reviewing?.approve
            ? `Duyệt lượt chấm của ${reviewing.record.fullName || reviewing.record.username}`
            : `Từ chối lượt chấm của ${reviewing?.record.fullName || reviewing?.record.username || ''}`
        }
        message={
          reviewing?.approve
            ? `Lượt chấm sẽ được ghi vào bảng công theo giờ khai báo ${dateTime(reviewing.record.occurredAt)}.`
            : 'Lượt chấm bị bỏ, không vào bảng công.'
        }
        confirmLabel={reviewing?.approve ? 'Duyệt' : 'Từ chối'}
        tone={reviewing?.approve ? 'primary' : 'danger'}
        requireReason={!reviewing?.approve}
        reasonLabel="Lý do từ chối"
        busy={review.isPending}
        onConfirm={async (reason) => {
          if (!reviewing) return
          try {
            await review.mutateAsync({ id: reviewing.record.id, approve: reviewing.approve, note: reason || undefined })
            toast.success(reviewing.approve ? 'Đã duyệt và ghi vào bảng công' : 'Đã từ chối lượt chấm')
            setReviewing(null)
          } catch (error) {
            toast.error('Không xử lý được', errorMessage(error))
          }
        }}
      />
    </>
  )
}

/**
 * Duyệt mẫu khuôn mặt: máy chủ chỉ nhận khi nhân sự khai đã đối chiếu trực tiếp và gửi kèm
 * 2 đến 3 khung chụp tại chỗ. Hai khung phải khác nhau, nên chụp cách nhau một nhịp.
 */
function ApproveEnrollmentModal({ request, onClose }: { request: FaceEnrollmentRequest; onClose: () => void }) {
  const toast = useToast()
  const camera = useCamera()
  const [shots, setShots] = useState<string[]>([])
  const [verified, setVerified] = useState(false)
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const capture = () => {
    const frame = camera.grab(640)
    if (!frame) {
      setError('Chưa lấy được khung hình. Bật camera rồi thử lại.')
      return
    }
    setError('')
    setShots((current) => [...current, frame].slice(0, 3))
  }

  const submit = async () => {
    setError('')
    if (!verified) {
      setError('Phải xác nhận đã đối chiếu trực tiếp người đăng ký.')
      return
    }
    if (shots.length < 2) {
      setError('Cần ít nhất 2 khung chụp trực tiếp.')
      return
    }
    setBusy(true)
    try {
      await api.post(`/chamcong/face-enrollments/${request.id}/approve`, {
        identityVerified: true,
        verificationMethod: 'in_person',
        note: note.trim(),
        verificationImages: shots,
      })
      toast.success('Đã duyệt mẫu khuôn mặt')
      camera.stop()
      onClose()
    } catch (err) {
      setError(errorMessage(err, 'Không duyệt được mẫu khuôn mặt.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={() => {
        camera.stop()
        onClose()
      }}
      title={`Duyệt mẫu của ${request.fullName || request.username}`}
      description="Nhân viên phải có mặt cùng nhân sự. Chụp 2 đến 3 khung tại chỗ để lưu bằng chứng đối chiếu."
      size="md"
      dismissible={false}
      footer={
        <>
          <Button
            size="sm"
            onClick={() => {
              camera.stop()
              onClose()
            }}
            disabled={busy}
          >
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={busy} onClick={() => void submit()}>
            Duyệt mẫu
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <div className="relative grid aspect-video place-items-center overflow-hidden rounded-md bg-[#0f1520]">
          <video
            ref={camera.videoRef}
            playsInline
            muted
            className={cn('size-full object-cover', camera.state !== 'on' && 'invisible')}
            style={{ transform: 'scaleX(-1)' }}
          />
          {camera.state !== 'on' && (
            <p className="absolute text-sm text-white/70">
              {camera.state === 'starting' ? 'Đang mở camera' : 'Camera chưa bật'}
            </p>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {camera.state === 'on' ? (
            <>
              <Button size="sm" variant="primary" icon={<Camera className="size-3.5" strokeWidth={1.7} />} onClick={capture} disabled={shots.length >= 3}>
                Chụp khung {shots.length + 1}
              </Button>
              <Button size="sm" onClick={camera.stop}>
                Tắt camera
              </Button>
            </>
          ) : (
            <Button size="sm" variant="primary" loading={camera.state === 'starting'} icon={<Camera className="size-3.5" strokeWidth={1.7} />} onClick={() => void camera.start()}>
              Bật camera
            </Button>
          )}
          {shots.length > 0 && (
            <Button size="sm" variant="ghost" icon={<RotateCw className="size-3.5" strokeWidth={1.7} />} onClick={() => setShots([])}>
              Chụp lại
            </Button>
          )}
          <span className="ml-auto text-xs text-ink-3">Đã chụp {shots.length}/3</span>
        </div>

        {shots.length > 0 && (
          <div className="flex gap-2">
            {shots.map((shot, index) => (
              <span key={index} className="relative">
                <img src={shot} alt="" className="h-20 rounded-sm border border-line object-cover" />
                <button
                  type="button"
                  aria-label="Bỏ khung này"
                  onClick={() => setShots((current) => current.filter((_, i) => i !== index))}
                  className="absolute -top-1.5 -right-1.5 grid size-5 place-items-center rounded-full border border-line bg-panel text-ink-3 hover:text-danger"
                >
                  <Trash2 className="size-3" strokeWidth={1.8} />
                </button>
              </span>
            ))}
          </div>
        )}

        {camera.error && <InlineAlert tone="danger">{camera.error}</InlineAlert>}

        <Checkbox
          label="Tôi đã đối chiếu trực tiếp giấy tờ tuỳ thân của người đăng ký"
          checked={verified}
          onChange={(e) => setVerified(e.target.checked)}
        />
        <Field label="Ghi chú đối chiếu">
          <Textarea value={note} onChange={(e) => setNote(e.target.value)} rows={2} />
        </Field>

        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
      </div>
    </Modal>
  )
}
