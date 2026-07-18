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
    private companion object {
        val RefreshScopes = setOf("hr", "data", "chat", "tasks", "portal", "config", "audit", "talent", "all")
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    @Volatile private var hub: HubConnection? = null
    private var job: Job? = null
    @Volatile private var wantConnected = false
    @Volatile private var selfUsername: String = ""
    fun isConnected(): Boolean = hub?.connectionState == HubConnectionState.CONNECTED

    @Synchronized
    fun start(selfUsername: String) {
        this.selfUsername = selfUsername
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
        CallManager.unbindSignaling()
        if (h != null) scope.launch { runCatching { h.stop().blockingAwait() } }
    }

    /**
     * Gửi một gói tín hiệu WebRTC (bắt tay cuộc gọi) tới đúng MỘT username qua hub `Relay`.
     * Backend chỉ chuyển tiếp cho [toUser] đang đăng nhập — kẻ lạ không chen được. Media của cuộc
     * gọi KHÔNG đi qua đây (mã hóa DTLS-SRTP, truyền thẳng P2P). Chạy nền để không chặn UI.
     */
    fun sendCallSignal(toUser: String, payload: String) {
        val h = hub ?: return
        if (h.connectionState != HubConnectionState.CONNECTED) return
        scope.launch { runCatching { h.send("Relay", toUser, payload) } }
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
            // Đã kết nối → cắm kênh gửi tín hiệu cuộc gọi cho CallManager (nhận qua listener "signal").
            CallManager.bindSignaling(selfUsername) { to, payload -> sendCallSignal(to, payload) }
            // Vừa (kết nối lại) WS → đồng bộ MỘT nhịp để bắt các thay đổi xảy ra lúc đang mất kết nối
            // (thay cho vòng poll định kỳ đã tắt khi WS khỏe). ViewModel nghe qua AppEvents.dataChanged.
            AppEvents.signalDataChanged("all")
            // Đã kết nối → chờ tới khi rớt (poll trạng thái nhẹ), rồi thử lại nếu vẫn muốn.
            while (wantConnected && connection.connectionState == HubConnectionState.CONNECTED) {
                delay(2000)
            }
            CallManager.unbindSignaling()
            runCatching { connection.stop().blockingAwait() }
            if (wantConnected) delay(2000)
        }
    }

    private fun build(): HubConnection {
        // Gắn token vào CẢ query "access_token" trên URL LẪN accessTokenProvider (header). Lý do: qua
        // reverse proxy/Cloudflare Tunnel, header Authorization có thể bị lược trên bước nâng cấp
        // WebSocket → hub sẽ kết nối ẨN DANH (UserIdentifier null) → Relay bị bỏ → KHÔNG nhận được cuộc
        // gọi. Backend đọc "access_token" từ query cho đường /hubs (Program.cs OnMessageReceived), nên
        // đưa token vào query đảm bảo hub luôn có định danh dù header bị mất. Token JWT ký tự URL-safe.
        val token = runBlocking { tokenStore.token() ?: "" }
        val base = BuildConfig.API_BASE_URL.trimEnd('/') + "/hubs/changes"
        val url = if (token.isNotBlank()) "$base?access_token=$token" else base
        val connection = HubConnectionBuilder.create(url)
            .withAccessTokenProvider(Single.defer { Single.just(runBlocking { tokenStore.token() ?: "" }) })
            .build()
        // Backend phát "changed" kèm 1 tham số phạm vi (scope): hr / data / presence / chat / all…
        connection.on(
            "changed",
            { scopeName: String ->
                // Giữ nguyên scope để ViewModel chỉ nạp đúng phần bị cũ. Trước đây một tin chat cũng
                // kéo lại đơn từ + công việc, tạo nhiều request không cần thiết trên server.
                if (scopeName in RefreshScopes)
                    AppEvents.signalDataChanged(scopeName)
            },
            String::class.java,
        )
        // Backend chuyển tiếp tín hiệu bắt tay WebRTC qua sự kiện "signal" (from, payload). Cuộc gọi
        // dùng khung JSON có "k":"call"; CallManager tự lọc, bỏ qua tín hiệu gửi tệp.
        connection.on(
            "signal",
            { from: String, payload: String -> CallManager.onSignal(from, payload) },
            String::class.java,
            String::class.java,
        )
        // ĐĂNG NHẬP 1 MÁY: server đẩy "kicked" kèm sid của phiên MỚI khi tài khoản đăng nhập ở máy khác.
        // Nếu sid đó KHÁC sid của máy này → máy này vừa bị đá → tự đăng xuất NGAY.
        connection.on(
            "kicked",
            { activeSid: String ->
                scope.launch {
                    val mySid = runCatching { tokenStore.sessionId() }.getOrNull().orEmpty()
                    if (mySid.isNotEmpty() && activeSid.isNotEmpty() && activeSid != mySid) {
                        AppEvents.signalForceLogout("Tài khoản của bạn vừa đăng nhập trên thiết bị khác.")
                    }
                }
            },
            String::class.java,
        )
        return connection
    }
}
