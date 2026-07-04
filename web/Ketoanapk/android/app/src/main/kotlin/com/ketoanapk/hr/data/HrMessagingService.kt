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
        scope.launch {
            if (repo.pushNotificationsEnabled()) {
                runCatching { repo.registerPushToken(token) }
            }
        }
    }

    override fun onMessageReceived(message: RemoteMessage) {
        val pushEnabled = runBlocking { HrRepository(applicationContext).pushNotificationsEnabled() }
        if (!pushEnabled) return

        val data = message.data
        val title = data["title"] ?: message.notification?.title ?: "Thông báo"
        val body = data["body"] ?: message.notification?.body ?: ""
        val target = data["notif_target"]?.takeIf { it.isNotBlank() }
        val notifId = data["notif_id"].orEmpty()
        val kind = kindOf(notifId, target)

        // Chạy đồng bộ để chắc chắn kho "chữ ký" đã lưu trước khi trả về (đảm bảo chống trùng).
        val created = runBlocking {
            NotificationCenter(applicationContext).ingestFromPush(notifId, kind, title, body, target)
        }
        created?.let { AppNotifier.show(applicationContext, it) }
    }

    private fun kindOf(notifId: String, target: String?): NotificationKind = when {
        notifId.startsWith("req:") -> NotificationKind.Request
        notifId.startsWith("inbox:") -> NotificationKind.Approval
        notifId.startsWith("pen:") -> NotificationKind.Penalty
        target == "Requests" -> NotificationKind.Request
        target == "Approval" -> NotificationKind.Approval
        target == "Penalty" -> NotificationKind.Penalty
        else -> NotificationKind.System
    }
}
