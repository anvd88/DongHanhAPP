package com.ketoanapk.hr.data

import android.content.Context
import android.media.AudioManager
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.util.Log
import com.ketoanapk.hr.BuildConfig
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import org.json.JSONObject
import org.webrtc.AudioSource
import org.webrtc.AudioTrack
import org.webrtc.Camera2Enumerator
import org.webrtc.CameraVideoCapturer
import org.webrtc.DefaultVideoDecoderFactory
import org.webrtc.DefaultVideoEncoderFactory
import org.webrtc.EglBase
import org.webrtc.IceCandidate
import org.webrtc.MediaConstraints
import org.webrtc.MediaStreamTrack
import org.webrtc.PeerConnection
import org.webrtc.PeerConnectionFactory
import org.webrtc.RtpTransceiver
import org.webrtc.SdpObserver
import org.webrtc.SessionDescription
import org.webrtc.SurfaceTextureHelper
import org.webrtc.SurfaceViewRenderer
import org.webrtc.VideoCapturer
import org.webrtc.VideoSource
import org.webrtc.VideoTrack
import java.util.UUID

/* =============================================================================
   Lõi GỌI THOẠI / GỌI VIDEO (WebRTC) — P2P giữa 2 máy trong công ty.

   BẢO MẬT:
     • Media (âm thanh/hình ảnh) mã hóa BẮT BUỘC bằng DTLS-SRTP của WebRTC — nội
       dung cuộc gọi KHÔNG đi qua máy chủ, không ai ở giữa nghe/xem được.
     • Bắt tay (invite/offer/answer/ICE) đi qua SignalR ĐÃ XÁC THỰC JWT và nhắm
       đúng MỘT username (Clients.User) — kẻ lạ không chen được vào kênh.
     • Chỉ chấp nhận tín hiệu thuộc callId hiện tại; invite lạ khi đang bận → báo bận.
       Mỗi callId là ngẫu nhiên (UUID) nên không đoán/không phát lại được.

   Tín hiệu dùng khung JSON có khóa "k":"call" để KHÔNG đụng kênh gửi tệp (dùng "t"/"tid").
   ========================================================================== */

object CallManager {
    private const val TAG = "CallManager"

    enum class Stage { Idle, Outgoing, Incoming, Connecting, Active, Ended }
    enum class Media { Audio, Video }

    data class Session(
        val callId: String,
        val peerUsername: String,
        val peerName: String,
        val peerInitials: String,
        val media: Media,
        val incoming: Boolean,
        val stage: Stage,
        val startedAt: Long? = null,   // mốc thời gian khi Active (để đếm giờ)
        val muted: Boolean = false,
        val speakerOn: Boolean = false,
        val cameraOn: Boolean = true,
        val remoteVideoOn: Boolean = false,
        // Phía GỌI: máy bên kia đã báo "đang đổ chuông" (đã nhận lời mời và bắt đầu reo) → UI hiện
        // "Đang đổ chuông…" thay vì "Đang gọi…", đồng thời bật nhạc chờ ringback.
        val remoteRinging: Boolean = false,
        val networkQuality: String = "Đang kiểm tra",
        val endedReason: String? = null,
    )

    private val _session = MutableStateFlow<Session?>(null)
    val session: StateFlow<Session?> = _session.asStateFlow()

    private lateinit var appContext: Context
    private var selfUsername: String = ""
    // Tên hiển thị của MÌNH (từ DB: full_name) — gửi kèm lời mời để máy nhận hiện tên thật ngay,
    // không phải chờ tải danh bạ. ViewModel đặt khi đăng nhập.
    @Volatile private var selfDisplayName: String = ""
    // Hàm gửi tín hiệu tới 1 username (do RealtimeClient cắm vào). null nếu chưa kết nối hub.
    @Volatile private var signalSender: ((toUser: String, payload: String) -> Unit)? = null
    // Hàng chờ tín hiệu khi hub CHƯA kết nối (vd app vừa cold-start từ push, bấm "Trả lời" trước khi
    // SignalR nối lại) — gửi bù khi bind. Giới hạn để không phình bộ nhớ.
    private val pendingOutgoing = ArrayList<Pair<String, String>>()
    // Đẩy "chuông" FCM để máy nhận reo cả khi app đóng/nền (do ViewModel cắm, có repo + coroutine).
    @Volatile private var pushInviter: ((toUser: String, callId: String, media: String) -> Unit)? = null
    @Volatile private var pushCanceler: ((toUser: String, callId: String) -> Unit)? = null
    // Báo cuộc gọi NHỠ (người kia không bắt máy/không online) qua thông báo TTL dài.
    @Volatile private var missedNotifier: ((toUser: String, callId: String, media: String) -> Unit)? = null
    @Volatile private var historyRecorder: ((session: Session, reason: String, endedAt: Long) -> Unit)? = null

    // TURN credential ĐỘNG (có hạn giờ, backend cấp qua HMAC) — KHÔNG nhúng mật khẩu vào APK.
    data class TurnCreds(val urls: List<String>, val username: String, val credential: String)
    @Volatile private var turnCreds: TurnCreds? = null
    // ViewModel cắm hàm nạp credential (gọi REST đã xác thực) — CallManager kích hoạt khi cần làm mới.
    @Volatile private var turnFetcher: (() -> Unit)? = null

