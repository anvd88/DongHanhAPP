package com.ketoanapk.hr.ui

import android.Manifest
import android.app.NotificationManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.activity.compose.BackHandler
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
import androidx.compose.material.icons.filled.AutoAwesome
import androidx.compose.material.icons.filled.CleaningServices
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.Devices
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.PhoneAndroid
import androidx.compose.material.icons.filled.PrivacyTip
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Shield
import androidx.compose.material.icons.filled.Storage
import androidx.compose.material.icons.filled.SystemUpdate
import androidx.compose.material.icons.filled.Tune
import androidx.compose.material.icons.filled.Description
import androidx.compose.material3.Button
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.FilterChip
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
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
import com.ketoanapk.hr.data.AppPinStore
import com.ketoanapk.hr.data.AppPersonalization
import com.ketoanapk.hr.data.DeviceSession
import com.ketoanapk.hr.data.HrUser

enum class SettingsRoute { Home, WebLogin, ChangePassword, AppPin, Devices, Notifications, Permissions, Personalization, FaceEnroll, Version, Terms, Privacy, Storage }

private data class LegalSection(
    val title: String,
    val paragraphs: List<String>,
)

@Composable
fun SettingsScreen(user: HrUser, vm: HrViewModel, onScanQr: () -> Unit, onLogout: () -> Unit) {
    // Màn con hiện tại lấy từ ViewModel để nút Back của điện thoại lùi được về Cài đặt gốc.
    val route = vm.settingsRoute

    // Đang ở màn con (Đổi mật khẩu, Thiết bị...) → Back lùi về Cài đặt gốc trước, chưa rời tab.
    BackHandler(enabled = route != SettingsRoute.Home) { vm.settingsRoute = SettingsRoute.Home }

    // Bấm "Đăng ký ngay" từ banner nhắc → tự mở thẳng màn Đăng ký khuôn mặt.
    LaunchedEffect(vm.openFaceEnroll) {
        if (vm.openFaceEnroll) {
            vm.settingsRoute = SettingsRoute.FaceEnroll
            vm.clearOpenFaceEnroll()
        }
    }

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
        val goHome = { vm.settingsRoute = SettingsRoute.Home }
        when (target) {
            SettingsRoute.Home -> SettingsHome(
                user = user,
                vm = vm,
                onOpen = { vm.settingsRoute = it },
                onScanQr = onScanQr,
                onLogout = onLogout,
            )
            SettingsRoute.WebLogin -> WebLoginSettings(vm, goHome)
            SettingsRoute.ChangePassword -> ChangePasswordScreen(vm, goHome)
            SettingsRoute.AppPin -> AppPinSettingsScreen(user, vm, goHome)
            SettingsRoute.Devices -> DeviceManagerScreen(vm, goHome)
            SettingsRoute.Notifications -> NotificationSettings(vm, goHome)
            SettingsRoute.Permissions -> PermissionCenterScreen(vm, goHome)
            SettingsRoute.Personalization -> PersonalizationSettings(goHome)
            SettingsRoute.FaceEnroll -> FaceEnrollScreen(vm, goHome)
            SettingsRoute.Version -> AppVersionScreen(vm, goHome)
            SettingsRoute.Storage -> StorageScreen(vm, goHome)
            SettingsRoute.Terms -> TermsScreen(goHome)
            SettingsRoute.Privacy -> PrivacyPolicyScreen(goHome)
        }
    }
}

