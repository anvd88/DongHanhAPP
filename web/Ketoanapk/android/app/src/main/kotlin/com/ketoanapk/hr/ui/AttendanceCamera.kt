package com.ketoanapk.hr.ui

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.ImageFormat
import android.graphics.Matrix
import android.graphics.Rect
import android.graphics.YuvImage
import android.os.SystemClock
import android.util.Base64
import android.view.WindowManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Login
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.filled.WarningAmber
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
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
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathMeasure
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.util.lerp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.face.FaceDetection
import com.google.mlkit.vision.face.FaceDetectorOptions
import com.ketoanapk.hr.data.CapturedFrame
import com.ketoanapk.hr.data.ChamCongResult
import com.ketoanapk.hr.data.AttendancePolicy
import com.ketoanapk.hr.data.OfflineAttendanceRecord
import com.ketoanapk.hr.ui.theme.Danger
import com.ketoanapk.hr.ui.theme.Success
import com.ketoanapk.hr.ui.theme.Warning
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import java.io.ByteArrayOutputStream
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.abs

// ── Tham số căn khung 2 bước + quét (đơn vị: tỉ lệ bề rộng mặt so với khung, thời gian ms) ──
// "Bề rộng mặt" = chiều rộng hộp khuôn mặt / chiều rộng ảnh. To hơn = ở gần hơn.
// Bước 1 (khung LỚN, ở xa thoải mái ~40cm) rồi bước 2 (khung NHỎ, nhích gần vừa phải ~30cm) — có
// chặn "quá gần" để không hại mắt.
private const val FAR_MIN = 0.24f          // xa quá (mặt bé) → "nhích lại gần"
private const val FAR_MAX = 0.44f          // ở khung lớn không cần quá gần
private const val NEAR_MIN = 0.42f         // khung nhỏ: cần gần hơn chút
private const val NEAR_TOO_CLOSE = 0.70f   // vượt ngưỡng này = quá sát → "lùi ra xa" (bảo vệ mắt)
private const val CENTER_TOL_X = 0.18f
private const val CENTER_TOL_Y = 0.20f
private const val STAGE_STABLE_MS = 550L   // giữ đạt liên tục ngần này mới sang bước kế
private const val HOLD_MS = 3000L          // quét giữ khung 3 giây
private const val MOTION_CENTER_YAW = 12f  // |yaw| < mức này = coi như nhìn thẳng (độ)
private const val MOTION_TURN_YAW = 20f    // |yaw| > mức này = đã quay đủ sang một bên (độ)
private const val MOTION_FRONTAL_MS = 350L // giữ nhìn thẳng ngần này trước khi yêu cầu quay
private const val SMILE_STABLE_MS = 500L   // giữ nụ cười liên tục để tránh nhận nhầm một khung hình
// ── Kiểm tra "đang nhìn vào màn hình" trong lúc quét (mở mắt + hướng mặt thẳng) ──
// Không có eye-tracking con ngươi (ML Kit không cho) nên "ánh nhìn" = tư thế đầu hướng vào máy trên
// cả hai trục + mắt đang mở. Nhắm mắt / nhìn đi chỗ khác sẽ TẠM DỪNG đồng hồ giữ khung.
private const val GAZE_MAX_YAW = 18f       // |yaw| < mức này = mặt hướng vào màn hình (trục ngang)
private const val GAZE_MAX_PITCH = 18f     // |pitch| < mức này = không cúi/ngẩng quá (trục dọc)
private const val EYE_OPEN_MIN = 0.35f     // xác suất mở mắt tối thiểu (dưới = coi như nhắm/lim dim)
private const val POLL_MS = 40L
private const val FACE_STALE_MS = 350L     // không thấy mặt quá lâu = coi như rời khung
private const val AIM_TIMEOUT_MS = 45_000L // không căn được khung đủ lâu → tự huỷ để thử lại
private const val MAX_FRAMES = 14          // trần số khung gửi lên ở chế độ nhìn thẳng
private const val PER_SLOT_CAP = 3         // chế độ quay đầu: tối đa số khung của mỗi pha chuyển động
private const val CAPTURE_GAP_MS = 180L    // trải đều khung, tránh tạo JPEG ở mọi frame gây tăng RAM đột biến

private enum class AimStage { Far, Near, Hold }

@Composable
fun AttendanceScreen(vm: HrViewModel) {
    val context = LocalContext.current
    val server = vm.attendanceServer
    val capture = vm.attendanceCapture

    // Đang trong luồng chấm công (căn khung, quét, chờ xác nhận...) → Back huỷ luồng về trạng thái nghỉ
    // trước, chưa rời tab. Lớp camera phủ toàn màn hình được dựng ở HrApp nhưng màn này vẫn nằm dưới nên
    // handler ở đây vẫn nhận được Back.
    BackHandler(enabled = capture != AttendanceCapture.Idle) { vm.resetCapture() }

    var hasCamera by remember {
        mutableStateOf(
            ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED,
        )
    }
    val permissionLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        hasCamera = granted
    }
    // Dùng chung màn quét CameraX + ML Kit với các chỗ quét khác (auto-zoom, chạm lấy nét, đèn pin).
    // Chế độ Raw vì chấm công đã có endpoint riêng, không đi qua /api/qr/resolve.
    val qrLauncher = rememberLauncherForActivityResult(QrCaptureContract()) { result ->
        result.value?.takeIf { it.isNotBlank() }?.let(vm::submitQrAttendance)
    }
    // Quyền vị trí (tùy chọn) cho chấm công ngoại tuyến: xin lần đầu rồi bắt đầu quét dù cấp hay không.
    val locationLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
        vm.refreshAttendanceContext()
        vm.startOfflineCapture()
    }
    val startOffline: () -> Unit = {
        val granted =
            ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED ||
                ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED
        if (granted) vm.startOfflineCapture()
        else locationLauncher.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
    }

    // Camera CHỉ bật ở bước quét (Collecting) — dùng chung cho trực tuyến lẫn ngoại tuyến, hiển thị bất
    // kể máy chủ online hay không. Các bước còn lại là màn hình riêng (không đè lên camera).
    when {
        // Bước quét camera hiển thị TOÀN MÀN HÌNH ở tầng ngoài Scaffold (xem HrShell) để không dính
        // thanh tiêu đề/điều hướng. Ở đây chỉ để nền đen làm lớp dưới, tránh bind camera hai lần.
        capture is AttendanceCapture.Collecting ->
            Box(Modifier.fillMaxSize().background(Color.Black))

        capture is AttendanceCapture.Preparing ->
            AttendanceStatusScreen("Đang chuẩn bị máy quét…")

        // Nhận diện → xác nhận → ghi công: MỘT màn hình duy nhất (cùng một call-site) để hiệu ứng
        // "vòng loading → vẽ dấu tích/dấu X → trượt lên góc trái → hiện thông tin" liền mạch, không
        // bị Compose thay composable giữa chừng làm giật. Kiểu xác thực giao dịch Techcombank.
        capture is AttendanceCapture.Recognizing ||
            capture is AttendanceCapture.AwaitingConfirm ||
            capture is AttendanceCapture.Submitting ||
            capture is AttendanceCapture.Done ->
            AttendanceVerifyScreen(vm)

        else -> AttendanceLanding(
            server = server,
            hasCamera = hasCamera,
            pending = vm.attendancePending,
            policy = vm.attendancePolicy,
            location = vm.attendanceLocation,
            history = vm.attendanceHistory,
            onRequestPermission = { permissionLauncher.launch(Manifest.permission.CAMERA) },
            onRetryServer = vm::checkAttendanceServer,
            onStart = vm::startCapture,
            onStartOffline = startOffline,
            onScanQr = {
                qrLauncher.launch(QrCaptureRequest(QrCaptureMode.Raw, prompt = "Quét mã QR chấm công"))
            },
        )
    }
}

/**
 * Bước quét: camera toàn màn hình, chỉ ẩn tạm thanh trạng thái; thanh điều hướng Android luôn hiện.
 * Được gọi ở tầng ngoài Scaffold
 * (xem HrShell) nên không dính thanh tiêu đề/điều hướng của app. Chỉ hiện khi đang Collecting.
 */
