package com.ketoanapk.hr.ui

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
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
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.ketoanapk.hr.ui.theme.BrandGradientBottom
import com.ketoanapk.hr.ui.theme.BrandGradientTop
import com.ketoanapk.hr.ui.theme.Success

private enum class ForgotPhase { Form, Scanning, Submitting, Success }

@Composable
fun LoginScreen(
    loading: Boolean,
    error: String?,
    resetLoading: Boolean,
    rememberedUsername: String,
    onLogin: (String, String, Boolean) -> Unit,
    onResetPasswordWithFace: (String, String, List<String>, (Boolean, String?) -> Unit) -> Unit,
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

                    androidx.compose.foundation.layout.Row(
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

        if (forgotOpen) {
            ForgotPasswordOverlay(
                username = username,
                onUsernameChange = { username = it },
                resetLoading = resetLoading,
                onResetPasswordWithFace = onResetPasswordWithFace,
                onClose = { forgotOpen = false },
            )
        }
    }
}

@Composable
private fun ForgotPasswordOverlay(
    username: String,
    onUsernameChange: (String) -> Unit,
    resetLoading: Boolean,
    onResetPasswordWithFace: (String, String, List<String>, (Boolean, String?) -> Unit) -> Unit,
    onClose: () -> Unit,
) {
    val context = LocalContext.current
    var phase by rememberSaveable { mutableStateOf(ForgotPhase.Form) }
    var newPassword by rememberSaveable { mutableStateOf("") }
    var confirmPassword by rememberSaveable { mutableStateOf("") }
    var showNewPassword by remember { mutableStateOf(false) }
    var showConfirmPassword by remember { mutableStateOf(false) }
    var localError by rememberSaveable { mutableStateOf<String?>(null) }
    var hasCamera by remember {
        mutableStateOf(
            ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED,
        )
    }
    val permissionLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        hasCamera = granted
        if (granted) phase = ForgotPhase.Scanning
        else localError = "Ứng dụng cần quyền camera để xác thực khuôn mặt."
    }

    fun validate(): Boolean {
        val u = username.trim()
        localError = when {
            u.isBlank() -> "Vui lòng nhập tên đăng nhập."
            newPassword.length < 6 -> "Mật khẩu mới cần ít nhất 6 ký tự."
            newPassword != confirmPassword -> "Mật khẩu nhập lại chưa khớp."
            else -> null
        }
        return localError == null
    }

    fun startScan() {
        if (!validate()) return
        if (hasCamera) phase = ForgotPhase.Scanning else permissionLauncher.launch(Manifest.permission.CAMERA)
    }

    fun close() {
        if (phase != ForgotPhase.Submitting && !resetLoading) onClose()
    }

    BackHandler(enabled = phase != ForgotPhase.Submitting && !resetLoading) {
        if (phase == ForgotPhase.Scanning) phase = ForgotPhase.Form else onClose()
    }

    when (phase) {
        ForgotPhase.Scanning -> FullScreenCameraScan(
            onCaptured = { frames ->
                phase = ForgotPhase.Submitting
                localError = null
                // Đăng nhập/đặt lại mật khẩu không chạy active-flash → chỉ lấy ảnh, bỏ nhãn slot.
                onResetPasswordWithFace(username.trim(), newPassword, frames.map { it.image }) { ok, message ->
                    if (ok) {
                        phase = ForgotPhase.Success
                        newPassword = ""
                        confirmPassword = ""
                    } else {
                        phase = ForgotPhase.Form
                        localError = message ?: "Không đặt lại được mật khẩu. Vui lòng thử lại."
                    }
                }
            },
            onCancel = { phase = ForgotPhase.Form },
        )

        else -> Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color(0xF20E0F12))
                .systemBarsPadding()
                .imePadding()
                .padding(20.dp),
            contentAlignment = Alignment.Center,
        ) {
            Surface(
                modifier = Modifier
                    .fillMaxWidth()
                    .widthIn(max = 440.dp),
                shape = RoundedCornerShape(24.dp),
                color = MaterialTheme.colorScheme.surface,
                shadowElevation = 10.dp,
            ) {
                Column(
                    modifier = Modifier.padding(20.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Icon(
                        if (phase == ForgotPhase.Success) Icons.Filled.CheckCircle else Icons.Filled.Face,
                        contentDescription = null,
                        tint = if (phase == ForgotPhase.Success) Success else MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(44.dp),
                    )
                    Text(
                        if (phase == ForgotPhase.Success) "Đã đổi mật khẩu" else "Quên mật khẩu",
                        style = MaterialTheme.typography.titleLarge,
                        color = MaterialTheme.colorScheme.onSurface,
                        fontWeight = FontWeight.Bold,
                        textAlign = TextAlign.Center,
                    )

                    when (phase) {
                        ForgotPhase.Submitting -> {
                            CircularProgressIndicator(color = MaterialTheme.colorScheme.primary)
                            Text(
                                "Đang xác thực khuôn mặt và cập nhật mật khẩu…",
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                textAlign = TextAlign.Center,
                            )
                        }

                        ForgotPhase.Success -> {
                            Text(
                                "Bạn có thể đăng nhập bằng mật khẩu mới.",
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                textAlign = TextAlign.Center,
                            )
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

                        else -> {
                            OutlinedTextField(
                                value = username,
                                onValueChange = onUsernameChange,
                                label = { Text("Tên đăng nhập") },
                                leadingIcon = { Icon(Icons.Filled.Person, contentDescription = null) },
                                singleLine = true,
                                shape = RoundedCornerShape(14.dp),
                                modifier = Modifier.fillMaxWidth(),
                                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
                            )
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
                                onClick = { startScan() },
                                enabled = !resetLoading,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .height(50.dp),
                                shape = RoundedCornerShape(14.dp),
                            ) {
                                Icon(Icons.Filled.Face, contentDescription = null, modifier = Modifier.size(20.dp))
                                Spacer(Modifier.width(8.dp))
                                Text("Quét mặt để đổi mật khẩu", fontWeight = FontWeight.Bold)
                            }
                        }
                    }
                }
            }

            IconButton(
                onClick = { close() },
                enabled = phase != ForgotPhase.Submitting && !resetLoading,
                modifier = Modifier
                    .align(Alignment.TopEnd)
                    .statusBarsPadding()
                    .padding(8.dp),
            ) {
                Icon(Icons.Filled.Close, contentDescription = "Đóng", tint = Color.White)
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
