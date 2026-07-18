package com.ketoanapk.hr.data

import android.annotation.SuppressLint
import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec

private const val APP_PIN_LENGTH = 6
private const val APP_PIN_ITERATIONS = 120_000
private const val APP_PIN_KEY_BITS = 256

sealed interface AppPinVerification {
    data object Success : AppPinVerification
    data class Incorrect(val attemptsBeforeLock: Int) : AppPinVerification
    data class Locked(val retryAtMillis: Long) : AppPinVerification
}

@Serializable
private data class StoredAppPin(
    val version: Int = 1,
    val saltBase64: String,
    val hashBase64: String,
    val failedAttempts: Int = 0,
    val lockedUntilMillis: Long = 0L,
)

/**
 * Mã bảo mật 6 số của riêng ứng dụng, tách biệt hoàn toàn với PIN/mật khẩu khóa màn hình.
 *
 * Không bao giờ lưu PIN bản rõ. PIN được kéo giãn bằng PBKDF2-HMAC-SHA256 với salt ngẫu nhiên; toàn
 * bộ bản ghi tiếp tục được mã hóa bằng khóa AES trong Android Keystore trước khi ghi vào vùng riêng
 * của ứng dụng. Bản ghi được tách theo tài khoản để hai tài khoản trên cùng máy không dùng chung PIN.
 */
@SuppressLint("UseKtx") // Cần commit() trả Boolean để lỗi ghi PIN phải fail-closed, không dùng apply().
class AppPinStore(context: Context) {
    private val preferences = context.applicationContext.getSharedPreferences(
        "ketoanapk_app_security",
        Context.MODE_PRIVATE,
    )
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }

    suspend fun hasPin(username: String): Boolean = withContext(Dispatchers.IO) {
        read(username) != null
    }

    suspend fun setPin(username: String, pin: String) = withContext(Dispatchers.IO) {
        require(username.isNotBlank()) { "Không xác định được tài khoản." }
        require(AppPinHasher.isValid(pin)) { "Mã bảo mật phải gồm đúng 6 chữ số." }
        val salt = ByteArray(16).also(SecureRandom()::nextBytes)
        val record = StoredAppPin(
            saltBase64 = Base64.getEncoder().encodeToString(salt),
            hashBase64 = Base64.getEncoder().encodeToString(AppPinHasher.derive(pin, salt)),
        )
        write(username, record)
    }

    suspend fun verify(
        username: String,
        pin: String,
        nowMillis: Long = System.currentTimeMillis(),
    ): AppPinVerification = withContext(Dispatchers.IO) {
        if (!AppPinHasher.isValid(pin)) return@withContext AppPinVerification.Incorrect(5)
        val record = read(username) ?: throw IllegalStateException("Chưa tạo mã bảo mật ứng dụng.")
        if (record.lockedUntilMillis > nowMillis) {
            return@withContext AppPinVerification.Locked(record.lockedUntilMillis)
        }

        val salt = Base64.getDecoder().decode(record.saltBase64)
        val expected = Base64.getDecoder().decode(record.hashBase64)
        val actual = AppPinHasher.derive(pin, salt)
        if (MessageDigest.isEqual(expected, actual)) {
            if (record.failedAttempts != 0 || record.lockedUntilMillis != 0L) {
                write(username, record.copy(failedAttempts = 0, lockedUntilMillis = 0L))
            }
            return@withContext AppPinVerification.Success
        }

        val failures = record.failedAttempts + 1
        val lockMillis = AppPinHasher.lockDurationMillis(failures)
        val lockedUntil = if (lockMillis > 0L) nowMillis + lockMillis else 0L
        write(username, record.copy(failedAttempts = failures, lockedUntilMillis = lockedUntil))
        if (lockedUntil > nowMillis) AppPinVerification.Locked(lockedUntil)
        else AppPinVerification.Incorrect((5 - failures % 5).coerceIn(1, 5))
    }

    /** Chỉ gọi sau khi máy chủ đã xác minh lại mật khẩu tài khoản. */
    suspend fun clear(username: String) = withContext(Dispatchers.IO) {
        if (!preferences.edit().remove(keyFor(username)).commit()) {
            throw IllegalStateException("Không thể đặt lại mã bảo mật trên thiết bị.")
        }
    }

    private fun read(username: String): StoredAppPin? {
        if (username.isBlank()) throw IllegalStateException("Không xác định được tài khoản.")
        val encoded = preferences.getString(keyFor(username), null) ?: return null
        return try {
            val encrypted = Base64.getDecoder().decode(encoded)
            val plain = OfflineCrypto.decrypt(encrypted).toString(Charsets.UTF_8)
            json.decodeFromString<StoredAppPin>(plain)
        } catch (error: Exception) {
            // Fail closed: bản ghi hỏng/Keystore không đọc được không được coi như "chưa có PIN".
            throw IllegalStateException("Không thể đọc mã bảo mật đã lưu. Hãy dùng Quên mã bảo mật.", error)
        }
    }

    private fun write(username: String, record: StoredAppPin) {
        val plain = json.encodeToString(record).toByteArray(Charsets.UTF_8)
        val encoded = Base64.getEncoder().encodeToString(OfflineCrypto.encrypt(plain))
        if (!preferences.edit().putString(keyFor(username), encoded).commit()) {
            throw IllegalStateException("Không thể lưu mã bảo mật trên thiết bị.")
        }
    }

    private fun keyFor(username: String): String {
        val digest = MessageDigest.getInstance("SHA-256")
            .digest(username.trim().lowercase().toByteArray(Charsets.UTF_8))
        return "pin_" + digest.joinToString("") { "%02x".format(it) }
    }
}

/** Phần thuần JVM để kiểm thử được quy tắc PIN, băm và thời gian khóa. */
internal object AppPinHasher {
    fun isValid(pin: String): Boolean = pin.length == APP_PIN_LENGTH && pin.all { it in '0'..'9' }

    fun derive(pin: String, salt: ByteArray): ByteArray {
        val spec = PBEKeySpec(pin.toCharArray(), salt, APP_PIN_ITERATIONS, APP_PIN_KEY_BITS)
        return try {
            SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).encoded
        } finally {
            spec.clearPassword()
        }
    }

    fun lockDurationMillis(failedAttempts: Int): Long = when {
        failedAttempts < 5 || failedAttempts % 5 != 0 -> 0L
        failedAttempts >= 15 -> 30 * 60_000L
        failedAttempts >= 10 -> 5 * 60_000L
        else -> 30_000L
    }
}
