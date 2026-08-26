import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "./api";
import { subscribeRealtime, type RealtimeScope } from "./realtime";

type ReloadOptions = {
  silent?: boolean;
};

/** Phạm vi realtime mà một path GET quan tâm — để chỉ refetch khi thật sự liên quan. */
function scopesForPath(path: string): RealtimeScope[] {
  if (path.startsWith("/api/chat/conversations/") && path.endsWith("/messages")) return ["chat"];
  if (path.startsWith("/api/chat/conversations") || path.startsWith("/api/chat/contacts"))
    return ["chat", "presence"];
  if (path.startsWith("/api/notifications")) return ["notify"];
  if (path.startsWith("/api/tasks")) return ["tasks", "hr"];
  if (path.startsWith("/api/worklist")) return ["tasks", "hr"];
  if (path.startsWith("/api/cash-collections")) return ["data", "hr"];
  if (path.startsWith("/api/portal")) return ["portal"];
  if (path.startsWith("/api/app-config")) return ["config"];
  if (path.startsWith("/api/audit")) return ["audit"];
  if (path.startsWith("/api/talent")) return ["talent"];
  if (path.startsWith("/api/chamcong/liveness-metrics")) return ["liveness"];
  // Hai nhóm này đọc dữ liệu KHÔNG nằm ở bảng cham_cong_*: cấu hình lưu ở web_system_settings (phát
  // scope 'hr'), còn màn duyệt chấm công ngoại tuyến nằm trong khu nhân sự. Nghe thiếu 'hr' thì chúng
  // đứng im khi người khác vừa sửa — nên giữ cả hai scope.
  if (
    path.startsWith("/api/chamcong/offline") ||
    path.startsWith("/api/chamcong/motion-config")
  )
    return ["attendance", "hr"];
  // Các màn chấm công còn lại đọc thẳng bảng cham_cong_* nên chỉ cần 'attendance' — thay đổi kế toán
  // hay nhân sự không còn làm chúng tải lại vô ích.
  if (path.startsWith("/api/chamcong")) return ["attendance"];
  if (
    path.startsWith("/api/users") ||
    path.startsWith("/api/directory") ||
    path.startsWith("/api/auth/devices")
  )
    return ["presence"];
  if (path.startsWith("/api/feedback")) return ["feedback"];
  if (path.startsWith("/api/releases")) return ["release"];
  if (
    path.startsWith("/api/hr") ||
    path.startsWith("/api/requests") ||
    path.startsWith("/api/shifts") ||
    path.startsWith("/api/timesheet") ||
    path.startsWith("/api/bank-accounts") ||
    path.startsWith("/api/penalt") ||
    path.startsWith("/api/payout-vouchers") ||
    path.startsWith("/api/payroll")
  )
    return ["hr", "data"];
  // Presence đổi mỗi nhịp tim (45 giây/người dùng). Chỉ màn hình thật sự hiển thị online
  // mới nghe scope đó; nếu không, một heartbeat sẽ làm tải lại gần như toàn bộ ứng dụng.
  return ["data"];
}

/** Hook fetch GET đơn giản với trạng thái loading/error + refetch. */
export function useApi<T>(path: string | null, _deps: unknown[] = []) {
  void _deps;
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const dataRef = useRef<T | null>(null);
  const pathRef = useRef<string | null>(null);

  const reload = useCallback((options: ReloadOptions = {}) => {
    if (!path) return;
    const pathChanged = pathRef.current !== path;
    const silent = options.silent || (dataRef.current !== null && !pathChanged);
    if (!silent) {
      setLoading(true);
      setError(null);
    }
    api
      .get<T>(path)
      .then((next) => {
        pathRef.current = path;
        dataRef.current = next;
        setData(next);
        setError(null);
      })
      .catch((e) => {
        if (!silent || dataRef.current === null) {
          setError(e instanceof Error ? e.message : "Lỗi tải dữ liệu");
        }
      })
      .finally(() => {
        if (!silent) setLoading(false);
      });
  }, [path]);

  useEffect(() => reload(), [reload]);

  // Tự làm mới khi backend phát tín hiệu "changed" trong phạm vi liên quan (gộp trong 250ms).
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  useEffect(() => {
    if (!path) return;
    const unsub = subscribeRealtime(() => {
      clearTimeout(timer.current);
      timer.current = setTimeout(() => reload({ silent: true }), 250);
    }, scopesForPath(path));
    return () => {
      unsub();
      clearTimeout(timer.current);
    };
  }, [path, reload]);

  return { data, loading, error, reload, setData };
}
