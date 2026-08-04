import { useEffect, useMemo, useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import {
  BanknoteArrowDown,
  CheckCircle2,
  CircleDollarSign,
  CircleCheckBig,
  History,
  Hourglass,
  Pencil,
  Plus,
  QrCode,
  ReceiptText,
  RefreshCw,
  ScanLine,
  Tags,
  Trash2,
  TriangleAlert,
  UserCheck,
  XCircle,
} from "lucide-react";
import { GlassPanel } from "../components/glass/GlassPanel";
import { LiquidTabs, type LiquidTab } from "../components/glass/LiquidTabs";
import { Modal } from "../components/Modal";
import { MonthPicker } from "../components/DateField";
import { useAppNotifications } from "../components/app-notifications-context";
import { Badge, Button, EmptyState, Field, Input, Select, Spinner } from "../components/ui";
import { StatCard } from "../features/giacong/StatCard";
import { api } from "../lib/api";
import { PERM, useAccess } from "../lib/access";
import { dateTime, moneyVnd } from "../lib/format";
import {
  voucherSourceLabel,
  voucherEventLabel,
  voucherStatusColor,
  voucherStatusLabel,
  type EmployeeDetail,
  type PayoutCategory,
  type PayoutRefundSource,
  type PayoutSummary,
  type PayoutVoucher,
  type PayoutVoucherEvent,
} from "../lib/hr";
import { useApi } from "../lib/useApi";
import "../features/giacong/giacong.css";

/** Người có thể nhận tiền (danh sách riêng của phòng kế toán — xem endpoint /recipients). */
interface Recipient {
  id: string;
  employeeCode: string;
  fullName: string;
  departmentName: string;
}

const currentMonth = () => new Date().toISOString().slice(0, 7);

const TABS: LiquidTab[] = [
  { key: "queue", label: "Đang chờ" },
  { key: "paid", label: "Đã chi" },
  { key: "all", label: "Tất cả" },
];

const badgeColor = (status: string) => {
  const tone = voucherStatusColor(status);
  return tone === "info" ? "accent" : tone;
};

/**
 * Mã QR của phiếu đã hết hạn chưa (hoặc chưa từng có mã).
 *
 * Để NGOÀI component có chủ ý: `Date.now()` là một lời gọi không thuần khiết, và khi nó nằm trong thân
 * component thì luật `react-hooks/purity` không phân biệt được "gọi lúc render" với "gọi trong hàm xử
 * lý sự kiện" nên báo nhầm. Đưa ra ngoài vừa hết cảnh báo vừa nói rõ: đây là hàm chỉ chạy khi người
 * dùng bấm mở mã QR, không phải một giá trị được tính lúc render.
 */
const isQrExpired = (v: PayoutVoucher) =>
  !v.qrValue || !v.qrExpiresAt || new Date(v.qrExpiresAt).getTime() <= Date.now();

type VoucherActionKind = "approve" | "complete" | "reject" | "cancel";

/**
 * Sổ phiếu chi tiền mặt: kế toán lập → người nhận xác nhận (nếu cần) → kế toán trưởng duyệt → thủ quỹ
 * hoàn tất. UI chỉ dựng nút từ access-profile; backend vẫn kiểm permission, phòng ban và trạng thái DB.
 */
export function PhieuChi() {
  const { can } = useAccess();
  const { notify } = useAppNotifications();
  const { data: me } = useApi<EmployeeDetail>("/api/hr/me");
  const accountingMember = !!me?.isAccounting;
  const canCreate = accountingMember && can(PERM.payoutCreate);
  const canApprove = accountingMember && can(PERM.payoutApprove);
  const canPay = accountingMember && can(PERM.payoutPay);
  const canManageCategories = can(PERM.systemSettingsManage);
  const hasMoneyPermission = can(PERM.payoutCreate) || can(PERM.payoutApprove) || can(PERM.payoutPay);

  const [tab, setTab] = useState("queue");
  const [month, setMonth] = useState("");
  const [creating, setCreating] = useState(false);
  const [managingCategories, setManagingCategories] = useState(false);
  const [qrVoucher, setQrVoucher] = useState<PayoutVoucher | null>(null);
  const [historyVoucher, setHistoryVoucher] = useState<PayoutVoucher | null>(null);
  const [voucherAction, setVoucherAction] = useState<{ voucher: PayoutVoucher; kind: VoucherActionKind } | null>(null);

  const query = `/api/payout-vouchers?scope=all${month ? `&month=${month}` : ""}`;
  const { data, loading, reload } = useApi<PayoutVoucher[]>(query, [query]);
  const { data: summary, reload: reloadSummary } = useApi<PayoutSummary>(
    `/api/payout-vouchers/summary?month=${month || currentMonth()}`,
    [month],
  );

  const vouchers = useMemo(() => data ?? [], [data]);
  const refresh = () => {
    reload({ silent: true });
    reloadSummary({ silent: true });
  };

  // Người nhận quét QR xong, server phát tín hiệu realtime → useApi tự tải lại danh sách (xem
  // lib/useApi.ts scopesForPath). Vì vậy ở đây chỉ cần theo dõi dữ liệu mới, không tự bắt sự kiện.
  // Mã QR đang mở phải bám theo dữ liệu đó: người nhận vừa ký nhận thì đóng ngay.
  useEffect(() => {
    if (!qrVoucher) return;
    const fresh = vouchers.find((v) => v.id === qrVoucher.id);
    if (!fresh) return;
    if (fresh.status !== "AwaitingScan") {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- dữ liệu realtime bên ngoài đã kết thúc modal QR.
      setQrVoucher(null);
      notify.success(`${fresh.voucherNo} đã được người nhận xác nhận và chuyển sang hàng chờ duyệt.`);
    } else if (fresh.qrValue !== qrVoucher.qrValue) {
      setQrVoucher(fresh);
    }
  }, [vouchers, qrVoucher, notify]);

  const visible = useMemo(() => {
    if (tab === "queue")
      return vouchers.filter((v) =>
        ["AwaitingScan", "AwaitingApproval", "Confirmed", "Approved"].includes(v.status),
      );
    if (tab === "paid") return vouchers.filter((v) => v.status === "Paid");
    return vouchers;
  }, [vouchers, tab]);

  const openQr = async (v: PayoutVoucher) => {
    // Mã cũ hết hạn (người nhận tới muộn) thì xin mã mới ngay khi mở, khỏi bắt kế toán bấm hai lần.
    const expired = isQrExpired(v);
    if (!expired) {
      setQrVoucher(v);
      return;
    }
    try {
      const res = await api.post<{ qrValue: string; qrExpiresAt: string }>(`/api/payout-vouchers/${v.id}/qr`, {});
      setQrVoucher({ ...v, qrValue: res.qrValue, qrExpiresAt: res.qrExpiresAt });
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không tạo được mã QR.");
    }
  };

  const totalPaid = summary?.totalPaid ?? 0;
  const totalPending = summary?.totalPending ?? 0;
  const awaiting = vouchers.filter((v) => v.status === "AwaitingScan").length;
  const awaitingReview = vouchers.filter((v) => v.status === "Confirmed" || v.status === "AwaitingApproval").length;
  const approved = vouchers.filter((v) => v.status === "Approved").length;

  return (
    <div className="gc-page space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="text-[0.78rem] font-bold uppercase tracking-wide text-[var(--gc-text-soft)]">Phòng kế toán</p>
          <h1 className="text-[1.75rem] font-black text-[var(--gc-text)]">Phiếu chi tiền mặt</h1>
        </div>
        {(canManageCategories || canCreate) && (
          <div className="flex flex-wrap gap-2">
            {canManageCategories && (
              <Button variant="ghost" onClick={() => setManagingCategories(true)}>
                <Tags className="h-4 w-4" /> Loại chi
              </Button>
            )}
            {canCreate && (
              <Button onClick={() => setCreating(true)}>
                <Plus className="h-4 w-4" /> Lập phiếu chi
              </Button>
            )}
          </div>
        )}
      </div>

      {hasMoneyPermission && !accountingMember && (
        <GlassPanel className="flex items-start gap-3 p-4 text-sm text-[var(--gc-text-soft)]">
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
          <p>
            Tài khoản có quyền nghiệp vụ nhưng hồ sơ nhân viên chưa thuộc phòng kế toán. Server sẽ không cho
            lập, duyệt hay hoàn tất chi cho đến khi điều kiện phòng ban được đáp ứng.
          </p>
        </GlassPanel>
      )}

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <StatCard index={0} icon={CircleDollarSign} label="Đã chi trong tháng" value={moneyVnd(totalPaid)} tone="0, 184, 148" />
        <StatCard index={1} icon={Hourglass} label="Đang trong quy trình" value={moneyVnd(totalPending)} tone="217, 119, 6" />
        <StatCard index={2} icon={ScanLine} label="Chờ người nhận" value={String(awaiting)} tone="88, 112, 152" />
        <StatCard index={3} icon={UserCheck} label="Chờ duyệt" value={String(awaitingReview)} tone="59, 130, 246" />
        <StatCard index={4} icon={CircleCheckBig} label="Đã duyệt · chờ chi" value={String(approved)} tone="124, 58, 237" />
      </div>

      <GlassPanel strong className="p-4">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <LiquidTabs tabs={TABS} value={tab} onChange={setTab} />
          <MonthPicker value={month} onChange={setMonth} ariaLabel="Lọc theo tháng" />
        </div>

        {loading && !data ? (
          <div className="flex justify-center py-10">
            <Spinner />
          </div>
        ) : visible.length === 0 ? (
          <EmptyState
            icon={<ReceiptText className="h-8 w-8" />}
            title={tab === "queue" ? "Không có phiếu nào đang chờ." : "Chưa có phiếu chi nào."}
            hint={canCreate ? "Bấm “Lập phiếu chi” để tạo phiếu mới." : undefined}
          />
        ) : (
          <div className="space-y-2.5">
            {visible.map((v) => (
              <VoucherRow
                key={v.id}
                v={v}
                canCreate={canCreate}
                canApprove={canApprove}
                canPay={canPay}
                onQr={() => openQr(v)}
                onAction={(kind) => setVoucherAction({ voucher: v, kind })}
                onHistory={() => setHistoryVoucher(v)}
              />
            ))}
          </div>
        )}
      </GlassPanel>

      {(summary?.byCategory.length ?? 0) > 0 && (
        <GlassPanel className="p-4">
          <h2 className="mb-3 text-sm font-black text-[var(--gc-text)]">
            Chi tiết theo loại · tháng {summary!.month.split("-").reverse().join("/")}
          </h2>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[520px] text-sm">
              <thead>
                <tr className="text-left text-[0.74rem] uppercase text-[var(--gc-text-soft)]">
                  <th className="px-3 py-2">Loại chi</th>
                  <th className="px-3 py-2 text-right">Số phiếu</th>
                  <th className="px-3 py-2 text-right">Đã chi</th>
                  <th className="px-3 py-2 text-right">Đang chờ</th>
                </tr>
              </thead>
              <tbody>
                {summary!.byCategory.map((c) => (
                  <tr key={c.categoryId ?? c.categoryName} className="border-t border-[var(--glass-border)]">
                    <td className="px-3 py-2.5 font-semibold text-[var(--gc-text)]">{c.categoryName}</td>
                    <td className="px-3 py-2.5 text-right">{c.count}</td>
                    <td className="px-3 py-2.5 text-right font-semibold">{moneyVnd(c.paidAmount)}</td>
                    <td className="px-3 py-2.5 text-right text-[var(--gc-text-soft)]">{moneyVnd(c.pendingAmount)}</td>
                  </tr>
                ))}
                <tr className="border-t-2 border-[var(--glass-border)] font-black text-[var(--gc-text)]">
                  <td className="px-3 py-2.5">Tổng cộng</td>
                  <td className="px-3 py-2.5" />
                  <td className="px-3 py-2.5 text-right">{moneyVnd(totalPaid)}</td>
                  <td className="px-3 py-2.5 text-right">{moneyVnd(totalPending)}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </GlassPanel>
      )}

      {creating && (
        <CreateVoucherModal
          onClose={() => setCreating(false)}
          onCreated={(v) => {
            setCreating(false);
            refresh();
            if (v.status === "AwaitingScan") {
              notify.success(`Đã lập phiếu ${v.voucherNo}. Đưa mã QR cho người nhận quét.`);
              setQrVoucher(v);
            } else {
              notify.success(`Đã lập phiếu ${v.voucherNo} và chuyển sang chờ duyệt.`);
            }
          }}
        />
      )}

      {qrVoucher && <VoucherQrModal voucher={qrVoucher} onClose={() => setQrVoucher(null)} onRefreshed={setQrVoucher} />}

      {historyVoucher && <VoucherHistoryModal voucher={historyVoucher} onClose={() => setHistoryVoucher(null)} />}

      {voucherAction && (
        <VoucherActionModal
          voucher={voucherAction.voucher}
          kind={voucherAction.kind}
          onClose={() => setVoucherAction(null)}
          onDone={() => {
            setVoucherAction(null);
            refresh();
          }}
        />
      )}

      {managingCategories && <CategoriesModal onClose={() => setManagingCategories(false)} />}
    </div>
  );
}

function VoucherRow({
  v,
  canCreate,
  canApprove,
  canPay,
  onQr,
  onAction,
  onHistory,
}: {
  v: PayoutVoucher;
  canCreate: boolean;
  canApprove: boolean;
  canPay: boolean;
  onQr: () => void;
  onAction: (kind: VoucherActionKind) => void;
  onHistory: () => void;
}) {
  const done = v.status === "Paid";
  const terminalFailure = v.status === "Cancelled" || v.status === "Rejected";
  const beforeApproval = ["AwaitingScan", "AwaitingApproval", "Confirmed"].includes(v.status);
  const canCancel = (canCreate && beforeApproval) || (canApprove && (beforeApproval || v.status === "Approved"));
  return (
    <article
      className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[var(--glass-border)] px-3.5 py-3"
      style={terminalFailure ? { opacity: 0.65 } : undefined}
    >
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <strong className="text-[var(--gc-text)]">{v.employeeName}</strong>
          <span className="text-[0.76rem] font-semibold text-[var(--gc-text-soft)]">{v.voucherNo}</span>
          <Badge color={badgeColor(v.status)}>{voucherStatusLabel(v.status)}</Badge>
        </div>
        <small className="text-[var(--gc-text-muted)]">
          {v.categoryName} ·{" "}
          {voucherSourceLabel(v.sourceKind)}
          {v.sourceNo ? ` ${v.sourceNo}` : ""} · lập {dateTime(v.createdAt)} bởi {v.createdBy || "Hệ thống"}
          {v.reason ? ` · ${v.reason}` : ""}
        </small>
        {v.status === "Confirmed" && v.confirmedAt && (
          <small className="block text-emerald-600 dark:text-emerald-400">
            Người nhận đã ký nhận lúc {dateTime(v.confirmedAt)}
          </small>
        )}
        {v.approvedAt && (
          <small className="block text-blue-600 dark:text-blue-400">
            Duyệt lúc {dateTime(v.approvedAt)} bởi {v.approvedBy || "Hệ thống"}
          </small>
        )}
        {done && (v.completedAt || v.paidAt) && (
          <small className="block text-[var(--gc-text-muted)]">
            Hoàn tất lúc {dateTime(v.completedAt ?? v.paidAt!)} bởi {v.completedBy || "Hệ thống"}
          </small>
        )}
        {v.status === "Rejected" && v.rejectedAt && (
          <small className="block text-red-600 dark:text-red-400">
            Từ chối lúc {dateTime(v.rejectedAt)} bởi {v.rejectedBy || "Hệ thống"} · {v.rejectReason}
          </small>
        )}
        {v.status === "Cancelled" && v.cancelledAt && (
          <small className="block text-[var(--gc-text-muted)]">
            Hủy lúc {dateTime(v.cancelledAt)} bởi {v.cancelledBy || "Hệ thống"} · {v.cancelReason}
          </small>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <span className="text-[1.05rem] font-black text-[var(--gc-text)]">{moneyVnd(v.amount)}</span>
        <Button variant="ghost" onClick={onHistory} title="Xem lịch sử phiếu">
          <History className="h-4 w-4" /> Lịch sử
        </Button>
        {canCreate && v.status === "AwaitingScan" && (
          <Button variant="soft" onClick={onQr}>
            <QrCode className="h-4 w-4" /> Mã QR
          </Button>
        )}
        {canApprove && (v.status === "Confirmed" || v.status === "AwaitingApproval") && (
          <Button onClick={() => onAction("approve")}>
            <CheckCircle2 className="h-4 w-4" /> Duyệt chi
          </Button>
        )}
        {canApprove && beforeApproval && (
          <Button variant="danger" onClick={() => onAction("reject")}>
            <XCircle className="h-4 w-4" /> Từ chối
          </Button>
        )}
        {canPay && v.status === "Approved" && (
          <Button onClick={() => onAction("complete")}>
            <BanknoteArrowDown className="h-4 w-4" /> Hoàn tất chi
          </Button>
        )}
        {canCancel && !done && !terminalFailure && (
          <Button variant="ghost" onClick={() => onAction("cancel")} title="Hủy phiếu">
            <XCircle className="h-4 w-4" /> Hủy
          </Button>
        )}
      </div>
    </article>
  );
}

/** Lập phiếu: chọn khoản hoàn đang chờ (ra sẵn số tiền) hoặc nhập tay khoản chi khác. */
function CreateVoucherModal({ onClose, onCreated }: { onClose: () => void; onCreated: (v: PayoutVoucher) => void }) {
  const { notify } = useAppNotifications();
  const { data: refunds } = useApi<PayoutRefundSource[]>("/api/payout-vouchers/sources/refunds");
  const { data: categories } = useApi<PayoutCategory[]>("/api/payout-vouchers/categories");
  const { data: recipients } = useApi<Recipient[]>("/api/payout-vouchers/recipients");

  const [mode, setMode] = useState<"refund" | "manual">("refund");
  const [refundId, setRefundId] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [employeeId, setEmployeeId] = useState("");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [note, setNote] = useState("");
  const [requiresConfirmation, setRequiresConfirmation] = useState(true);
  const [saving, setSaving] = useState(false);

  const pendingRefunds = refunds ?? [];
  const picked = pendingRefunds.find((r) => r.id === refundId) ?? null;

  // Không có khoản hoàn nào chờ thì mở thẳng chế độ nhập tay cho đỡ phải bấm. Đặt MỘT LẦN, ngay khi
  // biết danh sách, và chỉ khi người dùng chưa tự chọn — tải lại danh sách không được giật chế độ về.
  const [modePicked, setModePicked] = useState(false);
  if (!modePicked && refunds) {
    setModePicked(true);
    if (refunds.length === 0) setMode("manual");
  }

  // Loại chi mặc định: loại đầu tiên không phải danh mục hệ thống (lương/hoàn phạt do máy tự sinh).
  if (!categoryId && categories?.length) {
    setCategoryId((categories.find((c) => !c.isSystem) ?? categories[0]).id);
  }

  const submit = async () => {
    if (mode === "refund" && !refundId) {
      notify.error("Vui lòng chọn khoản hoàn cần chi.");
      return;
    }
    if (mode === "manual") {
      if (!employeeId) return notify.error("Vui lòng chọn người nhận tiền.");
      if (!(Number(amount) > 0)) return notify.error("Số tiền chi phải lớn hơn 0.");
      if (!reason.trim()) return notify.error("Vui lòng nhập nội dung chi.");
    }
    setSaving(true);
    try {
      const body =
        mode === "refund"
          ? { sourceKind: "refund", sourceId: refundId, reason: reason.trim(), note: note.trim() }
          : {
              sourceKind: "manual",
              categoryId,
              employeeId,
              amount: Number(amount),
              reason: reason.trim(),
              note: note.trim(),
              requiresRecipientConfirmation: requiresConfirmation,
            };
      const res = await api.post<{ id: string; voucherNo: string }>("/api/payout-vouchers", body);
      // Lấy lại phiếu vừa lập để có mã QR mà server sinh kèm.
      const list = await api.get<PayoutVoucher[]>("/api/payout-vouchers?scope=all");
      const fresh = list.find((v) => v.id === res.id);
      onCreated(
        fresh ??
          ({
            ...(picked as unknown as PayoutVoucher),
            id: res.id,
            voucherNo: res.voucherNo,
            status: mode === "refund" || requiresConfirmation ? "AwaitingScan" : "AwaitingApproval",
          } as PayoutVoucher),
      );
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lập được phiếu.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title="Lập phiếu chi tiền mặt"
      panel
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>
            Đóng
          </Button>
          <Button onClick={submit} loading={saving}>
            {mode === "refund" || requiresConfirmation ? "Lập phiếu & tạo mã QR" : "Lập phiếu chờ duyệt"}
          </Button>
        </div>
      }
    >
      <div className="space-y-4">
        <div className="flex gap-2">
          <Button variant={mode === "refund" ? "primary" : "ghost"} onClick={() => setMode("refund")}>
            Khoản hoàn đang chờ {pendingRefunds.length > 0 ? `(${pendingRefunds.length})` : ""}
          </Button>
          <Button variant={mode === "manual" ? "primary" : "ghost"} onClick={() => setMode("manual")}>
            Khoản chi khác
          </Button>
        </div>

        {mode === "refund" ? (
          pendingRefunds.length === 0 ? (
            <EmptyState title="Không có khoản hoàn nào đang chờ chi." hint="Chuyển sang “Khoản chi khác” để nhập tay." />
          ) : (
            <>
              <Field label="Chọn đơn hoàn tiền phạt của nhân viên">
                <Select value={refundId} onChange={(e) => setRefundId(e.target.value)}>
                  <option value="">-- Chọn khoản hoàn --</option>
                  {pendingRefunds.map((r) => (
                    <option key={r.id} value={r.id}>
                      {r.employeeName} · {r.refundNo} · {moneyVnd(r.amount)}
                    </option>
                  ))}
                </Select>
              </Field>
              {picked && (
                <GlassPanel className="space-y-1 p-3.5 text-sm">
                  <p className="text-[var(--gc-text-soft)]">Số tiền phải chi</p>
                  <p className="text-[1.6rem] font-black text-[var(--gc-text)]">{moneyVnd(picked.amount)}</p>
                  <p className="text-[var(--gc-text-muted)]">
                    {picked.employeeName} ({picked.employeeCode}) · phạt {picked.penaltyNo} · khiếu nại{" "}
                    {picked.appealRequestNo}
                  </p>
                  {picked.reason && <p className="text-[var(--gc-text-muted)]">{picked.reason}</p>}
                </GlassPanel>
              )}
            </>
          )
        ) : (
          <>
            <Field label="Loại chi">
              <Select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
                {(categories ?? []).map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Người nhận tiền">
              <Select value={employeeId} onChange={(e) => setEmployeeId(e.target.value)}>
                <option value="">-- Chọn người nhận --</option>
                {(recipients ?? []).map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.fullName} {r.employeeCode ? `(${r.employeeCode})` : ""}
                    {r.departmentName ? ` · ${r.departmentName}` : ""}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Số tiền (₫)">
              <Input type="number" min={0} step={1000} value={amount} onChange={(e) => setAmount(e.target.value)} />
            </Field>
          </>
        )}

        <Field label={mode === "refund" ? "Nội dung (để trống sẽ tự điền theo đơn)" : "Nội dung chi"}>
          <Input
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder={mode === "manual" ? "VD: Mua dầu chạy máy phát" : ""}
          />
        </Field>
        <Field label="Ghi chú">
          <Input value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
        {mode === "manual" && (
          <label className="flex items-start gap-3 rounded-xl border border-[var(--glass-border)] p-3.5 text-sm text-[var(--gc-text-soft)]">
            <input
              className="mt-0.5 h-4 w-4"
              type="checkbox"
              checked={requiresConfirmation}
              onChange={(e) => setRequiresConfirmation(e.target.checked)}
            />
            <span>
              <strong className="block text-[var(--gc-text)]">Yêu cầu người nhận xác nhận bằng QR</strong>
              Tắt lựa chọn này chỉ khi khoản chi không cần ký nhận; phiếu sẽ đi thẳng sang hàng chờ duyệt.
            </span>
          </label>
        )}
      </div>
    </Modal>
  );
}

function VoucherActionModal({
  voucher,
  kind,
  onClose,
  onDone,
}: {
  voucher: PayoutVoucher;
  kind: VoucherActionKind;
  onClose: () => void;
  onDone: () => void;
}) {
  const { notify } = useAppNotifications();
  const [note, setNote] = useState("");
  const [saving, setSaving] = useState(false);
  const destructive = kind === "reject" || kind === "cancel";
  const config: Record<VoucherActionKind, { title: string; button: string; success: string; hint: string }> = {
    approve: {
      title: "Duyệt phiếu chi",
      button: "Xác nhận duyệt",
      success: "Đã duyệt phiếu, đang chờ thủ quỹ hoàn tất.",
      hint: "Ghi chú duyệt (không bắt buộc)",
    },
    complete: {
      title: "Hoàn tất chi tiền",
      button: "Xác nhận đã chi",
      success: "Đã ghi nhận hoàn tất chi tiền.",
      hint: "Ghi chú thực chi (không bắt buộc)",
    },
    reject: {
      title: "Từ chối phiếu chi",
      button: "Từ chối",
      success: "Đã từ chối phiếu chi.",
      hint: "Lý do từ chối (bắt buộc)",
    },
    cancel: {
      title: "Hủy phiếu chi",
      button: "Hủy phiếu",
      success: "Đã hủy phiếu chi.",
      hint: "Lý do hủy (bắt buộc)",
    },
  };
  const copy = config[kind];

  const submit = async () => {
    const clean = note.trim();
    if (destructive && !clean) return notify.error(copy.hint);
    setSaving(true);
    try {
      const body = destructive ? { reason: clean } : { note: clean };
      await api.post(`/api/payout-vouchers/${voucher.id}/${kind}`, body);
      notify.success(copy.success);
      onDone();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không cập nhật được phiếu chi.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={copy.title}
      panel
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Đóng</Button>
          <Button variant={destructive ? "danger" : "primary"} onClick={submit} loading={saving}>
            {copy.button}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <GlassPanel className="p-3.5 text-sm">
          <div className="flex items-center justify-between gap-3">
            <div>
              <p className="font-black text-[var(--gc-text)]">{voucher.voucherNo} · {voucher.employeeName}</p>
              <p className="text-[var(--gc-text-muted)]">{voucher.reason}</p>
            </div>
            <strong className="shrink-0 text-lg text-[var(--gc-text)]">{moneyVnd(voucher.amount)}</strong>
          </div>
        </GlassPanel>
        <Field label={copy.hint}>
          <textarea
            className="km-form-control min-h-24 w-full resize-y rounded-xl border px-3.5 py-2.5 text-sm outline-none focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            maxLength={2000}
          />
        </Field>
        {destructive && (
          <p className="text-xs text-[var(--gc-text-muted)]">
            Lý do và người thao tác sẽ được lưu vĩnh viễn trong lịch sử phiếu.
          </p>
        )}
      </div>
    </Modal>
  );
}

function VoucherHistoryModal({ voucher, onClose }: { voucher: PayoutVoucher; onClose: () => void }) {
  const { data, loading } = useApi<PayoutVoucherEvent[]>(`/api/payout-vouchers/${voucher.id}/history`);
  const events = data ?? [];
  const milestones = [
    { label: "Lập phiếu", at: voucher.createdAt, actor: voucher.createdBy },
    voucher.confirmedAt ? { label: "Người nhận xác nhận", at: voucher.confirmedAt, actor: voucher.confirmedBy } : null,
    voucher.approvedAt ? { label: "Duyệt chi", at: voucher.approvedAt, actor: voucher.approvedBy } : null,
    voucher.rejectedAt ? { label: "Từ chối", at: voucher.rejectedAt, actor: voucher.rejectedBy } : null,
    voucher.cancelledAt ? { label: "Hủy phiếu", at: voucher.cancelledAt, actor: voucher.cancelledBy } : null,
    voucher.completedAt ? { label: "Hoàn tất", at: voucher.completedAt, actor: voucher.completedBy } : null,
  ].filter((m): m is { label: string; at: string; actor: string } => !!m);

  return (
    <Modal open onClose={onClose} title={`Lịch sử ${voucher.voucherNo}`} panel wide>
      <div className="space-y-5">
        <GlassPanel className="p-4">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <p className="font-black text-[var(--gc-text)]">{voucher.employeeName} · {voucher.categoryName}</p>
              <p className="text-sm text-[var(--gc-text-muted)]">{voucher.reason}</p>
            </div>
            <div className="text-right">
              <p className="text-xl font-black text-[var(--gc-text)]">{moneyVnd(voucher.amount)}</p>
              <Badge color={badgeColor(voucher.status)}>{voucherStatusLabel(voucher.status)}</Badge>
            </div>
          </div>
          <div className="mt-4 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {milestones.map((m) => (
              <div key={m.label} className="rounded-xl border border-[var(--glass-border)] p-3 text-xs">
                <p className="font-bold text-[var(--gc-text)]">{m.label}</p>
                <p className="text-[var(--gc-text-muted)]">{dateTime(m.at)}</p>
                <p className="text-[var(--gc-text-soft)]">{m.actor || "Hệ thống"}</p>
              </div>
            ))}
          </div>
        </GlassPanel>

        {loading && !data ? (
          <Spinner />
        ) : events.length === 0 ? (
          <EmptyState icon={<History className="h-8 w-8" />} title="Chưa có sự kiện lịch sử." />
        ) : (
          <ol className="relative ml-3 border-l border-[var(--glass-border)] pl-6">
            {events.map((event) => (
              <li key={event.id} className="relative pb-6 last:pb-0">
                <span className="absolute -left-[1.93rem] top-1.5 h-3 w-3 rounded-full border-2 border-[var(--accent)] bg-white dark:bg-slate-900" />
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <p className="font-black text-[var(--gc-text)]">{voucherEventLabel(event.action)}</p>
                    <p className="text-xs text-[var(--gc-text-muted)]">
                      {event.actorName || event.actor || "Hệ thống"}
                      {event.actorName && event.actor && event.actorName !== event.actor ? ` (${event.actor})` : ""}
                    </p>
                  </div>
                  <time className="text-xs font-semibold text-[var(--gc-text-soft)]">{dateTime(event.occurredAt)}</time>
                </div>
                {(event.beforeStatus || event.afterStatus) && (
                  <div className="mt-2 flex flex-wrap items-center gap-2 text-xs">
                    {event.beforeStatus && <Badge color={badgeColor(event.beforeStatus)}>{voucherStatusLabel(event.beforeStatus)}</Badge>}
                    {event.beforeStatus && event.afterStatus && <span className="text-[var(--gc-text-muted)]">→</span>}
                    {event.afterStatus && <Badge color={badgeColor(event.afterStatus)}>{voucherStatusLabel(event.afterStatus)}</Badge>}
                  </div>
                )}
                {event.note && <p className="mt-2 text-sm text-[var(--gc-text-soft)]">{event.note}</p>}
                {(event.before || event.after) && (
                  <details className="mt-2 text-xs text-[var(--gc-text-muted)]">
                    <summary className="cursor-pointer font-semibold">Dữ liệu trước/sau</summary>
                    <div className="mt-2 grid gap-2 lg:grid-cols-2">
                      <pre className="max-h-48 overflow-auto rounded-xl bg-black/5 p-2.5 dark:bg-white/5">{JSON.stringify(event.before, null, 2)}</pre>
                      <pre className="max-h-48 overflow-auto rounded-xl bg-black/5 p-2.5 dark:bg-white/5">{JSON.stringify(event.after, null, 2)}</pre>
                    </div>
                  </details>
                )}
              </li>
            ))}
          </ol>
        )}
      </div>
    </Modal>
  );
}

/** Mã QR để người nhận quét bằng app KetoanAPK và ký nhận đã cầm tiền. */
function VoucherQrModal({
  voucher,
  onClose,
  onRefreshed,
}: {
  voucher: PayoutVoucher;
  onClose: () => void;
  onRefreshed: (v: PayoutVoucher) => void;
}) {
  const { notify } = useAppNotifications();
  const [secondsLeft, setSecondsLeft] = useState(0);
  const [refreshing, setRefreshing] = useState(false);

  useEffect(() => {
    const tick = () => {
      const ms = voucher.qrExpiresAt ? new Date(voucher.qrExpiresAt).getTime() - Date.now() : 0;
      setSecondsLeft(Math.max(0, Math.floor(ms / 1000)));
    };
    tick();
    const id = setInterval(tick, 1000);
    return () => clearInterval(id);
  }, [voucher.qrExpiresAt]);

  const regenerate = async () => {
    setRefreshing(true);
    try {
      const res = await api.post<{ qrValue: string; qrExpiresAt: string }>(`/api/payout-vouchers/${voucher.id}/qr`, {});
      onRefreshed({ ...voucher, qrValue: res.qrValue, qrExpiresAt: res.qrExpiresAt });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không tạo lại được mã.");
    } finally {
      setRefreshing(false);
    }
  };

  const expired = secondsLeft <= 0;
  const mm = String(Math.floor(secondsLeft / 60)).padStart(2, "0");
  const ss = String(secondsLeft % 60).padStart(2, "0");

  return (
    <Modal open onClose={onClose} title={`Phiếu chi ${voucher.voucherNo}`} panel>
      <div className="space-y-4 text-center">
        <div>
          <p className="text-sm text-[var(--gc-text-soft)]">Chi cho</p>
          <p className="text-lg font-black text-[var(--gc-text)]">{voucher.employeeName}</p>
          <p className="text-[2rem] font-black leading-tight text-[var(--gc-text)]">{moneyVnd(voucher.amount)}</p>
          {voucher.reason && <p className="text-sm text-[var(--gc-text-muted)]">{voucher.reason}</p>}
        </div>

        <div className="flex justify-center">
          {expired || !voucher.qrValue ? (
            <div className="flex h-[240px] w-[240px] flex-col items-center justify-center gap-3 rounded-2xl border border-dashed border-[var(--glass-border)]">
              <TriangleAlert className="h-8 w-8 text-amber-500" />
              <p className="text-sm font-semibold text-[var(--gc-text-soft)]">Mã QR đã hết hạn</p>
              <Button variant="soft" onClick={regenerate} loading={refreshing}>
                <RefreshCw className="h-4 w-4" /> Tạo lại mã
              </Button>
            </div>
          ) : (
            <div className="rounded-2xl bg-white p-4">
              <QRCodeSVG value={voucher.qrValue} size={240} level="M" marginSize={4} />
            </div>
          )}
        </div>

        <p className="text-sm text-[var(--gc-text-soft)]">
          Đưa mã này cho <strong className="text-[var(--gc-text)]">{voucher.employeeName}</strong> quét bằng app
          KetoanAPK (nút quét QR) để ký nhận đã cầm tiền.
          {!expired && (
            <>
              {" "}
              Mã còn hiệu lực <strong className="text-[var(--gc-text)]">{mm}:{ss}</strong>.
            </>
          )}
        </p>
        <p className="text-xs text-[var(--gc-text-muted)]">
          Chỉ sau khi người nhận quét xong, nút “Duyệt chi” mới bấm được.
        </p>
      </div>
    </Modal>
  );
}

/** Quản trị danh mục loại chi (thêm/sửa/ẩn). Loại lõi lương & hoàn tiền phạt không xóa được. */
function CategoriesModal({ onClose }: { onClose: () => void }) {
  const { notify, confirm } = useAppNotifications();
  const { data, loading, reload } = useApi<PayoutCategory[]>("/api/payout-vouchers/categories?all=true");
  const [editing, setEditing] = useState<PayoutCategory | "new" | null>(null);

  const remove = async (c: PayoutCategory) => {
    const ok = await confirm({
      title: "Xóa loại chi?",
      description: `Xóa vĩnh viễn "${c.name}".`,
      confirmLabel: "Xóa",
      tone: "danger",
    });
    if (!ok) return;
    try {
      await api.del(`/api/payout-vouchers/categories/${c.id}`);
      notify.success("Đã xóa.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được.");
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title="Loại chi"
      panel
      footer={
        <div className="flex justify-between gap-2">
          <Button variant="soft" onClick={() => setEditing("new")}>
            <Plus className="h-4 w-4" /> Thêm loại chi
          </Button>
          <Button variant="ghost" onClick={onClose}>
            Đóng
          </Button>
        </div>
      }
    >
      {loading && !data ? (
        <div className="flex justify-center py-6">
          <Spinner />
        </div>
      ) : (
        <div className="space-y-2">
          {(data ?? []).map((c) => (
            <div
              key={c.id}
              className="flex items-center justify-between gap-3 rounded-xl border border-[var(--glass-border)] px-3.5 py-2.5"
            >
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <strong className="text-[var(--gc-text)]">{c.name}</strong>
                  <span className="text-[0.72rem] text-[var(--gc-text-muted)]">{c.code}</span>
                  {c.isSystem && <Badge color="accent">Hệ thống</Badge>}
                  {!c.isActive && <Badge color="muted">Đã tắt</Badge>}
                </div>
                {c.description && <small className="text-[var(--gc-text-muted)]">{c.description}</small>}
              </div>
              <div className="flex shrink-0 gap-1">
                <Button variant="ghost" onClick={() => setEditing(c)}>
                  <Pencil className="h-4 w-4" />
                </Button>
                {!c.isSystem && (
                  <Button variant="ghost" onClick={() => remove(c)}>
                    <Trash2 className="h-4 w-4" />
                  </Button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {editing && (
        <CategoryEditor
          category={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            reload({ silent: true });
            notify.success("Đã lưu loại chi.");
          }}
        />
      )}
    </Modal>
  );
}

function CategoryEditor({
  category,
  onClose,
  onSaved,
}: {
  category: PayoutCategory | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const [name, setName] = useState(category?.name ?? "");
  const [code, setCode] = useState(category?.code ?? "");
  const [description, setDescription] = useState(category?.description ?? "");
  const [isActive, setIsActive] = useState(category?.isActive ?? true);
  const [sortOrder, setSortOrder] = useState(String(category?.sortOrder ?? 100));
  const [saving, setSaving] = useState(false);

  const submit = async () => {
    if (!name.trim()) {
      notify.error("Vui lòng nhập tên loại chi.");
      return;
    }
    setSaving(true);
    try {
      const body = {
        code: code.trim(),
        name: name.trim(),
        description: description.trim(),
        isActive,
        sortOrder: Number(sortOrder) || 100,
      };
      if (category) await api.put(`/api/payout-vouchers/categories/${category.id}`, body);
      else await api.post("/api/payout-vouchers/categories", body);
      onSaved();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={category ? "Sửa loại chi" : "Thêm loại chi"}
      panel
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>
            Hủy
          </Button>
          <Button onClick={submit} loading={saving}>
            Lưu
          </Button>
        </div>
      }
    >
      <div className="space-y-3">
        <Field label="Tên loại chi">
          <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="VD: Sửa chữa xe" />
        </Field>
        {!category && (
          <Field label="Mã (để trống sẽ tự sinh từ tên)">
            <Input value={code} onChange={(e) => setCode(e.target.value)} placeholder="vd: sua-chua-xe" />
          </Field>
        )}
        <Field label="Mô tả">
          <Input value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <Field label="Thứ tự hiển thị">
          <Input type="number" value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} />
        </Field>
        {!category?.isSystem && (
          <label className="flex items-center gap-2 text-sm text-[var(--gc-text-soft)]">
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} className="h-4 w-4" />
            Đang dùng (bỏ chọn để ẩn khỏi danh sách lập phiếu)
          </label>
        )}
      </div>
    </Modal>
  );
}
