package com.ketoanapk.hr.data

import java.time.Instant
import java.time.ZoneId
import java.time.ZonedDateTime
import java.util.concurrent.atomic.AtomicLong

/**
 * Đồng hồ MÁY CHỦ: giữ độ lệch (offset) giữa giờ máy chủ và giờ máy điện thoại. Độ lệch được cập nhật
 * tự động từ header "Date" của MỌI phản hồi HTTP (xem [com.ketoanapk.hr.network.ApiClient]), nên các
 * tính năng hiển thị theo giờ (vd lời chào buổi sáng/chiều/tối) dùng ĐÚNG giờ máy chủ, không lệ thuộc
 * đồng hồ máy có thể bị chỉnh sai hoặc gian lận.
 *
 * Chưa có phản hồi nào (mới mở app, đang offline) thì [isSynced] = false và tạm dùng giờ máy.
 */
object ServerClock {
    /** Múi giờ Việt Nam — luôn quy giờ máy chủ về đây bất kể máy đặt múi giờ nào. */
    private val ZONE_VN: ZoneId = ZoneId.of("Asia/Ho_Chi_Minh")

    private val offsetMs = AtomicLong(0L)

    @Volatile
    var isSynced: Boolean = false
        private set

    /** Cập nhật độ lệch từ mốc thời gian máy chủ (epoch millis) đọc ở header Date của phản hồi. */
    fun sync(serverEpochMs: Long) {
        offsetMs.set(serverEpochMs - System.currentTimeMillis())
        isSynced = true
    }

    /** Giờ máy chủ hiện tại (epoch millis) = giờ máy + độ lệch đã đo. */
    fun nowMillis(): Long = System.currentTimeMillis() + offsetMs.get()

    /** Giờ máy chủ hiện tại theo múi giờ Việt Nam. */
    fun nowVietnam(): ZonedDateTime = Instant.ofEpochMilli(nowMillis()).atZone(ZONE_VN)
}
