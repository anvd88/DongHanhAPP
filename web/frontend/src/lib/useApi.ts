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
  if (path.startsWith("/api/feedback")) return ["feedback"];
  // Mọi path nghiệp vụ khác: phản ứng với thay đổi dữ liệu & hiện diện, KHÔNG bị chat làm phiền.
  return ["data", "presence"];
}

/** Hook fetch GET đơn giản với trạng thái loading/error + refetch. */
export function useApi<T>(path: string | null, deps: unknown[] = []) {
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [path, ...deps]);

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
