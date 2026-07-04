package com.ketoanapk.hr.ui

import java.text.DecimalFormat
import java.text.DecimalFormatSymbols
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale

private val moneyFormat = DecimalFormat("#,###", DecimalFormatSymbols(Locale.US))

/** Định dạng tiền tệ kiểu Việt Nam: 1.250.000 ₫ */
fun formatMoney(value: Double): String {
    if (value == 0.0) return "0 ₫"
    return moneyFormat.format(value).replace(',', '.') + " ₫"
}

/** Đổi số phút thành chuỗi giờ:phút thân thiện. */
fun formatMinutes(minutes: Int): String {
    if (minutes <= 0) return "0 phút"
    val h = minutes / 60
    val m = minutes % 60
    return when {
        h > 0 && m > 0 -> "${h}h ${m}p"
        h > 0 -> "${h}h"
        else -> "${m} phút"
    }
}

/** Định dạng ngày ISO (yyyy-MM-dd hoặc ISO datetime) sang dd/MM/yyyy. */
fun formatIsoDate(value: String?): String {
    if (value.isNullOrBlank()) return "--"
    return runCatching {
        LocalDate.parse(value.take(10)).format(DateTimeFormatter.ofPattern("dd/MM/yyyy"))
    }.getOrElse { value }
}

/** Định dạng datetime ISO sang dd/MM HH:mm. */
fun formatIsoDateTime(value: String?): String {
    if (value.isNullOrBlank()) return "--"
    return runCatching {
        OffsetDateTime.parse(value).format(DateTimeFormatter.ofPattern("dd/MM HH:mm"))
    }.getOrElse { formatIsoDate(value) }
}

/** Lấy chữ cái đầu của tên để hiển thị avatar chữ. */
fun initials(name: String): String {
    val parts = name.trim().split(" ").filter { it.isNotBlank() }
    return when {
        parts.isEmpty() -> "?"
        parts.size == 1 -> parts[0].take(1).uppercase()
        else -> (parts[parts.size - 2].take(1) + parts.last().take(1)).uppercase()
    }
}

fun currentMonthKey(): String =
    LocalDate.now().format(DateTimeFormatter.ofPattern("yyyy-MM"))

fun todayKey(): String =
    LocalDate.now().format(DateTimeFormatter.ISO_LOCAL_DATE)

/** Định dạng giờ ISO (UTC) sang HH:mm theo múi giờ thiết bị — dùng cho "Giờ vào/Giờ ra". */
fun formatIsoTimeLocal(value: String?): String {
    if (value.isNullOrBlank()) return "--"
    return runCatching {
        OffsetDateTime.parse(value)
            .atZoneSameInstant(java.time.ZoneId.systemDefault())
            .format(DateTimeFormatter.ofPattern("HH:mm"))
    }.getOrElse { "--" }
}

/** Thời gian tương đối thân thiện cho thông báo: "Vừa xong", "5 phút trước", "2 giờ trước", ... */
fun relativeTime(epochMillis: Long): String {
    val diff = System.currentTimeMillis() - epochMillis
    if (diff < 0) return "Vừa xong"
    val minutes = diff / 60_000
    val hours = diff / 3_600_000
    val days = diff / 86_400_000
    return when {
        minutes < 1 -> "Vừa xong"
        minutes < 60 -> "$minutes phút trước"
        hours < 24 -> "$hours giờ trước"
        days < 7 -> "$days ngày trước"
        else -> runCatching {
            java.time.Instant.ofEpochMilli(epochMillis)
                .atZone(java.time.ZoneId.systemDefault())
                .format(DateTimeFormatter.ofPattern("dd/MM/yyyy"))
        }.getOrDefault("")
    }
}
