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
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
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
private const val HOLD_MS = 3000L          // quét giữ khung 3 giây (kèm soi sáng)
private const val MOTION_CENTER_YAW = 12f  // |yaw| < mức này = coi như nhìn thẳng (độ)
private const val MOTION_TURN_YAW = 20f    // |yaw| > mức này = đã quay đủ sang một bên (độ)
private const val MOTION_FRONTAL_MS = 350L // giữ nhìn thẳng ngần này trước khi yêu cầu quay
private const val POLL_MS = 40L
private const val FACE_STALE_MS = 350L     // không thấy mặt quá lâu = coi như rời khung
private const val AIM_TIMEOUT_MS = 45_000L // không căn được khung đủ lâu → tự huỷ để thử lại
private const val MAX_FRAMES = 14          // trần số khung gửi lên khi KHÔNG chạy flash (giữ khung mới nhất)
private const val PER_SLOT_CAP = 3         // khi chạy flash: tối đa số khung mỗi ô màu (phủ đủ mọi slot)

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

        capture is AttendanceCapture.Recognizing ->
            AttendanceStatusScreen("Đang so khớp khuôn mặt…")

        capture is AttendanceCapture.AwaitingConfirm -> ConfirmScreen(
            result = capture.result,
            onConfirm = vm::confirmAttendance,
            onRescan = vm::rescanAttendance,
            onClose = vm::resetCapture,
        )

        capture is AttendanceCapture.Submitting ->
            AttendanceStatusScreen("Đang xử lý…")

        capture is AttendanceCapture.Done -> ResultScreen(
            result = capture.result,
            onRescan = vm::rescanAttendance,
            onClose = vm::resetCapture,
            onExplain = { vm.startAttendanceExplanation(capture.result) },
        )

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
 * Bước quét: camera TOÀN MÀN HÌNH thật sự — ẩn cả thanh trạng thái lẫn thanh điều hướng hệ thống
 * (immersive, vuốt để hiện tạm) để chỉ còn camera + nút Đóng. Được gọi ở tầng ngoài Scaffold
 * (xem HrShell) nên không dính thanh tiêu đề/điều hướng của app. Chỉ hiện khi đang Collecting.
 */
