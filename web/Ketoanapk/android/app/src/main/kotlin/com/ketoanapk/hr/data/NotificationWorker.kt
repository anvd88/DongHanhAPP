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
        val repo = HrRepository(applicationContext)
        if (!repo.pushNotificationsEnabled()) return Result.success()

        val token = repo.savedToken()
        if (token.isNullOrBlank()) return Result.success()

        val user = runCatching { repo.me() }.getOrNull() ?: return Result.success()
        val month = LocalDate.now().format(DateTimeFormatter.ofPattern("yyyy-MM"))

        val myRequests = runCatching { repo.requests("mine") }.getOrDefault(emptyList())
        val inbox = if (user.isAdmin) runCatching { repo.requests("inbox") }.getOrDefault(emptyList()) else emptyList()
        val penalties = runCatching {
            repo.penalties(if (user.isAdmin) "all" else "mine", if (user.isAdmin) month else null)
        }.getOrDefault(emptyList())

        val center = NotificationCenter(applicationContext)
        val fresh = center.sync(myRequests, inbox, penalties, user.isAdmin)
        fresh.forEach { AppNotifier.show(applicationContext, it) }
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
