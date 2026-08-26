package com.ketoanapk.hr.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.GenericShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathMeasure
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.ui.theme.Success
import kotlinx.coroutines.delay
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin

/** Trạng thái của cụm ô nhập mã: đang gõ / đang hỏi máy chủ / đúng mã / sai mã. */
enum class CodeCellsPhase { Idle, Verifying, Success, Error }

/** Hoạt cảnh "đúng mã" (thu về giữa + vẽ dấu ✓) dài bấy nhiêu mili giây. */
const val CODE_CELLS_SUCCESS_MILLIS = 1150L

/** Hoạt cảnh "sai mã": rung 380ms + đứng yên thấy vết nứt 110ms + mảnh rơi 760ms. */
const val CODE_CELLS_ERROR_MILLIS = 1420L

/**
 * 5 mảnh vỡ KHÔNG đều nhau, cắt từ một điểm nứt lệch tâm (44%, 52%) và có nếp gãy trên đường nứt —
 * ghép lại vẫn kín đúng ô vuông ban đầu, nhưng nhìn ra vết nứt kính chứ không phải "chia làm 4".
 */
private val SHARD_POLYGONS: List<List<Pair<Float, Float>>> = listOf(
    listOf(0.44f to 0.52f, 0.26f to 0.22f, 0f to 0f, 0f to 0.30f, 0.20f to 0.46f),
    listOf(0.44f to 0.52f, 0.26f to 0.22f, 0f to 0f, 0.38f to 0f, 0.36f to 0.27f),
    listOf(0.44f to 0.52f, 0.36f to 0.27f, 0.38f to 0f, 1f to 0f, 1f to 0.42f, 0.74f to 0.42f),
    listOf(0.44f to 0.52f, 0.74f to 0.42f, 1f to 0.42f, 1f to 1f, 0.56f to 1f, 0.54f to 0.74f),
    listOf(0.44f to 0.52f, 0.54f to 0.74f, 0.56f to 1f, 0f to 1f, 0f to 0.30f, 0.20f to 0.46f),
)

private fun shardShape(index: Int): Shape = GenericShape { size, _ ->
    val points = SHARD_POLYGONS[index]
    moveTo(points[0].first * size.width, points[0].second * size.height)
    for (k in 1 until points.size) lineTo(points[k].first * size.width, points[k].second * size.height)
    close()
}

/** Mỗi mảnh: dạt ngang, nảy lên lúc vỡ, rơi xuống (nhanh dần), xoay khi rơi — đơn vị dp/độ. */
private data class ShardFlight(val drift: Float, val lift: Float, val fall: Float, val rotate: Float)

private val SHARD_FLIGHT = listOf(
    ShardFlight(drift = -14f, lift = -18f, fall = 108f, rotate = -52f),
    ShardFlight(drift = -4f, lift = -20f, fall = 116f, rotate = -26f),
    ShardFlight(drift = 13f, lift = -9f, fall = 94f, rotate = 32f),
    ShardFlight(drift = 18f, lift = -7f, fall = 100f, rotate = 44f),
    ShardFlight(drift = -9f, lift = -12f, fall = 104f, rotate = -22f),
)

/**
 * Hàng ô nhập mã có hoạt cảnh, dùng chung cho **mã OTP quên mật khẩu** và **mã bảo mật (PIN) của
 * ứng dụng**. Thành phần này CHỈ VẼ: nội dung từng ô do nơi gọi cấp ([cellText]) và trạng thái do nơi
 * gọi điều khiển ([phase]) — nên nó chạy được cả với bàn phím hệ thống (OTP, có ô nhập ẩn bên ngoài)
 * lẫn bàn phím số tự vẽ (PIN, không có ô nhập nào).
 *
 * Hoạt cảnh:
 *  • Đang gõ — ô đang tới lượt nhô lên, viền đậm; ô đã có nội dung viền màu chính.
 *  • Đang kiểm tra — [cluster] = true thì các ô rời hàng, xếp quanh tâm và quay chậm (nội dung xoay
 *    ngược lại nên luôn đứng thẳng) kèm vòng tiến trình ở giữa; [cluster] = false thì giữ nguyên hàng
 *    và chạy một đợt sóng nhấp nhô — dùng cho chỗ chật (bảng PIN có bàn phím số ngay dưới) vì cụm
 *    tròn cần thêm ~100dp chiều cao.
 *  • Đúng mã — các ô thu về giữa rồi tan, vòng tròn xanh nở ra và nét ✓ được VẼ dần.
 *  • Sai mã — rung, nứt, rồi mỗi ô vỡ thành 5 mảnh văng ra và rơi.
 *
 * Nơi gọi giữ [phase] đủ lâu theo [CODE_CELLS_SUCCESS_MILLIS] / [CODE_CELLS_ERROR_MILLIS] rồi mới
 * chuyển bước, nếu không hoạt cảnh sẽ bị cắt ngang khi máy chủ trong LAN trả lời tức thì.
 */
