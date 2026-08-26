package com.ketoanapk.hr.data

import android.content.Context
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.concurrent.TimeUnit

/**
 * Định kỳ (nền) hỏi máy chủ và bắn thông báo hệ thống khi có sự kiện mới, kể cả khi app đã đóng.
 * Đây là cơ chế "đẩy" thực dụng khi chưa gắn Firebase Cloud Messaging (xem README).
 */
class NotificationWorker(
    appContext: Context,
    params: WorkerParameters,
) : CoroutineWorker(appContext, params) {

    override suspend fun doWork(): Result {
        // background = true: vòng poll này KHÔNG được tính là "người dùng đang dùng app", nếu không
        // phiên sẽ luôn tươi và quy tắc hết hạn sau SESSION_IDLE_DAYS ngày không mở app vô tác dụng.
        val repo = HrRepository(applicationContext, background = true)
        if (!repo.pushNotificationsEnabled()) return Result.success()

        val token = repo.savedToken()
        if (token.isNullOrBlank()) return Result.success()

        val user = runCatching { repo.me() }.getOrNull() ?: return Result.success()
        // Lấy ngày Việt Nam theo đồng hồ đã đồng bộ từ header Date của server, tránh máy người dùng
        // chỉnh sai ngày/giờ làm nhắc nhầm một ngày.
        val nowVietnam = ServerClock.nowVietnam()
        val today = nowVietnam.toLocalDate()
        val monthFormatter = DateTimeFormatter.ofPattern("yyyy-MM")
        val month = today.format(monthFormatter)
        // Ngày hôm qua có thể nằm ở tháng trước (ví dụ chạy ngày 01/09 để kiểm tra 31/08).

        val myRequests = runCatching { repo.requests("mine") }.getOrDefault(emptyList())
        val canApproveRequests = user.can(AppPermissions.RequestsApprove)
        val canManagePenalties = user.can(AppPermissions.PenaltyManage)
        val inbox = if (canApproveRequests) runCatching { repo.requests("inbox") }.getOrDefault(emptyList()) else emptyList()
        val penalties = runCatching {
            repo.penalties(if (canManagePenalties) "all" else "mine", if (canManagePenalties) month else null)
        }.getOrDefault(emptyList())
        // Catch-up bền qua mất mạng/Doze: lấy đủ các tháng phủ cửa sổ lookback, không chỉ "hôm qua".
        val attendanceSheets = missedCheckoutMonthKeys(today).mapNotNull { reminderMonth ->
            runCatching { repo.myTimesheet(reminderMonth) }.getOrNull()
        }

        val center = NotificationCenter(applicationContext, user.username)
        val fresh = center.sync(
            myRequests,
            inbox,
            penalties,
            canManagePenalties,
            attendanceSheets = attendanceSheets,
            nowVietnam = nowVietnam,
        ) +
            // Kể cả khi app đóng: vòng poll nền kéo hộp thư máy chủ về nên thông báo giao hàng/thu
            // tiền vẫn lên khay dù máy chưa nhận được gói FCM nào.
            center.ingestFromServer(repo.notificationFeed())
        val delivered = fresh.filter { it.kind != NotificationKind.Attendance }
            .filter { AppNotifier.show(applicationContext, it, user.username) }
            .map { it.id }
        center.markSystemDelivered(delivered)
        center.deliverPendingSystemAttendance()
        // Nhắc ca làm (trước giờ vào / sắp trễ) đã BỎ HẲN theo yêu cầu người dùng 2026-08-26: ca làm
        // đã hiện sẵn trên bảng công, thêm hai thông báo mỗi ngày chỉ làm phiền chứ không giúp gì.
        return Result.success()
    }

    companion object {
        private const val WORK_NAME = "hr-notification-poll"

        /** Bật kiểm tra nền định kỳ (~15 phút, chỉ khi có mạng). Gọi sau khi đăng nhập. */
        fun schedule(context: Context) {
            val request = PeriodicWorkRequestBuilder<NotificationWorker>(15, TimeUnit.MINUTES)
                .setConstraints(
                    Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build(),
                )
                .build()
            WorkManager.getInstance(context)
                .enqueueUniquePeriodicWork(WORK_NAME, ExistingPeriodicWorkPolicy.UPDATE, request)
        }

        /** Tắt khi đăng xuất. */
        fun cancel(context: Context) {
            WorkManager.getInstance(context).cancelUniqueWork(WORK_NAME)
        }
    }
}