@Composable
fun FullScreenCameraScan(
    onCaptured: (List<CapturedFrame>) -> Unit,
    onCancel: () -> Unit,
    motionMode: Boolean = false,
    smileMode: Boolean = false,
    smileThreshold: Float = 0.65f,
) {
    val context = LocalContext.current
    KeepScreenBrightEffect()
    // Chỉ ẩn thanh trạng thái để camera rộng hơn; luôn giữ thanh điều hướng Android cho người dùng.
    DisposableEffect(Unit) {
        val window = context.findActivity()?.window
        val controller = window?.let { WindowCompat.getInsetsController(it, it.decorView) }
        controller?.apply {
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            hide(WindowInsetsCompat.Type.statusBars())
        }
        onDispose { controller?.show(WindowInsetsCompat.Type.statusBars()) }
    }
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black),
    ) {
        BiometricFaceCamera(
            capturing = true,
            modifier = Modifier.fillMaxSize(),
            onCaptured = onCaptured,
            onCancel = onCancel,
            motionMode = motionMode,
            smileMode = smileMode,
            smileThreshold = smileThreshold,
        )
        IconButton(
            onClick = onCancel,
            modifier = Modifier
                .align(Alignment.TopStart)
                .statusBarsPadding() // vẫn bấm được nếu người dùng vuốt hiện thanh trạng thái
                .padding(12.dp)
                .size(44.dp)
                .clip(CircleShape)
                .background(Color.Black.copy(alpha = 0.48f)),
        ) {
            Icon(Icons.Filled.Close, contentDescription = "Đóng", tint = Color.White)
        }
    }
}

/** Màn hình chờ tối giản khi máy chủ đang xử lý (so khớp / ghi công). */
@Composable
private fun AttendanceStatusScreen(label: String) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background),
        contentAlignment = Alignment.Center,
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(14.dp)) {
            CircularProgressIndicator(color = MaterialTheme.colorScheme.primary)
            Text(label, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
        }
    }
}

/** Màn hình chờ trước khi bật camera: trạng thái máy chủ + hướng dẫn + nút bắt đầu (trực tuyến/ngoại tuyến). */
@Composable
private fun AttendanceLanding(
    server: AttendanceServerState,
    hasCamera: Boolean,
    pending: Int,
    policy: AttendancePolicy?,
    location: android.location.Location?,
    history: List<OfflineAttendanceRecord>,
    onRequestPermission: () -> Unit,
    onRetryServer: () -> Unit,
    onStart: () -> Unit,
    onStartOffline: () -> Unit,
    onScanQr: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        PageHead("Chấm công", "Xác thực khuôn mặt kiểu sinh trắc học")
        ServerStatusCard(server, onRetry = onRetryServer)
        AttendanceLocationCard(policy, location)
        if (pending > 0) PendingOfflineChip(pending)
        if (history.isNotEmpty()) OfflineHistorySummary(history)

        Box(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f)
                .clip(RoundedCornerShape(22.dp))
                .background(Color(0xFF0E0F13)),
            contentAlignment = Alignment.Center,
        ) {
            if (!hasCamera) {
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(14.dp),
                    modifier = Modifier.padding(24.dp),
                ) {
                    Icon(Icons.Filled.CameraAlt, contentDescription = null, tint = Color.White.copy(alpha = 0.85f), modifier = Modifier.size(48.dp))
                    Text(
                        "Ứng dụng cần quyền camera để chấm công bằng khuôn mặt.",
                        color = Color.White.copy(alpha = 0.85f),
                        style = MaterialTheme.typography.bodyMedium,
                        textAlign = TextAlign.Center,
                    )
                    Button(onClick = onRequestPermission, shape = RoundedCornerShape(14.dp)) {
                        Text("Cấp quyền camera", fontWeight = FontWeight.Bold)
                    }
                }
            } else {
                IdlePrompt()
            }
        }

        when {
            !hasCamera -> Unit // nút cấp quyền nằm trong khung ở trên

            server is AttendanceServerState.Online -> Button(
                onClick = onStart,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(54.dp),
                shape = RoundedCornerShape(16.dp),
            ) {
                Icon(Icons.Filled.Face, contentDescription = null, modifier = Modifier.size(22.dp))
                Spacer(Modifier.width(8.dp))
                Text("Bắt đầu chấm công", fontWeight = FontWeight.Bold)
            }

            server is AttendanceServerState.Checking -> Button(
                onClick = {},
                enabled = false,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(54.dp),
                shape = RoundedCornerShape(16.dp),
            ) { Text("Đang kiểm tra kết nối…", fontWeight = FontWeight.Bold) }

            else -> Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Button(
                    onClick = onStartOffline,
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(54.dp),
                    shape = RoundedCornerShape(16.dp),
                ) {
                    Icon(Icons.Filled.CloudOff, contentDescription = null, modifier = Modifier.size(22.dp))
                    Spacer(Modifier.width(8.dp))
                    Text("Chấm công ngoại tuyến (chờ duyệt)", fontWeight = FontWeight.Bold)
                }
                Text(
                    "Mất kết nối máy chủ. Lượt chấm sẽ được lưu tạm và tự đồng bộ để quản lý duyệt khi có mạng.",
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    style = MaterialTheme.typography.bodySmall,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }
        if (hasCamera) {
            OutlinedButton(onClick = onScanQr, modifier = Modifier.fillMaxWidth()) {
                Icon(Icons.Filled.QrCodeScanner, contentDescription = null)
                Spacer(Modifier.width(8.dp))
                Text("Quét QR dự phòng / công trình")
            }
        }
    }
}

