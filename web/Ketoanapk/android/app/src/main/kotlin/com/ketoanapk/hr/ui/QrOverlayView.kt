package com.ketoanapk.hr.ui

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.os.SystemClock
import android.util.AttributeSet
import android.view.HapticFeedbackConstants
import android.view.View
import kotlin.math.hypot
import kotlin.math.min
import kotlin.math.roundToInt

/**
 * Khung ngắm + lưới kẻ ô, vẽ trên PreviewView của CameraX.
 *
 * Hai trạng thái:
 *  • **Đang tìm** — khung vuông kèm lưới đứng yên GIỮA màn hình để người dùng biết chĩa vào đâu.
 *  • **Bắt được mã** — lưới rời khỏi giữa màn hình, TRƯỢT tới ôm lấy mã rồi bám theo. ML Kit trả về
 *    đúng 4 góc thật của mã nên lưới nghiêng/méo y hệt mã khi cầm máy chếch (xem [TrackQuad.pointAt]).
 *
 * Khác bản cũ (bám vào ViewfinderView của thư viện zxing): view này nhận thẳng TỨ GIÁC ĐÃ ĐỔI SANG
 * TOẠ ĐỘ MÀN HÌNH; bản cũ phải suy ra góc từ 3 tâm ô định vị nên khung hay lệch với mã méo phối cảnh.
 */
