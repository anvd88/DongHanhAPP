package com.ketoanapk.hr.ui

import android.os.Build
import android.view.HapticFeedbackConstants
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
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
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Backspace
import androidx.compose.material.icons.filled.Close
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
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.sp
import com.ketoanapk.hr.data.AppPinStore
import com.ketoanapk.hr.data.AppPinVerification
import com.ketoanapk.hr.ui.theme.Danger
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.ceil
import kotlin.math.roundToInt

enum class AppPinGateMode { Unlock, Manage }

private enum class AppPinPhase { Loading, Unlock, Create, Confirm, Recover }

/**
 * Bottom sheet PIN dùng chung cho mọi dữ liệu nhạy cảm. PIN được nhập hoàn toàn bằng bàn phím số tự
 * vẽ; không mở bàn phím hệ thống và không dùng ô nhập văn bản cho mã bảo mật.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AppPinGate(
    visible: Boolean,
    username: String,
    purpose: String,
    onDismiss: () -> Unit,
    onUnlocked: () -> Unit,
    onVerifyAccountPassword: (String, (Boolean, String?) -> Unit) -> Unit,
    mode: AppPinGateMode = AppPinGateMode.Unlock,
) {
    if (!visible) return

    val context = LocalContext.current
    val density = LocalDensity.current
    val view = LocalView.current
    val store = remember(context) { AppPinStore(context) }
    val scope = rememberCoroutineScope()
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    var phase by remember(visible, username, mode) { mutableStateOf(AppPinPhase.Loading) }
    var pin by remember(visible, username, mode) { mutableStateOf("") }
    var firstPin by remember(visible, username, mode) { mutableStateOf<String?>(null) }
    var password by remember(visible, username, mode) { mutableStateOf("") }
    var error by remember(visible, username, mode) { mutableStateOf<String?>(null) }
    var busy by remember(visible, username, mode) { mutableStateOf(false) }
    var rejectingPin by remember(visible, username, mode) { mutableStateOf(false) }
    var rejectionEvent by remember(visible, username, mode) { mutableIntStateOf(0) }
    var lockUntil by remember(visible, username, mode) { mutableLongStateOf(0L) }
    var clock by remember { mutableLongStateOf(System.currentTimeMillis()) }
    val pinShake = remember(visible, username, mode) { Animatable(0f) }

    LaunchedEffect(visible, username, mode) {
        phase = AppPinPhase.Loading
        error = null
        runCatching { store.hasPin(username) }
            .onSuccess { hasPin -> phase = if (hasPin) AppPinPhase.Unlock else AppPinPhase.Create }
            .onFailure {
                // Vẫn cho vào màn mở để người dùng có đường "Quên mã" khôi phục bản ghi hỏng.
                phase = AppPinPhase.Unlock
                error = it.message ?: "Không thể đọc mã bảo mật."
            }
    }

    LaunchedEffect(lockUntil) {
        while (lockUntil > System.currentTimeMillis()) {
            clock = System.currentTimeMillis()
            delay(1_000)
        }
        clock = System.currentTimeMillis()
    }

    LaunchedEffect(rejectionEvent) {
        if (rejectionEvent == 0) return@LaunchedEffect
        val activeEvent = rejectionEvent
        val distance = with(density) { 12.dp.toPx() }
        view.performHapticFeedback(
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) HapticFeedbackConstants.REJECT
            else HapticFeedbackConstants.LONG_PRESS,
        )
        pinShake.snapTo(0f)
        listOf(-1f, 1f, -0.75f, 0.75f, -0.4f, 0.4f).forEach { direction ->
            pinShake.animateTo(direction * distance, animationSpec = tween(42))
        }
        pinShake.animateTo(0f, animationSpec = tween(60))
        if (rejectionEvent == activeEvent) {
            pin = ""
            rejectingPin = false
        }
    }

    fun finish() {
        pin = ""
        firstPin = null
        busy = false
        onUnlocked()
    }

    fun saveConfirmedPin(value: String) {
        scope.launch {
            busy = true
            error = null
            runCatching { store.setPin(username, value) }
                .onSuccess { finish() }
                .onFailure {
                    busy = false
                    pin = ""
                    firstPin = null
                    phase = AppPinPhase.Create
                    error = it.message ?: "Không thể lưu mã bảo mật."
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
                    phase = AppPinPhase.Create
                    error = "Hai lần nhập chưa khớp. Vui lòng tạo mã lại."
                    rejectingPin = true
                    rejectionEvent++
                } else {
                    saveConfirmedPin(value)
                }
            }
            AppPinPhase.Unlock -> scope.launch {
                busy = true
                error = null
                when (val result = runCatching { store.verify(username, value) }.getOrElse {
                    busy = false
                    error = it.message ?: "Không thể xác minh mã bảo mật."
                    rejectingPin = true
                    rejectionEvent++
                    return@launch
                }) {
                    AppPinVerification.Success -> {
                        if (mode == AppPinGateMode.Manage) {
                            busy = false
                            pin = ""
                            firstPin = null
                            phase = AppPinPhase.Create
                        } else finish()
                    }
                    is AppPinVerification.Incorrect -> {
                        busy = false
                        error = "Mã không đúng. Còn ${result.attemptsBeforeLock} lần trước khi tạm khóa."
                        rejectingPin = true
                        rejectionEvent++
                    }
                    is AppPinVerification.Locked -> {
                        busy = false
                        lockUntil = result.retryAtMillis
                        clock = System.currentTimeMillis()
                        rejectingPin = true
                        rejectionEvent++
                    }
                }
            }
            else -> Unit
        }
    }

    fun appendDigit(digit: Int) {
        if (busy || rejectingPin || phase !in listOf(AppPinPhase.Unlock, AppPinPhase.Create, AppPinPhase.Confirm)) return
        if (lockUntil > clock || pin.length >= 6) return
        val next = pin + digit.toString()
        pin = next
        error = null
        if (next.length == 6) submitPin(next)
    }

    val secondsLeft = ceil(((lockUntil - clock).coerceAtLeast(0L)) / 1_000.0).toLong()
    val title = when (phase) {
        AppPinPhase.Loading -> "Mã bảo mật ứng dụng"
        AppPinPhase.Unlock -> "Nhập mã bảo mật"
        AppPinPhase.Create -> if (mode == AppPinGateMode.Manage) "Tạo mã bảo mật mới" else "Tạo mã bảo mật"
        AppPinPhase.Confirm -> "Nhập lại mã bảo mật"
        AppPinPhase.Recover -> "Quên mã bảo mật"
    }
    val subtitle = when (phase) {
        AppPinPhase.Unlock -> purpose
        AppPinPhase.Create -> "Chọn mã gồm đúng 6 chữ số, không phụ thuộc mã mở khóa điện thoại."
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
                    if (phase == AppPinPhase.Recover) Icons.Filled.Lock else Icons.Filled.Shield,
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
                        } else {
                            busy = true
                            error = null
                            onVerifyAccountPassword(password) { ok, message ->
                                if (!ok) {
                                    busy = false
                                    error = message ?: "Không thể xác minh mật khẩu."
                                } else scope.launch {
                                    runCatching { store.clear(username) }
                                        .onSuccess {
                                            busy = false
                                            password = ""
                                            pin = ""
                                            firstPin = null
                                            lockUntil = 0L
                                            phase = AppPinPhase.Create
                                        }
                                        .onFailure {
                                            busy = false
                                            error = it.message ?: "Không thể đặt lại mã bảo mật."
                                        }
                                }
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
                    Spacer(Modifier.height(22.dp))
                    PinDots(
                        filled = pin.length,
                        error = error != null || secondsLeft > 0,
                        modifier = Modifier.offset { IntOffset(pinShake.value.roundToInt(), 0) },
                    )
                    Box(
                        modifier = Modifier.fillMaxWidth().heightIn(min = 54.dp),
                        contentAlignment = Alignment.Center,
                    ) {
                        when {
                            secondsLeft > 0 -> Text(
                                "Thử lại sau ${formatPinWait(secondsLeft)}",
                                color = Danger,
                                fontWeight = FontWeight.SemiBold,
                            )
                            busy -> CircularProgressIndicator(Modifier.size(22.dp), strokeWidth = 2.dp)
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
                            enabled = !busy && !rejectingPin,
                            modifier = Modifier.fillMaxWidth(),
                        ) { Text("Quên mã bảo mật?", fontSize = 16.sp) }
                    } else {
                        Spacer(Modifier.height(48.dp))
                    }
                }
                NumericPinPad(
                    enabled = !busy && !rejectingPin && secondsLeft <= 0,
                    canDelete = pin.isNotEmpty(),
                    onDigit = ::appendDigit,
                    onDelete = { if (!busy && !rejectingPin && pin.isNotEmpty()) pin = pin.dropLast(1) },
                )
            }
        }
    }
}

@Composable
private fun PinDots(filled: Int, error: Boolean, modifier: Modifier = Modifier) {
    Row(modifier = modifier, horizontalArrangement = Arrangement.spacedBy(18.dp)) {
        repeat(6) { index ->
            Surface(
                modifier = Modifier.size(34.dp),
                shape = CircleShape,
                color = when {
                    error -> MaterialTheme.colorScheme.errorContainer
                    index < filled -> MaterialTheme.colorScheme.primary
                    else -> Color.Transparent
                },
                border = BorderStroke(
                    2.dp,
                    when {
                        error -> MaterialTheme.colorScheme.error
                        index < filled -> MaterialTheme.colorScheme.primary
                        else -> MaterialTheme.colorScheme.outline
                    },
                ),
            ) {}
        }
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
