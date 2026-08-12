package com.ketoanapk.hr.ui

import android.graphics.Matrix as AndroidMatrix
import android.graphics.Path as AndroidPath
import android.graphics.PathMeasure as AndroidPathMeasure
import android.graphics.RectF as AndroidRectF
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.animation.core.Easing
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathFillType
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.asAndroidPath
import androidx.compose.ui.graphics.asComposePath
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.graphics.drawscope.withTransform
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlin.math.PI
import kotlin.math.min
import kotlin.math.sin

// Màu và path lấy từ bộ nhận diện chính thức trong logo-src.
private val LogoRed = Color(0xFFEC0200)
private val LogoYellow = Color(0xFFFCDE00)
private val LogoNavy = Color(0xFF21305B)
private val IntroBg = Color(0xFFF4F4F2)
private const val DESIGN_W = 1000f
private const val DESIGN_H = 675f

// Giữ nhịp dựng nhiều lớp của intro cũ; chỉ nâng cấp đoạn chờ/morph và đoạn handoff vào app.
private const val T_MORPH_DUR = 820f
private const val T_COG_REVEAL_START = 620f
private const val T_COG_REVEAL_DUR = 160f
private const val T_COG_SETTLE_DUR = 850f
private const val T_CRE_DRAW_START = 720f
private const val T_CRE_DRAW_DUR = 850f
private const val T_CRE_FILL_START = 1320f
private const val T_CRE_FILL_DUR = 400f
private const val T_TRI_BASE_START = 900f
private const val T_TRI_BASE_DUR = 400f
private const val T_TRI_SIDES_START = 1150f
private const val T_TRI_SIDES_DUR = 900f
private const val T_P_DRAW_START = 1100f
private const val T_P_DRAW_DUR = 950f
private const val T_P_FILL_START = 1840f
private const val T_P_FILL_DUR = 450f
private const val T_TRIFILL_START = 1850f
private const val T_TRIFILL_DUR = 650f
private const val T_BANNER_START = 2350f
private const val T_BANNER_DUR = 650f
private const val T_TEXT_START = 2600f
private const val T_TEXT_DUR = 850f
private const val T_SHINE_START = 3420f
private const val T_SHINE_DUR = 720f
private const val T_SETTLE_START = 3500f
private const val T_SETTLE_DUR = 650f
private const val T_HANDOFF_START = 3970f
private const val T_TOTAL = 4470f
private const val T_EXIT_DUR = 650

// Tâm thật của nửa bánh răng trong logo-src. Path COG nằm bên trái trục này; lật chính path đó qua
// trục dọc sẽ tạo thành bánh răng chờ tròn mà không phải tự suy đoán số răng hay biên dạng răng.
private val BrandGearPivot = Offset(519.58f, 333.26f)
private const val WAITING_BRAND_GEAR_SCALE = 1.09f
private const val BRAND_GEAR_CLIP_LEFT = 300f
private const val BRAND_GEAR_CLIP_TOP = 145f
private const val BRAND_GEAR_CLIP_RIGHT = 740f
private const val BRAND_GEAR_CLIP_BOTTOM = 520f

private val SoftSettle: Easing = CubicBezierEasing(0.16f, 1f, 0.3f, 1f)

internal enum class IntroDestination { Login, Home }

private fun seg(elapsed: Float, start: Float, dur: Float, easing: Easing = LinearEasing): Float {
    val t = ((elapsed - start) / dur).coerceIn(0f, 1f)
    return easing.transform(t)
}

private fun mix(from: Float, to: Float, progress: Float): Float =
    from + (to - from) * progress.coerceIn(0f, 1f)

private fun parse(d: String, evenOdd: Boolean = false): Path {
    val path = PathParser().parsePathString(d).toPath()
    if (evenOdd) path.fillType = PathFillType.EvenOdd
    return path
}

/** Đo trước độ dài từng contour để giữ nguyên hiệu ứng vẽ nét mượt của intro cũ. */
private class Traced(composePath: Path) {
    val androidPath: AndroidPath = composePath.asAndroidPath()
    val lengths: FloatArray
    val total: Float

    init {
        val measure = AndroidPathMeasure(androidPath, false)
        val contourLengths = ArrayList<Float>()
        do {
            contourLengths.add(measure.length)
        } while (measure.nextContour())
        lengths = contourLengths.toFloatArray()
        total = lengths.sum()
    }
}

/**
 * Intro thương hiệu:
 * 1) bánh răng tròn quay đến khi bootstrap/API có kết quả;
 * 2) thu về C bánh răng chính thức;
 * 3) dựng lại trọn vẹn chuỗi trace/fill/banner/shine của hiệu ứng cũ;
 * 4) handoff riêng sang Login hoặc Home.
 */
