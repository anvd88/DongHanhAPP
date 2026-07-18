package com.ketoanapk.hr.data

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.RingtoneManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.ketoanapk.hr.MainActivity
import com.ketoanapk.hr.R

/** Cờ app có đang ở tiền cảnh (foreground) — để FCM biết cuộc gọi đến nên hiện overlay hay thông báo toàn màn hình. */
object AppForeground {
    @Volatile var isForeground = false
}

/**
 * Thông báo CUỘC GỌI ĐẾN kiểu toàn màn hình (full-screen intent) — reo + hiện màn nghe máy kể cả khi
 * máy đang khóa / app đang đóng. Bấm hoặc "Trả lời" mở app tới màn đổ chuông; "Từ chối" cúp ngay
 * (qua [CallActionReceiver]) mà không cần mở app.
 */
object CallNotifier {
    const val CHANNEL_ID = "hr_calls"
    private const val NOTIF_ID = 918273

    const val EXTRA_CALL_ID = "call_id"
    const val EXTRA_CALL_FROM = "call_from"
    const val EXTRA_CALL_NAME = "call_name"
    const val EXTRA_CALL_MEDIA = "call_media"
    const val EXTRA_CALL_ACTION = "call_action" // "answer"

    fun ensureChannel(context: Context) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val manager = context.getSystemService(NotificationManager::class.java) ?: return
        if (manager.getNotificationChannel(CHANNEL_ID) != null) return
        val channel = NotificationChannel(CHANNEL_ID, "Cuộc gọi đến", NotificationManager.IMPORTANCE_HIGH).apply {
            description = "Chuông cuộc gọi thoại/video."
            enableVibration(true)
            vibrationPattern = longArrayOf(0, 700, 800, 700, 800)
            val sound = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE)
            val attrs = AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_NOTIFICATION_RINGTONE)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                .build()
            setSound(sound, attrs)
            setBypassDnd(false)
            lockscreenVisibility = Notification.VISIBILITY_PUBLIC
        }
        manager.createNotificationChannel(channel)
    }

    fun showIncoming(context: Context, callId: String, from: String, name: String, media: String) {
        ensureChannel(context)
        if (!NotificationManagerCompat.from(context).areNotificationsEnabled()) return

        val display = name.ifBlank { from }
        val isVideo = media.equals("video", ignoreCase = true)

        val openIntent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_NEW_TASK
            putExtra(EXTRA_CALL_ID, callId)
            putExtra(EXTRA_CALL_FROM, from)
            putExtra(EXTRA_CALL_NAME, display)
            putExtra(EXTRA_CALL_MEDIA, media)
            putExtra(EXTRA_CALL_ACTION, "answer")
        }
        val openPi = PendingIntent.getActivity(
            context, callId.hashCode(), openIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val declineIntent = Intent(context, CallActionReceiver::class.java).apply {
            action = CallActionReceiver.ACTION_DECLINE
            putExtra(EXTRA_CALL_ID, callId)
        }
        val declinePi = PendingIntent.getBroadcast(
            context, ("d" + callId).hashCode(), declineIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val builder = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle(display)
            .setContentText(if (isVideo) "Cuộc gọi video đến" else "Cuộc gọi thoại đến")
            .setCategory(NotificationCompat.CATEGORY_CALL)
            .setPriority(NotificationCompat.PRIORITY_MAX)
            .setOngoing(true)
            .setAutoCancel(false)
            .setContentIntent(openPi)
            .setFullScreenIntent(openPi, true)
            .addAction(0, "Từ chối", declinePi)
            .addAction(0, "Trả lời", openPi)

        try {
            NotificationManagerCompat.from(context).notify(NOTIF_ID, builder.build())
        } catch (_: SecurityException) {
            // Chưa cấp quyền POST_NOTIFICATIONS — bỏ qua an toàn.
        }
    }

    fun dismiss(context: Context) {
        NotificationManagerCompat.from(context).cancel(NOTIF_ID)
    }
}
