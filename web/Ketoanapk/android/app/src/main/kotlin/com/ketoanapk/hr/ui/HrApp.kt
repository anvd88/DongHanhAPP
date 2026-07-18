package com.ketoanapk.hr.ui

import android.app.Activity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.LocalOnBackPressedDispatcherOwner
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
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
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.NotificationsNone
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import kotlin.math.roundToInt
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.AppPersonalization
import com.ketoanapk.hr.ui.theme.KetoanTheme

@Composable
fun HrApp(vm: HrViewModel) {
    val personalizationContext=LocalContext.current
    AppPersonalization.init(personalizationContext)
    val dark=when(AppPersonalization.themeMode){"dark"->true;"light"->false;else->androidx.compose.foundation.isSystemInDarkTheme()}
    KetoanTheme(darkTheme=dark,fontScale=AppPersonalization.fontScale) {
        val backDispatcher = LocalOnBackPressedDispatcherOwner.current?.onBackPressedDispatcher
        // Keep one QR controller at the app root so deep-link dialogs can appear above every overlay.
        val qrScanner = rememberQrScanController(vm)

        LaunchedEffect(vm.authState, vm.pendingQrLoginCode, qrScanner.busy) {
            val code = vm.pendingQrLoginCode
            if (vm.authState is AuthState.SignedIn && code != null && !qrScanner.busy) {
                vm.consumePendingQrLoginCode()
                qrScanner.resolveValue(code)
            }
        }
        LaunchedEffect(vm.authState, vm.pendingMobileAppLoginCode) {
            vm.processPendingMobileAppLogin()
        }
        // Intro mở app chạy 1 lần mỗi phiên (không lặp lại khi xoay màn hình).
        var showIntro by rememberSaveable { mutableStateOf(true) }
        val hasPendingWebLogin = vm.pendingQrLoginCode != null || vm.pendingMobileAppLoginCode != null
        LaunchedEffect(hasPendingWebLogin) {
            if (hasPendingWebLogin) showIntro = false
        }
        val appContext = LocalContext.current
        val permissionPrefs = remember { appContext.getSharedPreferences("permission_onboarding", android.content.Context.MODE_PRIVATE) }
        var showPermissionOnboarding by rememberSaveable {
            mutableStateOf(!permissionPrefs.getBoolean("seen", false))
        }
        fun finishPermissionOnboarding() {
            permissionPrefs.edit().putBoolean("seen", true).apply()
            showPermissionOnboarding = false
        }
        // Bấm ra vùng trống bất kỳ → thu bàn phím + bỏ focus ô nhập (nút/ô bấm con vẫn tự nuốt sự
        // kiện của chúng nên không bị ảnh hưởng). Áp ở gốc app nên có tác dụng trên MỌI màn hình nhập.
        val focusManager = LocalFocusManager.current
        Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
            EdgeBackContainer(
                enabled = backDispatcher != null,
                onBack = { backDispatcher?.onBackPressed() },
            ) {
                Box(
                    Modifier
                        .fillMaxSize()
                        .pointerInput(Unit) {
                            detectTapGestures(onTap = { focusManager.clearFocus() })
                        },
                ) {
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
                            onResetPasswordWithCode = vm::resetPasswordWithCode,
                        )
                        is AuthState.SignedIn -> HrShell(state.user, vm, qrScanner)
                    }
                }

                // Lớp phủ intro (vẽ logo bằng vector) nằm trên cùng, tự mờ dần rồi biến mất.
                if (showIntro) {
                    IntroOverlay(onFinished = { showIntro = false })
                }

                if (!showIntro && showPermissionOnboarding && !hasPendingWebLogin) {
                    PermissionOnboardingDialog(
                        onSkip = ::finishPermissionOnboarding,
                        onDone = ::finishPermissionOnboarding,
                    )
                }

                // Lớp phủ cuộc gọi (thoại/video) — luôn trên cùng, hiện khi có cuộc gọi đến/đi.
                CallHost(vm)

                // Keep the web-login confirmation above intro, onboarding and all feature dialogs.
                QrScanDialog(qrScanner)
                MobileAppLoginDialog(vm)
                }
            }
        }
    }
}

