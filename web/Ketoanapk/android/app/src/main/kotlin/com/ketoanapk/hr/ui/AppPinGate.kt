package com.ketoanapk.hr.ui

import android.os.Build
import android.os.SystemClock
import android.view.HapticFeedbackConstants
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Backspace
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Shield
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ketoanapk.hr.data.APP_PIN_LENGTH
import com.ketoanapk.hr.data.AppPinVerification
import com.ketoanapk.hr.data.HrRepository
import com.ketoanapk.hr.ui.theme.Danger
import com.ketoanapk.hr.ui.theme.Success
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.ceil

enum class AppPinGateMode { Unlock, Manage }

private enum class AppPinPhase { Loading, Unlock, Create, Confirm, Recover, Unavailable }

/**
 * Bottom sheet PIN dùng chung cho mọi dữ liệu nhạy cảm. PIN được nhập hoàn toàn bằng bàn phím số tự
 * vẽ; không mở bàn phím hệ thống và không dùng ô nhập văn bản cho mã bảo mật.
 *
 * MÃ NẰM Ở MÁY CHỦ, THIẾT BỊ KHÔNG GIỮ BẢN SAO NÀO. Màn hình này chỉ thu mã rồi hỏi máy chủ
 * (`/api/auth/app-pin/...`); hash, bộ đếm sai và thời gian khoá đều do máy chủ giữ. Vì thế:
 *  • máy bị mất/bị lấy bản sao lưu cũng không mang theo thứ gì để dò ngoại tuyến 10^6 mã;
 *  • xoá dữ liệu app hay cài lại app KHÔNG reset được số lần thử sai;
 *  • đổi mã trên một máy là mọi máy của tài khoản đều theo mã mới.
 * Đánh đổi: mở khoá phải có mạng — dữ liệu sau lớp khoá (phiếu lương, hồ sơ) vốn cũng tải từ máy chủ.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AppPinGate(
    visible: Boolean,
    username: String,
    purpose: String,
    onDismiss: () -> Unit,
    onUnlocked: () -> Unit,
    mode: AppPinGateMode = AppPinGateMode.Unlock,
) {
    if (!visible) return

    val context = LocalContext.current
    val view = LocalView.current
    val repo = remember(context) { HrRepository.foreground(context) }
    val scope = rememberCoroutineScope()
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    var phase by remember(visible, username, mode) { mutableStateOf(AppPinPhase.Loading) }
    var pin by remember(visible, username, mode) { mutableStateOf("") }
    var firstPin by remember(visible, username, mode) { mutableStateOf<String?>(null) }
    // Mã CŨ vừa được máy chủ xác nhận, giữ tạm trong RAM để gửi kèm khi đặt mã mới (máy chủ bắt buộc
    // có mã cũ mới cho đổi). Không bao giờ ghi xuống đĩa và mất ngay khi đóng bảng.
    var verifiedCurrentPin by remember(visible, username, mode) { mutableStateOf<String?>(null) }
    var password by remember(visible, username, mode) { mutableStateOf("") }
    var error by remember(visible, username, mode) { mutableStateOf<String?>(null) }
    var busy by remember(visible, username, mode) { mutableStateOf(false) }
    // Hoạt cảnh của hàng ô mã — dùng chung đúng thành phần với 5 ô OTP ở màn quên mật khẩu.
    var cellPhase by remember(visible, username, mode) { mutableStateOf(CodeCellsPhase.Idle) }
    var reloadEvent by remember(visible, username, mode) { mutableIntStateOf(0) }
    // Mốc hết khoá tính theo đồng hồ chạy-từ-lúc-khởi-động: máy chủ trả SỐ GIÂY còn lại, nên chỉnh
    // giờ máy không rút ngắn được thời gian chờ.
    var lockUntilElapsed by remember(visible, username, mode) { mutableLongStateOf(0L) }
    var clock by remember { mutableLongStateOf(SystemClock.elapsedRealtime()) }

    fun applyLock(seconds: Long) {
        lockUntilElapsed = if (seconds > 0) SystemClock.elapsedRealtime() + seconds * 1_000 else 0L
        clock = SystemClock.elapsedRealtime()
    }

    LaunchedEffect(visible, username, mode, reloadEvent) {
        phase = AppPinPhase.Loading
        error = null
        runCatching { repo.appPinStatus() }
            .onSuccess { status ->
                applyLock(status.lockedForSeconds)
                phase = if (status.hasPin) AppPinPhase.Unlock else AppPinPhase.Create
            }
            .onFailure {
                // Không hỏi được máy chủ thì KHÔNG đoán bừa là "chưa có mã" (đoán sai sẽ mời tạo mã mới
                // và ghi đè mã cũ). Báo rõ là cần kết nối, kèm nút thử lại.
                phase = AppPinPhase.Unavailable
                error = it.message ?: "Không kết nối được máy chủ."
            }
    }

    LaunchedEffect(lockUntilElapsed) {
        while (lockUntilElapsed > SystemClock.elapsedRealtime()) {
            clock = SystemClock.elapsedRealtime()
            delay(1_000)
        }
        clock = SystemClock.elapsedRealtime()
    }

    /**
     * Sai mã: rung máy rồi để hàng ô nứt–vỡ–rơi xong mới dựng lại hàng trống. Bàn phím số vẫn bị khoá
     * suốt hoạt cảnh ([busy]) để không ai gõ đè lên lúc các mảnh đang rơi.
     */
    fun reject(message: String?, onAnimationEnd: () -> Unit = {}) {
        error = message
        busy = true
        cellPhase = CodeCellsPhase.Error
        view.performHapticFeedback(
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) HapticFeedbackConstants.REJECT
            else HapticFeedbackConstants.LONG_PRESS,
        )
        scope.launch {
            delay(CODE_CELLS_ERROR_MILLIS)
            pin = ""
            cellPhase = CodeCellsPhase.Idle
            busy = false
            onAnimationEnd()
        }
    }

    /** Đúng mã: các ô thu về giữa, vòng tròn xanh nở ra và nét ✓ được vẽ, rồi mới đi tiếp. */
    fun sealAndThen(next: () -> Unit) {
        cellPhase = CodeCellsPhase.Success
        scope.launch {
            delay(CODE_CELLS_SUCCESS_MILLIS)
            cellPhase = CodeCellsPhase.Idle
            next()
        }
    }

    fun finish() {
        pin = ""
        firstPin = null
        verifiedCurrentPin = null
        busy = false
        onUnlocked()
    }

    /** Máy chủ trong LAN trả lời gần như tức thì — giữ nhịp tối thiểu để nhìn kịp hoạt cảnh chờ. */
    suspend fun holdSpinner(startedAt: Long, minimumMillis: Long) {
        val remain = minimumMillis - (System.currentTimeMillis() - startedAt)
        if (remain > 0) delay(remain)
    }

    /** Gửi mã mới lên máy chủ (kèm mã cũ nếu là đổi mã). */
    fun saveConfirmedPin(value: String) {
        scope.launch {
            busy = true
            error = null
            cellPhase = CodeCellsPhase.Verifying
            val startedAt = System.currentTimeMillis()
            val result = runCatching { repo.setAppPin(value, verifiedCurrentPin) }.getOrElse { failure ->
                holdSpinner(startedAt, 600)
                firstPin = null
                reject(failure.message ?: "Không lưu được mã bảo mật.") { phase = AppPinPhase.Create }
                return@launch
            }
            holdSpinner(startedAt, if (result is AppPinVerification.Success) 900 else 700)
            when (result) {
                AppPinVerification.Success, AppPinVerification.NotSet -> sealAndThen(::finish)
                // Mã cũ không còn đúng (bị đổi ở máy khác giữa chừng) → phải xác minh lại từ đầu.
                is AppPinVerification.Incorrect -> {
                    firstPin = null
                    verifiedCurrentPin = null
                    reject("Mã hiện tại không còn đúng. Vui lòng nhập lại mã hiện tại.") {
                        phase = AppPinPhase.Unlock
                    }
                }
                is AppPinVerification.Locked -> {
                    firstPin = null
                    verifiedCurrentPin = null
                    applyLock(result.seconds)
                    reject(null) { phase = AppPinPhase.Unlock }
                }
            }
        }
    }

    fun submitPin(value: String) {
        when (phase) {
            AppPinPhase.Create -> {
                firstPin = value
                pin = ""
                error = null
                phase = AppPinPhase.Confirm
            }
            AppPinPhase.Confirm -> {
                if (value != firstPin) {
                    firstPin = null
                    reject("Hai lần nhập chưa khớp. Vui lòng tạo mã lại.") { phase = AppPinPhase.Create }
                } else {
                    saveConfirmedPin(value)
                }
            }
            AppPinPhase.Unlock -> scope.launch {
                busy = true
                error = null
                cellPhase = CodeCellsPhase.Verifying
                val startedAt = System.currentTimeMillis()
                val result = runCatching { repo.verifyAppPin(value) }.getOrElse { failure ->
                    holdSpinner(startedAt, 600)
                    reject(failure.message ?: "Không kiểm tra được mã bảo mật.")
                    return@launch
                }
                holdSpinner(startedAt, if (result is AppPinVerification.Success) 900 else 700)
                when (result) {
                    AppPinVerification.Success -> sealAndThen {
                        if (mode == AppPinGateMode.Manage) {
                            busy = false
                            verifiedCurrentPin = value
                            pin = ""
                            firstPin = null
                            phase = AppPinPhase.Create
                        } else finish()
                    }
                    is AppPinVerification.Incorrect ->
                        reject("Mã không đúng. Còn ${result.attemptsBeforeLock} lần trước khi tạm khóa.")
                    is AppPinVerification.Locked -> {
                        applyLock(result.seconds)
                        reject(null)
                    }
                    // Mã đã bị đặt lại ở nơi khác → tạo mã mới ngay tại đây.
                    AppPinVerification.NotSet -> {
                        busy = false
                        pin = ""
                        cellPhase = CodeCellsPhase.Idle
                        firstPin = null
                        verifiedCurrentPin = null
                        phase = AppPinPhase.Create
                        error = "Tài khoản chưa có mã bảo mật. Hãy tạo mã mới."
                    }
                }
            }
            else -> Unit
        }
    }

    fun appendDigit(digit: Int) {
        if (busy || cellPhase != CodeCellsPhase.Idle) return
        if (phase !in listOf(AppPinPhase.Unlock, AppPinPhase.Create, AppPinPhase.Confirm)) return
        if (lockUntilElapsed > clock || pin.length >= APP_PIN_LENGTH) return
        val next = pin + digit.toString()
        pin = next
        error = null
        if (next.length == APP_PIN_LENGTH) submitPin(next)
    }

    val secondsLeft = ceil(((lockUntilElapsed - clock).coerceAtLeast(0L)) / 1_000.0).toLong()
    val title = when (phase) {
        AppPinPhase.Loading -> "Mã bảo mật ứng dụng"
        AppPinPhase.Unlock -> "Nhập mã bảo mật"
        AppPinPhase.Create -> if (mode == AppPinGateMode.Manage) "Tạo mã bảo mật mới" else "Tạo mã bảo mật"
        AppPinPhase.Confirm -> "Nhập lại mã bảo mật"
        AppPinPhase.Recover -> "Quên mã bảo mật"
        AppPinPhase.Unavailable -> "Cần kết nối máy chủ"
    }
    val subtitle = when (phase) {
        AppPinPhase.Unlock -> purpose
        AppPinPhase.Create -> "Chọn mã gồm đúng 6 chữ số, không phụ thuộc mã mở khóa điện thoại. Mã được lưu trên máy chủ, không lưu trên điện thoại."
        AppPinPhase.Confirm -> "Nhập lại 6 số vừa chọn để xác nhận."
        AppPinPhase.Recover -> "Xác minh mật khẩu tài khoản để đặt lại mã bảo mật của ứng dụng."
        else -> "Đang kiểm tra mã bảo mật…"
    }

    ModalBottomSheet(
        onDismissRequest = { if (!busy) onDismiss() },
        sheetState = sheetState,
        dragHandle = null,
        containerColor = MaterialTheme.colorScheme.surface,
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .navigationBarsPadding(),
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(start = 22.dp, end = 12.dp, top = 18.dp, bottom = 14.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Icon(
                    when (phase) {
                        AppPinPhase.Recover -> Icons.Filled.Lock
                        AppPinPhase.Unavailable -> Icons.Filled.CloudOff
                        else -> Icons.Filled.Shield
                    },
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(25.dp),
                )
                Spacer(Modifier.size(10.dp))
                Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.ExtraBold, modifier = Modifier.weight(1f))
                IconButton(onClick = onDismiss, enabled = !busy) {
                    Icon(Icons.Filled.Close, contentDescription = "Đóng")
                }
            }
            HorizontalDivider()

            if (phase == AppPinPhase.Loading) {
                Box(Modifier.fillMaxWidth().height(260.dp), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator()
                }
            } else if (phase == AppPinPhase.Unavailable) {
                AppPinUnavailable(
                    message = error ?: "Không kết nối được máy chủ.",
                    onRetry = { reloadEvent++ },
                    onClose = onDismiss,
                )
            } else if (phase == AppPinPhase.Recover) {
                RecoverWithAccountPassword(
                    subtitle = subtitle,
                    password = password,
                    error = error,
                    busy = busy,
                    onPasswordChange = { password = it; error = null },
                    onCancel = {
                        password = ""
                        error = null
                        phase = AppPinPhase.Unlock
                    },
                    onVerify = {
                        if (password.isBlank()) {
                            error = "Vui lòng nhập mật khẩu tài khoản."
                        } else scope.launch {
                            busy = true
                            error = null
                            // Máy chủ xác minh mật khẩu VÀ xoá mã cũ trong cùng một lượt; client không
                            // có đường tự xoá mã, nên không thể bỏ qua bước mật khẩu.
                            runCatching { repo.resetAppPin(password) }
                                .onSuccess {
                                    busy = false
                                    password = ""
                                    pin = ""
                                    firstPin = null
                                    verifiedCurrentPin = null
                                    applyLock(0)
                                    phase = AppPinPhase.Create
                                }
                                .onFailure {
                                    busy = false
                                    error = it.message ?: "Không đặt lại được mã bảo mật."
                                }
                        }
                    },
                )
            } else {
                Column(
                    modifier = Modifier.fillMaxWidth().padding(top = 18.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Text(
                        subtitle,
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = TextAlign.Center,
                        modifier = Modifier.padding(horizontal = 28.dp),
                    )
                    Spacer(Modifier.height(14.dp))
                    // Cùng một hàng ô có hoạt cảnh với mã OTP ở màn quên mật khẩu, nhưng KHÔNG xoè
                    // thành cụm tròn: dưới nó là bàn phím số tự vẽ, không có ~100dp để cụm nở ra.
                    AnimatedCodeCells(
                        count = APP_PIN_LENGTH,
                        filled = pin.length,
                        phase = cellPhase,
                        // Mã bảo mật luôn được che: ô đã nhập hiện dấu tròn, không hiện chữ số.
                        cellText = { index -> if (index < pin.length) "●" else "" },
                        boxSize = 44.dp,
                        gap = 8.dp,
                        cluster = false,
                    )
                    Box(
                        modifier = Modifier.fillMaxWidth().heightIn(min = 46.dp),
                        contentAlignment = Alignment.Center,
                    ) {
                        when {
                            secondsLeft > 0 -> Text(
                                "Thử lại sau ${formatPinWait(secondsLeft)}",
                                color = Danger,
                                fontWeight = FontWeight.SemiBold,
                            )
                            // Đang hỏi máy chủ/đang chốt: hàng ô đã tự nói lên điều đó, không cần thêm
                            // vòng quay thứ hai chen vào giữa.
                            cellPhase == CodeCellsPhase.Verifying -> Text(
                                "Đang kiểm tra mã…",
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                style = MaterialTheme.typography.bodySmall,
                            )
                            cellPhase == CodeCellsPhase.Success -> Text(
                                "Đã xác thực",
                                color = Success,
                                fontWeight = FontWeight.SemiBold,
                                style = MaterialTheme.typography.bodySmall,
                            )
                            error != null -> Text(
                                error.orEmpty(),
                                color = Danger,
                                textAlign = TextAlign.Center,
                                style = MaterialTheme.typography.bodySmall,
                                modifier = Modifier.padding(horizontal = 28.dp),
                            )
                        }
                    }
                    if (phase == AppPinPhase.Unlock) {
                        TextButton(
                            onClick = {
                                pin = ""
                                password = ""
                                error = null
                                phase = AppPinPhase.Recover
                            },
                            enabled = !busy,
                            modifier = Modifier.fillMaxWidth(),
                        ) { Text("Quên mã bảo mật?", fontSize = 16.sp) }
                    } else {
                        Spacer(Modifier.height(48.dp))
                    }
                }
                NumericPinPad(
                    enabled = !busy && secondsLeft <= 0,
                    canDelete = pin.isNotEmpty(),
                    onDigit = ::appendDigit,
                    onDelete = { if (!busy && pin.isNotEmpty()) pin = pin.dropLast(1) },
                )
            }
        }
    }
}

