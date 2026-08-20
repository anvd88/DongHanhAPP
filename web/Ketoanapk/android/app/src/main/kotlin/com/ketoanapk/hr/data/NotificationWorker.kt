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
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.Duration
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
        )
        val delivered = fresh.filter { it.kind != NotificationKind.Attendance }
            .filter { AppNotifier.show(applicationContext, it, user.username) }
            .map { it.id }
        center.markSystemDelivered(delivered)
        center.deliverPendingSystemAttendance()
        runCatching { notifyShiftIfNeeded(repo, center) }
        return Result.success()
    }

    private suspend fun notifyShiftIfNeeded(repo: HrRepository, center: NotificationCenter) {
        val now = LocalDateTime.now()
        val sheet = repo.myTimesheet(now.toLocalDate().format(DateTimeFormatter.ofPattern("yyyy-MM")))
        val day = sheet.days.firstOrNull { it.date.take(10) == now.toLocalDate().toString() } ?: return
        val start = runCatching { LocalTime.parse(day.shiftStart) }.getOrNull() ?: return
        val shiftAt = LocalDateTime.of(now.toLocalDate(), start)
        val minutes = Duration.between(now, shiftAt).toMinutes()
        val settings = ShiftReminderSettings(applicationContext)

        val notification = when {
            settings.beforeShift && minutes in 0..30 && settings.markOnce("before:${day.date}") ->
                AppNotification("shift:before:${day.date}", NotificationKind.Attendance, "Sắp đến giờ làm",
                    "${day.shiftName.ifBlank { "Ca làm" }} bắt đầu lúc ${day.shiftStart}.", System.currentTimeMillis(), target = "Timesheet")
            settings.lateWarning && minutes in -30L..-1L && day.checkIn.isNullOrBlank() && settings.markOnce("late:${day.date}") ->
                AppNotification("shift:late:${day.date}", NotificationKind.Attendance, "Bạn sắp bị ghi nhận đi muộn",
                    "Ca bắt đầu lúc ${day.shiftStart} nhưng chưa có lượt chấm vào.", System.currentTimeMillis(), target = "Scan")
            else -> null
        } ?: return
        center.ingestFromPush(notification.id, notification.kind, notification.title, notification.body, notification.target)
            ?.let { center.deliverPendingSystemAttendance() }
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
