import {
  Fragment,
  useEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
} from 'react'
import {
  ArrowDown,
  ArrowUp,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  Settings2,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import { containerTier, useContainerWidth } from '@/lib/device'
import { readPref, writePref } from '@/lib/prefs'
import { EmptyState, ErrorNote, Skeleton } from './data'
import { AnchoredLayer } from './Layer'
import { Button, IconButton } from './Button'
import { Checkbox } from './form'

export type Align = 'left' | 'right' | 'center'
export type SortDir = 'asc' | 'desc'
export interface SortState {
  key: string
  dir: SortDir
}
export type Density = 'normal' | 'compact'

export interface Column<T> {
  key: string
  header: ReactNode
  /** Nhãn trong menu ẩn/hiện cột khi header không phải chữ. */
  label?: string
  /** Cột số canh phải để hàng đơn vị thẳng cột. */
  align?: Align
  width?: string
  cell: (row: T, index: number) => ReactNode
  /** Khai báo thì cột sắp xếp được. */
  sortValue?: (row: T) => string | number | null | undefined
  /** Nội dung ô trên dòng tổng. Dòng tổng chỉ hiện khi có ít nhất một cột khai báo. */
  total?: ReactNode
  /** Ẩn mặc định, người dùng bật lại trong menu cột. */
  hidden?: boolean
  /** Không cho ẩn. */
  locked?: boolean
  className?: string
  truncate?: boolean
  /**
   * Thứ tự nhường chỗ khi khung chứa bảng hẹp lại. Không khai thì coi như 2.
   *
   *   1  luôn ở lại: mã chứng từ, tên đối tượng, số tiền, trạng thái
   *   2  rời bảng khi khung hẹp hơn khổ laptop
   *   3  rời bảng khi khung hẹp hơn khổ màn hình rời
   *
   * Cột rời bảng không mất dữ liệu: nó chuyển xuống dòng chi tiết mở ra khi bấm mũi tên đầu dòng.
   */
  priority?: 1 | 2 | 3
}

export function sortRows<T>(rows: T[], columns: Column<T>[], sort: SortState | null | undefined): T[] {
  if (!Array.isArray(rows)) return []
  if (!sort) return rows
  const column = columns.find((c) => c.key === sort.key)
  if (!column?.sortValue) return rows
  const get = column.sortValue
  const dir = sort.dir === 'asc' ? 1 : -1
  return [...rows].sort((a, b) => {
    const x = get(a)
    const y = get(b)
    if (x == null && y == null) return 0
    if (x == null) return 1
    if (y == null) return -1
    if (typeof x === 'number' && typeof y === 'number') return (x - y) * dir
    return String(x).localeCompare(String(y), 'vi', { numeric: true, sensitivity: 'base' }) * dir
  })
}

const DENSITY_KEY = 'km.table.density'

/**
 * Trạng thái của một bảng phía máy trạm: sắp xếp, phân trang, chọn dòng, mật độ, cột ẩn.
 * Sắp xếp và phân trang chạy trên toàn bộ danh sách rồi mới cắt trang.
 */
export function useDataTable<T>(
  rows: T[],
  columns: Column<T>[],
  options: { pageSize?: number; defaultSort?: SortState } = {},
) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(options.pageSize ?? 25)
  const [sort, setSort] = useState<SortState | null>(options.defaultSort ?? null)
  const [selected, setSelected] = useState<ReadonlySet<string | number>>(() => new Set())
  const [density, setDensityState] = useState<Density>(() => readPref<Density>(DENSITY_KEY, 'normal'))
  const [hidden, setHidden] = useState<ReadonlySet<string>>(
    () => new Set(columns.filter((c) => c.hidden).map((c) => c.key)),
  )

  // Máy chủ trả về thứ không phải mảng thì bảng phải trống, không được làm trắng màn hình.
  const sorted = useMemo(() => sortRows(rows, columns, sort), [rows, columns, sort])
  const total = sorted.length
  const lastPage = Math.max(1, Math.ceil(total / pageSize))
  const safePage = Math.min(page, lastPage)
  const paged = useMemo(
    () => sorted.slice((safePage - 1) * pageSize, safePage * pageSize),
    [sorted, safePage, pageSize],
  )

  useEffect(() => {
    setPage(1)
  }, [total, sort, pageSize])

  const visibleColumns = useMemo(() => columns.filter((c) => !hidden.has(c.key)), [columns, hidden])

  return {
    rows: paged,
    allRows: sorted,
    total,
    page: safePage,
    setPage,
    pageSize,
    setPageSize,
    sort,
    setSort,
    selected,
    setSelected,
    clearSelection: () => setSelected(new Set()),
    density,
    setDensity: (next: Density) => {
      setDensityState(next)
      writePref(DENSITY_KEY, next)
    },
    hidden,
    toggleColumn: (key: string) =>
      setHidden((prev) => {
        const next = new Set(prev)
        if (next.has(key)) next.delete(key)
        else next.add(key)
        return next
      }),
    visibleColumns,
  }
}

