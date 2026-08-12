package com.ketoanapk.hr.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.Base64
import androidx.camera.core.CameraSelector
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathFillType
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.face.FaceDetection
import com.google.mlkit.vision.face.FaceDetectorOptions
import com.ketoanapk.hr.ui.theme.Success
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.abs
import kotlin.math.min
import android.os.SystemClock

// ── Tham số căn khung chụp CHÂN DUNG (đơn vị: tỉ lệ bề rộng mặt, ms) ──
private const val P_FACE_MIN = 0.22f          // mặt quá nhỏ (ở xa) → nhích lại gần
private const val P_FACE_TOO_CLOSE = 0.78f    // quá sát → lùi ra
private const val P_CENTER_TOL = 0.26f        // lệch tâm cho phép
private const val P_HOLD_MS = 1300L           // giữ yên trong khung ~1.3s rồi tự chụp
private const val P_POLL_MS = 40L
private const val P_FACE_STALE_MS = 350L
private const val P_MAX_FRAMES = 8
private const val PORTRAIT_OUT_H = 683        // chiều cao ảnh chân dung xuất (dọc 3:4)

private data class PortraitObs(val cx: Float, val cy: Float, val widthFrac: Float, val heightFrac: Float, val t: Long)

/**
 * Tham số cắt ảnh chân dung, lấy từ remote config (AppConfig) để chỉnh được từ xa mà khỏi build APK.
 * heightFactor: chiều cao khung = bấy nhiêu lần chiều cao mặt. verticalNudge: nhích khung LÊN theo
 * chiều cao mặt (dương = lấy nhiều đỉnh đầu). aspect: tỉ lệ ngang/dọc. minWidthFactor: bề rộng tối thiểu.
 */
data class PortraitCropParams(
    val heightFactor: Float = 1.85f,
    val verticalNudge: Float = 0.15f,
    val aspect: Float = 0.75f,
    val minWidthFactor: Float = 1.35f,
)

private class PortraitAimState {
    @Volatile var latest: PortraitObs? = null
    val collect = AtomicBoolean(false)
    private val frames = ArrayList<String>()
    fun addFrame(url: String) = synchronized(frames) {
        if (frames.size >= P_MAX_FRAMES) frames.removeAt(0)
        frames.add(url)
    }
    fun snapshot(): List<String> = synchronized(frames) { frames.toList() }
    fun clearFrames() = synchronized(frames) { frames.clear() }
}

/**
 * Lớp phủ TOÀN MÀN HÌNH chụp ảnh chân dung có hướng dẫn (dùng chung bộ căn khung ML Kit với chấm công/
 * đăng ký mặt). Đưa mặt vào khung bầu dục và giữ yên → tự chụp; sau đó xem trước để "Dùng ảnh" hoặc
 * "Chụp lại". Bấm Đóng/Back → [onCancel]. Chọn xong → [onCaptured] trả về data URL JPEG.
 */
@Composable
fun PortraitCaptureScan(
    onCaptured: (String) -> Unit,
    onCancel: () -> Unit,
    params: PortraitCropParams = PortraitCropParams(),
) {
    val context = LocalContext.current
    var preview by remember { mutableStateOf<String?>(null) }

    DisposableEffect(Unit) {
        val window = context.findActivity()?.window
        val controller = window?.let { WindowCompat.getInsetsController(it, it.decorView) }
        controller?.apply {
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            hide(WindowInsetsCompat.Type.statusBars())
        }
        onDispose { controller?.show(WindowInsetsCompat.Type.statusBars()) }
    }

    Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
        val shot = preview
        if (shot == null) {
            PortraitCaptureCamera(modifier = Modifier.fillMaxSize(), params = params, onShot = { preview = it })
        } else {
            PortraitPreview(
                dataUrl = shot,
                onRetake = { preview = null },
                onUse = { onCaptured(shot) },
            )
        }
        IconButton(
            onClick = onCancel,
            modifier = Modifier
                .align(Alignment.TopStart)
                .statusBarsPadding()
                .padding(8.dp),
        ) {
            Icon(Icons.Filled.Close, contentDescription = "Đóng", tint = Color.White)
        }
    }
}

