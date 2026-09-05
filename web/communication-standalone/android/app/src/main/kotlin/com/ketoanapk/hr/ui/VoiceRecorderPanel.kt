package com.ketoanapk.hr.ui

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import kotlin.math.max
import kotlin.math.roundToInt

/* =============================================================================
   BẢNG GHI ÂM tin nhắn thoại — CHIẾM ĐÚNG CHỖ CỦA BÀN PHÍM, không phủ lên hội
   thoại: bấm nút mic ở thanh nhập là bàn phím thu lại, bảng này hiện lên thế chỗ,
   tin nhắn phía trên vẫn đọc được như khi đang gõ.

   Nút mic TO giữa bảng mới là chỗ ghi: bấm = ghi rảnh tay, giữ = thả ra gửi, giữ
   rồi vuốt trái = huỷ / vuốt phải = rảnh tay.

   Nút mic phải nằm GIỮA: đích vuốt hai bên chỉ tới được khi ngón tay còn đủ chỗ
   kéo về mỗi phía. Hồi nút ghi nằm sát mép trái (~90dp), vuốt-huỷ không tài nào
   đạt ngưỡng, thả tay ra hoá thành GỬI — đúng cái người dùng vừa cố huỷ.

   Ở đây chỉ có phần NHÌN: cử chỉ do RealChatScreen truyền vào qua [micGesture].
   ========================================================================== */

/** Bao nhiêu mẫu biên độ giữ lại — đủ phủ hết chiều ngang dải sóng rồi trượt dần. */
const val VOICE_WAVE_SAMPLES = 96

/** Nhịp lấy mẫu biên độ; 50ms ≈ 20 cột/giây, đủ mượt mà không phí khung hình. */
const val VOICE_WAVE_TICK_MS = 50L

/** Cao xấp xỉ bàn phím ảo để lúc đóng/mở bảng bố cục không giật nảy. */
private val PanelHeight = 340.dp
private val MicSize = 88.dp
private val RingSize = 168.dp

/** Thùng rác/ổ khoá đứng cách tâm bảng chừng này; nút mic kéo hết cỡ thì đậu đúng lên chúng. */
private val TargetOffset = 140.dp

/** Nút mic vào trong khoảng này quanh icon là coi như đã "đậu" lên icon đó. */
private val CaptureRadius = 44.dp

/**
 * Kéo xa chừng này là nút mic bị icon bắt — nhưng phải THẢ TAY ra mới ăn, kéo ngược lại là thoát.
 * Suy ra từ vị trí icon nên đổi [TargetOffset] là ngưỡng tự khớp theo, khỏi chỉnh tay hai chỗ.
 */
val VOICE_ENGAGE_DRAG = TargetOffset - CaptureRadius