@Composable
private fun MobileAppLoginDialog(vm: HrViewModel) {
    val activity = LocalContext.current as? Activity
    when (val state = vm.mobileAppLoginState) {
        MobileAppLoginState.Idle -> Unit
        MobileAppLoginState.Received, MobileAppLoginState.Resolving -> AlertDialog(
            onDismissRequest = {},
            icon = { CircularProgressIndicator(modifier = Modifier.size(30.dp), strokeWidth = 3.dp) },
            title = { Text("Đang nhận yêu cầu đăng nhập") },
            text = { Text("Ứng dụng đang kiểm tra yêu cầu từ trình duyệt mobile.") },
            confirmButton = {},
        )
        MobileAppLoginState.AwaitingAppLogin -> AlertDialog(
            onDismissRequest = vm::dismissMobileAppLogin,
            title = { Text("Cần đăng nhập ứng dụng") },
            text = {
                Text("Phiên ứng dụng đã hết hoặc chưa đăng nhập. Hãy đăng nhập ứng dụng; yêu cầu đăng nhập web sẽ tiếp tục tự động ngay sau đó.")
            },
            confirmButton = {
                Button(onClick = vm::dismissMobileAppLogin) { Text("Đăng nhập ứng dụng") }
            },
        )
        is MobileAppLoginState.Confirmation -> AlertDialog(
            onDismissRequest = vm::dismissMobileAppLogin,
            title = { Text(state.challenge.title) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text(state.challenge.message)
                    state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                }
            },
            confirmButton = {
                Button(
                    enabled = !state.submitting,
                    onClick = { vm.decideMobileAppLogin(true) },
                ) {
                    if (state.submitting) {
                        CircularProgressIndicator(
                            modifier = Modifier.size(18.dp),
                            strokeWidth = 2.dp,
                            color = MaterialTheme.colorScheme.onPrimary,
                        )
                        Spacer(Modifier.width(8.dp))
                    }
                    Text("Đăng nhập")
                }
            },
            dismissButton = {
                TextButton(
                    enabled = !state.submitting,
                    onClick = { vm.decideMobileAppLogin(false) },
                ) { Text("Từ chối") }
            },
        )
        is MobileAppLoginState.Finished -> if (state.accepted) {
            // Xác nhận xong là quay thẳng về trình duyệt. Web đang poll phiên riêng và sẽ
            // nhận JWT ngay, người dùng không phải bấm thêm nút "Đóng" trong ứng dụng.
            LaunchedEffect(state) {
                vm.dismissMobileAppLogin()
                if (activity?.moveTaskToBack(true) != true) activity?.finish()
            }
        } else {
            AlertDialog(
                onDismissRequest = vm::dismissMobileAppLogin,
                title = { Text("Không thể đăng nhập") },
                text = { Text(state.message) },
                confirmButton = {
                    Button(onClick = vm::dismissMobileAppLogin) { Text("Đóng") }
                },
            )
        }
    }
}

