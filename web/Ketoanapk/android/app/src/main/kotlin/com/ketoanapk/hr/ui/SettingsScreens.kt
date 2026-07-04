package com.ketoanapk.hr.ui

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.automirrored.filled.Login
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.Devices
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.PhoneAndroid
import androidx.compose.material.icons.filled.PrivacyTip
import androidx.compose.material.icons.filled.SystemUpdate
import androidx.compose.material.icons.filled.Description
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.ketoanapk.hr.data.AppUpdater
import com.ketoanapk.hr.data.DeviceSession
import com.ketoanapk.hr.data.HrUser

private enum class SettingsRoute { Home, WebLogin, ChangePassword, Devices, Notifications, Version, Terms, Privacy }

@Composable
fun SettingsScreen(user: HrUser, vm: HrViewModel, onLogout: () -> Unit) {
    var route by rememberSaveable { mutableStateOf(SettingsRoute.Home) }

    AnimatedContent(
        targetState = route,
        transitionSpec = {
            val forward = targetState != SettingsRoute.Home
            if (forward) {
                (slideInHorizontally { it } + fadeIn()) togetherWith (slideOutHorizontally { -it / 3 } + fadeOut())
            } else {
                (slideInHorizontally { -it / 3 } + fadeIn()) togetherWith (slideOutHorizontally { it } + fadeOut())
            }
        },
        label = "settings",
    ) { target ->
        when (target) {
            SettingsRoute.Home -> SettingsHome(user, vm, onOpen = { route = it }, onLogout = onLogout)
            SettingsRoute.WebLogin -> WebLoginSettings(vm) { route = SettingsRoute.Home }
            SettingsRoute.ChangePassword -> ChangePasswordScreen(vm) { route = SettingsRoute.Home }
            SettingsRoute.Devices -> DeviceManagerScreen(vm) { route = SettingsRoute.Home }
            SettingsRoute.Notifications -> NotificationSettings(vm) { route = SettingsRoute.Home }
            SettingsRoute.Version -> AppVersionScreen(vm) { route = SettingsRoute.Home }
            SettingsRoute.Terms -> ComingSoonScreen("Điều khoản sử dụng") { route = SettingsRoute.Home }
            SettingsRoute.Privacy -> ComingSoonScreen("Chính sách quyền riêng tư") { route = SettingsRoute.Home }
        }
    }
}

