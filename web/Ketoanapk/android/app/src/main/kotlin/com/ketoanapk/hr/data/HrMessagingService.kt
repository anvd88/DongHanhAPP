package com.ketoanapk.hr.data

import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking

/**
 * Nhận thông báo đẩy từ Firebase Cloud Messaging.
 *
 * Máy chủ gửi DATA-ONLY (không kèm khối notification) nên [onMessageReceived] LUÔN chạy dù app đang mở,
 * chạy nền hay vừa bị đóng. Nhờ đó ta:
 *  1. Ghi nhận "chữ ký" [notif_id] vào [NotificationCenter] (dùng chung kho với luồng kiểm tra nền) →
 *     lần kiểm tra nền sau sẽ KHÔNG bắn lại thông báo trùng.
 *  2. Tự dựng thông báo lên khay + thêm vào danh sách chuông trong app.
 * Nếu chữ ký đã thấy (đã hiển thị qua luồng khác) thì bỏ qua, không bắn trùng.
 */
class HrMessagingService : FirebaseMessagingService() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onNewToken(token: String) {
        val repo = HrRepository(applicationContext)
        // Đăng ký token BẤT KỂ công tắc thông báo — để server luôn gọi được tới máy này (cuộc gọi cần
        // token dù người dùng tắt thông báo nghiệp vụ). Chưa đăng nhập thì API tự lỗi, bỏ qua an toàn.
        scope.launch { runCatching { repo.registerPushToken(token) } }
    }

    override fun onMessageReceived(message: RemoteMessage) {
        val data = message.data

        // CUỘC GỌI đến/hủy: LUÔN xử lý (đổ chuông toàn màn hình) — KHÔNG phụ thuộc công tắc "thông báo
        // push" của người dùng, vì cuộc gọi quan trọng và phải reo được kể cả khi họ tắt thông báo
        // nghiệp vụ. (Trước đây return sớm ở đây khiến nhiều nhân viên không nhận được cuộc gọi.)
        when (data["type"]) {
            "call_invite" -> { handleCallInvite(data); return }
            "call_cancel" -> { handleCallCancel(data); return }
        }

        val title = data["title"] ?: message.notification?.title ?: "Thông báo"
        val body = data["body"] ?: message.notification?.body ?: ""
        val target = data["notif_target"]?.takeIf { it.isNotBlank() }
        val notifId = data["notif_id"].orEmpty()
        val kind = kindOf(notifId, target)

        // Tắt thông báo chỉ tắt phần HIỂN THỊ, không được tắt tín hiệu đồng bộ dữ liệu. Nếu không,
        // tin chat/voice đã tới FCM nhưng màn đang mở vẫn đứng im khi SignalR vừa reconnect.
        val pushEnabled = runBlocking { HrRepository(applicationContext).pushNotificationsEnabled() }
        if (!pushEnabled) {
            if (kind != NotificationKind.System) AppEvents.signalDataChanged()
            return
        }

        // Chạy đồng bộ để chắc chắn kho "chữ ký" đã lưu trước khi trả về (đảm bảo chống trùng).
        val created = runBlocking {
            NotificationCenter(applicationContext).ingestFromPush(notifId, kind, title, body, target)
        }
        created?.let { AppNotifier.show(applicationContext, it) }

        // Push này báo dữ liệu đổi (đơn duyệt/từ chối, đơn mới chờ duyệt, phạt…) → nếu app đang mở,
        // làm mới NGAY màn đang xem thay vì chờ nhịp poll (đơn từ cập nhật gần như tức thì).
        if (kind != NotificationKind.System) AppEvents.signalDataChanged()
    }

    /** Lời mời gọi đến: dựng phiên "đang đổ chuông" + reo toàn màn hình (nếu app không ở tiền cảnh). */
    private fun handleCallInvite(data: Map<String, String>) {
        val callId = data["call_id"].orEmpty()
        // "caller" = username người gọi (KHÔNG dùng "from" vì đó là khóa bị FCM cấm trong data payload).
        val from = data["caller"].orEmpty()
        if (callId.isBlank() || from.isBlank()) return
        val name = data["caller_name"].orEmpty()
        val media = data["media"].orEmpty()
        CallManager.init(applicationContext) // đảm bảo có context (cold-start từ push)
        // Dựng phiên để lớp phủ CallHost hiện màn nghe máy khi app mở lên (idempotent theo callId).
        CallManager.ingestIncomingFromPush(callId, from, name, media)
        // App đang mở → SignalR + overlay lo hết, khỏi reo trùng bằng thông báo hệ thống.
        if (!AppForeground.isForeground) {
            CallNotifier.showIncoming(applicationContext, callId, from, name, media)
        }
    }

    /** Người gọi hủy trước khi bắt máy → tắt màn đổ chuông + thông báo. */
    private fun handleCallCancel(data: Map<String, String>) {
        data["call_id"]?.takeIf { it.isNotBlank() }?.let { CallManager.cancelIncomingFromPush(it) }
        CallNotifier.dismiss(applicationContext)
    }

    private fun kindOf(notifId: String, target: String?): NotificationKind = when {
        notifId.startsWith("req:") -> NotificationKind.Request
        notifId.startsWith("inbox:") -> NotificationKind.Approval
        notifId.startsWith("pen:") -> NotificationKind.Penalty
        notifId.startsWith("chat:") -> NotificationKind.Chat
        target == "Requests" -> NotificationKind.Request
        target == "Approval" -> NotificationKind.Approval
        target == "Penalty" -> NotificationKind.Penalty
        target == "Chat" -> NotificationKind.Chat
        else -> NotificationKind.System
    }
}
