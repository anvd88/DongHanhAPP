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
        val tokenStore = TokenStore(applicationContext)
        val identityAtCallback = runBlocking {
            tokenStore.cachedUser()?.username.orEmpty() to tokenStore.token().orEmpty()
        }
        if (identityAtCallback.first.isBlank() || identityAtCallback.second.isBlank()) return
        val repo = HrRepository(applicationContext)
        // Đăng ký token BẤT KỂ công tắc thông báo để máy chủ có thể gửi thông báo nghiệp vụ.
        // Chốt cả username + JWT: callback của A chạy
        // muộn sau khi B đăng nhập không được phép đăng ký token bằng phiên B.
        scope.launch {
            val identityNow = runCatching {
                tokenStore.cachedUser()?.username.orEmpty() to tokenStore.token().orEmpty()
            }.getOrNull() ?: return@launch
            if (
                !identityNow.first.equals(identityAtCallback.first, ignoreCase = true) ||
                identityNow.second != identityAtCallback.second
            ) return@launch
            runCatching { repo.registerPushToken(token) }
        }
    }

    override fun onMessageReceived(message: RemoteMessage) {
        val data = message.data
        // Mọi push theo người nhận phải khớp hồ sơ đã xác thực gần nhất.
        val accountId = runBlocking { TokenStore(applicationContext).cachedUser()?.username }.orEmpty()
        if (accountId.isBlank()) return
        val currentScope = notificationAccountScope(accountId)
        val recipientScope = data["recipient_scope"].orEmpty().trim()

        val title = data["title"] ?: message.notification?.title ?: "Thông báo"
        val body = data["body"] ?: message.notification?.body ?: ""
        val target = data["notif_target"]?.takeIf { it.isNotBlank() }
        val notifId = data["notif_id"].orEmpty()
        val kind = kindOf(notifId, target)
        val isAppUpdate = target == APP_UPDATE_NOTIFICATION_TARGET
        if (!notificationRecipientMatches(kind, currentScope, recipientScope)) return

        // Tắt thông báo chỉ tắt phần HIỂN THỊ, không được tắt tín hiệu đồng bộ dữ liệu.
        val pushEnabled = runBlocking { HrRepository(applicationContext).pushNotificationsEnabled() }
        if (!pushEnabled) {
            // Công tắc push chỉ tắt THÔNG BÁO KHAY. Khi app đang mở, phát hành APK vẫn phải dựng
            // thanh/bảng cập nhật trong app; nếu bỏ sự kiện System ở đây thì người dùng không biết gì.
            if (isAppUpdate) AppEvents.signalDataChanged("release")
            else if (kind != NotificationKind.System) AppEvents.signalDataChanged()
            return
        }

        // Chạy đồng bộ để chắc chắn kho "chữ ký" đã lưu trước khi trả về (đảm bảo chống trùng).
        val center = NotificationCenter(applicationContext, accountId)
        val created = runBlocking { center.ingestFromPush(notifId, kind, title, body, target) }
        created?.let {
            // Khi app đang mở, update đã có banner/sheet nội bộ; không bắn thêm heads-up/tray cho
            // cùng một bản phát hành. Vẫn lưu vào chuông để người dùng xem lại.
            if (kind == NotificationKind.Attendance) {
                runBlocking { center.deliverPendingSystemAttendance() }
            } else if (!isAppUpdate || !AppForeground.isForeground) {
                val delivered = AppNotifier.show(applicationContext, it, accountId)
                if (delivered) runBlocking { center.markSystemDelivered(listOf(it.id)) }
            }
        }

        // Push này báo dữ liệu đổi (đơn duyệt/từ chối, đơn mới chờ duyệt, phạt…) → nếu app đang mở,
        // làm mới NGAY màn đang xem thay vì chờ nhịp poll (đơn từ cập nhật gần như tức thì).
        if (isAppUpdate) AppEvents.signalDataChanged("release")
        else if (kind != NotificationKind.System) AppEvents.signalDataChanged()
    }

    /**
     * Đoán nhóm từ CHỮ KÝ sự kiện. Chữ ký do máy chủ đặt (xem các endpoint nghiệp vụ) nên tiền tố ở
     * đây phải khớp với chúng — nếu không, thông báo vẫn hiện nhưng đeo nhầm icon/màu. Cùng một sự
     * kiện tới bằng đường đồng bộ hộp thư thì được phân loại theo `category` của máy chủ
     * (notificationKindFromCategory), hai đường cho ra cùng một kết quả.
     */
    private fun kindOf(notifId: String, target: String?): NotificationKind = when {
        notifId.startsWith("req:") -> NotificationKind.Request
        notifId.startsWith("inbox:") -> NotificationKind.Approval
        notifId.startsWith("pen:") -> NotificationKind.Penalty
        notifId.startsWith("attendance:") -> NotificationKind.Attendance
        notifId.startsWith("delivery:") -> NotificationKind.Delivery
        notifId.startsWith("cash-collection:") -> NotificationKind.Collection
        notifId.startsWith("document:") -> NotificationKind.Document
        notifId.startsWith("payout:") -> NotificationKind.Payout
        notifId.startsWith("task:") -> NotificationKind.Task
        target == "Requests" -> NotificationKind.Request
        target == "Approval" -> NotificationKind.Approval
        target == "Penalty" -> NotificationKind.Penalty
        target == "CashCollections" -> NotificationKind.Collection
        target == "Payout" -> NotificationKind.Payout
        target == "Tasks" -> NotificationKind.Task
        else -> NotificationKind.System
    }
}