    // Cấu hình gọi ĐIỀU KHIỂN TỪ XA (STUN, ép relay, timeout, chất lượng video, công tắc bật/tắt).
    // ViewModel đẩy vào mỗi lần nạp app-config → đổi ở backend là app áp dụng, khỏi build lại APK.
    @Volatile private var callConfig: CallRemoteConfig = CallRemoteConfig()

    /** ViewModel gọi khi nạp/đổi app-config từ backend. */
    fun setCallConfig(cfg: CallRemoteConfig) { callConfig = cfg }

    /** Cho UI biết có được phép gọi không (công tắc từ xa). */
    val callsEnabled: Boolean get() = callConfig.callsEnabled
    val videoCallEnabled: Boolean get() = callConfig.videoCallEnabled

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    // --- WebRTC ---
    private var factory: PeerConnectionFactory? = null
    private val eglBase: EglBase by lazy { EglBase.create() }
    private var peer: PeerConnection? = null
    private var audioSource: AudioSource? = null
    private var audioTrack: AudioTrack? = null
    private var videoSource: VideoSource? = null
    private var videoTrack: VideoTrack? = null
    private var videoCapturer: VideoCapturer? = null
    private var videoProcessor: BeautyVideoProcessor? = null
    private var surfaceHelper: SurfaceTextureHelper? = null
    private var remoteVideoTrack: VideoTrack? = null

    // Renderer do UI tạo (gắn/tháo khi màn video hiện/ẩn).
    private var localRenderer: SurfaceViewRenderer? = null
    private var remoteRenderer: SurfaceViewRenderer? = null

    private val pendingRemoteIce = ArrayList<IceCandidate>()
    private var remoteDescSet = false
    private var timeoutJob: Job? = null
    private var reconnectJob: Job? = null

    private fun buildIceServers(): List<PeerConnection.IceServer> {
        val list = ArrayList<PeerConnection.IceServer>()
        // STUN (danh sách lấy từ remote config — đổi ở backend khỏi build APK). Trong LAN thì
        // host-candidate nội bộ là đủ; STUN giúp khi 2 máy khác mạng.
        val stun = callConfig.stunServers.filter { it.isNotBlank() }
        if (stun.isNotEmpty()) list.add(PeerConnection.IceServer.builder(stun).createIceServer())
        // TURN: BẮT BUỘC để gọi được khi 2 máy khác mạng / sau NAT đối xứng (4G ↔ WiFi qua Internet) —
        // STUN không xuyên được thì TURN chuyển tiếp media (vẫn mã hóa DTLS-SRTP đầu-cuối).
        // ƯU TIÊN credential ĐỘNG do backend cấp (có hạn giờ, không nhúng mật khẩu vào APK).
        val dyn = turnCreds
        if (dyn != null && dyn.urls.isNotEmpty()) {
            list.add(
                PeerConnection.IceServer.builder(dyn.urls)
                    .setUsername(dyn.username)
                    .setPassword(dyn.credential)
                    .createIceServer(),
            )
        } else {
            // Dự phòng: TURN tĩnh qua BuildConfig (nếu ai đó vẫn cấu hình kiểu cũ). Thường để trống.
            val turnUrls = BuildConfig.TURN_URL.split(",").map { it.trim() }.filter { it.isNotEmpty() }
            if (turnUrls.isNotEmpty()) {
                list.add(
                    PeerConnection.IceServer.builder(turnUrls)
                        .setUsername(BuildConfig.TURN_USER)
                        .setPassword(BuildConfig.TURN_PASS)
                        .createIceServer(),
                )
            }
        }
        return list
    }

    /** ViewModel cắm hàm nạp TURN credential (REST có xác thực) + nạp ngay lần đầu. */
    fun bindTurn(fetcher: () -> Unit) {
        turnFetcher = fetcher
        refreshTurn()
    }

    /** ViewModel gọi khi có kết quả credential (null = server chưa cấu hình TURN → dùng STUN). */
    fun setTurnCreds(creds: TurnCreds?) {
        if (creds != null && creds.urls.isNotEmpty()) turnCreds = creds
    }

    /** Nạp/làm mới credential (best-effort) — gọi trước mỗi cuộc gọi để chắc chắn còn hạn. */
    private fun refreshTurn() {
        runCatching { turnFetcher?.invoke() }
    }

    val eglContext: EglBase.Context get() = eglBase.eglBaseContext

    /** Gọi 1 lần khi app khởi động (Application/MainActivity). */
    fun init(context: Context) {
        if (this::appContext.isInitialized) return
        appContext = context.applicationContext
        PeerConnectionFactory.initialize(
            PeerConnectionFactory.InitializationOptions.builder(appContext)
                .createInitializationOptions(),
        )
    }