export type DataTableState<T> = ReturnType<typeof useDataTable<T>>

/** Nút mở menu cột và mật độ, đặt trên thanh công cụ của bảng. */
export function TableSettings<T>({
  columns,
  hidden,
  onToggleColumn,
  density,
  onDensityChange,
}: {
  columns: Column<T>[]
  hidden: ReadonlySet<string>
  onToggleColumn: (key: string) => void
  density: Density
  onDensityChange: (density: Density) => void
}) {
  const anchor = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)
  return (
    <>
      <IconButton
        ref={anchor}
        label="Cột và mật độ hiển thị"
        size="sm"
        icon={<Settings2 className="size-3.5" strokeWidth={1.7} />}
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
      />
      <AnchoredLayer anchorRef={anchor} open={open} onClose={() => setOpen(false)} align="end" label="Tuỳ chọn bảng">
        <div className="w-56 p-2">
          <p className="px-1 pb-1 text-2xs font-semibold text-ink-3">Mật độ</p>
          <div className="mb-2 grid grid-cols-2 gap-1">
            {(['normal', 'compact'] as Density[]).map((d) => (
              <button
                key={d}
                type="button"
                onClick={() => onDensityChange(d)}
                className={cn(
                  'h-7 rounded-sm border text-xs',
                  density === d
                    ? 'border-brand bg-brand-wash font-medium text-brand-ink'
                    : 'border-line text-ink-2 hover:bg-panel-2',
                )}
              >
                {d === 'normal' ? 'Thường' : 'Gọn'}
              </button>
            ))}
          </div>
          <p className="px-1 pb-1 text-2xs font-semibold text-ink-3">Cột hiển thị</p>
          <ul className="max-h-64 overflow-y-auto">
            {columns.map((column) => {
              const text = column.label ?? (typeof column.header === 'string' ? column.header : column.key)
              if (!text) return null
              return (
                <li key={column.key} className="px-1 py-0.5">
                  <Checkbox
                    label={<span className="text-xs">{text}</span>}
                    checked={!hidden.has(column.key)}
                    disabled={column.locked}
                    onChange={() => onToggleColumn(column.key)}
                  />
                </li>
              )
            })}
          </ul>
        </div>
      </AnchoredLayer>
    </>
  )
}

const PAGE_SIZES = [25, 50, 100, 200]

/**
 * Bảng dữ liệu dùng chung cho mọi danh sách.
 *
 *   · Chỉ kẻ ngang, đầu bảng dính, số canh phải.
 *   · Bấm tiêu đề để sắp xếp (tăng → giảm → bỏ).
 *   · Tích chọn nhiều dòng; thanh hành động hàng loạt do màn hình cha vẽ.
 *   · Bàn phím: mũi tên lên/xuống chuyển dòng, Enter mở, Space chọn.
 *   · Dòng tổng nằm trong bảng, chân bảng đếm bản ghi và phân trang.
 */
