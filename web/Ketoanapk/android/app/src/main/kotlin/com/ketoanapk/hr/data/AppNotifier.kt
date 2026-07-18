package com.ketoanapk.hr.data

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.ketoanapk.hr.MainActivity
import com.ketoanapk.hr.R

/** Bắn thông báo lên khay hệ thống của điện thoại (kênh + PendingIntent mở đúng màn hình). */
object AppNotifier {
    const val CHANNEL_ID = "hr_general"
    const val EXTRA_TARGET = "notif_target"
    const val EXTRA_ENTITY_ID = "notif_entity_id"

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
        }
    }

    fun show(context: Context, notification: AppNotification) {
        ensureChannel(context)
        if (!NotificationManagerCompat.from(context).areNotificationsEnabled()) return

        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
            putExtra(EXTRA_TARGET, notification.target)
            putExtra(EXTRA_ENTITY_ID, notification.entityId)
        }
        val pendingIntent = PendingIntent.getActivity(
            context,
            notification.id.hashCode(),
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val builder = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle(notification.title)
            .setContentText(notification.body)
            .setStyle(NotificationCompat.BigTextStyle().bigText(notification.body))
            .setContentIntent(pendingIntent)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)

        try {
            NotificationManagerCompat.from(context).notify(notification.id.hashCode(), builder.build())
        } catch (_: SecurityException) {
            // Người dùng chưa cấp quyền POST_NOTIFICATIONS — bỏ qua an toàn.
        }
    }
}
