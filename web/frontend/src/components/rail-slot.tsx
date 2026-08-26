import { createContext, useContext, useEffect, useRef, useMemo, useState, type ReactNode } from "react";

/**
 * Cho phép MỘT trang chiếm chỗ thanh bên trái của vỏ app.
 *
 * Vì sao cần: trang chi tiết phiếu xuất kho muốn biến rìa trái thành "ngăn xếp phiếu" để nhảy qua
 * lại giữa các phiếu trong ngày mà không phải quay về danh sách. Làm bằng ngữ cảnh thay vì để
 * Layout tự dò đường dẫn: vỏ app không phải biết gì về nghiệp vụ bán hàng, và trang nào cũng dùng
 * lại được cơ chế này.
 *
 * Chỉ có MỘT chỗ: trang sau ghi đè trang trước. Rời trang thì tự trả lại menu như cũ.
 */
const RailSlotContext = createContext<{
  rail: ReactNode | null;
  setRail: (node: ReactNode | null) => void;
}>({ rail: null, setRail: () => {} });

export function RailSlotProvider({ children }: { children: ReactNode }) {
  const [rail, setRail] = useState<ReactNode | null>(null);
  const value = useMemo(() => ({ rail, setRail }), [rail]);
  return <RailSlotContext.Provider value={value}>{children}</RailSlotContext.Provider>;
}

/** Vỏ app đọc chỗ này để quyết định vẽ menu hay thanh bên riêng của trang. */
export function useRailContent() {
  return useContext(RailSlotContext).rail;
}

/**
 * Trang gọi hàm này để chiếm thanh bên. `node` phải được ghi nhớ (useMemo) — mỗi lần đổi tham chiếu
 * là một lần vẽ lại thanh bên, node dựng mới ở mỗi lần render sẽ làm nó nháy liên tục.
 */
export function useRailSlot(node: ReactNode | null) {
  const { setRail } = useContext(RailSlotContext);
  useEffect(() => {
    setRail(node);
    return () => setRail(null);
  }, [node, setRail]);
}

type RailKind = "menu" | "stack";

/**
 * Đổi giữa MENU và thanh bên riêng của trang, có hiệu ứng trượt:
 * lớp cũ đi ra bên TRÁI trước, lớp mới hiện ra ở bên PHẢI rồi trượt sang trái vào chỗ.
 *
 * Phải giữ lớp cũ thêm một nhịp sau khi nó đã bị gỡ khỏi ngữ cảnh — nếu không thì menu biến mất
 * ngay lập tức và chẳng còn gì để chạy hiệu ứng "đi ra". Vì vậy node cuối cùng được nhớ trong ref.
 *
 * Chỉ chạy hiệu ứng khi ĐỔI LOẠI. Cùng loại mà đổi nội dung (bấm sang phiếu khác trong ngăn xếp)
 * thì thay tại chỗ, không trượt — nếu không mỗi lần bấm là cả thanh bên nhảy một cái.
 */
/**
 * Máy yếu (perf-lite) hoặc người dùng tắt chuyển động: CSS đã bỏ hiệu ứng, nên cũng phải bỏ luôn
 * pha "giữ lớp cũ" — không thì hai lớp nằm chồng nhau nửa giây rồi lớp cũ biến mất đột ngột.
 */
function motionOff() {
  if (typeof window === "undefined") return true;
  return (
    document.documentElement.classList.contains("perf-lite") ||
    window.matchMedia("(prefers-reduced-motion: reduce)").matches
  );
}

export function RailSwitch({ menu }: { menu: ReactNode }) {
  const override = useRailContent();
  const kind: RailKind = override ? "stack" : "menu";
  const [shown, setShown] = useState<RailKind>(kind);
  const [leaving, setLeaving] = useState<{ kind: RailKind; node: ReactNode } | null>(null);
  const lastOverride = useRef<ReactNode>(override);

  useEffect(() => {
    if (kind === shown) {
      lastOverride.current = override;
      return;
    }
    setShown(kind);
    lastOverride.current = override;
    if (motionOff()) {
      setLeaving(null);
      return;
    }
    setLeaving({ kind: shown, node: shown === "stack" ? lastOverride.current : menu });
    const timer = window.setTimeout(() => setLeaving(null), 460);
    return () => window.clearTimeout(timer);
  }, [kind, shown, override, menu]);

  return (
    <div className="km-rail-swap">
      {leaving && (
        <div className="km-rail-layer is-leaving" aria-hidden="true">
          {leaving.node}
        </div>
      )}
      <div key={shown} className={`km-rail-layer ${leaving ? "is-entering" : ""}`}>
        {shown === "stack" ? override : menu}
      </div>
    </div>
  );
}
