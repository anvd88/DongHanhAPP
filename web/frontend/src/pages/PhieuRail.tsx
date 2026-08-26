import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { ArrowLeft, Search, X } from "lucide-react";
import { DatePicker } from "../components/DateField";
import { api } from "../lib/api";
import { money } from "../lib/format";
import type { DocumentStackItem } from "../lib/types";

/**
 * NGĂN XẾP PHIẾU — chiếm chỗ menu ở rìa trái khi đang xem chi tiết một phiếu xuất kho.
 *
 * Vì sao: xem chi tiết mà không thấy các phiếu khác thì mỗi lần đối chiếu lại phải quay về danh
 * sách rồi bấm vào lại. Kế toán làm việc theo NGÀY (một xấp phiếu của hôm đó), nên ngăn xếp khoá
 * theo một ngày, và tìm được trong đúng ngày ấy.
 *
 * Ô tìm kiếm hỏi máy chủ vì nó soi cả CHỦNG LOẠI HÀNG và QUY CÁCH — hai thứ nằm trong dòng hàng,
 * danh sách phiếu không mang theo.
 */
export function PhieuRail({ currentId, initialDate }: { currentId: string; initialDate: string }) {
  const navigate = useNavigate();

  // Ngày + từ khoá sống trong ĐƯỜNG DẪN, không phải trong state của component.
  // Lý do: vỏ app gắn key theo pathname cho vùng nội dung, nên bấm sang phiếu khác là cả trang
  // (kèm thanh bên này) bị dựng lại — state cục bộ mất sạch, người dùng lọc xong bấm một cái là
  // phải gõ lại. Nằm ở query thì còn nguyên sau khi chuyển phiếu, và F5 hay gửi link cũng đúng.
  const [params, setParams] = useSearchParams();
  const day = params.get("ngay") || initialDate;
  const term = params.get("tim") ?? "";
  const setParam = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  };
  const setDay = (value: string) => setParam("ngay", value);
  const setTerm = (value: string) => setParam("tim", value);

  const [items, setItems] = useState<DocumentStackItem[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    const timer = setTimeout(() => {
      setLoading(true);
      const query = new URLSearchParams({ date: day, q: term.trim() });
      api
        .get<{ items: DocumentStackItem[] }>(`/api/documents/stack?${query}`)
        .then((res) => {
          if (!cancelled) setItems(res.items);
        })
        .catch(() => {
          if (!cancelled) setItems([]);
        })
        .finally(() => {
          if (!cancelled) setLoading(false);
        });
      // Gõ tới đâu hỏi tới đó thì mỗi phím một truy vấn có JOIN dòng hàng — chờ người dùng ngừng gõ.
    }, term ? 300 : 0);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [day, term]);

  const totalOfDay = useMemo(
    () => items.filter((i) => !i.cancelledAt).reduce((sum, i) => sum + i.total, 0),
    [items],
  );

  return (
    <aside className="pr-rail">
      <button type="button" className="pr-back" onClick={() => navigate("/ban-hang")}>
        <ArrowLeft className="h-4 w-4" /> Danh sách bán hàng
      </button>

      <div className="pr-filters">
        <DatePicker value={day} onChange={setDay} ariaLabel="Ngày của xấp phiếu" />
        <div className="pr-search">
          <Search className="h-4 w-4 shrink-0 opacity-60" />
          <input
            value={term}
            onChange={(event) => setTerm(event.target.value)}
            placeholder="Khách, chủng loại, quy cách…"
            aria-label="Tìm trong xấp phiếu của ngày đã chọn"
          />
          {term && (
            <button type="button" onClick={() => setTerm("")} aria-label="Xoá từ khoá">
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>
      </div>

      <div className="pr-count">
        {loading ? "Đang tìm…" : `${items.length} phiếu · ${money(totalOfDay)} ₫`}
      </div>

      <ol className="pr-list scroll-thin">
        {!loading && items.length === 0 && (
          <li className="pr-empty">
            {term ? "Không có phiếu nào khớp trong ngày này." : "Ngày này chưa có phiếu xuất kho."}
          </li>
        )}
        {items.map((item) => {
          const active = item.id === currentId;
          return (
            <li key={item.id}>
              <button
                type="button"
                className={`pr-item ${active ? "is-active" : ""}`}
                aria-current={active ? "true" : undefined}
                // Mang theo bộ lọc để xem xong phiếu này còn quay lại đúng danh sách đang lọc.
                onClick={() => navigate({ pathname: `/ban-hang/${item.id}`, search: params.toString() })}
              >
                <span className="pr-item-top">
                  <b>{item.voucherNo || "Phiếu nháp"}</b>
                  <StackChip item={item} />
                </span>
                <span className="pr-item-customer">{item.customerName || "Khách lẻ"}</span>
                <span className="pr-item-total">{money(item.total)} ₫</span>
              </button>
            </li>
          );
        })}
      </ol>
    </aside>
  );
}

/** Trạng thái rút gọn — đủ để quét mắt xuống xấp phiếu và biết cái nào còn dở. */
function StackChip({ item }: { item: DocumentStackItem }) {
  if (item.cancelledAt) return <em className="pr-chip pr-chip--dead">Đã hủy</em>;
  if (!item.issuedAt) return <em className="pr-chip">Chưa phát hành</em>;
  if (item.deliveryReturnedAt) return <em className="pr-chip pr-chip--ok">Đã giao hàng</em>;
  // 'submitted' = lái xe đã báo giao xong; việc còn lại là thu tờ phiếu. ('accepted' chỉ còn ở
  // phiếu cũ, từ trước khi bỏ chặng nghiệm thu.)
  if (item.deliveryTaskStatus === "submitted" || item.deliveryTaskStatus === "accepted")
    return <em className="pr-chip pr-chip--warn">Chờ nộp phiếu</em>;
  if (item.deliveryMode === "driver") return <em className="pr-chip pr-chip--go">Đang giao</em>;
  if (item.deliveryMode === "pickup") return <em className="pr-chip">Khách lấy</em>;
  return <em className="pr-chip pr-chip--warn">Chưa gán</em>;
}
