package com.ketoanapk.hr.data

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.io.File

/**
 * Ảnh chụp dữ liệu Trang chủ + Bảng công của lần chạm máy chủ gần nhất. Lưu để khi người dùng THOÁT
 * HẲN app (tiến trình bị hệ thống thu hồi) rồi mở lại, màn hình hiện NGAY dữ liệu cũ thay vì màn trống
 * + vòng quay tải cho tới khi mạng về. App vẫn làm mới im lặng ở nền ngay sau đó.
 *
 * Chỉ giữ đúng ảnh chụp của MỘT tài khoản (người đăng nhập gần nhất). Đổi tài khoản → ảnh cũ bị coi là
 * không khớp và bỏ qua, nên không rò dữ liệu người này sang người kia.
 */
@Serializable
data class HomeSnapshot(
    val username: String = "",
    val employee: EmployeeDetail? = null,
    val timesheet: Timesheet? = null,
    val requests: List<RequestListItem> = emptyList(),
    val inbox: List<RequestListItem> = emptyList(),
    val penalties: List<Penalty> = emptyList(),
    val salaries: List<SalaryListItem> = emptyList(),
    val requestTypes: List<RequestType> = emptyList(),
    val payslipRequirement: PayslipRequirement = PayslipRequirement(),
)

/**
 * Kho ảnh chụp Trang chủ, mã hoá bằng Android Keystore ([OfflineCrypto]) vì chứa thông tin cá nhân
 * (hồ sơ, lương, đơn từ). Ghi theo kiểu atomic (ghi file tạm rồi đổi tên) để không bao giờ để lại
 * file hỏng một nửa nếu app bị tắt giữa chừng. Cùng khuôn với [ChatCacheStore].
 */
class HomeCacheStore(context: Context) {
    private val file = File(context.filesDir, "home_cache.bin")
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }
    private val mutex = Mutex()

    /** Trả về ảnh chụp CHỈ khi đúng tài khoản đang mở; ngược lại null (đổi tài khoản / chưa có / hỏng). */
    suspend fun load(username: String): HomeSnapshot? = mutex.withLock {
        val snapshot = read() ?: return null
        if (snapshot.username.equals(username, ignoreCase = true)) snapshot else null
    }

    suspend fun save(snapshot: HomeSnapshot) = mutex.withLock { write(snapshot) }

    suspend fun clear() = mutex.withLock { withContext(Dispatchers.IO) { file.delete(); Unit } }

    private suspend fun read(): HomeSnapshot? = withContext(Dispatchers.IO) {
        if (!file.exists()) return@withContext null
        runCatching {
            val plain = OfflineCrypto.decrypt(file.readBytes())
            json.decodeFromString<HomeSnapshot>(String(plain, Charsets.UTF_8))
        }.getOrNull()
    }

    private suspend fun write(snapshot: HomeSnapshot) = withContext(Dispatchers.IO) {
        val bytes = json.encodeToString(snapshot).toByteArray(Charsets.UTF_8)
        val temp = File(file.parentFile, "${file.name}.tmp")
        runCatching {
            temp.writeBytes(OfflineCrypto.encrypt(bytes))
            if (file.exists()) file.delete()
            if (!temp.renameTo(file)) throw java.io.IOException("Không thay được ảnh chụp Trang chủ.")
        }.onFailure { temp.delete() }
        Unit
    }
}
