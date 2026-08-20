package com.ketoanapk.hr.ui

import androidx.compose.foundation.Canvas
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.withTransform
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin

private val DongSonIvory = Color(0xFFFBF7EE)
private val DongSonIvoryLight = Color(0xFFFFFCF6)
private val DongSonJade = Color(0xFFDCE6D2)
private val DongSonBronze = Color(0xFFB98A50)

/**
 * Nền vector cho kết quả chấm công thành công. Bố cục bám mẫu đã chọn nhưng được dựng hoàn toàn
 * bằng Compose Canvas nên không vỡ nét và tự co giãn theo mọi màn hình.
 */
@Composable
internal fun DongSonSuccessBackground(
    revealProgress: Float,
    settleProgress: Float,
    modifier: Modifier = Modifier,
) {
    Canvas(modifier = modifier) {
        val w = size.width
        val h = size.height
        if (w <= 0f || h <= 0f) return@Canvas

        // Tick bắt đầu được vẽ tại khoảng 32% badgeDraw. Từ đúng mốc này nền mới xuất hiện.
        val reveal = ((revealProgress - 0.30f) / 0.70f).coerceIn(0f, 1f)
        if (reveal <= 0f) return@Canvas
        val revealEase = 1f - (1f - reveal) * (1f - reveal) * (1f - reveal)
        val settle = settleProgress.coerceIn(0f, 1f)
        val settleEase = 1f - (1f - settle) * (1f - settle)
        // 62% nét được dựng trong lúc vòng loading hóa thành tick, 38% còn lại nối tiếp trong lúc
        // badge đi lên góc. Nhờ vậy hình không chỉ tăng opacity mà thực sự được vẽ theo từng vành.
        val strokeProgress = (reveal * 0.62f + settleEase * 0.38f).coerceIn(0f, 1f)

        drawRect(
            brush = Brush.verticalGradient(
                colors = listOf(DongSonIvoryLight, DongSonIvory, Color(0xFFF7F1E4)),
                startY = 0f,
                endY = h,
            ),
            alpha = revealEase,
        )
        drawCircle(
            brush = Brush.radialGradient(
                colors = listOf(DongSonJade.copy(alpha = 0.16f), Color.Transparent),
                center = Offset(w * 0.92f, h * 0.72f),
                radius = w * 1.18f,
            ),
            radius = w * 1.18f,
            center = Offset(w * 0.92f, h * 0.72f),
            alpha = revealEase,
        )
        drawVectorPaperWash(w, h, revealEase)

        // Nét vẫn chìm nhưng đủ dày để không mất do anti-alias trên màn hình mật độ cao.
        val crisp = DongSonBronze.copy(alpha = 0.43f * revealEase)
        val soft = DongSonBronze.copy(alpha = 0.24f * revealEase)
        val stroke = (w * 0.0017f).coerceIn(1.1f, 3f)

        // Vành trên trượt xuống rất nhẹ khi tick đang được vẽ.
        drawPeripheralBands(
            center = Offset(w * 0.50f, -w * (0.94f - 0.06f * revealEase)),
            radius = w * 1.06f,
            color = crisp,
            strokeWidth = stroke,
            ornaments = true,
            progress = stagedProgress(strokeProgress, 0f, 0.72f),
        )

        // Hai cụm vành chìm ở mép trái/dưới, nằm sau mặt trống chính.
        drawPeripheralBands(
            center = Offset(-w * 0.58f, h * 0.66f),
            radius = w * 1.02f,
            color = soft,
            strokeWidth = stroke,
            ornaments = true,
            progress = stagedProgress(strokeProgress, 0.08f, 0.86f),
        )
        drawPeripheralBands(
            center = Offset(-w * 0.34f, h * 1.02f),
            radius = w * 0.88f,
            color = soft,
            strokeWidth = stroke,
            ornaments = false,
            progress = stagedProgress(strokeProgress, 0.20f, 0.96f),
        )

        // Mặt trống xoay/phóng trong lúc vẽ tick rồi tiếp tục dịch về đích cùng chuyển động tick lên
        // góc trái. Khi settle=1 mọi transform trở về trạng thái tĩnh.
        val drumCenter = Offset(
            x = w * (1.04f - 0.07f * settleEase),
            y = h * (0.75f - 0.05f * settleEase),
        )
        val motionProgress = (reveal * 0.66f + settleEase * 0.34f).coerceIn(0f, 1f)
        val drumScale = 0.82f + 0.18f * motionProgress
        // Xoay thêm một nhịp rõ ràng khi xuất hiện nhưng dừng đúng 0 độ, không vượt quá đích rồi
        // quay ngược lại — chuyển động ngược trước đây tạo cảm giác mặt trống bị nảy sau khi vẽ xong.
        val drumRotation = -42f * (1f - motionProgress)
        withTransform({
            rotate(drumRotation, pivot = drumCenter)
            scale(drumScale, drumScale, pivot = drumCenter)
        }) {
            drawDongSonDrumFace(
                center = drumCenter,
                radius = w * 0.77f,
                color = crisp,
                strokeWidth = stroke,
                background = DongSonIvory.copy(alpha = 0.82f * revealEase),
                progress = strokeProgress,
            )
            drawSuccessSweep(
                center = drumCenter,
                radius = w * 0.77f,
                reveal = revealEase,
                settle = settleEase,
                strokeWidth = stroke,
            )
        }
    }
}

