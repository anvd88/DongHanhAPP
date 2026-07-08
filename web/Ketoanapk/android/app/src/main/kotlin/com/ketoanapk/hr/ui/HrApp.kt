package com.ketoanapk.hr.ui

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.BorderStroke
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
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.NotificationsNone
import androidx.compose.material.icons.filled.SystemUpdate
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DrawerValue
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalDrawerSheet
import androidx.compose.material3.ModalNavigationDrawer
import androidx.compose.material3.NavigationDrawerItem
import androidx.compose.material3.NavigationDrawerItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.rememberDrawerState
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.ReleaseInfo
import com.ketoanapk.hr.ui.theme.KetoanTheme
import kotlinx.coroutines.launch

@Composable
fun HrApp(vm: HrViewModel) {
    KetoanTheme {
        // Intro mở app chạy 1 lần mỗi phiên (không lặp lại khi xoay màn hình).
        var showIntro by rememberSaveable { mutableStateOf(true) }
        Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
            Box(Modifier.fillMaxSize()) {
                // Chuyển cảnh mượt khi vào ứng dụng (Loading → Đăng nhập / Trang chủ).
                AnimatedContent(
                    targetState = vm.authState,
                    transitionSpec = {
                        (fadeIn(tween(400)) + scaleIn(tween(400), initialScale = 0.94f)) togetherWith fadeOut(tween(250))
                    },
                    label = "auth",
                ) { state ->
                    when (state) {
                        AuthState.Loading -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                            CircularProgressIndicator(color = MaterialTheme.colorScheme.primary)
                        }
                        AuthState.SignedOut -> LoginScreen(
                            loading = vm.loginLoading,
                            error = vm.loginError,
                            resetLoading = vm.resetPasswordLoading,
                            rememberedUsername = vm.rememberedUsername,
                            onLogin = vm::login,
                            onResetPasswordWithFace = vm::resetPasswordWithFace,
                        )
                        is AuthState.SignedIn -> HrShell(state.user, vm)
                    }
                }

                // Lớp phủ intro (vẽ logo bằng vector) nằm trên cùng, tự mờ dần rồi biến mất.
                if (showIntro) {
                    IntroOverlay(onFinished = { showIntro = false })
                }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun HrShell(user: HrUser, vm: HrViewModel) {
    val drawerState = rememberDrawerState(DrawerValue.Closed)
    val scope = rememberCoroutineScope()
    val snackbar = remember { SnackbarHostState() }
    // Đóng banner nhắc đăng ký khuôn mặt cho hết phiên (không nhắc lại tới khi mở lại app).
    var faceBannerDismissed by rememberSaveable { mutableStateOf(false) }
    // Thông báo (remote config) đã đóng — lưu theo nội dung để admin đổi nội dung thì hiện lại.
    var dismissedAnnouncement by rememberSaveable { mutableStateOf<String?>(null) }

    LaunchedEffect(vm.actionMessage) {
        vm.actionMessage?.let {
            snackbar.showSnackbar(it)
            vm.consumeActionMessage()
        }
    }

    // Xử lý nút Back của điện thoại: ưu tiên đóng ngăn kéo → thoát camera đang quét → về Trang chủ.
    // Chỉ bật khi có việc để lùi; khi đang ở Trang chủ (không mở gì) thì để hệ thống thoát app như thường.
    val inScanFlow = vm.selected == HrDestination.Scan && vm.attendanceCapture != AttendanceCapture.Idle
    val inFaceEnroll = vm.faceEnroll == FaceEnrollCapture.Capturing
    // Đang ở một màn con của tab Cài đặt (Đổi mật khẩu, Thiết bị, ...) → Back lùi về Cài đặt gốc trước.
    val inSettingsSub = vm.selected == HrDestination.Settings && vm.settingsRoute != SettingsRoute.Home
    BackHandler(enabled = drawerState.isOpen || inScanFlow || inFaceEnroll || inSettingsSub || vm.selected != HrDestination.Home) {
        when {
            drawerState.isOpen -> scope.launch { drawerState.close() }
            inFaceEnroll -> vm.cancelFaceEnroll()
            inScanFlow -> vm.resetCapture()
            inSettingsSub -> vm.settingsRoute = SettingsRoute.Home
            else -> vm.select(HrDestination.Home)
        }
    }

    val isRefreshing = when (vm.selected) {
        HrDestination.People -> vm.managerState.loading
        HrDestination.Settings -> vm.settingsState.loading
        HrDestination.Scan -> vm.attendanceServer is AttendanceServerState.Checking
        HrDestination.Timesheet -> vm.timesheetState.loading
        HrDestination.MySalary -> vm.payEstimateState.loading
        else -> vm.homeState.loading
    }

    Box(modifier = Modifier.fillMaxSize()) {
    ModalNavigationDrawer(
        drawerState = drawerState,
        drawerContent = {
            DrawerContent(
                user = user,
                selected = vm.selected,
                groups = vm.visibleNavGroups(user),
                approvalCount = vm.pendingApprovalCount,
                onSelect = {
                    vm.select(it)
                    scope.launch { drawerState.close() }
                },
                onClose = { scope.launch { drawerState.close() } },
                onLogout = vm::logout,
            )
        },
    ) {
        Scaffold(
            snackbarHost = { SnackbarHost(snackbar) },
            topBar = {
                TopAppBar(
                    title = {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(
                                "KETOANAPK",
                                style = MaterialTheme.typography.titleMedium,
                                color = MaterialTheme.colorScheme.primary,
                                fontWeight = FontWeight.ExtraBold,
                            )
                            Box(
                                Modifier
                                    .padding(horizontal = 10.dp)
                                    .width(1.dp)
                                    .height(20.dp)
                                    .background(MaterialTheme.colorScheme.outline),
                            )
                            Text(
                                vm.selected.title,
                                style = MaterialTheme.typography.titleMedium,
                                color = MaterialTheme.colorScheme.onSurface,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                    },
                    navigationIcon = {
                        IconButton(onClick = { scope.launch { drawerState.open() } }) {
                            Icon(Icons.Filled.Menu, contentDescription = "Điều hướng")
                        }
                    },
                    actions = {
                        NotificationBell(count = vm.unreadCount, onClick = vm::openNotifications)
                        UserAvatar(user.displayName, 34, modifier = Modifier.padding(start = 4.dp, end = 12.dp))
                    },
                    colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface),
                )
            },
            bottomBar = {
                BottomBar(
                    items = vm.bottomDestinations,
                    selected = vm.selected,
                    onSelect = vm::select,
                )
            },
        ) { padding ->
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding)
                    .background(MaterialTheme.colorScheme.background),
            ) {
                // Thông báo điều khiển từ xa (admin đặt ở trang Hệ thống → Cập nhật). Ẩn khi để trống.
                val announcement = vm.appConfig.announcement
                if (announcement.isNotBlank() && announcement != dismissedAnnouncement) {
                    AnnouncementBanner(
                        text = announcement,
                        level = vm.appConfig.announcementLevel,
                        onDismiss = { dismissedAnnouncement = announcement },
                    )
                }
                // Banner nhắc đăng ký khuôn mặt: chỉ hiện khi CHẮC CHẮN chưa đăng ký (cờ đi kèm đăng nhập),
                // admin không tắt từ xa, chưa bị đóng, và không phải đang ở màn Cài đặt.
                if (vm.showFaceEnrollBanner && !faceBannerDismissed && vm.selected != HrDestination.Settings) {
                    FaceEnrollBanner(
                        onEnroll = vm::requestFaceEnroll,
                        onDismiss = { faceBannerDismissed = true },
                    )
                }
                PullToRefreshBox(
                    isRefreshing = isRefreshing,
                    onRefresh = vm::refreshCurrent,
                    modifier = Modifier
                        .fillMaxWidth()
                        .weight(1f),
                ) {
                    when (vm.selected) {
                    HrDestination.Home -> HomeScreen(user, vm.homeState, vm.managerState, vm::select)
                    HrDestination.Profile -> ProfileScreen(vm.homeState)
                    HrDestination.Scan -> AttendanceScreen(vm)
                    HrDestination.Timesheet -> TimesheetScreen(vm.timesheetState, vm::changeTimesheetMonth, vm::setTimesheetMonth)
                    HrDestination.MySalary -> MySalaryScreen(vm.payEstimateState)
                    HrDestination.Requests -> RequestsScreen(vm)
                    HrDestination.Approval -> StaffRequestsScreen(vm)
                    HrDestination.Penalty -> PenaltyScreen(user, vm.homeState)
                    HrDestination.People -> ManagerScreen(vm.managerState)
                    HrDestination.Payroll -> PayrollScreen(vm.homeState)
                    HrDestination.Audit -> SimpleScreen("Nhật ký hệ thống", "Chưa có nhật ký mới trong ứng dụng.")
                    HrDestination.Settings -> SettingsScreen(user, vm, vm::logout)
                    HrDestination.Notifications -> NotificationsScreen(
                        notifications = vm.notifications,
                        onOpen = { n -> vm.markNotificationRead(n.id); vm.navigateTo(n.target) },
                        onMarkAllRead = vm::markAllNotificationsRead,
                        onClear = vm::clearNotifications,
                    )
                }
            }
            }
        }
    }

        // Camera quét khuôn mặt phủ TOÀN MÀN HÌNH (ngoài Scaffold) → không dính thanh tiêu đề/điều hướng.
        if (vm.selected == HrDestination.Scan && vm.attendanceCapture == AttendanceCapture.Collecting) {
            FullScreenCameraScan(onCaptured = vm::onFramesCaptured, onCancel = vm::resetCapture)
        }

        // Camera ĐĂNG KÝ khuôn mặt (quét nhiều góc) cũng phủ toàn màn hình như trên.
        if (vm.faceEnroll == FaceEnrollCapture.Capturing) {
            FaceEnrollCameraScan(onCompleted = vm::submitFaceEnroll, onCancel = vm::cancelFaceEnroll)
        }

        // Nhắc cập nhật ngay khi phát hiện bản mới (kiểm tra ngầm lúc đăng nhập/quay lại app).
        val update = vm.availableUpdate
        if (vm.updatePromptVisible && update != null) {
            val context = LocalContext.current
            UpdateDialog(
                info = update,
                installing = vm.settingsState.installing,
                onConfirm = { vm.confirmUpdatePrompt(context) },
                onDismiss = vm::dismissUpdatePrompt,
            )
        }
    }
}

