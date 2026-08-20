package com.ketoanapk.hr.data

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.ketoanapk.hr.NotificationEntryActivity
import com.ketoanapk.hr.R

/** Bắn thông báo lên khay hệ thống của điện thoại (kênh + PendingIntent mở đúng màn hình). */
object AppNotifier {
    const val CHANNEL_ID = "hr_general"
    const val ATTENDANCE_CHANNEL_ID = "hr_attendance_reminders"
    const val EXTRA_TARGET = "notif_target"
    const val EXTRA_ENTITY_ID = "notif_entity_id"
    const val EXTRA_NOTIFICATION_ID = "notif_id"
    const val EXTRA_ACCOUNT_SCOPE = "notif_account_scope"

    /** Tạo kênh thông báo (bắt buộc từ Android 8). Gọi được nhiều lần, không tạo trùng. */
    fun ensureChannel(context: Context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = context.getSystemService(NotificationManager::class.java) ?: return
            if (manager.getNotificationChannel(CHANNEL_ID) == null) {
                val channel = NotificationChannel(
                    CHANNEL_ID,
                    "Thông báo nhân sự",
                    NotificationManager.IMPORTANCE_HIGH,
                ).apply {
                    description = "Đơn từ, phê duyệt, phạt/kỷ luật và nhắc chấm công."
                    enableVibration(true)
                }
                manager.createNotificationChannel(channel)
            }
            // Kênh riêng để doanh nghiệp/người dùng có thể cấu hình nhắc chấm công độc lập với đơn từ.
            if (manager.getNotificationChannel(ATTENDANCE_CHANNEL_ID) == null) {
                manager.createNotificationChannel(
                    NotificationChannel(
                        ATTENDANCE_CHANNEL_ID,
                        "Nhắc chấm công",
                        NotificationManager.IMPORTANCE_HIGH,
                    ).apply {
                        description = "Nhắc giờ vào, giờ ra và các lượt chấm công còn thiếu."
                        enableVibration(true)
                    },
                )
            }
        }
    }

    /** Trả true chỉ khi notification đã được gửi thành công lên khay hệ thống. */
    fun show(context: Context, notification: AppNotification, accountId: String): Boolean {
        ensureChannel(context)
        if (!NotificationManagerCompat.from(context).areNotificationsEnabled()) return false
        val channelId = if (notification.kind == NotificationKind.Attendance) ATTENDANCE_CHANNEL_ID else CHANNEL_ID
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = context.getSystemService(NotificationManager::class.java)
            if (manager?.getNotificationChannel(channelId)?.importance == NotificationManager.IMPORTANCE_NONE) return false
        }

        val intent = Intent(context, NotificationEntryActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
            putExtra(EXTRA_TARGET, notification.target)
            putExtra(EXTRA_ENTITY_ID, notification.entityId)
            putExtra(EXTRA_NOTIFICATION_ID, notification.id)
            putExtra(EXTRA_ACCOUNT_SCOPE, notificationAccountScope(accountId))
        }
        val pendingIntent = PendingIntent.getActivity(
            context,
            "${notificationAccountScope(accountId)}:${notification.id}".hashCode(),
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val builder = NotificationCompat.Builder(context, channelId)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle(notification.title)
            .setContentText(notification.body)
            .setStyle(NotificationCompat.BigTextStyle().bigText(notification.body))
            .setContentIntent(pendingIntent)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setVisibility(NotificationCompat.VISIBILITY_PRIVATE)

        return try {
            NotificationManagerCompat.from(context).notify(
                notificationAccountScope(accountId),
                notification.id.hashCode(),
                builder.build(),
            )
            true
        } catch (_: SecurityException) {
            // Người dùng chưa cấp quyền POST_NOTIFICATIONS — bỏ qua an toàn.
            false
        }
    }

    fun cancel(context: Context, notification: AppNotification, accountId: String) {
        NotificationManagerCompat.from(context).cancel(
            notificationAccountScope(accountId),
            notification.id.hashCode(),
        )
    }
}