@Composable
private fun AttendanceLocationCard(policy: AttendancePolicy?, location: android.location.Location?) {
    val distance = if (policy?.geofenceLat != null && policy.geofenceLng != null && location != null) {
        val out = FloatArray(1)
        android.location.Location.distanceBetween(location.latitude, location.longitude, policy.geofenceLat, policy.geofenceLng, out)
        out[0].toDouble()
    } else null
    val inside = distance != null && policy != null && distance <= policy.geofenceRadiusM
    HrCard {
        Text("Địa điểm chấm công", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
        Text(
            when {
                location == null -> "Chưa có vị trí. Cấp quyền vị trí khi chấm ngoại tuyến để kiểm tra phạm vi."
                distance == null -> "Độ chính xác GPS ±${location.accuracy.toInt()} m · Đơn vị chưa cấu hình geofence."
                inside -> "Trong phạm vi hợp lệ · cách tâm ${distance.toInt()} m · chính xác ±${location.accuracy.toInt()} m"
                else -> "Ngoài geofence ${distance.toInt()} m (bán kính ${policy?.geofenceRadiusM?.toInt()} m) · lượt chấm sẽ cần duyệt"
            },
            style = MaterialTheme.typography.bodySmall,
            color = if (distance != null && !inside) Danger else MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun OfflineHistorySummary(history: List<OfflineAttendanceRecord>) {
    HrCard {
        Text("Lịch sử chấm công ngoại tuyến", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
        history.take(3).forEach { item ->
            val label = when (item.status.lowercase()) { "approved" -> "Đã duyệt"; "rejected" -> "Bị từ chối"; else -> "Chờ duyệt" }
            Text("$label · ${item.loai} · ${formatIsoDateTime(item.occurredAt)}${item.reviewNote.takeIf { it.isNotBlank() }?.let { " · $it" } ?: ""}",
                style = MaterialTheme.typography.bodySmall,
                color = when (item.status.lowercase()) { "approved" -> Success; "rejected" -> Danger; else -> Warning })
        }
    }
}

/** Nhãn nhỏ báo số bản chấm công ngoại tuyến đang chờ đồng bộ. */
@Composable
private fun PendingOfflineChip(pending: Int) {
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.Schedule, contentDescription = null, tint = Warning, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(10.dp))
            Text(
                "$pending lượt chấm công ngoại tuyến đang chờ đồng bộ + duyệt",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurface,
            )
        }
    }
}

/** Hướng dẫn ở màn hình chờ (chưa bật camera). */
@Composable
private fun IdlePrompt() {
    val accent = MaterialTheme.colorScheme.primary
    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(14.dp),
        modifier = Modifier.padding(28.dp),
    ) {
        Box(
            modifier = Modifier
                .size(96.dp)
                .clip(CircleShape)
                .background(accent.copy(alpha = 0.18f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Filled.Face, contentDescription = null, tint = accent, modifier = Modifier.size(56.dp))
        }
        Text(
            "Sẵn sàng chấm công",
            color = Color.White,
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold,
            textAlign = TextAlign.Center,
        )
        Text(
            "Giữ điện thoại ngang tầm mắt, tháo khẩu trang và chọn nơi đủ sáng. Khi mở camera, làm theo từng hướng dẫn trên màn hình.",
            color = Color.White.copy(alpha = 0.82f),
            style = MaterialTheme.typography.bodyMedium,
            textAlign = TextAlign.Center,
        )
    }
}

@Composable
private fun CameraHint(icon: ImageVector, text: String) {
    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.padding(24.dp)) {
        Icon(icon, contentDescription = null, tint = Color.White.copy(alpha = 0.8f), modifier = Modifier.size(52.dp))
        Text(text, color = Color.White.copy(alpha = 0.8f), style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.Center)
    }
}

// ── Xác thực chấm công kiểu Techcombank ─────────────────────────────────────────
// Vòng loading → vẽ dấu tích (thành công) / dấu X đỏ (thất bại) ngay trên vòng tròn → trượt badge
// lên góc trên bên trái → các dòng thông tin đi từ dưới lên, mờ dần hiện rõ (~1.5s). Nền TẠM để
// trắng theo yêu cầu; chữ để màu tối cố định cho dễ đọc ở mọi theme.
private val VerifyGreen = Color(0xFF2FB44E)
private val VerifyRed = Color(0xFFF23A33)
private val VerifyInk = Color(0xFF17181C)
private val VerifyInkSoft = Color(0xFF6B7280)

private enum class VerifyBadge { Check, Cross }
private enum class VerifyPhase { Loading, Resolving, Revealed }

/** Trạng thái "coi như thành công" để chọn dấu tích (còn lại vẽ dấu X). */
private fun isVerifySuccess(status: String) =
    status.equals("ok", true) || status.equals("offline", true) || status.equals("pending", true)

/**
 * MỘT màn hình cho cả Recognizing / AwaitingConfirm / Submitting / Done (cùng một call-site ở
 * [AttendanceScreen]) để hiệu ứng liền mạch, không bị Compose thay composable giữa chừng. Máy trạng
 * thái nội bộ:
 *  - Loading  : vòng xoay tròn ở giữa (đang so khớp / đang ghi công).
 *  - Resolving: vòng tròn đặc + dấu tích/dấu X vẽ dần rồi trượt lên góc trái.
 *  - Revealed : badge ở góc trên trái, các dòng thông tin lần lượt hiện lên + nút hành động.
 */
@Composable
private fun AttendanceVerifyScreen(vm: HrViewModel) {
    val cap = vm.attendanceCapture

    // Trạng thái + animation phải sống xuyên suốt các lần đổi state → remember (call-site duy nhất).
    var phase by remember { mutableStateOf(VerifyPhase.Loading) }
    var badge by remember { mutableStateOf<VerifyBadge?>(null) }
    var busy by remember { mutableStateOf(false) }
    // Kết quả + kiểu hiển thị được "chốt" lúc phân giải để không mất khi state nhảy sang Submitting.
    var shownResult by remember { mutableStateOf<ChamCongResult?>(null) }
    var confirmMode by remember { mutableStateOf(false) }

    val badgeDraw = remember { Animatable(0f) } // 0 = còn vòng xoay, 1 = tích/X vẽ xong
    val centerT = remember { Animatable(1f) }   // 1 = badge to & ở giữa, 0 = nhỏ & ở góc trái
    val reveal = remember { Animatable(0f) }    // 0..1 tiến độ hiện thông tin (tween ~1.5s)

    // "Chữ ký" state để biết khi nào chạy lại chuỗi animation.
    val key = when (cap) {
        is AttendanceCapture.Recognizing -> "load:recog"
        is AttendanceCapture.Submitting -> "load:submit"
        is AttendanceCapture.AwaitingConfirm -> "resolve:confirm"
        is AttendanceCapture.Done -> if (isVerifySuccess(cap.result.status)) "resolve:ok" else "resolve:fail"
        else -> "load:other"
    }

    LaunchedEffect(key) {
        if (key.startsWith("load")) {
            if (phase == VerifyPhase.Revealed) {
                // Đang hiện thông tin xác nhận thì bấm "Xác nhận" → ghi công: giữ nguyên bố cục, chỉ
                // hiện tiến trình ở khu nút (KHÔNG thu badge về giữa để vẽ lại).
                busy = true
            } else {
                busy = false
                phase = VerifyPhase.Loading
                badge = null
                badgeDraw.snapTo(0f); centerT.snapTo(1f); reveal.snapTo(0f)
            }
        } else {
            val target = if (key == "resolve:fail") VerifyBadge.Cross else VerifyBadge.Check
            busy = false
            shownResult = when (cap) {
                is AttendanceCapture.AwaitingConfirm -> cap.result
                is AttendanceCapture.Done -> cap.result
                else -> shownResult
            }
            confirmMode = cap is AttendanceCapture.AwaitingConfirm
            if (phase == VerifyPhase.Revealed && badge == target) {
                // Ví dụ: xác nhận (tích) → ghi công xong (vẫn tích): chỉ đổi nội dung, không vẽ lại.
                badgeDraw.snapTo(1f); centerT.snapTo(0f); reveal.snapTo(1f)
            } else {
                phase = VerifyPhase.Resolving
                badge = target
                badgeDraw.snapTo(0f); centerT.snapTo(1f); reveal.snapTo(0f)
                badgeDraw.animateTo(1f, tween(680))         // vòng tròn đặc + vẽ dấu
                delay(160)
                centerT.animateTo(0f, tween(520, easing = FastOutSlowInEasing)) // trượt lên góc trái
                reveal.animateTo(1f, tween(1500))           // các dòng thông tin hiện dần
                phase = VerifyPhase.Revealed
            }
        }
    }

    val loadingLabel = if (cap is AttendanceCapture.Submitting) "Đang ghi công…" else "Đang so khớp khuôn mặt…"

    // Nền TRẮNG cho vùng nội dung (dưới thanh tiêu đề Scaffold). KHÔNG thêm statusBars/navigationBars
    // padding: Scaffold đã trừ insets qua padding(innerPadding) ở HrApp, thêm nữa sẽ dôi kép.
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.White),
    ) {
        BoxWithConstraints(Modifier.fillMaxSize()) {
            val density = LocalDensity.current
            val cw = constraints.maxWidth.toFloat()
            val chh = constraints.maxHeight.toFloat()
            val bigPx = with(density) { 104.dp.toPx() } // vòng tích/X ở GIỮA giữ như cũ
            val smallScale = 56f / 104f                  // badge ở GÓC trên-trái to hơn (~44→56dp)
            val cornerPad = with(density) { 20.dp.toPx() }
            val cornerTop = with(density) { 10.dp.toPx() }
            // Tâm badge: giữa màn hình (centerT=1) ↔ góc trên trái (centerT=0).
            val centerCx = cw / 2f
            val centerCy = chh / 2f
            val cornerCx = cornerPad + bigPx * smallScale / 2f
            val cornerCy = cornerTop + bigPx * smallScale / 2f
            val t = centerT.value
            val scale = lerp(smallScale, 1f, t)
            val tx = lerp(cornerCx, centerCx, t) - centerCx
            val ty = lerp(cornerCy, centerCy, t) - centerCy

            // Thông tin (chỉ dựng khi đã có kết quả) — nằm dưới vị trí badge ở góc trái.
            shownResult?.let { res ->
                if (reveal.value > 0f || phase == VerifyPhase.Revealed) {
                    VerifyInfo(
                        result = res,
                        confirmMode = confirmMode,
                        busy = busy && cap is AttendanceCapture.Submitting,
                        reveal = reveal.value,
                        topPad = with(density) { (cornerTop + bigPx * smallScale + 18.dp.toPx()).toDp() },
                        onConfirm = vm::confirmAttendance,
                        onRescan = vm::rescanAttendance,
                        onClose = vm::resetCapture,
                        onExplain = { vm.startAttendanceExplanation(res) },
                    )
                }
            }

            // Nhãn "đang xử lý" ở giữa, mờ dần khi vòng tròn đặc hiện ra.
            val labelAlpha = (1f - badgeDraw.value / 0.3f).coerceIn(0f, 1f)
            if (labelAlpha > 0.01f) {
                Text(
                    loadingLabel,
                    color = VerifyInkSoft,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier
                        .align(Alignment.Center)
                        .graphicsLayer { alpha = labelAlpha; translationY = with(density) { 92.dp.toPx() } },
                )
            }

            // Badge (vòng xoay → tròn đặc + dấu tích/X). Luôn ở call-site này; di chuyển bằng graphicsLayer.
            Box(
                modifier = Modifier
                    .align(Alignment.Center)
                    .size(with(density) { bigPx.toDp() })
                    .graphicsLayer {
                        translationX = tx
                        translationY = ty
                        scaleX = scale
                        scaleY = scale
                    },
                contentAlignment = Alignment.Center,
            ) {
                val spinnerAlpha = (1f - badgeDraw.value / 0.28f).coerceIn(0f, 1f)
                if (spinnerAlpha > 0.01f) {
                    CircularProgressIndicator(
                        modifier = Modifier
                            .size(64.dp) // giữ vòng loading như cũ, chỉ vòng tích/X phóng to
                            .graphicsLayer { alpha = spinnerAlpha },
                        color = Color(0xFF9AA3AF),
                        strokeWidth = 4.dp,
                    )
                }
                badge?.let { b ->
                    if (badgeDraw.value > 0f) {
                        VerifyBadgeCanvas(badge = b, draw = badgeDraw.value, modifier = Modifier.fillMaxSize())
                    }
                }
            }

            // Nút đóng ở góc phải trên (badge đã chiếm góc trái) — chỉ hiện khi đã ra thông tin.
            if (phase == VerifyPhase.Revealed) {
                IconButton(
                    onClick = vm::resetCapture,
                    modifier = Modifier
                        .align(Alignment.TopEnd)
                        .padding(4.dp)
                        .graphicsLayer { alpha = reveal.value },
                ) {
                    Icon(Icons.Filled.Close, contentDescription = "Đóng", tint = VerifyInkSoft)
                }
            }
        }
    }
}