/** Hộp thoại nhắc cập nhật hiện trên mọi màn hình. Bản bắt buộc không cho bỏ qua. */
@Composable
private fun UpdateDialog(
    info: ReleaseInfo,
    installing: Boolean,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = { if (!info.isMandatory) onDismiss() },
        icon = { Icon(Icons.Filled.SystemUpdate, contentDescription = null, tint = MaterialTheme.colorScheme.primary) },
        title = { Text("Đã có bản cập nhật ${info.version}") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    if (info.isMandatory) "Đây là bản cập nhật bắt buộc. Vui lòng cập nhật để tiếp tục sử dụng."
                    else "Phiên bản mới đã sẵn sàng. Bạn có muốn cập nhật ngay không?",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurface,
                )
                if (info.releaseNotes.isNotBlank()) {
                    Text(info.releaseNotes, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        },
        confirmButton = {
            Button(onClick = onConfirm, enabled = !installing) {
                if (installing) {
                    CircularProgressIndicator(Modifier.size(18.dp), MaterialTheme.colorScheme.onPrimary, 2.dp)
                } else {
                    Text("Cập nhật ngay", fontWeight = FontWeight.Bold)
                }
            }
        },
        dismissButton = if (info.isMandatory) {
            null
        } else {
            { TextButton(onClick = onDismiss) { Text("Để sau") } }
        },
    )
}

