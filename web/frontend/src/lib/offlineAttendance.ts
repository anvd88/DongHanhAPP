// Chấm công NGOẠI TUYẾN: khi mất mạng, các khung ảnh đã chụp được xếp hàng trong IndexedDB kèm
// GIỜ CHẤM THẬT. Khi có mạng lại, tự gửi lên /api/chamcong/cham với occurredAt = giờ chụp để server
// nhận diện khuôn mặt và ghi log đúng thời điểm (không phải giờ đồng bộ). Nhận diện vẫn ở server nên
// khung ảnh gốc được giữ nguyên tới lúc gửi.

import { api, ApiError } from "./api";
import type { ChamCongResult } from "./types";

const DB_NAME = "km_attendance";
const STORE = "queue";
const DB_VERSION = 1;

interface QueueItem {
  id?: number;
  frames: string[];
  capturedAt: string; // ISO UTC — giờ chấm thật
  tryCount: number;
}

let dbPromise: Promise<IDBDatabase> | null = null;

function openDb(): Promise<IDBDatabase> {
  if (dbPromise) return dbPromise;
  dbPromise = new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE, { keyPath: "id", autoIncrement: true });
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
  return dbPromise;
}

function tx<T>(mode: IDBTransactionMode, fn: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  return openDb().then(
    (db) =>
      new Promise<T>((resolve, reject) => {
        const t = db.transaction(STORE, mode);
        const req = fn(t.objectStore(STORE));
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
      }),
  );
}

async function enqueue(frames: string[], capturedAt: string): Promise<void> {
  await tx("readwrite", (s) => s.add({ frames, capturedAt, tryCount: 0 } as QueueItem));
  emitCount();
}

async function allItems(): Promise<QueueItem[]> {
  return tx<QueueItem[]>("readonly", (s) => s.getAll() as IDBRequest<QueueItem[]>);
}

async function removeItem(id: number): Promise<void> {
  await tx("readwrite", (s) => s.delete(id));
}

export async function getOfflineCount(): Promise<number> {
  try {
    return await tx<number>("readonly", (s) => s.count());
  } catch {
    return 0;
  }
}

// ----- Pub/sub cho số bản ghi đang chờ (để hiển thị badge) -----
type CountListener = (count: number) => void;
const listeners = new Set<CountListener>();

export function subscribeOfflineCount(fn: CountListener): () => void {
  listeners.add(fn);
  void getOfflineCount().then(fn);
  return () => listeners.delete(fn);
}

function emitCount() {
  void getOfflineCount().then((n) => listeners.forEach((fn) => fn(n)));
}

/** Lỗi mạng thật (fetch reject) — khác với lỗi HTTP có mã (ApiError). */
function isNetworkError(e: unknown): boolean {
  if (e instanceof ApiError) return false; // server có phản hồi → không phải mất mạng
  return e instanceof TypeError || !navigator.onLine;
}

function offlineResult(capturedAt: string): ChamCongResult {
  return {
    status: "offline",
    matched: false,
    similarity: 0,
    quality: 0,
    occurredAt: capturedAt,
    message: "Đã lưu chấm công ngoại tuyến — sẽ tự đồng bộ khi có mạng.",
  };
}

/**
 * Gửi loạt khung để chấm công. Trực tuyến: POST bình thường. Mất mạng (offline hoặc fetch lỗi):
 * xếp hàng vào IndexedDB kèm giờ hiện tại và trả kết quả trạng thái "offline".
 */
export async function submitCheckInFrames(frames: string[]): Promise<ChamCongResult> {
  const capturedAt = new Date().toISOString();
  if (!navigator.onLine) {
    await enqueue(frames, capturedAt);
    return offlineResult(capturedAt);
  }
  try {
    return await api.post<ChamCongResult>("/api/chamcong/cham", { images: frames });
  } catch (e) {
    if (isNetworkError(e)) {
      await enqueue(frames, capturedAt);
      return offlineResult(capturedAt);
    }
    throw e;
  }
}

export interface SyncSummary {
  synced: number; // bản ghi đã gửi & server xử lý xong
  recognized: number; // trong số đó, nhận diện & ghi công thành công
  remaining: number; // còn tồn lại (vẫn mất mạng)
}

let syncing = false;

/**
 * Đồng bộ hàng đợi ngoại tuyến. Với mỗi bản ghi: gửi kèm occurredAt. Server xử lý xong (2xx) → xóa
 * khỏi hàng đợi. Mất mạng giữa chừng → dừng, giữ lại phần còn lại. Trả về tóm tắt để thông báo.
 */
export async function syncOfflineAttendance(): Promise<SyncSummary> {
  if (syncing || !navigator.onLine) return { synced: 0, recognized: 0, remaining: await getOfflineCount() };
  syncing = true;
  let synced = 0;
  let recognized = 0;
  try {
    const items = await allItems();
    for (const item of items) {
      if (!navigator.onLine) break;
      try {
        const res = await api.post<ChamCongResult>("/api/chamcong/cham", {
          images: item.frames,
          occurredAt: item.capturedAt,
        });
        // Server đã xử lý (dù nhận diện được hay không) → không giữ lại nữa.
        await removeItem(item.id!);
        synced++;
        if (res.matched && res.status === "ok") recognized++;
      } catch (e) {
        if (isNetworkError(e)) break; // mất mạng lại → dừng, thử sau
        // Lỗi phía server không phải mạng (dữ liệu hỏng…) → bỏ để không kẹt hàng đợi mãi.
        await removeItem(item.id!);
        synced++;
      }
    }
  } finally {
    syncing = false;
    emitCount();
  }
  return { synced, recognized, remaining: await getOfflineCount() };
}

let booted = false;
/** Tự đồng bộ khi cửa sổ online trở lại (gọi 1 lần lúc app khởi động). */
export function initOfflineAttendanceSync(onSynced?: (s: SyncSummary) => void) {
  if (booted) return;
  booted = true;
  const run = () => {
    void syncOfflineAttendance().then((s) => {
      if (s.synced > 0) onSynced?.(s);
    });
  };
  window.addEventListener("online", run);
  if (navigator.onLine) run();
}
