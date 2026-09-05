import { FileDown, X } from "lucide-react";
import { acceptTransfer, declineTransfer, useIncomingInvites } from "../lib/filetransfer";
import { formatBytes } from "../lib/format";

/**
 * Prompt toàn cục cho các lời mời NHẬN tệp qua LAN. Hiện ở mọi trang (mount trong
 * ChatNotificationProvider) để người nhận luôn thấy và bấm "Đồng ý" thì mới bắt đầu truyền.
 * Nội dung tệp truyền thẳng P2P; server không giữ tệp.
 */
export function FileTransferPrompts() {
  const invites = useIncomingInvites();
  if (invites.length === 0) return null;

  return (
    <div className="km-filetransfer-host" aria-live="polite">
      {invites.map((t) => (
        <div key={t.tid} className="km-filetransfer-prompt">
          <span className="km-filetransfer-icon">
            <FileDown className="h-5 w-5" />
          </span>
          <div className="km-filetransfer-body">
            <span className="km-filetransfer-kicker">@{t.peer} muốn gửi tệp qua LAN</span>
            <span className="km-filetransfer-name" title={t.name}>
              {t.name}
            </span>
            <span className="km-filetransfer-size">{formatBytes(t.size)}</span>
            <div className="km-filetransfer-actions">
              <button type="button" className="km-filetransfer-accept" onClick={() => acceptTransfer(t.tid)}>
                Đồng ý nhận
              </button>
              <button type="button" className="km-filetransfer-decline" onClick={() => declineTransfer(t.tid)}>
                Từ chối
              </button>
            </div>
          </div>
          <button
            type="button"
            className="km-filetransfer-close"
            aria-label="Bỏ qua"
            onClick={() => declineTransfer(t.tid)}
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      ))}
    </div>
  );
}
