package com.ketoanapk.hr.ui

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutLinearInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.animation.scaleOut
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.GenericShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.draw.scale
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathMeasure
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.ketoanapk.hr.ui.theme.BrandGradientBottom
import com.ketoanapk.hr.ui.theme.BrandGradientTop
import com.ketoanapk.hr.ui.theme.Success
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.cos
import kotlin.math.sin

private enum class ForgotPhase { Form, Submitting, Success }

/** Ba bước của màn quên mật khẩu: tên đăng nhập → mã khôi phục (OTP) → mật khẩu mới. */
private enum class ForgotStep { Username, Code, Password }

/** Trạng thái cụm ô OTP: đang gõ / đang xác thực / đúng mã / sai mã. */
private enum class OtpPhase { Idle, Verifying, Success, Error }

/** Mã khôi phục do admin cấp dài 5 ký tự (Security/RecoveryCodes.cs) → 5 ô OTP. */
private const val RECOVERY_CODE_LENGTH = 5
private const val RECOVERY_RESEND_SECONDS = 60

/** Mã chỉ gồm chữ HOA + số; lọc ngay khi gõ để không phải sửa tay. */
private fun sanitizeRecoveryCode(raw: String): String =
    raw.uppercase().filter { it in '0'..'9' || it in 'A'..'Z' }

