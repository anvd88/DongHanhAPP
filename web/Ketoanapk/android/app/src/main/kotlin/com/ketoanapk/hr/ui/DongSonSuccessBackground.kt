package com.ketoanapk.hr.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathMeasure
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.withTransform
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import com.ketoanapk.hr.R
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.max
import kotlin.math.sin

private const val DongSonDesignWidth = 2160f
private const val DongSonDesignHeight = 3840f
private const val TickStrokeStart = 0.32f

private val DongSonIvory = Color(0xFFFCF7EE)
private val DongSonBronze = Color(0xFFAA671E)

/**
 * Nền kết quả chấm công dùng nguyên artwork Ngọc Lũ 9:16 đã duyệt.
 *
 * Ảnh nền là bản runtime được xuất trực tiếp từ vector chuẩn, không trace, simplify hay dựng lại
 * họa tiết bằng Canvas. Canvas chỉ vẽ lớp highlight gồm đúng 26 vành cấu trúc và tâm 14 tia;
 * highlight chạy ngược chiều kim đồng hồ rồi biến mất để khung cuối còn nguyên artwork gốc.
 */
@Composable
internal fun DongSonSuccessBackground(
    revealProgress: Float,
    settleProgress: Float,
    modifier: Modifier = Modifier,
) {
    val reveal = normalizedProgress(
        value = revealProgress.takeIf(Float::isFinite) ?: 0f,
        start = TickStrokeStart,
        end = 1f,
    )
    val settle = settleProgress
        .takeIf(Float::isFinite)
        ?.coerceIn(0f, 1f)
        ?: 0f

    // 62% hiệu ứng chạy khi tick được vẽ, 38% còn lại theo nhịp tick đi lên góc trái.
    val animationProgress = (reveal * 0.62f + settle * 0.38f).coerceIn(0f, 1f)
    val artworkAlpha = smoothStep(0f, 0.24f, reveal)
    val tracks = remember { createRevealTracks() }

    Box(modifier = modifier.background(DongSonIvory.copy(alpha = artworkAlpha))) {
        Image(
            painter = painterResource(R.drawable.dong_son_wallpaper),
            contentDescription = null,
            contentScale = ContentScale.Crop,
            alpha = artworkAlpha,
            modifier = Modifier.fillMaxSize(),
        )

        Canvas(Modifier.fillMaxSize()) {
            val traceAlpha = artworkAlpha * (1f - smoothStep(0.60f, 1f, animationProgress))
            if (traceAlpha <= 0f) return@Canvas

            // ContentScale.Crop giống hệt ảnh nền: scale đều, crop cân giữa và không kéo méo mặt trống.
            val layoutScale = max(size.width / DongSonDesignWidth, size.height / DongSonDesignHeight)
            val offsetX = (size.width - DongSonDesignWidth * layoutScale) / 2f
            val offsetY = (size.height - DongSonDesignHeight * layoutScale) / 2f

            withTransform({
                translate(left = offsetX, top = offsetY)
                scale(scaleX = layoutScale, scaleY = layoutScale, pivot = Offset.Zero)
            }) {
                tracks.forEach { track ->
                    val localProgress = normalizedProgress(
                        animationProgress,
                        track.start,
                        track.end,
                    )
                    if (localProgress <= 0f) return@forEach

                    val pathToDraw = if (localProgress >= 1f) {
                        track.sourcePath
                    } else {
                        track.segment.reset()
                        val hasSegment = track.measure.getSegment(
                            startDistance = 0f,
                            stopDistance = track.length * localProgress,
                            destination = track.segment,
                            startWithMoveTo = true,
                        )
                        if (hasSegment) track.segment else null
                    }

                    if (pathToDraw != null) {
                        drawPath(
                            path = pathToDraw,
                            color = DongSonBronze.copy(alpha = track.opacity * traceAlpha),
                            style = track.style,
                        )
                    }
                }
            }
        }
    }
}

