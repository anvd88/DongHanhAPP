import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Printer } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { EMPTY, date, dateTime, qty } from '@/lib/format'
import { documentStatus, useCancelSalesDocument, useSalesDocument, useSalesDocuments } from '@/api/sales'
import {
  Button,
  ConfirmDialog,
  DataTable,
  InlineAlert,
  KeyValue,
  Money,
  PageHeader,
  Panel,
  Split,
  Stack,
  StatusBadge,
  useToast,
} from '@/ui'
import { errorMessage } from '../_shared'

/** Chi tiết một phiếu bán hàng. */
export function SalesDocumentDetailPage() {
  const { id } = useParams()
  const auth = useAuth()
  const toast = useToast()
  const navigate = useNavigate()
  const detail = useSalesDocument(id)
  const list = useSalesDocuments()
  const cancel = useCancelSalesDocument()
  const [cancelling, setCancelling] = useState(false)

  const doc = detail.data
  const listRow = list.data?.find((d) => d.id === id)
  const status = listRow ? documentStatus(listRow) : doc ? documentStatus({ ...doc, deliveryTaskStatus: '', deliveryReturnedAt: null }) : null
  const total = doc?.lines?.reduce((sum, l) => sum + l.quantity * l.unitPrice, 0) ?? 0

  return (
    <Stack>
      <PageHeader
        crumbs={[{ label: 'Bán hàng & Kho' }, { label: 'Phiếu bán hàng', to: '/ban-hang' }, { label: doc?.voucherNo ?? '…' }]}
        title={doc ? `Phiếu ${doc.voucherNo}` : 'Phiếu bán hàng'}
        meta={status && <StatusBadge tone={status.tone}>{status.label}</StatusBadge>}
        actions={
          <>
            <Button size="sm" icon={<Printer className="size-3.5" strokeWidth={1.7} />} onClick={() => window.print()}>
              In
            </Button>
            {doc && !doc.cancelledAt && auth.can(PERM.vouchersCancel) && (
              <Button size="sm" variant="danger" onClick={() => setCancelling(true)}>
                Huỷ phiếu
              </Button>
            )}
          </>
        }
      />

      {detail.isError && (
        <InlineAlert tone="danger" title="Không tải được phiếu" action={<Button size="sm" onClick={() => detail.refetch()}>Thử lại</Button>}>
          {errorMessage(detail.error)}
        </InlineAlert>
      )}

      {doc?.cancelledAt && (
        <InlineAlert tone="danger" title={`Phiếu đã huỷ lúc ${dateTime(doc.cancelledAt)} bởi ${doc.cancelledBy || 'người dùng'}`}>
          {doc.cancelReason || 'Không ghi lý do.'}
        </InlineAlert>
      )}

      <Split
        asideWidth="22rem"
        main={
          <Panel title="Dòng hàng" meta={doc ? `${doc.lines?.length ?? 0} dòng` : undefined}>
            <DataTable
              columns={[
                { key: 'no', priority: 3, header: '#', width: '2.5rem', align: 'center', cell: (_, i) => <span className="text-ink-3">{i + 1}</span> },
                { key: 'name', priority: 1, header: 'Tên hàng', cell: (row) => row.lineContent, total: 'Tổng cộng' },
                { key: 'spec', priority: 2, header: 'Quy cách', cell: (row) => row.spec },
                { key: 'qty', priority: 1, header: 'Số lượng', align: 'right', cell: (row) => qty(row.quantity) },
                { key: 'price', priority: 2, header: 'Đơn giá', align: 'right', cell: (row) => <Money value={row.unitPrice} /> },
                { key: 'amount', priority: 1, header: 'Thành tiền', align: 'right', cell: (row) => <Money value={row.quantity * row.unitPrice} />, total: <Money value={total} zero="zero" /> },
                // Nguồn hàng chỉ có trên màn hình nội bộ này; phiếu in và PDF gửi khách không có cột đó.
                { key: 'source', priority: 2, header: 'Nguồn hàng', cell: (row) => row.supplierName || EMPTY, truncate: true },
                { key: 'note', priority: 3, header: 'Ghi chú', cell: (row) => row.note, truncate: true },
              ]}
              rows={doc?.lines ?? []}
              getKey={(_, i) => i}
              loading={detail.isLoading}
              emptyTitle="Phiếu này chưa có dòng hàng"
            />
          </Panel>
        }
        aside={
          <>
            <Panel title="Thông tin phiếu" padded>
              {detail.isLoading ? (
                <p className="text-sm text-ink-3">Đang tải</p>
              ) : (
                <KeyValue
                  rows={[
                    ['Khách hàng', doc?.customerName],
                    ['Ngày lập', doc ? date(doc.date) : null],
                    ['Diễn giải', doc?.content || null],
                    ['Ghi chú', doc?.note || null],
                    ['Người lập', listRow?.createdBy || null],
                    ['Tổng tiền', <Money key="t" value={total} zero="zero" strong />],
                  ]}
                />
              )}
            </Panel>
            <Panel title="Giao hàng" padded>
              {listRow && (listRow.deliveryDriverName || listRow.deliveryTaskStatus) ? (
                <KeyValue
                  rows={[
                    ['Lái xe', listRow.deliveryDriverName || null],
                    ['Chặng', status?.label ?? null],
                    ['Về kho lúc', listRow.deliveryReturnedAt ? dateTime(listRow.deliveryReturnedAt) : null],
                  ]}
                />
              ) : (
                <p className="text-sm text-ink-3">
                  Chưa gán lái xe.{' '}
                  <Link to="/giao-hang" className="link">
                    Mở màn giao hàng
                  </Link>
                </p>
              )}
            </Panel>
            <Panel title="Lịch sử" padded>
              <ol className="flex flex-col gap-2 text-sm">
                <li className="flex gap-3">
                  <span className="w-28 shrink-0 text-xs text-ink-3">{doc ? date(doc.date) : '…'}</span>
                  <span>Lập phiếu{listRow?.createdBy ? ` · ${listRow.createdBy}` : ''}</span>
                </li>
                {doc?.issuedAt && (
                  <li className="flex gap-3">
                    <span className="w-28 shrink-0 text-xs text-ink-3">{dateTime(doc.issuedAt)}</span>
                    <span>Phát hành</span>
                  </li>
                )}
                {listRow?.deliveryReturnedAt && (
                  <li className="flex gap-3">
                    <span className="w-28 shrink-0 text-xs text-ink-3">{dateTime(listRow.deliveryReturnedAt)}</span>
                    <span>Xác nhận về kho</span>
                  </li>
                )}
                {doc?.cancelledAt && (
                  <li className="flex gap-3">
                    <span className="w-28 shrink-0 text-xs text-ink-3">{dateTime(doc.cancelledAt)}</span>
                    <span className="text-danger">Huỷ phiếu · {doc.cancelledBy}</span>
                  </li>
                )}
              </ol>
              <p className="mt-3 text-xs">
                <Link to="/cong-no" className="link">
                  Xem công nợ khách hàng
                </Link>
              </p>
            </Panel>
          </>
        }
      />

      <ConfirmDialog
        open={cancelling}
        onClose={() => setCancelling(false)}
        title={`Huỷ phiếu ${doc?.voucherNo ?? ''}`}
        message="Phiếu đã huỷ vẫn nằm trong sổ với dấu đã huỷ và không cộng vào doanh thu, công nợ."
        confirmLabel="Huỷ phiếu"
        tone="danger"
        requireReason
        reasonLabel="Lý do huỷ"
        busy={cancel.isPending}
        onConfirm={async (reason) => {
          if (!id) return
          try {
            await cancel.mutateAsync({ id, reason })
            toast.success('Đã huỷ phiếu')
            setCancelling(false)
            navigate('/ban-hang')
          } catch (error) {
            toast.error('Không huỷ được phiếu', errorMessage(error))
          }
        }}
      />
    </Stack>
  )
}