    /** RealtimeClient cắm kênh gửi + username hiện tại khi hub kết nối/đăng nhập. */
    fun bindSignaling(selfUser: String, sender: (toUser: String, payload: String) -> Unit) {
        selfUsername = selfUser
        signalSender = sender
        // Gửi bù các tín hiệu đã xếp hàng lúc chưa có kết nối (accept/reject sau khi mở app từ push…).
        val flush: List<Pair<String, String>>
        synchronized(pendingOutgoing) {
            flush = pendingOutgoing.toList()
            pendingOutgoing.clear()
        }
        flush.forEach { (to, payload) -> runCatching { sender(to, payload) } }
    }

    fun unbindSignaling() {
        signalSender = null
    }

    /** Đặt tên hiển thị của mình (từ DB) để gửi kèm lời mời gọi. */
    fun setSelfIdentity(displayName: String) {
        selfDisplayName = displayName
    }

    /** ViewModel cắm kênh đẩy "chuông"/"nhỡ" FCM (gọi REST có xác thực) cho cuộc gọi. */
    fun bindPush(
        inviter: (toUser: String, callId: String, media: String) -> Unit,
        canceler: (toUser: String, callId: String) -> Unit,
        misser: (toUser: String, callId: String, media: String) -> Unit,
    ) {
        pushInviter = inviter
        pushCanceler = canceler
        missedNotifier = misser
    }

    fun bindHistory(recorder: (session: Session, reason: String, endedAt: Long) -> Unit) {
        historyRecorder = recorder
    }

    /* ----------------------------- Điều khiển ngoài (UI) ----------------------------- */

    /** Bắt đầu gọi tới [peerUsername]. */
    fun startCall(peerUsername: String, peerName: String, peerInitials: String, media: Media) {
        if (_session.value != null) return // đang có cuộc khác
        if (!callConfig.callsEnabled) return // công tắc tổng tắt từ xa
        if (media == Media.Video && !callConfig.videoCallEnabled) return // gọi video bị tắt từ xa
        // KHÔNG chặn khi hub chưa nối: vẫn mở phiên + XẾP HÀNG lời mời (send() tự gửi bù khi bind) và
        // đẩy "chuông" FCM. Trước đây guard `signalSender ?: return` khiến bấm gọi lúc SignalR vừa
        // nối lại (mới mở app) im ru không làm gì → cảm giác "không gọi được".
        refreshTurn() // làm mới TURN credential trước khi bắt tay (kịp trước khi tạo peer)
        val callId = UUID.randomUUID().toString()
        _session.value = Session(
            callId = callId,
            peerUsername = peerUsername,
            peerName = peerName,
            peerInitials = peerInitials,
            media = media,
            incoming = false,
            stage = Stage.Outgoing,
            speakerOn = media == Media.Video,
        )
        applyAudioRoute(speakerOn = media == Media.Video)
        val mediaStr = media.name.lowercase()
        send(peerUsername, buildSignal("invite", callId, mapOf("media" to mediaStr, "name" to selfDisplayName)))
        // Đồng thời đẩy "chuông" qua FCM để máy nhận reo cả khi app họ đang ĐÓNG/nền (SignalR chỉ tới
        // được khi app mở). Người nhận online sẽ chống trùng theo callId.
        runCatching { pushInviter?.invoke(peerUsername, callId, mediaStr) }
        applyCallSounds() // Outgoing chưa reo → im lặng cho tới khi nhận "ringing"
        armTimeout(callConfig.outgoingTimeoutSeconds * 1000L) // không ai bắt máy → hủy
    }

    /** Nghe máy (phía nhận). */
    fun accept() {
        val s = _session.value ?: return
        if (s.stage != Stage.Incoming) return
        refreshTurn() // đảm bảo TURN credential còn hạn trước khi nhận offer/tạo peer
        cancelTimeout()
        _session.value = s.copy(stage = Stage.Connecting)
        applyCallSounds() // rời trạng thái "đang đổ chuông" → tắt chuông đến
        applyAudioRoute(speakerOn = s.media == Media.Video)
        send(s.peerUsername, buildSignal("accept", s.callId))
        // Phía nhận CHỜ offer từ phía gọi (giống luồng gửi tệp: bên được đồng ý sẽ tạo offer).
    }

    /** Từ chối / cúp máy. */
    fun hangup(reason: String = "ended") {
        val s = _session.value ?: return
        val type = when (s.stage) {
            Stage.Incoming -> "reject"
            else -> "end"
        }
        runCatching { send(s.peerUsername, buildSignal(type, s.callId, mapOf("reason" to reason))) }
        // Người GỌI cúp trước khi đối phương bắt máy → hủy luôn chuông FCM đã đẩy để họ hết reo,
        // đồng thời báo CUỘC GỌI NHỠ (để người kia biết dù đang offline lúc gọi).
        if (!s.incoming && (s.stage == Stage.Outgoing || s.stage == Stage.Connecting)) {
            runCatching { pushCanceler?.invoke(s.peerUsername, s.callId) }
        }
        if (!s.incoming && s.stage == Stage.Outgoing) {
            runCatching { missedNotifier?.invoke(s.peerUsername, s.callId, s.media.name.lowercase()) }
        }
        teardown(reason)
    }