/**
 * Banner thông báo điều khiển từ xa (remote config), ngay dưới header. Màu theo mức độ:
 * info = phụ, warning = nhấn, critical = báo lỗi. Đóng được (ẩn theo nội dung).
 */
@Composable
private fun AnnouncementBanner(text: String, level: String, onDismiss: () -> Unit) {
    val bg: Color
    val fg: Color
    when (level.lowercase()) {
        "critical" -> { bg = MaterialTheme.colorScheme.errorContainer; fg = MaterialTheme.colorScheme.onErrorContainer }
        "warning" -> { bg = MaterialTheme.colorScheme.tertiaryContainer; fg = MaterialTheme.colorScheme.onTertiaryContainer }
        else -> { bg = MaterialTheme.colorScheme.secondaryContainer; fg = MaterialTheme.colorScheme.onSecondaryContainer }
    }
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 14.dp, end = 14.dp, top = 12.dp, bottom = 2.dp),
        shape = RoundedCornerShape(18.dp),
        color = bg,
    ) {
        Row(
            modifier = Modifier.padding(start = 14.dp, end = 6.dp, top = 10.dp, bottom = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Icon(Icons.Filled.Info, contentDescription = null, tint = fg, modifier = Modifier.size(22.dp))
            Text(text, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold, color = fg)
            IconButton(onClick = onDismiss) {
                Icon(Icons.Filled.Close, contentDescription = "Ẩn thông báo", tint = fg.copy(alpha = 0.7f))
            }
        }
    }
}