/** Vẽ vòng tròn đặc + dấu tích (hoặc dấu X) theo tiến độ [draw] (0..1) như đang được vẽ tay. */
@Composable
private fun VerifyBadgeCanvas(badge: VerifyBadge, draw: Float, modifier: Modifier = Modifier) {
    val fill = if (badge == VerifyBadge.Check) VerifyGreen else VerifyRed
    Canvas(modifier) {
        val s = size.minDimension
        val c = Offset(size.width / 2f, size.height / 2f)
        // Vòng tròn đặc "nở" ra nhanh trong ~30% đầu (nối tiếp vòng xoay đang mờ đi).
        val popT = (draw / 0.3f).coerceIn(0f, 1f)
        drawCircle(color = fill, radius = s / 2f * lerp(0.82f, 1f, popT), center = c, alpha = popT)

        // Nét trắng của dấu, bắt đầu sau khi vòng tròn đã hiện (~32%).
        val strokeProg = ((draw - 0.32f) / 0.68f).coerceIn(0f, 1f)
        if (strokeProg <= 0f) return@Canvas
        val stroke = Stroke(width = s * 0.09f, cap = StrokeCap.Round, join = StrokeJoin.Round)
        val pm = PathMeasure()
        if (badge == VerifyBadge.Check) {
            val p = Path().apply {
                moveTo(s * 0.28f, s * 0.52f)
                lineTo(s * 0.44f, s * 0.68f)
                lineTo(s * 0.72f, s * 0.34f)
            }
            pm.setPath(p, false)
            val seg = Path()
            pm.getSegment(0f, pm.length * strokeProg, seg, true)
            drawPath(seg, color = Color.White, style = stroke)
        } else {
            // Dấu X: vẽ nét 1 rồi nét 2 nối tiếp cho có cảm giác "đang vẽ".
            val a = Path().apply { moveTo(s * 0.35f, s * 0.35f); lineTo(s * 0.65f, s * 0.65f) }
            val b2 = Path().apply { moveTo(s * 0.65f, s * 0.35f); lineTo(s * 0.35f, s * 0.65f) }
            val p1 = (strokeProg / 0.5f).coerceIn(0f, 1f)
            val p2 = ((strokeProg - 0.5f) / 0.5f).coerceIn(0f, 1f)
            pm.setPath(a, false)
            val sa = Path(); pm.getSegment(0f, pm.length * p1, sa, true)
            drawPath(sa, color = Color.White, style = stroke)
            if (p2 > 0f) {
                pm.setPath(b2, false)
                val sb = Path(); pm.getSegment(0f, pm.length * p2, sb, true)
                drawPath(sb, color = Color.White, style = stroke)
            }
        }
    }
}

/** Khối thông tin hiện dần (mỗi dòng đi từ dưới lên + mờ→rõ) + nút hành động, theo tiến độ [reveal]. */
@Composable
private fun VerifyInfo(
    result: ChamCongResult,
    confirmMode: Boolean,
    busy: Boolean,
    reveal: Float,
    topPad: androidx.compose.ui.unit.Dp,
    onConfirm: () -> Unit,
    onRescan: () -> Unit,
    onClose: () -> Unit,
    onExplain: () -> Unit,
) {
    val density = LocalDensity.current
    val dyPx = with(density) { 22.dp.toPx() }
    fun lineMod(index: Int): Modifier {
        val start = index * 0.12f
        val local = ((reveal - start) / 0.42f).coerceIn(0f, 1f)
        return Modifier.graphicsLayer { alpha = local; translationY = (1f - local) * dyPx }
    }

    val isOut = result.loai.equals("Ra", true)
    val timeLabel = if (isOut) "Giờ ra" else "Giờ vào"
    val name = result.fullName?.takeIf { it.isNotBlank() } ?: result.username ?: "--"
    val time = formatIsoTimeLocal(result.occurredAt)

    val title: String
    val subtitle: String?
    val rows = ArrayList<Pair<String, String>>()
    var note: String? = null
    var noteColor = Warning
    if (confirmMode) {
        title = "Đã xác thực khuôn mặt"
        subtitle = "Kiểm tra thông tin trước khi ghi công"
        rows += "Nhân viên" to name
        rows += timeLabel to time
        note = result.message.takeIf { it.isNotBlank() }
    } else when {
        result.status.equals("ok", true) -> {
            title = "Chấm công thành công"; subtitle = null
            rows += "Nhân viên" to name
            if (time != "--") rows += timeLabel to time
        }
        result.status.equals("offline", true) -> {
            title = "Đã lưu ngoại tuyến"; subtitle = null
            note = result.message.takeIf { it.isNotBlank() } ?: "Sẽ tự đồng bộ và chờ duyệt khi có mạng."
        }
        else -> {
            val (_, t, _) = attendanceVisual(result.status)
            title = t; subtitle = null
            note = result.guidance?.takeIf { it.isNotBlank() } ?: result.message.ifBlank { "Vui lòng thử lại." }
            noteColor = Danger
        }
    }
    val isError = !confirmMode && !result.status.equals("ok", true) && !result.status.equals("offline", true)
    val btnAlpha = ((reveal - (3 + rows.size) * 0.1f) / 0.3f).coerceIn(0f, 1f)

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(start = 24.dp, end = 24.dp, bottom = 20.dp)
            .padding(top = topPad),
    ) {
        Text(title, color = VerifyInk, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.ExtraBold, modifier = lineMod(0))
        if (subtitle != null) {
            Spacer(Modifier.height(4.dp))
            Text(subtitle, color = VerifyInkSoft, style = MaterialTheme.typography.bodyMedium, modifier = lineMod(1))
        }

        if (rows.isNotEmpty()) {
            Spacer(Modifier.height(18.dp))
            rows.forEachIndexed { i, (label, value) ->
                if (i > 0) Spacer(Modifier.height(12.dp))
                Column(modifier = lineMod(2 + i)) {
                    Text(label, color = VerifyInkSoft, style = MaterialTheme.typography.labelMedium)
                    Spacer(Modifier.height(2.dp))
                    Text(value, color = VerifyInk, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                }
            }
        }
        if (note != null) {
            Spacer(Modifier.height(14.dp))
            Text(
                note,
                color = noteColor,
                style = MaterialTheme.typography.bodyMedium,
                modifier = lineMod(2 + rows.size),
                maxLines = 4,
                overflow = TextOverflow.Ellipsis,
            )
        }

        Spacer(Modifier.weight(1f))

        if (busy) {
            Row(
                modifier = Modifier.fillMaxWidth().height(52.dp),
                horizontalArrangement = Arrangement.Center,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                CircularProgressIndicator(modifier = Modifier.size(22.dp), strokeWidth = 2.dp, color = MaterialTheme.colorScheme.primary)
                Spacer(Modifier.width(10.dp))
                Text("Đang ghi công…", color = VerifyInkSoft, fontWeight = FontWeight.SemiBold)
            }
        } else if (confirmMode) {
            Button(
                onClick = onConfirm,
                modifier = Modifier.fillMaxWidth().height(52.dp).graphicsLayer { alpha = btnAlpha },
                shape = RoundedCornerShape(16.dp),
            ) {
                Icon(Icons.Filled.CheckCircle, contentDescription = null, modifier = Modifier.size(20.dp))
                Spacer(Modifier.width(8.dp))
                Text("Xác nhận", fontWeight = FontWeight.Bold)
            }
            Spacer(Modifier.height(10.dp))
            OutlinedButton(
                onClick = onRescan,
                modifier = Modifier.fillMaxWidth().height(50.dp).graphicsLayer { alpha = btnAlpha },
                shape = RoundedCornerShape(16.dp),
            ) {
                Icon(Icons.Filled.Refresh, contentDescription = null, modifier = Modifier.size(20.dp))
                Spacer(Modifier.width(8.dp))
                Text("Quét lại", fontWeight = FontWeight.Bold)
            }
        } else {
            Button(
                onClick = onRescan,
                modifier = Modifier.fillMaxWidth().height(52.dp).graphicsLayer { alpha = btnAlpha },
                shape = RoundedCornerShape(16.dp),
            ) {
                Icon(Icons.Filled.Refresh, contentDescription = null, modifier = Modifier.size(20.dp))
                Spacer(Modifier.width(8.dp))
                Text("Quét lại", fontWeight = FontWeight.Bold)
            }
            if (isError) {
                Spacer(Modifier.height(10.dp))
                OutlinedButton(
                    onClick = onExplain,
                    modifier = Modifier.fillMaxWidth().graphicsLayer { alpha = btnAlpha },
                ) { Text("Gửi giải trình kèm ảnh/tài liệu") }
            }
            Spacer(Modifier.height(10.dp))
            OutlinedButton(
                onClick = onClose,
                modifier = Modifier.fillMaxWidth().height(50.dp).graphicsLayer { alpha = btnAlpha },
                shape = RoundedCornerShape(16.dp),
            ) {
                Text("Đóng", fontWeight = FontWeight.Bold)
            }
        }
    }
}