private fun DrawScope.drawVectorPaperWash(w: Float, h: Float, alphaScale: Float) {
    // Các mảng loang rất nhẹ, vẫn là vector và cố định giữa các lần vẽ.
    repeat(28) { index ->
        val x = ((index * 37 + 11) % 101) / 100f * w
        val y = ((index * 61 + 17) % 103) / 102f * h
        val radius = w * (0.055f + (index % 5) * 0.012f)
        drawCircle(
            color = if (index % 3 == 0) DongSonJade else DongSonBronze,
            radius = radius,
            center = Offset(x, y),
            alpha = (if (index % 3 == 0) 0.012f else 0.006f) * alphaScale,
        )
    }
}

/** Các vòng sáng vẽ/lia theo đúng tiến độ tổng của tick và biến mất khi cả hai cùng đứng yên. */
private fun DrawScope.drawSuccessSweep(
    center: Offset,
    radius: Float,
    reveal: Float,
    settle: Float,
    strokeWidth: Float,
) {
    val movement = (reveal * 0.68f + settle * 0.32f).coerceIn(0f, 1f)
    val active = sin(PI * movement).toFloat().coerceAtLeast(0f)
    val ringAlpha = (1f - settle) * reveal
    val topLeft = Offset(center.x - radius, center.y - radius)
    val diameter = Size(radius * 2f, radius * 2f)

    listOf(0.985f, 0.925f, 0.855f).forEachIndexed { index, factor ->
        val local = ((reveal - index * 0.08f) / (1f - index * 0.08f)).coerceIn(0f, 1f)
        val r = radius * factor
        drawArc(
            color = Color(0xFFD4B277),
            startAngle = -96f,
            sweepAngle = 360f * local,
            useCenter = false,
            topLeft = Offset(center.x - r, center.y - r),
            size = Size(r * 2f, r * 2f),
            alpha = ringAlpha * (0.30f - index * 0.065f),
            style = Stroke(width = strokeWidth * (1.45f - index * 0.16f), cap = StrokeCap.Round),
        )
    }

    // Một dải sáng ngắn chạy quanh vành, cùng kết thúc khi tick chạm góc trái.
    if (active > 0f) {
        drawArc(
            color = Color.White,
            startAngle = -110f + 420f * movement,
            sweepAngle = 42f,
            useCenter = false,
            topLeft = topLeft,
            size = diameter,
            alpha = active * 0.42f,
            style = Stroke(width = strokeWidth * 2.2f, cap = StrokeCap.Round),
        )
    }
}

