package com.ketoanapk.hr.data

import android.content.Context
import android.util.Base64
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.first
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.util.UUID

private val Context.dataStore: DataStore<Preferences> by preferencesDataStore(name = "ketoanapk_session")

/**
 * Lưu token JWT và mã phiên thiết bị (sid) bền vững qua các lần mở app.
 *
 * Token JWT được **mã hoá bằng khóa trong Android Keystore** ([OfflineCrypto]) trước khi ghi xuống
 * DataStore (khóa không rời thiết bị, ứng dụng khác đọc file cũng không giải mã được). Bản cài cũ còn
 * token ở dạng thô sẽ được **tự động mã hoá lại** ở lần đọc đầu tiên sau khi cập nhật.
 */
class TokenStore(private val context: Context) {
    private val keyToken = stringPreferencesKey("token")          // legacy (thô) — chỉ để migrate
    private val keyTokenEnc = stringPreferencesKey("token_enc")   // token đã mã hoá (base64)
    private val keySid = stringPreferencesKey("sid")
    private val keyRememberedUser = stringPreferencesKey("remembered_username")
    private val keyPushNotificationsEnabled = booleanPreferencesKey("push_notifications_enabled")
    private val keyUserEnc = stringPreferencesKey("user_enc")     // hồ sơ người dùng đã mã hoá
    private val keyLastOnlineAt = longPreferencesKey("last_online_at")

    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }

    suspend fun token(): String? {
        val prefs = context.dataStore.data.first()
        prefs[keyTokenEnc]?.let { enc ->
            return runCatching { decrypt(enc) }.getOrNull()
        }
        // Migrate: bản cũ còn token thô → mã hoá lại ngay rồi xoá bản thô.
        val legacy = prefs[keyToken]
        if (!legacy.isNullOrBlank()) {
            return try {
                saveToken(legacy)
                legacy
            } catch (_: IllegalStateException) {
                // Fail closed: token thô cũ phải bị xoá ngay cả khi Keystore của thiết bị hỏng.
                clearToken()
                null
            }
        }
        return null
    }

    /** Tên đăng nhập ghi nhớ (mặc định bật, KHÔNG lưu mật khẩu). */
    suspend fun rememberedUsername(): String = context.dataStore.data.first()[keyRememberedUser] ?: ""

    suspend fun saveRememberedUsername(username: String) {
        context.dataStore.edit {
            if (username.isBlank()) it.remove(keyRememberedUser) else it[keyRememberedUser] = username.trim()
        }
    }

    suspend fun saveToken(token: String) {
        val enc = try {
            encrypt(token)
        } catch (error: Exception) {
            clearToken()
            throw IllegalStateException("Không thể bảo vệ phiên đăng nhập bằng Android Keystore.", error)
        }
        context.dataStore.edit {
            it.remove(keyToken) // luôn dọn bản thô cũ; tuyệt đối không có fallback plaintext
            it[keyTokenEnc] = enc
        }
    }

    suspend fun pushNotificationsEnabled(): Boolean =
        context.dataStore.data.first()[keyPushNotificationsEnabled] ?: false

    suspend fun setPushNotificationsEnabled(enabled: Boolean) {
        context.dataStore.edit { it[keyPushNotificationsEnabled] = enabled }
    }

    /**
     * Hồ sơ người dùng của lần chạm máy chủ gần nhất. Mất mạng thì app mở lại bằng bản này thay vì
     * đá người dùng ra màn đăng nhập. Mã hoá như token vì chứa thông tin cá nhân (tên, email).
     */
    suspend fun cachedUser(): HrUser? {
        val enc = context.dataStore.data.first()[keyUserEnc] ?: return null
        return runCatching { json.decodeFromString<HrUser>(decrypt(enc)) }.getOrNull()
    }

    /** Best-effort: Keystore hỏng thì chỉ mất khả năng mở app khi offline, không chặn đăng nhập. */
    suspend fun saveCachedUser(user: HrUser) {
        val enc = runCatching { encrypt(json.encodeToString(user)) }.getOrNull() ?: return
        context.dataStore.edit { it[keyUserEnc] = enc }
    }

    /** Mốc (epoch ms) lần cuối máy chủ xác nhận phiên còn sống; 0 = chưa từng ghi nhận. */
    suspend fun lastOnlineAt(): Long = context.dataStore.data.first()[keyLastOnlineAt] ?: 0L

    suspend fun touchOnline() {
        context.dataStore.edit { it[keyLastOnlineAt] = System.currentTimeMillis() }
    }

    suspend fun clearToken() {
        context.dataStore.edit {
            it.remove(keyToken); it.remove(keyTokenEnc); it.remove(keyUserEnc); it.remove(keyLastOnlineAt)
        }
    }

    /** Trả về sid ổn định cho thiết bị này, tạo mới nếu chưa có. */
    suspend fun sessionId(): String {
        val existing = context.dataStore.data.first()[keySid]
        if (!existing.isNullOrBlank()) return existing
        val generated = "apk-" + UUID.randomUUID().toString()
        context.dataStore.edit { it[keySid] = generated }
        return generated
    }

    private suspend fun sseCursorKey(username: String): Preferences.Key<Long> {
        val identity = username.trim().lowercase() + ":" + sessionId()
        return longPreferencesKey("sse_cursor_" + identity.hashCode().toUInt().toString(16))
    }

    suspend fun sseCursor(username: String): Long =
        context.dataStore.data.first()[sseCursorKey(username)] ?: 0L

    suspend fun saveSseCursor(username: String, cursor: Long) {
        if (cursor < 0) return
        val key = sseCursorKey(username)
        context.dataStore.edit { it[key] = cursor }
    }

    suspend fun clearSseCursor(username: String) {
        val key = sseCursorKey(username)
        context.dataStore.edit { it.remove(key) }
    }

    private fun encrypt(token: String): String =
        Base64.encodeToString(OfflineCrypto.encrypt(token.toByteArray(Charsets.UTF_8)), Base64.NO_WRAP)

    private fun decrypt(enc: String): String =
        String(OfflineCrypto.decrypt(Base64.decode(enc, Base64.NO_WRAP)), Charsets.UTF_8)
}
