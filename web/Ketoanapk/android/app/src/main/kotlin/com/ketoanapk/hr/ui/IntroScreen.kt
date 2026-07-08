package com.ketoanapk.hr.ui

import android.graphics.Path as AndroidPath
import android.graphics.PathMeasure as AndroidPathMeasure
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.animation.core.Easing
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
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
import kotlinx.coroutines.launch
import kotlin.math.PI
import kotlin.math.min
import kotlin.math.sin

// Màu lấy đúng theo file SVG logo.
private val LogoRed = Color(0xFFEC0200)
private val LogoYellow = Color(0xFFFCDE00)
private val LogoNavy = Color(0xFF21305B)
private val IntroBg = Color(0xFFF4F4F2)

// Không gian thiết kế = viewBox của SVG.
private const val DESIGN_W = 1000f
private const val DESIGN_H = 675f

// Mốc thời gian (ms) của từng pha.
private const val T_COG_SPIN_START = 100f
private const val T_COG_SPIN_DUR = 1500f
private const val T_COG_FILL_START = 100f
private const val T_COG_FILL_DUR = 1200f
private const val T_CRE_DRAW_START = 1000f
private const val T_CRE_DRAW_DUR = 800f
private const val T_CRE_FILL_START = 1650f
private const val T_CRE_FILL_DUR = 400f
private const val T_P_DRAW_START = 1450f
private const val T_P_DRAW_DUR = 900f
private const val T_P_FILL_START = 2100f
private const val T_P_FILL_DUR = 500f
private const val T_TRI_DRAW_START = 1250f
private const val T_TRI_DRAW_DUR = 1100f
private const val T_TRIFILL_START = 2050f
private const val T_TRIFILL_DUR = 800f
private const val T_BANNER_START = 2450f
private const val T_BANNER_DUR = 700f
private const val T_TEXT_START = 2700f
private const val T_TEXT_DUR = 950f
private const val T_SHINE_START = 3600f
private const val T_SHINE_DUR = 800f
private const val T_BOUNCE_START = 3650f
private const val T_BOUNCE_DUR = 700f
private const val T_TOTAL = 4500f

private val SoftSettle: Easing = CubicBezierEasing(0.16f, 1f, 0.3f, 1f)

private fun seg(elapsed: Float, start: Float, dur: Float, easing: Easing = LinearEasing): Float {
    val t = ((elapsed - start) / dur).coerceIn(0f, 1f)
    return easing.transform(t)
}

private fun parse(d: String, evenOdd: Boolean = false): Path {
    val p = PathParser().parsePathString(d).toPath()
    if (evenOdd) p.fillType = PathFillType.EvenOdd
    return p
}

/** Đo trước độ dài từng contour để vẽ dần (trace) mượt qua các nét rời nhau. */
private class Traced(composePath: Path) {
    val androidPath: AndroidPath = composePath.asAndroidPath()
    val lengths: FloatArray
    val total: Float

    init {
        val pm = AndroidPathMeasure(androidPath, false)
        val ls = ArrayList<Float>()
        do { ls.add(pm.length) } while (pm.nextContour())
        lengths = ls.toFloatArray()
        total = lengths.sum()
    }
}

/**
 * Màn intro mở app: logo Inox Cường Phát dựng từ chính path SVG thật.
 * Bánh răng xoay về đúng chỗ, chữ C / chữ P / khung tam giác được "vẽ dần" từng nét,
 * dải chữ hiện ra, kết bằng vệt sáng kim loại quét ngang. Chạm để bỏ qua.
 */
