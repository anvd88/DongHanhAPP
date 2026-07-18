package com.ketoanapk.hr.ui

import android.graphics.BitmapFactory
import android.os.Build
import android.util.Base64
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.VolumeUp
import androidx.compose.material.icons.filled.AutoAwesome
import androidx.compose.material.icons.filled.Call
import androidx.compose.material.icons.filled.CallEnd
import androidx.compose.material.icons.filled.Cameraswitch
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.MicOff
import androidx.compose.material.icons.filled.Videocam
import androidx.compose.material.icons.filled.VideocamOff
import androidx.compose.material.icons.filled.VolumeOff
import androidx.compose.material3.Icon
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.roundToInt

/* =============================================================================
   Màn hình GỌI THOẠI / GỌI VIDEO — thiết kế cao cấp (dark, kính mờ glassmorphism).
   Nền chuyển sắc xanh-đêm + quầng sáng sau avatar; điều khiển là các nút kính tròn
   nổi; cuộc gọi đến có 2 nút lớn tách biệt rõ (từ chối / trả lời) với hoạt ảnh riêng.
   Màn video có bảng HIỆU ỨNG: Làm đẹp (mịn da) · Bộ lọc màu · Sticker dán kéo-thả.
   Khung hình video truyền vào qua slot [remoteVideo]/[localVideo].
   ========================================================================== */

data class ChatCallContactUi(
    val id: String = "am",
    val name: String = "Nguyễn Anh Minh",
    val initials: String = "AM",
    val avatarUrl: String? = null,
)

data class VoiceCallUiState(
    val contact: ChatCallContactUi = ChatCallContactUi(),
    val status: String = "Đang gọi thoại",
    val duration: String = "02:18",
    val connected: Boolean = true,
    val muted: Boolean = false,
    val speakerOn: Boolean = false,
)

data class VideoCallUiState(
    val contact: ChatCallContactUi = ChatCallContactUi(),
    val status: String = "Đang gọi video",
    val duration: String = "02:18",
    val connected: Boolean = true,
    val muted: Boolean = false,
    val speakerOn: Boolean = true,
    val cameraOn: Boolean = true,
    val remoteVideoOn: Boolean = false,
    val selfLabel: String = "Tôi",
)

/** Bộ lọc màu áp lên khung hình (hiển thị phía máy mình). */
private enum class CallFilter(val label: String, val scrim: Brush?) {
    None("Gốc", null),
    Warm("Nắng ấm", Brush.linearGradient(listOf(Color(0x33FF9A3D), Color(0x22FF5E62)))),
    Cool("Trong xanh", Brush.linearGradient(listOf(Color(0x2A2AD1FF), Color(0x2A4C6BFF)))),
    Pink("Hồng đào", Brush.linearGradient(listOf(Color(0x33FF7EB3), Color(0x22FF758C)))),
    Mono("Cổ điển", Brush.linearGradient(listOf(Color(0x40202024), Color(0x30403A33)))),
    Dreamy("Mộng mơ", Brush.linearGradient(listOf(Color(0x306D5BFF), Color(0x2AFF6FD8)))),
}

private val StickerChoices = listOf("😎", "🥰", "😂", "🐻", "🌟", "🔥", "🎉", "👑", "🌈", "💙", "🌸", "🦄")

/* ----------------------------------------------------------------------------
   GỌI THOẠI
   -------------------------------------------------------------------------- */