private fun DrawScope.drawPeripheralBands(
    center: Offset,
    radius: Float,
    color: Color,
    strokeWidth: Float,
    ornaments: Boolean,
    progress: Float,
) {
    val ringFactors = listOf(1f, 0.982f, 0.955f, 0.925f, 0.895f)
    ringFactors.forEachIndexed { index, factor ->
        val local = stagedProgress(progress, index * 0.045f, 0.46f + index * 0.045f)
        if (local <= 0f) return@forEachIndexed
        drawArc(
            color = color,
            startAngle = -102f,
            sweepAngle = 360f * local,
            useCenter = false,
            topLeft = Offset(center.x - radius * factor, center.y - radius * factor),
            size = Size(radius * factor * 2f, radius * factor * 2f),
            style = Stroke(width = strokeWidth, cap = StrokeCap.Round),
        )
    }
    drawDotBand(
        center,
        radius * 0.943f,
        72,
        radius * 0.0042f,
        color,
        stagedProgress(progress, 0.18f, 0.72f),
    )
    drawTriangleBand(
        center = center,
        innerRadius = radius * 0.902f,
        outerRadius = radius * 0.922f,
        count = 64,
        color = color,
        strokeWidth = strokeWidth * 0.82f,
        progress = stagedProgress(progress, 0.30f, 0.84f),
    )

    if (ornaments) {
        repeat(9) { index ->
            val local = stagedProgress(progress, 0.48f + index * 0.035f, 0.72f + index * 0.035f)
            if (local <= 0f) return@repeat
            val angle = -160f + index * 40f
            val at = pointOnCircle(center, radius * 0.855f, angle)
            drawDongSonBird(
                origin = at,
                scale = radius * 0.055f,
                rotationDegrees = angle - 90f,
                color = color.copy(alpha = color.alpha * local),
                strokeWidth = strokeWidth,
            )
        }
    }
}

private fun DrawScope.drawDongSonDrumFace(
    center: Offset,
    radius: Float,
    color: Color,
    strokeWidth: Float,
    background: Color,
    progress: Float,
) {
    // Khung và các vành chia lớp.
    val ringFactors = listOf(1f, 0.984f, 0.962f, 0.932f, 0.902f, 0.858f, 0.825f, 0.755f, 0.715f, 0.625f, 0.585f, 0.46f, 0.425f)
    ringFactors.forEachIndexed { index, factor ->
        val local = stagedProgress(progress, index * 0.018f, 0.34f + index * 0.018f)
        if (local <= 0f) return@forEachIndexed
        drawArc(
            color = color,
            startAngle = -90f,
            sweepAngle = 360f * local,
            useCenter = false,
            topLeft = Offset(center.x - radius * factor, center.y - radius * factor),
            size = Size(radius * factor * 2f, radius * factor * 2f),
            style = Stroke(width = strokeWidth, cap = StrokeCap.Round),
        )
    }

    drawTriangleBand(center, radius * 0.935f, radius * 0.956f, 72, color, strokeWidth * 0.78f, stagedProgress(progress, 0.08f, 0.46f))
    drawTriangleBand(center, radius * 0.865f, radius * 0.888f, 68, color, strokeWidth * 0.78f, stagedProgress(progress, 0.14f, 0.52f))
    drawTangentCircleBand(center, radius * 0.838f, 56, radius * 0.0063f, color, strokeWidth * 0.72f, stagedProgress(progress, 0.20f, 0.58f))
    drawTriangleBand(center, radius * 0.728f, radius * 0.748f, 52, color, strokeWidth * 0.76f, stagedProgress(progress, 0.28f, 0.64f))
    drawTriangleBand(center, radius * 0.595f, radius * 0.618f, 44, color, strokeWidth * 0.74f, stagedProgress(progress, 0.36f, 0.70f))
    drawTangentCircleBand(center, radius * 0.445f, 38, radius * 0.006f, color, strokeWidth * 0.7f, stagedProgress(progress, 0.44f, 0.76f))

    // Vành chim mỏ dài bay ngược chiều kim đồng hồ.
    repeat(18) { index ->
        val local = stagedProgress(progress, 0.30f + index * 0.018f, 0.52f + index * 0.018f)
        if (local <= 0f) return@repeat
        val angle = -90f - index * (360f / 18f)
        val at = pointOnCircle(center, radius * 0.79f, angle)
        drawDongSonBird(
            origin = at,
            scale = radius * 0.058f,
            rotationDegrees = angle - 90f,
            color = color.copy(alpha = color.alpha * local),
            strokeWidth = strokeWidth,
        )
    }

    // Vành chim - hươu xen kẽ như trong mẫu.
    repeat(16) { index ->
        val local = stagedProgress(progress, 0.42f + index * 0.020f, 0.64f + index * 0.020f)
        if (local <= 0f) return@repeat
        val angle = -90f - index * (360f / 16f)
        val at = pointOnCircle(center, radius * 0.67f, angle)
        if (index % 2 == 0) {
            drawDongSonBird(
                origin = at,
                scale = radius * 0.046f,
                rotationDegrees = angle - 90f,
                color = color.copy(alpha = color.alpha * local),
                strokeWidth = strokeWidth * 0.92f,
            )
        } else {
            drawDongSonDeer(
                origin = at,
                scale = radius * 0.046f,
                rotationDegrees = angle - 90f,
                color = color.copy(alpha = color.alpha * local),
                strokeWidth = strokeWidth * 0.92f,
            )
        }
    }

    // Dải sinh hoạt cách điệu: nhà sàn ở phía trên và đoàn người ở nửa dưới.
    val houseAt = pointOnCircle(center, radius * 0.79f, -116f)
    val houseProgress = stagedProgress(progress, 0.54f, 0.78f)
    if (houseProgress > 0f) {
        drawStiltHouse(houseAt, radius * 0.105f, color.copy(alpha = color.alpha * houseProgress), strokeWidth)
    }
    listOf(-134f, -126f, -104f, -96f).forEachIndexed { index, angle ->
        val local = stagedProgress(progress, 0.58f + index * 0.035f, 0.78f + index * 0.035f)
        if (local <= 0f) return@forEachIndexed
        drawDongSonPerson(
            origin = pointOnCircle(center, radius * 0.79f, angle),
            scale = radius * 0.044f,
            rotationDegrees = 0f,
            color = color.copy(alpha = color.alpha * local),
            strokeWidth = strokeWidth,
            holdingDrum = index == 0 || index == 3,
        )
    }
    listOf(42f, 52f, 62f, 72f, 82f, 92f, 102f, 112f, 122f, 132f).forEachIndexed { index, angle ->
        val local = stagedProgress(progress, 0.62f + index * 0.024f, 0.80f + index * 0.020f)
        if (local <= 0f) return@forEachIndexed
        drawDongSonPerson(
            origin = pointOnCircle(center, radius * 0.91f, angle),
            scale = radius * 0.043f,
            rotationDegrees = angle + 90f,
            color = color.copy(alpha = color.alpha * local),
            strokeWidth = strokeWidth,
            holdingDrum = index % 4 == 1,
        )
    }

    drawFourteenRaySun(
        center,
        radius * 0.405f,
        color,
        strokeWidth,
        background,
        stagedProgress(progress, 0.56f, 1f),
    )
}

