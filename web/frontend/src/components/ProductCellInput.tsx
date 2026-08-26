import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api";
import { money } from "../lib/format";
import type { Product } from "../lib/types";

/**
 * Ô "Chủng loại hàng hoá" trên bảng hàng của phiếu, có GỢI Ý từ danh mục hàng hoá.
 *
 * Nguyên tắc: gợi ý, KHÔNG ép. Gõ tay vẫn lưu được y như trước (hàng lạ, hàng gia công một lần,
 * phiếu chép lại từ giấy). Chọn từ danh mục chỉ là đường nhanh — và khi chọn thì điền luôn quy cách
 * + đóng dấu mã hàng để thống kê bám theo mã chứ không theo chính tả.
 *
 * Không dùng <datalist> vì một tên hàng có nhiều quy cách ("Thép tấm" 8mm/10mm/12mm): datalist chỉ
 * gợi ý theo value nên sẽ hiện mấy dòng trùng tên không phân biệt được. Dropdown tự vẽ cho phép hiện
 * "tên · quy cách · giá bán gần nhất" — đúng thứ người lập phiếu cần thấy.
 */
export function ProductCellInput({
  value,
  onChange,
  onPick,
}: {
  value: string;
  onChange: (value: string) => void;
  /** Chọn từ danh mục: điền tên + quy cách + mã hàng trong một nhịp. */
  onPick: (product: Product) => void;
}) {
  const products = useProductCatalog();
  const [open, setOpen] = useState(false);
  const [highlight, setHighlight] = useState(0);
  const boxRef = useRef<HTMLDivElement>(null);

  const keyword = value.trim().toLowerCase();
  const matches = products
    .filter((item) => {
      if (!item.isActive) return false;
      if (!keyword) return true;
      return `${item.name} ${item.spec} ${item.code}`.toLowerCase().includes(keyword);
    })
    .slice(0, 8);

  // Đóng khi bấm ra ngoài. Không dùng onBlur: bấm vào chính dòng gợi ý cũng làm ô mất focus, dropdown
  // biến mất trước khi click kịp ăn.
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (!boxRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  const choose = (product: Product) => {
    onPick(product);
    setOpen(false);
  };

  return (
    <div ref={boxRef} className="relative">
      <input
        type="text"
        value={value}
        onChange={(event) => {
          onChange(event.target.value);
          setOpen(true);
          setHighlight(0);
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={(event) => {
          if (!open || matches.length === 0) return;
          if (event.key === "ArrowDown") {
            event.preventDefault();
            setHighlight((n) => Math.min(n + 1, matches.length - 1));
          } else if (event.key === "ArrowUp") {
            event.preventDefault();
            setHighlight((n) => Math.max(n - 1, 0));
          } else if (event.key === "Enter") {
            // Chỉ cướp phím Enter khi người dùng đang thực sự chọn trong danh sách.
            if (matches[highlight]) {
              event.preventDefault();
              choose(matches[highlight]);
            }
          } else if (event.key === "Escape") {
            setOpen(false);
          }
        }}
        className="w-full min-w-[80px] rounded-lg bg-transparent px-2 py-1.5 text-sm outline-none focus:bg-[var(--accent-soft)]"
      />
      {open && matches.length > 0 && (
        <ul className="absolute left-0 top-full z-30 mt-1 max-h-60 w-[min(24rem,70vw)] overflow-auto rounded-xl border border-[var(--gc-border)] bg-[var(--gc-surface,var(--surface,#fff))] p-1 shadow-xl dark:bg-[#161b22]">
          {matches.map((item, index) => (
            <li key={item.id}>
              <button
                type="button"
                onMouseEnter={() => setHighlight(index)}
                onClick={() => choose(item)}
                className={`flex w-full items-center gap-2 rounded-lg px-2.5 py-1.5 text-left text-sm ${
                  index === highlight ? "bg-[var(--gc-accent)]/12" : ""
                }`}
              >
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-bold">{item.name}</span>
                  {item.spec && (
                    <span className="block truncate text-xs text-[var(--gc-text-muted)]">{item.spec}</span>
                  )}
                </span>
                {item.lastPrice != null && (
                  <span className="shrink-0 text-xs font-bold text-[var(--gc-text-muted)]">
                    {money(item.lastPrice)} ₫
                  </span>
                )}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// Danh mục đổi rất ít nhưng ô này có mặt trên MỌI dòng hàng của MỌI phiếu — để mỗi ô tự gọi mạng là
// mở một phiếu 20 dòng bắn 20 request. Nạp một lần cho cả phiên, ai mở trước thì những ô sau dùng ké
// cùng một promise.
let catalogPromise: Promise<Product[]> | null = null;
let catalogCache: Product[] | null = null;

/** Xoá bộ nhớ đệm sau khi sửa danh mục, để ô gợi ý thấy mặt hàng mới ngay. */
export function invalidateProductCatalog() {
  catalogPromise = null;
  catalogCache = null;
}

function useProductCatalog(): Product[] {
  const [products, setProducts] = useState<Product[]>(() => catalogCache ?? []);

  useEffect(() => {
    if (catalogCache) return;
    let cancelled = false;
    catalogPromise ??= api
      .get<{ items: Product[] }>("/api/products")
      .then((res) => {
        catalogCache = res.items;
        return res.items;
      })
      .catch(() => {
        // Danh mục hỏng thì ô nhập vẫn phải gõ tay được — im lặng là đúng ở đây.
        catalogPromise = null;
        return [];
      });
    void catalogPromise.then((items) => {
      if (!cancelled) setProducts(items);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  return products;
}