@Composable
private fun SettingsHome(
    user: HrUser,
    vm: HrViewModel,
    onOpen: (SettingsRoute) -> Unit,
    onLogout: () -> Unit,
) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item {
            HrCard {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    UserAvatar(user.displayName, 52)
                    Spacer(Modifier.width(12.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text(user.displayName, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(user.username, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    StatusChip(if (user.isAdmin) "Quản trị" else "Nhân viên", if (user.isAdmin) Tone.Neutral else Tone.Muted)
                }
            }
        }

        item { SectionTitle("Tài khoản", modifier = Modifier.padding(horizontal = 4.dp)) }
        item {
            SettingsGroup {
                SettingsRow(Icons.AutoMirrored.Filled.Login, "Cài đặt đăng nhập", "Bật/tắt đăng nhập trên web") { onOpen(SettingsRoute.WebLogin) }
                SettingsDivider()
                SettingsRow(Icons.Filled.Lock, "Đổi mật khẩu", "Cập nhật mật khẩu tài khoản") { onOpen(SettingsRoute.ChangePassword) }
                SettingsDivider()
                SettingsRow(Icons.Filled.Devices, "Quản lý thiết bị", "Phiên đăng nhập & thu hồi từ xa") { onOpen(SettingsRoute.Devices) }
                SettingsDivider()
                SettingsRow(Icons.Filled.Notifications, "Cài đặt thông báo", "Bật/tắt thông báo push của ứng dụng") { onOpen(SettingsRoute.Notifications) }
            }
        }

        item { SectionTitle("Ứng dụng", modifier = Modifier.padding(horizontal = 4.dp)) }
        item {
            SettingsGroup {
                SettingsRow(Icons.Filled.Description, "Điều khoản sử dụng", "Điều khoản dịch vụ") { onOpen(SettingsRoute.Terms) }
                SettingsDivider()
                SettingsRow(Icons.Filled.PrivacyTip, "Chính sách quyền riêng tư", "Cách dữ liệu được sử dụng") { onOpen(SettingsRoute.Privacy) }
                SettingsDivider()
                SettingsRow(Icons.Filled.Info, "Phiên bản ứng dụng", "Phiên bản & kiểm tra cập nhật") { onOpen(SettingsRoute.Version) }
            }
        }

        item {
            Button(
                onClick = onLogout,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(50.dp),
                shape = RoundedCornerShape(14.dp),
                colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
            ) {
                Icon(Icons.AutoMirrored.Filled.Logout, contentDescription = null, modifier = Modifier.size(20.dp))
                Spacer(Modifier.width(8.dp))
                Text("Đăng xuất", fontWeight = FontWeight.Bold)
            }
        }
    }
}

// ── Cài đặt đăng nhập web ────────────────────────────────────────────────────
@Composable
private fun WebLoginSettings(vm: HrViewModel, onBack: () -> Unit) {
    LaunchedEffect(Unit) { if (vm.settingsState.webLoginEnabled == null) vm.loadSettings() }
    val enabled = vm.settingsState.webLoginEnabled
    SubScreen("Cài đặt đăng nhập", onBack) {
        item {
            SettingsGroup {
                SettingsSwitchRow(
                    icon = Icons.AutoMirrored.Filled.Login,
                    title = "Đăng nhập trên web",
                    subtitle = "Cho phép dùng tài khoản này để đăng nhập trên trình duyệt web",
                    checked = enabled == true,
                    enabled = enabled != null,
                    onCheckedChange = { vm.setWebLoginEnabled(it) },
                )
            }
        }
        item {
            Text(
                "Khi tắt, tài khoản của bạn sẽ không thể đăng nhập trên trình duyệt web (kể cả bằng khuôn mặt). Ứng dụng trên điện thoại vẫn dùng bình thường.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 4.dp),
            )
        }
    }
}

// ── Đổi mật khẩu ─────────────────────────────────────────────────────────────
@Composable
private fun ChangePasswordScreen(vm: HrViewModel, onBack: () -> Unit) {
    var current by rememberSaveable { mutableStateOf("") }
    var next by rememberSaveable { mutableStateOf("") }
    var confirm by rememberSaveable { mutableStateOf("") }
    var submitting by remember { mutableStateOf(false) }
    var localError by remember { mutableStateOf<String?>(null) }

    SubScreen("Đổi mật khẩu", onBack) {
        item {
            SettingsGroup(padded = true) {
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    PasswordField("Mật khẩu hiện tại", current) { current = it }
                    PasswordField("Mật khẩu mới", next) { next = it }
                    PasswordField("Nhập lại mật khẩu mới", confirm) { confirm = it }
                    localError?.let { ErrorText(it) }
                    Button(
                        onClick = {
                            localError = when {
                                current.isBlank() -> "Vui lòng nhập mật khẩu hiện tại."
                                next.length < 4 -> "Mật khẩu mới phải có ít nhất 4 ký tự."
                                next != confirm -> "Mật khẩu nhập lại không khớp."
                                else -> null
                            }
                            if (localError == null) {
                                submitting = true
                                vm.changePassword(current, next) { ok ->
                                    submitting = false
                                    if (ok) { current = ""; next = ""; confirm = ""; onBack() }
                                }
                            }
                        },
                        enabled = !submitting,
                        shape = RoundedCornerShape(14.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(50.dp),
                    ) {
                        if (submitting) CircularProgressIndicator(Modifier.size(20.dp), MaterialTheme.colorScheme.onPrimary, 2.dp)
                        else Text("Cập nhật mật khẩu", fontWeight = FontWeight.Bold)
                    }
                }
            }
        }
    }
}

@Composable
private fun PasswordField(label: String, value: String, onChange: (String) -> Unit) {
    OutlinedTextField(
        value = value,
        onValueChange = onChange,
        label = { Text(label) },
        singleLine = true,
        shape = RoundedCornerShape(14.dp),
        visualTransformation = PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(keyboardType = androidx.compose.ui.text.input.KeyboardType.Password),
        modifier = Modifier.fillMaxWidth(),
    )
}

// ── Quản lý thiết bị ─────────────────────────────────────────────────────────
@Composable
private fun DeviceManagerScreen(vm: HrViewModel, onBack: () -> Unit) {
    LaunchedEffect(Unit) { vm.loadDevices() }
    val state = vm.settingsState
    SubScreen("Quản lý thiết bị", onBack) {
        if (state.devicesLoading && state.devices.isEmpty()) item { LoadingBlock() }
        if (!state.devicesLoading && state.devices.isEmpty()) {
            item { EmptyState("Chưa có thiết bị", "Không tìm thấy phiên đăng nhập nào.") }
        }
        items(state.devices, key = { it.sid }) { d -> DeviceCard(d) { vm.revokeDevice(d.sid) } }
    }
}

@Composable
private fun DeviceCard(d: DeviceSession, onRevoke: () -> Unit) {
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(
                modifier = Modifier
                    .size(40.dp)
                    .clip(RoundedCornerShape(11.dp))
                    .background(MaterialTheme.colorScheme.primaryContainer),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    if (d.clientKind.equals("Web", true)) Icons.Filled.Computer else Icons.Filled.PhoneAndroid,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onPrimaryContainer,
                    modifier = Modifier.size(22.dp),
                )
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(d.machineName.ifBlank { d.clientKind.ifBlank { "Thiết bị" } }, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(d.userAgent.ifBlank { "—" }, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("Hoạt động: ${formatIsoDateTime(d.lastSeen ?: "")}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
            }
            when {
                d.current -> StatusChip("Thiết bị này", Tone.Success)
                d.revoked -> StatusChip("Đã thu hồi", Tone.Muted)
                d.isActive -> StatusChip("Đang mở", Tone.Neutral)
                else -> StatusChip("Ngoại tuyến", Tone.Muted)
            }
        }
        if (!d.current && !d.revoked) {
            OutlinedButton(
                onClick = onRevoke,
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
            ) { Text("Thu hồi thiết bị") }
        }
    }
}

// ── Cài đặt thông báo ────────────────────────────────────────────────────────
@Composable
private fun NotificationSettings(vm: HrViewModel, onBack: () -> Unit) {
    val context = LocalContext.current
    var systemAllowed by remember { mutableStateOf(systemNotificationsAllowed(context)) }
    val permissionLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        systemAllowed = systemNotificationsAllowed(context)
        if (systemAllowed) vm.setPushNotificationsEnabled(true) else vm.onNotificationPermissionDenied()
    }
    val localEnabled = vm.settingsState.pushNotificationsEnabled
    val checked = localEnabled == true && systemAllowed

    LaunchedEffect(Unit) {
        vm.loadPushNotificationSetting()
        systemAllowed = systemNotificationsAllowed(context)
    }

    SubScreen("Cài đặt thông báo", onBack) {
        item {
            SettingsGroup {
                SettingsSwitchRow(
                    icon = Icons.Filled.Notifications,
                    title = "Thông báo push ứng dụng",
                    subtitle = pushNotificationSubtitle(localEnabled, systemAllowed),
                    checked = checked,
                    enabled = localEnabled != null,
                    onCheckedChange = { enable ->
                        if (!enable) {
                            vm.setPushNotificationsEnabled(false)
                            return@SettingsSwitchRow
                        }

                        systemAllowed = systemNotificationsAllowed(context)
                        if (systemAllowed) {
                            vm.setPushNotificationsEnabled(true)
                        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                            permissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                        } else {
                            vm.onNotificationPermissionDenied()
                        }
                    },
                )
            }
        }
        item {
            Text(
                "Khi bật, KetoanAPK sẽ nhận thông báo push cho đơn từ, phê duyệt, phạt/kỷ luật và các thông báo hệ thống. Khi tắt, app hủy đăng ký nhận push trên thiết bị này.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 4.dp),
            )
        }
    }
}

