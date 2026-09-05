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

@Serializable
data class ChatCacheSnapshot(
    val conversations: List<ChatConversation> = emptyList(),
    val messages: Map<String, List<ChatMessage>> = emptyMap(),
)

/** Cache chat mã hóa bằng Android Keystore, chỉ giữ tối đa 100 tin gần nhất mỗi hội thoại. */
class ChatCacheStore(context: Context) {
    private val file = File(context.filesDir, "chat_cache.bin")
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }
    private val mutex = Mutex()

    suspend fun load(): ChatCacheSnapshot = mutex.withLock { read() }

    suspend fun saveConversations(items: List<ChatConversation>) = mutex.withLock {
        write(read().copy(conversations = items.take(200)))
    }

    suspend fun saveMessages(conversationId: String, items: List<ChatMessage>) = mutex.withLock {
        val snapshot = read()
        val recent = items.distinctBy { it.id }.sortedBy { it.id }.takeLast(100)
        write(snapshot.copy(messages = snapshot.messages + (conversationId to recent)))
    }

    suspend fun clear() = mutex.withLock { withContext(Dispatchers.IO) { file.delete() } }

    private suspend fun read(): ChatCacheSnapshot = withContext(Dispatchers.IO) {
        if (!file.exists()) return@withContext ChatCacheSnapshot()
        runCatching {
            val plain = OfflineCrypto.decrypt(file.readBytes())
            json.decodeFromString<ChatCacheSnapshot>(String(plain, Charsets.UTF_8))
        }.getOrDefault(ChatCacheSnapshot())
    }

    private suspend fun write(snapshot: ChatCacheSnapshot) = withContext(Dispatchers.IO) {
        val bytes = json.encodeToString(snapshot).toByteArray(Charsets.UTF_8)
        val temp = File(file.parentFile, "${file.name}.tmp")
        runCatching {
            temp.writeBytes(OfflineCrypto.encrypt(bytes))
            if (file.exists()) file.delete()
            if (!temp.renameTo(file)) throw java.io.IOException("Không thay được cache chat.")
        }.onFailure { temp.delete() }
    }
}
