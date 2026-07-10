package com.ketoanapk.hr.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.VolumeUp
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CallEnd
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.MicOff
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Videocam
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

data class ChatCallContactUi(
    val id: String = "am",
    val name: String = "Nguyễn Anh Minh",
    val initials: String = "AM",
    val subtitle: String = "Kế toán tổng hợp",
)

enum class CallConnectionTone {
    Good,
    Warning,
    Poor,
}

data class CallConnectionUi(
    val label: String = "Tốt",
    val detail: String = "HD · 42 ms",
    val level: Int = 3,
    val tone: CallConnectionTone = CallConnectionTone.Good,
)

data class VoiceCallUiState(
    val contact: ChatCallContactUi = ChatCallContactUi(),
    val status: String = "Đang gọi thoại",
    val duration: String = "02:18",
    val connection: CallConnectionUi = CallConnectionUi(),
    val muted: Boolean = false,
    val speakerOn: Boolean = false,
)

data class VideoCallUiState(
    val contact: ChatCallContactUi = ChatCallContactUi(),
    val status: String = "Đang gọi video",
    val duration: String = "02:18",
    val connection: CallConnectionUi = CallConnectionUi(),
    val muted: Boolean = false,
    val cameraOn: Boolean = false,
    val speakerOn: Boolean = true,
    val selfPreviewLabel: String = "Bạn",
)

@Composable
fun VoiceCallScreen(
    state: VoiceCallUiState,
    onBack: () -> Unit,
    onMore: () -> Unit,
    onToggleMute: () -> Unit,
    onToggleSpeaker: () -> Unit,
    onAddParticipant: () -> Unit,
    onSwitchToVideo: () -> Unit,
    onEndCall: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxSize()
            .background(CallBackground),
    ) {
        CallTopBar(
            connection = state.connection,
            onBack = onBack,
            onMore = onMore,
        )
        Column(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
                .padding(horizontal = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            CallAvatar(initials = state.contact.initials, size = 116)
            Spacer(Modifier.height(22.dp))
            Text(
                text = state.contact.name,
                color = CallTextPrimary,
                style = MaterialTheme.typography.headlineSmall,
                textAlign = TextAlign.Center,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = state.status,
                color = CallTextSecondary,
                style = MaterialTheme.typography.bodyLarge,
                modifier = Modifier.padding(top = 6.dp),
            )
            Text(
                text = state.duration,
                color = CallTextPrimary,
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(top = 10.dp),
            )
            AudioWaveform(modifier = Modifier.padding(top = 28.dp))
        }
        VoiceCallControls(
            muted = state.muted,
            speakerOn = state.speakerOn,
            onToggleMute = onToggleMute,
            onToggleSpeaker = onToggleSpeaker,
            onAddParticipant = onAddParticipant,
            onSwitchToVideo = onSwitchToVideo,
            onEndCall = onEndCall,
        )
    }
}

@Composable
fun VideoCallScreen(
    state: VideoCallUiState,
    onBack: () -> Unit,
    onMore: () -> Unit,
    onToggleMute: () -> Unit,
    onToggleCamera: () -> Unit,
    onSwitchCamera: () -> Unit,
    onToggleSpeaker: () -> Unit,
    onEndCall: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Box(
        modifier = modifier
            .fillMaxSize()
            .background(CallVideoBackground),
    ) {
        VideoPlaceholder(
            contact = state.contact,
            cameraOn = state.cameraOn,
            modifier = Modifier.fillMaxSize(),
        )
        VideoCallHeader(
            state = state,
            onBack = onBack,
            onMore = onMore,
            modifier = Modifier.align(Alignment.TopCenter),
        )
        SelfPreview(
            label = state.selfPreviewLabel,
            modifier = Modifier
                .align(Alignment.TopEnd)
                .statusBarsPadding()
                .padding(top = 88.dp, end = 16.dp),
        )
        VideoCallControls(
            muted = state.muted,
            cameraOn = state.cameraOn,
            speakerOn = state.speakerOn,
            onToggleMute = onToggleMute,
            onToggleCamera = onToggleCamera,
            onSwitchCamera = onSwitchCamera,
            onToggleSpeaker = onToggleSpeaker,
            onEndCall = onEndCall,
            modifier = Modifier.align(Alignment.BottomCenter),
        )
    }
}

