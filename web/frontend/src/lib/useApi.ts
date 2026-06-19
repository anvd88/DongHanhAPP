import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "./api";
import { subscribeRealtime } from "./realtime";

/** Hook fetch GET đơn giản với trạng thái loading/error + refetch. */
export function useApi<T>(path: string | null, deps: unknown[] = []) {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(() => {
    if (!path) return;
    setLoading(true);
    setError(null);
    api
      .get<T>(path)
      .then(setData)
      .catch((e) => setError(e instanceof Error ? e.message : "Lỗi tải dữ liệu"))
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [path, ...deps]);

  useEffect(() => reload(), [reload]);

  // Tự làm mới khi backend phát tín hiệu "changed" (gộp nhiều tín hiệu trong 250ms).
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  useEffect(() => {
    if (!path) return;
    const unsub = subscribeRealtime(() => {
      clearTimeout(timer.current);
      timer.current = setTimeout(reload, 250);
    });
    return () => {
      unsub();
      clearTimeout(timer.current);
    };
  }, [path, reload]);

  return { data, loading, error, reload, setData };
}