@Composable
fun VoiceCallScreen(
    state: VoiceCallUiState,
    onBack: () -> Unit,
    onSwitchToVideo: () -> Unit,
    onToggleMute: () -> Unit,
    onToggleSpeaker: () -> Unit,
    onEndCall: () -> Unit,
    modifier: Modifier = Modifier,
) {
    CallBackdrop(modifier) {
        SwitchToVideoButton(
            onClick = onSwitchToVideo,
            modifier = Modifier
                .align(Alignment.TopEnd)
                .statusBarsPadding()
                .padding(top = 12.dp, end = 16.dp),
        )

        Column(
            modifier = Modifier.fillMaxSize().padding(horizontal = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Spacer(Modifier.weight(0.6f))
            ContactAvatar(contact = state.contact, size = 136, pulsing = !state.connected)
            Spacer(Modifier.height(28.dp))
            Text(
                text = state.contact.name,
                color = CallTextPrimary,
                fontSize = 26.sp,
                fontWeight = FontWeight.SemiBold,
                textAlign = TextAlign.Center,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Spacer(Modifier.height(10.dp))
            StatusPill(text = if (state.connected) state.duration else state.status, live = state.connected)
            Spacer(Modifier.weight(1.4f))
        }

        GlassControlBar(modifier = Modifier.align(Alignment.BottomCenter)) {
            GlassCallButton(
                icon = if (state.muted) Icons.Filled.MicOff else Icons.Filled.Mic,
                label = "Micro",
                active = state.muted,
                onClick = onToggleMute,
            )
            GlassCallButton(
                icon = if (state.speakerOn) Icons.AutoMirrored.Filled.VolumeUp else Icons.Filled.VolumeOff,
                label = "Loa ngoài",
                active = state.speakerOn,
                onClick = onToggleSpeaker,
            )
            GlassCallButton(
                icon = Icons.Filled.CallEnd,
                label = "Kết thúc",
                variant = CallButtonVariant.Danger,
                onClick = onEndCall,
            )
        }
    }
}

/* ----------------------------------------------------------------------------
   GỌI VIDEO
   -------------------------------------------------------------------------- */

private class StickerInstance(val id: Long, val emoji: String) {
    var x by mutableFloatStateOf(0f)
    var y by mutableFloatStateOf(0f)
}

@Composable
fun VideoCallScreen(
    state: VideoCallUiState,
    onBack: () -> Unit,
    onToggleMute: () -> Unit,
    onToggleSpeaker: () -> Unit,
    onSwitchCamera: () -> Unit,
    onEndCall: () -> Unit,
    modifier: Modifier = Modifier,
    remoteVideo: (@Composable () -> Unit)? = null,
    localVideo: (@Composable () -> Unit)? = null,
    onBeautyApply: (Float) -> Unit = {},   // áp làm mịn da vào luồng gửi (GPU) — bên kia cũng thấy
    onFilterApply: (Int) -> Unit = {},     // áp bộ lọc màu vào luồng gửi (GPU)
) {
    // Làm đẹp/bộ lọc áp THẬT bằng GPU (bên kia thấy) → khung tự xem hiển thị đúng, khỏi phủ giả.
    // Sticker vẫn là lớp dán trên màn của mình (kéo-thả).
    var showEffects by remember { mutableStateOf(false) }
    var beauty by remember { mutableFloatStateOf(0f) }
    var filter by remember { mutableStateOf(CallFilter.None) }
    val stickers = remember { mutableStateListOf<StickerInstance>() }

    Box(modifier = modifier.fillMaxSize().background(CallBgBottom)) {
        // 1) Khung hình đối phương (toàn màn) hoặc avatar khi chưa có video.
        if (state.remoteVideoOn && remoteVideo != null) {
            Box(Modifier.fillMaxSize()) { remoteVideo() }
        } else {
            CallBackdrop {
                Column(
                    modifier = Modifier.fillMaxSize().padding(horizontal = 24.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Spacer(Modifier.weight(0.6f))
                    ContactAvatar(contact = state.contact, size = 128, pulsing = !state.connected)
                    Spacer(Modifier.height(24.dp))
                    Text(
                        text = state.contact.name,
                        color = CallTextPrimary,
                        fontSize = 24.sp,
                        fontWeight = FontWeight.SemiBold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Spacer(Modifier.weight(1.4f))
                }
            }
        }

        // (Bộ lọc + làm mịn da được áp bằng GPU vào chính luồng camera của mình — hiển thị ở khung tự
        //  xem và truyền sang bên kia; KHÔNG phủ màu lên khung của đối phương.)

        // Sticker dán (kéo-thả tự do) — lớp trên màn của mình.
        stickers.forEach { st ->
            Text(
                text = st.emoji,
                fontSize = 60.sp,
                modifier = Modifier
                    .offset { IntOffset(st.x.roundToInt(), st.y.roundToInt()) }
                    .padding(20.dp)
                    .clickable(
                        interactionSource = remember { MutableInteractionSource() },
                        indication = null,
                    ) { }
                    .pointerDrag { dx, dy -> st.x += dx; st.y += dy },
            )
        }

        // Lớp làm tối nhẹ ở trên/dưới để chữ + nút luôn nổi trên nền video sáng.
        Box(
            Modifier.fillMaxSize().background(
                Brush.verticalGradient(
                    0f to Color(0x66000000), 0.22f to Color.Transparent,
                    0.75f to Color.Transparent, 1f to Color(0x99000000),
                ),
            ),
        )

        // Thanh trên: thu nhỏ · tiêu đề+đồng hồ · đổi camera.
        Row(
            modifier = Modifier
                .align(Alignment.TopCenter)
                .fillMaxWidth()
                .statusBarsPadding()
                .padding(horizontal = 10.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            GlassIconButton(Icons.Filled.ExpandMore, "Thu nhỏ", onBack)
            Column(Modifier.weight(1f), horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    text = state.status,
                    color = CallTextPrimary,
                    fontSize = 16.sp,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = if (state.connected) state.duration else "Đang kết nối…",
                    color = CallTextSecondary,
                    fontSize = 13.sp,
                )
            }
            GlassIconButton(Icons.Filled.Cameraswitch, "Đổi camera", onSwitchCamera)
        }

        // Khung hình của mình (self preview) — có lớp làm mịn da khi bật Làm đẹp.
        SelfPreviewCard(
            label = state.selfLabel,
            cameraOn = state.cameraOn,
            localVideo = localVideo,
            modifier = Modifier
                .align(Alignment.TopEnd)
                .statusBarsPadding()
                .padding(top = 64.dp, end = 16.dp),
        )

        // Bảng hiệu ứng (trượt lên) HOẶC thanh điều khiển chính.
        Box(Modifier.align(Alignment.BottomCenter).fillMaxWidth()) {
            androidx.compose.animation.AnimatedVisibility(
                visible = !showEffects,
                enter = fadeIn(), exit = fadeOut(),
            ) {
                GlassControlBar {
                    GlassCallButton(
                        icon = if (state.muted) Icons.Filled.MicOff else Icons.Filled.Mic,
                        label = "Micro", active = state.muted, onClick = onToggleMute,
                    )
                    GlassCallButton(
                        icon = Icons.Filled.AutoAwesome,
                        label = "Hiệu ứng",
                        active = beauty > 0f || filter != CallFilter.None || stickers.isNotEmpty(),
                        onClick = { showEffects = true },
                    )
                    GlassCallButton(
                        icon = if (state.speakerOn) Icons.AutoMirrored.Filled.VolumeUp else Icons.Filled.VolumeOff,
                        label = "Loa ngoài", active = state.speakerOn, onClick = onToggleSpeaker,
                    )
                    GlassCallButton(
                        icon = Icons.Filled.CallEnd, label = "Kết thúc",
                        variant = CallButtonVariant.Danger, onClick = onEndCall,
                    )
                }
            }
            androidx.compose.animation.AnimatedVisibility(
                visible = showEffects,
                enter = slideInVertically { it } + fadeIn(),
                exit = slideOutVertically { it } + fadeOut(),
            ) {
                EffectsPanel(
                    beauty = beauty,
                    filter = filter,
                    onBeautyChange = { beauty = it; onBeautyApply(it) },
                    onFilterChange = { filter = it; onFilterApply(it.ordinal) },
                    onPickSticker = { emoji ->
                        if (stickers.size >= 6) stickers.removeAt(0)
                        stickers.add(StickerInstance(System.nanoTime(), emoji))
                    },
                    onClearStickers = { stickers.clear() },
                    onClose = { showEffects = false },
                )
            }
        }
    }
}

/* ----------------------------------------------------------------------------
   CUỘC GỌI ĐẾN (đổ chuông) — 2 nút lớn tách biệt, hoạt ảnh riêng cho mỗi hành động.
   -------------------------------------------------------------------------- */

@Composable
fun IncomingCallScreen(
    contact: ChatCallContactUi,
    isVideo: Boolean,
    onAccept: () -> Unit,
    onDecline: () -> Unit,
    modifier: Modifier = Modifier,
) {
    CallBackdrop(modifier) {
        Column(
            modifier = Modifier.fillMaxSize().padding(horizontal = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Spacer(Modifier.weight(0.5f))
            Text(
                text = if (isVideo) "CUỘC GỌI VIDEO ĐẾN" else "CUỘC GỌI THOẠI ĐẾN",
                color = CallAccent,
                fontSize = 13.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 2.sp,
            )
            Spacer(Modifier.height(28.dp))
            ContactAvatar(contact = contact, size = 132, pulsing = true)
            Spacer(Modifier.height(26.dp))
            Text(
                text = contact.name,
                color = CallTextPrimary,
                fontSize = 27.sp,
                fontWeight = FontWeight.SemiBold,
                textAlign = TextAlign.Center,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Spacer(Modifier.height(8.dp))
            Text("đang gọi cho bạn…", color = CallTextSecondary, fontSize = 15.sp)
            Spacer(Modifier.weight(1.5f))
        }

        Row(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .navigationBarsPadding()
                .padding(horizontal = 52.dp, vertical = 46.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.Top,
        ) {
            IncomingAction(
                icon = Icons.Filled.CallEnd,
                label = "Từ chối",
                container = CallEndRed,
                wobble = true,
                onClick = onDecline,
            )
            IncomingAction(
                icon = if (isVideo) Icons.Filled.Videocam else Icons.Filled.Call,
                label = "Trả lời",
                container = CallAcceptGreen,
                pulse = true,
                onClick = onAccept,
            )
        }
    }
}

/* ----------------------------------------------------------------------------
   TRẠNG THÁI KẾT THÚC — thẻ gọn, thay cho việc nhấp nháy màn trong-cuộc-gọi.
   -------------------------------------------------------------------------- */

@Composable
fun CallEndedScreen(contact: ChatCallContactUi, message: String, modifier: Modifier = Modifier) {
    CallBackdrop(modifier) {
        Column(
            modifier = Modifier.fillMaxSize().padding(horizontal = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Spacer(Modifier.weight(0.62f))
            ContactAvatar(contact = contact, size = 116, pulsing = false, dim = true)
            Spacer(Modifier.height(22.dp))
            Text(
                text = contact.name,
                color = CallTextPrimary.copy(alpha = 0.85f),
                fontSize = 22.sp,
                fontWeight = FontWeight.SemiBold,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Spacer(Modifier.height(10.dp))
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Filled.CallEnd, contentDescription = null, tint = CallEndRed, modifier = Modifier.size(18.dp))
                Text(message, color = CallTextSecondary, fontSize = 15.sp)
            }
            Spacer(Modifier.weight(1.4f))
        }
    }
}

/* ----------------------------------------------------------------------------
   Bảng HIỆU ỨNG
   -------------------------------------------------------------------------- */

@Composable
private fun EffectsPanel(
    beauty: Float,
    filter: CallFilter,
    onBeautyChange: (Float) -> Unit,
    onFilterChange: (CallFilter) -> Unit,
    onPickSticker: (String) -> Unit,
    onClearStickers: () -> Unit,
    onClose: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .navigationBarsPadding()
            .padding(horizontal = 12.dp, vertical = 12.dp)
            .clip(RoundedCornerShape(26.dp))
            .background(CallSheet)
            .border(1.dp, CallHairline, RoundedCornerShape(26.dp))
            .padding(horizontal = 18.dp, vertical = 16.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.AutoAwesome, contentDescription = null, tint = CallAccent, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(8.dp))
            Text("Hiệu ứng làm đẹp", color = CallTextPrimary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
            GlassIconButton(Icons.Filled.Close, "Đóng", onClose, small = true)
        }

        Spacer(Modifier.height(16.dp))
        // Làm mịn da
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Mịn da", color = CallTextSecondary, fontSize = 14.sp, modifier = Modifier.width(64.dp))
            Slider(
                value = beauty,
                onValueChange = onBeautyChange,
                modifier = Modifier.weight(1f),
                colors = SliderDefaults.colors(
                    thumbColor = CallAccent,
                    activeTrackColor = CallAccent,
                    inactiveTrackColor = CallGlassStrong,
                ),
            )
            Text("${(beauty * 100).roundToInt()}%", color = CallTextPrimary, fontSize = 13.sp, modifier = Modifier.width(44.dp), textAlign = TextAlign.End)
        }

        Spacer(Modifier.height(10.dp))
        Text("Bộ lọc", color = CallTextSecondary, fontSize = 14.sp)
        Spacer(Modifier.height(8.dp))
        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            CallFilter.entries.forEach { f ->
                FilterChip(filter = f, selected = f == filter, onClick = { onFilterChange(f) })
            }
        }

        Spacer(Modifier.height(16.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Sticker", color = CallTextSecondary, fontSize = 14.sp, modifier = Modifier.weight(1f))
            Text("Xoá hết", color = CallAccent, fontSize = 13.sp, fontWeight = FontWeight.Medium,
                modifier = Modifier.clip(RoundedCornerShape(8.dp)).clickable(onClick = onClearStickers).padding(horizontal = 8.dp, vertical = 4.dp))
        }
        Spacer(Modifier.height(8.dp))
        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            StickerChoices.forEach { emoji ->
                Box(
                    modifier = Modifier
                        .size(52.dp)
                        .clip(RoundedCornerShape(16.dp))
                        .background(CallGlass)
                        .clickable { onPickSticker(emoji) },
                    contentAlignment = Alignment.Center,
                ) {
                    Text(emoji, fontSize = 26.sp)
                }
            }
        }
    }
}

@Composable
private fun FilterChip(filter: CallFilter, selected: Boolean, onClick: () -> Unit) {
    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Box(
            modifier = Modifier
                .size(58.dp)
                .clip(RoundedCornerShape(16.dp))
                .background(Brush.linearGradient(listOf(CallAvatarTop, CallAvatarBottom)))
                .then(if (selected) Modifier.border(2.5.dp, CallAccent, RoundedCornerShape(16.dp)) else Modifier)
                .clickable(onClick = onClick),
        ) {
            filter.scrim?.let { Box(Modifier.fillMaxSize().clip(RoundedCornerShape(16.dp)).background(it)) }
        }
        Text(
            filter.label,
            color = if (selected) CallAccent else CallTextSecondary,
            fontSize = 11.sp,
            fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal,
            maxLines = 1,
        )
    }
}

/* ----------------------------------------------------------------------------
   Thành phần dùng chung
   -------------------------------------------------------------------------- */

private enum class CallButtonVariant { Normal, Danger }

/** Nền chuyển sắc xanh-đêm + quầng sáng mềm phía sau nội dung. */
@Composable
private fun CallBackdrop(modifier: Modifier = Modifier, content: @Composable BoxScope.() -> Unit) {
    Box(
        modifier = modifier
            .fillMaxSize()
            .background(Brush.verticalGradient(listOf(CallBgTop, CallBgMid, CallBgBottom))),
    ) {
        // Quầng sáng mềm ở khoảng 1/3 trên.
        Box(
            Modifier
                .align(Alignment.TopCenter)
                .padding(top = 90.dp)
                .size(300.dp)
                .blur(90.dp)
                .clip(CircleShape)
                .background(CallGlow.copy(alpha = 0.22f)),
        )
        content()
    }
}

private typealias BoxScope = androidx.compose.foundation.layout.BoxScope

/** Thanh điều khiển kính mờ nổi ở đáy. */
@Composable
private fun GlassControlBar(modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    Row(
        modifier = modifier
            .fillMaxWidth()
            .navigationBarsPadding()
            .padding(horizontal = 16.dp, vertical = 26.dp)
            .clip(RoundedCornerShape(30.dp))
            .background(CallGlass)
            .border(1.dp, CallHairline, RoundedCornerShape(30.dp))
            .padding(vertical = 16.dp, horizontal = 8.dp),
        horizontalArrangement = Arrangement.SpaceEvenly,
        verticalAlignment = Alignment.Top,
    ) {
        content()
    }
}

@Composable
private fun GlassCallButton(
    icon: ImageVector,
    label: String,
    onClick: () -> Unit,
    active: Boolean = false,
    variant: CallButtonVariant = CallButtonVariant.Normal,
) {
    val danger = variant == CallButtonVariant.Danger
    val container = when {
        danger -> CallEndRed
        active -> CallAccent
        else -> CallGlassStrong
    }
    val content = when {
        danger || active -> Color.White
        else -> CallTextPrimary
    }
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val pressScale by animateFloatAsState(if (pressed) 0.88f else 1f, label = "press")
    Column(
        modifier = Modifier.clickable(interactionSource = interaction, indication = null, onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(9.dp),
    ) {
        Box(
            modifier = Modifier
                .scale(pressScale)
                .size(58.dp)
                .shadow(if (danger) 14.dp else 0.dp, CircleShape, clip = false, spotColor = CallEndRed)
                .clip(CircleShape)
                .background(container)
                .then(if (!danger && !active) Modifier.border(1.dp, CallHairline, CircleShape) else Modifier),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, contentDescription = label, tint = content, modifier = Modifier.size(25.dp))
        }
        Text(label, color = CallTextSecondary, fontSize = 12.sp, maxLines = 1)
    }
}

/** Nút kính tròn nhỏ trong suốt (thanh trên / đóng bảng). */
@Composable
private fun GlassIconButton(icon: ImageVector, description: String, onClick: () -> Unit, small: Boolean = false) {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val pressScale by animateFloatAsState(if (pressed) 0.85f else 1f, label = "press")
    val d = if (small) 36.dp else 42.dp
    Box(
        modifier = Modifier
            .scale(pressScale)
            .size(d)
            .clip(CircleShape)
            .background(CallGlass)
            .border(1.dp, CallHairline, CircleShape)
            .clickable(interactionSource = interaction, indication = null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = description, tint = CallTextPrimary, modifier = Modifier.size(if (small) 18.dp else 22.dp))
    }
}

/** Nút hành động lớn cho cuộc gọi đến — Từ chối (lắc nhẹ) / Trả lời (nhịp thở). */
@Composable
private fun IncomingAction(
    icon: ImageVector,
    label: String,
    container: Color,
    onClick: () -> Unit,
    pulse: Boolean = false,
    wobble: Boolean = false,
) {
    val transition = rememberInfiniteTransition(label = "act")
    val pulseScale by transition.animateFloat(
        1f, if (pulse) 1.08f else 1f,
        infiniteRepeatable(tween(900), RepeatMode.Reverse), label = "pulse",
    )
    val wob by transition.animateFloat(
        -1f, if (wobble) 1f else -1f,
        infiniteRepeatable(tween(1400), RepeatMode.Reverse), label = "wob",
    )
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val pressScale by animateFloatAsState(if (pressed) 0.9f else 1f, label = "press")
    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(12.dp),
        modifier = Modifier.clickable(interactionSource = interaction, indication = null, onClick = onClick),
    ) {
        Box(
            modifier = Modifier
                .scale(pressScale * if (pulse) pulseScale else 1f)
                .size(72.dp)
                .shadow(18.dp, CircleShape, clip = false, spotColor = container)
                .clip(CircleShape)
                .background(container),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                icon,
                contentDescription = label,
                tint = Color.White,
                modifier = Modifier
                    .size(30.dp)
                    .then(if (wobble) Modifier.offset { IntOffset(0, (wob * 2).roundToInt()) } else Modifier),
            )
        }
        Text(label, color = CallTextPrimary, fontSize = 14.sp, fontWeight = FontWeight.Medium)
    }
}

/** Chip trạng thái (đồng hồ đang chạy có chấm xanh nhấp nháy). */
@Composable
private fun StatusPill(text: String, live: Boolean) {
    Row(
        modifier = Modifier
            .clip(RoundedCornerShape(999.dp))
            .background(CallGlass)
            .border(1.dp, CallHairline, RoundedCornerShape(999.dp))
            .padding(horizontal = 14.dp, vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        if (live) {
            val t = rememberInfiniteTransition(label = "dot")
            val a by t.animateFloat(0.3f, 1f, infiniteRepeatable(tween(800), RepeatMode.Reverse), label = "a")
            Box(Modifier.size(8.dp).clip(CircleShape).background(CallAcceptGreen.copy(alpha = a)))
        }
        Text(text, color = CallTextPrimary, fontSize = 14.sp, fontWeight = FontWeight.Medium)
    }
}

/** Nút "chuyển sang gọi video" ở góc trên bên phải màn gọi thoại. */
@Composable
private fun SwitchToVideoButton(onClick: () -> Unit, modifier: Modifier = Modifier) {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val pressScale by animateFloatAsState(if (pressed) 0.92f else 1f, label = "press")
    Row(
        modifier = modifier
            .scale(pressScale)
            .clip(RoundedCornerShape(999.dp))
            .background(CallGlass)
            .border(1.dp, CallHairline, RoundedCornerShape(999.dp))
            .clickable(interactionSource = interaction, indication = null, onClick = onClick)
            .padding(start = 12.dp, end = 14.dp, top = 8.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Icon(Icons.Filled.Videocam, contentDescription = "Chuyển sang gọi video", tint = CallAccent, modifier = Modifier.size(20.dp))
        Text("Video", color = CallTextPrimary, fontSize = 13.sp, fontWeight = FontWeight.Medium)
    }
}

@Composable
private fun SelfPreviewCard(
    label: String,
    cameraOn: Boolean,
    localVideo: (@Composable () -> Unit)?,
    modifier: Modifier = Modifier,
) {
    Box(
        modifier = modifier
            .width(108.dp)
            .height(156.dp)
            .shadow(12.dp, RoundedCornerShape(20.dp), clip = false)
            .clip(RoundedCornerShape(20.dp))
            .background(CallBgMid)
            .border(1.dp, CallHairline, RoundedCornerShape(20.dp)),
        contentAlignment = Alignment.Center,
    ) {
        if (cameraOn && localVideo != null) {
            // Khung tự xem đã là ảnh ĐÃ XỬ LÝ (làm mịn/bộ lọc áp bằng GPU ở nguồn) — không cần phủ giả.
            Box(Modifier.fillMaxSize().clip(RoundedCornerShape(20.dp))) { localVideo() }
        } else {
            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Filled.VideocamOff, contentDescription = null, tint = CallTextSecondary, modifier = Modifier.size(26.dp))
                Text(label, color = CallTextSecondary, fontSize = 12.sp)
            }
        }
        Text(
            label,
            color = Color.White,
            fontSize = 11.sp,
            fontWeight = FontWeight.Medium,
            modifier = Modifier
                .align(Alignment.BottomStart)
                .padding(8.dp)
                .clip(RoundedCornerShape(8.dp))
                .background(Color(0x66000000))
                .padding(horizontal = 8.dp, vertical = 3.dp),
        )
    }
}

/**
 * Avatar tròn: ảnh thật nếu có [ChatCallContactUi.avatarUrl] (data URL); nếu không thì nền chuyển sắc +
 * chữ cái đầu. Khi đổ chuông ([pulsing]) hiện các vòng sóng lan tỏa quanh avatar.
 */
@Composable
private fun ContactAvatar(contact: ChatCallContactUi, size: Int, pulsing: Boolean, dim: Boolean = false, modifier: Modifier = Modifier) {
    Box(modifier = modifier.size((size + 56).dp), contentAlignment = Alignment.Center) {
        if (pulsing) {
            val transition = rememberInfiniteTransition(label = "ring")
            listOf(0, 800).forEach { delayMs ->
                val ringScale by transition.animateFloat(
                    1f, 1.42f,
                    infiniteRepeatable(tween(1800), RepeatMode.Restart, initialStartOffset = androidx.compose.animation.core.StartOffset(delayMs)),
                    label = "rs$delayMs",
                )
                val ringAlpha by transition.animateFloat(
                    0.34f, 0f,
                    infiniteRepeatable(tween(1800), RepeatMode.Restart, initialStartOffset = androidx.compose.animation.core.StartOffset(delayMs)),
                    label = "ra$delayMs",
                )
                Box(
                    Modifier.size(size.dp).scale(ringScale).clip(CircleShape)
                        .background(CallGlow.copy(alpha = ringAlpha)),
                )
            }
        }

        val bitmap = remember(contact.avatarUrl) { decodeDataImage(contact.avatarUrl) }
        Box(
            modifier = Modifier
                .size(size.dp)
                .shadow(20.dp, CircleShape, clip = false, spotColor = CallGlow)
                .clip(CircleShape)
                .background(Brush.linearGradient(listOf(CallAvatarTop, CallAvatarBottom)))
                .border(2.dp, Color.White.copy(alpha = 0.14f), CircleShape),
            contentAlignment = Alignment.Center,
        ) {
            if (bitmap != null) {
                Image(
                    bitmap = bitmap,
                    contentDescription = contact.name,
                    modifier = Modifier.fillMaxSize().clip(CircleShape),
                    contentScale = ContentScale.Crop,
                )
            } else {
                Text(
                    text = contact.initials,
                    color = Color.White,
                    fontSize = (size * 0.34f).sp,
                    fontWeight = FontWeight.Bold,
                    textAlign = TextAlign.Center,
                    maxLines = 1,
                )
            }
            if (dim) Box(Modifier.fillMaxSize().clip(CircleShape).background(Color(0x66000000)))
        }
    }
}

/** Kéo-thả đơn giản: cập nhật offset theo delta di chuyển. */
private fun Modifier.pointerDrag(onDrag: (Float, Float) -> Unit): Modifier =
    this.pointerInput(Unit) {
        detectDragGestures { change, dragAmount ->
            change.consume()
            onDrag(dragAmount.x, dragAmount.y)
        }
    }

/** Giải mã ảnh đại diện dạng data URL thành ImageBitmap; lỗi/không phải → null. */
private fun decodeDataImage(url: String?): ImageBitmap? {
    if (url.isNullOrBlank() || !url.startsWith("data:")) return null
    val comma = url.indexOf(',')
    if (comma < 0) return null
    return runCatching {
        val bytes = Base64.decode(url.substring(comma + 1), Base64.DEFAULT)
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
    }.getOrNull()
}


// --- Bảng màu (dark premium) ---
private val CallBgTop = Color(0xFF1A2A4C)
private val CallBgMid = Color(0xFF111A2E)
private val CallBgBottom = Color(0xFF070A12)
private val CallGlow = Color(0xFF3B82F6)
private val CallAccent = Color(0xFF4C9BFF)
private val CallGlass = Color(0x1FFFFFFF)         // trắng ~12%
private val CallGlassStrong = Color(0x33FFFFFF)   // trắng ~20%
private val CallHairline = Color(0x26FFFFFF)      // viền mảnh
private val CallSheet = Color(0xF21A2238)         // nền bảng hiệu ứng (đục)
private val CallEndRed = Color(0xFFFF3B44)
private val CallAcceptGreen = Color(0xFF25C561)
private val CallAvatarTop = Color(0xFF5AA0FF)
private val CallAvatarBottom = Color(0xFF2A6BE0)
private val CallTextPrimary = Color(0xFFFFFFFF)
private val CallTextSecondary = Color(0xFFA7B0C4)
