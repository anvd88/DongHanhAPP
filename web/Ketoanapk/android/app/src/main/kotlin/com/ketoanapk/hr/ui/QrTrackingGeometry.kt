package com.ketoanapk.hr.ui

import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.min

/** Lightweight geometry shared by the camera overlay and local unit tests. */
data class TrackPoint(val x: Float, val y: Float)

data class TrackQuad(
    val topLeft: TrackPoint,
    val topRight: TrackPoint,
    val bottomRight: TrackPoint,
    val bottomLeft: TrackPoint,
) {
    /**
     * Điểm bên trong tứ giác theo toạ độ tương đối: (0,0) = topLeft, (1,0) = topRight, (1,1) = bottomRight.
     *
     * Nội suy song tuyến theo hai cạnh trên/dưới, nhờ vậy lưới kẻ bên trong tự NGHIÊNG và MÉO đúng
     * theo mã khi người dùng cầm máy chếch — không cần ma trận phối cảnh riêng.
     */
    fun pointAt(u: Float, v: Float): TrackPoint {
        val topX = topLeft.x + (topRight.x - topLeft.x) * u
        val topY = topLeft.y + (topRight.y - topLeft.y) * u
        val bottomX = bottomLeft.x + (bottomRight.x - bottomLeft.x) * u
        val bottomY = bottomLeft.y + (bottomRight.y - bottomLeft.y) * u
        return TrackPoint(topX + (bottomX - topX) * v, topY + (bottomY - topY) * v)
    }
}

/** Trộn hai tứ giác theo tiến độ [progress] (0 = [from], 1 = [to]) để lưới trượt mượt sang vị trí mới. */
fun lerpQuad(from: TrackQuad, to: TrackQuad, progress: Float): TrackQuad {
    val t = progress.coerceIn(0f, 1f)
    fun blend(a: TrackPoint, b: TrackPoint) = TrackPoint(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t)
    return TrackQuad(
        topLeft = blend(from.topLeft, to.topLeft),
        topRight = blend(from.topRight, to.topRight),
        bottomRight = blend(from.bottomRight, to.bottomRight),
        bottomLeft = blend(from.bottomLeft, to.bottomLeft),
    )
}

/** Translucent amber-yellow laid over a QR only while it is being tracked. */
const val QR_TRACK_FILL_ARGB: Int = 0x30FFC400

/** Vùng ảnh phân tích đang thực sự hiển thị trên PreviewView (`ImageProxy.cropRect`). */
data class QrCropRect(val left: Int, val top: Int, val right: Int, val bottom: Int) {
    val width: Int get() = right - left
    val height: Int get() = bottom - top
}

/**
 * Đổi 4 góc mã (toạ độ ảnh của ML Kit) sang toạ độ màn hình của PreviewView.
 *
 * KHÔNG suy đoán tỉ lệ khung hình. CameraX được bind kèm `ViewPort` nên mỗi khung phân tích mang theo
 * `cropRect` = ĐÚNG vùng ảnh mà PreviewView đang hiển thị, kể cả khi Preview và ImageAnalysis được cấp
 * hai độ phân giải khác tỉ lệ. Trước đây chỗ này tự tính hệ số phóng theo giả định "hai luồng cùng tỉ
 * lệ" — giả định đó sai trên máy mà ImageAnalysis rơi về 4:3 trong khi Preview giữ 16:9, và khung vàng
 * vẽ lệch hẳn khỏi mã.
 *
 * Tách khỏi Activity để chạy được bằng unit test thường (không cần thiết bị).
 */
object QrFrameMapper {
    /**
     * `cropRect` nằm trong ảnh CHƯA xoay, còn toạ độ ML Kit nằm trong ảnh ĐÃ xoay — phải đưa về cùng hệ.
     * [rotationDegrees] là số độ quay THEO CHIỀU KIM ĐỒNG HỒ để ảnh đứng thẳng.
     */
    fun rotateCrop(crop: QrCropRect, imageWidth: Int, imageHeight: Int, rotationDegrees: Int): QrCropRect =
        when (((rotationDegrees % 360) + 360) % 360) {
            90 -> QrCropRect(imageHeight - crop.bottom, crop.left, imageHeight - crop.top, crop.right)
            180 -> QrCropRect(imageWidth - crop.right, imageHeight - crop.bottom, imageWidth - crop.left, imageHeight - crop.top)
            270 -> QrCropRect(crop.top, imageWidth - crop.right, crop.bottom, imageWidth - crop.left)
            else -> crop
        }

