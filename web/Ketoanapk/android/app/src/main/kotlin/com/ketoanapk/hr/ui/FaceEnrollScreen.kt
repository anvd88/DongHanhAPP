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
import androidx.compose.ui.graphics.Color
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
private const val E_FRONT_PITCH_MAX = 12f     // tránh lấy góc ngẩng/cúi làm mẫu chính diện
private const val E_SIDE_YAW_MIN = 16f        // nghiêng đủ để có dữ liệu khác chính diện
private const val E_SIDE_YAW_MAX = 38f        // nhưng không quay quá mạnh làm mất nửa mặt
private const val E_PITCH_DELTA = 11f         // độ ngẩng/cúi tối thiểu so với góc chính diện của máy
private const val E_ROLL_MAX = 15f            // không nghiêng đầu về vai
private const val E_EYE_OPEN_MIN = 0.40f      // loại khung nhắm/lim dim mắt
private const val E_HOLD_MS = 1500L           // đủ thời gian gom nhiều khung nét cho mỗi góc
private const val E_POLL_MS = 40L
private const val E_FACE_STALE_MS = 350L
private const val E_TIMEOUT_MS = 110_000L     // tổng thời gian tối đa cho đủ 5 góc
private const val E_MAX_FRAMES = 6            // 5 góc × 6 = 30, dưới trần backend 36 ảnh
private const val E_MIN_FRAMES = 4
private const val E_CAPTURE_GAP_MS = 170L     // tránh JPEG dồn dập; lấy khung trải đều trong 1,5 giây

private enum class PoseKind { Front, Side, Up, Down }
private data class EnrollPoseSpec(val label: String, val title: String, val kind: PoseKind)

// Năm góc phủ đủ biến thiên thường gặp khi chấm công: thẳng, hai bên, ngẩng và cúi.
private val ENROLL_POSES = listOf(
    EnrollPoseSpec("front", "Nhìn thẳng vào camera", PoseKind.Front),
    EnrollPoseSpec("side1", "Quay đầu nhẹ sang một bên", PoseKind.Side),
    EnrollPoseSpec("side2", "Quay sang bên còn lại", PoseKind.Side),
    EnrollPoseSpec("up", "Ngẩng mặt nhẹ lên", PoseKind.Up),
    EnrollPoseSpec("down", "Cúi mặt nhẹ xuống", PoseKind.Down),
)

/**
 * Màn hình ĐĂNG KÝ KHUÔN MẶT (mở từ Cài đặt): giải thích khuôn mặt dùng để làm gì + nút bắt đầu đăng ký.
 * Sau khi đăng ký thành công, nút bị làm mờ (mỗi tài khoản chỉ đăng ký một lần). Bước quét camera chạy ở
 * lớp phủ toàn màn hình (hoisted trong HrShell) nên màn này chỉ hiện phần chú thích + trạng thái/kết quả.
 */
