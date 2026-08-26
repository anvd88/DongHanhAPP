import { Vault } from "lucide-react";
import { StatCard } from "../features/giacong/StatCard";
import { PERM, useAccess } from "../lib/access";
import { moneyVnd } from "../lib/format";
import { useApi } from "../lib/useApi";

export interface CashFundBalance {
  balance: number;
  monthIn: number;
  monthOut: number;
  monthCount: number;
  month: string;
}

/**
 * Thẻ "Tồn quỹ tiền mặt" — cùng một ô xuất hiện ở trang Lệnh thu tiền, Phiếu chi tiền mặt và Quỹ
 * tiền mặt, để ở đâu cũng thấy ngay trong két còn bao nhiêu trước khi quyết định thu hay chi.
 *
 * Không có quyền xem quỹ thì component tự biến mất thay vì hiện ô rỗng hay ô báo lỗi 403.
 */
export function CashFundBalanceCard({ index = 0, month }: { index?: number; month?: string }) {
  const { can } = useAccess();
  const allowed = can(PERM.cashFundRead);
  const query = month ? `?month=${encodeURIComponent(month)}` : "";
  const { data } = useApi<CashFundBalance>(allowed ? `/api/cash-fund/balance${query}` : null);
  if (!allowed) return null;

  const balance = data?.balance ?? 0;
  return (
    <StatCard
      index={index}
      icon={Vault}
      label="Tồn quỹ tiền mặt"
      value={moneyVnd(balance)}
      sub={data ? `Tháng này +${moneyVnd(data.monthIn)} · −${moneyVnd(data.monthOut)}` : "Đang tải…"}
      tone={balance < 0 ? "225, 29, 72" : "5, 150, 105"}
    />
  );
}