@Composable
private fun SettingsHome(
    user: HrUser,
    vm: HrViewModel,
    onOpen: (SettingsRoute) -> Unit,
    onScanQr: () -> Unit,
    onLogout: () -> Unit,
) {
    LaunchedEffect(Unit) { vm.loadFaceStatus() }
    val canPreviewAnniversary = user.isAdmin ||
        user.role.equals("HR", ignoreCase = true) ||
        user.roles.any { it.equals("HR", ignoreCase = true) }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item {
            // Bấm vào thẻ tên → mở trang Hồ sơ cá nhân.
            HrCard(modifier = Modifier.clickable { vm.select(HrDestination.Profile) }) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    UserAvatar(user.displayName, 52)
                    Spacer(Modifier.width(12.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text(user.displayName, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(user.username, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    StatusChip(if (user.isAdmin) "Quản trị" else "Nhân viên", if (user.isAdmin) Tone.Neutral else Tone.Muted)
                    Spacer(Modifier.width(6.dp))
                    Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = "Xem hồ sơ", tint = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }

        item { SectionTitle("Tài khoản", modifier = Modifier.padding(horizontal = 4.dp)) }
        item {
            SettingsGroup {
                SettingsRow(Icons.Filled.QrCodeScanner, "Quét mã QR", "Đăng nhập web và các tác vụ QR do máy chủ hỗ trợ") {
                    onScanQr()
                }
                SettingsDivider()
                SettingsRow(Icons.AutoMirrored.Filled.Login, "Cài đặt đăng nhập", "Bật/tắt đăng nhập trên web") { onOpen(SettingsRoute.WebLogin) }
                SettingsDivider()
                SettingsRow(Icons.Filled.Lock, "Đổi mật khẩu", "Cập nhật mật khẩu tài khoản") { onOpen(SettingsRoute.ChangePassword) }
                SettingsDivider()
                SettingsRow(Icons.Filled.Shield, "Mã bảo mật ứng dụng", "PIN riêng 6 số, không dùng mã khóa điện thoại") { onOpen(SettingsRoute.AppPin) }
                SettingsDivider()
                SettingsRow(Icons.Filled.Devices, "Quản lý thiết bị", "Phiên đăng nhập & thu hồi từ xa") { onOpen(SettingsRoute.Devices) }
                SettingsDivider()
                SettingsRow(
                    Icons.Filled.Face,
                    "Đăng ký khuôn mặt",
                    when (vm.faceRegistered) {
                        true -> "Đã đăng ký · dùng cho chấm công và đăng nhập"
                        false -> "Quét khuôn mặt để chấm công & đăng nhập"
                        null -> "Chấm công và đăng nhập bằng khuôn mặt"
                    },
                ) { onOpen(SettingsRoute.FaceEnroll) }
                SettingsDivider()
                SettingsRow(Icons.Filled.Notifications, "Cài đặt thông báo", "Bật/tắt thông báo push của ứng dụng") { onOpen(SettingsRoute.Notifications) }
                SettingsDivider()
                SettingsRow(Icons.Filled.PrivacyTip, "Quyền ứng dụng", "Camera, micro, vị trí và cuộc gọi đến") { onOpen(SettingsRoute.Permissions) }
                SettingsDivider()
                SettingsRow(Icons.Filled.Tune, "Cá nhân hóa & trợ năng", "Giao diện, cỡ chữ, ngôn ngữ và tiết kiệm dữ liệu") { onOpen(SettingsRoute.Personalization) }
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
                SettingsDivider()
                SettingsRow(Icons.Filled.Storage, "Bộ nhớ & dữ liệu tạm", "Xem dung lượng và dọn cache của ứng dụng") { onOpen(SettingsRoute.Storage) }
                if (canPreviewAnniversary) {
                    SettingsDivider()
                    SettingsRow(
                        Icons.Filled.AutoAwesome,
                        "Xem thử thư tri ân",
                        if (vm.anniversaryPreviewLoading) "Đang chuẩn bị bản xem thử…" else "Mở mẫu thư 5 năm với hiệu ứng máy đánh chữ",
                    ) { vm.previewAnniversaryGreeting() }
                }
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

@Composable
private fun PersonalizationSettings(onBack: () -> Unit) {
    SubScreen("Cá nhân hóa & trợ năng", onBack) {
        item {
            SettingsGroup(padded = true) {
                Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                    Text("Giao diện", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        listOf("system" to "Theo máy", "light" to "Sáng", "dark" to "Tối").forEach { (value, label) ->
                            FilterChip(selected = AppPersonalization.themeMode == value, onClick = { AppPersonalization.setTheme(value) }, label = { Text(label) })
                        }
                    }
                    Text("Cỡ chữ: ${(AppPersonalization.fontScale * 100).toInt()}%", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
                    Slider(value = AppPersonalization.fontScale, onValueChange = AppPersonalization::setFont, valueRange = .85f..1.3f, steps = 8)
                    Text("Ngôn ngữ", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        FilterChip(selected = AppPersonalization.language == "vi", onClick = { AppPersonalization.updateLanguage("vi") }, label = { Text("Tiếng Việt") })
                        FilterChip(selected = AppPersonalization.language == "en", onClick = { AppPersonalization.updateLanguage("en") }, label = { Text("English") })
                    }
                    SettingsSwitchRow(Icons.Filled.Tune, "Đưa tác vụ nhanh lên trước", "Thay đổi thứ tự các thẻ trên trang chủ", AppPersonalization.reverseHomeCards, enabled = true, onCheckedChange = AppPersonalization::setReverseHome)
                    SettingsSwitchRow(Icons.Filled.PhoneAndroid, "Tiết kiệm dữ liệu", "Hạn chế dữ liệu chạy ngầm khi dùng mạng di động", AppPersonalization.dataSaver, enabled = true, onCheckedChange = AppPersonalization::updateDataSaver)
                    Text(
                        "Mục đích: đỡ hao dữ liệu di động và pin. Khi BẬT, ứng dụng giãn nhịp tự động đồng bộ ở chế " +
                            "độ nền (tối đa mỗi 60 giây/lần thay vì liên tục). Bạn vẫn nhận đủ tin nhắn, thông báo và " +
                            "số liệu mới nhất mỗi khi mở app hoặc kéo để làm mới — chỉ là app bớt âm thầm tải dữ liệu " +
                            "khi bạn không dùng tới. Nên bật khi dùng 3G/4G/5G hoặc gói cước giới hạn; tắt khi ở Wi-Fi " +
                            "để cập nhật tức thời hơn. Ngoài ra, khi dùng dữ liệu di động, bản cập nhật lớn (trên 20MB) " +
                            "sẽ luôn hỏi bạn trước khi tải.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Text("Ứng dụng hỗ trợ TalkBack, cỡ chữ hệ thống và vùng chạm tối thiểu 48dp cho các thao tác chính.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
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

// ── Mã bảo mật riêng của ứng dụng ───────────────────────────────────────────
@Composable
private fun AppPinSettingsScreen(user: HrUser, vm: HrViewModel, onBack: () -> Unit) {
    val context = LocalContext.current
    val store = remember(context) { AppPinStore(context) }
    var hasPin by remember(user.username) { mutableStateOf<Boolean?>(null) }
    var loadError by remember(user.username) { mutableStateOf<String?>(null) }
    var showGate by remember { mutableStateOf(false) }

    LaunchedEffect(user.username) {
        runCatching { store.hasPin(user.username) }
            .onSuccess { hasPin = it; loadError = null }
            .onFailure { hasPin = true; loadError = it.message }
    }

    SubScreen("Mã bảo mật ứng dụng", onBack) {
        item {
            SettingsGroup(padded = true) {
                Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(
                            modifier = Modifier.size(46.dp).clip(CircleShape).background(MaterialTheme.colorScheme.primaryContainer),
                            contentAlignment = Alignment.Center,
                        ) {
                            Icon(Icons.Filled.Shield, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        }
                        Spacer(Modifier.width(12.dp))
                        Column(Modifier.weight(1f)) {
                            Text("PIN riêng 6 số", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                            Text(
                                when (hasPin) {
                                    true -> "Đã thiết lập trên thiết bị này"
                                    false -> "Chưa thiết lập"
                                    null -> "Đang kiểm tra…"
                                },
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                    loadError?.let { ErrorText(it) }
                    Text(
                        "Mã này dùng để mở hồ sơ điện tử, phiếu lương và các dữ liệu nhạy cảm trong app. Mã không liên kết với PIN, hình vẽ hoặc mật khẩu mở khóa điện thoại.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Button(
                        onClick = { showGate = true },
                        enabled = hasPin != null,
                        modifier = Modifier.fillMaxWidth().height(50.dp),
                        shape = RoundedCornerShape(14.dp),
                    ) {
                        Text(if (hasPin == true) "Đổi mã bảo mật" else "Tạo mã bảo mật", fontWeight = FontWeight.Bold)
                    }
                    Text(
                        "Nếu quên mã, chọn “Quên mã bảo mật?” và xác minh lại bằng mật khẩu tài khoản để tạo mã mới.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }
    }

    AppPinGate(
        visible = showGate,
        username = user.username,
        purpose = "Nhập mã hiện tại trước khi đổi mã bảo mật.",
        mode = AppPinGateMode.Manage,
        onDismiss = { showGate = false },
        onUnlocked = {
            showGate = false
            hasPin = true
            loadError = null
            vm.showActionMessage("Đã cập nhật mã bảo mật ứng dụng.")
        },
        onVerifyAccountPassword = vm::verifyAccountPassword,
    )
}

// ── Quản lý thiết bị ─────────────────────────────────────────────────────────
@Composable
private fun DeviceManagerScreen(vm: HrViewModel, onBack: () -> Unit) {
    LaunchedEffect(Unit) { vm.loadDevices() }
    val state = vm.settingsState
    var confirmAll by remember{mutableStateOf(false)}
    SubScreen("Quản lý thiết bị", onBack) {
        item{Button(onClick={confirmAll=true},colors=ButtonDefaults.buttonColors(containerColor=MaterialTheme.colorScheme.error),modifier=Modifier.fillMaxWidth()){Text("Đăng xuất khỏi tất cả thiết bị")}}
        if (state.devicesLoading && state.devices.isEmpty()) item { LoadingBlock() }
        if (!state.devicesLoading && state.devices.isEmpty()) {
            item { EmptyState("Chưa có thiết bị", "Không tìm thấy phiên đăng nhập nào.") }
        }
        items(state.devices, key = { it.sid }) { d -> DeviceCard(d) { vm.revokeDevice(d.sid) } }
    }
    if(confirmAll)AlertDialog(onDismissRequest={confirmAll=false},title={Text("Đăng xuất tất cả thiết bị?")},text={Text("Tất cả phiên, kể cả thiết bị này, sẽ bị thu hồi.")},confirmButton={Button(onClick={confirmAll=false;vm.revokeAllDevices()}){Text("Đăng xuất tất cả")}},dismissButton={TextButton(onClick={confirmAll=false}){Text("Hủy")}})
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

@Composable
private fun PermissionCenterScreen(vm: HrViewModel, onBack: () -> Unit) {
    val context = LocalContext.current
    var refreshKey by remember { mutableStateOf(0) }
    var notificationDenied by rememberSaveable { mutableStateOf(false) }
    val notificationLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        notificationDenied = !granted
        refreshKey++
        vm.refreshPushPermissionState()
        if (granted) vm.setPushNotificationsEnabled(true)
    }

    fun granted(permission: String): Boolean =
        ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED
    fun openAppSettings() {
        context.startActivity(
            Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:${context.packageName}")),
        )
    }

    val notificationsGranted = remember(refreshKey) { systemNotificationsAllowed(context) }
    val fullScreenGranted = Build.VERSION.SDK_INT < Build.VERSION_CODES.UPSIDE_DOWN_CAKE ||
        (context.getSystemService(NotificationManager::class.java)?.canUseFullScreenIntent() == true)

    SubScreen("Quyền ứng dụng", onBack) {
        item {
            HrCard {
                Text("Bạn kiểm soát các quyền", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                Text(
                    "KetoanAPK không xin camera, micro hoặc vị trí khi khởi động. Các quyền này chỉ được hỏi khi bạn mở đúng tính năng cần dùng.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        item {
            PermissionStatusCard(
                icon = Icons.Filled.Notifications,
                title = "Thông báo",
                explanation = "Dùng cho đơn từ, tin nhắn và cuộc gọi đến khi app ở nền.",
                granted = notificationsGranted,
                actionLabel = if (notificationDenied) "Mở cài đặt" else "Cho phép",
                onAction = {
                    if (notificationDenied || Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) openAppSettings()
                    else notificationLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                },
            )
        }
        item {
            PermissionStatusCard(
                icon = Icons.Filled.CameraAlt,
                title = "Camera",
                explanation = "Chỉ dùng khi chấm công, chụp hồ sơ, đăng ký khuôn mặt hoặc gọi video.",
                granted = granted(Manifest.permission.CAMERA),
                actionLabel = "Mở cài đặt",
                onAction = ::openAppSettings,
            )
        }
        item {
            PermissionStatusCard(
                icon = Icons.Filled.Mic,
                title = "Micro",
                explanation = "Chỉ dùng sau khi bạn bắt đầu hoặc nhận cuộc gọi thoại/video.",
                granted = granted(Manifest.permission.RECORD_AUDIO),
                actionLabel = "Mở cài đặt",
                onAction = ::openAppSettings,
            )
        }
        item {
            PermissionStatusCard(
                icon = Icons.Filled.LocationOn,
                title = "Vị trí",
                explanation = "Chỉ dùng trong chấm công để kiểm tra địa điểm và lưu lượt ngoại tuyến.",
                granted = granted(Manifest.permission.ACCESS_FINE_LOCATION) || granted(Manifest.permission.ACCESS_COARSE_LOCATION),
                actionLabel = "Mở cài đặt",
                onAction = ::openAppSettings,
            )
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            item {
                PermissionStatusCard(
                    icon = Icons.Filled.PhoneAndroid,
                    title = "Cuộc gọi toàn màn hình",
                    explanation = "Tùy chọn để hiện cuộc gọi đến trên màn hình khóa. App không tự mở trang này khi khởi động.",
                    granted = fullScreenGranted,
                    actionLabel = "Bật thủ công",
                    onAction = {
                        context.startActivity(
                            Intent(
                                Settings.ACTION_MANAGE_APP_USE_FULL_SCREEN_INTENT,
                                Uri.parse("package:${context.packageName}"),
                            ),
                        )
                    },
                )
            }
        }
    }
}

@Composable
private fun PermissionStatusCard(
    icon: ImageVector,
    title: String,
    explanation: String,
    granted: Boolean,
    actionLabel: String,
    onAction: () -> Unit,
) {
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(26.dp))
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(title, fontWeight = FontWeight.Bold)
                Text(explanation, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            StatusChip(if (granted) "Đã cấp" else "Chưa cấp", if (granted) Tone.Success else Tone.Muted)
        }
        if (!granted) {
            OutlinedButton(onClick = onAction, modifier = Modifier.fillMaxWidth()) { Text(actionLabel) }
        }
    }
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
                    Text(
                        "Dung lượng ${AppUpdater.formatSize(context, info.apkSize)}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    if (info.releaseNotes.isNotBlank()) {
                        Text(info.releaseNotes, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    // Bấm là mở BẢNG cập nhật dùng chung (có tiến độ tải), không tải ngầm sau lưng nữa.
                    Button(
                        onClick = { vm.installUpdate(context) },
                        shape = RoundedCornerShape(14.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(48.dp),
                    ) { Text("Cập nhật ngay", fontWeight = FontWeight.Bold) }
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

// ── Bộ nhớ & dữ liệu tạm ─────────────────────────────────────────────────────
@Composable
private fun StorageScreen(vm: HrViewModel, onBack: () -> Unit) {
    var showConfirm by remember { mutableStateOf(false) }
    // Đo lại mỗi lần mở màn để con số phản ánh đúng hiện trạng (vd vừa tải xong gói cập nhật).
    LaunchedEffect(Unit) { vm.loadCacheSize() }

    SubScreen("Bộ nhớ & dữ liệu tạm", onBack) {
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
                        Icon(Icons.Filled.CleaningServices, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(32.dp))
                    }
                    Text(vm.cacheSizeText ?: "Đang tính…", style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
                    Text("Dung lượng dữ liệu tạm đang chiếm", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }

        item {
            SettingsGroup(padded = true) {
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text("Dọn cache sẽ xoá", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
                    BulletLine("Gói cập nhật đã tải sẵn")
                    BulletLine("Ảnh, PDF và âm thanh tạm")
                    BulletLine("Bộ đệm chat và ảnh chụp Trang chủ (giúp mở app nhanh)")
                    Spacer(Modifier.height(4.dp))
                    Text(
                        "Không ảnh hưởng: phiên đăng nhập, đơn nháp chưa gửi, và các lượt chấm công ngoại tuyến " +
                            "chưa đồng bộ. Dữ liệu đã dọn sẽ tự tải lại khi cần.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }

        item {
            Button(
                onClick = { showConfirm = true },
                enabled = !vm.cacheClearing,
                shape = RoundedCornerShape(14.dp),
                colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(50.dp),
            ) {
                if (vm.cacheClearing) {
                    CircularProgressIndicator(Modifier.size(20.dp), MaterialTheme.colorScheme.onError, 2.dp)
                } else {
                    Icon(Icons.Filled.CleaningServices, contentDescription = null, modifier = Modifier.size(20.dp))
                    Spacer(Modifier.width(8.dp))
                    Text("Dọn cache ngay", fontWeight = FontWeight.Bold)
                }
            }
        }
    }

    if (showConfirm) {
        AlertDialog(
            onDismissRequest = { showConfirm = false },
            icon = { Icon(Icons.Filled.CleaningServices, contentDescription = null) },
            title = { Text("Dọn cache?") },
            text = { Text("Ứng dụng sẽ xoá dữ liệu tạm để giải phóng bộ nhớ. Phiên đăng nhập và các dữ liệu chưa gửi vẫn được giữ nguyên.") },
            confirmButton = { TextButton(onClick = { showConfirm = false; vm.clearCache() }) { Text("Dọn cache") } },
            dismissButton = { TextButton(onClick = { showConfirm = false }) { Text("Huỷ") } },
        )
    }
}

@Composable
private fun BulletLine(text: String) {
    Row(verticalAlignment = Alignment.Top) {
        Text("•  ", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun TermsScreen(onBack: () -> Unit) {
    LegalDocumentScreen(
        title = "Điều khoản sử dụng",
        updatedAt = "Cập nhật: 08/07/2026",
        lead = "Các điều khoản này áp dụng khi bạn đăng nhập hoặc sử dụng Ketoan - Nhân sự để quản lý hồ sơ nhân sự, chấm công, đơn từ, phê duyệt, thông báo và các chức năng liên quan do đơn vị vận hành cung cấp.",
        sections = termsSections(),
        onBack = onBack,
    )
}

@Composable
private fun PrivacyPolicyScreen(onBack: () -> Unit) {
    LegalDocumentScreen(
        title = "Chính sách quyền riêng tư",
        updatedAt = "Cập nhật: 08/07/2026",
        lead = "Chính sách này giải thích cách Ketoan - Nhân sự thu thập, sử dụng, lưu trữ và bảo vệ dữ liệu khi bạn dùng ứng dụng nội bộ phục vụ quản lý nhân sự và chấm công.",
        sections = privacySections(),
        onBack = onBack,
    )
}

@Composable
private fun LegalDocumentScreen(
    title: String,
    updatedAt: String,
    lead: String,
    sections: List<LegalSection>,
    onBack: () -> Unit,
) {
    SubScreen(title, onBack) {
        item {
            SettingsGroup(padded = true) {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(title, style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
                    Text(updatedAt, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(lead, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }

        items(sections) { section ->
            SettingsGroup(padded = true) {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(section.title, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
                    section.paragraphs.forEach { paragraph ->
                        Text(paragraph, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }
    }
}

private fun termsSections(): List<LegalSection> = listOf(
    LegalSection(
        "1. Tài khoản và quyền truy cập",
        listOf(
            "Bạn chỉ được sử dụng tài khoản được cấp cho chính mình và phải bảo mật mật khẩu, thiết bị đăng nhập, mã phiên và các phương thức xác thực khuôn mặt nếu đã đăng ký.",
            "Mọi thao tác phát sinh từ tài khoản của bạn có thể được ghi nhận để phục vụ vận hành, kiểm tra bảo mật, chấm công, xử lý đơn từ và quản trị nhân sự. Nếu nghi ngờ tài khoản bị truy cập trái phép, bạn cần đổi mật khẩu và thông báo ngay cho người quản trị.",
        ),
    ),
    LegalSection(
        "2. Mục đích sử dụng",
        listOf(
            "Ứng dụng được cung cấp cho mục đích nội bộ gồm xem thông tin nhân sự, bảng công, lương/phụ cấp nếu được phân quyền, gửi và xử lý đơn từ, nhận thông báo, đăng ký khuôn mặt, chấm công trực tuyến hoặc đồng bộ bản chấm công ngoại tuyến.",
            "Bạn không được dùng ứng dụng để truy cập dữ liệu không thuộc thẩm quyền, giả mạo danh tính, can thiệp hệ thống, trích xuất dữ liệu trái phép, vô hiệu hóa cơ chế bảo mật hoặc thực hiện hành vi vi phạm nội quy, hợp đồng lao động hay pháp luật.",
        ),
    ),
    LegalSection(
        "3. Chấm công và xác thực khuôn mặt",
        listOf(
            "Khi dùng chức năng đăng ký khuôn mặt, đặt lại mật khẩu bằng khuôn mặt hoặc chấm công, ứng dụng có thể sử dụng camera để ghi nhận ảnh/khuôn mặt, kiểm tra chất lượng ảnh, gửi dữ liệu cần thiết tới máy chủ và nhận kết quả đối chiếu.",
            "Kết quả chấm công chỉ có giá trị khi được hệ thống tiếp nhận hoặc đồng bộ thành công. Trường hợp mất mạng, dữ liệu chấm công có thể được lưu tạm trên thiết bị và gửi lại khi có kết nối; bản ghi ngoại tuyến có thể kèm thời điểm phát sinh và vị trí nếu bạn cấp quyền.",
        ),
    ),
    LegalSection(
        "4. Thông báo, thiết bị và cập nhật",
        listOf(
            "Khi bật thông báo, ứng dụng có thể đăng ký mã thông báo push của thiết bị để gửi thông tin về đơn từ, phê duyệt, kỷ luật/phạt, chấm công, cập nhật hệ thống và nội dung vận hành liên quan.",
            "Ứng dụng có thể kiểm tra phiên bản mới, tải gói cập nhật APK từ máy chủ được cấu hình và yêu cầu xác nhận cài đặt theo cơ chế của Android. Bạn nên chỉ cài đặt bản cập nhật phát hành từ kênh chính thức của đơn vị vận hành.",
        ),
    ),
    LegalSection(
        "5. Dữ liệu, bảo mật và trách nhiệm",
        listOf(
            "Bạn đồng ý cung cấp dữ liệu chính xác khi gửi đơn từ, chấm công, cập nhật thông tin hoặc thực hiện phê duyệt. Dữ liệu sai lệch, gian lận hoặc sử dụng sai chức năng có thể bị xử lý theo quy định nội bộ và pháp luật áp dụng.",
            "Đơn vị vận hành áp dụng các biện pháp kỹ thuật và tổ chức phù hợp để bảo vệ hệ thống, nhưng việc sử dụng ứng dụng vẫn phụ thuộc vào thiết bị, kết nối mạng, dịch vụ máy chủ và cấu hình bảo mật của từng môi trường.",
        ),
    ),
    LegalSection(
        "6. Tạm ngừng, chấm dứt và thay đổi",
        listOf(
            "Quyền truy cập của bạn có thể bị tạm ngừng, thu hồi hoặc giới hạn khi tài khoản không còn thuộc phạm vi sử dụng, có rủi ro bảo mật, vi phạm điều khoản hoặc theo yêu cầu quản trị nhân sự.",
            "Các điều khoản này có thể được cập nhật để phù hợp với thay đổi của ứng dụng, quy trình nội bộ hoặc yêu cầu pháp luật. Việc tiếp tục sử dụng ứng dụng sau khi nội dung được cập nhật được hiểu là bạn đã biết và chấp nhận phiên bản mới.",
        ),
    ),
    LegalSection(
        "7. Liên hệ",
        listOf(
            "Nếu có câu hỏi về tài khoản, dữ liệu, chấm công, đơn từ hoặc việc áp dụng điều khoản này, vui lòng liên hệ bộ phận nhân sự, quản trị hệ thống hoặc đầu mối được đơn vị vận hành chỉ định.",
        ),
    ),
)

private fun privacySections(): List<LegalSection> = listOf(
    LegalSection(
        "1. Dữ liệu chúng tôi xử lý",
        listOf(
            "Ứng dụng có thể xử lý thông tin tài khoản như tên đăng nhập, họ tên, email, vai trò, trạng thái tài khoản, mã nhân viên, phòng ban, chức vụ, người quản lý và thông tin hồ sơ nhân sự được phân quyền.",
            "Ứng dụng cũng có thể xử lý bảng công, ca làm, thời điểm vào/ra, đơn từ, nội dung phê duyệt, dữ liệu lương/phụ cấp nếu tài khoản có quyền xem, thông báo, phiên đăng nhập, mã thiết bị, mã push notification và nhật ký hoạt động cần thiết cho bảo mật/vận hành.",
        ),
    ),
    LegalSection(
        "2. Camera, khuôn mặt và vị trí",
        listOf(
            "Khi bạn sử dụng chức năng khuôn mặt, ứng dụng cần quyền camera để chụp ảnh hoặc khung hình phục vụ đăng ký, xác thực, đặt lại mật khẩu hoặc chấm công. Máy chủ có thể tạo và lưu dữ liệu đối chiếu khuôn mặt hoặc bản ghi liên quan theo cấu hình của hệ thống.",
            "Khi chấm công ngoại tuyến hoặc chức năng yêu cầu kiểm tra phạm vi làm việc, ứng dụng có thể xin quyền vị trí chính xác hoặc tương đối. Dữ liệu vị trí chỉ được gửi kèm bản chấm công khi chức năng đó cần thiết và bạn đã cấp quyền trên thiết bị.",
        ),
    ),
    LegalSection(
        "3. Mục đích sử dụng dữ liệu",
        listOf(
            "Dữ liệu được dùng để xác thực đăng nhập, duy trì phiên làm việc, quản lý hồ sơ nhân sự, tính và hiển thị bảng công, xử lý đơn từ/phê duyệt, gửi thông báo, hỗ trợ chấm công khuôn mặt, đồng bộ dữ liệu ngoại tuyến, phát hiện lỗi và bảo vệ hệ thống.",
            "Chúng tôi không bán dữ liệu cá nhân. Dữ liệu không được dùng cho quảng cáo hành vi trong ứng dụng này.",
        ),
    ),
    LegalSection(
        "4. Chia sẻ dữ liệu",
        listOf(
            "Dữ liệu có thể được truy cập bởi người dùng nội bộ được phân quyền như nhân viên, quản lý, nhân sự, kế toán hoặc quản trị viên hệ thống tùy theo chức năng và vai trò.",
            "Ứng dụng có thể sử dụng dịch vụ bên thứ ba cần thiết cho vận hành, ví dụ Firebase Cloud Messaging của Google để gửi thông báo push và thư viện xử lý khuôn mặt trên thiết bị. Việc chia sẻ, nếu có, chỉ nhằm cung cấp chức năng ứng dụng, bảo mật, bảo trì hoặc đáp ứng yêu cầu pháp luật.",
        ),
    ),
    LegalSection(
        "5. Lưu trữ và bảo vệ",
        listOf(
            "Trên thiết bị, ứng dụng có thể lưu mã đăng nhập, mã phiên, tên đăng nhập đã ghi nhớ, cài đặt thông báo, thông báo cục bộ và hàng đợi chấm công ngoại tuyến. Ứng dụng không lưu mật khẩu đăng nhập dưới dạng ghi nhớ.",
            "Trên máy chủ, dữ liệu được lưu trong thời gian cần thiết cho mục đích nhân sự, chấm công, kế toán, kiểm toán nội bộ, bảo mật hoặc theo quy định pháp luật. Dữ liệu chấm công ngoại tuyến lưu tạm trên thiết bị sẽ được đồng bộ hoặc xử lý theo cơ chế của ứng dụng.",
        ),
    ),
    LegalSection(
        "6. Quyền của bạn",
        listOf(
            "Bạn có thể yêu cầu xem, cập nhật, chỉnh sửa, hạn chế xử lý, rút lại đồng ý đối với các chức năng không bắt buộc, hoặc yêu cầu xóa dữ liệu theo phạm vi pháp luật và quy định nội bộ cho phép.",
            "Một số dữ liệu có thể cần tiếp tục được lưu để thực hiện nghĩa vụ lao động, kế toán, chấm công, giải quyết tranh chấp, bảo mật hoặc yêu cầu pháp luật. Việc tắt quyền camera, vị trí hoặc thông báo trên thiết bị có thể làm một số chức năng không hoạt động đầy đủ.",
        ),
    ),
    LegalSection(
        "7. An toàn thiết bị và tài khoản",
        listOf(
            "Bạn nên đặt khóa màn hình, không chia sẻ thiết bị/tài khoản, không cài ứng dụng từ nguồn không tin cậy và đăng xuất hoặc thu hồi phiên khi không còn sử dụng thiết bị.",
            "Nếu phát hiện dữ liệu sai, mất thiết bị, rò rỉ tài khoản hoặc nghi ngờ truy cập trái phép, vui lòng thông báo ngay cho người quản trị hoặc bộ phận nhân sự để được hỗ trợ.",
        ),
    ),
    LegalSection(
        "8. Trẻ em và người không thuộc phạm vi sử dụng",
        listOf(
            "Ứng dụng được thiết kế cho nhân sự, quản lý và người dùng nội bộ được cấp tài khoản. Ứng dụng không hướng tới trẻ em hoặc người không được đơn vị vận hành cấp quyền truy cập.",
        ),
    ),
    LegalSection(
        "9. Thay đổi chính sách và liên hệ",
        listOf(
            "Chính sách này có thể được cập nhật khi chức năng, hạ tầng, quy trình xử lý dữ liệu hoặc yêu cầu pháp luật thay đổi. Phiên bản mới sẽ được hiển thị trong ứng dụng hoặc thông báo qua kênh phù hợp.",
            "Mọi yêu cầu về dữ liệu cá nhân, quyền riêng tư hoặc bảo mật vui lòng gửi tới bộ phận nhân sự, quản trị hệ thống hoặc đầu mối bảo vệ dữ liệu do đơn vị vận hành chỉ định.",
        ),
    ),
)

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