@Composable
fun FaceEnrollScreen(vm: HrViewModel, onBack: () -> Unit) {
    val context = LocalContext.current
    LaunchedEffect(Unit) { vm.loadFaceStatus(force = true) }
    val registered = vm.faceRegistered == true
    val pending = vm.faceEnrollmentPending
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
            contentPadding = PaddingValues(start = 14.dp, end = 14.dp, bottom = BottomBarScrollRoom, top = 4.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            item { EnrollHero(registered, pending, vm.faceEnrollmentStatus, vm.faceEnrollmentReviewNote) }

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
                            "Hệ thống chỉ lưu VECTOR đặc trưng đã mã hoá, KHÔNG lưu ảnh khuôn mặt. Vector chỉ được kích hoạt sau khi HR đối chiếu trực tiếp danh tính của bạn.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }

            item {
                HrCard {
                    Text("Cách quét (5 góc)", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
                    Spacer(Modifier.height(10.dp))
                    StepRow(1, "Nhìn thẳng vào camera")
                    StepRow(2, "Quay đầu nhẹ sang một bên")
                    StepRow(3, "Quay sang bên còn lại")
                    StepRow(4, "Ngẩng mặt nhẹ lên")
                    StepRow(5, "Cúi mặt nhẹ xuống")
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
                            Text("Đang mã hoá và gửi yêu cầu…", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
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
                pending = pending,
                submitting = enroll is FaceEnrollCapture.Submitting,
                failed = enroll is FaceEnrollCapture.Done && !(enroll).success,
                onStart = onStart,
            )
        }
    }
}

@Composable
private fun EnrollHero(registered: Boolean, pending: Boolean, status: String?, reviewNote: String?) {
    val accent = MaterialTheme.colorScheme.primary
    val rejected = status.equals("rejected", ignoreCase = true)
    val expired = status.equals("expired", ignoreCase = true)
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
                    if (registered) Icons.Filled.CheckCircle else if (pending) Icons.Filled.Shield else Icons.Filled.Fingerprint,
                    contentDescription = null,
                    tint = if (registered) Success else accent,
                    modifier = Modifier.size(42.dp),
                )
            }
            Text(
                when {
                    registered -> "Đã đăng ký khuôn mặt"
                    pending -> "Đang chờ HR xác minh"
                    rejected -> "Yêu cầu chưa được duyệt"
                    expired -> "Yêu cầu đã hết hạn"
                    else -> "Đăng ký khuôn mặt của bạn"
                },
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurface,
                fontWeight = FontWeight.Bold,
                textAlign = TextAlign.Center,
            )
            Text(
                when {
                    registered -> "Tài khoản của bạn đã có mẫu khuôn mặt được xác minh."
                    pending -> "Vector đã được mã hoá và lưu tạm. HR cần gặp, đối chiếu trực tiếp bạn với tài khoản rồi mới kích hoạt."
                    rejected -> reviewNote?.takeIf { it.isNotBlank() }?.let { "Lý do: $it. Bạn có thể quét và gửi lại." }
                        ?: "Bạn có thể quét và gửi lại sau khi kiểm tra thông tin với HR."
                    expired -> "Vector tạm đã được xoá an toàn. Vui lòng quét và gửi yêu cầu mới."
                    else -> "Quét khuôn mặt một lần để gửi yêu cầu xác minh dùng cho chấm công."
                },
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
private fun EnrollActionButton(registered: Boolean, pending: Boolean, submitting: Boolean, failed: Boolean, onStart: () -> Unit) {
    if (registered || pending) {
        // Đã đăng ký → nút bị LÀM MỜ (disabled) để tránh đăng ký lần hai.
        Button(
            onClick = {},
            enabled = false,
            modifier = Modifier
                .fillMaxWidth()
                .height(52.dp),
            shape = RoundedCornerShape(16.dp),
        ) {
            Icon(if (registered) Icons.Filled.CheckCircle else Icons.Filled.Shield, contentDescription = null, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(8.dp))
            Text(if (registered) "Đã đăng ký khuôn mặt" else "Đang chờ HR duyệt", fontWeight = FontWeight.Bold)
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
 * Lớp phủ toàn màn hình cho bước quét đăng ký (chỉ ẩn thanh trạng thái + nút Đóng). Thanh điều hướng
 * Android luôn hiện. Được gọi ở tầng ngoài
 * Scaffold (xem HrShell). Khi quét đủ 5 góc → [onCompleted]; bấm Đóng / hết giờ → [onCancel].
 */
@Composable
fun FaceEnrollCameraScan(onCompleted: (List<FaceEnrollPose>) -> Unit, onCancel: () -> Unit) {
    val context = LocalContext.current
    KeepScreenBrightEffect()
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

// Quan sát khuôn mặt cho đăng ký: tư thế 3 trục và độ mở mắt.
private data class EnrollObs(
    val cx: Float,
    val cy: Float,
    val widthFrac: Float,
    val yawDeg: Float,
    val pitchDeg: Float,
    val rollDeg: Float,
    val eyeOpen: Float,
    val t: Long,
)

private class EnrollAimState {
    @Volatile var latest: EnrollObs? = null
    val collect = AtomicBoolean(false)
    private val frames = ArrayList<String>()
    private var lastCaptureAt = 0L

    fun shouldCaptureFrame(now: Long): Boolean = synchronized(frames) {
        if (frames.size >= E_MAX_FRAMES || now - lastCaptureAt < E_CAPTURE_GAP_MS) return@synchronized false
        lastCaptureAt = now
        true
    }

    fun addFrame(url: String) = synchronized(frames) {
        if (frames.size < E_MAX_FRAMES) frames.add(url)
    }
    fun snapshot(): List<String> = synchronized(frames) { frames.toList() }
    fun clearFrames() = synchronized(frames) { frames.clear(); lastCaptureAt = 0L }
}

/**
 * CameraX + ML Kit nhẹ: quét 5 góc; mỗi góc giữ ổn định để gom 4–6 khung trải đều. Không chạy
 * MediaPipe/lưới mặt trên thiết bị; server mới là nơi chọn khung, PAD và trích vector bảo mật.
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
    val cameraClosed = remember { AtomicBoolean(false) }
    val onCompletedNow by rememberUpdatedState(onCompleted)
    val onCancelNow by rememberUpdatedState(onCancel)
    val detector = remember {
        FaceDetection.getClient(
            FaceDetectorOptions.Builder()
                .setPerformanceMode(FaceDetectorOptions.PERFORMANCE_MODE_FAST)
                .setClassificationMode(FaceDetectorOptions.CLASSIFICATION_MODE_ALL)
                .setMinFaceSize(0.15f)
                .build(),
        )
    }

    var poseIndex by remember { mutableStateOf(0) }
    var holdProgress by remember { mutableStateOf(0f) }
    var hint by remember { mutableStateOf(ENROLL_POSES[0].title) }
    val analysisRef = remember { java.util.concurrent.atomic.AtomicReference<ImageAnalysis?>() }

    DisposableEffect(Unit) {
        cameraClosed.set(false)
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
                            if (cameraClosed.get()) { image.close(); return@setAnalyzer }
                            if (aim.collect.get() && aim.shouldCaptureFrame(SystemClock.elapsedRealtime())) {
                                runCatching { aim.addFrame(image.toJpegDataUrl(quality = 88)) }
                            }
                            val media = image.image
                            if (media == null) { image.close(); return@setAnalyzer }
                            val rotation = image.imageInfo.rotationDegrees
                            val iw = if (rotation == 90 || rotation == 270) image.height else image.width
                            val ih = if (rotation == 90 || rotation == 270) image.width else image.height
                            val input = InputImage.fromMediaImage(media, rotation)
                            detector.process(input)
                                .addOnSuccessListener(mainExecutor) { faces ->
                                    if (!cameraClosed.get()) runCatching {
                                        val face = faces.maxByOrNull { it.boundingBox.width() * it.boundingBox.height() }
                                        aim.latest = if (face != null) {
                                            val bb = face.boundingBox
                                            EnrollObs(
                                                cx = (bb.exactCenterX() / iw).coerceIn(0f, 1f),
                                                cy = (bb.exactCenterY() / ih).coerceIn(0f, 1f),
                                                 widthFrac = (bb.width().toFloat() / iw).coerceIn(0f, 1f),
                                                 yawDeg = face.headEulerAngleY,
                                                 pitchDeg = face.headEulerAngleX,
                                                 rollDeg = face.headEulerAngleZ,
                                                 eyeOpen = minOf(
                                                     face.leftEyeOpenProbability ?: 1f,
                                                     face.rightEyeOpenProbability ?: 1f,
                                                 ),
                                                 t = SystemClock.elapsedRealtime(),
                                            )
                                        } else null
                                    }
                                }
                                .addOnFailureListener(mainExecutor) {
                                    if (!cameraClosed.get()) aim.latest = null
                                }
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
            cameraClosed.set(true)
            runCatching { analysisRef.getAndSet(null)?.clearAnalyzer() }
            runCatching { ProcessCameraProvider.getInstance(context).get().unbindAll() }
            runCatching { detector.close() }
            runCatching { executor.shutdown() }
        }
    }

    LaunchedEffect(Unit) {
        aim.collect.set(false); aim.clearFrames(); aim.latest = null
        poseIndex = 0; holdProgress = 0f; hint = ENROLL_POSES[0].title
        val collected = ArrayList<FaceEnrollPose>()
        var firstSideSign = 0
        var frontPitch = 0f
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
                val eyesOpen = face.eyeOpen >= E_EYE_OPEN_MIN
                val upright = abs(face.rollDeg) <= E_ROLL_MAX

                val poseGood: Boolean
                val poseHint: String
                when (spec.kind) {
                    PoseKind.Front -> {
                        poseGood = mag <= E_FRONT_YAW_MAX && abs(face.pitchDeg) <= E_FRONT_PITCH_MAX
                        poseHint = if (face.pitchDeg > E_FRONT_PITCH_MAX) {
                            "Hạ mặt xuống để nhìn thẳng"
                        } else if (face.pitchDeg < -E_FRONT_PITCH_MAX) {
                            "Ngẩng mặt lên để nhìn thẳng"
                        } else {
                            "Nhìn thẳng vào camera"
                        }
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
                    PoseKind.Up -> {
                        poseGood = mag <= E_FRONT_YAW_MAX && face.pitchDeg - frontPitch >= E_PITCH_DELTA
                        poseHint = "Ngẩng mặt lên thêm một chút"
                    }
                    PoseKind.Down -> {
                        poseGood = mag <= E_FRONT_YAW_MAX && frontPitch - face.pitchDeg >= E_PITCH_DELTA
                        poseHint = "Cúi mặt xuống thêm một chút"
                    }
                }

                when {
                    w > E_FACE_TOO_CLOSE -> resetHold("Quá gần — lùi ra xa một chút")
                    !centered -> resetHold("Đưa khuôn mặt vào giữa khung")
                    w < E_FACE_MIN -> resetHold("Nhích lại gần hơn một chút")
                    !eyesOpen -> resetHold("Hãy mở mắt nhìn vào camera")
                    !upright -> resetHold("Giữ đầu thẳng, không nghiêng về vai")
                    !poseGood -> resetHold(poseHint)
                    else -> {
                        if (!holding) { holding = true; holdStart = now; aim.clearFrames(); aim.collect.set(true) }
                        val held = now - holdStart
                        holdProgress = (held.toFloat() / E_HOLD_MS).coerceIn(0f, 1f)
                        hint = "Giữ yên…"
                        if (held >= E_HOLD_MS) {
                            val frames = aim.snapshot()
                            aim.collect.set(false); holding = false; holdProgress = 0f
                            if (frames.size >= E_MIN_FRAMES) {
                                collected.add(FaceEnrollPose(spec.label, frames))
                                if (spec.kind == PoseKind.Front) frontPitch = face.pitchDeg
                                if (spec.kind == PoseKind.Side && firstSideSign == 0) firstSideSign = sign
                                poseIndex++
                                if (poseIndex >= ENROLL_POSES.size) {
                                    onCompletedNow(collected)
                                    return@LaunchedEffect
                                }
                                hint = ENROLL_POSES[poseIndex].title
                            } else {
                                hint = "Chưa đủ ảnh rõ nét — giữ lại góc này"
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
