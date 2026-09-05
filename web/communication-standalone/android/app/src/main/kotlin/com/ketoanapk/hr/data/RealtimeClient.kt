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

private val RealtimeRefreshScopes = setOf("chat")

internal fun shouldForwardRealtimeRefreshScope(scopeName: String): Boolean =
    scopeName in RealtimeRefreshScopes

/**
 * SignalR communication legacy: chỉ chat + bắt tay call/P2P. Business invalidation, access và session
 * revocation đi qua [SseRealtimeClient].
 *
 * Chạy KHI APP ĐANG MỞ (bật ở `onAppResumed`, tắt ở `onAppPaused`) để tiết kiệm pin — lúc app ở nền đã
 * có FCM + WorkManager lo thông báo. Tự kết nối lại khi rớt. Token JWT gửi qua query `access_token`
 * (backend đọc ở `Program.cs` cho đường `/hubs`).
 */
class RealtimeClient(private val tokenStore: TokenStore) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    @Volatile private var hub: HubConnection? = null
    private var job: Job? = null
    @Volatile private var wantConnected = false
    @Volatile private var lifecycleEpoch = 0L
    fun isConnected(): Boolean = hub?.connectionState == HubConnectionState.CONNECTED

    @Synchronized
    fun start(selfUsername: String) {
        if (wantConnected) return
        lifecycleEpoch += 1
        val epoch = lifecycleEpoch
        wantConnected = true
        job = scope.launch { connectLoop(epoch, selfUsername) }
    }

    @Synchronized
    fun stop() {
        wantConnected = false
        // Vô hiệu hóa đồng bộ mọi callback/coroutine của hub cũ trước khi stop() bất đồng bộ.
        lifecycleEpoch += 1
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
    private suspend fun connectLoop(epoch: Long, accountUsername: String) {
        while (isEpochActive(epoch)) {
            val token = runCatching { tokenStore.token() }.getOrNull().orEmpty()
            if (!isEpochActive(epoch)) return
            if (token.isBlank()) { delay(3000); continue }

            val connection = build(epoch)
            if (!attachIfActive(connection, epoch)) {
                runCatching { connection.stop().blockingAwait() }
                return
            }
            val started = runCatching { connection.start().blockingAwait() }.isSuccess
            if (!started) {
                detachIfCurrent(connection, epoch)
                if (!isEpochActive(epoch)) return
                delay(3000)
                continue
            }
            if (!bindSignalingIfCurrent(connection, epoch, accountUsername)) {
                runCatching { connection.stop().blockingAwait() }
                return
            }
            // Đã kết nối → cắm kênh gửi tín hiệu cuộc gọi cho CallManager (nhận qua listener "signal").
            // Chat là communication legacy và không có durable cursor ở hub, nên reconnect refetch chat.
            if (isCurrent(connection, epoch)) AppEvents.signalDataChanged("chat")
            // Đã kết nối → chờ tới khi rớt (poll trạng thái nhẹ), rồi thử lại nếu vẫn muốn.
            while (isCurrent(connection, epoch) && connection.connectionState == HubConnectionState.CONNECTED) {
                delay(2000)
            }
            detachIfCurrent(connection, epoch)
            runCatching { connection.stop().blockingAwait() }
            if (isEpochActive(epoch)) delay(2000)
        }
    }

    private fun isEpochActive(epoch: Long): Boolean = wantConnected && lifecycleEpoch == epoch

    private fun isCurrent(connection: HubConnection, epoch: Long): Boolean =
        isEpochActive(epoch) && hub === connection

    /** Không cho loop A gắn lại hub sau khi stop/start đã tạo epoch B. */
    @Synchronized
    private fun attachIfActive(connection: HubConnection, epoch: Long): Boolean {
        if (!isEpochActive(epoch)) return false
        hub = connection
        return true
    }

    /** Chỉ hub đang sở hữu binding mới được tháo binding; hub cũ tuyệt đối không tháo của hub mới. */
    @Synchronized
    private fun detachIfCurrent(connection: HubConnection, epoch: Long) {
        if (lifecycleEpoch != epoch || hub !== connection) return
        hub = null
        CallManager.unbindSignaling()
    }

    /** Ghép phép kiểm tra + bind dưới cùng khóa với stop() để không có khe bind lại sau logout. */
    @Synchronized
    private fun bindSignalingIfCurrent(
        connection: HubConnection,
        epoch: Long,
        accountUsername: String,
    ): Boolean {
        if (!isCurrent(connection, epoch)) return false
        CallManager.bindSignaling(accountUsername) { to, payload ->
            if (isCurrent(connection, epoch)) sendCallSignal(to, payload)
        }
        return true
    }

    private fun build(epoch: Long): HubConnection {
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
                if (isCurrent(connection, epoch) && shouldForwardRealtimeRefreshScope(scopeName))
                    AppEvents.signalDataChanged(scopeName)
            },
            String::class.java,
        )
        // Backend chuyển tiếp tín hiệu bắt tay WebRTC qua sự kiện "signal" (from, payload). Cuộc gọi
        // dùng khung JSON có "k":"call"; CallManager tự lọc, bỏ qua tín hiệu gửi tệp.
        connection.on(
            "signal",
            { from: String, payload: String ->
                if (isCurrent(connection, epoch)) CallManager.onSignal(from, payload)
            },
            String::class.java,
            String::class.java,
        )
        return connection
    }
}