@Composable
internal fun IntroOverlay(
    bootstrapReady: Boolean,
    destination: IntroDestination,
    onHandoffStarted: () -> Unit,
    onFinished: () -> Unit,
) {
    val currentOnHandoffStarted by rememberUpdatedState(onHandoffStarted)
    val currentOnFinished by rememberUpdatedState(onFinished)
    val waitingRotation = remember { Animatable(0f) }
    val elapsed = remember { Animatable(0f) }
    val exitProgress = remember { Animatable(0f) }
    var revealStarted by remember { mutableStateOf(false) }
    var landingRotation by remember { mutableFloatStateOf(0f) }

    // Chỉ quay khi còn chờ. Khi ready, coroutine này bị hủy đúng frame và góc hiện tại được giữ lại.
    LaunchedEffect(bootstrapReady) {
        if (bootstrapReady) return@LaunchedEffect
        while (isActive) {
            waitingRotation.snapTo(0f)
            waitingRotation.animateTo(
                targetValue = 360f,
                animationSpec = tween(durationMillis = 960, easing = LinearEasing),
            )
        }
    }

    LaunchedEffect(bootstrapReady) {
        if (!bootstrapReady || revealStarted) return@LaunchedEffect
        revealStarted = true
        // Hạ về góc 0 theo cung ngắn nhất để đúng nửa bánh răng gốc mà không giật ngược một vòng.
        landingRotation = ((waitingRotation.value + 180f) % 360f) - 180f

        elapsed.animateTo(
            targetValue = T_HANDOFF_START,
            animationSpec = tween(T_HANDOFF_START.toInt(), easing = LinearEasing),
        )
        currentOnHandoffStarted()
        coroutineScope {
            launch {
                elapsed.animateTo(
                    targetValue = T_TOTAL,
                    animationSpec = tween((T_TOTAL - T_HANDOFF_START).toInt(), easing = LinearEasing),
                )
            }
            launch {
                exitProgress.animateTo(1f, tween(T_EXIT_DUR, easing = SoftSettle))
            }
        }
        currentOnFinished()
    }

    val triFill = remember { parse(IntroLogoPaths.TRI_FILL) }
    val cog = remember { parse(IntroLogoPaths.COG, evenOdd = true) }
    val fullWaitingGear = remember(cog) { createSeamlessWaitingGear(cog) }
    val crescent = remember { parse(IntroLogoPaths.CRESCENT) }
    val crescentTraced = remember { Traced(crescent) }
    val letterP = remember { parse(IntroLogoPaths.LETTER_P, evenOdd = true) }
    val letterPTraced = remember { Traced(letterP) }
    val banner = remember { parse(IntroLogoPaths.BANNER) }
    val text = remember { parse(IntroLogoPaths.TEXT, evenOdd = true) }
    // Vẽ đáy trước, rồi hai cạnh chạy đối xứng từ hai góc lên đỉnh.
    // Không có bất kỳ nét chéo đơn lẻ nào mọc ra cạnh bánh răng.
    val triBase = remember { Path().apply { moveTo(26.5f, 560f); lineTo(972f, 560f) } }
    val triLeft = remember { Path().apply { moveTo(26.5f, 560f); lineTo(502.75f, 17.5f) } }
    val triRight = remember { Path().apply { moveTo(972f, 560f); lineTo(502.75f, 17.5f) } }
    val triBaseTraced = remember { Traced(triBase) }
    val triLeftTraced = remember { Traced(triLeft) }
    val triRightTraced = remember { Traced(triRight) }

    Canvas(
        modifier = Modifier
            .fillMaxSize()
            .alpha(1f - exitProgress.value)
            .background(IntroBg)
            // Lớp intro chặn toàn bộ thao tác xuyên xuống form/màn chính ở phía dưới.
            .pointerInput(Unit) {
                awaitPointerEventScope {
                    while (true) {
                        awaitPointerEvent(PointerEventPass.Initial).changes.forEach { it.consume() }
                    }
                }
            },
    ) {
        val e = elapsed.value
        val exit = exitProgress.value
        val fit = min(size.width / DESIGN_W, size.height / DESIGN_H) * 0.9f
        val offsetX = (size.width - DESIGN_W * fit) / 2f
        val offsetY = (size.height - DESIGN_H * fit) / 2f

        val settle = seg(e, T_SETTLE_START, T_SETTLE_DUR)
        val settleScale = 1f + 0.015f * sin((PI * settle).toFloat())
        val exitScale = when (destination) {
            IntroDestination.Login -> mix(1f, 0.985f, exit)
            IntroDestination.Home -> mix(1f, 1.04f, exit)
        }
        val exitY = if (destination == IntroDestination.Login) -26f * exit else 0f

        withTransform({
            translate(offsetX, offsetY)
            scale(fit, fit, pivot = Offset.Zero)
            translate(0f, exitY)
            scale(settleScale * exitScale, settleScale * exitScale, pivot = Offset(500f, 325f))
        }) {
            // Nền và khung tam giác: giữ chất liệu cũ nhưng tách cạnh để nét chạy cân đối.
            val triFillAlpha = seg(e, T_TRIFILL_START, T_TRIFILL_DUR, FastOutSlowInEasing)
            if (triFillAlpha > 0f) drawPath(triFill, LogoYellow, alpha = triFillAlpha)
            drawTraced(
                triBaseTraced,
                seg(e, T_TRI_BASE_START, T_TRI_BASE_DUR, FastOutSlowInEasing),
                LogoRed,
                22f,
            )
            val sideProgress = seg(e, T_TRI_SIDES_START, T_TRI_SIDES_DUR, FastOutSlowInEasing)
            drawTraced(triLeftTraced, sideProgress, LogoRed, 22f)
            drawTraced(triRightTraced, sideProgress, LogoRed, 22f)

            // Bánh răng chờ được ghép từ đúng nửa bánh răng C của logo: một bản gốc và một bản lật.
            // Khi API sẵn sàng, bản lật thu vào trục giữa để để lại nguyên vẹn COG thương hiệu.
            val morph = seg(e, 0f, T_MORPH_DUR, SoftSettle)
            val waitingCenter = Offset(
                x = mix(500f, BrandGearPivot.x, morph),
                y = mix(305f, BrandGearPivot.y, morph),
            )
            val waitingScale = mix(WAITING_BRAND_GEAR_SCALE, 1f, morph)
            val waitingAlpha = 1f - seg(e, T_COG_REVEAL_START, T_COG_REVEAL_DUR)
            val waitingPulse = if (revealStarted) {
                1f
            } else {
                1f + 0.012f * sin((waitingRotation.value / 180f * PI).toFloat() * 2f)
            }
            if (waitingAlpha > 0f) {
                drawBrandWaitingGear(
                    fullGear = fullWaitingGear,
                    halfGear = cog,
                    center = waitingCenter,
                    scale = waitingScale * waitingPulse,
                    rotation = if (revealStarted) mix(landingRotation, 0f, morph) else waitingRotation.value,
                    cutProgress = morph,
                    alpha = waitingAlpha,
                )
            }

            // Crossfade rất ngắn khi hai hình đã khớp tuyệt đối; chỉ còn một nhịp co giãn nhẹ để chốt C gear.
            val cogAlpha = seg(e, T_COG_REVEAL_START + 10f, T_COG_REVEAL_DUR, LinearOutSlowInEasing)
            if (cogAlpha > 0f) {
                val cogSettle = seg(e, T_COG_REVEAL_START, T_COG_SETTLE_DUR, SoftSettle)
                val cogScale = mix(0.97f, 1f, cogSettle)
                withTransform({
                    scale(cogScale, cogScale, pivot = BrandGearPivot)
                }) {
                    drawPath(cog, LogoRed, alpha = cogAlpha)
                }
            }

            // Khôi phục trace chữ C và chữ P của intro cũ.
            drawTraced(
                crescentTraced,
                seg(e, T_CRE_DRAW_START, T_CRE_DRAW_DUR, LinearOutSlowInEasing),
                LogoRed,
                3f,
            )
            val crescentFillAlpha = seg(e, T_CRE_FILL_START, T_CRE_FILL_DUR)
            if (crescentFillAlpha > 0f) drawPath(crescent, LogoRed, alpha = crescentFillAlpha)

            drawTraced(
                letterPTraced,
                seg(e, T_P_DRAW_START, T_P_DRAW_DUR, LinearOutSlowInEasing),
                LogoNavy,
                3f,
            )
            val pFillAlpha = seg(e, T_P_FILL_START, T_P_FILL_DUR)
            if (pFillAlpha > 0f) drawPath(letterP, LogoNavy, alpha = pFillAlpha)

            // Dải băng và chữ thương hiệu giữ nguyên cách xuất hiện của bản cũ.
            val bannerProgress = seg(e, T_BANNER_START, T_BANNER_DUR, SoftSettle)
            if (bannerProgress > 0f) {
                withTransform({ translate(0f, (1f - bannerProgress) * 24f) }) {
                    drawPath(banner, LogoYellow, alpha = bannerProgress)
                }
            }
            val textProgress = seg(e, T_TEXT_START, T_TEXT_DUR)
            if (textProgress > 0f) {
                clipRect(left = 0f, top = 575f, right = DESIGN_W * textProgress, bottom = DESIGN_H) {
                    drawPath(text, LogoRed)
                }
            }

            // Vệt sáng kim loại cũ được làm dịu thành hai lớp để bớt gắt nhưng vẫn có điểm kết.
            val shineProgress = seg(e, T_SHINE_START, T_SHINE_DUR)
            if (shineProgress > 0f && shineProgress < 1f) {
                val x = -320f + shineProgress * 1520f
                val glow = sin((PI * shineProgress).toFloat())
                clipPath(triFill) {
                    drawShineBand(x = x, width = 150f, alpha = 0.18f * glow)
                    drawShineBand(x = x + 95f, width = 54f, alpha = 0.07f * glow)
                }
            }
        }
    }
}