/** Màn xem trước ảnh vừa chụp với hai lựa chọn: chụp lại hoặc dùng ảnh này. */
@Composable
private fun PortraitPreview(dataUrl: String, onRetake: () -> Unit, onUse: () -> Unit) {
    val bitmap = remember(dataUrl) { decodeDataUrl(dataUrl) }
    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            .navigationBarsPadding()
            .padding(horizontal = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Text(
            "Ảnh chân dung của bạn",
            color = Color.White,
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold,
        )
        Box(Modifier.height(18.dp))
        Box(
            modifier = Modifier
                .fillMaxWidth(0.72f)
                .aspectRatio(3f / 4f)
                .clip(RoundedCornerShape(18.dp))
                .background(Color(0xFF0B0F17)),
            contentAlignment = Alignment.Center,
        ) {
            if (bitmap != null) {
                Image(bitmap = bitmap.asImageBitmap(), contentDescription = "Ảnh chân dung", modifier = Modifier.fillMaxSize())
            }
        }
        Box(Modifier.height(24.dp))
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedButton(
                onClick = onRetake,
                modifier = Modifier.weight(1f).height(52.dp),
                shape = RoundedCornerShape(16.dp),
                colors = ButtonDefaults.outlinedButtonColors(contentColor = Color.White),
            ) {
                Icon(Icons.Filled.Refresh, contentDescription = null, modifier = Modifier.size(20.dp))
                Box(Modifier.width(8.dp))
                Text("Chụp lại", fontWeight = FontWeight.Bold)
            }
            Button(
                onClick = onUse,
                modifier = Modifier.weight(1f).height(52.dp),
                shape = RoundedCornerShape(16.dp),
            ) {
                Icon(Icons.Filled.Check, contentDescription = null, modifier = Modifier.size(20.dp))
                Box(Modifier.width(8.dp))
                Text("Dùng ảnh này", fontWeight = FontWeight.Bold)
            }
        }
    }
}

/**
 * CameraX (camera trước) + ML Kit: hướng dẫn đưa mặt vào khung, giữ yên ~1.3s rồi tự gom vài khung,
 * chọn khung mới nhất, cắt quanh mặt thành ảnh chân dung dọc 3:4 và trả về qua [onShot]. Có nút chụp tay.
 */
