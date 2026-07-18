package com.ketoanapk.hr.ui

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import com.ketoanapk.hr.data.CallManager
import kotlinx.coroutines.delay
import org.webrtc.RendererCommon
import org.webrtc.SurfaceViewRenderer

/**
 * Lớp phủ điều phối cuộc gọi — gắn ở gốc app, luôn nằm trên mọi màn hình. Đọc [CallManager.session]
 * để hiện màn gọi đến / gọi thoại / gọi video, xin quyền micro–camera, và cắm khung hình
 * (SurfaceViewRenderer) vào lõi WebRTC.
 */
@Composable
fun CallHost(vm: HrViewModel) {
    val ctx = LocalContext.current
    val session by CallManager.session.collectAsState()

    // Ghi nhớ hành động cần chạy sau khi được cấp quyền (nghe máy / bắt đầu gọi / nâng video).
    var pendingAction by remember { mutableStateOf<(() -> Unit)?>(null) }
    val permLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        val granted = result.values.all { it }
        val action = pendingAction
        pendingAction = null
        if (granted) action?.invoke() else CallManager.hangup("no_permission")
    }
    fun ensurePermsThen(video: Boolean, action: () -> Unit) {
        val perms = if (video) arrayOf(Manifest.permission.RECORD_AUDIO, Manifest.permission.CAMERA)
        else arrayOf(Manifest.permission.RECORD_AUDIO)
        val allGranted = perms.all { ContextCompat.checkSelfPermission(ctx, it) == PackageManager.PERMISSION_GRANTED }
        if (allGranted) action() else { pendingAction = action; permLauncher.launch(perms) }
    }
    // Người GỌI đã bấm gọi ⇒ xin quyền ngay khi phiên mở (để lúc đối phương nghe máy là sẵn sàng).
    LaunchedEffect(session?.callId) {
        val s = session ?: return@LaunchedEffect
        if (!s.incoming) ensurePermsThen(s.media == CallManager.Media.Video) { /* đã có quyền, chờ accept */ }
    }

    val s = session ?: return

    // Đồng hồ đếm giờ cuộc gọi (nhịp 1s khi đã kết nối).
    var nowMs by remember { mutableStateOf(System.currentTimeMillis()) }
    LaunchedEffect(s.startedAt) {
        while (s.startedAt != null) { nowMs = System.currentTimeMillis(); delay(1000) }
    }
    val duration = s.startedAt?.let { formatDuration(nowMs - it) } ?: "00:00"

    // XÁC THỰC danh tính hiển thị từ DB nhân viên theo username (do server xác thực), thay vì tin tên
    // client tự khai trong tín hiệu. Tra hồ sơ thật (tên + ảnh) rồi để nó ghi đè placeholder.
    LaunchedEffect(s.peerUsername) { vm.resolveCallPeer(s.peerUsername) }
    val dbProfile = vm.callPeerProfile?.takeIf { it.username.equals(s.peerUsername, true) }
    val resolvedName = dbProfile?.displayName?.takeIf { it.isNotBlank() } ?: s.peerName
    val contactUi = ChatCallContactUi(
        id = s.peerUsername,
        name = resolvedName,
        initials = initialsOf(resolvedName),
        avatarUrl = dbProfile?.avatarUrl,
    )

    // Đã kết thúc + CHÍNH MÌNH vừa từ chối một cuộc gọi ĐẾN (chưa từng kết nối) → tắt overlay NGAY,
    // KHÔNG nhấp nháy màn trong-cuộc-gọi (trước đây bấm "Từ chối" trông như nghe máy rồi cúp).
    if (s.stage == CallManager.Stage.Ended && s.incoming && s.startedAt == null && s.endedReason == "declined") return

    Box(Modifier.fillMaxSize()) {
        when {
            // Cuộc gọi đã kết thúc (mọi trường hợp khác) → thẻ "đã kết thúc" gọn, không hiện lại nút gọi.
            s.stage == CallManager.Stage.Ended -> CallEndedScreen(contact = contactUi, message = endedMessage(s))
            // Cuộc gọi ĐẾN đang đổ chuông.
            s.incoming && s.stage == CallManager.Stage.Incoming -> {
                IncomingCallScreen(
                    contact = contactUi,
                    isVideo = s.media == CallManager.Media.Video,
                    onAccept = { ensurePermsThen(s.media == CallManager.Media.Video) { CallManager.accept() } },
                    onDecline = { CallManager.hangup("declined") },
                )
            }
            // Gọi VIDEO.
            s.media == CallManager.Media.Video -> {
                val remote = remember { SurfaceViewRenderer(ctx) }
                val local = remember { SurfaceViewRenderer(ctx) }
                DisposableEffect(Unit) {
                    remote.init(CallManager.eglContext, null)
                    remote.setScalingType(RendererCommon.ScalingType.SCALE_ASPECT_FILL)
                    remote.setEnableHardwareScaler(true)
                    local.init(CallManager.eglContext, null)
                    local.setScalingType(RendererCommon.ScalingType.SCALE_ASPECT_FILL)
                    local.setMirror(true)
                    local.setZOrderMediaOverlay(true)
                    CallManager.attachRenderers(local = local, remote = remote)
                    onDispose {
                        CallManager.detachRenderers()
                        runCatching { remote.release() }
                        runCatching { local.release() }
                    }
                }
                VideoCallScreen(
                    state = VideoCallUiState(
                        contact = contactUi,
                        status = statusText(s, video = true),
                        duration = duration,
                        connected = s.stage == CallManager.Stage.Active,
                        muted = s.muted,
                        speakerOn = s.speakerOn,
                        cameraOn = s.cameraOn,
                        remoteVideoOn = s.remoteVideoOn,
                    ),
                    onBack = { CallManager.hangup("ended") },
                    onToggleMute = { CallManager.toggleMute() },
                    onToggleSpeaker = { CallManager.toggleSpeaker() },
                    onSwitchCamera = { CallManager.switchCamera() },
                    onEndCall = { CallManager.hangup("ended") },
                    remoteVideo = { AndroidView(factory = { remote }, modifier = Modifier.fillMaxSize()) },
                    localVideo = { AndroidView(factory = { local }, modifier = Modifier.fillMaxSize()) },
                    onBeautyApply = { CallManager.setBeauty(it) },   // làm mịn da (GPU, bên kia cũng thấy)
                    onFilterApply = { CallManager.setCallFilter(it) }, // bộ lọc màu (GPU)
                )
            }
            // Gọi THOẠI.
            else -> {
                VoiceCallScreen(
                    state = VoiceCallUiState(
                        contact = contactUi,
                        status = statusText(s, video = false),
                        duration = duration,
                        connected = s.stage == CallManager.Stage.Active,
                        muted = s.muted,
                        speakerOn = s.speakerOn,
                    ),
                    onBack = { CallManager.hangup("ended") },
                    onSwitchToVideo = { ensurePermsThen(video = true) { CallManager.switchToVideo() } },
                    onToggleMute = { CallManager.toggleMute() },
                    onToggleSpeaker = { CallManager.toggleSpeaker() },
                    onEndCall = { CallManager.hangup("ended") },
                )
            }
        }
    }
}

