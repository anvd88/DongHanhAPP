package com.ketoanapk.hr.ui

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.Login
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Fingerprint
import androidx.compose.material.icons.filled.LockReset
import androidx.compose.material.icons.filled.Shield
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
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
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathFillType
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.face.FaceDetection
import com.google.mlkit.vision.face.FaceDetectorOptions
import com.ketoanapk.hr.data.FaceEnrollPose
import com.ketoanapk.hr.ui.theme.Danger
import com.ketoanapk.hr.ui.theme.Success
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.abs
import android.os.SystemClock

// ── Tham số quét ĐĂNG KÝ nhiều góc (đơn vị: tỉ lệ bề rộng mặt, độ Euler, ms) ──
private const val E_FACE_MIN = 0.24f          // mặt quá nhỏ (ở xa) → nhích lại gần
private const val E_FACE_TOO_CLOSE = 0.75f    // quá sát → lùi ra (bảo vệ mắt)
private const val E_CENTER_TOL_X = 0.24f
private const val E_CENTER_TOL_Y = 0.24f
private const val E_FRONT_YAW_MAX = 12f       // "nhìn thẳng": |yaw| ≤ 12°
private const val E_SIDE_YAW_MIN = 15f        // "nghiêng": cần quay ít nhất 15°
private const val E_SIDE_YAW_MAX = 42f        // nhưng đừng quay quá 42° (mất mặt)
private const val E_HOLD_MS = 1100L           // giữ mỗi góc ~1.1s để gom khung
private const val E_POLL_MS = 40L
private const val E_FACE_STALE_MS = 350L
private const val E_TIMEOUT_MS = 70_000L      // tổng thời gian tối đa cho cả 3 góc
private const val E_MAX_FRAMES = 10

private enum class PoseKind { Front, Side }
private data class EnrollPoseSpec(val label: String, val title: String, val kind: PoseKind)

// Trình tự 3 góc: chính diện (mẫu chuẩn) + nghiêng 2 bên ngược nhau (mẫu bền hơn khi ánh sáng/biểu cảm đổi).
private val ENROLL_POSES = listOf(
    EnrollPoseSpec("front", "Nhìn thẳng vào camera", PoseKind.Front),
    EnrollPoseSpec("side1", "Quay đầu nhẹ sang một bên", PoseKind.Side),
    EnrollPoseSpec("side2", "Quay sang bên còn lại", PoseKind.Side),
)

/**
 * Màn hình ĐĂNG KÝ KHUÔN MẶT (mở từ Cài đặt): giải thích khuôn mặt dùng để làm gì + nút bắt đầu đăng ký.
 * Sau khi đăng ký thành công, nút bị làm mờ (mỗi tài khoản chỉ đăng ký một lần). Bước quét camera chạy ở
 * lớp phủ toàn màn hình (hoisted trong HrShell) nên màn này chỉ hiện phần chú thích + trạng thái/kết quả.
 */