private class DongSonRevealTrack(
    val sourcePath: Path,
    val start: Float,
    val end: Float,
    val opacity: Float,
    strokeWidth: Float,
) {
    val measure = PathMeasure().apply { setPath(sourcePath, forceClosed = false) }
    val length = measure.length
    val segment = Path()
    val style = Stroke(
        width = strokeWidth,
        cap = StrokeCap.Round,
        join = StrokeJoin.Round,
    )
}

private fun createRevealTracks(): List<DongSonRevealTrack> {
    val tracks = ArrayList<DongSonRevealTrack>(27)

    // Hai mặt trống ở góc dùng nét nhẹ để phần thông tin chấm công vẫn là lớp thị giác chính.
    listOf(1640f, 1700f, 1760f, 1820f, 1880f).forEachIndexed { index, radius ->
        tracks += DongSonRevealTrack(
            sourcePath = counterClockwiseCircle(-300f, -1350f, radius),
            start = index * 0.025f,
            end = 0.29f + index * 0.018f,
            opacity = 0.12f,
            strokeWidth = 3f,
        )
    }

    listOf(1860f, 1940f, 2020f, 2100f, 2180f).forEachIndexed { index, radius ->
        tracks += DongSonRevealTrack(
            sourcePath = counterClockwiseCircle(-500f, 5325f, radius),
            start = 0.07f + index * 0.025f,
            end = 0.37f + index * 0.018f,
            opacity = 0.11f,
            strokeWidth = 3f,
        )
    }

    val mainRadii = listOf(
        460f, 520f, 580f, 650f, 730f, 810f, 890f, 970f,
        1050f, 1130f, 1210f, 1300f, 1390f, 1470f, 1540f, 1600f,
    )
    mainRadii.forEachIndexed { index, radius ->
        tracks += DongSonRevealTrack(
            sourcePath = counterClockwiseCircle(2350f, 2910f, radius),
            start = 0.08f + index * 0.0125f,
            end = 0.43f + index * 0.011f,
            opacity = 0.255f,
            strokeWidth = if (index % 4 == 0) 3.6f else 2.5f,
        )
    }

    tracks += DongSonRevealTrack(
        sourcePath = counterClockwiseStar(
            centerX = 2350f,
            centerY = 2910f,
            points = 14,
            outerRadius = 460f,
            innerRadius = 184f,
        ),
        start = 0.25f,
        end = 0.60f,
        opacity = 0.32f,
        strokeWidth = 3.6f,
    )

    return tracks
}

/** Vòng cubic bắt đầu ở hướng 3 giờ và chạy ngược chiều kim đồng hồ. */
private fun counterClockwiseCircle(cx: Float, cy: Float, radius: Float): Path {
    val control = radius * 0.55228475f
    return Path().apply {
        moveTo(cx + radius, cy)
        cubicTo(cx + radius, cy - control, cx + control, cy - radius, cx, cy - radius)
        cubicTo(cx - control, cy - radius, cx - radius, cy - control, cx - radius, cy)
        cubicTo(cx - radius, cy + control, cx - control, cy + radius, cx, cy + radius)
        cubicTo(cx + control, cy + radius, cx + radius, cy + control, cx + radius, cy)
        close()
    }
}

private fun counterClockwiseStar(
    centerX: Float,
    centerY: Float,
    points: Int,
    outerRadius: Float,
    innerRadius: Float,
): Path = Path().apply {
    repeat(points * 2) { index ->
        val angle = -PI / 2.0 - PI * index / points
        val radius = if (index % 2 == 0) outerRadius else innerRadius
        val x = centerX + cos(angle).toFloat() * radius
        val y = centerY + sin(angle).toFloat() * radius
        if (index == 0) moveTo(x, y) else lineTo(x, y)
    }
    close()
}

private fun normalizedProgress(value: Float, start: Float, end: Float): Float =
    ((value - start) / (end - start)).coerceIn(0f, 1f)

private fun smoothStep(edge0: Float, edge1: Float, value: Float): Float {
    val t = normalizedProgress(value, edge0, edge1)
    return t * t * (3f - 2f * t)
}