    /**
     * Tâm mã có nằm trong vùng người dùng ĐANG NHÌN THẤY không.
     *
     * ML Kit soi toàn bộ khung cảm biến, nhưng ViewPort chỉ hiển thị một phần của nó — trên màn hình
     * dài, kiểu cắt FILL_CENTER bỏ đi rất nhiều. Không lọc thì app quét trúng mã nằm ngoài tầm nhìn:
     * người dùng "quét nhầm thứ mình không chĩa vào" (nguy với phiếu chi/chấm công), còn khung vàng
     * thì bị kẹp dính vào mép màn hình trông như lỗi.
     *
     * Thiếu bốn góc thì caller dùng tâm bounding-box làm fallback. Nếu detector không trả được cả hai,
     * không thể chứng minh mã nằm trong preview nên phải bỏ qua, nhất là với chấm công/phiếu chi.
     */
    fun isVisible(corners: List<TrackPoint>, crop: QrCropRect): Boolean {
        if (corners.size < 4) return false
        val centerX = (corners[0].x + corners[1].x + corners[2].x + corners[3].x) / 4f
        val centerY = (corners[0].y + corners[1].y + corners[2].y + corners[3].y) / 4f
        return isPointVisible(TrackPoint(centerX, centerY), crop)
    }

    /** Fallback for detectors that decoded a value and bounding box but omitted corner points. */
    fun isPointVisible(point: TrackPoint, crop: QrCropRect): Boolean =
        crop.width > 0 && crop.height > 0 &&
            point.x >= crop.left && point.x <= crop.right &&
            point.y >= crop.top && point.y <= crop.bottom

    /** Trả null khi thiếu góc hoặc kích thước chưa hợp lệ — khi đó chỉ là không vẽ khung, mã vẫn quét được. */
    fun map(corners: List<TrackPoint>, crop: QrCropRect, viewWidth: Float, viewHeight: Float): TrackQuad? {
        if (corners.size < 4) return null
        if (crop.width <= 0 || crop.height <= 0 || viewWidth <= 0f || viewHeight <= 0f) return null
        val scaleX = viewWidth / crop.width
        val scaleY = viewHeight / crop.height
        fun at(index: Int) = TrackPoint(
            x = (corners[index].x - crop.left) * scaleX,
            y = (corners[index].y - crop.top) * scaleY,
        )
        // ML Kit trả góc theo chiều kim đồng hồ từ trên-trái, tính theo hướng CỦA CHÍNH MÃ (không phải
        // hướng màn hình). Giữ nguyên thứ tự đó: mỗi góc luôn khớp đúng góc vật lý cũ khi mã xoay, nhờ
        // vậy bộ lọc làm mượt không bị "hoán góc" mỗi lần người dùng nghiêng điện thoại.
        return TrackQuad(topLeft = at(0), topRight = at(1), bottomRight = at(2), bottomLeft = at(3))
    }
}

/**
 * Adaptive one-euro-style tracker without frame history arrays.
 *
 * Small perspective noise is damped strongly. Normal motion follows immediately, while a very
 * large jump must be observed twice before it can move the overlay. This removes the corner-frame
 * twitch seen when a tilted phone makes the detector briefly report a distorted quad.
 */
class QrTrackingFilter {
    private var filtered: TrackQuad? = null
    private var pendingOutlier: TrackQuad? = null
    private var lastAcceptedAt = Long.MIN_VALUE