private fun DrawScope.drawFourteenRaySun(
    center: Offset,
    outerRadius: Float,
    color: Color,
    strokeWidth: Float,
    background: Color,
    progress: Float,
) {
    val innerRadius = outerRadius * 0.26f
    val halfBase = (PI / 14.0 * 0.45).toFloat()
    repeat(14) { index ->
        val local = stagedProgress(progress, index * 0.038f, 0.38f + index * 0.038f)
        if (local <= 0f) return@repeat
        val rayColor = color.copy(alpha = color.alpha * local)
        val angle = (-PI / 2.0 + index * 2.0 * PI / 14.0).toFloat()
        val path = Path().apply {
            moveTo(
                center.x + cos(angle - halfBase) * innerRadius,
                center.y + sin(angle - halfBase) * innerRadius,
            )
            lineTo(
                center.x + cos(angle) * outerRadius,
                center.y + sin(angle) * outerRadius,
            )
            lineTo(
                center.x + cos(angle + halfBase) * innerRadius,
                center.y + sin(angle + halfBase) * innerRadius,
            )
            close()
        }
        drawPath(path, rayColor, style = Stroke(width = strokeWidth, join = StrokeJoin.Round))

        val nestedInner = innerRadius * 1.18f
        val nestedOuter = outerRadius * 0.76f
        val nested = Path().apply {
            moveTo(
                center.x + cos(angle - halfBase * 0.7f) * nestedInner,
                center.y + sin(angle - halfBase * 0.7f) * nestedInner,
            )
            lineTo(
                center.x + cos(angle) * nestedOuter,
                center.y + sin(angle) * nestedOuter,
            )
            lineTo(
                center.x + cos(angle + halfBase * 0.7f) * nestedInner,
                center.y + sin(angle + halfBase * 0.7f) * nestedInner,
            )
            close()
        }
        drawPath(nested, rayColor, style = Stroke(width = strokeWidth * 0.72f, join = StrokeJoin.Round))
    }
    val centerProgress = stagedProgress(progress, 0.68f, 1f)
    if (centerProgress > 0f) {
        drawCircle(color = background.copy(alpha = background.alpha * centerProgress), radius = innerRadius * 0.86f, center = center)
        drawArc(
            color = color,
            startAngle = -90f,
            sweepAngle = 360f * centerProgress,
            useCenter = false,
            topLeft = Offset(center.x - innerRadius * 0.86f, center.y - innerRadius * 0.86f),
            size = Size(innerRadius * 1.72f, innerRadius * 1.72f),
            style = Stroke(width = strokeWidth, cap = StrokeCap.Round),
        )
    }
}