@Composable
fun ServerStatusCard(server: AttendanceServerState, onRetry: () -> Unit) {
    val (tone, label, detail) = when (server) {
        AttendanceServerState.Checking -> Triple(Tone.Warning, "Đang kiểm tra kết nối…", "Đang liên hệ máy chủ chấm công qua LAN")
        is AttendanceServerState.Online -> Triple(Tone.Success, "Đã kết nối máy chủ", "${server.engine} · ngưỡng khớp ${"%.2f".format(server.threshold)}")
        is AttendanceServerState.Offline -> Triple(Tone.Danger, "Mất kết nối máy chủ", server.message)
    }
    val color = toneColor(tone)
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(
                modifier = Modifier
                    .size(12.dp)
                    .clip(CircleShape)
                    .background(color),
            )
            Spacer(Modifier.width(10.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(label, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(detail, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
            if (server is AttendanceServerState.Checking) {
                CircularProgressIndicator(modifier = Modifier.size(20.dp), strokeWidth = 2.dp, color = color)
            } else {
                OutlinedButton(onClick = onRetry, shape = RoundedCornerShape(12.dp)) { Text("Thử lại") }
            }
        }
    }
}

private fun attendanceVisual(status: String): Triple<Tone, String, ImageVector> = when (status.lowercase()) {
    "ok" -> Triple(Tone.Success, "Chấm công thành công", Icons.Filled.CheckCircle)
    "offline" -> Triple(Tone.Warning, "Đã lưu ngoại tuyến", Icons.Filled.Schedule)
    "pending" -> Triple(Tone.Warning, "Đã gửi — chờ duyệt", Icons.Filled.Schedule)
    "posture" -> Triple(Tone.Warning, "Sai tư thế", Icons.Filled.WarningAmber)
    "eyesclosed" -> Triple(Tone.Warning, "Chưa mở mắt", Icons.Filled.WarningAmber)
    "nosmile" -> Triple(Tone.Warning, "Chưa thấy nụ cười", Icons.Filled.WarningAmber)
    "lowquality" -> Triple(Tone.Warning, "Ảnh chưa đủ rõ", Icons.Filled.WarningAmber)
    "noface" -> Triple(Tone.Warning, "Không thấy khuôn mặt", Icons.Filled.WarningAmber)
    "spoof" -> Triple(Tone.Danger, "Nghi ngờ giả mạo", Icons.Filled.ErrorOutline)
    "proxy" -> Triple(Tone.Danger, "Không phải tài khoản của bạn", Icons.Filled.ErrorOutline)
    // Nhận diện ĐÃ thành công, chỉ là để form xác nhận quá lâu — không phải lỗi khuôn mặt, nên báo
    // đúng bản chất (và ở mức nhắc nhở) thay vì rơi vào "Chưa nhận diện được".
    "expired" -> Triple(Tone.Warning, "Hết hạn xác nhận", Icons.Filled.Schedule)
    else -> Triple(Tone.Danger, "Chưa nhận diện được", Icons.Filled.ErrorOutline)
}

// ── Nơi giữ quan sát khuôn mặt mới nhất + bộ đệm khung để gửi lên ─────────────
private data class FaceObs(
    val cx: Float,        // tâm mặt theo trục X (0..1) trong ảnh đã xoay
    val cy: Float,        // tâm mặt theo trục Y (0..1)
    val widthFrac: Float, // bề rộng mặt / bề rộng ảnh (to hơn = ở gần hơn)
    val yaw: Float,       // góc quay đầu trái/phải (độ, từ ML Kit headEulerAngleY) — cho liveness quay đầu
    val pitch: Float,     // góc ngẩng/cúi (độ, headEulerAngleX) — cùng yaw để biết mặt có hướng vào màn hình
    val eyeOpen: Float,   // xác suất mở mắt (min hai mắt; 1f nếu ML Kit chưa chắc) — cho bước kiểm tra ánh nhìn
    val smile: Float,     // xác suất đang cười từ ML Kit; 0f khi không phân loại được
    val t: Long,          // mốc thời gian (elapsedRealtime) để biết khung còn "tươi"
)

private class FaceAimState {
    @Volatile var latest: FaceObs? = null
    // Pha quay đầu đang thu khung; -1 là chế độ nhìn thẳng thông thường. Không điều khiển màu màn hình.
    @Volatile var currentSlot: Int = -1
    val collect = AtomicBoolean(false)
    private val frames = ArrayList<CapturedFrame>()
    private var lastCaptureAt = 0L

    /** Đặt trước một chỗ chụp; null nghĩa là chưa tới nhịp hoặc pha hiện tại đã đủ ảnh. */
    fun reserveCaptureSlot(now: Long): Int? = synchronized(frames) {
        val slot = currentSlot
        if (now - lastCaptureAt < CAPTURE_GAP_MS) return@synchronized null
        val hasCapacity = if (slot < 0) {
            frames.count { it.slot < 0 } < MAX_FRAMES
        } else {
            frames.count { it.slot == slot } < PER_SLOT_CAP
        }
        if (!hasCapacity) return@synchronized null
        lastCaptureAt = now
        slot
    }

    fun addFrame(url: String, slot: Int) = synchronized(frames) {
        if (slot < 0) {
            // Chế độ nhìn thẳng: reserveCaptureSlot đã giới hạn MAX_FRAMES.
            frames.add(CapturedFrame(url, -1))
        } else {
            // Chế độ quay đầu: reserveCaptureSlot đã giới hạn từng pha.
            frames.add(CapturedFrame(url, slot))
        }
    }
    fun snapshot(): List<CapturedFrame> = synchronized(frames) { frames.toList() }
    fun clearFrames() = synchronized(frames) { frames.clear(); lastCaptureAt = 0L }
}

/**
 * CameraX (camera trước) + ML Kit nhận diện on-device để căn vị trí/khoảng cách, quét giữ khung
 * 3 giây, gom khung ngay trong lúc giữ rồi trả về loạt ảnh qua [onCaptured]. MediaPipe không chạy
 * trong luồng này: lớp nhận diện chỉ xử lý nền, không vẽ lưới hoặc khung bám theo khuôn mặt.
 */
@androidx.annotation.OptIn(ExperimentalGetImage::class)
@Composable
fun BiometricFaceCamera(
    capturing: Boolean,
    modifier: Modifier = Modifier,
    onCaptured: (List<CapturedFrame>) -> Unit,
    onCancel: () -> Unit,
    motionMode: Boolean = false,
    smileMode: Boolean = false,
    smileThreshold: Float = 0.65f,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val previewView = remember { PreviewView(context).apply { scaleType = PreviewView.ScaleType.FILL_CENTER } }
    val executor = remember { Executors.newSingleThreadExecutor() }
    // Callback ML Kit chạy trên MAIN executor (không bao giờ bị tắt) để tránh RejectedExecutionException
    // khi màn hình camera bị hủy lúc executor nền đã shutdown → tránh crash.
    val mainExecutor = remember { ContextCompat.getMainExecutor(context) }
    val aim = remember { FaceAimState() }
    val cameraClosed = remember { AtomicBoolean(false) }
    val onCapturedNow by rememberUpdatedState(onCaptured)
    val onCancelNow by rememberUpdatedState(onCancel)
    val detector = remember {
        FaceDetection.getClient(
            FaceDetectorOptions.Builder()
                .setPerformanceMode(FaceDetectorOptions.PERFORMANCE_MODE_FAST)
                // Bật phân loại để lấy xác suất MỞ MẮT (leftEye/rightEyeOpenProbability) cho bước kiểm
                // tra ánh nhìn. FAST + classification vẫn chạy realtime tốt trên máy hiện đời.
                .setClassificationMode(FaceDetectorOptions.CLASSIFICATION_MODE_ALL)
                .setMinFaceSize(0.15f)
                .build(),
        )
    }

    // Trạng thái UI do vòng lặp căn khung điều khiển (đọc/ghi trên luồng chính).
    var stage by remember { mutableStateOf(AimStage.Far) }
    var hint by remember { mutableStateOf("Đặt khuôn mặt vào giữa khung") }
    var holdProgress by remember { mutableStateOf(0f) }
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
                        // Bọc toàn bộ để MỘT lỗi bất kỳ (ML Kit/giải mã ảnh) không làm crash cả app và
                        // ImageProxy luôn được đóng đúng một lần (nếu không đóng, camera sẽ treo khung).
                        try {
                            if (cameraClosed.get()) { image.close(); return@setAnalyzer }
                            // Thu khung NGAY trên luồng nền (khi đang ở pha giữ) — tách khỏi callback ML Kit.
                            if (aim.collect.get()) {
                                val slot = aim.reserveCaptureSlot(SystemClock.elapsedRealtime())
                                if (slot != null) runCatching { aim.addFrame(image.toJpegDataUrl(), slot) }
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
                                            // ML Kit trả null khi CHƯA chắc mắt mở/nhắm → coi như mở (1f) để
                                            // không chặn oan; chỉ chặn khi thực sự thấy mắt nhắm/lim dim.
                                            val eyeOpen = minOf(
                                                face.leftEyeOpenProbability ?: 1f,
                                                face.rightEyeOpenProbability ?: 1f,
                                            )
                                            FaceObs(
                                                cx = (bb.exactCenterX() / iw).coerceIn(0f, 1f),
                                                cy = (bb.exactCenterY() / ih).coerceIn(0f, 1f),
                                                widthFrac = (bb.width().toFloat() / iw).coerceIn(0f, 1f),
                                                yaw = face.headEulerAngleY,
                                                pitch = face.headEulerAngleX,
                                                eyeOpen = eyeOpen,
                                                smile = face.smilingProbability ?: 0f,
                                                t = SystemClock.elapsedRealtime(),
                                            )
                                        } else {
                                            null
                                        }
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
            // Gỡ analyzer TRƯỚC để CameraX chắc chắn không đẩy thêm khung nào lên executor sắp tắt,
            // rồi mới unbind + đóng detector + shutdown (tất cả bọc runCatching cho an toàn tuyệt đối).
            cameraClosed.set(true)
            runCatching { analysisRef.getAndSet(null)?.clearAnalyzer() }
            runCatching { ProcessCameraProvider.getInstance(context).get().unbindAll() }
            runCatching { detector.close() }
            runCatching { executor.shutdown() }
        }
    }

    // Vòng lặp căn khung 2 bước + giữ 3 giây. Chỉ chạy khi capturing=true.
    LaunchedEffect(capturing) {
        if (!capturing) { aim.collect.set(false); return@LaunchedEffect }
        aim.collect.set(false)
        aim.clearFrames()
        aim.latest = null
        aim.currentSlot = -1
        stage = AimStage.Far
        holdProgress = 0f
        hint = "Đặt khuôn mặt vào giữa khung"

        var goodSince = 0L
        var holdStart = 0L
        var holdTick = 0L        // mốc vòng lặp trước ở pha giữ khung — để TẠM DỪNG đồng hồ khi chưa nhìn/nhắm mắt
        val startedAt = SystemClock.elapsedRealtime()

        // Trạng thái pha giữ khung khi bật liveness QUAY ĐẦU (motionMode):
        // 0 = nhìn thẳng, 1 = quay một bên, 2 = quay bên còn lại, 3 = cười (nếu bật), 4 = xong.
        var motionStep = 0
        var sideASign = 0        // dấu yaw của lần quay đầu tiên (ép lần sau quay NGƯỢC lại)
        var frontalSince = 0L
        var smileSince = 0L

        while (isActive) {
            val now = SystemClock.elapsedRealtime()
            val face = aim.latest?.takeIf { now - it.t < FACE_STALE_MS }

            if (face == null) {
                goodSince = 0L
                if (stage == AimStage.Hold) { stage = AimStage.Near; aim.collect.set(false); aim.clearFrames() }
                holdProgress = 0f
                aim.currentSlot = -1
                motionStep = 0; frontalSince = 0L; smileSince = 0L
                hint = "Đặt khuôn mặt vào giữa khung"
            } else {
                val centered = abs(face.cx - 0.5f) < CENTER_TOL_X && abs(face.cy - 0.5f) < CENTER_TOL_Y
                val w = face.widthFrac
                when (stage) {
                    AimStage.Far -> when {
                        !centered -> { hint = "Di chuyển nhẹ để khuôn mặt nằm chính giữa"; goodSince = 0L }
                        w < FAR_MIN -> { hint = "Đưa điện thoại lại gần khuôn mặt một chút"; goodSince = 0L }
                        w > FAR_MAX -> { hint = "Đưa điện thoại ra xa khuôn mặt một chút"; goodSince = 0L }
                        else -> {
                            if (goodSince == 0L) goodSince = now
                            if (now - goodSince >= STAGE_STABLE_MS) {
                                stage = AimStage.Near; goodSince = 0L
                            }
                            hint = "Đúng vị trí — giữ yên một chút"
                        }
                    }
                    AimStage.Near -> when {
                        w > NEAR_TOO_CLOSE -> { hint = "Đang quá gần — đưa điện thoại ra xa"; goodSince = 0L }
                        !centered -> { hint = "Căn khuôn mặt vào chính giữa khung"; goodSince = 0L }
                        w < NEAR_MIN -> { hint = "Đưa điện thoại lại gần thêm một chút"; goodSince = 0L }
                        else -> {
                            if (goodSince == 0L) goodSince = now
                            if (now - goodSince >= STAGE_STABLE_MS) {
                                stage = AimStage.Hold; holdStart = now; holdTick = now
                                aim.clearFrames(); aim.collect.set(true)
                                motionStep = 0; sideASign = 0; frontalSince = 0L; smileSince = 0L
                            }
                            hint = "Khoảng cách phù hợp — giữ yên"
                        }
                    }
                    AimStage.Hold -> {
                        if (motionMode) {
                            // ── Pha LIVENESS QUAY ĐẦU (không ép giữ tĩnh; chỉ cần mặt còn trong khung) ──
                            // Rời khung quá xa/quá gần → về Near (giữ nguyên bước để đỡ bực); mất mặt hẳn thì
                            // nhánh face==null lo. Quay đầu làm mặt hẹp lại nên KHÔNG chặn theo NEAR_MIN.
                            val tooClose = w > NEAR_TOO_CLOSE
                            val wayOff = abs(face.cx - 0.5f) > 0.30f || abs(face.cy - 0.5f) > 0.30f
                            if (tooClose || wayOff || w < NEAR_MIN * 0.55f) {
                                stage = AimStage.Near; goodSince = 0L; holdProgress = 0f
                                aim.currentSlot = -1; motionStep = 0; frontalSince = 0L; smileSince = 0L
                                aim.collect.set(false); aim.clearFrames()
                                hint = if (tooClose) "Đang quá gần — đưa điện thoại ra xa" else "Giữ toàn bộ khuôn mặt trong khung"
                            } else {
                                val yaw = face.yaw
                                aim.collect.set(true)
                                when (motionStep) {
                                    0 -> {
                                        aim.currentSlot = 0; holdProgress = 0f
                                        // Phải MỞ MẮT và nhìn thẳng (yaw & pitch nhỏ) mới cho qua bước quay đầu.
                                        val eyesOpen = face.eyeOpen >= EYE_OPEN_MIN
                                        hint = if (!eyesOpen) "Mở mắt và nhìn thẳng vào camera" else "Nhìn thẳng vào camera"
                                        if (eyesOpen && abs(yaw) < MOTION_CENTER_YAW && abs(face.pitch) < GAZE_MAX_PITCH) {
                                            if (frontalSince == 0L) frontalSince = now
                                            if (now - frontalSince >= MOTION_FRONTAL_MS) motionStep = 1
                                        } else frontalSince = 0L
                                    }
                                    1 -> {
                                        aim.currentSlot = 1; holdProgress = 0.34f
                                        hint = "Từ từ quay đầu sang một bên"
                                        if (abs(yaw) > MOTION_TURN_YAW) {
                                            sideASign = if (yaw >= 0f) 1 else -1
                                            motionStep = 2
                                        }
                                    }
                                    2 -> {
                                        aim.currentSlot = 2; holdProgress = 0.67f
                                        hint = "Từ từ quay sang bên còn lại"
                                        // Bắt buộc quay NGƯỢC dấu lần đầu, đủ mạnh → đã phủ cả hai bên.
                                        if (yaw * sideASign < -MOTION_TURN_YAW) {
                                            motionStep = if (smileMode) 3 else 4
                                            smileSince = 0L
                                        }
                                    }
                                    3 -> {
                                        aim.currentSlot = 3; holdProgress = 0.85f
                                        val front = abs(yaw) < MOTION_CENTER_YAW && abs(face.pitch) < GAZE_MAX_PITCH
                                        val smiling = face.smile >= smileThreshold
                                        aim.collect.set(front && smiling)
                                        hint = when {
                                            !front -> "Đưa mặt về giữa và nhìn thẳng"
                                            !smiling -> "Nhìn thẳng và mỉm cười"
                                            else -> "Giữ nụ cười một chút"
                                        }
                                        if (front && smiling) {
                                            if (smileSince == 0L) smileSince = now
                                            if (now - smileSince >= SMILE_STABLE_MS) motionStep = 4
                                        } else {
                                            smileSince = 0L
                                        }
                                    }
                                    else -> {
                                        holdProgress = 1f
                                        val frames = aim.snapshot()
                                        aim.collect.set(false); aim.currentSlot = -1
                                        if (frames.isNotEmpty()) { onCapturedNow(frames); return@LaunchedEffect }
                                        else {
                                            stage = AimStage.Near; goodSince = 0L; motionStep = 0
                                            hint = "Chưa bắt được ảnh, thử lại"
                                        }
                                    }
                                }
                            }
                        } else {
                            // ── Giữ khung tĩnh 3 giây, KÈM kiểm tra "đang nhìn vào màn hình" ──
                            // Chỉ đếm giờ + thu khung khi: mặt trong khung + MỞ MẮT + nhìn thẳng (yaw & pitch
                            // nhỏ). Nhắm mắt / nhìn đi chỗ khác → TẠM DỪNG đồng hồ (không reset về Near để
                            // chớp mắt tự nhiên không phá cả lượt) và ngừng thu khung tới khi nhìn lại.
                            val inBand = centered && w in NEAR_MIN..NEAR_TOO_CLOSE
                            val eyesOpen = face.eyeOpen >= EYE_OPEN_MIN
                            val looking = eyesOpen && abs(face.yaw) < GAZE_MAX_YAW && abs(face.pitch) < GAZE_MAX_PITCH
                            val smiling = !smileMode || face.smile >= smileThreshold
                            when {
                                !inBand -> {
                                    stage = AimStage.Near; goodSince = 0L; holdProgress = 0f
                                    aim.currentSlot = -1
                                    aim.collect.set(false); aim.clearFrames()
                                    hint = if (w > NEAR_TOO_CLOSE) "Đang quá gần — đưa điện thoại ra xa" else "Giữ toàn bộ khuôn mặt trong khung"
                                }
                                !looking -> {
                                    // DỪNG đồng hồ: đẩy mốc bắt đầu theo nhịp vừa trôi qua → held đứng yên,
                                    // vòng tiến độ giữ nguyên. Ngừng thu khung để ảnh gửi lên chỉ có mắt-mở/thẳng.
                                    holdStart += now - holdTick
                                    aim.collect.set(false); aim.currentSlot = -1
                                    hint = if (!eyesOpen) "Mở mắt và nhìn thẳng vào camera" else "Nhìn thẳng vào camera"
                                }
                                !smiling -> {
                                    // Tạm dừng cả tiến độ và thu ảnh cho tới khi nụ cười đạt ngưỡng cấu hình.
                                    holdStart += now - holdTick
                                    aim.collect.set(false); aim.currentSlot = -1
                                    hint = "Nhìn thẳng và mỉm cười để tiếp tục"
                                }
                                else -> {
                                    aim.collect.set(true)
                                    val held = now - holdStart
                                    holdProgress = (held.toFloat() / HOLD_MS).coerceIn(0f, 1f)
                                    aim.currentSlot = -1
                                    val remain = ((HOLD_MS - held) / 1000f).toInt() + 1
                                    hint = "Giữ yên • còn ${remain.coerceAtLeast(1)} giây"
                                    if (held >= HOLD_MS) {
                                        val frames = aim.snapshot()
                                        aim.collect.set(false); aim.currentSlot = -1
                                        if (frames.isNotEmpty()) { onCapturedNow(frames); return@LaunchedEffect }
                                        else {
                                            stage = AimStage.Near; goodSince = 0L; holdProgress = 0f
                                            hint = "Chưa bắt được ảnh, thử lại"
                                        }
                                    }
                                }
                            }
                            holdTick = now
                        }
                    }
                }
            }

            if (now - startedAt > AIM_TIMEOUT_MS) {
                aim.collect.set(false)
                onCancelNow()
                return@LaunchedEffect
            }
            delay(POLL_MS)
        }
    }

    Box(modifier = modifier) {
        AndroidView(factory = { previewView }, modifier = Modifier.fillMaxSize())
        if (capturing) {
            AttendanceCameraGuide(
                stage = stage,
                progress = holdProgress,
                hint = hint,
                motionMode = motionMode,
                modifier = Modifier.fillMaxSize(),
            )
        }
    }
}

/**
 * Lớp hướng dẫn cố định của camera chấm công. Khung bốn góc luôn nằm cùng một vị trí, không bám theo
 * hộp khuôn mặt ML Kit; nhờ vậy người dùng luôn biết phải căn vào đâu mà không thấy lưới MediaPipe.
 */
@Composable
private fun AttendanceCameraGuide(
    stage: AimStage,
    progress: Float,
    hint: String,
    motionMode: Boolean,
    modifier: Modifier = Modifier,
) {
    val step = when (stage) {
        AimStage.Far -> 1
        AimStage.Near -> 2
        AimStage.Hold -> 3
    }
    val title = when (stage) {
        AimStage.Far -> "Căn vị trí khuôn mặt"
        AimStage.Near -> "Điều chỉnh khoảng cách"
        AimStage.Hold -> if (motionMode) "Kiểm tra chuyển động" else "Xác thực khuôn mặt"
    }
    val guideColor = if (stage == AimStage.Hold) Success else Color.White

    Box(modifier = modifier) {
        Canvas(Modifier.fillMaxSize()) {
            val guideWidth = minOf(size.width * 0.74f, size.height * 0.42f)
            val guideHeight = guideWidth * 1.30f
            val left = (size.width - guideWidth) / 2f
            val top = size.height * 0.43f - guideHeight / 2f
            val right = left + guideWidth
            val bottom = top + guideHeight
            val dim = Color.Black.copy(alpha = 0.44f)

            // Làm dịu vùng ngoài khung chữ nhật; phần giữa giữ nguyên camera để khuôn mặt luôn rõ.
            drawRect(dim, topLeft = Offset.Zero, size = Size(size.width, top.coerceAtLeast(0f)))
            drawRect(dim, topLeft = Offset(0f, bottom), size = Size(size.width, (size.height - bottom).coerceAtLeast(0f)))
            drawRect(dim, topLeft = Offset(0f, top), size = Size(left.coerceAtLeast(0f), guideHeight))
            drawRect(dim, topLeft = Offset(right, top), size = Size((size.width - right).coerceAtLeast(0f), guideHeight))

            val radius = 26.dp.toPx()
            drawRoundRect(
                color = Color.White.copy(alpha = 0.28f),
                topLeft = Offset(left, top),
                size = Size(guideWidth, guideHeight),
                cornerRadius = CornerRadius(radius, radius),
                style = Stroke(width = 1.2.dp.toPx()),
            )

            // Chỉ vẽ bốn góc cố định thay vì khung/lưới chạy theo khuôn mặt.
            val corner = 40.dp.toPx()
            val stroke = 4.dp.toPx()
            fun cornerLine(a: Offset, b: Offset) = drawLine(guideColor, a, b, strokeWidth = stroke, cap = StrokeCap.Round)
            cornerLine(Offset(left, top + corner), Offset(left, top + radius))
            cornerLine(Offset(left + radius, top), Offset(left + corner, top))
            cornerLine(Offset(right - corner, top), Offset(right - radius, top))
            cornerLine(Offset(right, top + radius), Offset(right, top + corner))
            cornerLine(Offset(left, bottom - corner), Offset(left, bottom - radius))
            cornerLine(Offset(left + radius, bottom), Offset(left + corner, bottom))
            cornerLine(Offset(right - corner, bottom), Offset(right - radius, bottom))
            cornerLine(Offset(right, bottom - corner), Offset(right, bottom - radius))

            if (progress > 0f) {
                val y = bottom + 12.dp.toPx()
                drawLine(Color.White.copy(alpha = 0.28f), Offset(left, y), Offset(right, y), strokeWidth = 3.dp.toPx(), cap = StrokeCap.Round)
                drawLine(Success, Offset(left, y), Offset(left + guideWidth * progress.coerceIn(0f, 1f), y), strokeWidth = 4.dp.toPx(), cap = StrokeCap.Round)
            }
        }

        Column(
            modifier = Modifier
                .align(Alignment.TopCenter)
                .statusBarsPadding()
                .padding(top = 18.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(
                "CHẤM CÔNG KHUÔN MẶT",
                color = Color.White,
                style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.ExtraBold,
            )
            Text(
                "Giữ điện thoại ngang tầm mắt",
                color = Color.White.copy(alpha = 0.72f),
                style = MaterialTheme.typography.bodySmall,
            )
        }

        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .navigationBarsPadding()
                .padding(start = 16.dp, end = 16.dp, bottom = 18.dp)
                .fillMaxWidth()
                .clip(RoundedCornerShape(24.dp))
                .background(Color(0xE61B1D22))
                .padding(horizontal = 20.dp, vertical = 18.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                repeat(3) { index ->
                    Box(
                        Modifier
                            .width(34.dp)
                            .height(4.dp)
                            .clip(CircleShape)
                            .background(if (index < step) guideColor else Color.White.copy(alpha = 0.22f)),
                    )
                }
            }
            Text(
                "BƯỚC $step/3  •  $title",
                color = guideColor,
                style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.Bold,
                textAlign = TextAlign.Center,
            )
            Text(
                hint,
                color = Color.White,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold,
                textAlign = TextAlign.Center,
            )
            Text(
                "Giữ đủ sáng • Không che khuôn mặt",
                color = Color.White.copy(alpha = 0.64f),
                style = MaterialTheme.typography.bodySmall,
                textAlign = TextAlign.Center,
            )
        }
    }
}

/** Giữ màn hình thức và sáng tối đa trong phiên camera; thoát camera sẽ trả đúng trạng thái cũ. */
@Composable
internal fun KeepScreenBrightEffect() {
    val window = LocalContext.current.findActivity()?.window
    DisposableEffect(window) {
        if (window == null) {
            onDispose { }
        } else {
            val previousBrightness = window.attributes.screenBrightness
            val wasKeepingScreenOn =
                window.attributes.flags and WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON != 0
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            window.attributes = window.attributes.apply { screenBrightness = 1f }

            onDispose {
                runCatching { window.attributes = window.attributes.apply { screenBrightness = previousBrightness } }
                if (!wasKeepingScreenOn) runCatching { window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) }
            }
        }
    }
}

