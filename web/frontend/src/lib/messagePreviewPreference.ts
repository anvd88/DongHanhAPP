const MESSAGE_PREVIEW_PREFIX = "km:chat:message-preview";
const MESSAGE_PREVIEW_EVENT = "km:chat:message-preview-change";

const messagePreviewKey = (userId: string) => `${MESSAGE_PREVIEW_PREFIX}:${userId}`;

export function isMessagePreviewEnabled(userId: string) {
  return localStorage.getItem(messagePreviewKey(userId)) !== "false";
}

export function setMessagePreviewEnabled(userId: string, enabled: boolean) {
  localStorage.setItem(messagePreviewKey(userId), enabled ? "true" : "false");
  window.dispatchEvent(new CustomEvent(MESSAGE_PREVIEW_EVENT, { detail: { userId, enabled } }));
}

export function subscribeMessagePreviewEnabled(userId: string, onChange: () => void) {
  const key = messagePreviewKey(userId);

  const onLocalChange = (event: Event) => {
    const detail = event instanceof CustomEvent ? event.detail : undefined;
    if (!detail || detail.userId === userId) onChange();
  };

  const onStorage = (event: StorageEvent) => {
    if (event.key === key) onChange();
  };

  window.addEventListener(MESSAGE_PREVIEW_EVENT, onLocalChange);
  window.addEventListener("storage", onStorage);
  return () => {
    window.removeEventListener(MESSAGE_PREVIEW_EVENT, onLocalChange);
    window.removeEventListener("storage", onStorage);
  };
}