    fun update(
        measured: TrackQuad,
        nowMs: Long,
    ): TrackQuad {
        val current = filtered
        if (current == null || age(nowMs) > RELEASE_MS) {
            filtered = measured
            pendingOutlier = null
            lastAcceptedAt = nowMs
            return measured
        }

        val currentScale = characteristicSize(current).coerceAtLeast(1f)
        val measuredScale = characteristicSize(measured).coerceAtLeast(1f)
        val scaleRatio = max(currentScale, measuredScale) / min(currentScale, measuredScale)
        val centerMotion = distance(center(current), center(measured)) / currentScale
        val cornerMotion = maxCornerDistance(current, measured) / currentScale
        val isLargeJump =
            centerMotion > LARGE_CENTER_JUMP ||
                cornerMotion > LARGE_CORNER_JUMP ||
                scaleRatio > LARGE_SCALE_JUMP

        if (isLargeJump) {
            val pending = pendingOutlier
            if (pending == null || !sameCandidate(pending, measured)) {
                pendingOutlier = measured
                return current
            }

            // Two matching large-jump frames indicate real camera motion, not a detector glitch.
            val elapsed = elapsedSinceAccepted(nowMs)
            val centerAlpha = timeAdjustedAlpha(
                CONFIRMED_JUMP_CENTER_ALPHA,
                elapsed,
                MAX_CONFIRMED_CENTER_ALPHA,
            )
            val shapeAlpha = timeAdjustedAlpha(
                CONFIRMED_JUMP_SHAPE_ALPHA,
                elapsed,
                MAX_CONFIRMED_SHAPE_ALPHA,
            )
            return blendCenterAndShape(current, measured, centerAlpha, shapeAlpha).also {
                filtered = it
                pendingOutlier = null
                lastAcceptedAt = nowMs
            }
        }

        pendingOutlier = null
        val currentCenter = center(current)
        val measuredCenter = center(measured)
        val shapeMotion = maxCenteredCornerDistance(
            current,
            measured,
            currentCenter,
            measuredCenter,
        ) / currentScale
        val centerBaseAlpha = when {
            centerMotion < VERY_SMALL_CENTER_MOTION -> VERY_SMALL_CENTER_ALPHA
            centerMotion < SMALL_CENTER_MOTION -> SMALL_CENTER_ALPHA
            centerMotion < MEDIUM_CENTER_MOTION -> MEDIUM_CENTER_ALPHA
            else -> NORMAL_CENTER_ALPHA
        }
        val shapeBaseAlpha = when {
            shapeMotion < VERY_SMALL_SHAPE_MOTION -> VERY_SMALL_SHAPE_ALPHA
            shapeMotion < SMALL_SHAPE_MOTION -> SMALL_SHAPE_ALPHA
            shapeMotion < MEDIUM_SHAPE_MOTION -> MEDIUM_SHAPE_ALPHA
            else -> NORMAL_SHAPE_ALPHA
        }
        val elapsed = elapsedSinceAccepted(nowMs)
        val centerAlpha = timeAdjustedAlpha(centerBaseAlpha, elapsed, MAX_NORMAL_CENTER_ALPHA)
        val shapeAlpha = timeAdjustedAlpha(shapeBaseAlpha, elapsed, MAX_NORMAL_SHAPE_ALPHA)
        return blendCenterAndShape(current, measured, centerAlpha, shapeAlpha).also {
            filtered = it
            lastAcceptedAt = nowMs
        }
    }

    /** Keeps the last quad through a short solid hold and a soft fade, then releases it. */
    fun current(nowMs: Long): TrackQuad? {
        val value = filtered ?: return null
        return value.takeIf { age(nowMs) <= RELEASE_MS }
    }

    /**
     * Opacity for the stale-detection window: stay solid briefly to absorb dropped detector frames,
     * then fade to the idle view. This is intentionally independent from geometry smoothing so the
     * result card can remain visible after the yellow tracking frame has gone away.
     */
    fun opacity(nowMs: Long): Float {
        if (filtered == null) return 0f
        val age = age(nowMs)
        if (age <= HOLD_MS) return 1f
        if (age >= RELEASE_MS) return 0f
        return 1f - (age - HOLD_MS).toFloat() / (RELEASE_MS - HOLD_MS).toFloat()
    }

    fun reset() {
        filtered = null
        pendingOutlier = null
        lastAcceptedAt = Long.MIN_VALUE
    }

    private fun age(nowMs: Long) = (nowMs - lastAcceptedAt).coerceAtLeast(0L)

    private fun elapsedSinceAccepted(nowMs: Long): Long {
        if (lastAcceptedAt == Long.MIN_VALUE) return NOMINAL_FRAME_MS
        return (nowMs - lastAcceptedAt).coerceIn(MIN_FRAME_MS, MAX_FRAME_MS)
    }

