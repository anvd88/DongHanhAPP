import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Plus, Upload } from 'lucide-react'
import { api } from '@/lib/http'
import { ago, dateTime, monthLabel } from '@/lib/format'
import { matches } from '@/lib/text'
import { useAuth } from '@/auth/AuthProvider'
import { ROLE_LABELS, SCOPE_LABELS } from '@/lib/permissions'
import { NAV } from '@/nav/navigation'
import { visibleRoutes } from '@/shell/Sidebar'
import { useIsHandheld } from '@/lib/device'
import { useFiscal } from '@/shell/FiscalContext'
import {
  accountState,
  auditExportUrl,
  useApproveUser,
  useAppConfig,
  useAudit,
  useAuditFilters,
  useCreateUser,
  useDeleteReleases,
  useDeleteUser,
  usePublishRelease,
  useReleases,
  useIssueRecoveryCode,
  useResetPassword,
  useRoleCatalog,
  useSaveAppConfig,
  useSetPrimaryRole,
  useSetSecondaryRole,
  useSetUserLock,
  useUploadRelease,
  useUsers,
  type AppConfig,
  type AuditItem,
  type AuditQuery,
  type Release,
  type UserAccount,
} from '@/api/system'
import {
  Button,
  Checkbox,
  ConfirmDialog,
  Drawer,
  Field,
  Figure,
  FigureStrip,
  FormGrid,
  InlineAlert,
  Input,
  KeyValue,
  Modal,
  MonthPicker,
  NumberInput,
  Panel,
  PageHeader,
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
   Tài khoản & phân quyền
   ========================================================================== */

export function UsersPage() {
  const toast = useToast()
  const [search, setSearch] = useState('')
  const [role, setRole] = useState('')
  const [tab, setTab] = useState('active')
  const users = useUsers()
  const roles = useRoleCatalog()
  const approve = useApproveUser()
  const lock = useSetUserLock()
  const remove = useDeleteUser()
  const issueCode = useIssueRecoveryCode()
  const reset = useResetPassword()
  const [openId, setOpenId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [removing, setRemoving] = useState<UserAccount | null>(null)
  const [codeResult, setCodeResult] = useState<
    { user: UserAccount; kind: 'recovery' | 'password'; code: string | null; message: string } | null
  >(null)

  const all = users.data ?? []
  const rows = useMemo(
    () =>
      all.filter((u) => {
        if (accountState(u).id !== tab) return false
        if (role && u.role !== role && !u.secondaryRoles.includes(role)) return false
        if (search && !matches(`${u.username} ${u.fullName} ${u.email}`, search)) return false
        return true
      }),
    [all, tab, role, search],
  )

  const count = (id: string) => all.filter((u) => accountState(u).id === id).length

  const columns: Column<UserAccount>[] = [
    {
      key: 'username',
      priority: 1,
      header: 'Tên đăng nhập',
      width: '10rem',
      cell: (row) => <span className="font-medium">{row.username}</span>,
      sortValue: (r) => r.username,
    },
    {
      key: 'fullName',
      priority: 1,
      header: 'Họ tên',
      cell: (row) => (
        <span className="flex flex-col">
          <span>{row.fullName || '—'}</span>
          {row.email && <span className="text-xs text-ink-3">{row.email}</span>}
        </span>
      ),
      sortValue: (r) => r.fullName,
    },
    {
      key: 'primaryRole',
      priority: 1,
      header: 'Vai trò chính',
      width: '10rem',
      cell: (row) => ROLE_LABELS[row.role] ?? row.role,
      sortValue: (r) => r.role,
    },
    {
      key: 'secondaryRoles',
      priority: 2,
      header: 'Vai trò phụ',
      cell: (row) => row.secondaryRoles.map((r) => ROLE_LABELS[r] ?? r).join(', '),
      truncate: true,
    },
    {
      key: 'lastSeen',
      priority: 1,
      header: 'Hoạt động gần nhất',
      width: '10rem',
      cell: (row) =>
        row.isOnline ? <StatusBadge tone="ok">Đang trực tuyến</StatusBadge> : ago(row.lastSeen),
      sortValue: (r) => r.lastSeen ?? '',
    },
    {
      key: 'createdAt',
      priority: 3,
      header: 'Tạo lúc',
      width: '10rem',
      cell: (row) => dateTime(row.createdAt),
      hidden: true,
    },
    {
      key: 'status',
      priority: 1,
      header: 'Trạng thái',
      width: '8rem',
      cell: (row) => <StatusBadge tone={accountState(row).tone}>{accountState(row).label}</StatusBadge>,
      sortValue: (r) => accountState(r).label,
    },
    {
      key: 'action',
      priority: 1,
      header: '',
      align: 'right',
      locked: true,
      cell: (row) => (
        <span className="row-actions flex justify-end gap-1">
          {row.approvalStatus === 'Pending' && (
            <Button
              size="sm"
              variant="ghost"
              onClick={async (e) => {
                e.stopPropagation()
                try {
                  await approve.mutateAsync(row.id)
                  toast.success(`Đã duyệt tài khoản ${row.username}`)
                } catch (err) {
                  toast.error('Không duyệt được', errorMessage(err))
                }
              }}
            >
              Duyệt
            </Button>
          )}
          <Button
            size="sm"
            variant="ghost"
            className={row.isActive ? 'text-danger' : undefined}
            onClick={async (e) => {
              e.stopPropagation()
              try {
                await lock.mutateAsync({ id: row.id, locked: row.isActive })
                toast.success(row.isActive ? 'Đã khoá tài khoản' : 'Đã mở khoá tài khoản')
              } catch (err) {
                toast.error('Không đổi được trạng thái', errorMessage(err))
              }
            }}
          >
            {row.isActive ? 'Khoá' : 'Mở khoá'}
          </Button>
        </span>
      ),
    },
  ]

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Tài khoản đang hoạt động" value={users.data ? count('active') : '…'} />
            <Figure label="Chờ duyệt" value={users.data ? count('pending') : '…'} tone={count('pending') ? 'warn' : undefined} />
            <Figure label="Đã khoá" value={users.data ? count('locked') : '…'} />
            <Figure label="Đang trực tuyến" value={users.data ? all.filter((u) => u.isOnline).length : '…'} />
          </FigureStrip>
        }
        tabs={[
          { id: 'active', label: 'Đang hoạt động', count: count('active') },
          { id: 'pending', label: 'Chờ duyệt', count: count('pending') },
          { id: 'locked', label: 'Đã khoá', count: count('locked') },
        ]}
        tab={tab}
        onTabChange={setTab}
        actions={
          <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setCreating(true)}>
            Tạo tài khoản
          </Button>
        }
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Tên đăng nhập, họ tên"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
            <Select size="sm" className="w-44" value={role} onChange={(e) => setRole(e.target.value)}>
              <option value="">Mọi vai trò</option>
              {(roles.data ?? [])
                .filter((r) => !r.technical)
                .map((r) => (
                  <option key={r.role} value={r.role}>
                    {r.label}
                  </option>
                ))}
            </Select>
          </>
        }
        columns={columns}
        rows={rows}
        loading={users.isLoading}
        error={users.error}
        onRefresh={() => users.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        emptyTitle="Không có tài khoản nào trong mục này"
      />

      <UserDrawer
        user={all.find((u) => u.id === openId) ?? null}
        onClose={() => setOpenId(null)}
        onDelete={(u) => setRemoving(u)}
        onIssueRecoveryCode={async (u) => {
          try {
            const result = await issueCode.mutateAsync({ id: u.id })
            if (result.delivered) toast.success(result.message)
            setCodeResult({ user: u, kind: 'recovery', code: result.code, message: result.message })
          } catch (err) {
            toast.error('Không cấp được mã khôi phục', errorMessage(err))
          }
        }}
        onResetPassword={async (u) => {
          try {
            const result = await reset.mutateAsync(u.id)
            setCodeResult({
              user: u,
              kind: 'password',
              code: result?.code ?? '',
              message: 'Mật khẩu cũ đã bị thay. Đọc mật khẩu tạm này cho người dùng và nhắc họ đổi ngay sau khi vào.',
            })
          } catch (err) {
            toast.error('Không đặt lại được mật khẩu', errorMessage(err))
          }
        }}
      />

      {creating && <CreateUserModal onClose={() => setCreating(false)} />}

      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        title={`Xoá tài khoản ${removing?.username ?? ''}`}
        message="Tài khoản bị đánh dấu đã xoá và không đăng nhập được nữa."
        confirmLabel="Xoá tài khoản"
        tone="danger"
        busy={remove.isPending}
        onConfirm={async () => {
          if (!removing) return
          try {
            await remove.mutateAsync(removing.id)
            toast.success('Đã xoá tài khoản')
            setRemoving(null)
            setOpenId(null)
          } catch (err) {
            toast.error('Không xoá được tài khoản', errorMessage(err))
          }
        }}
      />

      <Modal
        open={!!codeResult}
        onClose={() => setCodeResult(null)}
        title={codeResult?.kind === 'password' ? 'Mật khẩu tạm' : 'Mã khôi phục mật khẩu'}
        description={
          codeResult?.kind === 'password'
            ? 'Người dùng đăng nhập bằng mật khẩu này rồi tự đổi lại.'
            : 'Người dùng nhập mã này ở màn quên mật khẩu để tự đặt mật khẩu mới. Mã sống 7 ngày và chỉ dùng một lần.'
        }
        size="sm"
        footer={
          <Button size="sm" variant="primary" onClick={() => setCodeResult(null)}>
            Đã ghi lại
          </Button>
        }
      >
        <div className="flex flex-col gap-2 p-4">
          <p className="text-sm text-ink-2">Tài khoản {codeResult?.user.username}</p>
          {codeResult?.code ? (
            <p className="select-all text-center text-2xl font-semibold tracking-[0.35em] tnum text-ink">{codeResult.code}</p>
          ) : (
            <InlineAlert tone="ok">{codeResult?.message}</InlineAlert>
          )}
          {codeResult?.code && <p className="text-xs text-ink-3">{codeResult.message}</p>}
        </div>
      </Modal>
    </>
  )
}