/** Mã nằm ở máy chủ nên mất mạng là không mở khoá được — nói thẳng thay vì im lặng cho nhập mã. */
@Composable
private fun AppPinUnavailable(message: String, onRetry: () -> Unit, onClose: () -> Unit) {
    Column(
        modifier = Modifier.fillMaxWidth().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(message, color = Danger, textAlign = TextAlign.Center)
        Text(
            "Mã bảo mật được giữ trên máy chủ để thiết bị không lưu bản sao nào. Vì vậy cần có mạng mới mở khóa được dữ liệu nhạy cảm.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
        Button(onClick = onRetry, modifier = Modifier.fillMaxWidth().height(50.dp)) {
            Text("Thử lại", fontWeight = FontWeight.Bold)
        }
        TextButton(onClick = onClose, modifier = Modifier.fillMaxWidth()) { Text("Đóng") }
    }
}

@Composable
private fun NumericPinPad(
    enabled: Boolean,
    canDelete: Boolean,
    onDigit: (Int) -> Unit,
    onDelete: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f))
            .padding(horizontal = 20.dp, vertical = 16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        listOf(listOf(1, 2, 3), listOf(4, 5, 6), listOf(7, 8, 9)).forEach { row ->
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                row.forEach { number -> PinKey(number.toString(), enabled, Modifier.weight(1f)) { onDigit(number) } }
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Spacer(Modifier.weight(1f).height(68.dp))
            PinKey("0", enabled, Modifier.weight(1f)) { onDigit(0) }
            Surface(
                modifier = Modifier.weight(1f).height(68.dp),
                color = Color.Transparent,
                onClick = onDelete,
                enabled = enabled && canDelete,
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        Icons.AutoMirrored.Filled.Backspace,
                        contentDescription = "Xóa số cuối",
                        modifier = Modifier.size(30.dp),
                        tint = if (enabled && canDelete) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.outline,
                    )
                }
            }
        }
    }
}

