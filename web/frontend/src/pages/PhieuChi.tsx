import { useEffect, useMemo, useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import {
  BanknoteArrowDown,
  CheckCircle2,
  CircleDollarSign,
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
import { useAuth } from "../lib/auth";
import { date, dateTime, moneyVnd } from "../lib/format";
import {
  voucherSourceLabel,
  voucherStatusColor,
  voucherStatusLabel,
  type EmployeeDetail,
  type PayoutCategory,
  type PayoutRefundSource,
  type PayoutSummary,
  type PayoutVoucher,
} from "../lib/hr";
import { isAccountingRole, isAdmin } from "../lib/types";
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

/**
 * Sổ phiếu chi tiền mặt của phòng kế toán. Luồng một phiếu: kế toán lập → người nhận quét QR ký nhận →
 * kế toán "Duyệt chi". Nút duyệt chi CHỈ mở khi phiếu đã được ký nhận — chốt chống gian lận nằm ở đó.
 * Nhân viên thường vào trang này chỉ thấy phiếu của chính mình (server lọc, không phải chỉ ẩn trên UI).
 */
export function PhieuChi() {
  const { user } = useAuth();
  const admin = isAdmin(user);
  const { notify, confirm } = useAppNotifications();
  const { data: me } = useApi<EmployeeDetail>("/api/hr/me");
  // Quyền thật do server chốt; đây chỉ để dựng đúng giao diện (role kế toán + thuộc phòng kế toán).
  const cashier = isAccountingRole(user) && !!me?.isAccounting;
  const canSeeLedger = cashier || admin;

  const [tab, setTab] = useState("queue");
  const [month, setMonth] = useState("");
  const [creating, setCreating] = useState(false);
  const [managingCategories, setManagingCategories] = useState(false);
  const [qrVoucher, setQrVoucher] = useState<PayoutVoucher | null>(null);

  const scope = canSeeLedger ? "all" : "mine";
  const query = `/api/payout-vouchers?scope=${scope}${month ? `&month=${month}` : ""}`;
  const { data, loading, reload } = useApi<PayoutVoucher[]>(query, [query]);
  const { data: summary, reload: reloadSummary } = useApi<PayoutSummary>(
    canSeeLedger ? `/api/payout-vouchers/summary?month=${month || currentMonth()}` : null,
    [canSeeLedger, month],
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
      setQrVoucher(null);
      notify.success(`${fresh.voucherNo} đã được người nhận ký nhận. Bạn có thể duyệt chi.`);
    } else if (fresh.qrValue !== qrVoucher.qrValue) {
      setQrVoucher(fresh);
    }
  }, [vouchers, qrVoucher, notify]);

  const visible = useMemo(() => {
    if (tab === "queue") return vouchers.filter((v) => v.status === "AwaitingScan" || v.status === "Confirmed");
    if (tab === "paid") return vouchers.filter((v) => v.status === "Paid");
    return vouchers;
  }, [vouchers, tab]);

  const approve = async (v: PayoutVoucher) => {
    const ok = await confirm({
      title: "Duyệt chi phiếu này?",
      description: `${v.voucherNo} · ${moneyVnd(v.amount)} cho ${v.employeeName}. Phiếu đã được người nhận ký nhận.`,
      confirmLabel: "Duyệt chi",
      tone: "info",
    });
    if (!ok) return;
    try {
      await api.post(`/api/payout-vouchers/${v.id}/approve`, {});
      notify.success(`Đã duyệt chi ${v.voucherNo}.`);
      refresh();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không duyệt chi được.");
    }
  };

  const cancel = async (v: PayoutVoucher) => {
    const ok = await confirm({
      title: "Hủy phiếu chi?",
      description: `${v.voucherNo} · ${moneyVnd(v.amount)}. Khoản hoàn tiền phạt (nếu có) sẽ quay lại hàng chờ.`,
      confirmLabel: "Hủy phiếu",
      tone: "danger",
    });
    if (!ok) return;
    try {
      await api.post(`/api/payout-vouchers/${v.id}/cancel`, { reason: "" });
      notify.success("Đã hủy phiếu.");
      refresh();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không hủy được.");
    }
  };

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
  const confirmed = vouchers.filter((v) => v.status === "Confirmed").length;

  return (
    <div className="gc-page space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="text-[0.78rem] font-bold uppercase tracking-wide text-[var(--gc-text-soft)]">Phòng kế toán</p>
          <h1 className="text-[1.75rem] font-black text-[var(--gc-text)]">
            {canSeeLedger ? "Phiếu chi tiền mặt" : "Phiếu chi của tôi"}
          </h1>
        </div>
        {cashier && (
          <div className="flex flex-wrap gap-2">
            {admin && (
              <Button variant="ghost" onClick={() => setManagingCategories(true)}>
                <Tags className="h-4 w-4" /> Loại chi
              </Button>
            )}
            <Button onClick={() => setCreating(true)}>
              <Plus className="h-4 w-4" /> Lập phiếu chi
            </Button>
          </div>
        )}
        {admin && !cashier && (
          <Button variant="ghost" onClick={() => setManagingCategories(true)}>
            <Tags className="h-4 w-4" /> Loại chi
          </Button>
        )}
      </div>

      {admin && !cashier && (
        <GlassPanel className="flex items-start gap-3 p-4 text-sm text-[var(--gc-text-soft)]">
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
          <p>
            Bạn đang xem sổ chi với quyền quản trị. Việc lập phiếu và duyệt chi chỉ dành cho tài khoản có role
            kế toán thuộc phòng kế toán — quản trị hệ thống cố ý không được chi tiền.
          </p>
        </GlassPanel>
      )}

      {canSeeLedger && (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard index={0} icon={CircleDollarSign} label="Đã chi trong tháng" value={moneyVnd(totalPaid)} tone="0, 184, 148" />
          <StatCard index={1} icon={Hourglass} label="Đang chờ chi" value={moneyVnd(totalPending)} tone="217, 119, 6" />
          <StatCard index={2} icon={ScanLine} label="Chờ người nhận quét" value={String(awaiting)} tone="88, 112, 152" />
          <StatCard index={3} icon={CheckCircle2} label="Đã ký nhận · chờ duyệt" value={String(confirmed)} tone="59, 130, 246" />
        </div>
      )}

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
            hint={cashier ? "Bấm “Lập phiếu chi” để tạo phiếu mới." : undefined}
          />
        ) : (
          <div className="space-y-2.5">
            {visible.map((v) => (
              <VoucherRow
                key={v.id}
                v={v}
                showEmployee={canSeeLedger}
                cashier={cashier}
                onQr={() => openQr(v)}
                onApprove={() => approve(v)}
                onCancel={() => cancel(v)}
              />
            ))}
          </div>
        )}
      </GlassPanel>

      {canSeeLedger && (summary?.byCategory.length ?? 0) > 0 && (
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
            notify.success(`Đã lập phiếu ${v.voucherNo}. Đưa mã QR cho người nhận quét.`);
            setQrVoucher(v);
          }}
        />
      )}

      {qrVoucher && <VoucherQrModal voucher={qrVoucher} onClose={() => setQrVoucher(null)} onRefreshed={setQrVoucher} />}

      {managingCategories && <CategoriesModal onClose={() => setManagingCategories(false)} />}
    </div>
  );
}

