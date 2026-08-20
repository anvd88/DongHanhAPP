package com.ketoanapk.hr

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import java.util.UUID

/**
 * Cổng riêng cho PendingIntent của notification. Activity này không exported nên ứng dụng khác không
 * thể gọi trực tiếp với extras giả; nó chỉ chuyển tiếp payload đã được Android giao qua PendingIntent.
 */
class NotificationEntryActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val forward = Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_NEW_TASK
            intent.extras?.let(::putExtras)
            putExtra(NotificationLaunchTrust.EXTRA_TRUST_TOKEN, NotificationLaunchTrust.issue())
        }
        startActivity(forward)
        finish()
    }
}

/** Token ngẫu nhiên dùng một lần, chỉ tồn tại trong tiến trình giữa entry activity và MainActivity. */
internal object NotificationLaunchTrust {
    const val EXTRA_TRUST_TOKEN = "notification_launch_trust"
    private const val MAX_OUTSTANDING_TOKENS = 32
    private val outstanding = LinkedHashSet<String>()

    @Synchronized
    fun issue(): String {
        while (outstanding.size >= MAX_OUTSTANDING_TOKENS) {
            outstanding.remove(outstanding.first())
        }
        return UUID.randomUUID().toString().also(outstanding::add)
    }

    @Synchronized
    fun consume(token: String?): Boolean = !token.isNullOrBlank() && outstanding.remove(token)
}