@Composable
fun FullScreenCameraScan(
    onCaptured: (List<CapturedFrame>) -> Unit,
    onCancel: () -> Unit,
    motionMode: Boolean = false,
) {
    val context = LocalContext.current
    // Ẩn thanh hệ thống trong lúc quét để camera phủ kín màn hình; khôi phục khi thoát.
    DisposableEffect(Unit) {
        val window = context.findActivity()?.window
        val controller = window?.let { WindowCompat.getInsetsController(it, it.decorView) }
        controller?.apply {
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            hide(WindowInsetsCompat.Type.systemBars())
        }
        onDispose { controller?.show(WindowInsetsCompat.Type.systemBars()) }
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
        )
        IconButton(
            onClick = onCancel,
            modifier = Modifier
                .align(Alignment.TopStart)
                .statusBarsPadding() // vẫn bấm được nếu người dùng vuốt hiện thanh trạng thái
                .padding(8.dp),
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
            "Sẵn sàng xác thực khuôn mặt",
            color = Color.White,
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold,
            textAlign = TextAlign.Center,
        )
        Text(
            "Bấm \"Bắt đầu chấm công\", giữ máy ngang tầm mắt ở khoảng cách thoải mái. Đưa khuôn mặt vào khung lớn rồi khung nhỏ — không cần đưa mặt quá gần.",
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

/**
 * Màn hình XÁC NHẬN RIÊNG (không đè lên camera): hiện "Nhân viên / Giờ vào (hoặc Giờ ra)" trên nền
 * ứng dụng, dưới có nút Xác nhận (ghi công thật) và Quét lại. Nút Đóng ở góc để thoát về màn chờ.
 */
@Composable
private fun ConfirmScreen(
    result: ChamCongResult,
    onConfirm: () -> Unit,
    onRescan: () -> Unit,
    onClose: () -> Unit,
) {
    val isOut = result.loai.equals("Ra", true)
    val timeLabel = if (isOut) "Giờ ra" else "Giờ vào"
    val timeIcon = if (isOut) Icons.AutoMirrored.Filled.Logout else Icons.AutoMirrored.Filled.Login
    val accent = MaterialTheme.colorScheme.primary
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .padding(20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onClose) {
                Icon(Icons.Filled.Close, contentDescription = "Đóng", tint = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Spacer(Modifier.weight(1f))
        }

        Spacer(Modifier.weight(1f))
        Box(
            modifier = Modifier
                .size(84.dp)
                .clip(CircleShape)
                .background(accent.copy(alpha = 0.16f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Filled.CheckCircle, contentDescription = null, tint = accent, modifier = Modifier.size(52.dp))
        }
        Spacer(Modifier.height(14.dp))
        Text("Xác nhận chấm công", color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.ExtraBold)
        Spacer(Modifier.height(4.dp))
        Text("Vui lòng kiểm tra thông tin trước khi ghi công", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.Center)

        Spacer(Modifier.height(20.dp))
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(18.dp))
                .background(MaterialTheme.colorScheme.surfaceVariant)
                .padding(18.dp),
        ) {
            Text("Nhân viên", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.height(2.dp))
            Text(
                result.fullName?.takeIf { it.isNotBlank() } ?: result.username ?: "--",
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface,
            )
            Spacer(Modifier.height(14.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    modifier = Modifier
                        .size(40.dp)
                        .clip(CircleShape)
                        .background(accent.copy(alpha = 0.15f)),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(timeIcon, contentDescription = null, tint = accent, modifier = Modifier.size(22.dp))
                }
                Spacer(Modifier.width(10.dp))
                Column {
                    Text(timeLabel, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(
                        formatIsoTimeLocal(result.occurredAt),
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface,
                    )
                }
            }
        }
        if (result.message.isNotBlank()) {
            Spacer(Modifier.height(10.dp))
            Text(result.message, color = Warning, style = MaterialTheme.typography.bodySmall, textAlign = TextAlign.Center, maxLines = 3, overflow = TextOverflow.Ellipsis)
        }

        Spacer(Modifier.weight(1f))
        Button(
            onClick = onConfirm,
            modifier = Modifier
                .fillMaxWidth()
                .height(52.dp),
            shape = RoundedCornerShape(16.dp),
        ) {
            Icon(Icons.Filled.CheckCircle, contentDescription = null, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(8.dp))
            Text("Xác nhận", fontWeight = FontWeight.Bold)
        }
        Spacer(Modifier.height(10.dp))
        OutlinedButton(
            onClick = onRescan,
            modifier = Modifier
                .fillMaxWidth()
                .height(50.dp),
            shape = RoundedCornerShape(16.dp),
        ) {
            Icon(Icons.Filled.Refresh, contentDescription = null, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(8.dp))
            Text("Quét lại", fontWeight = FontWeight.Bold)
        }
    }
}

/** Màn hình KẾT QUẢ RIÊNG sau khi ghi công (hoặc lỗi nhận diện): nền ứng dụng + nút Quét lại / Đóng. */
@Composable
private fun ResultScreen(result: ChamCongResult, onRescan: () -> Unit, onClose: () -> Unit, onExplain: () -> Unit) {
    val (tone, title, icon) = attendanceVisual(result.status)
    val color = when (tone) {
        Tone.Success -> Success
        Tone.Warning -> Warning
        else -> Danger
    }
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .padding(20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Spacer(Modifier.weight(1f))
        Box(
            modifier = Modifier
                .size(88.dp)
                .clip(CircleShape)
                .background(color.copy(alpha = 0.16f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, contentDescription = null, tint = color, modifier = Modifier.size(52.dp))
        }
        Spacer(Modifier.height(14.dp))
        Text(title, color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.ExtraBold, textAlign = TextAlign.Center)
        if (result.status.equals("ok", true) && !result.fullName.isNullOrBlank()) {
            Spacer(Modifier.height(6.dp))
            Text(result.fullName, color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.titleMedium, textAlign = TextAlign.Center)
            val meta = buildString {
                result.loai?.takeIf { it.isNotBlank() }?.let { append(if (it.equals("Ra", true)) "Giờ ra" else "Giờ vào") }
                val time = formatIsoTimeLocal(result.occurredAt)
                if (time != "--") { if (isNotEmpty()) append(": "); append(time) }
            }
            if (meta.isNotBlank()) Text(meta, color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodyMedium)
        } else {
            Spacer(Modifier.height(8.dp))
            Text(
                result.guidance?.takeIf { it.isNotBlank() } ?: result.message.ifBlank { "Vui lòng thử lại." },
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodyMedium,
                textAlign = TextAlign.Center,
                maxLines = 4,
                overflow = TextOverflow.Ellipsis,
            )
        }

        Spacer(Modifier.weight(1f))
        Button(
            onClick = onRescan,
            modifier = Modifier
                .fillMaxWidth()
                .height(52.dp),
            shape = RoundedCornerShape(16.dp),
        ) {
            Icon(Icons.Filled.Refresh, contentDescription = null, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(8.dp))
            Text("Quét lại", fontWeight = FontWeight.Bold)
        }
        Spacer(Modifier.height(10.dp))
        if (!result.status.equals("ok", true) && !result.status.equals("offline", true)) {
            OutlinedButton(onClick = onExplain, modifier = Modifier.fillMaxWidth()) { Text("Gửi giải trình kèm ảnh/tài liệu") }
        }
        OutlinedButton(
            onClick = onClose,
            modifier = Modifier
                .fillMaxWidth()
                .height(50.dp),
            shape = RoundedCornerShape(16.dp),
        ) {
            Text("Đóng", fontWeight = FontWeight.Bold)
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
    "lowquality" -> Triple(Tone.Warning, "Ảnh chưa đủ rõ", Icons.Filled.WarningAmber)
    "noface" -> Triple(Tone.Warning, "Không thấy khuôn mặt", Icons.Filled.WarningAmber)
    "spoof" -> Triple(Tone.Danger, "Nghi ngờ giả mạo", Icons.Filled.ErrorOutline)
    "proxy" -> Triple(Tone.Danger, "Không phải tài khoản của bạn", Icons.Filled.ErrorOutline)
    else -> Triple(Tone.Danger, "Chưa nhận diện được", Icons.Filled.ErrorOutline)
}

// ── Nơi giữ quan sát khuôn mặt mới nhất + bộ đệm khung để gửi lên ─────────────
private data class FaceObs(
    val cx: Float,        // tâm mặt theo trục X (0..1) trong ảnh đã xoay
    val cy: Float,        // tâm mặt theo trục Y (0..1)
    val widthFrac: Float, // bề rộng mặt / bề rộng ảnh (to hơn = ở gần hơn)
    val yaw: Float,       // góc quay đầu trái/phải (độ, từ ML Kit headEulerAngleY) — cho liveness quay đầu
    val t: Long,          // mốc thời gian (elapsedRealtime) để biết khung còn "tươi"
)

private class FaceAimState {
    @Volatile var latest: FaceObs? = null
    // Ô màu flash đang chiếu lúc thu khung (-1 = không chạy flash liveness). Vòng lặp giữ khung cập nhật.
    @Volatile var currentSlot: Int = -1
    val collect = AtomicBoolean(false)
    private val frames = ArrayList<CapturedFrame>()

    fun addFrame(url: String) = synchronized(frames) {
        val slot = currentSlot
        if (slot < 0) {
            // Khung TỰ NHIÊN (không flash, hoặc đuôi sau chuỗi màu để nhận diện): giữ tối đa MAX_FRAMES
            // khung tự nhiên MỚI NHẤT — chỉ loại khung tự nhiên cũ, KHÔNG đụng khung flash (slot ≥ 0)
            // đang cần cho việc đối chiếu phản xạ.
            while (frames.count { it.slot < 0 } >= MAX_FRAMES) {
                val i = frames.indexOfFirst { it.slot < 0 }
                if (i < 0) break
                frames.removeAt(i)
            }
            frames.add(CapturedFrame(url, -1))
        } else {
            // Khung FLASH: giới hạn số khung MỖI slot để không slot nào lấn át, phủ đủ mọi màu.
            if (frames.count { it.slot == slot } >= PER_SLOT_CAP) return@synchronized
            frames.add(CapturedFrame(url, slot))
        }
    }
    fun snapshot(): List<CapturedFrame> = synchronized(frames) { frames.toList() }
    fun clearFrames() = synchronized(frames) { frames.clear() }
}

/**
 * CameraX (camera trước) + ML Kit nhận diện on-device để căn khung 2 bước (khung lớn → khung nhỏ,
 * không đòi đưa mặt quá sát), quét giữ khung 3 giây có SOI SÁNG bằng cách đổi màu dịu trên màn hình,
 * gom khung ngay trong lúc giữ rồi trả về loạt ảnh qua [onCaptured]. [onCancel] khi hết thời gian căn.
 */
@androidx.annotation.OptIn(ExperimentalGetImage::class)
@Composable
fun BiometricFaceCamera(
    capturing: Boolean,
    modifier: Modifier = Modifier,
    onCaptured: (List<CapturedFrame>) -> Unit,
    onCancel: () -> Unit,
    motionMode: Boolean = false,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val previewView = remember { PreviewView(context).apply { scaleType = PreviewView.ScaleType.FILL_CENTER } }
    val executor = remember { Executors.newSingleThreadExecutor() }
    // Callback ML Kit chạy trên MAIN executor (không bao giờ bị tắt) để tránh RejectedExecutionException
    // khi màn hình camera bị hủy lúc executor nền đã shutdown → tránh crash.
    val mainExecutor = remember { ContextCompat.getMainExecutor(context) }
    val aim = remember { FaceAimState() }
    val onCapturedNow by rememberUpdatedState(onCaptured)
    val onCancelNow by rememberUpdatedState(onCancel)
    val detector = remember {
        FaceDetection.getClient(
            FaceDetectorOptions.Builder()
                .setPerformanceMode(FaceDetectorOptions.PERFORMANCE_MODE_FAST)
                .setMinFaceSize(0.15f)
                .build(),
        )
    }

    // Trạng thái UI do vòng lặp căn khung điều khiển (đọc/ghi trên luồng chính).
    var stage by remember { mutableStateOf(AimStage.Far) }
    var hint by remember { mutableStateOf("Đưa khuôn mặt vào khung lớn") }
    var holdProgress by remember { mutableStateOf(0f) }
    // Màu SOI SÁNG hắt ra VÙNG NGOÀI khung (phần đen quanh khuôn mặt) trong lúc quét — KHÔNG phủ lên
    // mặt. null = không soi (chưa tới bước giữ khung). Xem [FaceGuideOverlay].
    var lightColor by remember { mutableStateOf<Color?>(null) }
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
                        // Bọc toàn bộ để MỘT lỗi bất kỳ (ML Kit/giải mã ảnh) không làm crash cả app và
                        // ImageProxy luôn được đóng đúng một lần (nếu không đóng, camera sẽ treo khung).
                        try {
                            // Thu khung NGAY trên luồng nền (khi đang ở pha giữ) — tách khỏi callback ML Kit.
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
                                            FaceObs(
                                                cx = (bb.exactCenterX() / iw).coerceIn(0f, 1f),
                                                cy = (bb.exactCenterY() / ih).coerceIn(0f, 1f),
                                                widthFrac = (bb.width().toFloat() / iw).coerceIn(0f, 1f),
                                                yaw = face.headEulerAngleY,
                                                t = SystemClock.elapsedRealtime(),
                                            )
                                        } else {
                                            null
                                        }
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
            // Gỡ analyzer TRƯỚC để CameraX chắc chắn không đẩy thêm khung nào lên executor sắp tắt,
            // rồi mới unbind + đóng detector + shutdown (tất cả bọc runCatching cho an toàn tuyệt đối).
            runCatching { analysisRef.getAndSet(null)?.clearAnalyzer() }
            runCatching { ProcessCameraProvider.getInstance(context).get().unbindAll() }
            runCatching { detector.close() }
            runCatching { executor.shutdown() }
        }
    }

    // Tăng độ sáng màn hình tối đa trong lúc quét để soi sáng khuôn mặt (khôi phục khi xong).
    DisposableEffect(capturing) {
        val window = context.findActivity()?.window
        val prev = window?.attributes?.screenBrightness
        if (capturing && window != null) {
            window.attributes = window.attributes.apply { screenBrightness = 1f }
        }
        onDispose {
            if (window != null && prev != null) {
                window.attributes = window.attributes.apply { screenBrightness = prev }
            }
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
        lightColor = null
        hint = "Đưa khuôn mặt vào khung lớn"

        var goodSince = 0L
        var holdStart = 0L
        val startedAt = SystemClock.elapsedRealtime()

        // Trạng thái pha giữ khung khi bật liveness QUAY ĐẦU (motionMode):
        // 0 = nhìn thẳng, 1 = quay sang một bên, 2 = quay sang bên còn lại, 3 = xong.
        var motionStep = 0
        var sideASign = 0        // dấu yaw của lần quay đầu tiên (ép lần sau quay NGƯỢC lại)
        var frontalSince = 0L

        while (isActive) {
            val now = SystemClock.elapsedRealtime()
            val face = aim.latest?.takeIf { now - it.t < FACE_STALE_MS }

            if (face == null) {
                goodSince = 0L
                if (stage == AimStage.Hold) { stage = AimStage.Near; aim.collect.set(false); aim.clearFrames() }
                holdProgress = 0f
                lightColor = null
                aim.currentSlot = -1
                motionStep = 0; frontalSince = 0L
                hint = "Đưa khuôn mặt vào giữa khung"
            } else {
                val centered = abs(face.cx - 0.5f) < CENTER_TOL_X && abs(face.cy - 0.5f) < CENTER_TOL_Y
                val w = face.widthFrac
                when (stage) {
                    AimStage.Far -> when {
                        !centered -> { hint = "Đưa khuôn mặt vào giữa khung"; goodSince = 0L }
                        w < FAR_MIN -> { hint = "Nhích lại gần khung một chút"; goodSince = 0L }
                        w > FAR_MAX -> { hint = "Lùi ra xa một chút"; goodSince = 0L }
                        else -> {
                            if (goodSince == 0L) goodSince = now
                            if (now - goodSince >= STAGE_STABLE_MS) {
                                stage = AimStage.Near; goodSince = 0L
                            }
                            hint = "Giữ khuôn mặt trong khung lớn…"
                        }
                    }
                    AimStage.Near -> when {
                        w > NEAR_TOO_CLOSE -> { hint = "Quá gần — lùi ra xa để bảo vệ mắt"; goodSince = 0L }
                        !centered -> { hint = "Đưa khuôn mặt vào giữa khung nhỏ"; goodSince = 0L }
                        w < NEAR_MIN -> { hint = "Nhích lại gần thêm một chút"; goodSince = 0L }
                        else -> {
                            if (goodSince == 0L) goodSince = now
                            if (now - goodSince >= STAGE_STABLE_MS) {
                                stage = AimStage.Hold; holdStart = now
                                aim.clearFrames(); aim.collect.set(true)
                                motionStep = 0; sideASign = 0; frontalSince = 0L
                            }
                            hint = "Giữ yên trong khung nhỏ…"
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
                                aim.currentSlot = -1; motionStep = 0; frontalSince = 0L
                                aim.collect.set(false); aim.clearFrames()
                                hint = if (tooClose) "Quá gần — lùi ra xa" else "Giữ khuôn mặt trong khung"
                            } else {
                                val yaw = face.yaw
                                aim.collect.set(true)
                                when (motionStep) {
                                    0 -> {
                                        aim.currentSlot = 0; holdProgress = 0f
                                        hint = "Nhìn thẳng vào camera"
                                        if (abs(yaw) < MOTION_CENTER_YAW) {
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
                                        hint = "Rồi quay sang bên còn lại"
                                        // Bắt buộc quay NGƯỢC dấu lần đầu, đủ mạnh → đã phủ cả hai bên.
                                        if (yaw * sideASign < -MOTION_TURN_YAW) motionStep = 3
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
                            // ── Giữ khung tĩnh 3 giây (như cũ) ──
                            val inBand = centered && w in NEAR_MIN..NEAR_TOO_CLOSE
                            if (!inBand) {
                                stage = AimStage.Near; goodSince = 0L; holdProgress = 0f
                                lightColor = null; aim.currentSlot = -1
                                aim.collect.set(false); aim.clearFrames()
                                hint = if (w > NEAR_TOO_CLOSE) "Quá gần — lùi ra xa" else "Giữ khuôn mặt trong khung"
                            } else {
                                val held = now - holdStart
                                holdProgress = (held.toFloat() / HOLD_MS).coerceIn(0f, 1f)
                                aim.currentSlot = -1
                                lightColor = softFlashColor(held)
                                val remain = ((HOLD_MS - held) / 1000f).toInt() + 1
                                hint = "Đang quét khuôn mặt… ${remain.coerceAtLeast(1)}s"
                                if (held >= HOLD_MS) {
                                    val frames = aim.snapshot()
                                    aim.collect.set(false); lightColor = null; aim.currentSlot = -1
                                    if (frames.isNotEmpty()) { onCapturedNow(frames); return@LaunchedEffect }
                                    else {
                                        stage = AimStage.Near; goodSince = 0L; holdProgress = 0f
                                        hint = "Chưa bắt được ảnh, thử lại"
                                    }
                                }
                            }
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
            // Soi sáng KHÔNG phủ lên mặt: màu sáng chỉ hắt ra VÙNG NGOÀI khung (lightColor) — xem overlay.
            FaceGuideOverlay(stage = stage, holdProgress = holdProgress, lightColor = lightColor)
            Text(
                hint,
                color = Color.White,
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.SemiBold,
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .navigationBarsPadding()
                    .padding(bottom = 20.dp, start = 20.dp, end = 20.dp),
            )
        } else {
            // Khung tĩnh gợi ý vùng đặt mặt khi chưa quét.
            FaceGuideOverlay(stage = AimStage.Far, holdProgress = 0f, idle = true)
        }
    }
}

/**
 * Vẽ khung ngắm bầu dục (lớn ở bước Far, nhỏ ở bước Near/Hold) + vòng tiến độ khi quét 3 giây, kèm
 * hiệu ứng "đóng dần": khi mặt đã vào khung (Near → Hold) thì thu hẹp vùng camera xung quanh, chỉ
 * chừa lại đúng ô khuôn mặt (khoét lỗ bầu dục bằng Path + PathFillType.EvenOdd, an toàn trên mọi GPU).
 *
 * SOI SÁNG: vùng NGOÀI khung (phần quanh khuôn mặt) là nơi hắt ánh sáng — KHÔNG phủ màu lên mặt.
 * Khi đang quét ([lightColor] != null) vùng này phát màu sáng để hắt thêm sáng vào mặt lúc thiếu sáng;
 * lúc chưa quét thì tối lại như cũ để làm nổi khuôn mặt.
 */
@Composable
private fun FaceGuideOverlay(
    stage: AimStage,
    holdProgress: Float,
    idle: Boolean = false,
    lightColor: Color? = null,
) {
    val targetFrac = if (stage == AimStage.Far) 0.82f else 0.60f
    val ovalFrac by animateFloatAsState(targetFrac, tween(450), label = "oval")
    val targetScrim = when {
        idle -> 0f
        stage == AimStage.Far -> 0f      // còn đang căn khung lớn → chưa che
        stage == AimStage.Near -> 0.55f  // mặt đã vào khung → bắt đầu đóng dần
        else -> 0.9f                     // Hold: gần như chỉ còn thấy ô khuôn mặt
    }
    val scrim by animateFloatAsState(targetScrim, tween(500), label = "scrim")
    val ring = when {
        idle -> Color.White.copy(alpha = 0.35f)
        stage == AimStage.Hold -> Success
        else -> Color.White.copy(alpha = 0.9f)
    }
    Canvas(modifier = Modifier.fillMaxSize()) {
        val cw = size.width
        val ch = size.height
        val ovalW = cw * ovalFrac
        val ovalH = ovalW / 0.78f
        val left = (cw - ovalW) / 2f
        val top = (ch - ovalH) / 2f
        val topLeft = Offset(left, top)
        val ovalSize = Size(ovalW, ovalH)
        // Phủ vùng NGOÀI bầu dục NHƯNG chừa lỗ bầu dục ở giữa (mặt): vẽ path (chữ nhật trừ bầu dục theo
        // quy tắc EvenOdd) → an toàn trên mọi GPU, không cần lớp vẽ offscreen. Đang quét → tô màu SÁNG
        // (soi sáng), chưa quét → tô đen (làm nổi mặt).
        val outside = lightColor ?: (if (scrim > 0f) Color.Black.copy(alpha = scrim) else null)
        if (outside != null) {
            val mask = Path().apply {
                addRect(androidx.compose.ui.geometry.Rect(0f, 0f, cw, ch))
                addOval(androidx.compose.ui.geometry.Rect(left, top, left + ovalW, top + ovalH))
                fillType = PathFillType.EvenOdd
            }
            drawPath(mask, color = outside)
        }
        drawOval(color = ring, topLeft = topLeft, size = ovalSize, style = Stroke(width = 6.dp.toPx()))
        if (stage == AimStage.Hold && holdProgress > 0f) {
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

/**
 * Màu SOI SÁNG hắt ra vùng NGOÀI khung (quanh khuôn mặt), đổi liên tục theo thời gian giữ khung.
 * Dùng màu sáng + độ mờ cao để thực sự hắt thêm sáng vào mặt khi môi trường thiếu sáng (không phủ
 * lên mặt nên không làm ám màu ảnh chấm công).
 */
private fun softFlashColor(heldMs: Long): Color {
    val palette = listOf(
        Color(0xFFFFFFFF), // trắng
        Color(0xFFFFF3E0), // trắng ấm
        Color(0xFFE3F2FD), // xanh nhạt
        Color(0xFFFFF9C4), // vàng nhạt
        Color(0xFFFCE4EC), // hồng nhạt
    )
    val idx = ((heldMs / 450L) % palette.size).toInt()
    return palette[idx].copy(alpha = 0.92f)
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
        val m = Matrix().apply { postRotate(rotation.toFloat()) }
        bmp = Bitmap.createBitmap(bmp, 0, 0, bmp.width, bmp.height, m, true)
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
