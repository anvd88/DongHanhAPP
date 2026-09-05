import { useState } from 'react'
import { date, qty, todayISO, vnd } from '@/lib/format'
import { useCustomers } from '@/api/sales'
import { useCreateReturn, useReturnSources, type ReturnSourceLine } from '@/api/returns'
import {
  Button,
  Combobox,
  DataTable,
  DatePicker,
  DocumentSummary,
  Field,
  FormGrid,
  InlineAlert,
  Input,
  Modal,
  Money,
  NumberInput,
  Panel,
  useToast,
} from '@/ui'
import { errorMessage } from '../_shared'

export function ReturnComposer({ onClose }: { onClose: () => void }) {
  const toast = useToast()
  const customers = useCustomers()
  const create = useCreateReturn()
  const [customerId, setCustomerId] = useState('')
  const [search, setSearch] = useState('')
  const [docDate, setDocDate] = useState(todayISO())
  const [reason, setReason] = useState('')
  const [note, setNote] = useState('')
  const [picked, setPicked] = useState<Record<string, number | null>>({})
  const [error, setError] = useState<string | null>(null)

  const sources = useReturnSources({ customerId: customerId || undefined, q: search || undefined }, !!customerId)
  const items = sources.data?.items ?? []
  const keyOf = (line: ReturnSourceLine) => `${line.documentId}:${line.lineNo}`

  const chosen = items.filter((line) => (picked[keyOf(line)] ?? 0) > 0)
  const total = chosen.reduce((s, line) => s + (picked[keyOf(line)] ?? 0) * line.unitPrice, 0)
  const overs = chosen.filter((line) => (picked[keyOf(line)] ?? 0) > line.remaining)
  const valid = chosen.length > 0 && overs.length === 0 && reason.trim().length > 0

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      size="lg"
      title="Nhận hàng khách trả về"
      description="Chọn khách rồi nhập số cân thực nhận trên từng dòng đã bán. Đơn giá lấy theo đơn nguồn."
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
              try {
                const result = await create.mutateAsync({
                  date: docDate,
                  reason: reason.trim(),
                  note: note.trim(),
                  lines: chosen.map((line) => ({
                    sourceDocumentId: line.documentId,
                    sourceLineNo: line.lineNo,
                    quantity: picked[keyOf(line)] ?? 0,
                  })),
                })
                const parts: string[] = []
                if (result.returnNo) parts.push(`phiếu trả ${result.returnNo}`)
                if (result.adjustedLines > 0) parts.push(`${result.adjustedLines} dòng hạ thẳng trên phiếu chưa chốt`)
                toast.success(`Đã ghi nhận hàng trả: ${parts.join(', ')}`)
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không ghi được hàng trả về.'))
              }
            }}
          >
            Ghi nhận hàng trả
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <FormGrid cols={3}>
          <Field label="Khách hàng" required>
            <Combobox
              value={customerId}
              onChange={(value) => {
                setCustomerId(value)
                setPicked({})
              }}
              loading={customers.isLoading}
              placeholder="Chọn khách hàng"
              options={(customers.data ?? []).map((c) => ({ value: c.id, label: c.name, description: c.phone }))}
            />
          </Field>
          <Field label="Ngày nhận về" required>
            <DatePicker value={docDate} onChange={setDocDate} />
          </Field>
          <Field label="Tìm trong hàng đã bán">
            <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Tên hàng, quy cách, số phiếu" />
          </Field>
        </FormGrid>

        {!customerId ? (
          <InlineAlert tone="info">Chọn khách hàng để xem những dòng hàng đã bán và còn trả lại được.</InlineAlert>
        ) : (
          <Panel title="Hàng đã bán cho khách này" meta={sources.isLoading ? 'Đang tra…' : `${items.length} dòng`}>
            <DataTable
              columns={[
                {
                  key: 'source',
                  priority: 1,
                  header: 'Đơn nguồn',
                  width: '9rem',
                  cell: (row) => (
                    <span className="flex flex-col">
                      <span className="tnum">{row.voucherNo}</span>
                      <span className="text-xs text-ink-3">{date(row.docDate)}</span>
                    </span>
                  ),
                },
                { key: 'name', priority: 1, header: 'Tên hàng', cell: (row) => row.content, truncate: true },
                { key: 'spec', priority: 2, header: 'Quy cách', cell: (row) => row.spec },
                { key: 'sold', priority: 2, header: 'Đã bán', align: 'right', cell: (row) => qty(row.quantity) },
                { key: 'remaining', priority: 1, header: 'Còn trả được', align: 'right', cell: (row) => qty(row.remaining) },
                { key: 'price', priority: 1, header: 'Đơn giá', align: 'right', cell: (row) => <Money value={row.unitPrice} /> },
                {
                  key: 'route',
                  priority: 2,
                  header: 'Cách ghi sổ',
                  width: '11rem',
                  cell: (row) => (row.settled ? 'Sinh phiếu trả riêng' : 'Hạ thẳng trên phiếu'),
                },
                {
                  key: 'pick',
                  priority: 1,
                  header: 'Số cân thực nhận',
                  align: 'right',
                  width: '9rem',
                  locked: true,
                  cell: (row) => (
                    <NumberInput
                      size="sm"
                      decimals={2}
                      value={picked[keyOf(row)] ?? null}
                      onChange={(v) => setPicked((c) => ({ ...c, [keyOf(row)]: v }))}
                      aria-label={`Số cân trả của ${row.content}`}
                    />
                  ),
                },
              ]}
              rows={items}
              getKey={(row) => keyOf(row)}
              loading={sources.isLoading}
              error={sources.error ? errorMessage(sources.error) : undefined}
              density="compact"
              emptyTitle="Khách này không còn dòng hàng nào trả lại được"
            />
          </Panel>
        )}

        {overs.length > 0 && (
          <InlineAlert tone="danger" title="Có dòng trả vượt số đã bán">
            {overs.map((line) => `${line.content} (còn ${qty(line.remaining)})`).join(', ')}
          </InlineAlert>
        )}

        <FormGrid cols={2}>
          <Field label="Lý do khách trả" required>
            <Input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Ví dụ: hàng không đúng quy cách" />
          </Field>
          <Field label="Ghi chú">
            <Input value={note} onChange={(e) => setNote(e.target.value)} />
          </Field>
        </FormGrid>

        <DocumentSummary
          rows={[
            { label: 'Số dòng trả', value: String(chosen.length) },
            { label: 'Giá trị hàng trả', value: vnd(total), strong: true },
          ]}
        />
      </div>
    </Modal>
  )
}
