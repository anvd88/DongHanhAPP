package com.ketoanapk.hr.data

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.io.File

/**
 * Hàng đợi chấm công NGOẠI TUYẾN: khi mất điện/mất mạng, các lượt chấm được lưu tạm (loạt ảnh + giờ
 * chấm thật + GPS) vào một file trong bộ nhớ RIÊNG của app (sandbox). Nội dung được MÃ HOÁ AES-256-GCM
 * bằng khóa trong Android Keystore ([OfflineCrypto]). Khi máy chủ trở lại, [HrRepository] tự gửi lên
 * (server tạo bản CHỜ DUYỆT) rồi XÓA khỏi hàng đợi; đồng bộ hết → file bị xoá hẳn.
 */
class OfflineAttendanceStore(context: Context) {
    private val file = File(context.filesDir, "offline_attendance.bin")
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }
    private val mutex = Mutex()

    private suspend fun readAll(): List<OfflineAttendanceItem> = withContext(Dispatchers.IO) {
        if (!file.exists()) return@withContext emptyList()
        runCatching {
            val plain = OfflineCrypto.decrypt(file.readBytes())
            json.decodeFromString<List<OfflineAttendanceItem>>(String(plain, Charsets.UTF_8))
        }.getOrDefault(emptyList())
    }

    private suspend fun writeAll(items: List<OfflineAttendanceItem>) = withContext(Dispatchers.IO) {
        runCatching {
            if (items.isEmpty()) {
                // Đồng bộ hết → xoá hẳn file (không giữ lại ảnh khuôn mặt trên máy).
                file.delete()
            } else {
                val bytes = json.encodeToString<List<OfflineAttendanceItem>>(items).toByteArray(Charsets.UTF_8)
                file.writeBytes(OfflineCrypto.encrypt(bytes))
            }
        }
    }

    suspend fun enqueue(frames: List<String>, occurredAt: String, gpsLat: Double?, gpsLng: Double?) = mutex.withLock {
        val items = readAll().toMutableList()
        items.add(
            OfflineAttendanceItem(
                id = System.currentTimeMillis(),
                frames = frames,
                occurredAt = occurredAt,
                gpsLat = gpsLat,
                gpsLng = gpsLng,
            ),
        )
        writeAll(items)
    }

    suspend fun all(): List<OfflineAttendanceItem> = mutex.withLock { readAll() }

    suspend fun remove(id: Long) = mutex.withLock {
        writeAll(readAll().filterNot { it.id == id })
    }

    suspend fun count(): Int = mutex.withLock { readAll().size }
}