@Composable
fun LoginScreen(
    loading: Boolean,
    error: String?,
    resetLoading: Boolean,
    rememberedUsername: String,
    onLogin: (String, String, Boolean) -> Unit,
    onVerifyRecoveryCode: (String, String, (Boolean, String?) -> Unit) -> Unit,
    onResetPasswordWithCode: (String, String, String, (Boolean, String?) -> Unit) -> Unit,
) {
    var username by rememberSaveable(rememberedUsername) { mutableStateOf(rememberedUsername) }
    var password by rememberSaveable { mutableStateOf("") }
    var remember by rememberSaveable { mutableStateOf(true) }
    var showPassword by remember { mutableStateOf(false) }
    var forgotOpen by rememberSaveable { mutableStateOf(false) }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Brush.verticalGradient(listOf(BrandGradientTop, BrandGradientBottom))),
        contentAlignment = Alignment.Center,
    ) {
        // Quên mật khẩu là MỘT MÀN HÌNH riêng (không phải popup): trượt sang ngang như điều hướng thật.
        AnimatedContent(
            targetState = forgotOpen,
            transitionSpec = {
                if (targetState) {
                    (slideInHorizontally(tween(320)) { it / 3 } + fadeIn(tween(240))) togetherWith
                        (slideOutHorizontally(tween(320)) { -it / 8 } + fadeOut(tween(200)))
                } else {
                    (slideInHorizontally(tween(320)) { -it / 3 } + fadeIn(tween(240))) togetherWith
                        (slideOutHorizontally(tween(320)) { it / 8 } + fadeOut(tween(200)))
                }
            },
            label = "forgotScreen",
        ) { showForgot ->
            if (showForgot) {
                ForgotPasswordScreen(
                    username = username,
                    onUsernameChange = { username = it },
                    resetLoading = resetLoading,
                    onVerifyRecoveryCode = onVerifyRecoveryCode,
                    onResetPasswordWithCode = onResetPasswordWithCode,
                    onClose = { forgotOpen = false },
                )
            } else {
                Column(
                    modifier = Modifier
                        .fillMaxSize()
                        .verticalScroll(rememberScrollState())
                        .systemBarsPadding()
                        .imePadding()
                        .padding(horizontal = 24.dp, vertical = 32.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.Center,
                ) {
                    Box(
                        modifier = Modifier
                            .size(76.dp)
                            .clip(CircleShape)
                            .background(Color.White),
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(Icons.Filled.Face, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(42.dp))
                    }
                    Spacer(Modifier.height(14.dp))
                    Text("KetoanAPK", style = MaterialTheme.typography.headlineSmall, color = Color.White)
                    Text("Nhân sự & chấm công", style = MaterialTheme.typography.bodyMedium, color = Color.White.copy(alpha = 0.85f))
                    Spacer(Modifier.height(22.dp))

                    Surface(
                        modifier = Modifier
                            .fillMaxWidth()
                            .widthIn(max = 440.dp),
                        shape = RoundedCornerShape(22.dp),
                        color = MaterialTheme.colorScheme.surface,
                        shadowElevation = 6.dp,
                    ) {
                        Column(
                            modifier = Modifier.padding(20.dp),
                            verticalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            Text("Đăng nhập", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)

                            OutlinedTextField(
                                value = username,
                                onValueChange = { username = it },
                                label = { Text("Tên đăng nhập") },
                                leadingIcon = { Icon(Icons.Filled.Person, contentDescription = null) },
                                singleLine = true,
                                shape = RoundedCornerShape(14.dp),
                                modifier = Modifier.fillMaxWidth(),
                                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
                            )
                            OutlinedTextField(
                                value = password,
                                onValueChange = { password = it },
                                label = { Text("Mật khẩu") },
                                leadingIcon = { Icon(Icons.Filled.Lock, contentDescription = null) },
                                trailingIcon = {
                                    IconButton(onClick = { showPassword = !showPassword }) {
                                        Icon(
                                            if (showPassword) Icons.Filled.VisibilityOff else Icons.Filled.Visibility,
                                            contentDescription = if (showPassword) "Ẩn mật khẩu" else "Hiện mật khẩu",
                                        )
                                    }
                                },
                                singleLine = true,
                                shape = RoundedCornerShape(14.dp),
                                visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
                                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
                                modifier = Modifier.fillMaxWidth(),
                            )

                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Checkbox(checked = remember, onCheckedChange = { remember = it })
                                Text("Nhớ tài khoản", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
                            }

                            if (error != null) ErrorText(error)

                            Button(
                                onClick = { onLogin(username, password, remember) },
                                enabled = !loading && !resetLoading,
                                shape = RoundedCornerShape(14.dp),
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .height(50.dp),
                            ) {
                                if (loading) {
                                    CircularProgressIndicator(modifier = Modifier.size(20.dp), color = MaterialTheme.colorScheme.onPrimary, strokeWidth = 2.dp)
                                } else {
                                    Text("Đăng nhập", fontWeight = FontWeight.Bold)
                                }
                            }

                            OutlinedButton(
                                onClick = { forgotOpen = true },
                                enabled = !loading && !resetLoading,
                                shape = RoundedCornerShape(14.dp),
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .height(50.dp),
                            ) {
                                Icon(Icons.Filled.Lock, contentDescription = null, modifier = Modifier.size(20.dp))
                                Spacer(Modifier.width(8.dp))
                                Text("Quên mật khẩu", fontWeight = FontWeight.Bold)
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ForgotPasswordScreen(
    username: String,
    onUsernameChange: (String) -> Unit,
    resetLoading: Boolean,
    onVerifyRecoveryCode: (String, String, (Boolean, String?) -> Unit) -> Unit,
    onResetPasswordWithCode: (String, String, String, (Boolean, String?) -> Unit) -> Unit,
    onClose: () -> Unit,
) {
    var phase by rememberSaveable { mutableStateOf(ForgotPhase.Form) }
    var step by rememberSaveable { mutableStateOf(ForgotStep.Username) }
    var code by rememberSaveable { mutableStateOf("") }
    var otpPhase by rememberSaveable { mutableStateOf(OtpPhase.Idle) }
    var resendLeft by rememberSaveable { mutableStateOf(RECOVERY_RESEND_SECONDS) }
    var resendCycle by rememberSaveable { mutableStateOf(0) }
    var resendHint by rememberSaveable { mutableStateOf<String?>(null) }
    var newPassword by rememberSaveable { mutableStateOf("") }
    var confirmPassword by rememberSaveable { mutableStateOf("") }
    var showNewPassword by remember { mutableStateOf(false) }
    var showConfirmPassword by remember { mutableStateOf(false) }
    var localError by rememberSaveable { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    // Đang chạy hoạt cảnh xác thực hoặc đang gọi máy chủ ⇒ khóa lối thoát để không bỏ dở giữa chừng.
    val busy = phase == ForgotPhase.Submitting || otpPhase == OtpPhase.Verifying || otpPhase == OtpPhase.Success

    fun goStep(next: ForgotStep) {
        step = next
        localError = null
        if (next == ForgotStep.Code) {
            code = ""
            otpPhase = OtpPhase.Idle
            resendLeft = RECOVERY_RESEND_SECONDS
            resendCycle += 1
            resendHint = null
        }
    }

    fun submit() {
        localError = when {
            username.trim().isBlank() -> "Vui lòng nhập tên đăng nhập."
            code.length < RECOVERY_CODE_LENGTH -> "Vui lòng nhập mã khôi phục."
            newPassword.length < 6 -> "Mật khẩu mới cần ít nhất 6 ký tự."
            newPassword != confirmPassword -> "Mật khẩu nhập lại chưa khớp."
            else -> null
        }
        if (localError != null) return
        phase = ForgotPhase.Submitting
        onResetPasswordWithCode(username.trim(), code, newPassword) { ok, message ->
            if (ok) {
                phase = ForgotPhase.Success
                newPassword = ""
                confirmPassword = ""
                code = ""
            } else {
                phase = ForgotPhase.Form
                localError = message ?: "Không đặt lại được mật khẩu. Vui lòng thử lại."
                // Mã hết hạn/bị dùng mất giữa chừng ⇒ quay về bước nhập mã thay vì báo lỗi cụt.
                if (message != null && message.contains("mã khôi phục", ignoreCase = true)) {
                    val keep = message
                    goStep(ForgotStep.Code)
                    localError = keep
                }
            }
        }
    }

    // Nhập đủ 5 ký tự là TỰ xác thực — màn hình này cố ý không có nút "Xác nhận".
    LaunchedEffect(step, code, otpPhase) {
        if (step != ForgotStep.Code || otpPhase != OtpPhase.Idle) return@LaunchedEffect
        if (code.length < RECOVERY_CODE_LENGTH) return@LaunchedEffect
        otpPhase = OtpPhase.Verifying
        localError = null
        resendHint = null
        val startedAt = System.currentTimeMillis()
        onVerifyRecoveryCode(username.trim(), code) { ok, message ->
            scope.launch {
                // Giữ hoạt cảnh đủ lâu để nhìn được, kể cả khi máy chủ trong LAN trả lời tức thì.
                val remain = (if (ok) 1100L else 800L) - (System.currentTimeMillis() - startedAt)
                if (remain > 0) delay(remain)
                if (ok) {
                    otpPhase = OtpPhase.Success
                    delay(1150)
                    goStep(ForgotStep.Password)
                } else {
                    otpPhase = OtpPhase.Error
                    localError = message ?: "Mã khôi phục không đúng hoặc đã hết hạn."
                    // Chờ rung (380ms) + nứt (110ms) + rơi (760ms) xong mới dựng lại hàng ô trống.
                    delay(1420)
                    code = ""
                    otpPhase = OtpPhase.Idle
                }
            }
        }
    }

    LaunchedEffect(step, resendCycle) {
        if (step != ForgotStep.Code) return@LaunchedEffect
        while (resendLeft > 0) {
            delay(1000)
            resendLeft -= 1
        }
    }

    fun back() {
        if (busy || resetLoading) return
        when {
            phase != ForgotPhase.Form -> onClose()
            step == ForgotStep.Password -> goStep(ForgotStep.Code)
            step == ForgotStep.Code -> goStep(ForgotStep.Username)
            else -> onClose()
        }
    }

    BackHandler(enabled = !busy && !resetLoading) { back() }

    val title = when {
        phase == ForgotPhase.Success -> "Đã đổi mật khẩu"
        step == ForgotStep.Code -> "Xác thực mã OTP"
        step == ForgotStep.Password -> "Đặt mật khẩu mới"
        else -> "Khôi phục mật khẩu"
    }
    val subtitle = when {
        phase == ForgotPhase.Success -> "Bạn có thể đăng nhập bằng mật khẩu mới."
        step == ForgotStep.Code -> "Nhập mã khôi phục đã được cấp cho tài khoản ${username.trim()}"
        step == ForgotStep.Password -> "Mã khôi phục hợp lệ. Đặt mật khẩu mới cho tài khoản."
        else -> "Nhập tên đăng nhập cần khôi phục. Bước sau bạn sẽ nhập mã do quản trị viên cấp."
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .systemBarsPadding()
            .imePadding()
            .padding(horizontal = 20.dp, vertical = 10.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = { back() }, enabled = !busy && !resetLoading) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Quay lại", tint = Color.White)
            }
            Spacer(Modifier.width(2.dp))
            Text(
                "Khôi phục truy cập",
                style = MaterialTheme.typography.titleMedium,
                color = Color.White,
                fontWeight = FontWeight.SemiBold,
            )
        }

        Spacer(Modifier.height(18.dp))
        Box(
            modifier = Modifier
                .size(72.dp)
                .clip(CircleShape)
                .background(Color.White),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                if (phase == ForgotPhase.Success) Icons.Filled.CheckCircle else Icons.Filled.Lock,
                contentDescription = null,
                tint = if (phase == ForgotPhase.Success) Success else MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(36.dp),
            )
        }
        Spacer(Modifier.height(14.dp))
        Text(
            title,
            style = MaterialTheme.typography.headlineSmall,
            color = Color.White,
            fontWeight = FontWeight.Bold,
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(6.dp))
        Text(
            subtitle,
            style = MaterialTheme.typography.bodyMedium,
            color = Color.White.copy(alpha = 0.85f),
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(20.dp))

        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 440.dp),
            shape = RoundedCornerShape(24.dp),
            color = MaterialTheme.colorScheme.surface,
            shadowElevation = 8.dp,
        ) {
            Column(
                modifier = Modifier.padding(horizontal = 20.dp, vertical = 22.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                if (phase == ForgotPhase.Form) ForgotStepBar(step)

                when {
                    phase == ForgotPhase.Submitting -> {
                        CircularProgressIndicator(color = MaterialTheme.colorScheme.primary, strokeWidth = 2.5.dp)
                        Text(
                            "Đang cập nhật mật khẩu…",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            textAlign = TextAlign.Center,
                        )
                    }

                    phase == ForgotPhase.Success -> {
                        Button(
                            onClick = onClose,
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(50.dp),
                            shape = RoundedCornerShape(14.dp),
                        ) {
                            Text("Quay lại đăng nhập", fontWeight = FontWeight.Bold)
                        }
                    }

                    step == ForgotStep.Username -> {
                        OutlinedTextField(
                            value = username,
                            onValueChange = onUsernameChange,
                            label = { Text("Tên đăng nhập") },
                            leadingIcon = { Icon(Icons.Filled.Person, contentDescription = null) },
                            singleLine = true,
                            shape = RoundedCornerShape(14.dp),
                            modifier = Modifier.fillMaxWidth(),
                            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                        )
                        localError?.let { ErrorText(it) }
                        Button(
                            onClick = {
                                if (username.trim().isBlank()) localError = "Vui lòng nhập tên đăng nhập."
                                else goStep(ForgotStep.Code)
                            },
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(50.dp),
                            shape = RoundedCornerShape(14.dp),
                        ) {
                            Text("Tiếp tục", fontWeight = FontWeight.Bold)
                        }
                    }

                    step == ForgotStep.Code -> {
                        RecoveryOtpBoxes(
                            code = code,
                            phase = otpPhase,
                            onCodeChange = { code = it },
                        )

                        // Khung trạng thái cố định chiều cao để chữ hiện ra không làm nhảy bố cục.
                        Box(modifier = Modifier.height(22.dp), contentAlignment = Alignment.Center) {
                            val statusText = when (otpPhase) {
                                OtpPhase.Verifying -> "Đang xác thực mã…"
                                OtpPhase.Success -> "Xác thực thành công"
                                OtpPhase.Error -> localError ?: "Mã OTP không chính xác"
                                OtpPhase.Idle -> ""
                            }
                            if (statusText.isNotEmpty()) {
                                Text(
                                    statusText,
                                    style = MaterialTheme.typography.bodyMedium,
                                    fontWeight = FontWeight.Bold,
                                    color = when (otpPhase) {
                                        OtpPhase.Success -> Success
                                        OtpPhase.Error -> MaterialTheme.colorScheme.error
                                        else -> MaterialTheme.colorScheme.primary
                                    },
                                    textAlign = TextAlign.Center,
                                )
                            }
                        }

                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(
                                "Bạn chưa nhận được mã?",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                            if (resendLeft > 0) {
                                Spacer(Modifier.width(6.dp))
                                Text(
                                    "Gửi lại sau $resendLeft giây",
                                    style = MaterialTheme.typography.bodySmall,
                                    fontWeight = FontWeight.Bold,
                                    color = MaterialTheme.colorScheme.onSurface,
                                )
                            } else {
                                TextButton(
                                    onClick = {
                                        // Mã do quản trị viên cấp trực tiếp (không gửi SMS/email).
                                        resendHint = "Mã khôi phục do quản trị viên cấp trực tiếp. Liên hệ quản trị viên để được cấp mã mới."
                                        resendLeft = RECOVERY_RESEND_SECONDS
                                        resendCycle += 1
                                    },
                                    enabled = otpPhase == OtpPhase.Idle,
                                ) {
                                    Text("Gửi lại mã", fontWeight = FontWeight.Bold)
                                }
                            }
                        }
                        resendHint?.let {
                            Text(
                                it,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                textAlign = TextAlign.Center,
                            )
                        }
                    }

                    else -> {
                        PasswordResetField(
                            label = "Mật khẩu mới",
                            value = newPassword,
                            show = showNewPassword,
                            onShowChange = { showNewPassword = it },
                            onChange = { newPassword = it },
                            imeAction = ImeAction.Next,
                        )
                        PasswordResetField(
                            label = "Nhập lại mật khẩu mới",
                            value = confirmPassword,
                            show = showConfirmPassword,
                            onShowChange = { showConfirmPassword = it },
                            onChange = { confirmPassword = it },
                            imeAction = ImeAction.Done,
                        )
                        localError?.let { ErrorText(it) }
                        Button(
                            onClick = { submit() },
                            enabled = !resetLoading,
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(50.dp),
                            shape = RoundedCornerShape(14.dp),
                        ) {
                            Text("Đặt lại mật khẩu", fontWeight = FontWeight.Bold)
                        }
                    }
                }
            }
        }

        Spacer(Modifier.height(18.dp))
        if (phase == ForgotPhase.Form && step != ForgotStep.Username) {
            TextButton(onClick = { back() }, enabled = !busy && !resetLoading) {
                Text(
                    if (step == ForgotStep.Password) "Nhập lại mã khôi phục" else "Đổi tên đăng nhập",
                    color = Color.White,
                    fontWeight = FontWeight.SemiBold,
                )
            }
        }
        Spacer(Modifier.height(10.dp))
    }
}

/** Ba vạch chỉ bước đang đứng của luồng khôi phục. */
@Composable
private fun ForgotStepBar(step: ForgotStep) {
    val index = ForgotStep.entries.indexOf(step)
    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        ForgotStep.entries.forEachIndexed { i, _ ->
            val active = i == index
            val width by animateDpAsState(if (active) 34.dp else 26.dp, label = "stepBar$i")
            Box(
                modifier = Modifier
                    .width(width)
                    .height(4.dp)
                    .clip(RoundedCornerShape(2.dp))
                    .background(
                        when {
                            i < index -> Success.copy(alpha = 0.65f)
                            active -> MaterialTheme.colorScheme.primary
                            else -> MaterialTheme.colorScheme.outlineVariant
                        },
                    ),
            )
        }
    }
}

/**
 * 5 mảnh vỡ KHÔNG đều nhau, cắt từ một điểm nứt lệch tâm (44%, 52%) và có nếp gãy trên đường nứt —
 * ghép lại vẫn kín đúng ô vuông ban đầu, nhưng nhìn ra vết nứt kính chứ không phải "chia làm 4".
 */
private val SHARD_POLYGONS: List<List<Pair<Float, Float>>> = listOf(
    listOf(0.44f to 0.52f, 0.26f to 0.22f, 0f to 0f, 0f to 0.30f, 0.20f to 0.46f),
    listOf(0.44f to 0.52f, 0.26f to 0.22f, 0f to 0f, 0.38f to 0f, 0.36f to 0.27f),
    listOf(0.44f to 0.52f, 0.36f to 0.27f, 0.38f to 0f, 1f to 0f, 1f to 0.42f, 0.74f to 0.42f),
    listOf(0.44f to 0.52f, 0.74f to 0.42f, 1f to 0.42f, 1f to 1f, 0.56f to 1f, 0.54f to 0.74f),
    listOf(0.44f to 0.52f, 0.54f to 0.74f, 0.56f to 1f, 0f to 1f, 0f to 0.30f, 0.20f to 0.46f),
)

private fun shardShape(index: Int): Shape = GenericShape { size, _ ->
    val points = SHARD_POLYGONS[index]
    moveTo(points[0].first * size.width, points[0].second * size.height)
    for (k in 1 until points.size) lineTo(points[k].first * size.width, points[k].second * size.height)
    close()
}

/** Mỗi mảnh: dạt ngang, nảy lên lúc vỡ, rơi xuống (nhanh dần), xoay khi rơi — đơn vị dp/độ. */
private data class ShardFlight(val drift: Float, val lift: Float, val fall: Float, val rotate: Float)

private val SHARD_FLIGHT = listOf(
    ShardFlight(drift = -14f, lift = -18f, fall = 108f, rotate = -52f),
    ShardFlight(drift = -4f, lift = -20f, fall = 116f, rotate = -26f),
    ShardFlight(drift = 13f, lift = -9f, fall = 94f, rotate = 32f),
    ShardFlight(drift = 18f, lift = -7f, fall = 100f, rotate = 44f),
    ShardFlight(drift = -9f, lift = -12f, fall = 104f, rotate = -22f),
)

/**
 * 5 ô mã khôi phục. Gõ đủ ký tự thì các ô rời hàng ngang, xếp quanh tâm và quay chậm một vòng
 * (chữ số xoay ngược lại nên luôn đứng thẳng) trong lúc máy chủ kiểm tra mã. Đúng mã: thu về giữa,
 * vòng tròn xanh nở ra và nét ✓ được vẽ. Sai mã: rung rồi mỗi ô VỠ thành 4 mảnh văng ra, sau đó
 * hàng ô trống mới hiện lên để nhập lại. Ô nhập thật là một BasicTextField ẩn — bàn phím Android
 * chỉ cần một ô để gõ liền mạch, 5 ô kia là phần nhìn.
 */
@Composable
private fun RecoveryOtpBoxes(
    code: String,
    phase: OtpPhase,
    onCodeChange: (String) -> Unit,
) {
    val boxSize = 54.dp
    val gap = 10.dp
    val radius = boxSize * 1.28f
    val clustered = phase == OtpPhase.Verifying || phase == OtpPhase.Success
    val focusRequester = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    val shake = remember { Animatable(0f) }
    // 0 = bình thường, 1 = đang rung, 2 = đang vỡ.
    var errorStage by remember { mutableStateOf(0) }
    val shatter = remember { Animatable(0f) }
    val sealDraw = remember { Animatable(0f) }
    val stageHeight by animateDpAsState(
        if (clustered) radius * 2 + boxSize + 10.dp else boxSize + 6.dp,
        spring(dampingRatio = Spring.DampingRatioNoBouncy, stiffness = Spring.StiffnessMediumLow),
        label = "otpStageHeight",
    )
    val orbit = rememberInfiniteTransition(label = "otpOrbit")
    val spin by orbit.animateFloat(
        initialValue = 0f,
        targetValue = 360f,
        animationSpec = infiniteRepeatable(tween(7500, easing = LinearEasing), RepeatMode.Restart),
        label = "otpSpin",
    )
    val orbitAngle = if (phase == OtpPhase.Verifying) spin else 0f

    LaunchedEffect(phase) {
        if (phase != OtpPhase.Idle) return@LaunchedEffect
        // Chờ một nhịp cho FocusRequester gắn xong (màn hình vừa trượt vào) rồi mới đòi con trỏ.
        delay(140)
        runCatching { focusRequester.requestFocus() }
        keyboard?.show()
    }

    LaunchedEffect(phase) {
        if (phase != OtpPhase.Error) {
            errorStage = 0
            shake.snapTo(0f)
            shatter.snapTo(0f)
            return@LaunchedEffect
        }
        errorStage = 1
        shake.snapTo(0f)
        shake.animateTo(
            targetValue = 0f,
            animationSpec = keyframes {
                durationMillis = 380
                0f at 0
                -26f at 60
                22f at 120
                -16f at 190
                10f at 260
                -5f at 320
                0f at 380
            },
        )
        errorStage = 2
        shatter.snapTo(0f)
        // Nứt xong đứng yên một nhịp rồi mới rơi — mắt kịp thấy vết nứt trước khi mảnh rời ra.
        delay(110)
        shatter.animateTo(1f, tween(760, easing = LinearEasing))
    }

    LaunchedEffect(phase) {
        if (phase != OtpPhase.Success) {
            sealDraw.snapTo(0f)
            return@LaunchedEffect
        }
        delay(260)
        sealDraw.animateTo(1f, tween(420))
    }

    Box(
        modifier = Modifier
            .fillMaxWidth()
            .height(stageHeight)
            .graphicsLayer { translationX = shake.value },
        contentAlignment = Alignment.Center,
    ) {
        BasicTextField(
            value = TextFieldValue(code, TextRange(code.length)),
            onValueChange = {
                if (phase == OtpPhase.Idle) onCodeChange(sanitizeRecoveryCode(it.text).take(RECOVERY_CODE_LENGTH))
            },
            enabled = phase == OtpPhase.Idle,
            singleLine = true,
            cursorBrush = SolidColor(Color.Transparent),
            keyboardOptions = KeyboardOptions(
                capitalization = KeyboardCapitalization.Characters,
                keyboardType = KeyboardType.Text,
                imeAction = ImeAction.Done,
            ),
            modifier = Modifier
                .size(1.dp)
                .alpha(0f)
                .focusRequester(focusRequester),
        )

        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(stageHeight)
                .graphicsLayer { rotationZ = orbitAngle },
            contentAlignment = Alignment.Center,
        ) {
            val cursor = code.length.coerceAtMost(RECOVERY_CODE_LENGTH - 1)
            for (i in 0 until RECOVERY_CODE_LENGTH) {
                val angle = Math.toRadians(-90.0 + i * 360.0 / RECOVERY_CODE_LENGTH)
                val rowX = (boxSize + gap) * (i - (RECOVERY_CODE_LENGTH - 1) / 2f)
                val targetX: Dp = when {
                    phase == OtpPhase.Success -> 0.dp
                    clustered -> radius * cos(angle).toFloat()
                    else -> rowX
                }
                val targetY: Dp = when {
                    phase == OtpPhase.Success -> 0.dp
                    clustered -> radius * sin(angle).toFloat()
                    else -> 0.dp
                }
                val offsetX by animateDpAsState(
                    targetX,
                    spring(dampingRatio = 0.68f, stiffness = Spring.StiffnessLow),
                    label = "otpX$i",
                )
                val offsetY by animateDpAsState(
                    targetY,
                    spring(dampingRatio = 0.68f, stiffness = Spring.StiffnessLow),
                    label = "otpY$i",
                )
                val cellScale by animateFloatAsState(
                    if (phase == OtpPhase.Success) 0.34f else if (errorStage == 2) 0.9f else 1f,
                    spring(dampingRatio = 0.72f, stiffness = Spring.StiffnessLow),
                    label = "otpScale$i",
                )
                val cellAlpha by animateFloatAsState(
                    if (phase == OtpPhase.Success || errorStage == 2) 0f else 1f,
                    tween(durationMillis = if (errorStage == 2) 60 else 380, delayMillis = if (phase == OtpPhase.Idle) i * 45 else 0),
                    label = "otpAlpha$i",
                )

                val char = code.getOrNull(i)?.toString().orEmpty()
                val focused = phase == OtpPhase.Idle && i == cursor && code.length < RECOVERY_CODE_LENGTH
                val borderColor = when {
                    phase == OtpPhase.Error -> MaterialTheme.colorScheme.error
                    phase == OtpPhase.Success -> Success
                    focused -> MaterialTheme.colorScheme.primary
                    char.isNotEmpty() -> MaterialTheme.colorScheme.primary.copy(alpha = 0.5f)
                    else -> MaterialTheme.colorScheme.outlineVariant
                }
                val borderWidth by animateDpAsState(if (focused) 2.dp else 1.5.dp, label = "otpBorder$i")
                val focusLift by animateFloatAsState(if (focused) 1.05f else 1f, label = "otpLift$i")

                Box(
                    modifier = Modifier
                        .offset(x = offsetX, y = offsetY)
                        .size(boxSize)
                        .rotate(-orbitAngle)
                        .scale(cellScale * focusLift)
                        .alpha(cellAlpha)
                        .clip(RoundedCornerShape(12.dp))
                        .background(
                            if (phase == OtpPhase.Error) MaterialTheme.colorScheme.error.copy(alpha = 0.07f)
                            else MaterialTheme.colorScheme.surface,
                        )
                        .border(borderWidth, borderColor, RoundedCornerShape(12.dp))
                        .clickable(enabled = phase == OtpPhase.Idle) {
                            focusRequester.requestFocus()
                            keyboard?.show()
                        },
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        char,
                        style = MaterialTheme.typography.headlineSmall,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface,
                    )
                }
            }
        }

        // Mảnh vỡ: mỗi ô tách thành 4 mảnh tam giác văng ra rồi tan.
        if (errorStage == 2) {
            val progress = shatter.value
            Box(modifier = Modifier.fillMaxWidth().height(stageHeight), contentAlignment = Alignment.Center) {
                for (i in 0 until RECOVERY_CODE_LENGTH) {
                    val rowX = (boxSize + gap) * (i - (RECOVERY_CODE_LENGTH - 1) / 2f)
                    val char = code.getOrNull(i)?.toString().orEmpty()
                    SHARD_FLIGHT.forEachIndexed { part, flight ->
                        // Nảy lên 20% quãng đầu, sau đó rơi theo p² nên nhanh dần như có trọng lực.
                        val fallY = if (progress < 0.2f) {
                            flight.lift * (progress / 0.2f)
                        } else {
                            val q = (progress - 0.2f) / 0.8f
                            flight.lift * (1f - q) + flight.fall * q * q
                        }
                        val fade = if (progress < 0.35f) 1f else ((1f - progress) / 0.65f).coerceIn(0f, 1f)
                        Box(
                            modifier = Modifier
                                .offset(x = rowX + (flight.drift * progress).dp, y = fallY.dp)
                                .size(boxSize)
                                .rotate(flight.rotate * progress)
                                .scale(1f - 0.14f * progress)
                                .alpha(fade)
                                .clip(shardShape(part))
                                .background(MaterialTheme.colorScheme.error.copy(alpha = 0.08f))
                                .border(1.dp, MaterialTheme.colorScheme.error.copy(alpha = 0.85f), shardShape(part)),
                            contentAlignment = Alignment.Center,
                        ) {
                            Text(
                                char,
                                style = MaterialTheme.typography.headlineSmall,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.onSurface,
                            )
                        }
                    }
                }
            }
        }

        if (phase == OtpPhase.Verifying) {
            CircularProgressIndicator(
                modifier = Modifier.size(30.dp),
                color = MaterialTheme.colorScheme.primary,
                trackColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.16f),
                strokeWidth = 2.5.dp,
            )
        }

        AnimatedVisibility(
            visible = phase == OtpPhase.Success,
            enter = scaleIn(spring(dampingRatio = 0.55f)) + fadeIn(),
            exit = scaleOut() + fadeOut(),
        ) {
            val checkColor = Color.White
            Box(
                modifier = Modifier
                    .size(68.dp)
                    .clip(CircleShape)
                    .background(Success),
                contentAlignment = Alignment.Center,
            ) {
                // Nét ✓ được VẼ dần chứ không hiện ra ngay — cảm giác "đã chốt".
                Canvas(modifier = Modifier.size(34.dp)) {
                    val path = Path().apply {
                        moveTo(size.width * 0.08f, size.height * 0.52f)
                        lineTo(size.width * 0.40f, size.height * 0.82f)
                        lineTo(size.width * 0.94f, size.height * 0.18f)
                    }
                    val measure = PathMeasure().apply { setPath(path, false) }
                    val drawn = Path()
                    measure.getSegment(0f, measure.length * sealDraw.value, drawn, true)
                    drawPath(
                        path = drawn,
                        color = checkColor,
                        style = Stroke(width = 5.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round),
                    )
                }
            }
        }
    }
}

@Composable
private fun PasswordResetField(
    label: String,
    value: String,
    show: Boolean,
    onShowChange: (Boolean) -> Unit,
    onChange: (String) -> Unit,
    imeAction: ImeAction,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onChange,
        label = { Text(label) },
        leadingIcon = { Icon(Icons.Filled.Lock, contentDescription = null) },
        trailingIcon = {
            IconButton(onClick = { onShowChange(!show) }) {
                Icon(
                    if (show) Icons.Filled.VisibilityOff else Icons.Filled.Visibility,
                    contentDescription = if (show) "Ẩn mật khẩu" else "Hiện mật khẩu",
                )
            }
        },
        singleLine = true,
        shape = RoundedCornerShape(14.dp),
        visualTransformation = if (show) VisualTransformation.None else PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = imeAction),
        modifier = Modifier.fillMaxWidth(),
    )
}