    /**
     * Cuộc gọi đến qua FCM (khi app đóng/nền, SignalR chưa nhận được invite). Dựng phiên "đang đổ
     * chuông" để lớp phủ [ui.CallHost] hiện màn nghe máy khi app mở lên. Nếu đã có phiên (SignalR
     * tới trước, hoặc đang bận) thì bỏ qua để không chồng cuộc gọi.
     */
    fun ingestIncomingFromPush(callId: String, from: String, callerName: String, media: String) {
        scope.launch {
            if (_session.value != null) return@launch
            val name = callerName.ifBlank { from }
            val mediaEnum = if (media.equals("video", true)) Media.Video else Media.Audio
            _session.value = Session(
                callId = callId,
                peerUsername = from,
                peerName = name,
                peerInitials = initialsOf(name),
                media = mediaEnum,
                incoming = true,
                stage = Stage.Incoming,
                speakerOn = mediaEnum == Media.Video,
            )
            // Báo NGƯỢC cho người gọi "máy đang đổ chuông" (gửi bù nếu hub chưa nối) → họ thấy đúng
            // trạng thái. Bật chuông trong app nếu đang tiền cảnh (nền đã có CallNotifier reo).
            send(from, buildSignal("ringing", callId))
            applyCallSounds()
            armTimeout(callConfig.incomingTimeoutSeconds * 1000L)
        }
    }

    /** Người gọi đã hủy chuông (FCM call_cancel) → tắt màn đổ chuông nếu đang chờ nghe đúng cuộc đó. */
    fun cancelIncomingFromPush(callId: String) {
        scope.launch {
            val cur = _session.value ?: return@launch
            if (cur.callId == callId && cur.stage == Stage.Incoming) teardown("canceled")
        }
    }

    /**
     * Activity gọi khi app đổi tiền cảnh/nền: bật chuông TRONG APP nếu đang có cuộc gọi đến và app vừa
     * lên tiền cảnh (lúc nền do CallNotifier reo qua thông báo). Không tắt khi xuống nền để không "câm"
     * giữa lúc đang đổ chuông.
     */
    fun onForegroundChanged() {
        scope.launch { applyCallSounds() }
    }

    fun toggleMute() {
        val s = _session.value ?: return
        val muted = !s.muted
        audioTrack?.setEnabled(!muted)
        _session.value = s.copy(muted = muted)
    }

    fun toggleSpeaker() {
        val s = _session.value ?: return
        val on = !s.speakerOn
        applyAudioRoute(on)
        _session.value = s.copy(speakerOn = on)
    }

    fun toggleCamera() {
        val s = _session.value ?: return
        val on = !s.cameraOn
        videoTrack?.setEnabled(on)
        _session.value = s.copy(cameraOn = on)
    }

    fun switchCamera() {
        (videoCapturer as? CameraVideoCapturer)?.switchCamera(null)
    }

    /** Hiệu ứng video (áp GPU, cả bên kia thấy). level 0..1. */
    fun setBeauty(level: Float) = CallVideoEffects.setBeauty(level)

    /** Bộ lọc màu: 0 gốc · 1 nắng ấm · 2 trong xanh · 3 hồng đào · 4 cổ điển · 5 mộng mơ. */
    fun setCallFilter(index: Int) = CallVideoEffects.setFilter(index)

    /** Nâng cuộc thoại đang chạy lên video (cả 2 phía cùng bật). Bên bấm là bên tạo offer mới. */
    fun switchToVideo() {
        val s = _session.value ?: return
        if (s.media == Media.Video || s.stage != Stage.Active) return
        _session.value = s.copy(media = Media.Video, speakerOn = true, cameraOn = true)
        applyAudioRoute(true)
        ensureLocalVideo()
        send(s.peerUsername, buildSignal("upgrade", s.callId))
        // Gửi offer đàm phán lại NGAY sau tín hiệu upgrade (SignalR giữ đúng thứ tự).
        createOffer()
    }

    /* ----------------------------- Renderer (UI gắn) ----------------------------- */

    fun attachRenderers(local: SurfaceViewRenderer?, remote: SurfaceViewRenderer?) {
        localRenderer = local
        remoteRenderer = remote
        remote?.let { r -> remoteVideoTrack?.let { runCatching { it.addSink(r) } } }
        local?.let { r -> videoTrack?.let { runCatching { it.addSink(r) } } }
    }

    fun detachRenderers() {
        remoteRenderer?.let { r -> remoteVideoTrack?.let { runCatching { it.removeSink(r) } } }
        localRenderer?.let { r -> videoTrack?.let { runCatching { it.removeSink(r) } } }
        localRenderer = null
        remoteRenderer = null
    }

    /* ----------------------------- Nhận tín hiệu ----------------------------- */

    /** RealtimeClient gọi khi có sự kiện "signal" (from = username người gửi). */
    fun onSignal(from: String, raw: String) {
        val msg = runCatching { JSONObject(raw) }.getOrNull() ?: return
        if (msg.optString("k") != "call") return // không phải tín hiệu gọi (bỏ qua, vd gửi tệp)
        val type = msg.optString("type")
        val callId = msg.optString("id")
        if (callId.isEmpty()) return
        scope.launch { handleSignal(from, type, callId, msg) }
    }

