import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/http'
import { refreshTopics } from '@/lib/realtime'
import type { Tone } from '@/ui'

/* Mua hàng: nhà cung cấp và phiếu nhập mua. Công nợ phải trả = tổng phiếu trừ số đã trả. */

export interface Supplier {
  id: string
  name: string
  taxCode: string
  phone: string
  address: string
  note: string
  isActive: boolean
  purchaseCount: number
  purchasedTotal: number
  paidTotal: number
  /** Dương là còn nợ nhà cung cấp. */
  balance: number
  lastPurchaseDate: string | null
  /** Các tên gọi khác cùng trỏ về nhà cung cấp này, ví dụ "anh A - Đại Phát". */
  aliases: string[]
}

export interface SupplierAlias {
  id: number
  alias: string
  createdBy: string
  createdAt: string
}

export interface SaveSupplierRequest {
  name: string
  taxCode: string
  phone: string
  address: string
  note: string
  isActive?: boolean
}

export interface Purchase {
  id: string
  voucherNo: string
  docDate: string
  supplierName: string
  supplierInvoiceNo: string
  note: string
  total: number
  paidAmount: number
  remaining: number
  cancelledAt: string | null
  cancelReason: string
  createdBy: string
}

export interface PurchaseLine {
  lineNo: number
  productId: string | null
  lineContent: string
  spec: string
  quantity: number
  unitPrice: number
  note: string
}

export interface PurchaseDetail {
  purchase: {
    id: string
    voucherNo: string
    docDate: string
    supplierId: string | null
    supplierName: string
    supplierInvoiceNo: string
    note: string
    paidAmount: number
    cancelledAt: string | null
    cancelReason: string
  }
  lines: PurchaseLine[]
}

export interface SavePurchaseRequest {
  voucherNo?: string
  date: string
  supplierId?: string | null
  supplierName: string
  supplierInvoiceNo: string
  note: string
  paidAmount: number
  lines: Array<{
    productId?: string | null
    lineContent: string
    spec: string
    quantity: number
    unitPrice: number
    note: string
  }>
}

export type PurchaseStatusId = 'cancelled' | 'paid' | 'partial' | 'unpaid'

export function purchaseStatus(row: Pick<Purchase, 'cancelledAt' | 'total' | 'paidAmount'>): {
  id: PurchaseStatusId
  label: string
  tone: Tone
} {
  if (row.cancelledAt) return { id: 'cancelled', label: 'Đã huỷ', tone: 'danger' }
  if (row.total > 0 && row.paidAmount >= row.total) return { id: 'paid', label: 'Đã trả đủ', tone: 'ok' }
  if (row.paidAmount > 0) return { id: 'partial', label: 'Trả một phần', tone: 'warn' }
  return { id: 'unpaid', label: 'Chưa trả', tone: 'neutral' }
}

const SUPPLIERS = ['purchases', 'suppliers'] as const
const PURCHASES = ['purchases', 'purchases'] as const

export function useSuppliers(includeInactive = false) {
  return useQuery({
    queryKey: [...SUPPLIERS, includeInactive],
    queryFn: () => api.get<{ items: Supplier[] }>('/suppliers', { query: { includeInactive } }),
  })
}

/** Một mặt hàng còn tồn của nhà cung cấp này: đã nhập bao nhiêu, đã bán ra bao nhiêu. */
export interface SupplierStockItem {
  productId: string
  code: string
  name: string
  spec: string
  unit: string
  bought: number
  sold: number
  remaining: number
  lastCost: number | null
  lastBoughtDate: string | null
}

/**
 * Hàng của một nhà cung cấp còn lại trong kho.
 *
 * Số này bắc cầu giữa hai chủ đề realtime: nhập thêm thì đổi (chủ đề 'purchases'), bán ra cũng đổi
 * (chủ đề 'sales'). Chọn 'sales' vì bán là việc xảy ra hàng ngày còn nhập thì thưa; kèm staleTime 0
 * để mỗi lần mở ngăn kéo là đọc lại, nên vế nhập cũng không bao giờ cũ quá một lần mở.
 */
export function useSupplierStock(supplierId: string | null | undefined) {
  return useQuery({
    queryKey: ['sales', 'supplier-stock', supplierId],
    queryFn: () => api.get<{ items: SupplierStockItem[] }>(`/suppliers/${supplierId}/stock`),
    enabled: !!supplierId,
    staleTime: 0,
  })
}

export function useSupplierAliases(supplierId: string | null | undefined) {
  return useQuery({
    queryKey: [...SUPPLIERS, supplierId, 'aliases'],
    queryFn: () => api.get<{ items: SupplierAlias[] }>(`/suppliers/${supplierId}/aliases`),
    enabled: !!supplierId,
  })
}

export function useAddSupplierAlias() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ supplierId, alias }: { supplierId: string; alias: string }) =>
      api.post<{ alias: string }>(`/suppliers/${supplierId}/aliases`, { alias }),
    onSuccess: () => refreshTopics(queryClient, 'purchases'),
  })
}

export function useDeleteSupplierAlias() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ supplierId, aliasId }: { supplierId: string; aliasId: number }) =>
      api.del<void>(`/suppliers/${supplierId}/aliases/${aliasId}`),
    onSuccess: () => refreshTopics(queryClient, 'purchases'),
  })
}

export function useSaveSupplier() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: SaveSupplierRequest }) => {
      if (id) await api.put<void>(`/suppliers/${id}`, body)
      else await api.post<{ id: string }>('/suppliers', body)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['purchases'] }),
  })
}

export function usePurchases(params: { supplierId?: string; from?: string; to?: string } = {}) {
  return useQuery({
    queryKey: [...PURCHASES, params.supplierId ?? '', params.from ?? '', params.to ?? ''],
    queryFn: () => api.get<{ items: Purchase[] }>('/purchases', { query: params }),
  })
}

export function usePurchase(id: string | undefined) {
  return useQuery({
    queryKey: [...PURCHASES, id],
    queryFn: () => api.get<PurchaseDetail>(`/purchases/${id}`),
    enabled: !!id,
  })
}

export function useSavePurchase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SavePurchaseRequest }) =>
      id
        ? api.put<{ id: string; voucherNo: string; total: number }>(`/purchases/${id}`, body)
        : api.post<{ id: string; voucherNo: string; total: number }>('/purchases', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['purchases'] }),
  })
}

export function useCancelPurchase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      api.put<void>(`/purchases/${id}/cancel`, { reason }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['purchases'] }),
  })
}