/**
 * Tạo bánh răng tròn bằng hai bản của CHÍNH path nửa bánh răng trong logo. Không có hình học răng
 * tự sinh. [collapse] chỉ thu bản lật về trục giữa; bản gốc không biến dạng và trở thành COG cuối.
 */
private fun createSeamlessWaitingGear(halfGear: Path): Path {
    val source = AndroidPath(halfGear.asAndroidPath())
    val mirrored = AndroidPath(source).apply {
        transform(
            AndroidMatrix().apply {
                setScale(-1f, 1f, BrandGearPivot.x, BrandGearPivot.y)
            },
        )
    }
    val seamless = AndroidPath().apply {
        op(source, mirrored, AndroidPath.Op.UNION)
    }
    val terminalSeams = AndroidPath().apply {
        // Remove only the two radial closing bars contributed by the pair of mirrored C paths.
        // The outer and inner horizontal bands remain continuous, so this reads as one gear.
        addRect(AndroidRectF(512.9f, 176.2f, 526.3f, 206.1f), AndroidPath.Direction.CW)
        addRect(AndroidRectF(512.9f, 455.5f, 526.3f, 490.4f), AndroidPath.Direction.CW)
    }
    return AndroidPath().apply {
        op(seamless, terminalSeams, AndroidPath.Op.DIFFERENCE)
    }.asComposePath()
}