/** Tìm Activity chứa để chỉnh độ sáng cửa sổ. */
internal fun Context.findActivity(): Activity? {
    var ctx: Context? = this
    while (ctx is ContextWrapper) {
        if (ctx is Activity) return ctx
        ctx = ctx.baseContext
    }
    return null
}

/** Chuyển 1 khung camera (YUV_420_888) thành data URL JPEG đã xoay đúng chiều. */
internal fun ImageProxy.toJpegDataUrl(quality: Int = 80): String {
    val nv21 = toNv21()
    val yuv = YuvImage(nv21, ImageFormat.NV21, width, height, null)
    val jpegOut = ByteArrayOutputStream()
    yuv.compressToJpeg(Rect(0, 0, width, height), 92, jpegOut)
    val raw = jpegOut.toByteArray()
    var bmp = BitmapFactory.decodeByteArray(raw, 0, raw.size)
    val rotation = imageInfo.rotationDegrees
    if (rotation != 0) {
        val source = bmp
        val m = Matrix().apply { postRotate(rotation.toFloat()) }
        bmp = Bitmap.createBitmap(source, 0, 0, source.width, source.height, m, true)
        if (bmp !== source && !source.isRecycled) source.recycle()
    }
    val out = ByteArrayOutputStream()
    bmp.compress(Bitmap.CompressFormat.JPEG, quality, out)
    bmp.recycle()
    return "data:image/jpeg;base64," + Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP)
}