private fun DrawScope.drawTriangleBand(
    center: Offset,
    innerRadius: Float,
    outerRadius: Float,
    count: Int,
    color: Color,
    strokeWidth: Float,
    progress: Float,
) {
    val step = (2.0 * PI / count).toFloat()
    repeat(count) { index ->
        val local = itemRevealProgress(progress, index, count)
        if (local <= 0f) return@repeat
        val itemColor = color.copy(alpha = color.alpha * local)
        val angle = -PI.toFloat() / 2f + index * step
        val half = step * 0.35f
        val inward = index % 2 == 0
        val tipRadius = if (inward) innerRadius else outerRadius
        val baseRadius = if (inward) outerRadius else innerRadius
        val path = Path().apply {
            moveTo(
                center.x + cos(angle - half) * baseRadius,
                center.y + sin(angle - half) * baseRadius,
            )
            lineTo(
                center.x + cos(angle) * tipRadius,
                center.y + sin(angle) * tipRadius,
            )
            lineTo(
                center.x + cos(angle + half) * baseRadius,
                center.y + sin(angle + half) * baseRadius,
            )
            close()
        }
        drawPath(path, itemColor, style = Stroke(width = strokeWidth, join = StrokeJoin.Round))
    }
}

private fun DrawScope.drawDotBand(
    center: Offset,
    radius: Float,
    count: Int,
    dotRadius: Float,
    color: Color,
    progress: Float,
) {
    repeat(count) { index ->
        val local = itemRevealProgress(progress, index, count)
        if (local <= 0f) return@repeat
        val angle = -90f + index * 360f / count
        drawCircle(
            color.copy(alpha = color.alpha * local),
            dotRadius * (0.62f + 0.38f * local),
            pointOnCircle(center, radius, angle),
        )
    }
}

private fun DrawScope.drawTangentCircleBand(
    center: Offset,
    radius: Float,
    count: Int,
    circleRadius: Float,
    color: Color,
    strokeWidth: Float,
    progress: Float,
) {
    val points = List(count) { index ->
        val angle = -90f + index * 360f / count
        pointOnCircle(center, radius, angle)
    }
    points.forEachIndexed { index, point ->
        val local = itemRevealProgress(progress, index, count)
        if (local <= 0f) return@forEachIndexed
        val itemColor = color.copy(alpha = color.alpha * local)
        drawCircle(itemColor, circleRadius, point, style = Stroke(width = strokeWidth))
        val next = points[(index + 1) % count]
        val angle = Math.toRadians((-90.0 + index * 360.0 / count))
        val nextAngle = Math.toRadians((-90.0 + (index + 1) * 360.0 / count))
        val offsetA = Offset(cos(angle).toFloat(), sin(angle).toFloat()) * circleRadius * 0.72f
        val offsetB = Offset(cos(nextAngle).toFloat(), sin(nextAngle).toFloat()) * circleRadius * 0.72f
        val lineStartA = point + offsetA
        val lineStartB = point - offsetA
        val lineEndA = lineStartA + (next + offsetB - lineStartA) * local
        val lineEndB = lineStartB + (next - offsetB - lineStartB) * local
        drawLine(itemColor, lineStartA, lineEndA, strokeWidth)
        drawLine(itemColor, lineStartB, lineEndB, strokeWidth)
    }
}

