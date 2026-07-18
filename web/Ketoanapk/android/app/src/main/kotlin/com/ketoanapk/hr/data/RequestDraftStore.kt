package com.ketoanapk.hr.data

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.io.File

/** Nháp đơn tự động, mã hóa AES-GCM bằng Android Keystore. */
class RequestDraftStore(context: Context) {
    private val file = File(context.filesDir, "request_drafts.bin")
    private val json = Json { ignoreUnknownKeys = true }

    suspend fun load(type: String): Map<String, String> = withContext(Dispatchers.IO) {
        runCatching {
            if (!file.exists()) return@runCatching emptyMap()
            val all = json.decodeFromString<Map<String, Map<String, String>>>(String(OfflineCrypto.decrypt(file.readBytes())))
            all[type].orEmpty()
        }.getOrDefault(emptyMap())
    }

    suspend fun save(type: String, values: Map<String, String>) = withContext(Dispatchers.IO) {
        val all = runCatching {
            if (!file.exists()) emptyMap() else json.decodeFromString<Map<String, Map<String, String>>>(String(OfflineCrypto.decrypt(file.readBytes())))
        }.getOrDefault(emptyMap()).toMutableMap()
        if (values.values.any { it.isNotBlank() }) all[type] = values else all.remove(type)
        if (all.isEmpty()) file.delete() else file.writeBytes(OfflineCrypto.encrypt(json.encodeToString(all).toByteArray()))
    }

    suspend fun clear(type: String) = save(type, emptyMap())
}
