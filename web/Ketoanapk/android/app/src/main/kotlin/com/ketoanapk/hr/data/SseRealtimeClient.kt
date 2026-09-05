package com.ketoanapk.hr.data

import com.ketoanapk.hr.BuildConfig
import com.ketoanapk.hr.network.ApiClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import okhttp3.OkHttpClient
import okhttp3.Call
import okhttp3.Request
import java.io.BufferedReader
import java.util.concurrent.TimeUnit
import kotlin.math.min
import kotlin.random.Random

internal data class SseFrame(val id: Long?, val event: String, val data: String)

internal suspend fun parseSseFrames(
    reader: BufferedReader,
    shouldContinue: () -> Boolean = { true },
    onFrame: suspend (SseFrame) -> Unit,
) {
    var id: Long? = null
    var event = "message"
    val data = StringBuilder()
    while (shouldContinue()) {
        val line = reader.readLine() ?: break
        if (!shouldContinue()) return
        if (line.isEmpty()) {
            if (data.isNotEmpty()) onFrame(SseFrame(id, event, data.toString()))
            id = null; event = "message"; data.clear()
            continue
        }
        if (line.startsWith(":")) continue
        val colon = line.indexOf(':')
        val field = if (colon < 0) line else line.substring(0, colon)
        val value = if (colon < 0) "" else line.substring(colon + 1).removePrefix(" ")
        when (field) {
            "id" -> id = value.toLongOrNull()
            "event" -> event = value
            "data" -> { if (data.isNotEmpty()) data.append('\n'); data.append(value) }
        }
    }
}

/**
 * URL của luồng SSE. Mốc 0 nghĩa là MÁY NÀY CHƯA TỪNG NHẬN GÌ (mới cài, vừa đăng nhập lại) — lúc đó
 * KHÔNG được gửi con trỏ: máy chủ sẽ hiểu là "phát lại từ dòng số 1" và dội về tới 48 giờ sự kiện cũ,
 * mỗi sự kiện là một lệnh tải lại màn hình. Không gửi thì máy chủ trả đúng một khung resync.required
 * rồi chỉ đẩy cái mới.
 */
internal fun sseStreamUrl(baseUrl: String, cursor: Long): String {
    val stream = baseUrl.trimEnd('/') + "/api/realtime/stream"
    return if (cursor > 0) "$stream?after=$cursor" else stream
}

internal fun sseReconnectDelayMs(attempt: Int, jitterFraction: Double): Long {
    val base = min(30_000L, 1_000L shl min(attempt.coerceAtLeast(0), 5))
    return base + (base * jitterFraction.coerceIn(0.0, 1.0 / 3.0)).toLong()
}

/** Foreground-only business realtime over authenticated SSE. FCM remains the background channel. */
class SseRealtimeClient(private val tokenStore: TokenStore) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val client = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .retryOnConnectionFailure(true)
        .build()
    @Volatile private var connected = false
    @Volatile private var epoch = 0L
    private var job: Job? = null
    private var activeUsername: String? = null
    @Volatile private var activeCall: Call? = null

    fun isConnected(): Boolean = connected

    @Synchronized fun start(username: String) {
        if (job?.isActive == true && activeUsername.equals(username, ignoreCase = true)) return
        stop()
        activeUsername = username
        val currentEpoch = ++epoch
        job = scope.launch { reconnectLoop(username, currentEpoch) }
    }

    @Synchronized fun stop() {
        epoch += 1
        connected = false
        activeCall?.cancel()
        activeCall = null
        job?.cancel()
        job = null
    }

    fun clearCursor(username: String) {
        scope.launch { tokenStore.clearSseCursor(username) }
    }

    private suspend fun reconnectLoop(username: String, currentEpoch: Long) {
        var attempt = 0
        while (scope.isActive && epoch == currentEpoch) {
            val token = tokenStore.token().orEmpty()
            if (token.isBlank()) { delay(1_000); continue }
            val cursor = tokenStore.sseCursor(username)
            val url = sseStreamUrl(BuildConfig.API_BASE_URL, cursor)
            val request = Request.Builder().url(url)
                .header("Authorization", "Bearer $token")
                .header("Accept", "text/event-stream")
                .header("Cache-Control", "no-cache")
                .apply { if (cursor > 0) header("Last-Event-ID", cursor.toString()) }
                .build()
            try {
                val call = client.newCall(request)
                activeCall = call
                call.execute().use { response ->
                    if (response.code == 401) {
                        AppEvents.signalForceLogout("Phiên đăng nhập đã hết hạn hoặc bị thu hồi.")
                        return
                    }
                    if (!response.isSuccessful || response.body == null)
                        error("SSE HTTP ${response.code}")
                    connected = true
                    attempt = 0
                    parse(response.body!!.charStream().buffered(), username, currentEpoch)
                }
            } catch (_: Exception) {
                // Durable replay resumes from the last committed cursor; no event is dropped here.
            } finally {
                activeCall = null
                connected = false
            }
            if (epoch != currentEpoch) return
            delay(sseReconnectDelayMs(attempt, Random.nextDouble(0.0, 1.0 / 3.0)))
            attempt += 1
        }
    }

    private suspend fun parse(reader: BufferedReader, username: String, currentEpoch: Long) {
        parseSseFrames(reader, shouldContinue = { epoch == currentEpoch }) { frame ->
            dispatch(username, frame.id, frame.event, frame.data)
        }
    }

    private suspend fun dispatch(username: String, id: Long?, event: String, data: String) {
        if (id != null) tokenStore.saveSseCursor(username, id)
        val scopeName = runCatching {
            val obj = ApiClient.json.parseToJsonElement(data) as? JsonObject
            (obj?.get("scope") as? JsonPrimitive)?.content ?: "all"
        }.getOrDefault("all")
        when (event) {
            "invalidated", "presence.changed" -> AppEvents.signalDataChanged(scopeName)
            "access.changed" -> AppEvents.signalDataChanged("access")
            "session.revoked" -> AppEvents.signalForceLogout("Phiên đăng nhập đã bị thu hồi.")
            "feedback.resolved" -> AppEvents.signalDataChanged("feedback")
            "resync.required" -> AppEvents.signalDataChanged("all")
        }
    }
}
