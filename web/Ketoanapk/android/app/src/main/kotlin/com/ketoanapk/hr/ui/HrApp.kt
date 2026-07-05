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
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
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
        Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
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
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun HrShell(user: HrUser, vm: HrViewModel) {
    val drawerState = rememberDrawerState(DrawerValue.Closed)
    val scope = rememberCoroutineScope()
    val snackbar = remember { SnackbarHostState() }

    LaunchedEffect(vm.actionMessage) {
        vm.actionMessage?.let {
            snackbar.showSnackbar(it)
            vm.consumeActionMessage()
        }
    }

    // Xử lý nút Back của điện thoại: ưu tiên đóng ngăn kéo → thoát camera đang quét → về Trang chủ.
    // Chỉ bật khi có việc để lùi; khi đang ở Trang chủ (không mở gì) thì để hệ thống thoát app như thường.
    val inScanFlow = vm.selected == HrDestination.Scan && vm.attendanceCapture != AttendanceCapture.Idle
    BackHandler(enabled = drawerState.isOpen || inScanFlow || vm.selected != HrDestination.Home) {
        when {
            drawerState.isOpen -> scope.launch { drawerState.close() }
            inScanFlow -> vm.resetCapture()
            else -> vm.select(HrDestination.Home)
        }
    }

    val isRefreshing = when (vm.selected) {
        HrDestination.People -> vm.managerState.loading
        HrDestination.Settings -> vm.settingsState.loading
        HrDestination.Scan -> vm.attendanceServer is AttendanceServerState.Checking
        HrDestination.Timesheet -> vm.timesheetState.loading
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
            PullToRefreshBox(
                isRefreshing = isRefreshing,
                onRefresh = vm::refreshCurrent,
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding)
                    .background(MaterialTheme.colorScheme.background),
            ) {
                when (vm.selected) {
                    HrDestination.Home -> HomeScreen(user, vm.homeState, vm.managerState, vm::select)
                    HrDestination.Profile -> ProfileScreen(vm.homeState)
                    HrDestination.Scan -> AttendanceScreen(vm)
                    HrDestination.Timesheet -> TimesheetScreen(vm.timesheetState, vm::changeTimesheetMonth, vm::resetTimesheetMonth)
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

        // Camera quét khuôn mặt phủ TOÀN MÀN HÌNH (ngoài Scaffold) → không dính thanh tiêu đề/điều hướng.
        if (vm.selected == HrDestination.Scan && vm.attendanceCapture == AttendanceCapture.Collecting) {
            FullScreenCameraScan(onCaptured = vm::onFramesCaptured, onCancel = vm::resetCapture)
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