    private fun handleSignal(from: String, type: String, callId: String, msg: JSONObject) {
        val s = _session.value

        // Lời mời đến khi đang RẢNH → hiện màn gọi đến. Khi đang BẬN → báo bận.
        if (type == "invite") {
            if (s != null) {
                // Đã có phiên đúng cuộc này (vd đã dựng từ chuông FCM) → bỏ qua, KHÔNG báo bận.
                if (s.callId == callId) return
                send(from, buildSignal("reject", callId, mapOf("reason" to "busy")))
                return
            }
            val media = if (msg.optString("media") == "video") Media.Video else Media.Audio
            val callerName = msg.optString("name").ifBlank { from } // tên thật (DB) gửi kèm lời mời
            _session.value = Session(
                callId = callId,
                peerUsername = from,
                peerName = callerName,
                peerInitials = initialsOf(callerName),
                media = media,
                incoming = true,
                stage = Stage.Incoming,
                speakerOn = media == Media.Video,
            )
            // Báo NGƯỢC "đang đổ chuông" cho người gọi (hiện đúng trạng thái) + reo chuông trong app.
            send(from, buildSignal("ringing", callId))
            applyCallSounds()
            armTimeout(callConfig.incomingTimeoutSeconds * 1000L)
            return
        }

        // Từ đây phải khớp cuộc gọi hiện tại (đúng callId + đúng người) mới xử lý.
        if (s == null || s.callId != callId || s.peerUsername != from) return

        when (type) {
            "ringing" -> {
                // Phía GỌI: máy bên kia đã nhận lời mời và bắt đầu đổ chuông → hiện "Đang đổ chuông…"
                // + bật nhạc chờ ringback thay cho im lặng.
                if (s.stage == Stage.Outgoing && !s.remoteRinging) {
                    _session.value = s.copy(remoteRinging = true)
                    applyCallSounds()
                }
            }
            "accept" -> {
                // Phía GỌI được đồng ý → tạo peer + offer.
                cancelTimeout()
                _session.value = s.copy(stage = Stage.Connecting)
                applyCallSounds() // tắt nhạc chờ khi đã bắt máy
                createPeer()
                addLocalTracks(s.media)
                createOffer()
            }
            "reject" -> teardown(msg.optString("reason").ifEmpty { "declined" })
            "end" -> teardown("ended")
            "cancel" -> teardown("canceled")
            "upgrade" -> {
                // Đối phương nâng lên video: bật video của mình rồi chờ offer đàm phán lại.
                _session.value = _session.value?.copy(media = Media.Video, speakerOn = true)
                applyAudioRoute(true)
                ensureLocalVideo()
            }
            "offer" -> onRemoteSdp(SessionDescription.Type.OFFER, msg.optString("sdp"))
            "answer" -> onRemoteSdp(SessionDescription.Type.ANSWER, msg.optString("sdp"))
            "ice" -> {
                val cand = IceCandidate(msg.optString("mid"), msg.optInt("line"), msg.optString("cand"))
                if (remoteDescSet) peer?.addIceCandidate(cand) else pendingRemoteIce.add(cand)
            }
        }
    }

    private fun onRemoteSdp(type: SessionDescription.Type, sdp: String) {
        if (sdp.isEmpty()) return
        // Phía NHẬN chưa có peer (chờ offer) → tạo peer + track trước khi nhận offer.
        if (peer == null) {
            createPeer()
            addLocalTracks(_session.value?.media ?: Media.Audio)
        }
        val pc = peer ?: return
        pc.setRemoteDescription(object : SimpleSdp() {
            override fun onSetSuccess() {
                remoteDescSet = true
                drainRemoteIce()
                if (type == SessionDescription.Type.OFFER) createAnswer()
            }
        }, SessionDescription(type, sdp))
    }

    /* ----------------------------- WebRTC dựng/đàm phán ----------------------------- */

    private fun ensureFactory(): PeerConnectionFactory {
        factory?.let { return it }
        val encoder = DefaultVideoEncoderFactory(eglBase.eglBaseContext, true, true)
        val decoder = DefaultVideoDecoderFactory(eglBase.eglBaseContext)
        val f = PeerConnectionFactory.builder()
            .setVideoEncoderFactory(encoder)
            .setVideoDecoderFactory(decoder)
            .setOptions(PeerConnectionFactory.Options())
            .createPeerConnectionFactory()
        factory = f
        return f
    }