@Composable
fun IntroOverlay(onFinished: () -> Unit) {
    val scope = rememberCoroutineScope()
    val elapsed = remember { Animatable(0f) }
    val containerAlpha = remember { Animatable(1f) }
    var ended by remember { mutableStateOf(false) }

    fun end() {
        if (ended) return
        ended = true
        scope.launch {
            containerAlpha.animateTo(0f, tween(350))
            onFinished()
        }
    }

    LaunchedEffect(Unit) {
        elapsed.animateTo(T_TOTAL, tween(T_TOTAL.toInt(), easing = LinearEasing))
        end()
    }

    val triFill = remember { parse(IntroLogoPaths.TRI_FILL) }
    val cog = remember { parse(IntroLogoPaths.COG, evenOdd = true) }
    val cogPivot = remember { cog.getBounds().center }
    val crescent = remember { parse(IntroLogoPaths.CRESCENT) }
    val crescentTraced = remember { Traced(crescent) }
    val letterP = remember { parse(IntroLogoPaths.LETTER_P, evenOdd = true) }
    val letterPTraced = remember { Traced(letterP) }
    val banner = remember { parse(IntroLogoPaths.BANNER) }
    val text = remember { parse(IntroLogoPaths.TEXT, evenOdd = true) }

    // Đường vẽ khung tam giác: đi qua giữa viền đỏ (giữa tam giác ngoài và trong của SVG).
    val triStroke = remember {
        Path().apply {
            moveTo(502.75f, 17.5f)
            lineTo(972f, 560f)
            lineTo(26.5f, 560f)
            close()
        }
    }
    val triStrokeTraced = remember { Traced(triStroke) }

    val interaction = remember { MutableInteractionSource() }

    Canvas(
        modifier = Modifier
            .fillMaxSize()
            .alpha(containerAlpha.value)
            .background(IntroBg)
            .clickable(interactionSource = interaction, indication = null) {
                scope.launch { elapsed.snapTo(T_TOTAL) }
                end()
            },
    ) {
        val e = elapsed.value
        val fit = min(size.width / DESIGN_W, size.height / DESIGN_H) * 0.9f
        val offsetX = (size.width - DESIGN_W * fit) / 2f
        val offsetY = (size.height - DESIGN_H * fit) / 2f

        val bounce = seg(e, T_BOUNCE_START, T_BOUNCE_DUR)
        val bounceScale = 1f + 0.04f * sin((PI * bounce).toFloat())

        withTransform({
            translate(offsetX, offsetY)
            scale(fit, fit, pivot = Offset.Zero)
            scale(bounceScale, bounceScale, pivot = Offset(500f, 300f))
        }) {
            // 1. Nền vàng tam giác loang vào.
            val triFillA = seg(e, T_TRIFILL_START, T_TRIFILL_DUR, FastOutSlowInEasing)
            if (triFillA > 0f) drawPath(triFill, LogoYellow, alpha = triFillA)

            // 2. Viền đỏ tam giác vẽ lan ra.
            drawTraced(triStrokeTraced, seg(e, T_TRI_DRAW_START, T_TRI_DRAW_DUR, FastOutSlowInEasing), LogoRed, 22f)

            // 3. Bánh răng: xoay về đúng vị trí + hiện dần (ý tưởng gốc của logo).
            val cogSpin = (1f - seg(e, T_COG_SPIN_START, T_COG_SPIN_DUR, SoftSettle)) * 720f
            val cogFillA = seg(e, T_COG_FILL_START, T_COG_FILL_DUR)
            if (cogFillA > 0f) {
                withTransform({ rotate(cogSpin, pivot = cogPivot) }) {
                    drawPath(cog, LogoRed, alpha = cogFillA)
                }
            }

            // 4. Chữ C đỏ: vẽ nét rồi đổ màu.
            drawTraced(crescentTraced, seg(e, T_CRE_DRAW_START, T_CRE_DRAW_DUR, LinearOutSlowInEasing), LogoRed, 3f)
            val creFillA = seg(e, T_CRE_FILL_START, T_CRE_FILL_DUR)
            if (creFillA > 0f) drawPath(crescent, LogoRed, alpha = creFillA)

            // 5. Chữ P navy: vẽ nét rồi đổ màu.
            drawTraced(letterPTraced, seg(e, T_P_DRAW_START, T_P_DRAW_DUR, LinearOutSlowInEasing), LogoNavy, 3f)
            val pFillA = seg(e, T_P_FILL_START, T_P_FILL_DUR)
            if (pFillA > 0f) drawPath(letterP, LogoNavy, alpha = pFillA)

            // 6. Dải băng trượt lên + hiện.
            val bannerP = seg(e, T_BANNER_START, T_BANNER_DUR, SoftSettle)
            if (bannerP > 0f) {
                withTransform({ translate(0f, (1f - bannerP) * 24f) }) {
                    drawPath(banner, LogoYellow, alpha = bannerP)
                }
            }

            // 7. Chữ "INOX CUONG PHAT" hiện dần từ trái sang phải (như đang viết).
            val textP = seg(e, T_TEXT_START, T_TEXT_DUR)
            if (textP > 0f) {
                clipRect(left = 0f, top = 575f, right = DESIGN_W * textP, bottom = DESIGN_H) {
                    drawPath(text, LogoRed)
                }
            }

            // 8. Vệt sáng kim loại quét ngang, chỉ trong tam giác.
            val shineP = seg(e, T_SHINE_START, T_SHINE_DUR)
            if (shineP > 0f && shineP < 1f) {
                val x = -320f + shineP * 1520f
                val glow = sin((PI * shineP).toFloat())
                clipPath(triFill) {
                    val bar = Path().apply {
                        moveTo(x, 20f); lineTo(x + 180f, 20f)
                        lineTo(x + 90f, 560f); lineTo(x - 90f, 560f); close()
                    }
                    drawPath(bar, Color.White, alpha = 0.5f * glow)
                }
            }
        }
    }
}

/** Vẽ dần một Path đa contour theo tiến độ 0..1 (hiệu ứng "đang vẽ"). */
private fun DrawScope.drawTraced(t: Traced, progress: Float, color: Color, width: Float) {
    if (progress <= 0f) return
    val target = t.total * progress.coerceIn(0f, 1f)
    val pm = AndroidPathMeasure(t.androidPath, false)
    val dst = AndroidPath()
    var acc = 0f
    var i = 0
    do {
        val len = if (i < t.lengths.size) t.lengths[i] else pm.length
        when {
            target >= acc + len -> pm.getSegment(0f, len, dst, true)
            target > acc -> pm.getSegment(0f, target - acc, dst, true)
        }
        acc += len
        i++
    } while (pm.nextContour() && acc < target)
    drawPath(dst.asComposePath(), color, style = Stroke(width = width, cap = StrokeCap.Round, join = StrokeJoin.Round))
}