@androidx.annotation.OptIn(ExperimentalGetImage::class)
@Composable
private fun PortraitCaptureCamera(modifier: Modifier = Modifier, params: PortraitCropParams, onShot: (String) -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val previewView = remember { PreviewView(context).apply { scaleType = PreviewView.ScaleType.FILL_CENTER } }
    val executor = remember { Executors.newSingleThreadExecutor() }
    val mainExecutor = remember { ContextCompat.getMainExecutor(context) }
    val aim = remember { PortraitAimState() }
    val manualRequested = remember { AtomicBoolean(false) }
    val onShotNow by rememberUpdatedState(onShot)
    val detector = remember {
        FaceDetection.getClient(
            FaceDetectorOptions.Builder()
                .setPerformanceMode(FaceDetectorOptions.PERFORMANCE_MODE_FAST)
                .setMinFaceSize(0.15f)
                .build(),
        )
    }

    var holdProgress by remember { mutableStateOf(0f) }
    var hint by remember { mutableStateOf("Đưa khuôn mặt vào giữa khung") }
    var busy by remember { mutableStateOf(false) }
    val analysisRef = remember { java.util.concurrent.atomic.AtomicReference<ImageAnalysis?>() }

    DisposableEffect(Unit) {
        val future = ProcessCameraProvider.getInstance(context)
        future.addListener({
            val provider = future.get()
            val previewUse = Preview.Builder().build().also { it.setSurfaceProvider(previewView.surfaceProvider) }
            val analysis = ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
                .also { a ->
                    analysisRef.set(a)
                    a.setAnalyzer(executor) { image ->
                        try {
                            if (aim.collect.get()) runCatching { aim.addFrame(image.toJpegDataUrl(88)) }
                            val media = image.image
                            if (media == null) { image.close(); return@setAnalyzer }
                            val rotation = image.imageInfo.rotationDegrees
                            val iw = if (rotation == 90 || rotation == 270) image.height else image.width
                            val ih = if (rotation == 90 || rotation == 270) image.width else image.height
                            val input = InputImage.fromMediaImage(media, rotation)
                            detector.process(input)
                                .addOnSuccessListener(mainExecutor) { faces ->
                                    runCatching {
                                        val face = faces.maxByOrNull { it.boundingBox.width() * it.boundingBox.height() }
                                        aim.latest = if (face != null) {
                                            val bb = face.boundingBox
                                            PortraitObs(
                                                cx = (bb.exactCenterX() / iw).coerceIn(0f, 1f),
                                                cy = (bb.exactCenterY() / ih).coerceIn(0f, 1f),
                                                widthFrac = (bb.width().toFloat() / iw).coerceIn(0f, 1f),
                                                heightFrac = (bb.height().toFloat() / ih).coerceIn(0f, 1f),
                                                t = SystemClock.elapsedRealtime(),
                                            )
                                        } else null
                                    }
                                }
                                .addOnFailureListener(mainExecutor) { aim.latest = null }
                                .addOnCompleteListener(mainExecutor) { runCatching { image.close() } }
                        } catch (_: Throwable) {
                            runCatching { image.close() }
                        }
                    }
                }
            runCatching {
                provider.unbindAll()
                provider.bindToLifecycle(lifecycleOwner, CameraSelector.DEFAULT_FRONT_CAMERA, previewUse, analysis)
            }
        }, ContextCompat.getMainExecutor(context))

        onDispose {
            runCatching { analysisRef.getAndSet(null)?.clearAnalyzer() }
            runCatching { ProcessCameraProvider.getInstance(context).get().unbindAll() }
            runCatching { detector.close() }
            runCatching { executor.shutdown() }
        }
    }

    // Tăng độ sáng màn hình để soi mặt trong lúc chụp (khôi phục khi xong).
    DisposableEffect(Unit) {
        val window = context.findActivity()?.window
        val prev = window?.attributes?.screenBrightness
        if (window != null) window.attributes = window.attributes.apply { screenBrightness = 1f }
        onDispose {
            if (window != null && prev != null) window.attributes = window.attributes.apply { screenBrightness = prev }
        }
    }

    LaunchedEffect(Unit) {
        aim.collect.set(false); aim.clearFrames(); aim.latest = null
        var holding = false
        var holdStart = 0L

        suspend fun shoot(obs: PortraitObs?) {
            busy = true
            aim.collect.set(false)
            val frames = aim.snapshot()
            val frame = frames.lastOrNull()
            if (frame == null) {
                busy = false
                hint = "Chưa bắt được ảnh, thử lại"
                aim.clearFrames()
                return
            }
            val cropped = withContext(Dispatchers.Default) { cropPortraitDataUrl(frame, obs, params) }
            onShotNow(cropped)
        }

        while (isActive) {
            if (busy) { delay(P_POLL_MS); continue }
            val now = SystemClock.elapsedRealtime()
            val face = aim.latest?.takeIf { now - it.t < P_FACE_STALE_MS }

            // Nút chụp tay: gom nhanh vài khung rồi cắt (dùng khung mặt mới nhất nếu có).
            if (manualRequested.getAndSet(false)) {
                aim.clearFrames(); aim.collect.set(true)
                hint = "Đang chụp…"
                delay(220)
                shoot(aim.latest ?: face)
                continue
            }

            fun resetHold(msg: String) {
                holding = false; holdProgress = 0f
                aim.collect.set(false); aim.clearFrames()
                hint = msg
            }

            if (face == null) {
                resetHold("Đưa khuôn mặt vào giữa khung")
            } else {
                val centered = abs(face.cx - 0.5f) < P_CENTER_TOL && abs(face.cy - 0.5f) < P_CENTER_TOL
                val w = face.widthFrac
                when {
                    w > P_FACE_TOO_CLOSE -> resetHold("Quá gần — lùi ra xa một chút")
                    !centered -> resetHold("Đưa khuôn mặt vào giữa khung")
                    w < P_FACE_MIN -> resetHold("Nhích lại gần hơn một chút")
                    else -> {
                        if (!holding) { holding = true; holdStart = now; aim.clearFrames(); aim.collect.set(true) }
                        val held = now - holdStart
                        holdProgress = (held.toFloat() / P_HOLD_MS).coerceIn(0f, 1f)
                        hint = "Giữ yên…"
                        if (held >= P_HOLD_MS) {
                            holding = false; holdProgress = 0f
                            shoot(face)
                        }
                    }
                }
            }
            delay(P_POLL_MS)
        }
    }

    Box(modifier = modifier) {
        AndroidView(factory = { previewView }, modifier = Modifier.fillMaxSize())
        PortraitGuideOverlay(holdProgress = holdProgress)
        Text(
            "Chụp ảnh chân dung",
            color = Color.White,
            style = MaterialTheme.typography.titleSmall,
            fontWeight = FontWeight.Bold,
            modifier = Modifier
                .align(Alignment.TopCenter)
                .statusBarsPadding()
                .padding(top = 12.dp, start = 20.dp, end = 20.dp),
        )
        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .navigationBarsPadding()
                .fillMaxWidth()
                .padding(bottom = 24.dp, start = 20.dp, end = 20.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Text(
                hint,
                color = Color.White,
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.SemiBold,
                textAlign = TextAlign.Center,
            )
            OutlinedButton(
                onClick = { if (!busy) manualRequested.set(true) },
                enabled = !busy,
                shape = RoundedCornerShape(16.dp),
                colors = ButtonDefaults.outlinedButtonColors(contentColor = Color.White),
            ) {
                Icon(Icons.Filled.CameraAlt, contentDescription = null, modifier = Modifier.size(20.dp))
                Box(Modifier.width(8.dp))
                Text("Chụp ngay", fontWeight = FontWeight.Bold)
            }
        }
    }
}