    private fun sameCandidate(first: TrackQuad, second: TrackQuad): Boolean {
        val scale = max(characteristicSize(first), characteristicSize(second)).coerceAtLeast(1f)
        val scaleRatio = max(characteristicSize(first), characteristicSize(second)) /
            min(characteristicSize(first), characteristicSize(second)).coerceAtLeast(1f)
        return distance(center(first), center(second)) <= scale * OUTLIER_CONFIRM_CENTER &&
            maxCornerDistance(first, second) <= scale * OUTLIER_CONFIRM_CORNER &&
            scaleRatio <= OUTLIER_CONFIRM_SCALE
    }

    private fun timeAdjustedAlpha(base: Float, elapsedMs: Long, maximum: Float): Float {
        val frameFactor = elapsedMs.toFloat() / NOMINAL_FRAME_MS.toFloat()
        return (base * frameFactor).coerceIn(base * 0.72f, maximum)
    }

    private fun blendCenterAndShape(
        from: TrackQuad,
        measured: TrackQuad,
        centerAlpha: Float,
        shapeAlpha: Float,
    ): TrackQuad {
        val fromCenter = center(from)
        val measuredCenter = center(measured)
        val nextCenter = blend(fromCenter, measuredCenter, centerAlpha)

        // Detector noise often alternates between a slightly smaller and larger tilted quad. Ignore
        // area breathing below 6%; above it, allow at most 4% shrink or 10% growth per accepted
        // frame. Rotation and perspective still update through the separate, slower shape alpha.
        val fromArea = area(from).coerceAtLeast(1f)
        val measuredArea = area(measured).coerceAtLeast(1f)
        val areaRatio = measuredArea / fromArea
        val rawLinearRatio = kotlin.math.sqrt(areaRatio)
        val allowedLinearRatio = if (abs(areaRatio - 1f) <= AREA_DEADBAND) {
            1f
        } else {
            rawLinearRatio.coerceIn(MIN_SHRINK_PER_FRAME, MAX_GROWTH_PER_FRAME)
        }
        val measuredShapeScale = allowedLinearRatio / rawLinearRatio.coerceAtLeast(0.001f)

        fun point(fromPoint: TrackPoint, measuredPoint: TrackPoint): TrackPoint {
            val fromOffsetX = fromPoint.x - fromCenter.x
            val fromOffsetY = fromPoint.y - fromCenter.y
            val measuredOffsetX = (measuredPoint.x - measuredCenter.x) * measuredShapeScale
            val measuredOffsetY = (measuredPoint.y - measuredCenter.y) * measuredShapeScale
            return TrackPoint(
                x = nextCenter.x + fromOffsetX + (measuredOffsetX - fromOffsetX) * shapeAlpha,
                y = nextCenter.y + fromOffsetY + (measuredOffsetY - fromOffsetY) * shapeAlpha,
            )
        }

        return TrackQuad(
            topLeft = point(from.topLeft, measured.topLeft),
            topRight = point(from.topRight, measured.topRight),
            bottomRight = point(from.bottomRight, measured.bottomRight),
            bottomLeft = point(from.bottomLeft, measured.bottomLeft),
        )
    }

    private fun blend(from: TrackPoint, to: TrackPoint, alpha: Float) = TrackPoint(
        x = from.x + (to.x - from.x) * alpha,
        y = from.y + (to.y - from.y) * alpha,
    )

    private fun center(quad: TrackQuad) = TrackPoint(
        x = (quad.topLeft.x + quad.topRight.x + quad.bottomRight.x + quad.bottomLeft.x) * 0.25f,
        y = (quad.topLeft.y + quad.topRight.y + quad.bottomRight.y + quad.bottomLeft.y) * 0.25f,
    )

    private fun characteristicSize(quad: TrackQuad) = (
        distance(quad.topLeft, quad.topRight) +
            distance(quad.topRight, quad.bottomRight) +
            distance(quad.bottomRight, quad.bottomLeft) +
            distance(quad.bottomLeft, quad.topLeft)
        ) * 0.25f

