package com.ketoanapk.hr.data

/** Trạng thái vòng đời tiến trình dùng để tránh hiện trùng thông báo hệ thống khi app đang mở. */
object AppForeground {
    @Volatile
    var isForeground: Boolean = false
}