@Composable
private fun PermissionOnboardingDialog(onSkip: () -> Unit, onDone: () -> Unit) {
    AlertDialog(
        onDismissRequest = onSkip,
        title = { Text("Quyền riêng tư trên điện thoại") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("KetoanAPK chỉ hỏi quyền khi bạn dùng tính năng liên quan:")
                Text("• Thông báo: đơn từ, tin nhắn và cuộc gọi đến")
                Text("• Camera: chấm công, hồ sơ và gọi video")
                Text("• Micro: cuộc gọi thoại/video")
                Text("• Vị trí: kiểm tra địa điểm chấm công")
                Text(
                    "Bạn có thể bỏ qua và xem lại tại Cài đặt → Quyền ứng dụng.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        },
        confirmButton = { Button(onClick = onDone) { Text("Đã hiểu") } },
        dismissButton = { TextButton(onClick = onSkip) { Text("Bỏ qua") } },
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun HrShell(user: HrUser, vm: HrViewModel, qrScanner: QrScanController) {
    val snackbar = remember { SnackbarHostState() }
    // Giữ trạng thái riêng cho TỪNG màn (vị trí cuộn, ô nhập...). Không có cái này thì khối `when` bên
    // dưới dựng lại composable mỗi lần đổi tab, nên cuộn giữa danh sách rồi sang tab khác và quay lại là
    // mất chỗ, phải cuộn từ đầu.
    val screenState = rememberSaveableStateHolder()
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

    // Deep-link từ trang đăng nhập mobile dùng cùng bộ xử lý QR đang có. Chỉ tiêu thụ sau khi phiên
    // app đã được khôi phục/đăng nhập và bộ xử lý rảnh, nên cold start không làm mất yêu cầu.
    // Back của điện thoại xếp theo ĐỘ ƯU TIÊN = THỨ TỰ KHAI BÁO: BackHandler khai sau (lồng sâu hơn) luôn
    // thắng cái khai trước. Cái dưới đây khai đầu tiên nên là mức THẤP NHẤT — chỉ chạy khi không màn nào
    // trong app nhận Back — và chỉ làm một việc: lùi ngăn xếp màn.
    //
    // Mỗi màn con tự khai BackHandler của nó ngay chỗ nó được dựng (Cài đặt, Cổng thông tin, Phiếu lương,
    // Chấm công, Chat, Đơn từ, Quản lý nhân sự...). Nhờ vậy thêm màn con mới KHÔNG phải sửa gì ở đây.
    BackHandler(enabled = vm.canGoBack) { vm.goBack() }

    // Khai SAU cái trên nên được ưu tiên hơn: đang mở tìm kiếm thì Back đóng tìm kiếm, chưa rời màn.
    BackHandler(enabled = vm.searchOpen) { vm.closeSearch() }

    val isRefreshing = when (vm.selected) {
        HrDestination.People -> vm.managerState.loading
        HrDestination.Settings -> vm.settingsState.loading
        HrDestination.Scan -> vm.attendanceServer is AttendanceServerState.Checking
        HrDestination.Timesheet -> vm.timesheetState.loading
        HrDestination.MySalary -> vm.payEstimateState.loading
        HrDestination.MyPayslips -> vm.payslipsState.loading
        HrDestination.Payout -> vm.payoutState.loading
        HrDestination.Portal -> vm.portalState.loading
        HrDestination.Tasks -> vm.homeState.loading
        HrDestination.WorkTasks -> vm.workTasksState.loading
        HrDestination.Chat -> vm.realChatState.loading
        HrDestination.Directory -> vm.directoryState.loading
        HrDestination.Calls -> vm.callHistoryState.loading
        else -> vm.homeState.loading
    }

    Box(modifier = Modifier.fillMaxSize()) {
        // Chat là "mini app": chiếm trọn màn, tự dựng header + thanh tab riêng nên ẩn hẳn header và
        // thanh dưới của HR ở đây.
        val chatFullScreen = vm.selected == HrDestination.Chat
        Scaffold(
            snackbarHost = { SnackbarHost(snackbar) },
            topBar = {
                if (chatFullScreen) Unit
                else if (vm.searchOpen) SearchTopBar(
                    query = vm.searchQuery,
                    onQuery = vm::typeSearch,
                    onClose = vm::closeSearch,
                )
                else TopAppBar(
                    // Bỏ chữ "KETOANAPK": người dùng đã ở trong app rồi, tên app chiếm gần nửa header mà
                    // không nói thêm điều gì. Giữ lại đúng tên màn đang xem.
                    title = {
                        Text(
                            vm.selected.title,
                            style = MaterialTheme.typography.titleLarge,
                            color = MaterialTheme.colorScheme.onSurface,
                            fontWeight = FontWeight.Bold,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    },
                    actions = {
                        IconButton(onClick = vm::openSearch) {
                            Icon(Icons.Filled.Search, contentDescription = "Tìm kiếm")
                        }
                        IconButton(enabled = !qrScanner.busy, onClick = qrScanner.startScan) {
                            Icon(Icons.Filled.QrCodeScanner, contentDescription = "Quét mã QR")
                        }
                        NotificationBell(count = vm.unreadCount, onClick = vm::openNotifications)
                        // Ảnh đại diện mở màn Cá nhân — nơi ở của nhóm mục cá nhân sau khi bỏ ngăn kéo.
                        UserAvatar(
                            user.displayName,
                            34,
                            modifier = Modifier
                                .padding(start = 4.dp, end = 12.dp)
                                .clip(CircleShape)
                                .clickable { vm.select(HrDestination.Personal) },
                        )
                    },
                    colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface),
                )
            },
            bottomBar = {
                if (!chatFullScreen) BottomBar(
                    items = vm.bottomDestinations(user),
                    selected = vm.selected,
                    badgeCount = vm::badgeCount,
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
                // Không nhắc trên các màn "chứa"/cấu hình — ở đó banner chỉ chiếm chỗ của danh sách.
                val bannerFreeScreens = setOf(HrDestination.Settings, HrDestination.Personal)
                if (vm.showFaceEnrollBanner && !faceBannerDismissed && vm.selected !in bannerFreeScreens) {
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
                    if (vm.searchOpen) SearchResults(
                        query = vm.searchQuery,
                        results = vm.searchResults(user),
                        badgeCount = vm::badgeCount,
                        onSelect = { vm.closeSearch(); vm.select(it) },
                    ) else screenState.SaveableStateProvider(vm.selected) {
                    when (vm.selected) {
                    HrDestination.Home -> HomeScreen(user, vm.homeState, vm.managerState, vm.hubFor(HrDestination.Home), vm.workTasksState, vm::select)
                    HrDestination.Personal -> PersonalHubScreen(user, vm.homeState, vm::select)
                    HrDestination.Portal -> PortalScreen(vm.portalState, vm.portalDetail, vm::openPortalPost, vm::closePortalDetail)
                    HrDestination.Profile -> ElectronicProfileScreen(vm)
                    HrDestination.Onboarding -> OnboardingScreen(vm)
                    HrDestination.Performance -> PerformanceScreen(vm)
                    HrDestination.Training -> TrainingScreen(vm)
                    HrDestination.Benefits -> BenefitsScreen(vm)
                    HrDestination.Feedback -> SurveyFeedbackScreen(vm)
                    HrDestination.Help -> HelpCenterScreen(vm)
                    HrDestination.Scan -> AttendanceScreen(vm)
                    HrDestination.Timesheet -> TimesheetScreen(vm.timesheetState, vm::changeTimesheetMonth, vm::setTimesheetMonth, vm::startShiftSwap)
                    HrDestination.MySalary -> MySalaryScreen(vm.payEstimateState)
                    HrDestination.MyPayslips -> MyPayslipsScreen(
                        state = vm.payslipsState,
                        openPeriod = vm.payslipOpenPeriod,
                        username = user.username,
                        onOpen = vm::openPayslip,
                        onClose = vm::closePayslip,
                        onAcknowledge = vm::acknowledgePayslip,
                        onInquiry = vm::sendPayslipInquiry,
                        onDownload = vm::downloadPayslip,
                        onVerifyAccountPassword = vm::verifyAccountPassword,
                    )
                    HrDestination.Payout -> PayoutScreen(vm)
                    HrDestination.Requests -> RequestsScreen(vm)
                    HrDestination.Tasks -> TaskCenterScreen(vm)
                    HrDestination.WorkTasks -> WorkTaskScreen(vm)
                    HrDestination.Chat -> RealChatScreen(vm)
                    HrDestination.Directory -> DirectoryScreen(vm)
                    HrDestination.Calls -> CallHistoryScreen(vm)
                    HrDestination.Approval -> StaffRequestsScreen(vm)
                    HrDestination.Penalty -> PenaltyScreen(user, vm.homeState, vm::startPenaltyAppeal)
                    HrDestination.People -> AdminPeopleScreen(vm)
                    HrDestination.Dashboard -> ExecutiveDashboardScreen(vm)
                    HrDestination.Payroll -> PayrollScreen(vm.homeState)
                    HrDestination.Audit -> AuditScreen(vm)
                    HrDestination.Settings -> SettingsScreen(
                        user = user,
                        vm = vm,
                        onScanQr = qrScanner.startScan,
                        onLogout = vm::logout,
                    )
                    HrDestination.Notifications -> NotificationsScreen(
                        notifications = vm.notifications,
                        onOpen = { n -> vm.markNotificationRead(n.id); vm.navigateTo(n.target, n.entityId) },
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
            FullScreenCameraScan(
                onCaptured = vm::onFramesCaptured,
                onCancel = vm::resetCapture,
                motionMode = vm.motionMode,
            )
        }

        // Camera ĐĂNG KÝ khuôn mặt (quét nhiều góc) cũng phủ toàn màn hình như trên.
        if (vm.faceEnroll == FaceEnrollCapture.Capturing) {
            BackHandler { vm.cancelFaceEnroll() }
            FaceEnrollCameraScan(onCompleted = vm::submitFaceEnroll, onCancel = vm::cancelFaceEnroll)
        }

        // Camera CHỤP ẢNH CHÂN DUNG (hồ sơ) phủ toàn màn hình; xong → lưu lên máy chủ.
        // Tham số cắt lấy từ remote config (AppConfig) → chỉnh trên trang Hệ thống, khỏi build APK.
        if (vm.portraitCapture == PortraitCapture.Capturing) {
            BackHandler { vm.cancelPortraitCapture() }
            PortraitCaptureScan(
                onCaptured = vm::submitPortrait,
                onCancel = vm::cancelPortraitCapture,
                params = PortraitCropParams(
                    heightFactor = vm.appConfig.portraitHeightFactor.toFloat(),
                    verticalNudge = vm.appConfig.portraitVerticalNudge.toFloat(),
                    aspect = vm.appConfig.portraitAspect.toFloat(),
                    minWidthFactor = vm.appConfig.portraitMinWidthFactor.toFloat(),
                ),
            )
        }
        if (vm.portraitCapture == PortraitCapture.Saving) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .background(Color(0xCC000000)),
                contentAlignment = Alignment.Center,
            ) {
                CircularProgressIndicator(color = Color.White)
            }
        }
        (vm.portraitCapture as? PortraitCapture.Done)?.let { done ->
            AlertDialog(
                onDismissRequest = vm::resetPortraitCapture,
                confirmButton = { TextButton(onClick = vm::resetPortraitCapture) { Text("Đóng") } },
                title = { Text(if (done.success) "Đã cập nhật ảnh" else "Chưa lưu được ảnh") },
                text = { Text(done.message) },
            )
        }

        // Bảng cập nhật: hiện khi phát hiện bản mới (kiểm tra ngầm lúc đăng nhập/quay lại app) và
        // Ở LẠI suốt lúc tải để vẽ tiến độ — xem [UpdateSheet].
        val update = vm.availableUpdate
        if (vm.updateSheetVisible && update != null) {
            val context = LocalContext.current
            UpdateSheet(
                info = update,
                stage = vm.updateStage,
                needsMeteredConsent = vm.updateNeedsMeteredConsent,
                onDownload = { vm.startUpdateDownload(context) },
                onAcceptMetered = { vm.acceptMeteredUpdate(context) },
                onRetry = { vm.startUpdateDownload(context) },
                onDismiss = vm::dismissUpdateSheet,
            )
        }

    }
}

/** Header biến thành ô nhập khi đang tìm kiếm. Tự bật bàn phím để gõ được ngay, khỏi chạm thêm lần nữa. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SearchTopBar(query: String, onQuery: (String) -> Unit, onClose: () -> Unit) {
    val focus = remember { FocusRequester() }
    LaunchedEffect(Unit) { focus.requestFocus() }
    TopAppBar(
        title = {
            TextField(
                value = query,
                onValueChange = onQuery,
                modifier = Modifier
                    .fillMaxWidth()
                    .focusRequester(focus),
                placeholder = { Text("Tìm màn hình…") },
                singleLine = true,
                colors = TextFieldDefaults.colors(
                    focusedContainerColor = Color.Transparent,
                    unfocusedContainerColor = Color.Transparent,
                    focusedIndicatorColor = Color.Transparent,
                    unfocusedIndicatorColor = Color.Transparent,
                ),
            )
        },
        navigationIcon = {
            IconButton(onClick = onClose) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Đóng tìm kiếm")
            }
        },
        colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface),
    )
}

/** Kết quả tìm kiếm: danh sách màn khớp tên, bấm là nhảy thẳng tới. */
@Composable
private fun SearchResults(
    query: String,
    results: List<HrDestination>,
    badgeCount: (HrDestination) -> Int,
    onSelect: (HrDestination) -> Unit,
) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        when {
            query.isBlank() -> item {
                Text(
                    "Gõ tên màn hình để nhảy thẳng tới, ví dụ: phiếu lương, phúc lợi, danh bạ.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            results.isEmpty() -> item {
                Text(
                    "Không có màn nào khớp \"$query\".",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            else -> item {
                HrCard { HubList(destinations = results, badgeCount = badgeCount, onSelect = onSelect) }
            }
        }
    }
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
            MarqueeText(text = text, color = fg, modifier = Modifier.weight(1f))
            IconButton(onClick = onDismiss) {
                Icon(Icons.Filled.Close, contentDescription = "Ẩn thông báo", tint = fg.copy(alpha = 0.7f))
            }
        }
    }
}

/**
 * Chữ chạy liên tục từ PHẢI sang TRÁI (kiểu bảng LED). Luôn chạy dù nội dung ngắn hay dài:
 * đo bề rộng khung + bề rộng chữ rồi trượt offset từ mép phải ra khỏi mép trái, lặp vô hạn.
 */
@Composable
private fun MarqueeText(text: String, color: Color, modifier: Modifier = Modifier) {
    val density = LocalDensity.current.density
    var containerWidth by remember { mutableIntStateOf(0) }
    var textWidth by remember { mutableIntStateOf(0) }
    val offsetX = remember { Animatable(0f) }

    LaunchedEffect(text, containerWidth, textWidth) {
        if (containerWidth > 0 && textWidth > 0) {
            val start = containerWidth.toFloat()       // bắt đầu ngay ngoài mép phải
            val end = -textWidth.toFloat()             // kết thúc khi khuất hẳn mép trái
            val distance = start - end
            // Tốc độ ~55dp/giây, kẹp trong khoảng 4–40 giây cho dễ đọc.
            val durationMs = (distance / (density * 55f) * 1000f).roundToInt().coerceIn(4000, 40000)
            while (true) {
                offsetX.snapTo(start)
                offsetX.animateTo(end, animationSpec = tween(durationMs, easing = LinearEasing))
            }
        }
    }

    Box(
        modifier = modifier
            .clipToBounds()
            .onGloballyPositioned { containerWidth = it.size.width },
    ) {
        Text(
            text = text,
            color = color,
            maxLines = 1,
            softWrap = false,
            style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.SemiBold,
            modifier = Modifier
                .wrapContentWidth(align = Alignment.Start, unbounded = true)
                .offset { IntOffset(offsetX.value.roundToInt(), 0) }
                .onGloballyPositioned { textWidth = it.size.width },
        )
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
        BadgedBox(badge = { if (count > 0) CountBadge(count) }) {
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
    badgeCount: (HrDestination) -> Int,
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
                        BottomItem(
                            icon = item.icon,
                            label = item.label,
                            active = selected == item,
                            badge = badgeCount(item),
                            modifier = Modifier.weight(1f),
                            onClick = { onSelect(item) },
                        )
                    }
                }
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
    badge: Int,
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
        BadgedBox(badge = { if (badge > 0) CountBadge(badge) }) {
            Icon(icon, contentDescription = label, tint = color, modifier = Modifier.size(22.dp))
        }
        Text(label, style = MaterialTheme.typography.labelSmall, color = color, maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center)
    }
}

/** Huy hiệu số đỏ dùng chung cho thanh dưới, ngăn kéo và chuông thông báo. */
@Composable
private fun CountBadge(count: Int) {
    Badge(
        containerColor = MaterialTheme.colorScheme.error,
        contentColor = MaterialTheme.colorScheme.onError,
    ) { Text(if (count > 99) "99+" else "$count") }
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