/** Khung ngắm bầu dục + vòng tiến độ giữ khung (giống đăng ký khuôn mặt). */
@Composable
private fun PortraitGuideOverlay(holdProgress: Float) {
    val ovalFrac by animateFloatAsState(0.66f, tween(300), label = "poval")
    val ring = if (holdProgress > 0f) Success else Color.White.copy(alpha = 0.9f)
    Canvas(modifier = Modifier.fillMaxSize()) {
        val cw = size.width
        val ch = size.height
        val ovalW = cw * ovalFrac
        val ovalH = ovalW / 0.78f
        val left = (cw - ovalW) / 2f
        val top = (ch - ovalH) / 2f
        val topLeft = Offset(left, top)
        val ovalSize = Size(ovalW, ovalH)
        val mask = Path().apply {
            addRect(Rect(0f, 0f, cw, ch))
            addOval(Rect(left, top, left + ovalW, top + ovalH))
            fillType = PathFillType.EvenOdd
        }
        drawPath(mask, color = Color.Black.copy(alpha = 0.5f))
        drawOval(color = ring, topLeft = topLeft, size = ovalSize, style = Stroke(width = 6.dp.toPx()))
        if (holdProgress > 0f) {
            drawArc(
                color = Success,
                startAngle = -90f,
                sweepAngle = 360f * holdProgress,
                useCenter = false,
                topLeft = topLeft,
                size = ovalSize,
                style = Stroke(width = 9.dp.toPx(), cap = StrokeCap.Round),
            )
        }
    }
}

