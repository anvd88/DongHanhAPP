import { useCallback, useEffect, useRef, useState } from 'react'

/**
 * Bốn khổ máy của web. Ranh giới trùng với breakpoint của Tailwind đang dùng trong giao diện:
 *   mobile  < 768px   điện thoại, một cột, thao tác bằng ngón tay
 *   tablet  768–1023  máy bảng, hai cột, menu trái vẫn ẩn
 *   laptop  1024–1439 máy xách tay, menu trái hiện, bảng bắt đầu đủ chỗ
 *   desktop ≥ 1440px  màn hình rời, bảng hiện đủ cột và có chỗ cho cột phụ
 */
export type Breakpoint = 'mobile' | 'tablet' | 'laptop' | 'desktop'

const QUERIES: Array<[Breakpoint, string]> = [
  ['mobile', '(max-width: 767px)'],
  ['tablet', '(min-width: 768px) and (max-width: 1023px)'],
  ['laptop', '(min-width: 1024px) and (max-width: 1439px)'],
  ['desktop', '(min-width: 1440px)'],
]

function readBreakpoint(): Breakpoint {
  for (const [name, query] of QUERIES) if (window.matchMedia(query).matches) return name
  return 'desktop'
}

/** Khổ máy hiện tại, cập nhật khi người dùng đổi cỡ cửa sổ hoặc xoay máy. */
export function useBreakpoint(): Breakpoint {
  const [breakpoint, setBreakpoint] = useState<Breakpoint>(readBreakpoint)
  useEffect(() => {
    const lists = QUERIES.map(([, query]) => window.matchMedia(query))
    const update = () => setBreakpoint(readBreakpoint())
    lists.forEach((list) => list.addEventListener('change', update))
    update()
    return () => lists.forEach((list) => list.removeEventListener('change', update))
  }, [])
  return breakpoint
}

/**
 * Máy có phải điện thoại hay máy bảng cầm tay không. Dùng cho những màn hình chỉ có nghĩa khi
 * cầm máy trên tay, ví dụ trạm chấm công bằng khuôn mặt.
 *
 * Xét cả bề rộng lẫn kiểu con trỏ: một máy tính để bàn thu nhỏ cửa sổ vẫn là máy tính (có chuột),
 * còn máy bảng cầm ngang thì rộng hơn 768px nhưng vẫn là máy cầm tay.
 */
export function useIsHandheld(): boolean {
  const [handheld, setHandheld] = useState(readHandheld)
  useEffect(() => {
    const list = window.matchMedia(HANDHELD_QUERY)
    const update = () => setHandheld(list.matches)
    list.addEventListener('change', update)
    update()
    return () => list.removeEventListener('change', update)
  }, [])
  return handheld
}

const HANDHELD_QUERY = '(pointer: coarse) and (max-width: 1279px)'

function readHandheld() {
  return window.matchMedia(HANDHELD_QUERY).matches
}

/** Bản không phải hook, dùng ở nơi không dựng được React hook. */
export const isHandheld = readHandheld

/**
 * Khổ của MỘT KHUNG trên trang, không phải của cửa sổ.
 *
 * Bảng dữ liệu phải quyết định giấu cột theo bề rộng thật của chỗ nó đứng: cùng cửa sổ 1024px,
 * có menu trái thì bảng chỉ còn 766px, không có thì được cả 1000px. Đo cửa sổ sẽ giấu sai cột
 * ở đúng khổ máy quan trọng nhất.
 *
 *   narrow  < 640px   không đủ cho bảng, chuyển sang danh sách thẻ
 *   medium  640–1039  đủ cho cột chính, giấu cột phụ
 *   wide    ≥ 1040px  đủ cho mọi cột
 */
export type ContainerTier = 'narrow' | 'medium' | 'wide'

const NARROW_MAX = 640
const MEDIUM_MAX = 1040

export function containerTier(width: number): ContainerTier {
  if (width < NARROW_MAX) return 'narrow'
  if (width < MEDIUM_MAX) return 'medium'
  return 'wide'
}

/**
 * Theo dõi bề rộng của một phần tử. Trả về hàm gắn ref và bề rộng đo được; bề rộng là 0 cho tới
 * lần đo đầu tiên, nên nơi dùng phải coi 0 là "chưa biết" chứ không phải "rất hẹp".
 */
export function useContainerWidth(): [(node: HTMLElement | null) => void, number] {
  const [width, setWidth] = useState(0)
  const observer = useRef<ResizeObserver>(null)

  const ref = useCallback((node: HTMLElement | null) => {
    observer.current?.disconnect()
    if (!node) return
    const next = new ResizeObserver((entries) => {
      const box = entries[0]?.contentRect
      if (box) setWidth(box.width)
    })
    next.observe(node)
    observer.current = next
    setWidth(node.getBoundingClientRect().width)
  }, [])

  useEffect(() => () => observer.current?.disconnect(), [])

  return [ref, width]
}