private fun DrawScope.drawDongSonBird(
    origin: Offset,
    scale: Float,
    rotationDegrees: Float,
    color: Color,
    strokeWidth: Float,
) {
    withTransform({
        translate(origin.x, origin.y)
        rotate(rotationDegrees, pivot = Offset.Zero)
    }) {
        val outline = Path().apply {
            moveTo(-scale * 0.52f, scale * 0.03f)
            lineTo(-scale * 0.22f, -scale * 0.04f)
            lineTo(-scale * 0.04f, -scale * 0.36f)
            lineTo(scale * 0.04f, -scale * 0.08f)
            lineTo(scale * 0.22f, -scale * 0.04f)
            lineTo(scale * 0.55f, -scale * 0.15f)
            lineTo(scale * 0.28f, scale * 0.03f)
            lineTo(scale * 0.10f, scale * 0.08f)
            lineTo(-scale * 0.06f, scale * 0.34f)
            lineTo(-scale * 0.16f, scale * 0.08f)
            close()
        }
        drawPath(outline, color, style = Stroke(width = strokeWidth, cap = StrokeCap.Round, join = StrokeJoin.Round))
        drawLine(color, Offset(-scale * 0.34f, 0f), Offset(-scale * 0.47f, -scale * 0.12f), strokeWidth)
        drawLine(color, Offset(-scale * 0.30f, scale * 0.02f), Offset(-scale * 0.43f, scale * 0.15f), strokeWidth)
        drawLine(color, Offset(scale * 0.13f, -scale * 0.02f), Offset(scale * 0.17f, scale * 0.11f), strokeWidth * 0.72f)
        drawLine(color, Offset(scale * 0.18f, -scale * 0.04f), Offset(scale * 0.22f, scale * 0.08f), strokeWidth * 0.72f)
    }
}

private fun DrawScope.drawDongSonDeer(
    origin: Offset,
    scale: Float,
    rotationDegrees: Float,
    color: Color,
    strokeWidth: Float,
) {
    withTransform({
        translate(origin.x, origin.y)
        rotate(rotationDegrees, pivot = Offset.Zero)
    }) {
        val stroke = Stroke(width = strokeWidth, cap = StrokeCap.Round, join = StrokeJoin.Round)
        val body = Path().apply {
            moveTo(-scale * 0.30f, -scale * 0.08f)
            lineTo(scale * 0.20f, -scale * 0.10f)
            lineTo(scale * 0.30f, scale * 0.08f)
            lineTo(-scale * 0.26f, scale * 0.10f)
            close()
        }
        drawPath(body, color, style = stroke)
        drawLine(color, Offset(scale * 0.19f, -scale * 0.09f), Offset(scale * 0.34f, -scale * 0.30f), strokeWidth)
        drawLine(color, Offset(scale * 0.34f, -scale * 0.30f), Offset(scale * 0.48f, -scale * 0.27f), strokeWidth)
        drawCircle(color, scale * 0.055f, Offset(scale * 0.38f, -scale * 0.31f), style = stroke)
        drawLine(color, Offset(scale * 0.33f, -scale * 0.36f), Offset(scale * 0.28f, -scale * 0.48f), strokeWidth)
        drawLine(color, Offset(scale * 0.31f, -scale * 0.42f), Offset(scale * 0.39f, -scale * 0.49f), strokeWidth)
        drawLine(color, Offset(-scale * 0.18f, scale * 0.08f), Offset(-scale * 0.22f, scale * 0.38f), strokeWidth)
        drawLine(color, Offset(scale * 0.16f, scale * 0.07f), Offset(scale * 0.20f, scale * 0.38f), strokeWidth)
        drawLine(color, Offset(-scale * 0.30f, -scale * 0.04f), Offset(-scale * 0.45f, -scale * 0.18f), strokeWidth)
    }
}