/** Giải mã data URL JPEG (data:image/...;base64,xxx) thành Bitmap; null nếu lỗi. */
internal fun decodeDataUrl(dataUrl: String): Bitmap? = runCatching {
    val comma = dataUrl.indexOf(',')
    val b64 = if (comma >= 0) dataUrl.substring(comma + 1) else dataUrl
    val bytes = Base64.decode(b64, Base64.DEFAULT)
    BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
}.getOrNull()

/**
 * Cắt một ảnh chân dung dọc (3:4) quanh khuôn mặt từ một khung đã chụp (upright). [obs] là vị trí mặt
 * (toạ độ chuẩn hoá) cùng hệ với khung; null thì cắt giữa. Ảnh xuất giữ chiều thật (không lật gương).
 */
private fun cropPortraitDataUrl(dataUrl: String, obs: PortraitObs?, params: PortraitCropParams): String {
    val src = decodeDataUrl(dataUrl) ?: return dataUrl
    val bw = src.width
    val bh = src.height
    val aspect = params.aspect.coerceIn(0.4f, 1f)
    val cwF: Float
    val chF: Float
    val centerX: Float
    val centerY: Float
    if (obs != null && obs.widthFrac > 0f && obs.heightFrac > 0f) {
        // Ảnh thẻ "từ cổ hắt lên": chừa trên đỉnh đầu, cắt ngay dưới cằm (KHÔNG lấy vai).
        val fw = (obs.widthFrac * bw).coerceAtLeast(1f)
        val fh = (obs.heightFrac * bh).coerceAtLeast(1f)
        var ch = fh * params.heightFactor   // ~ đỉnh đầu → dưới cằm một chút
        var cwd = ch * aspect
        if (cwd < fw * params.minWidthFactor) { cwd = fw * params.minWidthFactor; ch = cwd / aspect } // đủ rộng cho mặt to-ngắn
        chF = ch; cwF = cwd
        centerX = obs.cx * bw
        centerY = obs.cy * bh - fh * params.verticalNudge   // nhích LÊN để lấy đỉnh đầu, bỏ vai
    } else {
        // Không có mặt → cắt giữa theo tỉ lệ cấu hình.
        if (bw.toFloat() / bh > aspect) { chF = bh.toFloat(); cwF = bh * aspect }
        else { cwF = bw.toFloat(); chF = bw / aspect }
        centerX = bw / 2f
        centerY = bh / 2f
    }
    // Co để nằm gọn trong ảnh, rồi kẹp vị trí trong biên.
    val scale = min(1f, min(bw / cwF, bh / chF))
    val cropW = (cwF * scale).toInt().coerceIn(1, bw)
    val cropH = (chF * scale).toInt().coerceIn(1, bh)
    val leftPx = (centerX - cropW / 2f).toInt().coerceIn(0, bw - cropW)
    val topPx = (centerY - cropH / 2f).toInt().coerceIn(0, bh - cropH)
    val cropped = Bitmap.createBitmap(src, leftPx, topPx, cropW, cropH)
    // Thu nhỏ về tối đa PORTRAIT_OUT_H chiều cao (giữ tỉ lệ) cho gọn nhẹ.
    val outH = min(PORTRAIT_OUT_H, cropped.height)
    val outW = (outH * cropped.width.toFloat() / cropped.height).toInt().coerceAtLeast(1)
    val scaled = if (outH != cropped.height) Bitmap.createScaledBitmap(cropped, outW, outH, true) else cropped
    val out = ByteArrayOutputStream()
    scaled.compress(Bitmap.CompressFormat.JPEG, 88, out)
    return "data:image/jpeg;base64," + Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP)
}
