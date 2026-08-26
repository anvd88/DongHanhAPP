package com.ketoanapk.hr.data

import android.content.Context
import kotlinx.serialization.Serializable

/** Độ dài cố định của mã bảo mật ứng dụng. Máy chủ kiểm lại (AppPinPolicy) chứ không tin số này. */
const val APP_PIN_LENGTH = 6

/**
 * Trạng thái mã bảo mật do MÁY CHỦ trả về.
 *
 * [lockedForSeconds] là SỐ GIÂY CÒN LẠI do máy chủ tính, không phải mốc thời gian tuyệt đối: đồng hồ
 * điện thoại lệch hay bị chỉnh tay cũng không rút ngắn được thời gian khoá.
 */
@Serializable
data class AppPinStatus(
    val hasPin: Boolean = false,
    val lockedForSeconds: Long = 0L,
    val attemptsBeforeLock: Int = 5,
)

@Serializable
data class AppPinSetBody(val pin: String, val currentPin: String? = null)

@Serializable
data class AppPinVerifyBody(val pin: String)

@Serializable
data class AppPinResetBody(val password: String)

/**
 * Kết quả một lần đối chiếu mã bảo mật với máy chủ.
 *
 * MÁY CHỦ QUYẾT ĐỊNH, KHÔNG PHẢI THIẾT BỊ: mã (dạng hash Argon2id), bộ đếm sai và thời gian khoá đều
 * nằm ở bảng `app_pin_codes`. Thiết bị không giữ bản sao nào nên mất máy cũng không ai dò ngoại tuyến
 * được, và xoá dữ liệu app/cài lại app không reset được số lần thử sai.
 */
sealed interface AppPinVerification {
    data object Success : AppPinVerification
    /** Sai mã; còn [attemptsBeforeLock] lần nữa là bị khoá tạm. */
    data class Incorrect(val attemptsBeforeLock: Int) : AppPinVerification
    /** Đang bị khoá thử lại; còn [seconds] giây (theo đồng hồ máy chủ). */
    data class Locked(val seconds: Long) : AppPinVerification
    /** Tài khoản chưa có mã trên máy chủ (ví dụ vừa bị đặt lại ở thiết bị khác) → phải tạo mã mới. */
    data object NotSet : AppPinVerification
}

/**
 * Xoá TÀN DƯ của cách lưu cũ: mã bảo mật từng nằm trong SharedPreferences "ketoanapk_app_security"
 * (hash PBKDF2 + salt + bộ đếm sai). Bản ghi đó không còn được dùng, nhưng để lại trên máy thì vẫn là
 * thứ kẻ lấy được điện thoại/bản sao lưu có thể mang đi dò ngoại tuyến — đúng thứ mà việc chuyển mã
 * lên máy chủ muốn loại bỏ. Gọi một lần lúc mở app; đã xoá rồi thì các lần sau không làm gì thêm.
 */
fun purgeLegacyAppPinStorage(context: Context) {
    val app = context.applicationContext
    runCatching {
        // Xoá nội dung trước rồi mới xoá tệp: kể cả khi deleteSharedPreferences thất bại (tệp đang mở
        // ở tiến trình khác) thì dữ liệu nhạy cảm cũng đã biến mất.
        app.getSharedPreferences(LEGACY_APP_PIN_PREFS, Context.MODE_PRIVATE).edit().clear().commit()
        app.deleteSharedPreferences(LEGACY_APP_PIN_PREFS)
    }
}

private const val LEGACY_APP_PIN_PREFS = "ketoanapk_app_security"

/** Mã hợp lệ về hình thức (đúng 6 chữ số). Máy chủ vẫn kiểm lại trước khi lưu. */
fun isWellFormedAppPin(pin: String): Boolean =
    pin.length == APP_PIN_LENGTH && pin.all { it in '0'..'9' }