@Composable
fun VoiceRecorderPanel(
    recording: Boolean,
    locked: Boolean,
    elapsedMs: Long,
    amplitudes: List<Float>,
    dragX: Float,
    engageAt: Float,
    onCancel: () -> Unit,
    onSend: () -> Unit,
    micGesture: Modifier,
    modifier: Modifier = Modifier,
) {
    val holding = recording && !locked
    // Sáng dần trên đường kéo tới: người dùng thấy mình đang tiến gần đích chứ không phải mò trong tối.
    val cancelProgress = if (holding) (-dragX / engageAt).coerceIn(0f, 1f) else 0f
    val lockProgress = if (holding) (dragX / engageAt).coerceIn(0f, 1f) else 0f
    // "Đậu" lên icon = nút mic đã nằm trên nó. Tới đây vẫn CHƯA ăn: thả tay mới ăn, kéo ra là thoát.
    val overCancel = cancelProgress >= 1f
    val overLock = lockProgress >= 1f
    Column(modifier.fillMaxWidth().background(MaterialTheme.colorScheme.background)) {
        HorizontalDivider(color = MaterialTheme.colorScheme.outline)
        Column(
            Modifier.fillMaxWidth().height(PanelHeight).padding(horizontal = 18.dp, vertical = 12.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            if (recording) WaveformPill(elapsedMs = elapsedMs, amplitudes = amplitudes)
            Spacer(Modifier.weight(1f))
            val (title, hint) = when {
                !recording -> "Bấm hoặc bấm giữ để ghi âm" to "Bấm để ghi rảnh tay · giữ rồi thả là gửi"
                locked -> "Đang ghi rảnh tay" to "Bấm nút gửi khi nói xong"
                overCancel -> "Thả ra để huỷ" to "Kéo ra chỗ khác nếu đổi ý"
                overLock -> "Thả ra để ghi rảnh tay" to "Nhả tay xong vẫn ghi tiếp"
                cancelProgress >= 0.4f -> "Kéo vào thùng rác để huỷ" to "Thả tay lúc này thì vẫn gửi"
                lockProgress >= 0.4f -> "Kéo vào ổ khoá để ghi rảnh tay" to "Thả tay lúc này thì vẫn gửi"
                else -> "Thả tay để gửi" to "Vuốt sang phải để bật chế độ rảnh tay"
            }
            Text(
                title,
                style = MaterialTheme.typography.titleLarge,
                color = if (overCancel) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurface,
                textAlign = TextAlign.Center,
            )
            Spacer(Modifier.height(4.dp))
            Text(
                hint,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
            Spacer(Modifier.weight(1f))
            RecordControls(
                recording = recording,
                amplitudes = amplitudes,
                dragX = dragX,
                locked = locked,
                cancelProgress = cancelProgress,
                lockProgress = lockProgress,
                overCancel = overCancel,
                overLock = overLock,
                onCancel = onCancel,
                onSend = onSend,
                micGesture = micGesture,
            )
        }
    }
}

/**
 * Dải sóng âm + đồng hồ. Cột sóng mọc từ trái, mẫu mới nhất nằm sát đồng hồ; đầy dải thì trượt đi như
 * băng ghi.
 */
@Composable
private fun WaveformPill(elapsedMs: Long, amplitudes: List<Float>) {
    val accent = MaterialTheme.colorScheme.primary
    Row(
        Modifier
            .fillMaxWidth()
            .clip(CircleShape)
            .background(accent.copy(alpha = 0.10f))
            .padding(horizontal = 18.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Canvas(Modifier.weight(1f).height(32.dp)) {
            val barWidth = 3.dp.toPx()
            val gap = 2.5f.dp.toPx()
            val step = barWidth + gap
            val slots = max(1, (size.width / step).toInt())
            val visible = if (amplitudes.size > slots) amplitudes.subList(amplitudes.size - slots, amplitudes.size) else amplitudes
            val midY = size.height / 2f
            visible.forEachIndexed { index, level ->
                // Cột thấp nhất vẫn phải bằng bề rộng nét, nếu không lúc im lặng dải sóng biến mất hẳn
                // và trông như đã hỏng — ảnh mẫu để lại hàng chấm tròn nhỏ.
                val barHeight = max(barWidth, level * size.height)
                val x = index * step + barWidth / 2f
                drawLine(
                    color = accent,
                    start = Offset(x, midY - barHeight / 2f),
                    end = Offset(x, midY + barHeight / 2f),
                    strokeWidth = barWidth,
                    cap = StrokeCap.Round,
                )
            }
        }
        Spacer(Modifier.width(12.dp))
        Text(
            formatRecordClock(elapsedMs),
            style = MaterialTheme.typography.titleLarge,
            color = MaterialTheme.colorScheme.onSurface,
        )
    }
}

/** Nút mic có quầng nhịp theo tiếng nói, hai bên là đích vuốt: huỷ (trái) và khoá rảnh tay (phải). */
@Composable
private fun RecordControls(
    recording: Boolean,
    amplitudes: List<Float>,
    dragX: Float,
    locked: Boolean,
    cancelProgress: Float,
    lockProgress: Float,
    overCancel: Boolean,
    overLock: Boolean,
    onCancel: () -> Unit,
    onSend: () -> Unit,
    micGesture: Modifier,
) {
    val accent = MaterialTheme.colorScheme.primary
    // Quầng bám theo biên độ nhưng phải làm mượt: nhảy thẳng theo mẫu 50ms sẽ giật như đèn nháy.
    val level by animateFloatAsState(
        targetValue = amplitudes.lastOrNull() ?: 0f,
        animationSpec = tween(durationMillis = 110),
        label = "mucAmThanh",
    )
    Box(Modifier.fillMaxWidth().height(RingSize + 8.dp), contentAlignment = Alignment.Center) {
        // Lúc chờ chưa ghi thì hai đích vuốt vô nghĩa — ảnh mẫu cũng chỉ có mỗi nút mic.
        if (recording) {
            DragTarget(
                icon = Icons.Filled.Delete,
                label = "Huỷ bản ghi",
                progress = cancelProgress,
                activeColor = MaterialTheme.colorScheme.error,
                enabled = locked,
                onClick = onCancel,
                modifier = Modifier.offset(x = -TargetOffset),
            )
            DragTarget(
                icon = Icons.Filled.Lock,
                label = "Ghi rảnh tay",
                progress = lockProgress,
                activeColor = accent,
                // Đã khoá rồi thì ổ khoá chỉ còn là biểu tượng trạng thái, bấm không làm gì thêm.
                enabled = false,
                onClick = {},
                modifier = Modifier.offset(x = TargetOffset),
            )
        }
        Box(
            // Chặn đúng ở vị trí hai icon: kéo hết cỡ là nút mic đậu chồng lên icon, không trôi quá.
            Modifier.offset {
                val limit = TargetOffset.toPx()
                IntOffset(dragX.coerceIn(-limit, limit).roundToInt(), 0)
            },
            contentAlignment = Alignment.Center,
        ) {
            Canvas(Modifier.size(RingSize)) {
                val maxRadius = size.minDimension / 2f
                val micRadius = MicSize.toPx() / 2f
                val outer = micRadius + (maxRadius - micRadius) * (0.35f + 0.65f * level)
                val mid = micRadius + (maxRadius - micRadius) * (0.18f + 0.34f * level)
                drawCircle(accent.copy(alpha = 0.10f), radius = outer)
                drawCircle(accent.copy(alpha = 0.18f), radius = mid)
            }
            Box(
                Modifier
                    .size(MicSize)
                    .clip(CircleShape)
                    // Đỏ dần về phía "sắp huỷ" để lời cảnh báo nằm ngay dưới ngón tay.
                    .background(lerp(accent, MaterialTheme.colorScheme.error, cancelProgress))
                    // Khoá rồi thì nút đổi vai thành GỬI, nên cử chỉ giữ-vuốt nhường chỗ cho cú bấm.
                    .then(if (locked) Modifier.clickable(onClick = onSend) else micGesture),
                contentAlignment = Alignment.Center,
            ) {
                // Đậu lên icon nào thì MANG LUÔN hình icon đó: nút mic che mất icon nằm dưới, nên nó phải
                // tự nói ra mình đang là nút gì — thả tay ra là ăn cái đang thấy.
                Icon(
                    when {
                        locked -> Icons.AutoMirrored.Filled.Send
                        overCancel -> Icons.Filled.Delete
                        overLock -> Icons.Filled.Lock
                        else -> Icons.Filled.Mic
                    },
                    contentDescription = when {
                        locked -> "Gửi tin nhắn thoại"
                        overCancel -> "Thả ra để huỷ bản ghi"
                        overLock -> "Thả ra để ghi rảnh tay"
                        recording -> "Đang ghi âm"
                        else -> "Bấm hoặc bấm giữ để ghi âm"
                    },
                    tint = MaterialTheme.colorScheme.onPrimary,
                    modifier = Modifier.size(38.dp),
                )
            }
        }
    }
}

/**
 * Đích vuốt: to dần + ăn màu dần theo [progress] (0 = chưa kéo, 1 = nút mic đã đậu lên).
 *
 * Quầng nền phải RỘNG HƠN nút mic để lúc đậu nó còn ló ra thành viền quanh nút — nút mic 88dp che kín
 * icon 30dp nằm dưới, không có quầng thì người dùng mất dấu đích mình vừa nhắm.
 */
@Composable
private fun DragTarget(
    icon: ImageVector,
    label: String,
    progress: Float,
    activeColor: Color,
    enabled: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Box(
        modifier = modifier
            .size(112.dp)
            .then(if (enabled) Modifier.clip(CircleShape).clickable(onClick = onClick) else Modifier),
        contentAlignment = Alignment.Center,
    ) {
        Canvas(Modifier.size(112.dp)) {
            if (progress <= 0f) return@Canvas
            drawCircle(
                color = activeColor.copy(alpha = 0.10f + 0.12f * progress),
                radius = size.minDimension / 2f * (0.55f + 0.45f * progress),
            )
        }
        Icon(
            icon,
            contentDescription = label,
            tint = lerp(MaterialTheme.colorScheme.onSurfaceVariant, activeColor, progress),
            modifier = Modifier.size((30 + 10 * progress).dp),
        )
    }
}

/** Đồng hồ ghi âm dạng mm:ss như ảnh mẫu (00:04). */
private fun formatRecordClock(ms: Long): String {
    val total = ms / 1000
    return "%02d:%02d".format(total / 60, total % 60)
}
