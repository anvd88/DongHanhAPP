import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode,
  type RefObject,
} from 'react'
import { createPortal } from 'react-dom'
import { cn } from '@/lib/cn'

/**
 * Lớp nổi bám theo một phần tử mốc (ô nhập, nút). Vẽ vào body qua portal và định vị cố định
 * nên không bị khung cuộn ngang của bảng cắt mất; lật lên trên khi thiếu chỗ bên dưới.
 * Đóng khi bấm ra ngoài hoặc nhấn Esc; Esc ở đây chặn lan lên để không đóng luôn hộp thoại cha.
 */
export function AnchoredLayer({
  anchorRef,
  open,
  onClose,
  children,
  align = 'start',
  matchWidth,
  minWidth,
  className,
  label,
}: {
  anchorRef: RefObject<HTMLElement | null>
  open: boolean
  onClose: () => void
  children: ReactNode
  align?: 'start' | 'end'
  /** Rộng bằng phần tử mốc (danh sách gợi ý của ô nhập). */
  matchWidth?: boolean
  minWidth?: number
  className?: string
  label?: string
}) {
  const layerRef = useRef<HTMLDivElement>(null)
  const [style, setStyle] = useState<CSSProperties>({ position: 'fixed', visibility: 'hidden' })

  useLayoutEffect(() => {
    if (!open) return
    const place = () => {
      const anchor = anchorRef.current
      const layer = layerRef.current
      if (!anchor || !layer) return
      const a = anchor.getBoundingClientRect()
      const width = matchWidth ? a.width : layer.offsetWidth
      const height = layer.offsetHeight
      const vw = window.innerWidth
      const vh = window.innerHeight
      let left = align === 'end' ? a.right - width : a.left
      left = Math.max(8, Math.min(left, vw - width - 8))
      let top = a.bottom + 4
      if (top + height > vh - 8 && a.top - height - 4 > 8) top = a.top - height - 4
      setStyle({
        position: 'fixed',
        top,
        left,
        width: matchWidth ? a.width : undefined,
        // Không khớp bề rộng mốc thì ít nhất rộng bằng mốc, và không nhỏ hơn mức tối thiểu yêu cầu.
        minWidth: matchWidth ? undefined : Math.min(Math.max(minWidth ?? 0, a.width), vw - 16),
        maxWidth: vw - 16,
        visibility: 'visible',
      })
    }
    place()
    window.addEventListener('scroll', place, true)
    window.addEventListener('resize', place)
    return () => {
      window.removeEventListener('scroll', place, true)
      window.removeEventListener('resize', place)
    }
  }, [open, align, matchWidth, minWidth, anchorRef])

  useEffect(() => {
    if (!open) return
    const onPointer = (event: PointerEvent) => {
      const target = event.target as Node
      if (layerRef.current?.contains(target) || anchorRef.current?.contains(target)) return
      onClose()
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation()
        onClose()
      }
    }
    document.addEventListener('pointerdown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      document.removeEventListener('keydown', onKey)
    }
  }, [open, onClose, anchorRef])

  if (!open) return null

  return createPortal(
    <div
      ref={layerRef}
      role="dialog"
      aria-label={label}
      data-layer=""
      style={style}
      className={cn('pop-panel z-[60]', className)}
    >
      {children}
    </div>,
    document.body,
  )
}

/** Có lớp nổi nào đang mở không; hộp thoại dùng để không đóng nhầm khi người dùng nhấn Esc trong lớp nổi. */
export function hasOpenLayer() {
  return document.querySelector('[data-layer]') !== null
}
