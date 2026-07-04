package com.ketoanapk.hr.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.first
import java.util.UUID

private val Context.dataStore: DataStore<Preferences> by preferencesDataStore(name = "ketoanapk_session")

/** Lưu token JWT và mã phiên thiết bị (sid) bền vững qua các lần mở app. */
class TokenStore(private val context: Context) {
    private val keyToken = stringPreferencesKey("token")
    private val keySid = stringPreferencesKey("sid")
    private val keyRememberedUser = stringPreferencesKey("remembered_username")
    private val keyPushNotificationsEnabled = booleanPreferencesKey("push_notifications_enabled")

    suspend fun token(): String? = context.dataStore.data.first()[keyToken]

    /** Tên đăng nhập ghi nhớ (mặc định bật, KHÔNG lưu mật khẩu). */
    suspend fun rememberedUsername(): String = context.dataStore.data.first()[keyRememberedUser] ?: ""

    suspend fun saveRememberedUsername(username: String) {
        context.dataStore.edit {
            if (username.isBlank()) it.remove(keyRememberedUser) else it[keyRememberedUser] = username.trim()
        }
    }

    suspend fun saveToken(token: String) {
        context.dataStore.edit { it[keyToken] = token }
    }

    suspend fun pushNotificationsEnabled(): Boolean =
        context.dataStore.data.first()[keyPushNotificationsEnabled] ?: false

    suspend fun setPushNotificationsEnabled(enabled: Boolean) {
        context.dataStore.edit { it[keyPushNotificationsEnabled] = enabled }
    }

    suspend fun clearToken() {
        context.dataStore.edit { it.remove(keyToken) }
    }

    /** Trả về sid ổn định cho thiết bị này, tạo mới nếu chưa có. */
    suspend fun sessionId(): String {
        val existing = context.dataStore.data.first()[keySid]
        if (!existing.isNullOrBlank()) return existing
        val generated = "apk-" + UUID.randomUUID().toString()
        context.dataStore.edit { it[keySid] = generated }
        return generated
    }
}