    private fun createPeer() {
        if (peer != null) return
        val config = PeerConnection.RTCConfiguration(buildIceServers()).apply {
            sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN
            continualGatheringPolicy = PeerConnection.ContinualGatheringPolicy.GATHER_CONTINUALLY
            bundlePolicy = PeerConnection.BundlePolicy.MAXBUNDLE
            rtcpMuxPolicy = PeerConnection.RtcpMuxPolicy.REQUIRE
            tcpCandidatePolicy = PeerConnection.TcpCandidatePolicy.ENABLED // thêm ứng viên TCP khi UDP bị chặn
            candidateNetworkPolicy = PeerConnection.CandidateNetworkPolicy.ALL
            iceCandidatePoolSize = 2 // gom ứng viên sớm để bắt tay nhanh hơn
            // Ép mọi media đi qua TURN (relay) nếu remote config bật — ổn định hơn + giấu IP thật đôi bên.
            iceTransportsType =
                if (callConfig.forceRelay) PeerConnection.IceTransportsType.RELAY
                else PeerConnection.IceTransportsType.ALL
        }
        peer = ensureFactory().createPeerConnection(config, object : PeerObserver() {
            override fun onIceCandidate(candidate: IceCandidate) {
                val s = _session.value ?: return
                send(
                    s.peerUsername,
                    buildSignal(
                        "ice", s.callId,
                        mapOf("mid" to candidate.sdpMid, "line" to candidate.sdpMLineIndex, "cand" to candidate.sdp),
                    ),
                )
            }

            override fun onTrack(transceiver: RtpTransceiver) {
                val track = transceiver.receiver.track()
                if (track is VideoTrack) {
                    scope.launch {
                        remoteVideoTrack = track
                        track.setEnabled(true)
                        remoteRenderer?.let { runCatching { track.addSink(it) } }
                        _session.value = _session.value?.copy(remoteVideoOn = true)
                    }
                }
            }

            override fun onConnectionChange(newState: PeerConnection.PeerConnectionState) {
                scope.launch {
                    when (newState) {
                        PeerConnection.PeerConnectionState.CONNECTED -> {
                            reconnectJob?.cancel()
                            reconnectJob = null
                            markActive()
                        }
                        PeerConnection.PeerConnectionState.DISCONNECTED -> beginReconnect()
                        PeerConnection.PeerConnectionState.FAILED,
                        PeerConnection.PeerConnectionState.CLOSED -> teardown("disconnected")
                        else -> {}
                    }
                }
            }
        })
    }

    private fun addLocalTracks(media: Media) {
        val pc = peer ?: return
        val f = ensureFactory()
        if (audioTrack == null) {
            val src = f.createAudioSource(MediaConstraints())
            audioSource = src
            audioTrack = f.createAudioTrack("audio0", src).apply { setEnabled(true) }
        }
        audioTrack?.let { pc.addTrack(it, listOf(STREAM_ID)) }
        if (media == Media.Video) ensureLocalVideo()
    }

    private fun ensureLocalVideo() {
        val pc = peer ?: return
        if (videoTrack != null) return
        val f = ensureFactory()
        val capturer = createCameraCapturer() ?: return
        videoCapturer = capturer
        val helper = SurfaceTextureHelper.create("CaptureThread", eglBase.eglBaseContext)
        surfaceHelper = helper
        val source = f.createVideoSource(capturer.isScreencast)
        videoSource = source
        // Cắm bộ xử lý HIỆU ỨNG (làm mịn da + bộ lọc) ngay đầu nguồn: cả bên nhận LẪN khung tự xem đều
        // thấy ảnh đã xử lý; khi không bật hiệu ứng thì nó tự chuyển tiếp nguyên bản (không tốn GPU).
        runCatching {
            val processor = BeautyVideoProcessor(eglBase.eglBaseContext)
            videoProcessor = processor
            source.setVideoProcessor(processor)
        }
        capturer.initialize(helper, appContext, source.capturerObserver)
        // Độ phân giải + FPS lấy từ remote config (hạ xuống khi mạng yếu, khỏi build APK).
        capturer.startCapture(callConfig.videoWidth, callConfig.videoHeight, callConfig.videoFps)
        val track = f.createVideoTrack("video0", source).apply { setEnabled(true) }
        videoTrack = track
        localRenderer?.let { runCatching { track.addSink(it) } }
        pc.addTrack(track, listOf(STREAM_ID))
    }

    /**
     * Áp TRẦN bitrate cho các luồng gửi theo remote config (0 = không giới hạn). Gọi sau khi đã đặt
     * local description (lúc đó encodings mới tồn tại). Giúp gọi mượt hơn trên mạng yếu, chỉnh từ backend.
     */
    private fun applySenderBitrates() {
        val pc = peer ?: return
        val vk = callConfig.videoMaxBitrateKbps
        val ak = callConfig.audioMaxBitrateKbps
        if (vk <= 0 && ak <= 0) return
        pc.senders.forEach { sender ->
            val max = when (sender.track()?.kind()) {
                MediaStreamTrack.VIDEO_TRACK_KIND -> vk
                MediaStreamTrack.AUDIO_TRACK_KIND -> ak
                else -> 0
            }
            if (max <= 0) return@forEach
            runCatching {
                val p = sender.parameters ?: return@runCatching
                p.encodings.forEach { it.maxBitrateBps = max * 1000 }
                sender.parameters = p
            }
        }
    }