export function DataTable<T>({
  columns,
  rows,
  getKey,
  loading,
  error,
  onRetry,
  emptyTitle = 'Chưa có dữ liệu',
  emptyDescription,
  emptyAction,
  onRowClick,
  activeKey,
  selectable,
  selectedKeys,
  onSelectionChange,
  page = 1,
  pageSize = 25,
  total,
  onPageChange,
  onPageSizeChange,
  sort,
  onSortChange,
  density = 'normal',
  rowClassName,
  maxHeight,
  minWidth,
  footerExtra,
  className,
}: {
  columns: Column<T>[]
  rows: T[]
  getKey: (row: T, index: number) => string | number
  loading?: boolean
  error?: string | null
  onRetry?: () => void
  emptyTitle?: string
  emptyDescription?: ReactNode
  emptyAction?: ReactNode
  onRowClick?: (row: T) => void
  /** Khoá của dòng đang mở chi tiết, được tô nền nhạt. */
  activeKey?: string | number | null
  selectable?: boolean
  selectedKeys?: ReadonlySet<string | number>
  onSelectionChange?: (keys: Set<string | number>) => void
  page?: number
  pageSize?: number
  /** Truyền tổng số bản ghi thì chân bảng được hiển thị. */
  total?: number
  onPageChange?: (page: number) => void
  onPageSizeChange?: (size: number) => void
  sort?: SortState | null
  onSortChange?: (sort: SortState | null) => void
  density?: Density
  rowClassName?: (row: T) => string | undefined
  /** Giới hạn chiều cao để bảng tự cuộn bên trong, đầu bảng vẫn dính. */
  maxHeight?: string
  minWidth?: string
  footerExtra?: ReactNode
  className?: string
}) {
  const align = (a: Align | undefined) =>
    a === 'right' ? 'text-right' : a === 'center' ? 'text-center' : 'text-left'

  // Bề rộng của chính khung bảng, không phải của cửa sổ: cùng cửa sổ 1024px, có menu trái thì
  // bảng chỉ còn 766px.
  const [wrapRef, wrapWidth] = useContainerWidth()
  const tier = wrapWidth ? containerTier(wrapWidth) : 'wide'
  const cardMode = tier === 'narrow'
  const maxPriority = tier === 'wide' ? 3 : 2

  const inTable = useMemo(
    () => (cardMode ? columns : columns.filter((column) => (column.priority ?? 2) <= maxPriority)),
    [columns, cardMode, maxPriority],
  )
  // Cột phải nhường chỗ. Trong bảng chúng xuống dòng chi tiết, trong thẻ chúng nằm sau nút mở rộng.
  const spilled = useMemo(
    () =>
      cardMode
        ? columns.filter((column) => (column.priority ?? 2) > 2)
        : columns.filter((column) => !inTable.includes(column)),
    [columns, inTable, cardMode],
  )
  const [expanded, setExpanded] = useState<ReadonlySet<string | number>>(() => new Set())
  useEffect(() => setExpanded(new Set()), [rows])
  const toggleExpanded = (key: string | number) =>
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const empty = !loading && !error && rows.length === 0
  const showTotalRow = !empty && !loading && !error && columns.some((column) => column.total !== undefined)
  // Cột đầu chỉ tự ghi "Tổng cộng" khi không cột nào đã mang nhãn chữ cho dòng tổng.
  const hasTotalLabel = columns.some((column) => typeof column.total === 'string')
  const selected = selectedKeys ?? new Set<string | number>()
  const keys = rows.map((row, index) => getKey(row, index))
  const allSelected = keys.length > 0 && keys.every((key) => selected.has(key))
  const someSelected = keys.some((key) => selected.has(key))

  const [focusIndex, setFocusIndex] = useState(-1)
  const bodyRef = useRef<HTMLTableSectionElement>(null)
  useEffect(() => setFocusIndex(-1), [rows])

  const toggleAll = () =>
    onSelectionChange?.(allSelected ? new Set() : new Set<string | number>(keys))

  const toggleOne = (key: string | number) => {
    const next = new Set(selected)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    onSelectionChange?.(next)
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const target = event.target as HTMLElement
    if (target.closest('input, select, textarea, button, a, [contenteditable]')) return
    if (rows.length === 0) return
    let handled = true
    if (event.key === 'ArrowDown') setFocusIndex((i) => Math.min(i + 1, rows.length - 1))
    else if (event.key === 'ArrowUp') setFocusIndex((i) => Math.max(i - 1, 0))
    else if (event.key === 'Home') setFocusIndex(0)
    else if (event.key === 'End') setFocusIndex(rows.length - 1)
    else if (event.key === 'Enter' && focusIndex >= 0) onRowClick?.(rows[focusIndex])
    else if (event.key === ' ' && focusIndex >= 0 && selectable) toggleOne(keys[focusIndex])
    else handled = false
    if (handled) event.preventDefault()
  }

  useEffect(() => {
    if (focusIndex < 0) return
    const row = bodyRef.current?.children[focusIndex] as HTMLElement | undefined
    row?.scrollIntoView({ block: 'nearest' })
  }, [focusIndex])

  const cycleSort = (column: Column<T>) => {
    if (!column.sortValue || !onSortChange) return
    if (!sort || sort.key !== column.key) onSortChange({ key: column.key, dir: 'asc' })
    else if (sort.dir === 'asc') onSortChange({ key: column.key, dir: 'desc' })
    else onSortChange(null)
  }

  const lastPage = total !== undefined ? Math.max(1, Math.ceil(total / pageSize)) : 1
  const firstIndex = total ? (page - 1) * pageSize + 1 : 0
  const lastIndex = total ? Math.min(page * pageSize, total) : 0

  const labelOf = (column: Column<T>) =>
    column.label ?? (typeof column.header === 'string' ? column.header : column.key)

  const bodyColSpan = inTable.length + (selectable ? 1 : 0) + (spilled.length > 0 ? 1 : 0)

  return (
    <div ref={wrapRef} className={cn('flex min-w-0 flex-col', className)}>
      {cardMode ? (
        <CardList
          columns={columns}
          spilled={spilled}
          rows={rows}
          keys={keys}
          labelOf={labelOf}
          loading={loading}
          onRowClick={onRowClick}
          activeKey={activeKey}
          selectable={selectable}
          selected={selected}
          allSelected={allSelected}
          someSelected={someSelected}
          onToggleAll={toggleAll}
          onToggleOne={toggleOne}
          expanded={expanded}
          onToggleExpanded={toggleExpanded}
          sort={sort}
          onSortChange={onSortChange}
          rowClassName={rowClassName}
          maxHeight={maxHeight}
        />
      ) : (
      <div
        className="overflow-auto outline-none focus-visible:shadow-[inset_0_0_0_2px_var(--brand-ring)]"
        style={maxHeight ? { maxHeight } : undefined}
        tabIndex={0}
        onKeyDown={onKeyDown}
        onBlur={() => setFocusIndex(-1)}
      >
        <table className="data-grid" data-density={density} style={minWidth ? { minWidth } : undefined}>
          <thead>
            <tr>
              {selectable && (
                <th scope="col" className="w-8 !px-2.5">
                  <input
                    type="checkbox"
                    aria-label="Chọn tất cả dòng trên trang"
                    checked={allSelected}
                    ref={(el) => {
                      if (el) el.indeterminate = !allSelected && someSelected
                    }}
                    onChange={toggleAll}
                    className="checkbox"
                  />
                </th>
              )}
              {spilled.length > 0 && <th scope="col" className="w-7 !px-1" />}
              {inTable.map((column) => {
                const sortable = !!column.sortValue && !!onSortChange
                const active = sort?.key === column.key
                return (
                  <th
                    key={column.key}
                    scope="col"
                    style={column.width ? { width: column.width } : undefined}
                    aria-sort={active ? (sort.dir === 'asc' ? 'ascending' : 'descending') : sortable ? 'none' : undefined}
                    onClick={sortable ? () => cycleSort(column) : undefined}
                    className={cn(align(column.align), sortable && 'is-sortable', column.className)}
                  >
                    <span
                      className={cn(
                        'inline-flex items-center gap-1',
                        column.align === 'right' && 'flex-row-reverse',
                      )}
                    >
                      {column.header}
                      {active &&
                        (sort.dir === 'asc' ? (
                          <ArrowUp className="size-3 text-brand" strokeWidth={2} />
                        ) : (
                          <ArrowDown className="size-3 text-brand" strokeWidth={2} />
                        ))}
                    </span>
                  </th>
                )
              })}
            </tr>
          </thead>
          <tbody ref={bodyRef}>
            {loading &&
              Array.from({ length: 8 }, (_, row) => (
                <tr key={`skeleton-${row}`}>
                  {selectable && <td />}
                  {spilled.length > 0 && <td />}
                  {inTable.map((column) => (
                    <td key={column.key}>
                      <Skeleton className={cn('h-3', column.align === 'right' ? 'ml-auto w-20' : 'w-full max-w-40')} />
                    </td>
                  ))}
                </tr>
              ))}

            {!loading &&
              !error &&
              rows.map((row, index) => {
                const key = keys[index]
                const isSelected = selected.has(key)
                const isOpen = expanded.has(key)
                return (
                  <Fragment key={key}>
                    <tr
                      onClick={onRowClick ? () => onRowClick(row) : undefined}
                      className={cn(
                        isSelected && 'is-selected',
                        activeKey === key && !isSelected && 'is-selected',
                        focusIndex === index && 'is-focused',
                        onRowClick && 'is-clickable',
                        rowClassName?.(row),
                      )}
                    >
                      {selectable && (
                        <td className="!px-2.5" onClick={(event) => event.stopPropagation()}>
                          <input
                            type="checkbox"
                            aria-label="Chọn dòng"
                            checked={isSelected}
                            onChange={() => toggleOne(key)}
                            className="checkbox"
                          />
                        </td>
                      )}
                      {spilled.length > 0 && (
                        <td className="!px-1" onClick={(event) => event.stopPropagation()}>
                          <ExpandToggle open={isOpen} onClick={() => toggleExpanded(key)} />
                        </td>
                      )}
                      {inTable.map((column) => (
                        <td
                          key={column.key}
                          className={cn(align(column.align), column.truncate && 'is-truncate', column.className)}
                        >
                          {column.cell(row, index)}
                        </td>
                      ))}
                    </tr>
                    {isOpen && (
                      <tr className="is-detail">
                        <td colSpan={bodyColSpan}>
                          <SpilledPairs
                            columns={spilled}
                            row={row}
                            index={index}
                            labelOf={labelOf}
                          />
                        </td>
                      </tr>
                    )}
                  </Fragment>
                )
              })}

            {showTotalRow && (
              <tr className="is-total">
                {selectable && <td />}
                {spilled.length > 0 && <td />}
                {inTable.map((column, index) => (
                  <td key={column.key} className={cn(align(column.align), column.className)}>
                    {column.total ?? (index === 0 && !hasTotalLabel ? 'Tổng cộng' : null)}
                  </td>
                ))}
              </tr>
            )}
          </tbody>
        </table>
      </div>
      )}

      {error && (
        <div className="p-3">
          <ErrorNote
            message={error}
            action={
              onRetry && (
                <Button size="sm" onClick={onRetry}>
                  Thử lại
                </Button>
              )
            }
          />
        </div>
      )}

      {/* Trạng thái rỗng đặt ngoài khung cuộn ngang để luôn nằm giữa vùng nhìn thấy. */}
      {empty && <EmptyState title={emptyTitle} description={emptyDescription} action={emptyAction} compact />}

      {total !== undefined && (
        <div className="tap-row flex flex-wrap items-center gap-x-3 gap-y-2 border-t border-line px-3 py-2 text-xs text-ink-2">
          <span className="tnum">
            {total === 0 ? 'Không có bản ghi' : `${firstIndex}-${lastIndex} trong ${total} bản ghi`}
          </span>
          {footerExtra}
          <span className="ml-auto flex items-center gap-1.5">
            <label className="flex items-center gap-1.5">
              <span className="hidden text-ink-3 sm:inline">Dòng/trang</span>
              <select
                value={pageSize}
                onChange={(event) => onPageSizeChange?.(Number(event.target.value))}
                className="control control-sm w-auto"
              >
                {PAGE_SIZES.map((size) => (
                  <option key={size} value={size}>
                    {size}
                  </option>
                ))}
              </select>
            </label>
            <span className="tnum mx-1 text-ink-3">
              Trang {page}/{lastPage}
            </span>
            <PageButton label="Trang đầu" disabled={page <= 1} onClick={() => onPageChange?.(1)}>
              <ChevronsLeft className="size-3.5" strokeWidth={1.8} />
            </PageButton>
            <PageButton label="Trang trước" disabled={page <= 1} onClick={() => onPageChange?.(page - 1)}>
              <ChevronLeft className="size-3.5" strokeWidth={1.8} />
            </PageButton>
            <PageButton label="Trang sau" disabled={page >= lastPage} onClick={() => onPageChange?.(page + 1)}>
              <ChevronRight className="size-3.5" strokeWidth={1.8} />
            </PageButton>
            <PageButton label="Trang cuối" disabled={page >= lastPage} onClick={() => onPageChange?.(lastPage)}>
              <ChevronsRight className="size-3.5" strokeWidth={1.8} />
            </PageButton>
          </span>
        </div>
      )}
    </div>
  )
}

