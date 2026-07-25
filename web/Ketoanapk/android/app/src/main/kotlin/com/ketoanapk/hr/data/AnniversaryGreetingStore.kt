package com.ketoanapk.hr.data

import android.content.Context

/**
 * Nhớ cục bộ những mốc tri ân ĐÃ XEM để mỗi mốc chỉ bật popup một lần trong suốt tuần kỷ niệm
 * (server vẫn trả thư suốt cả tuần nếu người dùng chưa mở app đúng ngày). Khoá gồm cả username để
 * hai tài khoản khác nhau trên cùng máy không "ăn ké" trạng thái đã xem của nhau.
 */
class AnniversaryGreetingStore(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences("anniversary_greetings", Context.MODE_PRIVATE)

    private fun key(username: String, greetingKey: String) = "seen:${username.lowercase()}:$greetingKey"

    fun wasSeen(username: String, greetingKey: String): Boolean =
        prefs.getBoolean(key(username, greetingKey), false)

    fun markSeen(username: String, greetingKey: String) {
        prefs.edit().putBoolean(key(username, greetingKey), true).apply()
    }
}
