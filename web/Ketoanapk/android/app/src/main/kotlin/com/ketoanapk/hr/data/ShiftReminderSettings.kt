package com.ketoanapk.hr.data

import android.content.Context

/** Cài đặt nhắc ca cục bộ; không chứa dữ liệu nhạy cảm và có thể đọc an toàn từ Worker. */
class ShiftReminderSettings(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences("shift_reminders", Context.MODE_PRIVATE)

    var beforeShift: Boolean
        get() = prefs.getBoolean(KEY_BEFORE, true)
        set(value) { prefs.edit().putBoolean(KEY_BEFORE, value).apply() }

    var lateWarning: Boolean
        get() = prefs.getBoolean(KEY_LATE, true)
        set(value) { prefs.edit().putBoolean(KEY_LATE, value).apply() }

    fun markOnce(key: String): Boolean {
        if (prefs.getBoolean("sent:$key", false)) return false
        prefs.edit().putBoolean("sent:$key", true).apply()
        return true
    }

    companion object {
        private const val KEY_BEFORE = "before_shift"
        private const val KEY_LATE = "late_warning"
    }
}