    private fun createCameraCapturer(): VideoCapturer? {
        val enumerator = Camera2Enumerator(appContext)
        val names = enumerator.deviceNames
        // Ưu tiên camera trước.
        names.firstOrNull { enumerator.isFrontFacing(it) }?.let { return enumerator.createCapturer(it, null) }
        names.firstOrNull()?.let { return enumerator.createCapturer(it, null) }
        return null
    }

    private fun createOffer() {
        val pc = peer ?: return
        pc.createOffer(object : SimpleSdp() {
            override fun onCreateSuccess(sdp: SessionDescription) {
                pc.setLocalDescription(object : SimpleSdp() {
                    override fun onSetSuccess() = applySenderBitrates()
                }, sdp)
                val s = _session.value ?: return
                send(s.peerUsername, buildSignal("offer", s.callId, mapOf("sdp" to sdp.description)))
            }
        }, MediaConstraints())
    }

    private fun createAnswer() {
        val pc = peer ?: return
        pc.createAnswer(object : SimpleSdp() {
            override fun onCreateSuccess(sdp: SessionDescription) {
                pc.setLocalDescription(object : SimpleSdp() {
                    override fun onSetSuccess() = applySenderBitrates()
                }, sdp)
                val s = _session.value ?: return
                send(s.peerUsername, buildSignal("answer", s.callId, mapOf("sdp" to sdp.description)))
            }
        }, MediaConstraints())
    }

    private fun drainRemoteIce() {
        val pc = peer ?: return
        pendingRemoteIce.forEach { runCatching { pc.addIceCandidate(it) } }
        pendingRemoteIce.clear()
    }

    private fun markActive() {
        val s = _session.value ?: return
        if (s.stage == Stage.Active) return
        cancelTimeout()
        _session.value = s.copy(stage = Stage.Active, startedAt = s.startedAt ?: System.currentTimeMillis(), networkQuality = estimateNetworkQuality())
        applyCallSounds() // đã kết nối → tắt mọi chuông/nhạc chờ
    }

    /* ----------------------------- Chuông / nhạc chờ ----------------------------- */

    /**
     * Đồng bộ âm thanh cuộc gọi theo trạng thái phiên hiện tại:
     *  • Có cuộc ĐẾN đang đổ chuông + app tiền cảnh → chuông đến (nền để CallNotifier lo).
     *  • Phía GỌI, máy kia đang đổ chuông → nhạc chờ ringback.
     *  • Còn lại (đã kết nối / kết thúc / chưa reo) → tắt hết.
     */
    private fun beginReconnect() {
        val s = _session.value ?: return
        if (s.stage == Stage.Ended || s.stage == Stage.Incoming || s.stage == Stage.Outgoing) return
        _session.value = s.copy(stage = Stage.Connecting, networkQuality = "Đang kết nối lại")
        runCatching { peer?.restartIce() }
        if (!s.incoming) runCatching { createOffer() }
        reconnectJob?.cancel()
        reconnectJob = scope.launch {
            delay(12_000)
            if (_session.value?.stage == Stage.Connecting) teardown("disconnected")
        }
    }