function UserDrawer({
  user,
  onClose,
  onDelete,
  onIssueRecoveryCode,
  onResetPassword,
}: {
  user: UserAccount | null
  onClose: () => void
  onDelete: (user: UserAccount) => void
  onIssueRecoveryCode: (user: UserAccount) => void
  onResetPassword: (user: UserAccount) => void
}) {
  const toast = useToast()
  const roles = useRoleCatalog(!!user)
  const setPrimary = useSetPrimaryRole()
  const setSecondary = useSetSecondaryRole()
  const [reason, setReason] = useState('')

  const assignable = (roles.data ?? []).filter((r) => r.assignable && !r.technical)
  const current = roles.data?.find((r) => r.role === user?.role)

  return (
    <Drawer
      open={!!user}
      onClose={onClose}
      width="lg"
      title={user ? user.fullName || user.username : 'Tài khoản'}
      meta={
        user && (
          <>
            <span>{user.username}</span>
            <StatusBadge tone={accountState(user).tone}>{accountState(user).label}</StatusBadge>
          </>
        )
      }
      actions={
        user && (
          <>
            <Button size="sm" variant="primary" onClick={() => onIssueRecoveryCode(user)}>
              Cấp mã khôi phục
            </Button>
            <Button size="sm" onClick={() => onResetPassword(user)}>
              Đặt lại mật khẩu
            </Button>
            <Button size="sm" variant="ghost" className="text-danger" onClick={() => onDelete(user)}>
              Xoá
            </Button>
          </>
        )
      }
    >
      <div className="flex flex-col gap-3 p-3">
        {user?.rolesManagedByPositions && (
          <InlineAlert tone="info" title="Vai trò của người này lấy từ chức vụ">
            Hồ sơ nhân sự đã gán chức vụ, nên vai trò do chức vụ quyết định. Đổi chức vụ ở màn Quản lý nhân sự.
          </InlineAlert>
        )}

        <Panel title="Thông tin tài khoản" padded>
          <KeyValue
            rows={[
              ['Tên đăng nhập', user?.username],
              ['Họ tên', user?.fullName || null],
              ['Thư điện tử', user?.email || null],
              ['Tạo lúc', user ? dateTime(user.createdAt) : null],
              ['Hoạt động gần nhất', user ? (user.isOnline ? 'Đang trực tuyến' : ago(user.lastSeen)) : null],
            ]}
          />
        </Panel>

        <Panel title="Vai trò chính" padded>
          <div className="flex flex-col gap-2.5">
            <Field label="Vai trò" hint="Đổi vai trò áp dụng ngay ở request kế tiếp của người đó.">
              <Select
                value={user?.role ?? ''}
                disabled={!user || user.rolesManagedByPositions || setPrimary.isPending}
                onChange={async (e) => {
                  if (!user) return
                  try {
                    await setPrimary.mutateAsync({ id: user.id, role: e.target.value, reason: reason.trim() || undefined })
                    toast.success('Đã đổi vai trò chính')
                  } catch (err) {
                    toast.error('Không đổi được vai trò', errorMessage(err))
                  }
                }}
              >
                {assignable.map((r) => (
                  <option key={r.role} value={r.role}>
                    {r.label}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Lý do đổi quyền" hint="Ghi vào lịch sử phân quyền để sau này tra soát.">
              <Input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Ví dụ: bàn giao kho tháng 9" />
            </Field>
          </div>
        </Panel>

        <Panel title="Vai trò phụ" meta="Cấp thêm quyền mà không đổi vai trò chính" padded>
          <div className="flex flex-wrap gap-3">
            {assignable
              .filter((r) => r.role !== user?.role)
              .map((r) => (
                <Checkbox
                  key={r.role}
                  label={r.label}
                  checked={!!user?.secondaryRoles.includes(r.role)}
                  disabled={!user || user.rolesManagedByPositions || setSecondary.isPending}
                  onChange={async (e) => {
                    if (!user) return
                    try {
                      await setSecondary.mutateAsync({
                        id: user.id,
                        role: r.role,
                        grant: e.target.checked,
                        reason: reason.trim() || undefined,
                      })
                      toast.success(e.target.checked ? `Đã cấp vai trò ${r.label}` : `Đã thu vai trò ${r.label}`)
                    } catch (err) {
                      toast.error('Không đổi được vai trò phụ', errorMessage(err))
                    }
                  }}
                />
              ))}
          </div>
        </Panel>

        <Panel title={`Vai trò ${current?.label ?? ''} cho phép làm gì`} padded>
          <ul className="grid gap-1 text-sm sm:grid-cols-2">
            {(current?.permissions ?? []).map((p) => (
              <li key={p.key} className="text-ink-2">
                {p.label}
              </li>
            ))}
            {!current?.permissions.length && <li className="text-ink-3">Không có quyền nào.</li>}
          </ul>
        </Panel>
      </div>
    </Drawer>
  )
}

function CreateUserModal({ onClose }: { onClose: () => void }) {
  const toast = useToast()
  const roles = useRoleCatalog()
  const create = useCreateUser()
  const [username, setUsername] = useState('')
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('Employee')
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const problems = {
    username: !username.trim() ? 'Nhập tên đăng nhập' : null,
    password: password.length < 6 ? 'Mật khẩu tối thiểu 6 ký tự' : null,
  }
  const valid = !problems.username && !problems.password

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      title="Tạo tài khoản"
      description="Người dùng đăng nhập bằng mật khẩu này và nên đổi ngay lần đầu."
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={create.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            loading={create.isPending}
            onClick={async () => {
              setTouched(true)
              if (!valid) return
              setError(null)
              try {
                await create.mutateAsync({ username: username.trim(), fullName: fullName.trim(), email: email.trim(), password, role })
                toast.success('Đã tạo tài khoản')
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không tạo được tài khoản.'))
              }
            }}
          >
            Tạo tài khoản
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <FormGrid cols={2}>
          <Field label="Tên đăng nhập" required error={touched ? problems.username : null}>
            <Input value={username} onChange={(e) => setUsername(e.target.value)} autoFocus autoComplete="off" />
          </Field>
          <Field label="Họ tên">
            <Input value={fullName} onChange={(e) => setFullName(e.target.value)} />
          </Field>
          <Field label="Thư điện tử">
            <Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
          </Field>
          <Field label="Mật khẩu ban đầu" required error={touched ? problems.password : null}>
            <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" />
          </Field>
          <Field label="Vai trò">
            <Select value={role} onChange={(e) => setRole(e.target.value)}>
              {(roles.data ?? [])
                .filter((r) => r.assignable && !r.technical)
                .map((r) => (
                  <option key={r.role} value={r.role}>
                    {r.label}
                  </option>
                ))}
            </Select>
          </Field>
        </FormGrid>
      </div>
    </Modal>
  )
}

/* ============================================================================
   Cấu hình hệ thống

   Bảng app_config một dòng: thông báo chạy chữ, công tắc tính năng, nội dung xin quyền. Sửa ở đây
   là ứng dụng áp dụng ngay, không phải phát hành APK mới.
   ========================================================================== */

const FEATURE_LABELS: Array<[keyof AppConfig['features'], string, string]> = [
  ['locationEnabled', 'Vị trí', 'Ứng dụng xin quyền vị trí và gắn toạ độ vào chấm công.'],
  ['offlineAttendanceEnabled', 'Chấm công ngoại tuyến', 'Cho phép chấm khi mất mạng rồi đồng bộ chờ duyệt.'],
  ['biometricAttendanceEnabled', 'Chấm công sinh trắc', 'Nhận diện khuôn mặt ngay trên máy.'],
  ['companyPortalEnabled', 'Cổng thông tin', 'Hiện tin tức và sự kiện công ty trên ứng dụng.'],
]

export function SettingsPage() {
  const toast = useToast()
  const config = useAppConfig()
  const save = useSaveAppConfig()
  const [draft, setDraft] = useState<AppConfig | null>(null)
  const [tab, setTab] = useState('app')

  useEffect(() => {
    if (config.data) setDraft(config.data)
  }, [config.data])

  const patch = (change: Partial<AppConfig>) => setDraft((d) => (d ? { ...d, ...change } : d))
  const dirty = !!draft && !!config.data && JSON.stringify(draft) !== JSON.stringify(config.data)

  const submit = async () => {
    if (!draft) return
    try {
      await save.mutateAsync(draft)
      toast.success('Đã lưu cấu hình')
    } catch (e) {
      toast.error('Không lưu được cấu hình', errorMessage(e))
    }
  }

  if (config.isLoading || !draft) {
    return (
      <Stack>
        <Panel padded>
          <p className="text-sm text-ink-3">{config.error ? errorMessage(config.error) : 'Đang tải cấu hình…'}</p>
        </Panel>
      </Stack>
    )
  }

  return (
    <Stack>
      <Panel
        title="Cấu hình điều khiển từ xa"
        meta={dirty ? 'Có thay đổi chưa lưu' : 'Đã đồng bộ'}
        actions={
          <>
            <Select size="sm" className="w-48" value={tab} onChange={(e) => setTab(e.target.value)}>
              <option value="app">Ứng dụng &amp; thông báo</option>
              <option value="features">Công tắc tính năng</option>
              <option value="onboarding">Nội dung xin quyền</option>
            </Select>
            <Button size="sm" variant="primary" disabled={!dirty} loading={save.isPending} onClick={submit}>
              Lưu thay đổi
            </Button>
          </>
        }
        padded
      >
        {tab === 'app' && (
          <div className="flex flex-col gap-3">
            <FormGrid cols={2}>
              <Field label="Thông báo hiện trên ứng dụng" hint="Để trống là không hiện băng thông báo nào.">
                <Textarea rows={2} value={draft.announcement} onChange={(e) => patch({ announcement: e.target.value })} />
              </Field>
              <Field label="Mức độ">
                <Select
                  value={draft.announcementLevel}
                  onChange={(e) => patch({ announcementLevel: e.target.value as AppConfig['announcementLevel'] })}
                >
                  <option value="info">Thông tin</option>
                  <option value="warning">Cảnh báo</option>
                  <option value="critical">Khẩn cấp</option>
                </Select>
              </Field>
              <Field label="Nhịp tự làm mới khi mở ứng dụng (giây)" hint="Từ 5 đến 3600 giây.">
                <NumberInput
                  value={draft.foregroundPollSeconds}
                  onChange={(v) => patch({ foregroundPollSeconds: v ?? 20 })}
                />
              </Field>
              <Field label="Nhắc đăng ký khuôn mặt">
                <Checkbox
                  label="Hiện băng nhắc trên trang chủ ứng dụng"
                  checked={draft.faceEnrollBannerEnabled}
                  onChange={(e) => patch({ faceEnrollBannerEnabled: e.target.checked })}
                />
              </Field>
            </FormGrid>
            <Field
              label="Lời nhắc chạy chữ trên trang chủ"
              hint="Mỗi dòng một lời nhắc, tối đa 20 dòng. Chúng luân phiên cùng lời chào theo buổi."
            >
              <Textarea
                rows={5}
                value={draft.notices.join('\n')}
                onChange={(e) => patch({ notices: e.target.value.split('\n') })}
              />
            </Field>
          </div>
        )}

        {tab === 'features' && (
          <div className="flex flex-col gap-3">
            <InlineAlert tone="info">
              Tắt một công tắc là ứng dụng ẩn phần đó ngay ở lần mở kế tiếp, không cần cài lại.
            </InlineAlert>
            <div className="flex flex-col gap-2.5">
              {FEATURE_LABELS.map(([key, label, hint]) => (
                <Field key={key} hint={hint}>
                  <Checkbox
                    label={label}
                    checked={draft.features[key]}
                    onChange={(e) => patch({ features: { ...draft.features, [key]: e.target.checked } })}
                  />
                </Field>
              ))}
            </div>
          </div>
        )}

        {tab === 'onboarding' && (
          <FormGrid cols={2}>
            <Field label="Lời giới thiệu khi mở ứng dụng lần đầu">
              <Textarea rows={3} value={draft.onboarding.introText} onChange={(e) => patch({ onboarding: { ...draft.onboarding, introText: e.target.value } })} />
            </Field>
            <Field label="Vì sao cần máy ảnh">
              <Textarea rows={3} value={draft.onboarding.cameraReason} onChange={(e) => patch({ onboarding: { ...draft.onboarding, cameraReason: e.target.value } })} />
            </Field>
            <Field label="Vì sao cần vị trí">
              <Textarea rows={3} value={draft.onboarding.locationReason} onChange={(e) => patch({ onboarding: { ...draft.onboarding, locationReason: e.target.value } })} />
            </Field>
            <Field label="Vì sao cần thông báo">
              <Textarea rows={3} value={draft.onboarding.notificationReason} onChange={(e) => patch({ onboarding: { ...draft.onboarding, notificationReason: e.target.value } })} />
            </Field>
          </FormGrid>
        )}
      </Panel>

      <Panel title="Tham số cắt ảnh chân dung" meta="Áp cho ảnh thẻ chụp trên ứng dụng" padded>
        <FormGrid cols={4}>
          <Field label="Hệ số chiều cao" hint="Lớn hơn thì khung lấy rộng hơn (1,0 – 4,0).">
            <NumberInput decimals={2} value={draft.portraitHeightFactor} onChange={(v) => patch({ portraitHeightFactor: v ?? 1.85 })} />
          </Field>
          <Field label="Nhích tâm khung" hint="Dương là lấy thêm đỉnh đầu (-1,0 – 1,0).">
            <NumberInput decimals={2} allowNegative value={draft.portraitVerticalNudge} onChange={(v) => patch({ portraitVerticalNudge: v ?? 0.15 })} />
          </Field>
          <Field label="Tỉ lệ ngang / dọc" hint="0,75 là ảnh 3:4.">
            <NumberInput decimals={2} value={draft.portraitAspect} onChange={(v) => patch({ portraitAspect: v ?? 0.75 })} />
          </Field>
          <Field label="Bề rộng tối thiểu" hint="Theo bề rộng khuôn mặt (0,5 – 3,0).">
            <NumberInput decimals={2} value={draft.portraitMinWidthFactor} onChange={(v) => patch({ portraitMinWidthFactor: v ?? 1.35 })} />
          </Field>
        </FormGrid>
      </Panel>
    </Stack>
  )
}

/* ============================================================================
   Bản cập nhật APK
   ========================================================================== */

const fileSize = (bytes: number) =>
  bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.round(bytes / 1024)} KB`

export function ReleasesPage() {
  const toast = useToast()
  const releases = useReleases()
  const publish = usePublishRelease()
  const remove = useDeleteReleases()
  const [uploading, setUploading] = useState(false)
  const [removing, setRemoving] = useState<Release[] | null>(null)

  const rows = releases.data ?? []
  const live = rows.find((r) => r.isPublished)

  const columns: Column<Release>[] = [
    {
      key: 'versionName',
      priority: 1,
      header: 'Phiên bản',
      cell: (row) => (
        <span className="flex flex-col">
          <span className="font-medium">{row.version}</span>
          <span className="text-xs text-ink-3">{row.apkFileName}</span>
        </span>
      ),
      sortValue: (r) => r.versionCode,
    },
    { key: 'versionCode', priority: 1, header: 'Mã bản', align: 'right', width: '6rem', cell: (row) => <span className="tnum">{row.versionCode}</span>, sortValue: (r) => r.versionCode },
    { key: 'target', priority: 3, header: 'Ứng dụng', width: '8rem', cell: (row) => row.appTarget, hidden: true },
    { key: 'notes', priority: 2, header: 'Nội dung bản mới', cell: (row) => row.releaseNotes, truncate: true },
    { key: 'size', priority: 1, header: 'Dung lượng', align: 'right', width: '7rem', cell: (row) => fileSize(row.apkSize), sortValue: (r) => r.apkSize },
    { key: 'uploadedAt', priority: 1, header: 'Đăng lúc', width: '10rem', cell: (row) => dateTime(row.publishedAt), sortValue: (r) => r.publishedAt },
    { key: 'publishedBy', priority: 3, header: 'Người đăng', cell: (row) => row.publishedBy, hidden: true },
    {
      key: 'status',
      priority: 1,
      header: 'Trạng thái',
      width: '9rem',
      cell: (row) =>
        row.isPublished ? (
          <StatusBadge tone={row.isMandatory ? 'danger' : 'ok'}>{row.isMandatory ? 'Đang phát hành · bắt buộc' : 'Đang phát hành'}</StatusBadge>
        ) : (
          <StatusBadge>Bản nháp</StatusBadge>
        ),
      sortValue: (r) => (r.isPublished ? 0 : 1),
    },
    {
      key: 'action',
      priority: 1,
      header: '',
      align: 'right',
      locked: true,
      cell: (row) => (
        <span className="row-actions flex justify-end gap-1">
          <Button
            size="sm"
            variant="ghost"
            onClick={(e) => {
              e.stopPropagation()
              window.location.assign(`/api/releases/${row.id}/download`)
            }}
          >
            Tải về
          </Button>
          <Button
            size="sm"
            variant="ghost"
            loading={publish.isPending}
            onClick={async (e) => {
              e.stopPropagation()
              try {
                await publish.mutateAsync({ id: row.id, isPublished: !row.isPublished })
                toast.success(row.isPublished ? 'Đã gỡ phát hành' : 'Đã phát hành bản mới')
              } catch (err) {
                toast.error('Không đổi được trạng thái', errorMessage(err))
              }
            }}
          >
            {row.isPublished ? 'Gỡ phát hành' : 'Phát hành'}
          </Button>
        </span>
      ),
    },
  ]

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Bản đang phát hành" value={live ? live.version : releases.data ? 'Chưa có' : '…'} />
            <Figure label="Mã bản đang phát hành" value={live ? live.versionCode : '—'} />
            <Figure label="Dung lượng bản mới nhất" value={rows[0] ? fileSize(rows[0].apkSize) : '—'} />
            <Figure label="Số bản đang giữ" value={releases.data ? rows.length : '…'} />
          </FigureStrip>
        }
        actions={
          <Button variant="primary" size="sm" icon={<Upload className="size-3.5" strokeWidth={1.7} />} onClick={() => setUploading(true)}>
            Đăng bản mới
          </Button>
        }
        columns={columns}
        rows={rows}
        getKey={(row) => row.id}
        loading={releases.isLoading}
        error={releases.error}
        onRefresh={() => releases.refetch()}
        defaultSort={{ key: 'versionCode', dir: 'desc' }}
        selectable
        bulkActions={(selected, clear) => (
          <Button
            size="sm"
            variant="danger"
            onClick={() => {
              setRemoving(rows.filter((r) => selected.has(r.id)))
              clear()
            }}
          >
            Gỡ các bản đã chọn
          </Button>
        )}
        emptyTitle="Chưa đăng bản cập nhật nào"
      />

      {uploading && <ReleaseUploadModal onClose={() => setUploading(false)} />}

      <ConfirmDialog
        open={!!removing?.length}
        onClose={() => setRemoving(null)}
        title={removing?.length === 1 ? `Gỡ bản ${removing[0].version}` : `Gỡ ${removing?.length ?? 0} bản cập nhật`}
        message="Tệp APK bị xoá khỏi đĩa máy chủ, máy đã cài không bị ảnh hưởng."
        confirmLabel="Gỡ"
        tone="danger"
        busy={remove.isPending}
        onConfirm={async () => {
          if (!removing?.length) return
          try {
            await remove.mutateAsync(removing.map((r) => r.id))
            toast.success('Đã gỡ bản cập nhật')
            setRemoving(null)
          } catch (err) {
            toast.error('Không gỡ được', errorMessage(err))
          }
        }}
      />
    </>
  )
}

function ReleaseUploadModal({ onClose }: { onClose: () => void }) {
  const toast = useToast()
  const upload = useUploadRelease()
  const releases = useReleases()
  const inputRef = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [version, setVersion] = useState('')
  const [versionCode, setVersionCode] = useState<number | null>(null)
  const [notes, setNotes] = useState('')
  const [mandatory, setMandatory] = useState(false)
  const [publishNow, setPublishNow] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const highest = Math.max(0, ...(releases.data ?? []).filter((r) => r.isPublished).map((r) => r.versionCode))
  const codeTooLow = publishNow && versionCode !== null && versionCode <= highest

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      title="Đăng bản cập nhật"
      description="Tệp APK nằm trên đĩa máy chủ; cơ sở dữ liệu chỉ giữ phần mô tả."
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={upload.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            loading={upload.isPending}
            disabled={!file || !version.trim() || !versionCode || codeTooLow}
            onClick={async () => {
              if (!file || !versionCode) return
              setError(null)
              try {
                await upload.mutateAsync({
                  file,
                  version: version.trim(),
                  versionCode,
                  appTarget: 'hr-apk',
                  releaseNotes: notes.trim(),
                  isMandatory: mandatory,
                  isPublished: publishNow,
                })
                toast.success('Đã đăng bản cập nhật')
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không đăng được bản cập nhật.'))
              }
            }}
          >
            Đăng bản mới
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        {codeTooLow && (
          <InlineAlert tone="warn" title="Mã bản phải lớn hơn bản đang phát hành">
            Bản đang phát hành có mã {highest}. Máy đã cài chỉ nhận bản có mã lớn hơn.
          </InlineAlert>
        )}
        <Field label="Tệp APK" required hint={file ? `${file.name} · ${fileSize(file.size)}` : 'Chỉ nhận tệp .apk'}>
          <div className="flex items-center gap-2">
            <input
              ref={inputRef}
              type="file"
              accept=".apk,application/vnd.android.package-archive"
              className="hidden"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
            <Button size="sm" onClick={() => inputRef.current?.click()}>
              Chọn tệp
            </Button>
            <span className="truncate text-sm text-ink-2">{file?.name ?? 'Chưa chọn tệp'}</span>
          </div>
        </Field>
        <FormGrid cols={2}>
          <Field label="Phiên bản" required hint="Tên người dùng nhìn thấy, ví dụ 2.4.1">
            <Input value={version} onChange={(e) => setVersion(e.target.value)} placeholder="2.4.1" />
          </Field>
          <Field label="Mã bản" required hint={`Phải lớn hơn ${highest} để máy đã cài nhận được.`}>
            <NumberInput value={versionCode} onChange={setVersionCode} />
          </Field>
        </FormGrid>
        <Field label="Nội dung bản mới">
          <Textarea rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Mỗi dòng một thay đổi" />
        </Field>
        <div className="flex flex-wrap gap-4">
          <Checkbox label="Phát hành ngay" checked={publishNow} onChange={(e) => setPublishNow(e.target.checked)} />
          <Checkbox label="Bắt buộc cập nhật" checked={mandatory} onChange={(e) => setMandatory(e.target.checked)} />
        </div>
      </div>
    </Modal>
  )
}

/* ============================================================================
   Nhật ký hoạt động

   Đường dẫn /saoluu giữ theo hợp đồng cũ với chuông thông báo. Phạm vi do máy chủ chốt: kế toán
   chỉ tra cứu được phần tiền.
   ========================================================================== */

export function AuditPage() {
  const fiscal = useFiscal()
  const filters = useAuditFilters()
  const [search, setSearch] = useState('')
  const [group, setGroup] = useState('')
  const [action, setAction] = useState('')
  const [page, setPage] = useState(1)
  const [open, setOpen] = useState<AuditItem | null>(null)

  const query: AuditQuery = {
    page,
    pageSize: 50,
    search: search || undefined,
    group: group || undefined,
    action: action || undefined,
    month: fiscal.period || undefined,
  }
  const audit = useAudit(query)

  useEffect(() => {
    setPage(1)
  }, [search, group, action, fiscal.period])

  const columns: Column<AuditItem>[] = [
    { key: 'occurredAt', priority: 1, header: 'Thời điểm', width: '10rem', cell: (row) => dateTime(row.occurredAt), sortValue: (r) => r.occurredAt },
    { key: 'actor', priority: 1, header: 'Người thực hiện', width: '10rem', cell: (row) => row.username, sortValue: (r) => r.username },
    { key: 'action', priority: 1, header: 'Hành động', cell: (row) => row.action, sortValue: (r) => r.action },
    {
      key: 'entity',
      priority: 2,
      header: 'Đối tượng',
      cell: (row) => (
        <span className="flex flex-col">
          <span>{row.entityName || row.entity}</span>
          {row.entityName && <span className="text-xs text-ink-3">{row.entity}</span>}
        </span>
      ),
      sortValue: (r) => r.entity,
    },
    { key: 'detail', priority: 2, header: 'Nội dung', cell: (row) => row.details, truncate: true },
  ]

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label={`Số việc trong ${monthLabel(fiscal.period)}`} value={audit.data ? audit.data.total : '…'} />
            <Figure label="Phạm vi bạn xem được" value={filters.data ? (filters.data.canSeeAll ? 'Toàn hệ thống' : 'Phần tiền') : '…'} />
          </FigureStrip>
        }
        actions={
          <Button size="sm" onClick={() => window.location.assign(auditExportUrl(query, 'excel'))}>
            Xuất Excel
          </Button>
        }
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Người dùng, đối tượng"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
            <MonthPicker value={fiscal.period} onChange={fiscal.setPeriod} size="sm" className="w-40" />
            <Select size="sm" className="w-44" value={group} onChange={(e) => setGroup(e.target.value)}>
              <option value="">Mọi nhóm nghiệp vụ</option>
              {(filters.data?.groups ?? []).map((g) => (
                <option key={g.key} value={g.key}>
                  {g.label}
                </option>
              ))}
            </Select>
            <Select size="sm" className="w-44" value={action} onChange={(e) => setAction(e.target.value)}>
              <option value="">Mọi hành động</option>
              {(filters.data?.actions ?? []).map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </Select>
          </>
        }
        columns={columns}
        rows={audit.data?.items ?? []}
        getKey={(row) => row.id}
        loading={audit.isLoading}
        error={audit.error}
        onRefresh={() => audit.refetch()}
        onRowClick={(row) => setOpen(row)}
        activeKey={open?.id ?? null}
        pageSize={50}
        emptyTitle="Không có việc nào khớp bộ lọc"
      >
        {(audit.data?.total ?? 0) > 50 && (
          <div className="flex items-center justify-end gap-2 text-sm text-ink-2">
            <span>
              Trang {audit.data?.page} / {Math.ceil((audit.data?.total ?? 0) / 50)}
            </span>
            <Button size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
              Trang trước
            </Button>
            <Button size="sm" disabled={page >= Math.ceil((audit.data?.total ?? 0) / 50)} onClick={() => setPage((p) => p + 1)}>
              Trang sau
            </Button>
          </div>
        )}
      </ModuleScreen>

      <Drawer
        open={!!open}
        onClose={() => setOpen(null)}
        width="lg"
        title={open ? open.action : 'Nhật ký'}
        meta={
          open && (
            <>
              <span>{open.username}</span>
              <span>{dateTime(open.occurredAt)}</span>
            </>
          )
        }
      >
        <div className="flex flex-col gap-3 p-3">
          <Panel title="Việc đã làm" padded>
            <KeyValue
              rows={[
                ['Hành động', open?.action],
                ['Đối tượng', open?.entity],
                ['Tên đối tượng', open?.entityName || null],
                ['Nội dung', open?.details || null],
              ]}
            />
          </Panel>
          {open?.before && (
            <Panel title="Trước khi đổi" padded>
              <pre className="overflow-x-auto whitespace-pre-wrap break-words text-xs text-ink-2">{open.before}</pre>
            </Panel>
          )}
          {open?.after && (
            <Panel title="Sau khi đổi" padded>
              <pre className="overflow-x-auto whitespace-pre-wrap break-words text-xs text-ink-2">{open.after}</pre>
            </Panel>
          )}
        </div>
      </Drawer>
    </>
  )
}

/* ============================================================================
   Thiết bị & phiên
   ========================================================================== */

interface Device {
  sid: string
  machineName: string
  clientKind: string
  userAgent: string
  startedAt: string | null
  lastSeen: string | null
  isActive: boolean
  revoked: boolean
  current: boolean
}

/** Thiết bị và phiên đăng nhập. Cho phép thu hồi phiên lạ từ xa. */
export function DevicesPage() {
  const queryClient = useQueryClient()
  const toast = useToast()
  const devices = useQuery({
    queryKey: ['presence', 'devices'],
    queryFn: () => api.get<Device[]>('/auth/devices'),
  })
  const revoke = useMutation({
    mutationFn: (sid: string) => api.post<void>(`/auth/devices/${encodeURIComponent(sid)}/revoke`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['presence'] }),
  })
  const [revoking, setRevoking] = useState<Device | null>(null)

  const columns: Column<Device>[] = [
    {
      key: 'machine', priority: 1,
      header: 'Thiết bị',
      cell: (row) => (
        <span className="flex flex-col">
          <span className="font-medium">{row.machineName || row.clientKind}</span>
          <span className="max-w-md truncate text-xs text-ink-3">{row.userAgent}</span>
        </span>
      ),
      sortValue: (row) => row.machineName || row.clientKind,
    },
    { key: 'kind', priority: 2, header: 'Loại', cell: (row) => row.clientKind, sortValue: (row) => row.clientKind },
    { key: 'startedAt', priority: 2, header: 'Đăng nhập', cell: (row) => dateTime(row.startedAt), sortValue: (row) => row.startedAt ?? '' },
    { key: 'lastSeen', priority: 1, header: 'Hoạt động', cell: (row) => ago(row.lastSeen), sortValue: (row) => row.lastSeen ?? '' },
    {
      key: 'status', priority: 1,
      header: 'Trạng thái',
      cell: (row) =>
        row.revoked ? (
          <StatusBadge tone="danger">Đã thu hồi</StatusBadge>
        ) : row.current ? (
          <StatusBadge tone="brand">Máy này</StatusBadge>
        ) : row.isActive ? (
          <StatusBadge tone="ok">Đang mở</StatusBadge>
        ) : (
          <StatusBadge>Đã đóng</StatusBadge>
        ),
    },
    {
      key: 'action', priority: 1,
      header: '',
      align: 'right',
      locked: true,
      cell: (row) =>
        row.revoked || row.current ? null : (
          <span className="row-actions">
            <Button size="sm" variant="ghost" className="text-danger" onClick={() => setRevoking(row)}>
              Thu hồi
            </Button>
          </span>
        ),
    },
  ]

  return (
    <>
      <ModuleScreen
        columns={columns}
        rows={devices.data ?? []}
        getKey={(row) => row.sid}
        loading={devices.isLoading}
        error={devices.error}
        onRefresh={() => devices.refetch()}
        emptyTitle="Chưa ghi nhận thiết bị nào"
        defaultSort={{ key: 'lastSeen', dir: 'desc' }}
      />
      <ConfirmDialog
        open={!!revoking}
        onClose={() => setRevoking(null)}
        title="Thu hồi phiên đăng nhập"
        message={revoking ? `Thiết bị ${revoking.machineName || revoking.clientKind} sẽ bị đăng xuất ngay.` : undefined}
        confirmLabel="Thu hồi"
        tone="danger"
        busy={revoke.isPending}
        onConfirm={async () => {
          if (!revoking) return
          try {
            await revoke.mutateAsync(revoking.sid)
            toast.success('Đã thu hồi phiên')
            setRevoking(null)
          } catch (error) {
            toast.error('Không thu hồi được', errorMessage(error))
          }
        }}
      />
    </>
  )
}

interface WebNotification {
  id: number
  title: string
  body: string
  category: string
  link: string
  createdAt: string
  read: boolean
}

/** Trung tâm thông báo, dùng chung hộp thư với chuông. */
export function NotificationsPage() {
  const queryClient = useQueryClient()
  const toast = useToast()
  const inbox = useQuery({
    queryKey: ['notify', 'page'],
    queryFn: () => api.get<{ unread: number; items: WebNotification[] }>('/notifications', { query: { limit: 200 } }),
  })
  const clearRead = useMutation({
    mutationFn: () => api.del<void>('/notifications/read'),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['notify'] })
      toast.success('Đã dọn thông báo đã đọc')
    },
  })
  const [tab, setTab] = useState('all')
  const items = (inbox.data?.items ?? []).filter((n) => tab === 'all' || (tab === 'unread' ? !n.read : n.read))

  return (
    <ModuleScreen
      tabs={[
        { id: 'all', label: 'Tất cả', count: inbox.data?.items.length },
        { id: 'unread', label: 'Chưa đọc', count: inbox.data?.unread },
        { id: 'read', label: 'Đã đọc' },
      ]}
      tab={tab}
      onTabChange={setTab}
      actions={
        <Button size="sm" onClick={() => clearRead.mutate()} loading={clearRead.isPending}>
          Dọn thông báo đã đọc
        </Button>
      }
      columns={[
        {
          key: 'title', priority: 1,
          header: 'Nội dung',
          cell: (row) => (
            <span className="flex flex-col">
              <span className={row.read ? 'text-ink-2' : 'font-medium text-ink'}>{row.title}</span>
              {row.body && <span className="text-xs text-ink-3">{row.body}</span>}
            </span>
          ),
        },
        { key: 'category', priority: 2, header: 'Nhóm', cell: (row) => row.category, sortValue: (row) => row.category },
        { key: 'createdAt', priority: 1, header: 'Thời điểm', cell: (row) => dateTime(row.createdAt), sortValue: (row) => row.createdAt },
        {
          key: 'state', priority: 1,
          header: 'Trạng thái',
          cell: (row) => (row.read ? <StatusBadge>Đã đọc</StatusBadge> : <StatusBadge tone="brand">Mới</StatusBadge>),
          sortValue: (row) => (row.read ? 1 : 0),
        },
      ]}
      rows={items}
      loading={inbox.isLoading}
      error={inbox.error}
      onRefresh={() => inbox.refetch()}
      emptyTitle="Hộp thư trống"
      defaultSort={{ key: 'createdAt', dir: 'desc' }}
    />
  )
}

/* ============================================================================
   Hồ sơ & bảo mật của chính mình
   ========================================================================== */

/** Thu nhỏ ảnh về cạnh dài 512 điểm rồi mã hoá thành data URL — máy chủ chặn ảnh quá lớn. */
async function shrinkImage(file: File): Promise<string> {
  const bitmap = await createImageBitmap(file)
  const scale = Math.min(1, 512 / Math.max(bitmap.width, bitmap.height))
  const canvas = document.createElement('canvas')
  canvas.width = Math.round(bitmap.width * scale)
  canvas.height = Math.round(bitmap.height * scale)
  canvas.getContext('2d')?.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
  bitmap.close()
  return canvas.toDataURL('image/jpeg', 0.85)
}

export function ProfilePage() {
  const auth = useAuth()
  const toast = useToast()
  const handheld = useIsHandheld()
  const queryClient = useQueryClient()
  const { profile, user } = auth
  const avatarInput = useRef<HTMLInputElement>(null)

  const allowed = NAV.map((group) => ({ group, routes: visibleRoutes(group.routes, auth, handheld) })).filter(
    (entry) => entry.routes.length > 0,
  )

  const [fullName, setFullName] = useState(user?.fullName ?? '')
  const [email, setEmail] = useState(user?.email ?? '')
  useEffect(() => {
    setFullName(user?.fullName ?? '')
    setEmail(user?.email ?? '')
  }, [user?.fullName, user?.email])

  const settings = useQuery({
    queryKey: ['presence', 'account-settings'],
    queryFn: () => api.get<{ webLoginEnabled: boolean }>('/auth/account-settings'),
  })

  const saveProfile = useMutation({
    mutationFn: (body: { fullName: string; email: string }) => api.put<void>('/auth/profile', body),
    onSuccess: () => auth.refreshUser(),
  })
  const saveAvatar = useMutation({
    mutationFn: (imageDataUrl: string) => api.put<void>('/auth/avatar', { imageDataUrl }),
    onSuccess: () => auth.refreshUser(),
  })
  const clearAvatar = useMutation({
    mutationFn: () => api.del<void>('/auth/avatar'),
    onSuccess: () => auth.refreshUser(),
  })
  const setWebLogin = useMutation({
    mutationFn: (webLoginEnabled: boolean) => api.put<void>('/auth/account-settings', { webLoginEnabled }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['presence', 'account-settings'] }),
  })

  const [changing, setChanging] = useState(false)
  const [disableWeb, setDisableWeb] = useState(false)
  const dirty = fullName !== (user?.fullName ?? '') || email !== (user?.email ?? '')

  return (
    <Stack>
      <PageHeader title="Hồ sơ và bảo mật" crumbs={[{ label: 'Hệ thống' }, { label: 'Hồ sơ' }]} />
      <div className="grid gap-3 lg:grid-cols-2">
        <Panel
          title="Thông tin tài khoản"
          actions={
            <Button
              size="sm"
              variant="primary"
              disabled={!dirty || !fullName.trim()}
              loading={saveProfile.isPending}
              onClick={async () => {
                try {
                  await saveProfile.mutateAsync({ fullName: fullName.trim(), email: email.trim() })
                  toast.success('Đã lưu hồ sơ')
                } catch (e) {
                  toast.error('Không lưu được hồ sơ', errorMessage(e))
                }
              }}
            >
              Lưu
            </Button>
          }
          padded
        >
          <div className="flex flex-col gap-3">
            <KeyValue
              rows={[
                ['Tên đăng nhập', profile?.username],
                ['Vai trò', profile?.roleLabels.join(', ') || null],
                ['Phạm vi dữ liệu', SCOPE_LABELS[profile?.scope ?? ''] ?? null],
              ]}
            />
            <FormGrid cols={2}>
              <Field label="Tên hiển thị" required>
                <Input value={fullName} onChange={(e) => setFullName(e.target.value)} />
              </Field>
              <Field label="Thư điện tử">
                <Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
              </Field>
            </FormGrid>
          </div>
        </Panel>

        <Panel title="Phần việc bạn được vào" padded>
          <ul className="flex flex-col gap-2 text-sm">
            {allowed.length === 0 && <li className="text-ink-3">Chưa được cấp phần việc nào.</li>}
            {allowed.map(({ group, routes }) => (
              <li key={group.id}>
                <span className="font-medium text-ink">{group.label}</span>
                <span className="mt-0.5 block text-xs text-ink-3">{routes.map((route) => route.label).join(', ')}</span>
              </li>
            ))}
          </ul>
        </Panel>
      </div>

      <Panel title="Bảo mật" padded>
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap gap-2">
            <input
              ref={avatarInput}
              type="file"
              accept="image/*"
              className="hidden"
              onChange={async (e) => {
                const file = e.target.files?.[0]
                e.target.value = ''
                if (!file) return
                try {
                  await saveAvatar.mutateAsync(await shrinkImage(file))
                  toast.success('Đã đổi ảnh đại diện')
                } catch (err) {
                  toast.error('Không đổi được ảnh', errorMessage(err))
                }
              }}
            />
            <Button size="sm" onClick={() => setChanging(true)}>
              Đổi mật khẩu
            </Button>
            <Button size="sm" loading={saveAvatar.isPending} onClick={() => avatarInput.current?.click()}>
              Đổi ảnh đại diện
            </Button>
            {user?.avatarUrl && (
              <Button
                size="sm"
                variant="ghost"
                loading={clearAvatar.isPending}
                onClick={async () => {
                  try {
                    await clearAvatar.mutateAsync()
                    toast.success('Đã xoá ảnh đại diện')
                  } catch (e) {
                    toast.error('Không xoá được ảnh', errorMessage(e))
                  }
                }}
              >
                Xoá ảnh đại diện
              </Button>
            )}
            <Button
              size="sm"
              variant={settings.data?.webLoginEnabled ? 'danger' : 'default'}
              loading={setWebLogin.isPending}
              disabled={settings.isLoading}
              onClick={async () => {
                if (settings.data?.webLoginEnabled) {
                  setDisableWeb(true)
                  return
                }
                try {
                  await setWebLogin.mutateAsync(true)
                  toast.success('Đã bật đăng nhập web')
                } catch (e) {
                  toast.error('Không đổi được cài đặt', errorMessage(e))
                }
              }}
            >
              {settings.data?.webLoginEnabled ? 'Tắt đăng nhập web cho tài khoản này' : 'Bật đăng nhập web cho tài khoản này'}
            </Button>
          </div>
          {settings.data && !settings.data.webLoginEnabled && (
            <InlineAlert tone="warn" title="Tài khoản này đang tắt đăng nhập web">
              Bạn vẫn dùng được ứng dụng trên điện thoại. Bật lại bằng nút ở trên.
            </InlineAlert>
          )}
        </div>
      </Panel>

      {changing && <ChangePasswordModal onClose={() => setChanging(false)} />}

      <ConfirmDialog
        open={disableWeb}
        onClose={() => setDisableWeb(false)}
        title="Tắt đăng nhập web"
        message="Bạn sẽ bị đăng xuất khỏi trình duyệt và chỉ đăng nhập lại được từ ứng dụng."
        confirmLabel="Tắt đăng nhập web"
        tone="danger"
        busy={setWebLogin.isPending}
        onConfirm={async () => {
          try {
            await setWebLogin.mutateAsync(false)
            setDisableWeb(false)
            toast.success('Đã tắt đăng nhập web')
          } catch (e) {
            toast.error('Không đổi được cài đặt', errorMessage(e))
          }
        }}
      />
    </Stack>
  )
}

function ChangePasswordModal({ onClose }: { onClose: () => void }) {
  const toast = useToast()
  const change = useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) => api.post<void>('/auth/change-password', body),
  })
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [again, setAgain] = useState('')
  const [error, setError] = useState<string | null>(null)

  const mismatch = again.length > 0 && next !== again
  const valid = current.length > 0 && next.length >= 6 && !mismatch

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      size="sm"
      title="Đổi mật khẩu"
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={change.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            disabled={!valid}
            loading={change.isPending}
            onClick={async () => {
              setError(null)
              try {
                await change.mutateAsync({ currentPassword: current, newPassword: next })
                toast.success('Đã đổi mật khẩu')
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không đổi được mật khẩu.'))
              }
            }}
          >
            Đổi mật khẩu
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <Field label="Mật khẩu hiện tại" required>
          <Input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} autoComplete="current-password" autoFocus />
        </Field>
        <Field label="Mật khẩu mới" required hint="Tối thiểu 6 ký tự">
          <Input type="password" value={next} onChange={(e) => setNext(e.target.value)} autoComplete="new-password" />
        </Field>
        <Field label="Nhập lại mật khẩu mới" required error={mismatch ? 'Hai lần nhập chưa khớp' : null}>
          <Input type="password" value={again} onChange={(e) => setAgain(e.target.value)} autoComplete="new-password" />
        </Field>
      </div>
    </Modal>
  )
}