private fun DrawScope.drawDongSonPerson(
    origin: Offset,
    scale: Float,
    rotationDegrees: Float,
    color: Color,
    strokeWidth: Float,
    holdingDrum: Boolean,
) {
    withTransform({
        translate(origin.x, origin.y)
        rotate(rotationDegrees, pivot = Offset.Zero)
    }) {
        val stroke = Stroke(width = strokeWidth, cap = StrokeCap.Round, join = StrokeJoin.Round)
        drawCircle(color, scale * 0.095f, Offset(0f, -scale * 0.34f), style = stroke)
        val body = Path().apply {
            moveTo(-scale * 0.10f, -scale * 0.22f)
            lineTo(scale * 0.11f, -scale * 0.22f)
            lineTo(scale * 0.21f, scale * 0.15f)
            lineTo(-scale * 0.20f, scale * 0.15f)
            close()
        }
        drawPath(body, color, style = stroke)
        repeat(3) { feather ->
            val x = (feather - 1) * scale * 0.07f
            drawLine(
                color,
                Offset(x, -scale * 0.43f),
                Offset(x * 1.5f, -scale * (0.62f + feather * 0.025f)),
                strokeWidth * 0.82f,
            )
        }
        drawLine(color, Offset(-scale * 0.08f, scale * 0.15f), Offset(-scale * 0.15f, scale * 0.47f), strokeWidth)
        drawLine(color, Offset(scale * 0.08f, scale * 0.15f), Offset(scale * 0.18f, scale * 0.47f), strokeWidth)
        drawLine(color, Offset(-scale * 0.08f, -scale * 0.14f), Offset(-scale * 0.30f, scale * 0.02f), strokeWidth)
        drawLine(color, Offset(scale * 0.08f, -scale * 0.14f), Offset(scale * 0.30f, scale * 0.01f), strokeWidth)
        if (holdingDrum) {
            drawCircle(color, scale * 0.14f, Offset(scale * 0.39f, scale * 0.06f), style = stroke)
            drawLine(color, Offset(scale * 0.28f, -scale * 0.02f), Offset(scale * 0.50f, -scale * 0.17f), strokeWidth)
        }
    }
}

private fun DrawScope.drawStiltHouse(
    origin: Offset,
    scale: Float,
    color: Color,
    strokeWidth: Float,
) {
    withTransform({ translate(origin.x, origin.y) }) {
        val stroke = Stroke(width = strokeWidth, cap = StrokeCap.Round, join = StrokeJoin.Round)
        val roof = Path().apply {
            moveTo(-scale * 0.62f, -scale * 0.18f)
            lineTo(-scale * 0.42f, -scale * 0.55f)
            lineTo(scale * 0.42f, -scale * 0.55f)
            lineTo(scale * 0.62f, -scale * 0.18f)
            close()
        }
        drawPath(roof, color, style = stroke)
        drawLine(color, Offset(-scale * 0.45f, -scale * 0.17f), Offset(-scale * 0.45f, scale * 0.30f), strokeWidth)
        drawLine(color, Offset(scale * 0.45f, -scale * 0.17f), Offset(scale * 0.45f, scale * 0.30f), strokeWidth)
        drawLine(color, Offset(-scale * 0.45f, scale * 0.11f), Offset(scale * 0.45f, scale * 0.11f), strokeWidth)
        drawLine(color, Offset(-scale * 0.50f, scale * 0.30f), Offset(scale * 0.50f, scale * 0.30f), strokeWidth)
        listOf(-0.38f, -0.12f, 0.12f, 0.38f).forEach { x ->
            drawLine(color, Offset(scale * x, scale * 0.11f), Offset(scale * x, scale * 0.50f), strokeWidth)
        }
        repeat(7) { index ->
            val x = -scale * 0.35f + index * scale * 0.116f
            drawLine(color, Offset(x, -scale * 0.49f), Offset(x + scale * 0.08f, -scale * 0.24f), strokeWidth * 0.65f)
        }
    }
}

private fun pointOnCircle(center: Offset, radius: Float, angleDegrees: Float): Offset {
    val angle = Math.toRadians(angleDegrees.toDouble())
    return Offset(
        x = center.x + cos(angle).toFloat() * radius,
        y = center.y + sin(angle).toFloat() * radius,
    )
}

/** Ánh xạ một khoảng con của timeline tổng về 0..1 để các lớp được dựng nối tiếp. */
private fun stagedProgress(progress: Float, start: Float, end: Float): Float {
    if (end <= start) return if (progress >= end) 1f else 0f
    return ((progress - start) / (end - start)).coerceIn(0f, 1f)
}

/** Mỗi họa tiết bắt đầu theo thứ tự quanh vành và có một đoạn chồng nhịp ngắn để nét vẽ liền mạch. */
private fun itemRevealProgress(progress: Float, index: Int, count: Int): Float {
    if (count <= 1) return progress.coerceIn(0f, 1f)
    val start = index.toFloat() / count * 0.86f
    return stagedProgress(progress, start, (start + 0.18f).coerceAtMost(1f))
}
