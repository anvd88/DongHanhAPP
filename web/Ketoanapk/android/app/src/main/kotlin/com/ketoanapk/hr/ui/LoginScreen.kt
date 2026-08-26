package com.ketoanapk.hr.ui

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.FastOutLinearInEasing
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
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
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
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
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.ketoanapk.hr.ui.theme.BrandGradientBottom
import com.ketoanapk.hr.ui.theme.BrandGradientTop
import com.ketoanapk.hr.ui.theme.Success
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private enum class ForgotPhase { Form, Submitting, Success }

/** Ba bước của màn quên mật khẩu: tên đăng nhập → mã khôi phục (OTP) → mật khẩu mới. */
private enum class ForgotStep { Username, Code, Password }

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
    var otpPhase by rememberSaveable { mutableStateOf(CodeCellsPhase.Idle) }
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
    val busy = phase == ForgotPhase.Submitting || otpPhase == CodeCellsPhase.Verifying || otpPhase == CodeCellsPhase.Success

    fun goStep(next: ForgotStep) {
        step = next
        localError = null
        if (next == ForgotStep.Code) {
            code = ""
            otpPhase = CodeCellsPhase.Idle
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
        if (step != ForgotStep.Code || otpPhase != CodeCellsPhase.Idle) return@LaunchedEffect
        if (code.length < RECOVERY_CODE_LENGTH) return@LaunchedEffect
        otpPhase = CodeCellsPhase.Verifying
        localError = null
        resendHint = null
        val startedAt = System.currentTimeMillis()
        onVerifyRecoveryCode(username.trim(), code) { ok, message ->
            scope.launch {
                // Giữ hoạt cảnh đủ lâu để nhìn được, kể cả khi máy chủ trong LAN trả lời tức thì.
                val remain = (if (ok) 1100L else 800L) - (System.currentTimeMillis() - startedAt)
                if (remain > 0) delay(remain)
                if (ok) {
                    otpPhase = CodeCellsPhase.Success
                    delay(CODE_CELLS_SUCCESS_MILLIS)
                    goStep(ForgotStep.Password)
                } else {
                    otpPhase = CodeCellsPhase.Error
                    localError = message ?: "Mã khôi phục không đúng hoặc đã hết hạn."
                    // Chờ rung + nứt + rơi xong mới dựng lại hàng ô trống.
                    delay(CODE_CELLS_ERROR_MILLIS)
                    code = ""
                    otpPhase = CodeCellsPhase.Idle
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
                                CodeCellsPhase.Verifying -> "Đang xác thực mã…"
                                CodeCellsPhase.Success -> "Xác thực thành công"
                                CodeCellsPhase.Error -> localError ?: "Mã OTP không chính xác"
                                CodeCellsPhase.Idle -> ""
                            }
                            if (statusText.isNotEmpty()) {
                                Text(
                                    statusText,
                                    style = MaterialTheme.typography.bodyMedium,
                                    fontWeight = FontWeight.Bold,
                                    color = when (otpPhase) {
                                        CodeCellsPhase.Success -> Success
                                        CodeCellsPhase.Error -> MaterialTheme.colorScheme.error
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
                                    enabled = otpPhase == CodeCellsPhase.Idle,
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
 * 5 ô mã khôi phục. Phần nhìn + hoạt cảnh nằm ở [AnimatedCodeCells] (dùng chung với bảng mã bảo mật
 * của ứng dụng); ở đây chỉ thêm ô nhập THẬT là một BasicTextField ẩn, vì bàn phím Android cần đúng
 * một ô để gõ liền mạch — 5 ô kia là phần nhìn.
 */
@Composable
private fun RecoveryOtpBoxes(
    code: String,
    phase: CodeCellsPhase,
    onCodeChange: (String) -> Unit,
) {
    val focusRequester = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current

    LaunchedEffect(phase) {
        if (phase != CodeCellsPhase.Idle) return@LaunchedEffect
        // Chờ một nhịp cho FocusRequester gắn xong (màn hình vừa trượt vào) rồi mới đòi con trỏ.
        delay(140)
        runCatching { focusRequester.requestFocus() }
        keyboard?.show()
    }

    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        BasicTextField(
            value = TextFieldValue(code, TextRange(code.length)),
            onValueChange = {
                if (phase == CodeCellsPhase.Idle) onCodeChange(sanitizeRecoveryCode(it.text).take(RECOVERY_CODE_LENGTH))
            },
            enabled = phase == CodeCellsPhase.Idle,
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

        AnimatedCodeCells(
            count = RECOVERY_CODE_LENGTH,
            filled = code.length,
            phase = phase,
            cellText = { code.getOrNull(it)?.toString().orEmpty() },
            onCellClick = {
                focusRequester.requestFocus()
                keyboard?.show()
            },
        )
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