/** Mũi tên mở dòng chi tiết chứa các cột đã nhường chỗ. */
function ExpandToggle({ open, onClick }: { open: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-expanded={open}
      aria-label={open ? 'Thu gọn dòng' : 'Xem thêm cột'}
      className="grid size-6 place-items-center rounded-sm text-ink-3 hover:bg-panel-2 hover:text-ink"
    >
      <ChevronDown className={cn('size-3.5 transition-transform', open && 'rotate-180')} strokeWidth={1.8} />
    </button>
  )
}

/** Các cột đã rời bảng, bày lại thành cặp nhãn và giá trị. */
function SpilledPairs<T>({
  columns,
  row,
  index,
  labelOf,
}: {
  columns: Column<T>[]
  row: T
  index: number
  labelOf: (column: Column<T>) => string
}) {
  return (
    <dl className="grid gap-x-4 gap-y-1 sm:grid-cols-2 lg:grid-cols-3">
      {columns.map((column) => (
        // Ô không có giá trị thì giấu cả nhãn: một chữ "Chi" đứng trơ không nói lên điều gì.
        <div key={column.key} className="flex min-w-0 items-baseline gap-2 has-[dd:empty]:hidden">
          <dt className="shrink-0 text-2xs text-ink-3">{labelOf(column)}</dt>
          {/* Dòng chi tiết tồn tại để đọc hết nội dung, nên ở đây chữ xuống dòng chứ không cắt. */}
          <dd className="m-0 min-w-0 flex-1 break-words text-xs text-ink">{column.cell(row, index)}</dd>
        </div>
      ))}
    </dl>
  )
}

