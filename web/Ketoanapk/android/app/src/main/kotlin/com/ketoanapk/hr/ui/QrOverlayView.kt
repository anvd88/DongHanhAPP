package com.ketoanapk.hr.ui

import android.animation.ValueAnimator
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
 * Khung ngắm vẽ trên PreviewView của CameraX.
 *
 * Ba trạng thái:
 *  • **Đang tìm** — bốn góc trắng đứng yên GIỮA màn hình để người dùng biết chĩa vào đâu.
 *  • **Bắt được mã** — bốn góc xanh trượt tới ôm mã, nảy nhẹ và phát một nhịp sáng rồi bám theo.
 *    ML Kit trả về đúng 4 góc thật nên
 *    khung nghiêng/méo theo mã khi cầm máy chếch.
 *  • **Mất mã** — giữ ngắn để hấp thụ vài frame rớt, sau đó mờ dần và trở về khung trắng. Kết quả đã
 *    đọc vẫn do Activity giữ lại; chỉ lớp đánh dấu vị trí biến mất, giống hành vi quan sát ở Zalo.
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
    private val scrimPath = Path().apply { fillType = Path.FillType.EVEN_ODD }
    private val fillPath = Path()
    private val cornerPath = Path()
    private var lastHapticAt = 0L

    private val beginRelease = Runnable { postInvalidateOnAnimation() }

    /** Hình đang vẽ ở khung trước — điểm xuất phát khi khung cần trượt sang mã mới. */
    private var lastDrawnQuad: TrackQuad? = null
    private var flyFrom: TrackQuad? = null
    private var flyStartAt = 0L
    private var lockPulseStartAt = 0L
    private var scrimExitStartAt = 0L

    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = QR_TRACK_FILL_ARGB
        style = Paint.Style.FILL
    }
    private val scrimPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.BLACK
        alpha = OUTSIDE_SCRIM_ALPHA
        style = Paint.Style.FILL
    }
    private val scanLinePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.rgb(77, 158, 255)
        alpha = SCAN_LINE_ALPHA
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeWidth = SCAN_LINE_STROKE_DP * density
    }
    private val cornerShadowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.BLACK
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }
    private val cornerPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.rgb(77, 158, 255)
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }
    private val lockGlowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.rgb(77, 158, 255)
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }
    /**
     * Cập nhật khung. [snapToQr] = true khi vừa chuyển sang MỘT mã khác — khi đó khung TRƯỢT từ chỗ
     * đang đứng (khung ngắm giữa màn hình, hoặc mã trước đó) sang mã mới thay vì nhảy giật.
     */
    fun submitQuad(quad: TrackQuad, snapToQr: Boolean) {
        if (width <= 0 || height <= 0) return
        val now = SystemClock.uptimeMillis()
        val wasVisible = trackingFilter.current(now) != null
        if (snapToQr || !wasVisible) {
            flyFrom = lastDrawnQuad ?: idleQuad()
            flyStartAt = now
            lockPulseStartAt = now
            trackingFilter.reset()
        }
        if (!wasVisible) scrimExitStartAt = now
        trackingFilter.update(measured = clampToView(quad), nowMs = now)
        removeCallbacks(beginRelease)
        val releaseDelay = if (ValueAnimator.areAnimatorsEnabled()) {
            QrTrackingFilter.HOLD_MS + 1L
        } else {
            QrTrackingFilter.RELEASE_MS + 1L
        }
        postDelayed(beginRelease, releaseDelay)
        if ((snapToQr || !wasVisible) && now - lastHapticAt >= HAPTIC_GUARD_MS) {
            performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
            lastHapticAt = now
        }
        postInvalidateOnAnimation()
    }

    /** Quay lại khung ngắm đứng giữa màn hình. */
    fun clear() {
        removeCallbacks(beginRelease)
        trackingFilter.reset()
        flyFrom = null
        lockPulseStartAt = 0L
        scrimExitStartAt = 0L
        lastDrawnQuad = null
        postInvalidateOnAnimation()
    }

    override fun onDraw(canvas: Canvas) {
        val now = SystemClock.uptimeMillis()
        val tracked = trackingFilter.current(now)
        val finder = idleQuad()
        if (tracked == null) {
            trackingFilter.reset()
            flyFrom = null
            lockPulseStartAt = 0L
            scrimExitStartAt = 0L
            lastDrawnQuad = null
            // Chưa bắt được mã: giữ một vùng camera sáng rõ và cho đường quét chuyển động thật nhẹ.
            drawOutsideScrim(canvas, finder, alphaFraction = 1f)
            drawFrame(canvas, finder, tracking = false, trackingAlpha = 1f)
            drawScanLine(canvas, finder, now)
            return
        }

        val animationsEnabled = ValueAnimator.areAnimatorsEnabled()
        val trackingAlpha = if (animationsEnabled) trackingFilter.opacity(now) else 1f
        if (animationsEnabled && trackingAlpha < 1f) postInvalidateOnAnimation()

        val from = flyFrom
        val displayedQuad = if (from == null || !animationsEnabled) {
            flyFrom = null
            if (!animationsEnabled) lockPulseStartAt = 0L
            lockBounce(tracked, now)
        } else {
            val elapsed = now - flyStartAt
            if (elapsed >= FLY_MS) {
                flyFrom = null
                lockBounce(tracked, now)
            } else {
                // Khung tăng tốc rồi hãm mềm ở mã; nhịp nảy + quầng sáng bên dưới tạo cảm giác đã "khóa" mục tiêu.
                val progress = elapsed.toFloat() / FLY_MS
                val eased = easeOutCubic(progress)
                postInvalidateOnAnimation()
                lerpQuad(from, tracked, eased)
            }
        }

        // Lỗ sáng dùng đúng tứ giác đang hiển thị, rồi lớp tối biến mất khi khóa mã. Khi mất mã,
        // cả lỗ sáng và độ tối cùng trở về khung chờ nên không còn lưu lại vùng quét cũ ở giữa màn hình.
        if (drawTrackingScrim(canvas, displayedQuad, finder, trackingAlpha, now, animationsEnabled)) {
            postInvalidateOnAnimation()
        }
        drawTrackedFrame(canvas, displayedQuad, trackingAlpha, now)
    }

    override fun onDetachedFromWindow() {
        removeCallbacks(beginRelease)
        trackingFilter.reset()
        lockPulseStartAt = 0L
        scrimExitStartAt = 0L
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

    /**
     * Khi bắt mã, lớp tối rời đi cùng khung thay vì để lại lỗ sáng cố định. Khi detector mất mã,
     * lớp tối trở lại từ chính vị trí cuối và khép mềm về khung chờ.
     */
    private fun drawTrackingScrim(
        canvas: Canvas,
        displayedQuad: TrackQuad,
        finder: TrackQuad,
        trackingAlpha: Float,
        now: Long,
        animationsEnabled: Boolean,
    ): Boolean {
        if (!animationsEnabled) {
            scrimExitStartAt = 0L
            return false
        }

        val returnProgress = (1f - trackingAlpha).coerceIn(0f, 1f)
        if (returnProgress > 0f) {
            scrimExitStartAt = 0L
            val eased = easeInOut(returnProgress)
            drawOutsideScrim(
                canvas = canvas,
                finder = lerpQuad(displayedQuad, finder, eased),
                alphaFraction = eased,
            )
            return returnProgress < 1f
        }

        val exitStartedAt = scrimExitStartAt
        if (exitStartedAt == 0L) return false
        val progress = ((now - exitStartedAt).toFloat() / SCRIM_EXIT_MS).coerceIn(0f, 1f)
        if (progress >= 1f) {
            scrimExitStartAt = 0L
            return false
        }
        drawOutsideScrim(canvas, displayedQuad, alphaFraction = 1f - easeOutCubic(progress))
        return true
    }

    /** Làm tối phần camera ngoài đúng tứ giác đang hiển thị, kể cả khi QR bị nghiêng hoặc méo phối cảnh. */
    private fun drawOutsideScrim(canvas: Canvas, finder: TrackQuad, alphaFraction: Float) {
        scrimPath.reset()
        scrimPath.fillType = Path.FillType.EVEN_ODD
        scrimPath.addRect(0f, 0f, width.toFloat(), height.toFloat(), Path.Direction.CW)
        addRoundedQuad(scrimPath, finder, FINDER_RADIUS_DP * density)
        scrimPaint.alpha = (OUTSIDE_SCRIM_ALPHA * alphaFraction.coerceIn(0f, 1f)).roundToInt()
        canvas.drawPath(scrimPath, scrimPaint)
    }

    private fun addRoundedQuad(path: Path, quad: TrackQuad, requestedRadius: Float) {
        val points = arrayOf(quad.topLeft, quad.topRight, quad.bottomRight, quad.bottomLeft)
        fun toward(from: TrackPoint, to: TrackPoint, distance: Float): TrackPoint {
            val dx = to.x - from.x
            val dy = to.y - from.y
            val edge = hypot(dx, dy).coerceAtLeast(1f)
            val step = min(distance, edge * MAX_CUTOUT_RADIUS_EDGE_FRACTION)
            return TrackPoint(from.x + dx / edge * step, from.y + dy / edge * step)
        }

        val radius = min(requestedRadius, edgeLength(quad) * CUTOUT_RADIUS_RATIO)
        val entries = Array(points.size) { index ->
            toward(points[index], points[(index + points.lastIndex) % points.size], radius)
        }
        val exits = Array(points.size) { index ->
            toward(points[index], points[(index + 1) % points.size], radius)
        }

        path.moveTo(exits[0].x, exits[0].y)
        for (index in 1 until points.size) {
            path.lineTo(entries[index].x, entries[index].y)
            path.quadTo(points[index].x, points[index].y, exits[index].x, exits[index].y)
        }
        path.lineTo(entries[0].x, entries[0].y)
        path.quadTo(points[0].x, points[0].y, exits[0].x, exits[0].y)
        path.close()
    }

    private fun easeOutCubic(progress: Float): Float {
        val remaining = 1f - progress.coerceIn(0f, 1f)
        return 1f - remaining * remaining * remaining
    }

    private fun easeInOut(progress: Float): Float {
        val value = progress.coerceIn(0f, 1f)
        return value * value * (3f - 2f * value)
    }

    private fun drawScanLine(canvas: Canvas, finder: TrackQuad, now: Long) {
        if (!ValueAnimator.areAnimatorsEnabled()) return
        val progress = (now % SCAN_LINE_PERIOD_MS).toFloat() / SCAN_LINE_PERIOD_MS
        val inset = SCAN_LINE_INSET_DP * density
        val top = finder.topLeft.y + inset
        val bottom = finder.bottomLeft.y - inset
        val y = top + (bottom - top) * progress
        canvas.drawLine(finder.topLeft.x + inset, y, finder.topRight.x - inset, y, scanLinePaint)
        postInvalidateOnAnimation()
    }

    /** Vẽ quầng sáng trước rồi mới vẽ nét góc sắc phía trên để hiệu ứng khóa vẫn rõ trên nền camera. */
    private fun drawTrackedFrame(canvas: Canvas, quad: TrackQuad, trackingAlpha: Float, now: Long) {
        val pulseActive = drawLockGlow(canvas, quad, now)
        drawFrame(canvas, quad, tracking = true, trackingAlpha = trackingAlpha)
        if (pulseActive) postInvalidateOnAnimation()
    }

    /**
     * Sau khi bốn góc chạm mã, khung nở ra tối đa 5% rồi thu về đúng bốn góc detector. Biên độ nhỏ
     * giúp người dùng nhận ra thời điểm quét trúng mà không che nội dung mã hoặc tạo cảm giác rung giật.
     */
    private fun lockBounce(quad: TrackQuad, now: Long): TrackQuad {
        val elapsed = now - lockPulseStartAt
        if (lockPulseStartAt == 0L || elapsed < FLY_MS || elapsed >= LOCK_ANIMATION_MS) return quad
        val progress = (elapsed - FLY_MS).toFloat() / (LOCK_ANIMATION_MS - FLY_MS)
        val bounce = kotlin.math.sin(Math.PI.toFloat() * progress) * (1f - progress)
        return scaleAroundCenter(quad, 1f + LOCK_BOUNCE_SCALE * bounce)
    }

    private fun scaleAroundCenter(quad: TrackQuad, scale: Float): TrackQuad {
        val center = quad.pointAt(0.5f, 0.5f)
        fun scalePoint(point: TrackPoint) = TrackPoint(
            x = center.x + (point.x - center.x) * scale,
            y = center.y + (point.y - center.y) * scale,
        )
        return TrackQuad(
            topLeft = scalePoint(quad.topLeft),
            topRight = scalePoint(quad.topRight),
            bottomRight = scalePoint(quad.bottomRight),
            bottomLeft = scalePoint(quad.bottomLeft),
        )
    }

    private fun drawLockGlow(canvas: Canvas, quad: TrackQuad, now: Long): Boolean {
        val elapsed = now - lockPulseStartAt
        if (lockPulseStartAt == 0L || elapsed < FLY_MS || elapsed >= LOCK_ANIMATION_MS) {
            if (elapsed >= LOCK_ANIMATION_MS) lockPulseStartAt = 0L
            return false
        }
        val progress = (elapsed - FLY_MS).toFloat() / (LOCK_ANIMATION_MS - FLY_MS)
        val fade = (1f - progress) * (1f - progress)
        buildRoundedCornerPath(quad)
        lockGlowPaint.alpha = (LOCK_GLOW_ALPHA * fade).roundToInt()
        lockGlowPaint.strokeWidth = cornerPaint.strokeWidth + LOCK_GLOW_EXTRA_DP * density * fade
        canvas.drawPath(cornerPath, lockGlowPaint)
        return true
    }

    private fun drawFrame(canvas: Canvas, quad: TrackQuad, tracking: Boolean, trackingAlpha: Float) {
        lastDrawnQuad = quad

        // Chỉ phủ một lớp vàng rất nhẹ khi nhận diện. Không kẻ lưới lên mã: bốn góc màu đã đủ chỉ vị
        // trí, còn ảnh QR nên được giữ thoáng và dễ nhìn như giao diện Zalo.
        if (tracking) {
            fillPath.reset()
            fillPath.moveTo(quad.topLeft.x, quad.topLeft.y)
            fillPath.lineTo(quad.topRight.x, quad.topRight.y)
            fillPath.lineTo(quad.bottomRight.x, quad.bottomRight.y)
            fillPath.lineTo(quad.bottomLeft.x, quad.bottomLeft.y)
            fillPath.close()
            fillPaint.alpha = (TRACK_FILL_ALPHA * trackingAlpha).roundToInt()
            canvas.drawPath(fillPath, fillPaint)
        }

        val lineColor = if (tracking) Color.rgb(77, 158, 255) else Color.WHITE
        buildRoundedCornerPath(quad)
        cornerPaint.color = lineColor
        cornerPaint.alpha = if (tracking) (255 * trackingAlpha).roundToInt() else IDLE_CORNER_ALPHA
        cornerShadowPaint.alpha = if (tracking) {
            (TRACK_SHADOW_ALPHA * trackingAlpha).roundToInt()
        } else {
            IDLE_SHADOW_ALPHA
        }
        canvas.drawPath(cornerPath, cornerShadowPaint)
        canvas.drawPath(cornerPath, cornerPaint)
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
        const val FLY_MS = 160L
        const val SCRIM_EXIT_MS = FLY_MS
        const val LOCK_ANIMATION_MS = 520L
        const val LOCK_BOUNCE_SCALE = 0.05f
        const val LOCK_GLOW_ALPHA = 150
        const val LOCK_GLOW_EXTRA_DP = 12f
        /** Khung ngắm chiếm 62% cạnh ngắn màn hình — đủ rộng để đưa mã vào, không lấn hết hình. */
        const val IDLE_SIDE_RATIO = 0.62f
        const val IDLE_CORNER_ALPHA = 210
        const val IDLE_SHADOW_ALPHA = 56
        const val OUTSIDE_SCRIM_ALPHA = 112
        const val FINDER_RADIUS_DP = 28f
        const val CUTOUT_RADIUS_RATIO = 0.12f
        const val MAX_CUTOUT_RADIUS_EDGE_FRACTION = 0.34f
        const val SCAN_LINE_ALPHA = 150
        const val SCAN_LINE_STROKE_DP = 2f
        const val SCAN_LINE_INSET_DP = 18f
        const val SCAN_LINE_PERIOD_MS = 1_800L
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
        /** Nền xanh chỉ để chỉ rõ vùng mã, cố ý rất nhạt để không lấn mất chính mã QR bên dưới. */
        const val TRACK_FILL_ALPHA = 0x30
        const val TRACK_SHADOW_ALPHA = 92
        const val HAPTIC_GUARD_MS = 1_000L
    }
}