/** YUV_420_888 → NV21 (xử lý đúng rowStride/pixelStride của từng plane). */
internal fun ImageProxy.toNv21(): ByteArray {
    val nv21 = ByteArray(width * height * 3 / 2)
    val yPlane = planes[0]
    val uPlane = planes[1]
    val vPlane = planes[2]

    var pos = 0
    val yBuffer = yPlane.buffer
    val yRowStride = yPlane.rowStride
    val yPixelStride = yPlane.pixelStride
    for (row in 0 until height) {
        var yIndex = row * yRowStride
        for (col in 0 until width) {
            nv21[pos++] = yBuffer.get(yIndex)
            yIndex += yPixelStride
        }
    }

    val uBuffer = uPlane.buffer
    val vBuffer = vPlane.buffer
    val uRowStride = uPlane.rowStride
    val uPixelStride = uPlane.pixelStride
    val vRowStride = vPlane.rowStride
    val vPixelStride = vPlane.pixelStride
    val chromaHeight = height / 2
    val chromaWidth = width / 2
    for (row in 0 until chromaHeight) {
        var vIndex = row * vRowStride
        var uIndex = row * uRowStride
        for (col in 0 until chromaWidth) {
            nv21[pos++] = vBuffer.get(vIndex)
            nv21[pos++] = uBuffer.get(uIndex)
            vIndex += vPixelStride
            uIndex += uPixelStride
        }
    }
    return nv21
}