/**
 * Bảng dưới dạng danh sách thẻ, dùng khi khung chứa hẹp hơn 640px.
 *
 * Thẻ dựng từ chính khai báo cột chứ không phải mã riêng của từng màn hình: cột mức 1 đầu tiên
 * làm tiêu đề, cột số canh phải đầu tiên làm con số nổi, phần còn lại xếp thành cặp nhãn và giá trị.
 */
function CardList<T>({
  columns,
  spilled,
  rows,
  keys,
  labelOf,
  loading,
  onRowClick,
  activeKey,
  selectable,
  selected,
  allSelected,
  someSelected,
  onToggleAll,
  onToggleOne,
  expanded,
  onToggleExpanded,
  sort,
  onSortChange,
  rowClassName,
  maxHeight,
}: {
  columns: Column<T>[]
  spilled: Column<T>[]
  rows: T[]
  keys: (string | number)[]
  labelOf: (column: Column<T>) => string
  loading?: boolean
  onRowClick?: (row: T) => void
  activeKey?: string | number | null
  selectable?: boolean
  selected: ReadonlySet<string | number>
  allSelected: boolean
  someSelected: boolean
  onToggleAll: () => void
  onToggleOne: (key: string | number) => void
  expanded: ReadonlySet<string | number>
  onToggleExpanded: (key: string | number) => void
  sort?: SortState | null
  onSortChange?: (sort: SortState | null) => void
  rowClassName?: (row: T) => string | undefined
  maxHeight?: string
}) {
  const rest = useMemo(() => columns.filter((column) => !spilled.includes(column)), [columns, spilled])
  const title = rest.find((column) => column.priority === 1) ?? rest[0]
  // Con số nổi lấy cột canh phải CUỐI cùng, không phải cột đầu: trên một dòng chứng từ đó là
  // Thành tiền chứ không phải Số lượng, trên bảng lương là Thực nhận chứ không phải Giờ tăng ca.
  const money = rest.filter((column) => column.align === 'right' && column !== title)
  const amount = money.filter((column) => column.priority === 1).at(-1) ?? money.at(-1)
  const meta = rest.filter((column) => column !== title && column !== amount)
  const sortable = columns.filter((column) => column.sortValue)
  // Dòng tổng của bảng thành một dải dưới danh sách. Cột khai `total` là chữ chỉ là nhãn, không phải số.
  const totals = columns.filter((column) => column.total !== undefined && typeof column.total !== 'string')

  return (
    <div className="flex min-w-0 flex-col">
      {(selectable || (sortable.length > 0 && onSortChange)) && (
        <div className="tap-row flex items-center gap-2 border-b border-line px-3 py-1.5">
          {selectable && (
            <label className="flex items-center gap-1.5 text-xs text-ink-2">
              <input
                type="checkbox"
                aria-label="Chọn tất cả dòng trên trang"
                checked={allSelected}
                ref={(el) => {
                  if (el) el.indeterminate = !allSelected && someSelected
                }}
                onChange={onToggleAll}
                className="checkbox"
              />
              Chọn tất cả
            </label>
          )}
          {sortable.length > 0 && onSortChange && (
            <label className="ml-auto flex items-center gap-1.5 text-xs text-ink-3">
              Sắp xếp
              <select
                value={sort ? `${sort.key}:${sort.dir}` : ''}
                onChange={(event) => {
                  const value = event.target.value
                  if (!value) return onSortChange(null)
                  const [key, dir] = value.split(':')
                  onSortChange({ key, dir: dir as SortDir })
                }}
                className="control control-sm w-auto"
              >
                <option value="">Mặc định</option>
                {sortable.map((column) => (
                  <Fragment key={column.key}>
                    <option value={`${column.key}:asc`}>{labelOf(column)} ↑</option>
                    <option value={`${column.key}:desc`}>{labelOf(column)} ↓</option>
                  </Fragment>
                ))}
              </select>
            </label>
          )}
        </div>
      )}

      <div className="overflow-y-auto" style={maxHeight ? { maxHeight } : undefined}>
        {loading &&
          Array.from({ length: 6 }, (_, index) => (
            <div key={`skeleton-${index}`} className="flex flex-col gap-2 border-b border-line px-3 py-3">
              <Skeleton className="h-3.5 w-32" />
              <Skeleton className="h-3 w-full max-w-56" />
            </div>
          ))}

        {!loading &&
          rows.map((row, index) => {
            const key = keys[index]
            const isSelected = selected.has(key)
            const isOpen = expanded.has(key)
            return (
              <article
                key={key}
                className={cn(
                  'flex flex-col gap-1.5 border-b border-line px-3 py-2.5',
                  (isSelected || activeKey === key) && 'bg-brand-wash',
                  rowClassName?.(row),
                )}
              >
                {/* Hàng đầu của thẻ là vùng chạm chính. Nút tiêu đề trải hết chiều cao hàng để
                    ngón tay không phải nhắm vào một dòng chữ cao 16px. */}
                <div className="flex items-center gap-1">
                  {selectable && (
                    <label className="-m-1 flex shrink-0 cursor-pointer items-center p-2.5">
                      <input
                        type="checkbox"
                        aria-label="Chọn dòng"
                        checked={isSelected}
                        onChange={() => onToggleOne(key)}
                        className="checkbox"
                      />
                    </label>
                  )}
                  <button
                    type="button"
                    onClick={onRowClick ? () => onRowClick(row) : undefined}
                    disabled={!onRowClick}
                    className="-my-1 min-w-0 flex-1 truncate py-2 text-left text-sm font-medium text-ink disabled:cursor-default"
                  >
                    {title ? title.cell(row, index) : null}
                  </button>
                  {amount && (
                    <span className="tnum shrink-0 text-sm font-semibold text-ink">
                      {amount.cell(row, index)}
                    </span>
                  )}
                </div>

                {meta.length > 0 && (
                  <dl className="flex flex-wrap gap-x-3 gap-y-1 pl-0.5">
                    {meta.map((column) => (
                      <div
                        key={column.key}
                        className="flex min-w-0 items-baseline gap-1.5 has-[dd:empty]:hidden"
                      >
                        <dt className="shrink-0 text-2xs text-ink-3">{labelOf(column)}</dt>
                        <dd className="m-0 min-w-0 truncate text-xs text-ink-2">{column.cell(row, index)}</dd>
                      </div>
                    ))}
                  </dl>
                )}

                {spilled.length > 0 && (
                  <div className="flex flex-col gap-1.5">
                    <button
                      type="button"
                      onClick={() => onToggleExpanded(key)}
                      aria-expanded={isOpen}
                      className="-my-1.5 flex items-center gap-1 self-start py-2 text-2xs text-ink-3 hover:text-ink"
                    >
                      <ChevronDown className={cn('size-3 transition-transform', isOpen && 'rotate-180')} strokeWidth={1.8} />
                      {isOpen ? 'Thu gọn' : `Xem thêm ${spilled.length} mục`}
                    </button>
                    {isOpen && (
                      <div className="rounded-sm bg-panel-2 px-2 py-1.5">
                        <SpilledPairs columns={spilled} row={row} index={index} labelOf={labelOf} />
                      </div>
                    )}
                  </div>
                )}
              </article>
            )
          })}
      </div>

      {!loading && rows.length > 0 && totals.length > 0 && (
        <dl className="flex flex-wrap items-baseline gap-x-4 gap-y-1 border-t border-line bg-panel-2 px-3 py-2">
          <dt className="text-xs font-semibold text-ink">Tổng cộng</dt>
          {totals.map((column) => (
            <div key={column.key} className="flex items-baseline gap-1.5">
              <dt className="text-2xs text-ink-3">{labelOf(column)}</dt>
              <dd className="tnum m-0 text-xs font-semibold text-ink">{column.total}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  )
}

function PageButton({
  label,
  disabled,
  onClick,
  children,
}: {
  label: string
  disabled?: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className="grid size-7 place-items-center rounded-sm border border-line bg-panel text-ink-2 hover:bg-panel-2 hover:text-ink disabled:opacity-40"
    >
      {children}
    </button>
  )
}

/** Thanh xuất hiện phía trên bảng khi có dòng được tích chọn. */
export function BulkBar({
  count,
  onClear,
  children,
}: {
  count: number
  onClear: () => void
  children: ReactNode
}) {
  if (count === 0) return null
  return (
    <div className="flex flex-wrap items-center gap-2 border-b border-line bg-brand-wash px-3 py-1.5 text-xs">
      <span className="tnum font-medium text-brand-ink">Đã chọn {count} dòng</span>
      <button type="button" onClick={onClear} className="text-ink-2 underline-offset-2 hover:underline">
        Bỏ chọn
      </button>
      <span className="ml-auto flex flex-wrap items-center gap-1.5">{children}</span>
    </div>
  )
}