/**
 * Banner nhắc đăng ký khuôn mặt, nằm ngay dưới header. Hiện khi tài khoản CHƯA đăng ký (biết từ 1 lần
 * hỏi máy chủ lúc đăng nhập — không dò lại liên tục). Bấm "Đăng ký" mở thẳng luồng quét; bấm X thì ẩn
 * đến khi mở lại app.
 */
@Composable
private fun FaceEnrollBanner(onEnroll: () -> Unit, onDismiss: () -> Unit) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 14.dp, end = 14.dp, top = 12.dp, bottom = 2.dp),
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.primaryContainer,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.35f)),
    ) {
        Row(
            modifier = Modifier.padding(start = 14.dp, end = 6.dp, top = 12.dp, bottom = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Box(
                modifier = Modifier
                    .size(40.dp)
                    .clip(CircleShape)
                    .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.18f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Filled.Face, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(24.dp))
            }
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(
                    "Bạn chưa đăng ký khuôn mặt",
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                )
                Text(
                    "Đăng ký để chấm công và đăng nhập nhanh bằng khuôn mặt.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.85f),
                )
                Spacer(Modifier.height(6.dp))
                Button(
                    onClick = onEnroll,
                    contentPadding = PaddingValues(horizontal = 16.dp, vertical = 6.dp),
                ) {
                    Icon(Icons.Filled.Face, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(6.dp))
                    Text("Đăng ký ngay", fontWeight = FontWeight.Bold)
                }
            }
            IconButton(onClick = onDismiss) {
                Icon(Icons.Filled.Close, contentDescription = "Ẩn nhắc nhở", tint = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.7f))
            }
        }
    }
}

@Composable
private fun NotificationBell(count: Int, onClick: () -> Unit) {
    IconButton(onClick = onClick) {
        BadgedBox(
            badge = {
                if (count > 0) {
                    Badge(
                        containerColor = MaterialTheme.colorScheme.error,
                        contentColor = MaterialTheme.colorScheme.onError,
                    ) { Text(if (count > 99) "99+" else "$count") }
                }
            },
        ) {
            Icon(
                if (count > 0) Icons.Filled.Notifications else Icons.Filled.NotificationsNone,
                contentDescription = "Thông báo",
                tint = MaterialTheme.colorScheme.onSurface,
            )
        }
    }
}

