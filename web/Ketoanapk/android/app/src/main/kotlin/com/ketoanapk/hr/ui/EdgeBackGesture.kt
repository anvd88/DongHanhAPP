package com.ketoanapk.hr.ui

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.animate
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.Orientation
import androidx.compose.foundation.gestures.draggable
import androidx.compose.foundation.gestures.rememberDraggableState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp

/**
 * Mép màn hình bắt cử chỉ Back. `direction` là chiều ngón tay phải đi để trở lại:
 * mép trái kéo sang phải (+1), mép phải kéo sang trái (-1).
 */
internal enum class BackEdge(val direction: Float) {
    Left(1f),
    Right(-1f),
}

/**
 * Cử chỉ Back chung của app: bắt đầu trong dải hẹp ở MÉP TRÁI (kéo sang phải) hoặc MÉP PHẢI
 * (kéo sang trái).
 *
 * Nội dung bám trực tiếp theo ngón tay. Kéo chưa đủ thì đàn hồi về; kéo đủ thì màn hiện tại trượt ra,
 * gọi đúng BackHandler đang có độ ưu tiên cao nhất, rồi màn trước trượt vào từ phía đối diện.
 */
@Composable
internal fun EdgeBackContainer(
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable BoxScope.() -> Unit,
) {
    var widthPx by remember { mutableFloatStateOf(0f) }
    var offsetPx by remember { mutableFloatStateOf(0f) }
    var animating by remember { mutableStateOf(false) }
    val currentOnBack by rememberUpdatedState(onBack)
    val density = LocalDensity.current
    val minimumDistancePx = with(density) { 64.dp.toPx() }
    val minimumFlingVelocityPx = with(density) { 900.dp.toPx() }

    // Mỗi mép chỉ cho nội dung trôi về đúng phía của nó; kéo ngược chiều không làm màn rời vị trí gốc.
    fun drag(edge: BackEdge, delta: Float) {
        if (animating || widthPx <= 0f) return
        val range = if (edge == BackEdge.Left) 0f..widthPx else -widthPx..0f
        offsetPx = (offsetPx + delta).coerceIn(range.start, range.endInclusive)
    }

    suspend fun settle(edge: BackEdge, velocity: Float) {
        val commit = shouldCommitEdgeBack(
            edge = edge,
            dragOffsetPx = offsetPx,
            widthPx = widthPx,
            velocityXPx = velocity,
            minimumDistancePx = minimumDistancePx,
            minimumVelocityXPx = minimumFlingVelocityPx,
        )
        animating = true
        if (!commit) {
            animate(
                initialValue = offsetPx,
                targetValue = 0f,
                initialVelocity = velocity,
                animationSpec = spring(dampingRatio = 0.82f, stiffness = 520f),
            ) { value, _ -> offsetPx = value }
        } else {
            animate(
                initialValue = offsetPx,
                targetValue = widthPx * edge.direction,
                initialVelocity = velocity,
                animationSpec = tween(durationMillis = 150, easing = FastOutSlowInEasing),
            ) { value, _ -> offsetPx = value }

            currentOnBack()
            // Chờ BackHandler cập nhật màn con/màn trước trước khi đưa nội dung mới vào.
            withFrameNanos { }
            if (widthPx > 0f) {
                // Màn trước vào từ phía ĐỐI DIỆN với hướng màn cũ vừa trượt ra.
                offsetPx = -widthPx * edge.direction
                animate(
                    initialValue = offsetPx,
                    targetValue = 0f,
                    animationSpec = tween(durationMillis = 220, easing = FastOutSlowInEasing),
                ) { value, _ -> offsetPx = value }
            }
        }
        offsetPx = 0f
        animating = false
    }

    Box(
        modifier = modifier
            .fillMaxSize()
            .clipToBounds()
            .background(MaterialTheme.colorScheme.background)
            .onSizeChanged { widthPx = it.width.toFloat() }
            .testTag("app-edge-back-root"),
    ) {
        // Chỉ đọc offset trong graphics layer để lúc kéo không tái compose toàn bộ màn hình.
        Box(
            modifier = Modifier
                .fillMaxSize()
                .graphicsLayer { translationX = offsetPx },
            content = content,
        )

        BackEdge.entries.forEach { edge ->
            val alignment = if (edge == BackEdge.Left) Alignment.CenterStart else Alignment.CenterEnd

            // Dấu hiệu Back xuất hiện dần trong phần nền vừa được hé ra ở mép đang kéo.
            Surface(
                modifier = Modifier
                    .align(alignment)
                    .padding(
                        start = if (edge == BackEdge.Left) 14.dp else 0.dp,
                        end = if (edge == BackEdge.Right) 14.dp else 0.dp,
                    )
                    .size(42.dp)
                    .graphicsLayer {
                        // Offset cùng dấu với chiều của mép này mới là lượt kéo của nó.
                        val travelled = offsetPx * edge.direction
                        val progress = if (widthPx <= 0f) 0f else (travelled / widthPx).coerceIn(0f, 1f)
                        alpha = (progress * 2.4f).coerceIn(0f, 1f)
                        val scale = 0.72f + progress * 0.28f
                        scaleX = scale
                        scaleY = scale
                    },
                shape = CircleShape,
                color = MaterialTheme.colorScheme.primary,
                shadowElevation = 4.dp,
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        if (edge == BackEdge.Left) Icons.AutoMirrored.Filled.ArrowBack else Icons.AutoMirrored.Filled.ArrowForward,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.onPrimary,
                        modifier = Modifier.size(22.dp),
                    )
                }
            }
        }

        // Hai dải 28dp nằm trên cùng để cử chỉ mép luôn thắng các cử chỉ ngang của màn con (như lịch).
        BackEdge.entries.forEach { edge ->
            val dragState = rememberDraggableState { delta -> drag(edge, delta) }
            Box(
                modifier = Modifier
                    .align(if (edge == BackEdge.Left) Alignment.CenterStart else Alignment.CenterEnd)
                    .fillMaxHeight()
                    .width(28.dp)
                    .testTag(if (edge == BackEdge.Left) "app-edge-back-handle-left" else "app-edge-back-handle")
                    .draggable(
                        enabled = enabled && !animating,
                        state = dragState,
                        orientation = Orientation.Horizontal,
                        onDragStopped = { velocity -> settle(edge, velocity) },
                    ),
            )
        }
    }
}

internal fun shouldCommitEdgeBack(
    edge: BackEdge,
    dragOffsetPx: Float,
    widthPx: Float,
    velocityXPx: Float,
    minimumDistancePx: Float,
    minimumVelocityXPx: Float,
): Boolean {
    if (widthPx <= 0f) return false
    // Quy về hệ "đi tới" của mép đang kéo: quãng đường và vận tốc dương = đang hướng về phía Back.
    val travelledPx = dragOffsetPx * edge.direction
    val velocityTowardBackPx = velocityXPx * edge.direction
    if (travelledPx <= 0f) return false
    val distanceThreshold = maxOf(minimumDistancePx, widthPx * 0.18f)
    return travelledPx >= distanceThreshold || velocityTowardBackPx >= minimumVelocityXPx
}
