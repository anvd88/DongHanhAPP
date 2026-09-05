import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/http'
import { refreshTopics } from '@/lib/realtime'

/* ============================================================================
   Hàng trả về và gia công — hai sổ phụ của kho.
   ========================================================================== */

/* ── Hàng trả về ────────────────────────────────────────────────────────── */

export interface ReturnSourceLine {
  documentId: string
  voucherNo: string
  docDate: string
  lineNo: number
  content: string
  spec: string
  quantity: number
  unitPrice: number
  returnedQuantity: number
  remaining: number
  /** Phiếu đã chốt về kho: phải lập phiếu trả riêng, không hạ thẳng số lượng được nữa. */
  settled: boolean
}

export interface GoodsReturn {
  id: string
  voucherNo: string
  docDate: string
  customerName: string
  content: string
  note: string
  total: number
  cancelledAt: string | null
  cancelReason: string
}

export interface GoodsReturnLine {
  lineNo: number
  content: string
  spec: string
  quantity: number
  unitPrice: number
  amount: number
  sourceDocumentId: string | null
  sourceVoucherNo: string
  sourceDate: string | null
}

export interface GoodsReturnDetail {
  document: {
    id: string
    voucherNo: string
    docDate: string
    customerName: string
    content: string
    note: string
    cancelledAt: string | null
    cancelReason: string
    createdAt: string
  }
  lines: GoodsReturnLine[]
}

export interface CreateReturnRequest {
  date: string
  reason: string
  note: string
  contextDocumentId?: string | null
  lines: Array<{ sourceDocumentId: string; sourceLineNo: number; quantity: number }>
}

export interface CreateReturnResult {
  returnId: string | null
  returnNo: string
  returnTotal: number
  /** Số dòng hạ thẳng trên phiếu chưa chốt, không sinh phiếu trả. */
  adjustedLines: number
  vouchisedLines: number
}

const RETURNS = ['sales', 'returns'] as const

export function useReturns(params: { customerId?: string; sourceDocumentId?: string; from?: string; to?: string } = {}) {
  return useQuery({
    queryKey: [...RETURNS, params.customerId ?? '', params.sourceDocumentId ?? '', params.from ?? '', params.to ?? ''],
    queryFn: () => api.get<{ items: GoodsReturn[] }>('/returns', { query: params }),
  })
}

export function useReturn(id: string | null | undefined) {
  return useQuery({
    queryKey: [...RETURNS, 'detail', id],
    queryFn: () => api.get<GoodsReturnDetail>(`/returns/${id}`),
    enabled: !!id,
  })
}

/** Các dòng đã bán cho một khách, kèm số còn có thể trả. Đây là bảng tra giá bán gốc. */
export function useReturnSources(params: { customerId?: string; customerName?: string; q?: string }, enabled = true) {
  return useQuery({
    queryKey: [...RETURNS, 'sources', params.customerId ?? '', params.customerName ?? '', params.q ?? ''],
    queryFn: () => api.get<{ items: ReturnSourceLine[] }>('/returns/sources', { query: params }),
    enabled: enabled && (!!params.customerId || !!params.customerName),
  })
}

function useReturnMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    // Hàng trả về hạ số ĐÃ BÁN nên công nợ của khách đổi theo, không chỉ sổ bán hàng.
    onSuccess: () => refreshTopics(queryClient, 'sales', 'debts'),
  })
}

export function useCreateReturn() {
  return useReturnMutation((body: CreateReturnRequest) => api.post<CreateReturnResult>('/returns', body))
}

export function useCancelReturn() {
  return useReturnMutation(({ id, reason }: { id: string; reason: string }) =>
    api.put<void>(`/returns/${id}/cancel`, { reason }),
  )
}

/* ── Gia công ───────────────────────────────────────────────────────────── */

export interface GiaCongLine {
  id: number
  loaiDong: string
  /** Mã hàng trong danh mục; có thì phiếu gia công ghép được với phiếu bán và phiếu nhập mua. */
  productId?: string | null
  maHang: string
  tenHang: string
  quyCach: string
  donViTinh: string
  soLuong: number
  donGiaGiaCong: number
  ghiChu: string
  thanhTien: number
}

export interface GiaCongVoucher {
  id: number
  maPhieu: string
  loaiPhieu: string
  doiTac: string
  nhanVienPhuTrach: string
  ngayLap: string
  hanHoanThanh: string | null
  soMatHang: number
  tongGiaTri: number
  soLuongXuat: number
  soLuongNhap: number
  /** Còn nằm ở xưởng đối tác: đã xuất đi mà chưa nhập về. */
  soLuongConTaiCongTy: number
  tienGiaCongPhaiTra: number
}

export interface GiaCongDetail {
  id: number
  maPhieu: string
  loaiPhieu: string
  doiTac: string
  nhanVienPhuTrach: string
  ngayLap: string
  hanHoanThanh: string | null
  ghiChu: string
  lines: GiaCongLine[]
}

export interface GiaCongReport {
  soLuongXuat: number
  soLuongNhap: number
  soLuongConTaiCongTy: number
  tienGiaCongPhaiTra: number
  partners: Array<{
    doiTac: string
    soLuongXuat: number
    soLuongNhap: number
    soLuongConTaiCongTy: number
    tienGiaCongPhaiTra: number
  }>
  items: Array<{
    doiTac: string
    tenHang: string
    quyCach: string
    donViTinh: string
    soLuongXuat: number
    soLuongNhap: number
    soLuongConTaiCongTy: number
    tienGiaCongPhaiTra: number
  }>
}

export interface SaveGiaCongRequest {
  loaiPhieu: string
  doiTac: string
  nhanVienPhuTrach: string
  ngayLap: string
  hanHoanThanh?: string | null
  ghiChu: string
  lines: Array<{
    id: number
    loaiDong: string
    maHang: string
    tenHang: string
    quyCach: string
    donViTinh: string
    soLuong: number
    donGiaGiaCong: number
    ghiChu: string
  }>
}

const GIACONG = ['sales', 'giacong'] as const

export function useGiaCongVouchers(params: { filter?: string; search?: string } = {}) {
  return useQuery({
    queryKey: [...GIACONG, params.filter ?? '', params.search ?? ''],
    queryFn: () => api.get<GiaCongVoucher[]>('/giacong', { query: params }),
  })
}

export function useGiaCong(id: number | null | undefined) {
  return useQuery({
    queryKey: [...GIACONG, 'detail', id],
    queryFn: () => api.get<GiaCongDetail>(`/giacong/${id}`),
    enabled: id != null,
  })
}

export function useGiaCongReport(params: { doiTac?: string; from?: string; to?: string }, enabled = true) {
  return useQuery({
    queryKey: [...GIACONG, 'report', params.doiTac ?? '', params.from ?? '', params.to ?? ''],
    queryFn: () => api.get<GiaCongReport>('/giacong/report', { query: params }),
    enabled,
  })
}

function useGiaCongMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => refreshTopics(queryClient, 'sales'),
  })
}

export function useSaveGiaCong() {
  return useGiaCongMutation(({ id, body }: { id?: number; body: SaveGiaCongRequest }) =>
    id ? api.put<GiaCongDetail>(`/giacong/${id}`, body) : api.post<GiaCongDetail>('/giacong', body),
  )
}

export function useDeleteGiaCong() {
  return useGiaCongMutation((id: number) => api.del<void>(`/giacong/${id}`))
}
