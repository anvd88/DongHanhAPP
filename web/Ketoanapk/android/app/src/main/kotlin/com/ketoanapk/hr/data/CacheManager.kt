package com.ketoanapk.hr.data

import android.content.Context
import android.text.format.Formatter
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Dọn dẹp bộ nhớ tạm của ứng dụng theo yêu cầu người dùng (màn "Bộ nhớ & dữ liệu tạm" trong Cài đặt).
 *
 * ĐO & DỌN:
 *  - Toàn bộ `cacheDir` (và `externalCacheDir` nếu có): gói cập nhật APK đã tải sẵn, ảnh/PDF/âm thanh
 *    tạm (chat, phiếu lương, chân dung) — đều tạo lại được khi cần.
 *  - Hai file cache có tên trong `filesDir`: `chat_cache.bin` (lịch sử chat ngoại tuyến) và
 *    `home_cache.bin` (ảnh chụp Trang chủ để mở app nhanh) — sẽ tự dựng lại ở lần đồng bộ kế tiếp.
 *
 * TUYỆT ĐỐI KHÔNG đụng dữ liệu người dùng KHÔNG phải cache: phiên đăng nhập (DataStore), đơn nháp chưa
 * gửi (`request_drafts.bin`), hàng đợi chấm công ngoại tuyến chưa đồng bộ (`offline_attendance.bin`),
 * mã PIN ứng dụng và thông báo đã lưu. Mất những thứ này là mất dữ liệu thật, không phải dọn cache.
 */
object CacheManager {

    /** Các file cache nằm trong filesDir (ngoài cacheDir) cần dọn kèm — nhưng không phải dữ liệu người dùng. */
    private fun namedCacheFiles(context: Context): List<File> = listOf(
        File(context.filesDir, "chat_cache.bin"),
        File(context.filesDir, "home_cache.bin"),
    )

    /** Tổng dung lượng cache có thể dọn, tính bằng byte. */
    suspend fun sizeBytes(context: Context): Long = withContext(Dispatchers.IO) {
        var total = dirSize(context.cacheDir)
        total += dirSize(context.externalCacheDir)
        namedCacheFiles(context).forEach { if (it.isFile) total += it.length() }
        total
    }

    /** Xoá sạch cache. Trả về số byte đã giải phóng (ước lượng: dung lượng trước khi xoá). */
    suspend fun clear(context: Context): Long = withContext(Dispatchers.IO) {
        val before = runCatching { sizeBytesBlocking(context) }.getOrDefault(0L)
        deleteContents(context.cacheDir)
        context.externalCacheDir?.let { deleteContents(it) }
        namedCacheFiles(context).forEach { runCatching { it.delete() } }
        before
    }

    /** Chuỗi dung lượng dễ đọc theo chuẩn hệ thống, ví dụ "12 MB". */
    fun format(context: Context, bytes: Long): String = Formatter.formatShortFileSize(context, bytes)

    private fun sizeBytesBlocking(context: Context): Long {
        var total = dirSize(context.cacheDir)
        total += dirSize(context.externalCacheDir)
        namedCacheFiles(context).forEach { if (it.isFile) total += it.length() }
        return total
    }

    private fun dirSize(dir: File?): Long {
        if (dir == null || !dir.exists()) return 0L
        return runCatching {
            dir.walkBottomUp().filter { it.isFile }.sumOf { it.length() }
        }.getOrDefault(0L)
    }

    /** Xoá nội dung bên trong thư mục nhưng GIỮ lại chính thư mục đó (cacheDir phải tồn tại). */
    private fun deleteContents(dir: File) {
        if (!dir.exists()) return
        dir.listFiles()?.forEach { child -> runCatching { child.deleteRecursively() } }
    }
}