@Composable
private fun PinKey(label: String, enabled: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Surface(
        modifier = modifier.height(68.dp),
        shape = RoundedCornerShape(12.dp),
        color = MaterialTheme.colorScheme.surface,
        shadowElevation = 1.dp,
        onClick = onClick,
        enabled = enabled,
    ) {
        Box(contentAlignment = Alignment.Center) {
            Text(label, fontSize = 28.sp, fontWeight = FontWeight.Bold)
        }
    }
}

@Composable
private fun RecoverWithAccountPassword(
    subtitle: String,
    password: String,
    error: String?,
    busy: Boolean,
    onPasswordChange: (String) -> Unit,
    onCancel: () -> Unit,
    onVerify: () -> Unit,
) {
    Column(
        modifier = Modifier.fillMaxWidth().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Text(subtitle, color = MaterialTheme.colorScheme.onSurfaceVariant)
        OutlinedTextField(
            value = password,
            onValueChange = onPasswordChange,
            label = { Text("Mật khẩu tài khoản") },
            singleLine = true,
            enabled = !busy,
            visualTransformation = PasswordVisualTransformation(),
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier.fillMaxWidth(),
        )
        error?.let { Text(it, color = Danger, style = MaterialTheme.typography.bodySmall) }
        Button(onClick = onVerify, enabled = !busy, modifier = Modifier.fillMaxWidth().height(50.dp)) {
            if (busy) CircularProgressIndicator(Modifier.size(20.dp), color = MaterialTheme.colorScheme.onPrimary, strokeWidth = 2.dp)
            else Text("Xác minh và tạo mã mới", fontWeight = FontWeight.Bold)
        }
        TextButton(onClick = onCancel, enabled = !busy, modifier = Modifier.fillMaxWidth()) { Text("Quay lại") }
        Text(
            "Bước này cần kết nối máy chủ. Mật khẩu chỉ được gửi qua kết nối bảo mật để xác minh và không được lưu trên điện thoại.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}

internal fun formatPinWait(seconds: Long): String = when {
    seconds < 60 -> "$seconds giây"
    else -> "${(seconds + 59) / 60} phút"
}