    private fun estimateNetworkQuality(): String {
        if (!this::appContext.isInitialized) return "không rõ"
        val cm = appContext.getSystemService(ConnectivityManager::class.java) ?: return "không rõ"
        val caps = cm.getNetworkCapabilities(cm.activeNetwork) ?: return "yếu"
        val bandwidth = caps.linkDownstreamBandwidthKbps
        return when {
            bandwidth >= 5_000 -> "tốt"
            bandwidth >= 1_000 -> "trung bình"
            caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) -> "trung bình"
            else -> "yếu"
        }
    }

    private fun applyCallSounds() {
        val s = _session.value
        if (s == null) { CallSounds.stopAll(); return }
        when {
            s.incoming && s.stage == Stage.Incoming -> {
                CallSounds.stopRingback()
                if (AppForeground.isForeground && this::appContext.isInitialized) {
                    CallNotifier.dismiss(appContext) // tránh reo trùng với thông báo (nếu có)
                    CallSounds.startRingtone(appContext)
                }
            }
            !s.incoming && s.remoteRinging && s.stage == Stage.Outgoing -> {
                CallSounds.stopRingtone()
                CallSounds.startRingback()
            }
            else -> CallSounds.stopAll()
        }
    }

    /* ----------------------------- Dọn dẹp ----------------------------- */

    private fun teardown(reason: String) {
        val original = _session.value ?: return
        if (original.stage == Stage.Ended) return
        cancelTimeout()
        reconnectJob?.cancel()
        reconnectJob = null
        CallSounds.stopAll()
        val endedAt = System.currentTimeMillis()
        runCatching { historyRecorder?.invoke(original, reason, endedAt) }
        val ended = original.copy(stage = Stage.Ended, endedReason = reason)
        remoteDescSet = false
        pendingRemoteIce.clear()

        CallVideoEffects.reset() // tắt hiệu ứng cho cuộc gọi sau
        videoProcessor = null    // GL được dọn ở onCapturerStopped khi capturer dừng bên dưới
        runCatching { videoCapturer?.stopCapture() }
        runCatching { videoCapturer?.dispose() }
        videoCapturer = null
        runCatching { surfaceHelper?.dispose() }
        surfaceHelper = null
        remoteVideoTrack = null
        videoTrack = null
        audioTrack = null
        runCatching { videoSource?.dispose() }
        videoSource = null
        runCatching { audioSource?.dispose() }
        audioSource = null
        runCatching { peer?.close() }
        peer = null
        resetAudioRoute()

        _session.value = ended
        // Xóa hẳn phiên sau 1 nhịp để UI kịp hiện trạng thái "đã kết thúc".
        scope.launch {
            delay(1200)
            if (_session.value?.stage == Stage.Ended) _session.value = null
        }
    }

    /* ----------------------------- Âm thanh ----------------------------- */

    private fun audioManager(): AudioManager =
        appContext.getSystemService(Context.AUDIO_SERVICE) as AudioManager

    private fun applyAudioRoute(speakerOn: Boolean) {
        if (!this::appContext.isInitialized) return // cold-start từ push, chưa init → bỏ qua an toàn
        runCatching {
            val am = audioManager()
            am.mode = AudioManager.MODE_IN_COMMUNICATION
            am.isSpeakerphoneOn = speakerOn
        }
    }

    private fun resetAudioRoute() {
        if (!this::appContext.isInitialized) return
        runCatching {
            val am = audioManager()
            am.mode = AudioManager.MODE_NORMAL
            am.isSpeakerphoneOn = false
        }
    }

    /* ----------------------------- Tiện ích ----------------------------- */

    private fun armTimeout(ms: Long) {
        cancelTimeout()
        timeoutJob = scope.launch {
            delay(ms)
            val s = _session.value
            if (s != null && (s.stage == Stage.Outgoing || s.stage == Stage.Incoming)) {
                if (!s.incoming) {
                    runCatching { send(s.peerUsername, buildSignal("cancel", s.callId)) }
                    // Hết 30s không ai bắt máy → báo cuộc gọi nhỡ (đến được cả khi họ offline lúc gọi).
                    runCatching { missedNotifier?.invoke(s.peerUsername, s.callId, s.media.name.lowercase()) }
                }
                teardown(if (s.incoming) "missed" else "no_answer")
            }
        }
    }

    private fun cancelTimeout() {
        timeoutJob?.cancel()
        timeoutJob = null
    }

    private fun send(toUser: String, payload: String) {
        val sender = signalSender
        if (sender == null) {
            // Chưa có kết nối (vd vừa mở app từ push, hub đang nối lại) → xếp hàng, gửi bù khi bind.
            synchronized(pendingOutgoing) { if (pendingOutgoing.size < 50) pendingOutgoing.add(toUser to payload) }
            return
        }
        runCatching { sender(toUser, payload) }.onFailure { Log.w(TAG, "gửi tín hiệu lỗi", it) }
    }

    private fun buildSignal(type: String, callId: String, extra: Map<String, Any?> = emptyMap()): String {
        val o = JSONObject()
        o.put("k", "call")
        o.put("type", type)
        o.put("id", callId)
        for ((key, value) in extra) o.put(key, value)
        return o.toString()
    }

    private fun initialsOf(name: String): String {
        val parts = name.trim().split(" ", ".", "_").filter { it.isNotBlank() }
        return when {
            parts.isEmpty() -> "?"
            parts.size == 1 -> parts[0].take(2).uppercase()
            else -> (parts[parts.size - 2].take(1) + parts.last().take(1)).uppercase()
        }
    }

    private const val STREAM_ID = "ketoan_call"
}

/** SdpObserver rút gọn: chỉ ghi đè phương thức cần dùng. */
private open class SimpleSdp : SdpObserver {
    override fun onCreateSuccess(sdp: SessionDescription) {}
    override fun onSetSuccess() {}
    override fun onCreateFailure(error: String?) { Log.w("CallManager", "SDP create fail: $error") }
    override fun onSetFailure(error: String?) { Log.w("CallManager", "SDP set fail: $error") }
}

/** PeerConnection.Observer rút gọn với mặc định rỗng. */
private open class PeerObserver : PeerConnection.Observer {
    override fun onSignalingChange(state: PeerConnection.SignalingState?) {}
    override fun onIceConnectionChange(state: PeerConnection.IceConnectionState?) {}
    override fun onIceConnectionReceivingChange(receiving: Boolean) {}
    override fun onIceGatheringChange(state: PeerConnection.IceGatheringState?) {}
    override fun onIceCandidate(candidate: IceCandidate) {}
    override fun onIceCandidatesRemoved(candidates: Array<out IceCandidate>?) {}
    override fun onAddStream(stream: org.webrtc.MediaStream?) {}
    override fun onRemoveStream(stream: org.webrtc.MediaStream?) {}
    override fun onDataChannel(channel: org.webrtc.DataChannel?) {}
    override fun onRenegotiationNeeded() {}
    override fun onAddTrack(receiver: org.webrtc.RtpReceiver?, streams: Array<out org.webrtc.MediaStream>?) {}
    override fun onTrack(transceiver: RtpTransceiver) {}
    override fun onConnectionChange(newState: PeerConnection.PeerConnectionState) {}
}
