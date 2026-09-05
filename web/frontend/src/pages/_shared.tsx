import { useMemo, useState, type ReactNode } from 'react'
import { ChevronDown, RotateCw, SlidersHorizontal } from 'lucide-react'
import { ApiError } from '@/lib/http'
import { useContainerWidth } from '@/lib/device'
import { cn } from '@/lib/cn'
import {
  BulkBar,
  Button,
  DataTable,
  IconButton,
  Panel,
  Segmented,
  Split,
  Stack,
  TableSettings,
  Toolbar,
  ToolbarSpacer,
  useDataTable,
  type Column,
  type SortState,
  type TabItem,
} from '@/ui'

/** Thông điệp lỗi hiển thị cho người dùng từ một lỗi bất kỳ của lớp gọi API. */
export function errorMessage(error: unknown, fallback = 'Không tải được dữ liệu.') {
  if (error instanceof ApiError) return error.message || fallback
  if (error instanceof Error && error.message) return error.message
  return fallback
}

/**
 * Khung chuẩn của một màn hình sổ sách:
 *
 *   dải số liệu (tuỳ chọn, do màn hình vẽ bằng FigureStrip)
 *   thanh công cụ: lọc trạng thái, bộ lọc, [phải] tuỳ chọn bảng, nạp lại, hành động chính
 *   thanh hành động hàng loạt khi có dòng được chọn
 *   bảng dữ liệu có sắp xếp, dòng tổng và phân trang
 *   khối chi tiết (tuỳ chọn)
 *
 * Không có tiêu đề màn hình ở đây vì dải tab phía trên đã hiển thị màn hình đang mở.
 */
export function ModuleScreen<T>({
  figures,
  tabs,
  tab,
  onTabChange,
  filters,
  actions,
  columns,
  rows = [],
  getKey,
  loading,
  error,
  onRetry,
  emptyTitle,
  emptyDescription,
  emptyAction,
  selectable,
  bulkActions,
  onRowClick,
  activeKey,
  onRefresh,
  defaultSort,
  pageSize,
  aside,
  asideWidth,
  detail,
  children,
}: {
  figures?: ReactNode
  tabs?: TabItem[]
  tab?: string
  onTabChange?: (id: string) => void
  filters?: ReactNode
  actions?: ReactNode
  columns: Column<T>[]
  rows?: T[]
  getKey?: (row: T, index: number) => string | number
  loading?: boolean
  error?: unknown
  onRetry?: () => void
  emptyTitle?: string
  emptyDescription?: ReactNode
  emptyAction?: ReactNode
  selectable?: boolean
  /** Nút thao tác hiển thị khi có dòng được tích chọn. */
  bulkActions?: (selected: ReadonlySet<string | number>, clear: () => void) => ReactNode
  onRowClick?: (row: T) => void
  activeKey?: string | number | null
  onRefresh?: () => void
  defaultSort?: SortState
  pageSize?: number
  aside?: ReactNode
  asideWidth?: string
  detail?: ReactNode
  children?: ReactNode
}) {
  const [innerTab, setInnerTab] = useState(tabs?.[0]?.id ?? '')
  const activeTab = tab ?? innerTab
  const changeTab = onTabChange ?? setInnerTab

  const keyOf = useMemo(
    () => getKey ?? ((row: T, index: number) => (row as { id?: string | number }).id ?? index),
    [getKey],
  )

  const table = useDataTable(rows, columns, { pageSize, defaultSort })

  const bulk = bulkActions?.(table.selected, table.clearSelection)

  // Khung hẹp thì bộ lọc rời thanh công cụ, nếu không nó chiếm gần nửa màn hình điện thoại và
  // đẩy đầu bảng xuống dưới nếp gấp. Ngưỡng trùng với ngưỡng bảng chuyển sang thẻ.
  const [panelRef, panelWidth] = useContainerWidth()
  const compact = panelWidth > 0 && panelWidth < 640
  const [filtersOpen, setFiltersOpen] = useState(false)

  const tablePanel = (
    <Panel ref={panelRef} className="flex min-w-0 flex-col">
      <Toolbar>
        {tabs && tabs.length > 0 && <Segmented items={tabs} active={activeTab} onChange={changeTab} />}
        {compact && filters ? (
          <Button
            size="sm"
            variant={filtersOpen ? 'subtle' : 'default'}
            onClick={() => setFiltersOpen((open) => !open)}
            aria-expanded={filtersOpen}
          >
            <SlidersHorizontal className="size-3.5" strokeWidth={1.7} />
            Bộ lọc
            <ChevronDown className={cn('size-3.5', filtersOpen && 'rotate-180')} strokeWidth={1.8} />
          </Button>
        ) : (
          filters
        )}
        <ToolbarSpacer />
        <TableSettings
          columns={columns}
          hidden={table.hidden}
          onToggleColumn={table.toggleColumn}
          density={table.density}
          onDensityChange={table.setDensity}
        />
        {onRefresh && (
          <IconButton
            label="Nạp lại"
            size="sm"
            onClick={onRefresh}
            icon={<RotateCw className={loading ? 'size-3.5 animate-spin' : 'size-3.5'} strokeWidth={1.7} />}
          />
        )}
        {actions}
      </Toolbar>

      {compact && filters && filtersOpen && (
        <div className="flex flex-wrap items-center gap-2 border-b border-line bg-panel-2 px-3 py-2.5">
          {filters}
        </div>
      )}

      {selectable && bulk && (
        <BulkBar count={table.selected.size} onClear={table.clearSelection}>
          {bulk}
        </BulkBar>
      )}

      <DataTable
        columns={table.visibleColumns}
        rows={table.rows}
        getKey={keyOf}
        loading={loading}
        error={error ? errorMessage(error) : undefined}
        onRetry={onRetry ?? onRefresh}
        emptyTitle={emptyTitle ?? 'Chưa có dữ liệu trong bộ lọc này'}
        emptyDescription={emptyDescription}
        emptyAction={emptyAction}
        onRowClick={onRowClick}
        activeKey={activeKey}
        selectable={selectable}
        selectedKeys={table.selected}
        onSelectionChange={table.setSelected}
        page={table.page}
        pageSize={table.pageSize}
        total={table.total}
        onPageChange={table.setPage}
        onPageSizeChange={table.setPageSize}
        sort={table.sort}
        onSortChange={table.setSort}
        density={table.density}
      />
    </Panel>
  )

  return (
    <Stack>
      {figures}
      {children}
      {aside ? <Split main={tablePanel} aside={aside} asideWidth={asideWidth} /> : tablePanel}
      {detail}
    </Stack>
  )
}