@Composable
fun FaceEnrollScreen(vm: HrViewModel, onBack: () -> Unit) {
    val context = LocalContext.current
    LaunchedEffect(Unit) { vm.loadFaceStatus() }
    val registered = vm.faceRegistered == true
    val enroll = vm.faceEnroll
    val back: () -> Unit = { vm.resetFaceEnroll(); onBack() }

    // Xin quyền camera trước khi mở bước quét (giống luồng chấm công). Cấp xong thì bắt đầu ngay.
    val cameraPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) vm.startFaceEnroll()
    }
    val onStart: () -> Unit = {
        if (ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
            vm.startFaceEnroll()
        } else {
            cameraPermission.launch(Manifest.permission.CAMERA)
        }
    }

    Column(modifier = Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 6.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = back) { Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Quay lại") }
            Text("Đăng ký khuôn mặt", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }

        LazyColumn(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth(),
            contentPadding = PaddingValues(start = 14.dp, end = 14.dp, bottom = 24.dp, top = 4.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            item { EnrollHero(registered) }

            item {
                HrCard {
                    Text("Khuôn mặt của bạn dùng để làm gì?", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
                    Spacer(Modifier.height(10.dp))
                    PurposeRow(Icons.Filled.Face, "Chấm công bằng khuôn mặt", "Vào/Ra nhanh, không cần thẻ hay mật khẩu.")
                    Spacer(Modifier.height(10.dp))
                    PurposeRow(Icons.AutoMirrored.Filled.Login, "Đăng nhập bằng khuôn mặt", "Mở ứng dụng bằng khuôn mặt của chính bạn.")
                    Spacer(Modifier.height(10.dp))
                    PurposeRow(Icons.Filled.LockReset, "Khôi phục mật khẩu", "Đặt lại mật khẩu khi quên, bằng khuôn mặt.")
                }
            }

            item {
                HrCard {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Filled.Shield, contentDescription = null, tint = Success, modifier = Modifier.size(22.dp))
                        Spacer(Modifier.width(10.dp))
                        Text(
                            "Hệ thống chỉ lưu VECTOR đặc trưng đã mã hoá, KHÔNG lưu ảnh khuôn mặt của bạn.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }

            item {
                HrCard {
                    Text("Cách quét (3 góc)", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
                    Spacer(Modifier.height(10.dp))
                    StepRow(1, "Nhìn thẳng vào camera")
                    StepRow(2, "Quay đầu nhẹ sang một bên")
                    StepRow(3, "Quay sang bên còn lại")
                    Spacer(Modifier.height(6.dp))
                    Text(
                        "Giữ máy ngang tầm mắt ở nơi đủ sáng. Mỗi tài khoản chỉ đăng ký một lần.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }

            // Trạng thái/kết quả của lần đăng ký hiện tại.
            when (enroll) {
                is FaceEnrollCapture.Submitting -> item {
                    HrCard {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            CircularProgressIndicator(Modifier.size(20.dp), MaterialTheme.colorScheme.primary, 2.dp)
                            Spacer(Modifier.width(12.dp))
                            Text("Đang lưu mẫu khuôn mặt…", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
                        }
                    }
                }
                is FaceEnrollCapture.Done -> item { EnrollResultCard(enroll.success, enroll.message) }
                else -> Unit
            }
        }

        // Nút hành động luôn hiển thị ở đáy màn hình (không cần cuộn xuống mới thấy).
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .background(MaterialTheme.colorScheme.surface)
                .navigationBarsPadding()
                .padding(horizontal = 14.dp, vertical = 12.dp),
        ) {
            EnrollActionButton(
                registered = registered,
                submitting = enroll is FaceEnrollCapture.Submitting,
                failed = enroll is FaceEnrollCapture.Done && !(enroll).success,
                onStart = onStart,
            )
        }
    }
}

@Composable
private fun EnrollHero(registered: Boolean) {
    val accent = MaterialTheme.colorScheme.primary
    HrCard {
        Column(modifier = Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Box(
                modifier = Modifier
                    .size(76.dp)
                    .clip(CircleShape)
                    .background((if (registered) Success else accent).copy(alpha = 0.16f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    if (registered) Icons.Filled.CheckCircle else Icons.Filled.Fingerprint,
                    contentDescription = null,
                    tint = if (registered) Success else accent,
                    modifier = Modifier.size(42.dp),
                )
            }
            Text(
                if (registered) "Đã đăng ký khuôn mặt" else "Đăng ký khuôn mặt của bạn",
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurface,
                fontWeight = FontWeight.Bold,
                textAlign = TextAlign.Center,
            )
            Text(
                if (registered) "Tài khoản của bạn đã có mẫu khuôn mặt. Mỗi tài khoản chỉ đăng ký một lần."
                else "Quét khuôn mặt một lần để dùng cho chấm công, đăng nhập và khôi phục mật khẩu.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
        }
    }
}

@Composable
private fun PurposeRow(icon: ImageVector, title: String, subtitle: String) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(
            modifier = Modifier
                .size(38.dp)
                .clip(RoundedCornerShape(11.dp))
                .background(MaterialTheme.colorScheme.primaryContainer),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp))
        }
        Spacer(Modifier.width(12.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface)
            Text(subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun StepRow(index: Int, text: String) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 4.dp)) {
        Box(
            modifier = Modifier
                .size(26.dp)
                .clip(CircleShape)
                .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.14f)),
            contentAlignment = Alignment.Center,
        ) {
            Text("$index", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.Bold)
        }
        Spacer(Modifier.width(12.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
    }
}

@Composable
private fun EnrollResultCard(success: Boolean, message: String) {
    val color = if (success) Success else Danger
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(
                if (success) Icons.Filled.CheckCircle else Icons.Filled.ErrorOutline,
                contentDescription = null,
                tint = color,
                modifier = Modifier.size(24.dp),
            )
            Spacer(Modifier.width(12.dp))
            Text(
                message.ifBlank { if (success) "Đăng ký khuôn mặt thành công." else "Đăng ký chưa thành công." },
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurface,
            )
        }
    }
}

@Composable
private fun EnrollActionButton(registered: Boolean, submitting: Boolean, failed: Boolean, onStart: () -> Unit) {
    if (registered) {
        // Đã đăng ký → nút bị LÀM MỜ (disabled) để tránh đăng ký lần hai.
        Button(
            onClick = {},
            enabled = false,
            modifier = Modifier
                .fillMaxWidth()
                .height(52.dp),
            shape = RoundedCornerShape(16.dp),
        ) {
            Icon(Icons.Filled.CheckCircle, contentDescription = null, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(8.dp))
            Text("Đã đăng ký khuôn mặt", fontWeight = FontWeight.Bold)
        }
    } else {
        Button(
            onClick = onStart,
            enabled = !submitting,
            modifier = Modifier
                .fillMaxWidth()
                .height(52.dp),
            shape = RoundedCornerShape(16.dp),
        ) {
            if (submitting) {
                CircularProgressIndicator(Modifier.size(20.dp), MaterialTheme.colorScheme.onPrimary, 2.dp)
            } else {
                Icon(Icons.Filled.Face, contentDescription = null, modifier = Modifier.size(20.dp))
                Spacer(Modifier.width(8.dp))
                Text(if (failed) "Đăng ký lại" else "Bắt đầu đăng ký", fontWeight = FontWeight.Bold)
            }
        }
    }
}

/**
 * Lớp phủ TOÀN MÀN HÌNH cho bước quét đăng ký (ẩn thanh hệ thống + nút Đóng). Được gọi ở tầng ngoài
 * Scaffold (xem HrShell). Khi quét đủ 3 góc → [onCompleted]; bấm Đóng / hết giờ → [onCancel].
 */
@Composable
fun FaceEnrollCameraScan(onCompleted: (List<FaceEnrollPose>) -> Unit, onCancel: () -> Unit) {
    val context = LocalContext.current
    DisposableEffect(Unit) {
        val window = context.findActivity()?.window
        val controller = window?.let { WindowCompat.getInsetsController(it, it.decorView) }
        controller?.apply {
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            hide(WindowInsetsCompat.Type.systemBars())
        }
        onDispose { controller?.show(WindowInsetsCompat.Type.systemBars()) }
    }
    Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
        FaceEnrollCamera(modifier = Modifier.fillMaxSize(), onCompleted = onCompleted, onCancel = onCancel)
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

// Quan sát khuôn mặt cho đăng ký (kèm yaw để căn góc nghiêng).
private data class EnrollObs(val cx: Float, val cy: Float, val widthFrac: Float, val yawDeg: Float, val t: Long)

private class EnrollAimState {
    @Volatile var latest: EnrollObs? = null
    val collect = AtomicBoolean(false)
    private val frames = ArrayList<String>()
    fun addFrame(url: String) = synchronized(frames) {
        if (frames.size >= E_MAX_FRAMES) frames.removeAt(0)
        frames.add(url)
    }
    fun snapshot(): List<String> = synchronized(frames) { frames.toList() }
    fun clearFrames() = synchronized(frames) { frames.clear() }
}

/**
 * CameraX (camera trước) + ML Kit: quét lần lượt 3 góc (chính diện → nghiêng trái/phải bất kỳ, bên
 * thứ hai phải ngược bên thứ nhất). Mỗi góc giữ ~1.1s để gom khung, xong tự chuyển góc kế. Đủ 3 góc →
 * trả loạt ảnh theo từng góc qua [onCompleted]. Hết giờ → [onCancel].
 */
@androidx.annotation.OptIn(ExperimentalGetImage::class)
@Composable
private fun FaceEnrollCamera(
    modifier: Modifier = Modifier,
    onCompleted: (List<FaceEnrollPose>) -> Unit,
    onCancel: () -> Unit,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val previewView = remember { PreviewView(context).apply { scaleType = PreviewView.ScaleType.FILL_CENTER } }
    val executor = remember { Executors.newSingleThreadExecutor() }
    val mainExecutor = remember { ContextCompat.getMainExecutor(context) }
    val aim = remember { EnrollAimState() }
    val onCompletedNow by rememberUpdatedState(onCompleted)
    val onCancelNow by rememberUpdatedState(onCancel)
    val detector = remember {
        FaceDetection.getClient(
            FaceDetectorOptions.Builder()
                .setPerformanceMode(FaceDetectorOptions.PERFORMANCE_MODE_FAST)
                .setMinFaceSize(0.15f)
                .build(),
        )
    }

    var poseIndex by remember { mutableStateOf(0) }
    var holdProgress by remember { mutableStateOf(0f) }
    var hint by remember { mutableStateOf(ENROLL_POSES[0].title) }
    val analysisRef = remember { java.util.concurrent.atomic.AtomicReference<ImageAnalysis?>() }

    DisposableEffect(Unit) {
        val future = ProcessCameraProvider.getInstance(context)
        future.addListener({
            val provider = future.get()
            val preview = Preview.Builder().build().also { it.setSurfaceProvider(previewView.surfaceProvider) }
            val analysis = ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
                .also { a ->
                    analysisRef.set(a)
                    a.setAnalyzer(executor) { image ->
                        try {
                            if (aim.collect.get()) runCatching { aim.addFrame(image.toJpegDataUrl()) }
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
                                            EnrollObs(
                                                cx = (bb.exactCenterX() / iw).coerceIn(0f, 1f),
                                                cy = (bb.exactCenterY() / ih).coerceIn(0f, 1f),
                                                widthFrac = (bb.width().toFloat() / iw).coerceIn(0f, 1f),
                                                yawDeg = face.headEulerAngleY,
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
                provider.bindToLifecycle(lifecycleOwner, CameraSelector.DEFAULT_FRONT_CAMERA, preview, analysis)
            }
        }, ContextCompat.getMainExecutor(context))

        onDispose {
            runCatching { analysisRef.getAndSet(null)?.clearAnalyzer() }
            runCatching { ProcessCameraProvider.getInstance(context).get().unbindAll() }
            runCatching { detector.close() }
            runCatching { executor.shutdown() }
        }
    }

    // Tăng độ sáng màn hình để soi sáng khuôn mặt trong lúc quét (khôi phục khi xong).
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
        poseIndex = 0; holdProgress = 0f; hint = ENROLL_POSES[0].title
        val collected = ArrayList<FaceEnrollPose>()
        var firstSideSign = 0
        var holding = false
        var holdStart = 0L
        val startedAt = SystemClock.elapsedRealtime()

        while (isActive) {
            val now = SystemClock.elapsedRealtime()
            val spec = ENROLL_POSES[poseIndex]
            val face = aim.latest?.takeIf { now - it.t < E_FACE_STALE_MS }

            fun resetHold(msg: String) {
                holding = false; holdProgress = 0f
                aim.collect.set(false); aim.clearFrames()
                hint = msg
            }

            if (face == null) {
                resetHold("Đưa khuôn mặt vào giữa khung")
            } else {
                val centered = abs(face.cx - 0.5f) < E_CENTER_TOL_X && abs(face.cy - 0.5f) < E_CENTER_TOL_Y
                val w = face.widthFrac
                val yaw = face.yawDeg
                val mag = abs(yaw)
                val sign = if (yaw >= 0f) 1 else -1

                val poseGood: Boolean
                val poseHint: String
                when (spec.kind) {
                    PoseKind.Front -> {
                        poseGood = mag <= E_FRONT_YAW_MAX
                        poseHint = "Nhìn thẳng vào camera"
                    }
                    PoseKind.Side -> {
                        val inBand = mag in E_SIDE_YAW_MIN..E_SIDE_YAW_MAX
                        if (firstSideSign == 0) {
                            poseGood = inBand
                            poseHint = "Quay đầu nhẹ sang một bên"
                        } else {
                            poseGood = inBand && sign == -firstSideSign
                            poseHint = "Quay sang bên còn lại"
                        }
                    }
                }

                when {
                    w > E_FACE_TOO_CLOSE -> resetHold("Quá gần — lùi ra xa một chút")
                    !centered -> resetHold("Đưa khuôn mặt vào giữa khung")
                    w < E_FACE_MIN -> resetHold("Nhích lại gần hơn một chút")
                    !poseGood -> resetHold(poseHint)
                    else -> {
                        if (!holding) { holding = true; holdStart = now; aim.clearFrames(); aim.collect.set(true) }
                        val held = now - holdStart
                        holdProgress = (held.toFloat() / E_HOLD_MS).coerceIn(0f, 1f)
                        hint = "Giữ yên…"
                        if (held >= E_HOLD_MS) {
                            val frames = aim.snapshot()
                            aim.collect.set(false); holding = false; holdProgress = 0f
                            if (frames.isNotEmpty()) {
                                collected.add(FaceEnrollPose(spec.label, frames))
                                if (spec.kind == PoseKind.Side && firstSideSign == 0) firstSideSign = sign
                                poseIndex++
                                if (poseIndex >= ENROLL_POSES.size) {
                                    onCompletedNow(collected)
                                    return@LaunchedEffect
                                }
                                hint = ENROLL_POSES[poseIndex].title
                            } else {
                                hint = "Chưa bắt được ảnh, thử lại"
                            }
                            aim.clearFrames()
                        }
                    }
                }
            }

            if (now - startedAt > E_TIMEOUT_MS) {
                aim.collect.set(false)
                onCancelNow()
                return@LaunchedEffect
            }
            delay(E_POLL_MS)
        }
    }

    Box(modifier = modifier) {
        AndroidView(factory = { previewView }, modifier = Modifier.fillMaxSize())
        EnrollGuideOverlay(holdProgress = holdProgress, poseIndex = poseIndex, total = ENROLL_POSES.size)
        Column(
            modifier = Modifier
                .align(Alignment.TopCenter)
                .statusBarsPadding()
                .padding(top = 12.dp, start = 20.dp, end = 20.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(
                "Đăng ký khuôn mặt · Góc ${(poseIndex + 1).coerceAtMost(ENROLL_POSES.size)}/${ENROLL_POSES.size}",
                color = Color.White,
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.Bold,
            )
        }
        Text(
            hint,
            color = Color.White,
            style = MaterialTheme.typography.titleSmall,
            fontWeight = FontWeight.SemiBold,
            textAlign = TextAlign.Center,
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .navigationBarsPadding()
                .padding(bottom = 24.dp, start = 20.dp, end = 20.dp),
        )
    }
}

/** Khung ngắm bầu dục + vòng tiến độ giữ góc + các chấm đếm số góc đã xong. */
@Composable
private fun EnrollGuideOverlay(holdProgress: Float, poseIndex: Int, total: Int) {
    val ovalFrac by animateFloatAsState(0.66f, tween(300), label = "eoval")
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
        // Tối nhẹ vùng ngoài khung để làm nổi khuôn mặt.
        val mask = Path().apply {
            addRect(androidx.compose.ui.geometry.Rect(0f, 0f, cw, ch))
            addOval(androidx.compose.ui.geometry.Rect(left, top, left + ovalW, top + ovalH))
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
        // Chấm đếm số góc đã hoàn thành, đặt dưới khung.
        val dotR = 6.dp.toPx()
        val gap = 22.dp.toPx()
        val totalW = (total - 1) * gap
        val startX = cw / 2f - totalW / 2f
        val y = top + ovalH + 26.dp.toPx()
        for (i in 0 until total) {
            drawOval(
                color = if (i < poseIndex) Success else Color.White.copy(alpha = 0.4f),
                topLeft = Offset(startX + i * gap - dotR, y - dotR),
                size = Size(dotR * 2, dotR * 2),
            )
        }
    }
}