private fun DrawScope.drawBrandWaitingGear(
    fullGear: Path,
    halfGear: Path,
    center: Offset,
    scale: Float,
    rotation: Float,
    cutProgress: Float,
    alpha: Float,
) {
    val translatedX = center.x - BrandGearPivot.x
    val translatedY = center.y - BrandGearPivot.y
    val cut = cutProgress.coerceIn(0f, 1f)
    val cutEdge = mix(BRAND_GEAR_CLIP_RIGHT, BrandGearPivot.x, cut)
    val rebuild = ((cut - 0.52f) / 0.48f).coerceIn(0f, 1f)

    withTransform({ translate(translatedX, translatedY) }) {
        withTransform({
            rotate(rotation, pivot = BrandGearPivot)
            scale(scale, scale, pivot = BrandGearPivot)
        }) {
            fun DrawScope.drawMorphLayer(color: Color, layerAlpha: Float) {
                clipRect(
                    left = BRAND_GEAR_CLIP_LEFT,
                    top = BRAND_GEAR_CLIP_TOP,
                    right = cutEdge,
                    bottom = BRAND_GEAR_CLIP_BOTTOM,
                ) {
                    drawPath(fullGear, color, alpha = layerAlpha)
                }
                if (rebuild > 0f) {
                    // Repaint the exact source C so its missing top/bottom terminals are reconstructed.
                    drawPath(halfGear, color, alpha = layerAlpha * rebuild)
                }
            }

            withTransform({ translate(0f, 8f) }) {
                drawMorphLayer(LogoNavy, 0.09f * alpha)
            }
            drawMorphLayer(LogoRed, alpha)
        }
    }
}

private fun DrawScope.drawShineBand(x: Float, width: Float, alpha: Float) {
    val band = Path().apply {
        moveTo(x, 20f)
        lineTo(x + width, 20f)
        lineTo(x + width - 90f, 560f)
        lineTo(x - 90f, 560f)
        close()
    }
    drawPath(band, Color.White, alpha = alpha)
}

/** Vẽ dần một Path đa contour theo tiến độ 0..1. */
private fun DrawScope.drawTraced(
    traced: Traced,
    progress: Float,
    color: Color,
    width: Float,
) {
    if (progress <= 0f) return
    val target = traced.total * progress.coerceIn(0f, 1f)
    val measure = AndroidPathMeasure(traced.androidPath, false)
    val destination = AndroidPath()
    var accumulated = 0f
    var contourIndex = 0
    do {
        val length = if (contourIndex < traced.lengths.size) traced.lengths[contourIndex] else measure.length
        when {
            target >= accumulated + length -> measure.getSegment(0f, length, destination, true)
            target > accumulated -> measure.getSegment(0f, target - accumulated, destination, true)
        }
        accumulated += length
        contourIndex++
    } while (measure.nextContour() && accumulated < target)

    drawPath(
        path = destination.asComposePath(),
        color = color,
        style = Stroke(width = width, cap = StrokeCap.Round, join = StrokeJoin.Round),
    )
}