class QrOverlayView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
) : View(context, attrs) {
    private val density = resources.displayMetrics.density
    private val trackingFilter = QrTrackingFilter()
    private val fillPath = Path()
    private val cornerPath = Path()
    private val gridPath = Path()
    private var lastHapticAt = 0L

    /** Hình đang vẽ ở khung trước — điểm XUẤT PHÁT khi lưới cần trượt sang mã mới. */
    private var lastDrawnQuad: TrackQuad? = null
    private var flyFrom: TrackQuad? = null
    private var flyStartAt = 0L

    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = QR_TRACK_FILL_ARGB
        style = Paint.Style.FILL
    }
    private val cornerShadowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.BLACK
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }
    private val cornerPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.rgb(255, 196, 0)
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }
    private val gridPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
    }
    private val gridShadowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.BLACK
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
    }

    /**
     * Cập nhật khung. [snapToQr] = true khi vừa chuyển sang MỘT mã khác — khi đó lưới TRƯỢT từ chỗ
     * đang đứng (khung ngắm giữa màn hình, hoặc mã trước đó) sang mã mới thay vì nhảy giật.
     */
    fun submitQuad(quad: TrackQuad, snapToQr: Boolean) {
        if (width <= 0 || height <= 0) return
        val now = SystemClock.uptimeMillis()
        val wasVisible = trackingFilter.retained() != null
        if (snapToQr || !wasVisible) {
            flyFrom = lastDrawnQuad ?: idleQuad()
            flyStartAt = now
            trackingFilter.reset()
        }
        trackingFilter.update(measured = clampToView(quad), nowMs = now, releaseWhenStale = false)
        if ((snapToQr || !wasVisible) && now - lastHapticAt >= HAPTIC_GUARD_MS) {
            performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
            lastHapticAt = now
        }
        postInvalidateOnAnimation()
    }

    /** Quay lại khung ngắm đứng giữa màn hình. */
    fun clear() {
        trackingFilter.reset()
        flyFrom = null
        lastDrawnQuad = null
        postInvalidateOnAnimation()
    }

    override fun onDraw(canvas: Canvas) {
        val tracked = trackingFilter.retained()
        if (tracked == null) {
            // Chưa bắt được mã: khung ngắm đứng yên, màu trắng mờ để không tranh chấp với khung vàng.
            drawFrame(canvas, idleQuad(), tracking = false)
            return
        }

        val from = flyFrom
        if (from == null) {
            drawFrame(canvas, tracked, tracking = true)
            return
        }

        val elapsed = SystemClock.uptimeMillis() - flyStartAt
        if (elapsed >= FLY_MS) {
            flyFrom = null
            drawFrame(canvas, tracked, tracking = true)
            return
        }
        // Chậm dần về cuối: lưới lao tới mã rồi "dính" vào, thay vì đứng khựng lại đột ngột.
        val progress = elapsed.toFloat() / FLY_MS
        val eased = 1f - (1f - progress) * (1f - progress) * (1f - progress)
        drawFrame(canvas, lerpQuad(from, tracked, eased), tracking = true)
        postInvalidateOnAnimation()
    }

    override fun onDetachedFromWindow() {
        trackingFilter.reset()
        super.onDetachedFromWindow()
    }

    /** Khung ngắm vuông đứng giữa màn hình, dùng khi chưa bắt được mã nào. */
    private fun idleQuad(): TrackQuad {
        val side = min(width, height) * IDLE_SIDE_RATIO
        val centerX = width / 2f
        val centerY = height / 2f
        val radius = side / 2f
        return TrackQuad(
            topLeft = TrackPoint(centerX - radius, centerY - radius),
            topRight = TrackPoint(centerX + radius, centerY - radius),
            bottomRight = TrackPoint(centerX + radius, centerY + radius),
            bottomLeft = TrackPoint(centerX - radius, centerY + radius),
        )
    }

    private fun drawFrame(canvas: Canvas, quad: TrackQuad, tracking: Boolean) {
        lastDrawnQuad = quad

        // Nền và lưới CHỈ vẽ khi đã nhận diện được mã. Lúc đang tìm thì chỉ có khung ngắm bốn góc —
        // màn hình thoáng, không có gì che tầm nhìn khi người dùng đang căn mã.
        if (tracking) {
            fillPath.reset()
            fillPath.moveTo(quad.topLeft.x, quad.topLeft.y)
            fillPath.lineTo(quad.topRight.x, quad.topRight.y)
            fillPath.lineTo(quad.bottomRight.x, quad.bottomRight.y)
            fillPath.lineTo(quad.bottomLeft.x, quad.bottomLeft.y)
            fillPath.close()
            fillPaint.alpha = TRACK_FILL_ALPHA
            canvas.drawPath(fillPath, fillPaint)

            buildGridPath(quad)
            gridPaint.color = Color.rgb(255, 196, 0)
            gridPaint.alpha = TRACK_GRID_ALPHA
            gridShadowPaint.alpha = TRACK_SHADOW_ALPHA
            canvas.drawPath(gridPath, gridShadowPaint)
            canvas.drawPath(gridPath, gridPaint)
        }

        val lineColor = if (tracking) Color.rgb(255, 196, 0) else Color.WHITE
        buildRoundedCornerPath(quad)
        cornerPaint.color = lineColor
        cornerPaint.alpha = if (tracking) 255 else IDLE_CORNER_ALPHA
        cornerShadowPaint.alpha = if (tracking) TRACK_SHADOW_ALPHA else IDLE_SHADOW_ALPHA
        canvas.drawPath(cornerPath, cornerShadowPaint)
        canvas.drawPath(cornerPath, cornerPaint)
    }

    /**
     * Lưới [GRID_CELLS]×[GRID_CELLS] kẻ bên trong tứ giác. Mỗi đường nối hai điểm trên hai cạnh đối
     * diện qua [TrackQuad.pointAt] nên lưới nghiêng và méo đúng theo mã, không phải lưới vuông dán đè.
     */
    private fun buildGridPath(quad: TrackQuad) {
        gridPath.reset()
        val stroke = (edgeLength(quad) * GRID_STROKE_RATIO)
            .coerceIn(MIN_GRID_STROKE_DP * density, MAX_GRID_STROKE_DP * density)
        gridPaint.strokeWidth = stroke
        gridShadowPaint.strokeWidth = stroke + GRID_SHADOW_EXTRA_DP * density

        for (i in 1 until GRID_CELLS) {
            val ratio = i.toFloat() / GRID_CELLS
            val top = quad.pointAt(ratio, 0f)
            val bottom = quad.pointAt(ratio, 1f)
            gridPath.moveTo(top.x, top.y)
            gridPath.lineTo(bottom.x, bottom.y)

            val left = quad.pointAt(0f, ratio)
            val right = quad.pointAt(1f, ratio)
            gridPath.moveTo(left.x, left.y)
            gridPath.lineTo(right.x, right.y)
        }
    }

    private fun edgeLength(quad: TrackQuad) = minOf(
        distance(quad.topLeft, quad.topRight),
        distance(quad.topRight, quad.bottomRight),
        distance(quad.bottomRight, quad.bottomLeft),
        distance(quad.bottomLeft, quad.topLeft),
    )

    private fun buildRoundedCornerPath(quad: TrackQuad) {
        val shortestEdge = edgeLength(quad)
        val length = (shortestEdge * CORNER_LENGTH_RATIO).coerceIn(
            MIN_CORNER_LENGTH_DP * density,
            MAX_CORNER_LENGTH_DP * density,
        )
        val radius = min(CORNER_RADIUS_DP * density, length * 0.36f)
        cornerPaint.strokeWidth = (shortestEdge * CORNER_STROKE_RATIO).coerceIn(
            MIN_CORNER_STROKE_DP * density,
            MAX_CORNER_STROKE_DP * density,
        )
        cornerShadowPaint.strokeWidth = cornerPaint.strokeWidth + CORNER_SHADOW_EXTRA_DP * density

        cornerPath.reset()
        appendRoundedCorner(cornerPath, quad.topLeft, quad.topRight, quad.bottomLeft, length, radius)
        appendRoundedCorner(cornerPath, quad.topRight, quad.topLeft, quad.bottomRight, length, radius)
        appendRoundedCorner(cornerPath, quad.bottomRight, quad.topRight, quad.bottomLeft, length, radius)
        appendRoundedCorner(cornerPath, quad.bottomLeft, quad.topLeft, quad.bottomRight, length, radius)
    }

    private fun appendRoundedCorner(
        path: Path,
        corner: TrackPoint,
        firstNeighbour: TrackPoint,
        secondNeighbour: TrackPoint,
        requestedLength: Float,
        requestedRadius: Float,
    ) {
        val firstDx = firstNeighbour.x - corner.x
        val firstDy = firstNeighbour.y - corner.y
        val secondDx = secondNeighbour.x - corner.x
        val secondDy = secondNeighbour.y - corner.y
        val firstEdge = hypot(firstDx, firstDy).coerceAtLeast(1f)
        val secondEdge = hypot(secondDx, secondDy).coerceAtLeast(1f)
        val firstLength = min(requestedLength, firstEdge * MAX_EDGE_FRACTION)
        val secondLength = min(requestedLength, secondEdge * MAX_EDGE_FRACTION)
        val firstRadius = min(requestedRadius, firstLength * 0.48f)
        val secondRadius = min(requestedRadius, secondLength * 0.48f)

        val firstUnitX = firstDx / firstEdge
        val firstUnitY = firstDy / firstEdge
        val secondUnitX = secondDx / secondEdge
        val secondUnitY = secondDy / secondEdge
        path.moveTo(corner.x + firstUnitX * firstLength, corner.y + firstUnitY * firstLength)
        path.lineTo(corner.x + firstUnitX * firstRadius, corner.y + firstUnitY * firstRadius)
        path.quadTo(
            corner.x,
            corner.y,
            corner.x + secondUnitX * secondRadius,
            corner.y + secondUnitY * secondRadius,
        )
        path.lineTo(corner.x + secondUnitX * secondLength, corner.y + secondUnitY * secondLength)
    }

    private fun clampToView(quad: TrackQuad): TrackQuad {
        val inset = VIEW_EDGE_INSET_DP * density
        val maxX = (width.toFloat() - inset).coerceAtLeast(inset)
        val maxY = (height.toFloat() - inset).coerceAtLeast(inset)
        fun clamp(point: TrackPoint) = TrackPoint(
            x = point.x.coerceIn(inset, maxX),
            y = point.y.coerceIn(inset, maxY),
        )
        return TrackQuad(
            topLeft = clamp(quad.topLeft),
            topRight = clamp(quad.topRight),
            bottomRight = clamp(quad.bottomRight),
            bottomLeft = clamp(quad.bottomLeft),
        )
    }

    private fun distance(first: TrackPoint, second: TrackPoint) =
        hypot(first.x - second.x, first.y - second.y)

    private companion object {
        /** Lưới 5×5 ô: rõ hiệu ứng mà không rối mắt hay che mất mã. Chỉ hiện khi đã nhận diện được mã. */
        const val GRID_CELLS = 5
        const val FLY_MS = 260L
        /** Khung ngắm chiếm 62% cạnh ngắn màn hình — đủ rộng để đưa mã vào, không lấn hết hình. */
        const val IDLE_SIDE_RATIO = 0.62f
        const val IDLE_CORNER_ALPHA = 210
        const val IDLE_SHADOW_ALPHA = 56
        const val TRACK_GRID_ALPHA = 150
        const val GRID_STROKE_RATIO = 0.014f
        const val MIN_GRID_STROKE_DP = 0.8f
        const val MAX_GRID_STROKE_DP = 2f
        const val GRID_SHADOW_EXTRA_DP = 1.5f
        const val VIEW_EDGE_INSET_DP = 6f
        const val MIN_CORNER_LENGTH_DP = 4f
        const val MAX_CORNER_LENGTH_DP = 54f
        const val CORNER_RADIUS_DP = 12f
        const val CORNER_LENGTH_RATIO = 0.23f
        const val CORNER_STROKE_RATIO = 0.035f
        const val MIN_CORNER_STROKE_DP = 1.5f
        const val MAX_CORNER_STROKE_DP = 5.5f
        const val CORNER_SHADOW_EXTRA_DP = 3.5f
        const val MAX_EDGE_FRACTION = 0.38f
        /** Nền vàng chỉ để chỉ rõ vùng mã, cố ý rất nhạt để không lấn mất chính mã QR bên dưới. */
        const val TRACK_FILL_ALPHA = 0x16
        const val TRACK_SHADOW_ALPHA = 92
        const val HAPTIC_GUARD_MS = 1_000L
    }
}