@Composable
private fun CallTopBar(
    connection: CallConnectionUi,
    onBack: () -> Unit,
    onMore: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .statusBarsPadding()
            .padding(start = 4.dp, end = 4.dp, top = 8.dp, bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(onClick = onBack) {
            Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Thu nhỏ", tint = CallTextPrimary)
        }
        Spacer(Modifier.weight(1f))
        CompactConnectionIndicator(connection)
        IconButton(onClick = onMore) {
            Icon(Icons.Filled.MoreVert, contentDescription = "Tùy chọn", tint = CallTextPrimary)
        }
    }
}

@Composable
private fun VideoCallHeader(
    state: VideoCallUiState,
    onBack: () -> Unit,
    onMore: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(
                Brush.verticalGradient(
                    colors = listOf(Color.White.copy(alpha = 0.94f), Color.White.copy(alpha = 0.18f)),
                ),
            )
            .statusBarsPadding()
            .padding(start = 4.dp, end = 4.dp, top = 8.dp, bottom = 18.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onBack) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Thu nhỏ", tint = CallTextPrimary)
            }
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = state.contact.name,
                    color = CallTextPrimary,
                    style = MaterialTheme.typography.titleMedium,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = "${state.status} · ${state.duration}",
                    color = CallTextSecondary,
                    style = MaterialTheme.typography.bodySmall,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            CompactConnectionIndicator(state.connection)
            IconButton(onClick = onMore) {
                Icon(Icons.Filled.MoreVert, contentDescription = "Tùy chọn", tint = CallTextPrimary)
            }
        }
    }
}

@Composable
private fun VideoPlaceholder(
    contact: ChatCallContactUi,
    cameraOn: Boolean,
    modifier: Modifier = Modifier,
) {
    Box(
        modifier = modifier
            .background(
                Brush.verticalGradient(
                    colors = listOf(Color(0xFFF8FAFC), Color(0xFFE5E7EB)),
                ),
            ),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            CallAvatar(initials = contact.initials, size = 108)
            Text(
                text = contact.name,
                color = CallTextPrimary,
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold,
            )
            if (!cameraOn) {
                Text(
                    text = "Camera đang tắt",
                    color = CallTextSecondary,
                    style = MaterialTheme.typography.bodyMedium,
                )
            }
        }
    }
}

@Composable
private fun SelfPreview(
    label: String,
    modifier: Modifier = Modifier,
) {
    Surface(
        modifier = modifier
            .width(104.dp)
            .height(142.dp),
        shape = RoundedCornerShape(20.dp),
        color = Color.White.copy(alpha = 0.88f),
        border = BorderStroke(1.dp, Color.White.copy(alpha = 0.92f)),
        shadowElevation = 8.dp,
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(10.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            CallAvatar(initials = "Tôi", size = 48)
            Spacer(Modifier.height(8.dp))
            Text(label, color = CallTextSecondary, style = MaterialTheme.typography.labelMedium)
        }
    }
}