function VoucherRow({
  v,
  showEmployee,
  cashier,
  onQr,
  onApprove,
  onCancel,
}: {
  v: PayoutVoucher;
  showEmployee: boolean;
  cashier: boolean;
  onQr: () => void;
  onApprove: () => void;
  onCancel: () => void;
}) {
  const done = v.status === "Paid";
  const cancelled = v.status === "Cancelled";
  return (
    <article
      className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[var(--glass-border)] px-3.5 py-3"
      style={cancelled ? { opacity: 0.55 } : undefined}
    >
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <strong className="text-[var(--gc-text)]">{showEmployee ? v.employeeName : v.categoryName}</strong>
          <span className="text-[0.76rem] font-semibold text-[var(--gc-text-soft)]">{v.voucherNo}</span>
          <Badge color={badgeColor(v.status)}>{voucherStatusLabel(v.status)}</Badge>
        </div>
        <small className="text-[var(--gc-text-muted)]">
          {showEmployee ? `${v.categoryName} · ` : ""}
          {voucherSourceLabel(v.sourceKind)}
          {v.sourceNo ? ` ${v.sourceNo}` : ""} · {date(v.createdAt)}
          {v.reason ? ` · ${v.reason}` : ""}
        </small>
        {v.status === "Confirmed" && v.confirmedAt && (
          <small className="block text-emerald-600 dark:text-emerald-400">
            Người nhận đã ký nhận lúc {dateTime(v.confirmedAt)}
          </small>
        )}
        {done && v.paidAt && (
          <small className="block text-[var(--gc-text-muted)]">
            Đã chi lúc {dateTime(v.paidAt)} · duyệt bởi {v.approvedBy}
          </small>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <span className="text-[1.05rem] font-black text-[var(--gc-text)]">{moneyVnd(v.amount)}</span>
        {cashier && !done && !cancelled && (
          <>
            {v.status === "AwaitingScan" && (
              <Button variant="soft" onClick={onQr}>
                <QrCode className="h-4 w-4" /> Mã QR
              </Button>
            )}
            <Button
              onClick={onApprove}
              disabled={v.status !== "Confirmed"}
              title={v.status !== "Confirmed" ? "Người nhận chưa quét QR xác nhận đã nhận tiền" : undefined}
            >
              <BanknoteArrowDown className="h-4 w-4" /> Duyệt chi
            </Button>
            <Button variant="ghost" onClick={onCancel}>
              <XCircle className="h-4 w-4" />
            </Button>
          </>
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
            };
      const res = await api.post<{ id: string; voucherNo: string }>("/api/payout-vouchers", body);
      // Lấy lại phiếu vừa lập để có mã QR mà server sinh kèm.
      const list = await api.get<PayoutVoucher[]>("/api/payout-vouchers?scope=all");
      const fresh = list.find((v) => v.id === res.id);
      onCreated(fresh ?? ({ ...(picked as unknown as PayoutVoucher), id: res.id, voucherNo: res.voucherNo } as PayoutVoucher));
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
            Lập phiếu &amp; tạo mã QR
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