/** Thanh điều hướng nổi (floating): thẻ trắng bo tròn, nút Chấm công đỏ nhô lên ở giữa. */
@Composable
private fun BottomBar(
    items: List<HrDestination>,
    selected: HrDestination,
    onSelect: (HrDestination) -> Unit,
) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .navigationBarsPadding() // né thanh điều hướng hệ thống (cử chỉ / 3 nút) để không bị che
            .padding(start = 14.dp, end = 14.dp, bottom = 12.dp)
            .height(84.dp),
    ) {
        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .height(64.dp)
                .align(Alignment.BottomCenter),
            shape = RoundedCornerShape(28.dp),
            color = MaterialTheme.colorScheme.surface,
            shadowElevation = 16.dp,
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
        ) {
            Row(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                items.forEach { item ->
                    if (item == HrDestination.Scan) {
                        Spacer(Modifier.weight(1f)) // chừa chỗ cho nút nổi ở giữa
                    } else {
                        BottomItem(item.icon, item.label, active = selected == item, modifier = Modifier.weight(1f)) { onSelect(item) }
                    }
                }
                BottomItem(
                    HrDestination.Settings.icon,
                    HrDestination.Settings.label,
                    active = selected == HrDestination.Settings,
                    modifier = Modifier.weight(1f),
                    onClick = { onSelect(HrDestination.Settings) },
                )
            }
        }

        ScanButton(
            onClick = { onSelect(HrDestination.Scan) },
            modifier = Modifier.align(Alignment.TopCenter),
        )
    }
}

@Composable
private fun BottomItem(
    icon: ImageVector,
    label: String,
    active: Boolean,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val color = if (active) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant
    Column(
        modifier = modifier
            .clip(RoundedCornerShape(12.dp))
            .clickable(onClick = onClick)
            .padding(vertical = 4.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(3.dp),
    ) {
        Icon(icon, contentDescription = label, tint = color, modifier = Modifier.size(22.dp))
        Text(label, style = MaterialTheme.typography.labelSmall, color = color, maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center)
    }
}

@Composable
private fun ScanButton(onClick: () -> Unit, modifier: Modifier = Modifier) {
    Box(
        modifier = modifier
            .size(60.dp)
            .shadow(10.dp, CircleShape)
            .clip(CircleShape)
            .background(MaterialTheme.colorScheme.primary)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            HrDestination.Scan.icon,
            contentDescription = "Chấm công",
            tint = MaterialTheme.colorScheme.onPrimary,
            modifier = Modifier.size(30.dp),
        )
    }
}

@Composable
private fun DrawerContent(
    user: HrUser,
    selected: HrDestination,
    groups: List<NavGroup>,
    approvalCount: Int,
    onSelect: (HrDestination) -> Unit,
    onClose: () -> Unit,
    onLogout: () -> Unit,
) {
    ModalDrawerSheet(
        drawerContainerColor = MaterialTheme.colorScheme.surface,
        modifier = Modifier.width(316.dp),
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            UserAvatar(user.displayName, 46)
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(user.displayName, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(if (user.isAdmin) "Quản trị nhân sự" else "Nhân viên", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            IconButton(onClick = onClose) { Icon(Icons.Filled.Close, contentDescription = "Đóng") }
        }
        HorizontalDivider(color = MaterialTheme.colorScheme.outline)
        LazyColumn(
            modifier = Modifier.weight(1f),
            contentPadding = PaddingValues(horizontal = 10.dp, vertical = 10.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            groups.forEach { group ->
                item(key = "title-${group.title}") {
                    SectionTitle(group.title, modifier = Modifier.padding(horizontal = 6.dp))
                }
                items(group.destinations, key = { it.name }) { dest ->
                    NavigationDrawerItem(
                        label = { Text(dest.title, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                        selected = selected == dest,
                        icon = { Icon(dest.icon, contentDescription = null) },
                        badge = {
                            if (dest == HrDestination.Approval && approvalCount > 0) {
                                Badge(
                                    containerColor = MaterialTheme.colorScheme.error,
                                    contentColor = MaterialTheme.colorScheme.onError,
                                ) { Text(if (approvalCount > 99) "99+" else "$approvalCount") }
                            }
                        },
                        onClick = { onSelect(dest) },
                        colors = NavigationDrawerItemDefaults.colors(
                            selectedContainerColor = MaterialTheme.colorScheme.primaryContainer,
                        ),
                    )
                }
            }
        }
        HorizontalDivider(color = MaterialTheme.colorScheme.outline)
        NavigationDrawerItem(
            label = { Text("Đăng xuất", fontWeight = FontWeight.Bold) },
            selected = false,
            icon = { Icon(Icons.AutoMirrored.Filled.Logout, contentDescription = null, tint = MaterialTheme.colorScheme.error) },
            onClick = onLogout,
            modifier = Modifier.padding(10.dp),
        )
    }
}