@Composable
fun AnimatedCodeCells(
    count: Int,
    filled: Int,
    phase: CodeCellsPhase,
    cellText: (Int) -> String,
    modifier: Modifier = Modifier,
    boxSize: Dp = 54.dp,
    gap: Dp = 10.dp,
    cluster: Boolean = true,
    onCellClick: (() -> Unit)? = null,
) {
    val radius = boxSize * 1.28f
    val clustered = cluster && (phase == CodeCellsPhase.Verifying || phase == CodeCellsPhase.Success)
    val shake = remember { Animatable(0f) }
    // 0 = bình thường, 1 = đang rung, 2 = đang vỡ.
    var errorStage by remember { mutableIntStateOf(0) }
    val shatter = remember { Animatable(0f) }
    val sealDraw = remember { Animatable(0f) }
    // Chiều cao chỗ đứng: đủ chứa vòng ✓ (68dp) khi không xoè, và chứa cả cụm tròn khi có xoè.
    val stageHeight by animateDpAsState(
        when {
            clustered -> radius * 2 + boxSize + 10.dp
            boxSize + 20.dp < 74.dp -> 74.dp
            else -> boxSize + 20.dp
        },
        spring(dampingRatio = Spring.DampingRatioNoBouncy, stiffness = Spring.StiffnessMediumLow),
        label = "codeCellsHeight",
    )
    val motion = rememberInfiniteTransition(label = "codeCellsMotion")
    val spin by motion.animateFloat(
        initialValue = 0f,
        targetValue = 360f,
        animationSpec = infiniteRepeatable(tween(7500, easing = LinearEasing), RepeatMode.Restart),
        label = "codeCellsSpin",
    )
    val wave by motion.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(1100, easing = LinearEasing), RepeatMode.Restart),
        label = "codeCellsWave",
    )
    val orbitAngle = if (clustered && phase == CodeCellsPhase.Verifying) spin else 0f

    LaunchedEffect(phase) {
        if (phase != CodeCellsPhase.Error) {
            errorStage = 0
            shake.snapTo(0f)
            shatter.snapTo(0f)
            return@LaunchedEffect
        }
        errorStage = 1
        shake.snapTo(0f)
        shake.animateTo(
            targetValue = 0f,
            animationSpec = keyframes {
                durationMillis = 380
                0f at 0
                -26f at 60
                22f at 120
                -16f at 190
                10f at 260
                -5f at 320
                0f at 380
            },
        )
        errorStage = 2
        shatter.snapTo(0f)
        // Nứt xong đứng yên một nhịp rồi mới rơi — mắt kịp thấy vết nứt trước khi mảnh rời ra.
        delay(110)
        shatter.animateTo(1f, tween(760, easing = LinearEasing))
    }

    LaunchedEffect(phase) {
        if (phase != CodeCellsPhase.Success) {
            sealDraw.snapTo(0f)
            return@LaunchedEffect
        }
        delay(260)
        sealDraw.animateTo(1f, tween(420))
    }

    Box(
        modifier = modifier
            .fillMaxWidth()
            .height(stageHeight)
            .graphicsLayer { translationX = shake.value },
        contentAlignment = Alignment.Center,
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(stageHeight)
                .graphicsLayer { rotationZ = orbitAngle },
            contentAlignment = Alignment.Center,
        ) {
            val cursor = filled.coerceAtMost(count - 1)
            for (i in 0 until count) {
                val angle = Math.toRadians(-90.0 + i * 360.0 / count)
                val rowX = (boxSize + gap) * (i - (count - 1) / 2f)
                val targetX: Dp = when {
                    phase == CodeCellsPhase.Success -> 0.dp
                    clustered -> radius * cos(angle).toFloat()
                    else -> rowX
                }
                val targetY: Dp = when {
                    phase == CodeCellsPhase.Success -> 0.dp
                    clustered -> radius * sin(angle).toFloat()
                    else -> 0.dp
                }
                val offsetX by animateDpAsState(
                    targetX,
                    spring(dampingRatio = 0.68f, stiffness = Spring.StiffnessLow),
                    label = "codeCellX$i",
                )
                val offsetY by animateDpAsState(
                    targetY,
                    spring(dampingRatio = 0.68f, stiffness = Spring.StiffnessLow),
                    label = "codeCellY$i",
                )
                val cellScale by animateFloatAsState(
                    if (phase == CodeCellsPhase.Success) 0.34f else if (errorStage == 2) 0.9f else 1f,
                    spring(dampingRatio = 0.72f, stiffness = Spring.StiffnessLow),
                    label = "codeCellScale$i",
                )
                val cellAlpha by animateFloatAsState(
                    if (phase == CodeCellsPhase.Success || errorStage == 2) 0f else 1f,
                    tween(
                        durationMillis = if (errorStage == 2) 60 else 380,
                        delayMillis = if (phase == CodeCellsPhase.Idle) i * 45 else 0,
                    ),
                    label = "codeCellAlpha$i",
                )
                // Đợt sóng chạy dọc hàng ô khi đang hỏi máy chủ mà KHÔNG xoè thành cụm tròn: mỗi ô
                // nhô lên lệch pha nhau nên nhìn ra "đang chạy" trong đúng chiều cao của hàng.
                val waveLift = if (!cluster && phase == CodeCellsPhase.Verifying) {
                    val t = (((wave - i * 0.11f) % 1f) + 1f) % 1f
                    if (t < 0.5f) sin(t * 2f * PI.toFloat()) else 0f
                } else 0f

                val text = cellText(i)
                val focused = phase == CodeCellsPhase.Idle && i == cursor && filled < count
                val borderColor = when {
                    phase == CodeCellsPhase.Error -> MaterialTheme.colorScheme.error
                    phase == CodeCellsPhase.Success -> Success
                    focused -> MaterialTheme.colorScheme.primary
                    text.isNotEmpty() -> MaterialTheme.colorScheme.primary.copy(alpha = 0.5f)
                    else -> MaterialTheme.colorScheme.outlineVariant
                }
                val borderWidth by animateDpAsState(if (focused) 2.dp else 1.5.dp, label = "codeCellBorder$i")
                val focusLift by animateFloatAsState(if (focused) 1.05f else 1f, label = "codeCellLift$i")

                Box(
                    modifier = Modifier
                        .offset(x = offsetX, y = offsetY - (waveLift * 7f).dp)
                        .size(boxSize)
                        .rotate(-orbitAngle)
                        .scale(cellScale * focusLift * (1f + 0.06f * waveLift))
                        .alpha(cellAlpha)
                        .clip(RoundedCornerShape(12.dp))
                        .background(
                            if (phase == CodeCellsPhase.Error) MaterialTheme.colorScheme.error.copy(alpha = 0.07f)
                            else MaterialTheme.colorScheme.surface,
                        )
                        .border(borderWidth, borderColor, RoundedCornerShape(12.dp))
                        .then(
                            if (onCellClick != null) {
                                Modifier.clickable(enabled = phase == CodeCellsPhase.Idle) { onCellClick() }
                            } else Modifier,
                        ),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text,
                        style = MaterialTheme.typography.headlineSmall,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface,
                    )
                }
            }
        }

        // Mảnh vỡ: mỗi ô tách thành 5 mảnh văng ra rồi tan.
        if (errorStage == 2) {
            val progress = shatter.value
            Box(modifier = Modifier.fillMaxWidth().height(stageHeight), contentAlignment = Alignment.Center) {
                for (i in 0 until count) {
                    val rowX = (boxSize + gap) * (i - (count - 1) / 2f)
                    val text = cellText(i)
                    SHARD_FLIGHT.forEachIndexed { part, flight ->
                        // Nảy lên 20% quãng đầu, sau đó rơi theo p² nên nhanh dần như có trọng lực.
                        val fallY = if (progress < 0.2f) {
                            flight.lift * (progress / 0.2f)
                        } else {
                            val q = (progress - 0.2f) / 0.8f
                            flight.lift * (1f - q) + flight.fall * q * q
                        }
                        val fade = if (progress < 0.35f) 1f else ((1f - progress) / 0.65f).coerceIn(0f, 1f)
                        Box(
                            modifier = Modifier
                                .offset(x = rowX + (flight.drift * progress).dp, y = fallY.dp)
                                .size(boxSize)
                                .rotate(flight.rotate * progress)
                                .scale(1f - 0.14f * progress)
                                .alpha(fade)
                                .clip(shardShape(part))
                                .background(MaterialTheme.colorScheme.error.copy(alpha = 0.08f))
                                .border(1.dp, MaterialTheme.colorScheme.error.copy(alpha = 0.85f), shardShape(part)),
                            contentAlignment = Alignment.Center,
                        ) {
                            Text(
                                text,
                                style = MaterialTheme.typography.headlineSmall,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.onSurface,
                            )
                        }
                    }
                }
            }
        }

        if (clustered && phase == CodeCellsPhase.Verifying) {
            CircularProgressIndicator(
                modifier = Modifier.size(30.dp),
                color = MaterialTheme.colorScheme.primary,
                trackColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.16f),
                strokeWidth = 2.5.dp,
            )
        }

        AnimatedVisibility(
            visible = phase == CodeCellsPhase.Success,
            enter = scaleIn(spring(dampingRatio = 0.55f)) + fadeIn(),
            exit = scaleOut() + fadeOut(),
        ) {
            Box(
                modifier = Modifier
                    .size(68.dp)
                    .clip(CircleShape)
                    .background(Success),
                contentAlignment = Alignment.Center,
            ) {
                // Nét ✓ được VẼ dần chứ không hiện ra ngay — cảm giác "đã chốt".
                Canvas(modifier = Modifier.size(34.dp)) {
                    val path = Path().apply {
                        moveTo(size.width * 0.08f, size.height * 0.52f)
                        lineTo(size.width * 0.40f, size.height * 0.82f)
                        lineTo(size.width * 0.94f, size.height * 0.18f)
                    }
                    val measure = PathMeasure().apply { setPath(path, false) }
                    val drawn = Path()
                    measure.getSegment(0f, measure.length * sealDraw.value, drawn, true)
                    drawPath(
                        path = drawn,
                        color = Color.White,
                        style = Stroke(width = 5.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round),
                    )
                }
            }
        }
    }
}