private fun systemNotificationsAllowed(context: Context): Boolean {
    val runtimeGranted = Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
        ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
    return runtimeGranted && NotificationManagerCompat.from(context).areNotificationsEnabled()
}

private fun pushNotificationSubtitle(localEnabled: Boolean?, systemAllowed: Boolean): String = when {
    localEnabled == null -> "Đang kiểm tra trạng thái thông báo"
    localEnabled && systemAllowed -> "Đang bật"
    localEnabled -> "Đang bật trong app, cần cấp quyền thông báo của hệ thống"
    !systemAllowed -> "Đang tắt, sẽ hỏi quyền thông báo khi bật"
    else -> "Đang tắt"
}

// ── Phiên bản ứng dụng ───────────────────────────────────────────────────────
@Composable
private fun AppVersionScreen(vm: HrViewModel, onBack: () -> Unit) {
    val context = LocalContext.current
    val versionName = remember { AppUpdater.installedVersionName(context) }
    val state = vm.settingsState
    SubScreen("Phiên bản ứng dụng", onBack) {
        item {
            HrCard {
                Column(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    Box(
                        modifier = Modifier
                            .size(64.dp)
                            .clip(RoundedCornerShape(18.dp))
                            .background(MaterialTheme.colorScheme.primaryContainer),
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(Icons.Filled.SystemUpdate, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(32.dp))
                    }
                    Text("KetoanAPK", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
                    Text("Phiên bản $versionName", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text("Ứng dụng thuần native · Kotlin & Compose", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }

        state.updateMessage?.let { msg ->
            item {
                Text(
                    msg,
                    style = MaterialTheme.typography.bodyMedium,
                    color = if (state.updateInfo != null) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(horizontal = 4.dp),
                )
            }
        }

        state.updateInfo?.let { info ->
            item {
                HrCard {
                    Text("Bản cập nhật ${info.version}", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
                    if (info.releaseNotes.isNotBlank()) {
                        Text(info.releaseNotes, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    Button(
                        onClick = { vm.installUpdate(context) },
                        enabled = !state.installing,
                        shape = RoundedCornerShape(14.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(48.dp),
                    ) {
                        if (state.installing) CircularProgressIndicator(Modifier.size(20.dp), MaterialTheme.colorScheme.onPrimary, 2.dp)
                        else Text("Cập nhật ngay", fontWeight = FontWeight.Bold)
                    }
                }
            }
        }

        item {
            OutlinedButton(
                onClick = { vm.checkForUpdate(context) },
                enabled = !state.checkingUpdate,
                shape = RoundedCornerShape(14.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(50.dp),
            ) {
                if (state.checkingUpdate) {
                    CircularProgressIndicator(Modifier.size(20.dp), MaterialTheme.colorScheme.primary, 2.dp)
                } else {
                    Icon(Icons.Filled.SystemUpdate, contentDescription = null, modifier = Modifier.size(20.dp))
                    Spacer(Modifier.width(8.dp))
                    Text("Kiểm tra cập nhật", fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}

@Composable
private fun ComingSoonScreen(title: String, onBack: () -> Unit) {
    SubScreen(title, onBack) {
        item { EmptyState(title, "Nội dung đang được cập nhật. Vui lòng quay lại sau.") }
    }
}

// ── Thành phần dùng chung ────────────────────────────────────────────────────
@Composable
private fun SubScreen(
    title: String,
    onBack: () -> Unit,
    content: androidx.compose.foundation.lazy.LazyListScope.() -> Unit,
) {
    Column(modifier = Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 6.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Quay lại") }
            Text(title, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 14.dp, end = 14.dp, bottom = 24.dp, top = 4.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
            content = content,
        )
    }
}

@Composable
private fun SettingsGroup(padded: Boolean = false, content: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit) {
    androidx.compose.material3.Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surface,
        border = androidx.compose.foundation.BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
    ) {
        Column(modifier = if (padded) Modifier.padding(14.dp) else Modifier, content = content)
    }
}

@Composable
private fun SettingsDivider() {
    androidx.compose.material3.HorizontalDivider(
        modifier = Modifier.padding(start = 62.dp),
        color = MaterialTheme.colorScheme.outline,
    )
}

@Composable
private fun SettingsRow(icon: ImageVector, title: String, subtitle: String, onClick: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 13.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        SettingsIcon(icon)
        Spacer(Modifier.width(14.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun SettingsSwitchRow(
    icon: ImageVector,
    title: String,
    subtitle: String,
    checked: Boolean,
    enabled: Boolean,
    onCheckedChange: (Boolean) -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 14.dp, vertical = 11.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        SettingsIcon(icon)
        Spacer(Modifier.width(14.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface)
            Text(subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Switch(checked = checked, onCheckedChange = onCheckedChange, enabled = enabled)
    }
}

@Composable
private fun SettingsIcon(icon: ImageVector) {
    Box(
        modifier = Modifier
            .size(34.dp)
            .clip(RoundedCornerShape(10.dp))
            .background(MaterialTheme.colorScheme.primaryContainer),
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(19.dp))
    }
}