@Composable
private fun VoiceCallControls(
    muted: Boolean,
    speakerOn: Boolean,
    onToggleMute: () -> Unit,
    onToggleSpeaker: () -> Unit,
    onAddParticipant: () -> Unit,
    onSwitchToVideo: () -> Unit,
    onEndCall: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .navigationBarsPadding()
            .padding(horizontal = 18.dp, vertical = 20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(24.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceAround,
        ) {
            CallActionButton(
                icon = if (muted) Icons.Filled.MicOff else Icons.Filled.Mic,
                label = "Tắt mic",
                active = muted,
                onClick = onToggleMute,
            )
            CallActionButton(
                icon = Icons.AutoMirrored.Filled.VolumeUp,
                label = "Loa",
                active = speakerOn,
                onClick = onToggleSpeaker,
            )
            CallActionButton(
                icon = Icons.Filled.Add,
                label = "Thêm",
                onClick = onAddParticipant,
            )
            CallActionButton(
                icon = Icons.Filled.Videocam,
                label = "Video",
                onClick = onSwitchToVideo,
            )
        }
        EndCallButton(onClick = onEndCall)
    }
}

@Composable
private fun VideoCallControls(
    muted: Boolean,
    cameraOn: Boolean,
    speakerOn: Boolean,
    onToggleMute: () -> Unit,
    onToggleCamera: () -> Unit,
    onSwitchCamera: () -> Unit,
    onToggleSpeaker: () -> Unit,
    onEndCall: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(
                Brush.verticalGradient(
                    colors = listOf(Color.Transparent, Color.White.copy(alpha = 0.96f)),
                ),
            )
            .navigationBarsPadding()
            .padding(horizontal = 14.dp, vertical = 18.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Surface(
            shape = RoundedCornerShape(30.dp),
            color = Color.White.copy(alpha = 0.94f),
            shadowElevation = 8.dp,
        ) {
            Row(
                modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                CompactCallAction(
                    icon = if (muted) Icons.Filled.MicOff else Icons.Filled.Mic,
                    label = "Tắt mic",
                    active = muted,
                    onClick = onToggleMute,
                )
                CompactCallAction(
                    icon = Icons.Filled.Videocam,
                    label = "Camera",
                    active = cameraOn,
                    onClick = onToggleCamera,
                )
                CompactCallAction(
                    icon = Icons.Filled.CameraAlt,
                    label = "Đổi cam",
                    onClick = onSwitchCamera,
                )
                CompactCallAction(
                    icon = Icons.AutoMirrored.Filled.VolumeUp,
                    label = "Loa",
                    active = speakerOn,
                    onClick = onToggleSpeaker,
                )
                Box(
                    modifier = Modifier
                        .size(54.dp)
                        .clip(CircleShape)
                        .background(CallEndRed)
                        .clickable(onClick = onEndCall),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(Icons.Filled.CallEnd, contentDescription = "Kết thúc", tint = Color.White, modifier = Modifier.size(26.dp))
                }
            }
        }
    }
}

@Composable
private fun CallActionButton(
    icon: ImageVector,
    label: String,
    active: Boolean = false,
    onClick: () -> Unit,
) {
    val container = if (active) CallAccent.copy(alpha = 0.12f) else Color.White
    val content = if (active) CallAccent else CallTextPrimary
    Column(
        modifier = Modifier.clickable(onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Box(
            modifier = Modifier
                .size(58.dp)
                .clip(CircleShape)
                .background(container),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, contentDescription = label, tint = content, modifier = Modifier.size(25.dp))
        }
        Text(label, color = CallTextSecondary, style = MaterialTheme.typography.labelMedium)
    }
}