/** Câu trạng thái ngắn cho màn "đã kết thúc" (thẻ gọn thay cho việc nhấp nháy màn trong-cuộc-gọi). */
private fun endedMessage(s: CallManager.Session): String = when (s.endedReason) {
    "busy" -> "Máy bận"
    "declined" -> if (s.incoming) "Bạn đã từ chối" else "Cuộc gọi bị từ chối"
    "no_answer" -> "Không có phản hồi"
    "missed" -> "Cuộc gọi nhỡ"
    "canceled" -> if (s.incoming) "Cuộc gọi nhỡ" else "Đã huỷ cuộc gọi"
    "no_permission" -> "Thiếu quyền micro/camera"
    "disconnected" -> "Mất kết nối"
    else -> if (s.startedAt != null) "Cuộc gọi đã kết thúc" else "Đã kết thúc"
}

private fun statusText(s: CallManager.Session, video: Boolean): String = when (s.stage) {
    // Chỉ hiện "Đang đổ chuông…" khi máy bên kia ĐÃ báo nhận được lời mời (remoteRinging); trước đó
    // vẫn là "Đang gọi…" (đang kết nối tới máy họ). Nhờ vậy trạng thái phản ánh đúng thực tế.
    CallManager.Stage.Outgoing -> if (s.remoteRinging) "Đang đổ chuông…" else "Đang gọi…"
    CallManager.Stage.Incoming -> if (video) "Cuộc gọi video đến" else "Cuộc gọi thoại đến"
    CallManager.Stage.Connecting -> if (s.networkQuality == "Đang kết nối lại") "Đang kết nối lại…" else "Đang kết nối…"
    CallManager.Stage.Active -> (if (video) "Đang gọi video" else "Đang gọi thoại") + " · Mạng ${s.networkQuality.lowercase()}"
    CallManager.Stage.Ended -> when (s.endedReason) {
        "busy" -> "Máy bận"
        "declined" -> "Cuộc gọi bị từ chối"
        "no_answer" -> "Không có phản hồi"
        "missed" -> "Cuộc gọi nhỡ"
        "no_permission" -> "Thiếu quyền micro/camera"
        else -> "Đã kết thúc"
    }
    CallManager.Stage.Idle -> ""
}

private fun formatDuration(ms: Long): String {
    val total = (ms / 1000).coerceAtLeast(0)
    val m = total / 60
    val sec = total % 60
    return "%02d:%02d".format(m, sec)
}

private fun initialsOf(name: String): String {
    val parts = name.trim().split(" ", ".", "_").filter { it.isNotBlank() }
    return when {
        parts.isEmpty() -> "?"
        parts.size == 1 -> parts[0].take(2).uppercase()
        else -> (parts[parts.size - 2].take(1) + parts.last().take(1)).uppercase()
    }
}
