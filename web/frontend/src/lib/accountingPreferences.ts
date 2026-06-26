const KEEP_CREATE_OPEN_PREFIX = "km:accounting:keep-create-open";
const KEEP_CREATE_OPEN_EVENT = "km:accounting:keep-create-open-change";

const keepCreateOpenKey = (userId: string) => `${KEEP_CREATE_OPEN_PREFIX}:${userId}`;

export function isKeepCreateVoucherOpenEnabled(userId: string) {
  return localStorage.getItem(keepCreateOpenKey(userId)) === "true";
}

export function setKeepCreateVoucherOpenEnabled(userId: string, enabled: boolean) {
  localStorage.setItem(keepCreateOpenKey(userId), enabled ? "true" : "false");
  window.dispatchEvent(new CustomEvent(KEEP_CREATE_OPEN_EVENT, { detail: { userId, enabled } }));
}

export function subscribeKeepCreateVoucherOpenEnabled(userId: string, onChange: () => void) {
  const key = keepCreateOpenKey(userId);

  const onLocalChange = (event: Event) => {
    const detail = event instanceof CustomEvent ? event.detail : undefined;
    if (!detail || detail.userId === userId) onChange();
  };

  const onStorage = (event: StorageEvent) => {
    if (event.key === key) onChange();
  };

  window.addEventListener(KEEP_CREATE_OPEN_EVENT, onLocalChange);
  window.addEventListener("storage", onStorage);
  return () => {
    window.removeEventListener(KEEP_CREATE_OPEN_EVENT, onLocalChange);
    window.removeEventListener("storage", onStorage);
  };
}