    private fun area(quad: TrackQuad): Float {
        val twiceArea =
            quad.topLeft.x * quad.topRight.y - quad.topLeft.y * quad.topRight.x +
                quad.topRight.x * quad.bottomRight.y - quad.topRight.y * quad.bottomRight.x +
                quad.bottomRight.x * quad.bottomLeft.y - quad.bottomRight.y * quad.bottomLeft.x +
                quad.bottomLeft.x * quad.topLeft.y - quad.bottomLeft.y * quad.topLeft.x
        return abs(twiceArea) * 0.5f
    }

    private fun maxCenteredCornerDistance(
        first: TrackQuad,
        second: TrackQuad,
        firstCenter: TrackPoint,
        secondCenter: TrackPoint,
    ) = maxOf(
        centeredDistance(first.topLeft, firstCenter, second.topLeft, secondCenter),
        centeredDistance(first.topRight, firstCenter, second.topRight, secondCenter),
        centeredDistance(first.bottomRight, firstCenter, second.bottomRight, secondCenter),
        centeredDistance(first.bottomLeft, firstCenter, second.bottomLeft, secondCenter),
    )

    private fun centeredDistance(
        first: TrackPoint,
        firstCenter: TrackPoint,
        second: TrackPoint,
        secondCenter: TrackPoint,
    ) = hypot(
        (first.x - firstCenter.x) - (second.x - secondCenter.x),
        (first.y - firstCenter.y) - (second.y - secondCenter.y),
    )

    private fun maxCornerDistance(first: TrackQuad, second: TrackQuad) = maxOf(
        distance(first.topLeft, second.topLeft),
        distance(first.topRight, second.topRight),
        distance(first.bottomRight, second.bottomRight),
        distance(first.bottomLeft, second.bottomLeft),
    )

    private fun distance(first: TrackPoint, second: TrackPoint) =
        hypot(first.x - second.x, first.y - second.y)

    companion object {
        const val HOLD_MS = 520L
        const val RELEASE_MS = 850L

        private const val NOMINAL_FRAME_MS = 16L
        private const val MIN_FRAME_MS = 8L
        private const val MAX_FRAME_MS = 42L
        private const val VERY_SMALL_CENTER_MOTION = 0.025f
        private const val SMALL_CENTER_MOTION = 0.12f
        private const val MEDIUM_CENTER_MOTION = 0.46f
        private const val VERY_SMALL_CENTER_ALPHA = 0.18f
        private const val SMALL_CENTER_ALPHA = 0.34f
        private const val MEDIUM_CENTER_ALPHA = 0.46f
        private const val NORMAL_CENTER_ALPHA = 0.56f
        private const val VERY_SMALL_SHAPE_MOTION = 0.025f
        private const val SMALL_SHAPE_MOTION = 0.09f
        private const val MEDIUM_SHAPE_MOTION = 0.24f
        private const val VERY_SMALL_SHAPE_ALPHA = 0.09f
        private const val SMALL_SHAPE_ALPHA = 0.16f
        private const val MEDIUM_SHAPE_ALPHA = 0.24f
        private const val NORMAL_SHAPE_ALPHA = 0.32f
        private const val CONFIRMED_JUMP_CENTER_ALPHA = 0.78f
        private const val CONFIRMED_JUMP_SHAPE_ALPHA = 0.40f
        private const val MAX_NORMAL_CENTER_ALPHA = 0.72f
        private const val MAX_NORMAL_SHAPE_ALPHA = 0.42f
        private const val MAX_CONFIRMED_CENTER_ALPHA = 0.90f
        private const val MAX_CONFIRMED_SHAPE_ALPHA = 0.58f
        private const val AREA_DEADBAND = 0.06f
        private const val MIN_SHRINK_PER_FRAME = 0.96f
        private const val MAX_GROWTH_PER_FRAME = 1.10f
        private const val LARGE_CENTER_JUMP = 0.82f
        private const val LARGE_CORNER_JUMP = 1.08f
        private const val LARGE_SCALE_JUMP = 1.72f
        private const val OUTLIER_CONFIRM_CENTER = 0.30f
        private const val OUTLIER_CONFIRM_CORNER = 0.42f
        private const val OUTLIER_CONFIRM_SCALE = 1.32f
    }
}