@Composable
private fun CompactCallAction(
    icon: ImageVector,
    label: String,
    active: Boolean = false,
    onClick: () -> Unit,
) {
    val content = if (active) CallAccent else CallTextPrimary
    Column(
        modifier = Modifier
            .clip(RoundedCornerShape(22.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 4.dp, vertical = 2.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Box(
            modifier = Modifier
                .size(42.dp)
                .clip(CircleShape)
                .background(if (active) CallAccent.copy(alpha = 0.12f) else CallSurfaceAlt),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, contentDescription = label, tint = content, modifier = Modifier.size(21.dp))
        }
        Text(label, color = CallTextSecondary, style = MaterialTheme.typography.labelSmall, maxLines = 1)
    }
}

@Composable
private fun EndCallButton(onClick: () -> Unit) {
    Column(
        modifier = Modifier.clickable(onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Box(
            modifier = Modifier
                .size(68.dp)
                .clip(CircleShape)
                .background(CallEndRed),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Filled.CallEnd, contentDescription = "Kết thúc", tint = Color.White, modifier = Modifier.size(31.dp))
        }
        Text("Kết thúc", color = CallEndRed, style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.Bold)
    }
}

@Composable
private fun CompactConnectionIndicator(connection: CallConnectionUi) {
    val tone = connectionColor(connection.tone)
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        SignalBars(level = connection.level.coerceIn(0, 4), color = tone)
        Column(horizontalAlignment = Alignment.End) {
            Text(
                text = connection.label,
                color = tone,
                fontSize = 11.sp,
                fontWeight = FontWeight.Bold,
                maxLines = 1,
            )
            Text(
                text = connection.detail,
                color = CallTextMuted,
                fontSize = 10.sp,
                maxLines = 1,
            )
        }
    }
}

@Composable
private fun SignalBars(level: Int, color: Color) {
    val heights = listOf(5.dp, 8.dp, 11.dp, 14.dp)
    Row(
        modifier = Modifier.height(16.dp),
        verticalAlignment = Alignment.Bottom,
        horizontalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        heights.forEachIndexed { index, height ->
            Box(
                modifier = Modifier
                    .width(3.dp)
                    .height(height)
                    .clip(RoundedCornerShape(2.dp))
                    .background(if (index < level) color else CallTextMuted.copy(alpha = 0.28f)),
            )
        }
    }
}

@Composable
private fun AudioWaveform(modifier: Modifier = Modifier) {
    val heights = listOf(12.dp, 22.dp, 34.dp, 18.dp, 28.dp, 42.dp, 24.dp, 14.dp, 30.dp, 20.dp, 36.dp)
    Row(
        modifier = modifier
            .height(48.dp)
            .fillMaxWidth(),
        horizontalArrangement = Arrangement.Center,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        heights.forEach { height ->
            Box(
                modifier = Modifier
                    .padding(horizontal = 3.dp)
                    .width(5.dp)
                    .height(height)
                    .clip(RoundedCornerShape(999.dp))
                    .background(CallAccent.copy(alpha = 0.18f)),
            )
        }
    }
}

@Composable
private fun CallAvatar(initials: String, size: Int, modifier: Modifier = Modifier) {
    Box(
        modifier = modifier
            .size(size.dp)
            .clip(CircleShape)
            .background(Brush.linearGradient(listOf(Color(0xFFCBD5E1), Color(0xFFFEE2E2)))),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = initials,
            color = Color(0xFF7F1D1D),
            style = if (size >= 90) MaterialTheme.typography.headlineSmall else MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.ExtraBold,
            textAlign = TextAlign.Center,
            maxLines = 1,
        )
    }
}

@Composable
private fun connectionColor(tone: CallConnectionTone): Color = when (tone) {
    CallConnectionTone.Good -> Color(0xFF16A34A)
    CallConnectionTone.Warning -> Color(0xFFF59E0B)
    CallConnectionTone.Poor -> CallEndRed
}

fun sampleVoiceCallUiState(): VoiceCallUiState = VoiceCallUiState()

fun sampleVideoCallUiState(): VideoCallUiState = VideoCallUiState()

private val CallAccent = Color(0xFFC62828)
private val CallEndRed = Color(0xFFDC2626)
private val CallBackground = Color(0xFFF8FAFC)
private val CallVideoBackground = Color(0xFFF1F5F9)
private val CallSurfaceAlt = Color(0xFFF1F2F4)
private val CallTextPrimary = Color(0xFF111827)
private val CallTextSecondary = Color(0xFF4B5563)
private val CallTextMuted = Color(0xFF9CA3AF)
