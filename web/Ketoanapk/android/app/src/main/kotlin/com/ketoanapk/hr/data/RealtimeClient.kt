package com.ketoanapk.hr.data

import com.ketoanapk.hr.BuildConfig
import com.microsoft.signalr.HubConnection
import com.microsoft.signalr.HubConnectionBuilder
import com.microsoft.signalr.HubConnectionState
import io.reactivex.rxjava3.core.Single
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking

/**
 * Client SignalR cho app native — kết nối `/hubs/changes` của backend GIỐNG bản web để nhận tín hiệu
 * "changed" và làm mới NGAY (qua [AppEvents]) khi có thay đổi nghiệp vụ (đơn từ được duyệt/từ chối…).
 *
 * Chạy KHI APP ĐANG MỞ (bật ở `onAppResumed`, tắt ở `onAppPaused`) để tiết kiệm pin — lúc app ở nền đã
 * có FCM + WorkManager lo thông báo. Tự kết nối lại khi rớt. Token JWT gửi qua query `access_token`
 * (backend đọc ở `Program.cs` cho đường `/hubs`).
 */
class RealtimeClient(private val tokenStore: TokenStore) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var hub: HubConnection? = null
    private var job: Job? = null
    @Volatile private var wantConnected = false

    @Synchronized
    fun start() {
        if (wantConnected) return
        wantConnected = true
        job = scope.launch { connectLoop() }
    }

    @Synchronized
    fun stop() {
        wantConnected = false
        job?.cancel()
        job = null
        val h = hub
        hub = null
        if (h != null) scope.launch { runCatching { h.stop().blockingAwait() } }
    }

    /** Vòng kết nối bền: giữ kết nối khi còn muốn; rớt/ lỗi thì đợi rồi thử lại. */
    private suspend fun connectLoop() {
        while (wantConnected) {
            val token = runCatching { tokenStore.token() }.getOrNull().orEmpty()
            if (token.isBlank()) { delay(3000); continue }

            val connection = build()
            hub = connection
            val started = runCatching { connection.start().blockingAwait() }.isSuccess
            if (!started) {
                if (!wantConnected) return
                delay(3000)
                continue
            }
            // Đã kết nối → chờ tới khi rớt (poll trạng thái nhẹ), rồi thử lại nếu vẫn muốn.
            while (wantConnected && connection.connectionState == HubConnectionState.CONNECTED) {
                delay(2000)
            }
            runCatching { connection.stop().blockingAwait() }
            if (wantConnected) delay(2000)
        }
    }

    private fun build(): HubConnection {
        val url = BuildConfig.API_BASE_URL.trimEnd('/') + "/hubs/changes"
        val connection = HubConnectionBuilder.create(url)
            .withAccessTokenProvider(Single.defer { Single.just(runBlocking { tokenStore.token() ?: "" }) })
            .build()
        // Backend phát "changed" kèm 1 tham số phạm vi (scope): hr / data / presence / chat / all…
        connection.on(
            "changed",
            { scopeName: String ->
                // Chỉ quan tâm nghiệp vụ HR/đơn từ/dữ liệu chung → báo ViewModel làm mới màn đang xem.
                if (scopeName == "hr" || scopeName == "data" || scopeName == "all") AppEvents.signalDataChanged()
            },
            String::class.java,
        )
        return connection
    }
}
