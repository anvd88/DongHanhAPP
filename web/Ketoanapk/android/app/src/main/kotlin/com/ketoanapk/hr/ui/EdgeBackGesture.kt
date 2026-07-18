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
 * Cử chỉ Back chung của app: bắt đầu trong dải hẹp ở mép phải rồi kéo sang trái.
 *
 * Nội dung bám trực tiếp theo ngón tay. Kéo chưa đủ thì đàn hồi về; kéo đủ thì màn hiện tại trượt ra,
 * gọi đúng BackHandler đang có độ ưu tiên cao nhất, rồi màn trước trượt vào từ phía phải.
 */
@Composable
internal fun RightEdgeBackContainer(
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

    val dragState = rememberDraggableState { delta ->
        if (!animating && widthPx > 0f) {
            // Cử chỉ chỉ đi từ phải sang trái; kéo ngược chiều không làm nội dung trôi khỏi vị trí gốc.
            offsetPx = (offsetPx + delta).coerceIn(-widthPx, 0f)
        }
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

        // Dấu hiệu Back xuất hiện dần trong phần nền vừa được hé ra ở mép phải.
        Surface(
            modifier = Modifier
                .align(Alignment.CenterEnd)
                .padding(end = 14.dp)
                .size(42.dp)
                .graphicsLayer {
                    val progress = if (widthPx <= 0f) 0f else (-offsetPx / widthPx).coerceIn(0f, 1f)
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
                    Icons.AutoMirrored.Filled.ArrowBack,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onPrimary,
                    modifier = Modifier.size(22.dp),
                )
            }
        }

        // Dải 28dp nằm trên cùng để cử chỉ mép luôn thắng các cử chỉ ngang của màn con (như lịch).
        Box(
            modifier = Modifier
                .align(Alignment.CenterEnd)
                .fillMaxHeight()
                .width(28.dp)
                .testTag("app-edge-back-handle")
                .draggable(
                    enabled = enabled && !animating,
                    state = dragState,
                    orientation = Orientation.Horizontal,
                    onDragStopped = { velocity ->
                        val commit = shouldCommitRightEdgeBack(
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
                                targetValue = -widthPx,
                                initialVelocity = velocity,
                                animationSpec = tween(durationMillis = 150, easing = FastOutSlowInEasing),
                            ) { value, _ -> offsetPx = value }

                            currentOnBack()
                            // Chờ BackHandler cập nhật màn con/màn trước trước khi đưa nội dung mới vào.
                            withFrameNanos { }
                            if (widthPx > 0f) {
                                offsetPx = widthPx
                                animate(
                                    initialValue = offsetPx,
                                    targetValue = 0f,
                                    animationSpec = tween(durationMillis = 220, easing = FastOutSlowInEasing),
                                ) { value, _ -> offsetPx = value }
                            }
                        }
                        offsetPx = 0f
                        animating = false
                    },
                ),
        )
    }
}

internal fun shouldCommitRightEdgeBack(
    dragOffsetPx: Float,
    widthPx: Float,
    velocityXPx: Float,
    minimumDistancePx: Float,
    minimumVelocityXPx: Float,
): Boolean {
    if (dragOffsetPx >= 0f || widthPx <= 0f) return false
    val distanceThreshold = maxOf(minimumDistancePx, widthPx * 0.18f)
    return dragOffsetPx <= -distanceThreshold || velocityXPx <= -minimumVelocityXPx
}
