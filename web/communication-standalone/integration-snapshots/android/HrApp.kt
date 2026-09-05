package com.ketoanapk.hr.ui

import android.app.Activity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.LocalOnBackPressedDispatcherOwner
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.expandVertically
import androidx.compose.animation.shrinkVertically
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.AutoAwesome
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.WifiOff
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.NotificationsNone
import androidx.compose.material.icons.filled.SupportAgent
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
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.core.view.WindowCompat
import kotlinx.coroutines.delay
import kotlin.math.roundToInt
import com.ketoanapk.hr.data.AnniversaryGreeting
import com.ketoanapk.hr.R
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.AppPersonalization
import com.ketoanapk.hr.ui.theme.BrandGradientBottom
import com.ketoanapk.hr.ui.theme.BrandGradientTop
import com.ketoanapk.hr.ui.theme.KetoanTheme

/**
 * Artwork dùng chung cho toàn app. Màn xác thực chấm công không dùng ảnh nền chung, nhưng nền trống
 * đồng thành công vẫn được vẽ ở lớp gốc để chạy liền mạch dưới header, footer và thanh điều hướng.
 */
@Composable
private fun AppWallpaper(
    darkTheme: Boolean,
    showArtwork: Boolean,
    attendanceCapture: AttendanceCapture,
) {
    val showAttendanceArtwork = !showArtwork && when (attendanceCapture) {
        is AttendanceCapture.AwaitingConfirm -> true
        is AttendanceCapture.Done -> attendanceCapture.result.status.equals("ok", true) ||
            attendanceCapture.result.status.equals("offline", true) ||
            attendanceCapture.result.status.equals("pending", true)
        else -> false
    }
    val attendanceReveal by animateFloatAsState(
        targetValue = if (showAttendanceArtwork) 1f else 0f,
        animationSpec = tween(900, easing = FastOutSlowInEasing),
        label = "attendance-root-background-reveal",
    )
    val attendanceSettle by animateFloatAsState(
        targetValue = if (showAttendanceArtwork) 1f else 0f,
        animationSpec = tween(1400, easing = FastOutSlowInEasing),
        label = "attendance-root-background-settle",
    )

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(if (showArtwork) MaterialTheme.colorScheme.background else Color.White),
    ) {
        if (showArtwork) {
            Image(
                painter = painterResource(R.drawable.app_background),
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.fillMaxSize(),
            )
            if (darkTheme) {
                // Asset gốc là tông sáng; phủ lớp tối để giữ tương phản mà vẫn thấy logo/họa tiết.
                Box(Modifier.fillMaxSize().background(Color(0xD9061923)))
            }
        } else if (attendanceReveal > 0f) {
            DongSonSuccessBackground(
                revealProgress = attendanceReveal,
                settleProgress = attendanceSettle,
                modifier = Modifier.fillMaxSize(),
            )
        }
    }
}

@Composable
fun HrApp(vm: HrViewModel) {
    val personalizationContext=LocalContext.current
    AppPersonalization.init(personalizationContext)
    val dark=when(AppPersonalization.themeMode){"dark"->true;"light"->false;else->androidx.compose.foundation.isSystemInDarkTheme()}
    KetoanTheme(darkTheme=dark,fontScale=AppPersonalization.fontScale) {
        val rootContext = LocalContext.current
        LaunchedEffect(dark) {
            val window = rootContext.findActivity()?.window ?: return@LaunchedEffect
            WindowCompat.getInsetsController(window, window.decorView).isAppearanceLightNavigationBars = !dark
        }
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
        var introHandoffStarted by rememberSaveable { mutableStateOf(false) }
        val hasPendingWebLogin = vm.pendingQrLoginCode != null || vm.pendingMobileAppLoginCode != null
        LaunchedEffect(hasPendingWebLogin) {
            if (hasPendingWebLogin) {
                introHandoffStarted = true
                showIntro = false
            }
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
        Surface(modifier = Modifier.fillMaxSize(), color = Color.Transparent) {
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
                val attendanceVerification = vm.selected == HrDestination.Scan && when (vm.attendanceCapture) {
                    AttendanceCapture.Recognizing,
                    AttendanceCapture.Submitting,
                    is AttendanceCapture.AwaitingConfirm,
                    is AttendanceCapture.Done -> true
                    else -> false
                }
                AppWallpaper(
                    darkTheme = dark,
                    showArtwork = !attendanceVerification,
                    attendanceCapture = vm.attendanceCapture,
                )
                val introDestination = if (vm.authState is AuthState.SignedIn) {
                    IntroDestination.Home
                } else {
                    IntroDestination.Login
                }
                val contentReveal by animateFloatAsState(
                    targetValue = if (introHandoffStarted || !showIntro) 1f else 0f,
                    animationSpec = tween(
                        durationMillis = 650,
                        easing = CubicBezierEasing(0.16f, 1f, 0.3f, 1f),
                    ),
                    label = "intro-content-handoff",
                )
                val hiddenScale = if (introDestination == IntroDestination.Home) 0.955f else 0.975f
                val hiddenAlpha = if (introDestination == IntroDestination.Home) 0.78f else 0.72f
                val hiddenOffsetPx = with(LocalDensity.current) {
                    (if (introDestination == IntroDestination.Home) 12.dp else 22.dp).toPx()
                }

                // Login và Home được dựng sẵn dưới intro, rồi cùng chuyển động khi logo bắt đầu handoff.
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .graphicsLayer {
                            alpha = hiddenAlpha + (1f - hiddenAlpha) * contentReveal
                            val scale = hiddenScale + (1f - hiddenScale) * contentReveal
                            scaleX = scale
                            scaleY = scale
                            translationY = hiddenOffsetPx * (1f - contentReveal)
                        },
                ) {
                    // AnimatedContent vẫn xử lý các lần đăng nhập/đăng xuất sau intro như trước.
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
                                onVerifyRecoveryCode = vm::verifyRecoveryCode,
                                onResetPasswordWithCode = vm::resetPasswordWithCode,
                            )
                            is AuthState.SignedIn -> HrShell(state.user, vm, qrScanner)
                        }
                    }
                }

                // Giữ intro cũ làm nền, thêm gear chờ API và handoff riêng sang Login/Home.
                if (showIntro) {
                    IntroOverlay(
                        bootstrapReady = vm.authState !is AuthState.Loading,
                        destination = introDestination,
                        onHandoffStarted = { introHandoffStarted = true },
                        onFinished = {
                            introHandoffStarted = true
                            showIntro = false
                        },
                    )
                }

                if (!showIntro && showPermissionOnboarding && !hasPendingWebLogin && !vm.payslipConfirmationVisible) {
                    PermissionOnboardingDialog(
                        onSkip = ::finishPermissionOnboarding,
                        onDone = ::finishPermissionOnboarding,
                    )
                }

                // Thư tri ân nằm trên nội dung ứng dụng nhưng nhường ưu tiên cho cuộc gọi/đăng nhập QR.
                // Chỉ dựng sau intro và hướng dẫn quyền để nhân viên không nhận nhiều popup cùng lúc.
                if (
                    vm.authState is AuthState.SignedIn &&
                    !showIntro &&
                    !showPermissionOnboarding &&
                    !hasPendingWebLogin &&
                    !vm.payslipConfirmationVisible
                ) {
                    vm.anniversaryGreeting?.let { greeting ->
                        AnniversaryLetterDialog(
                            greeting = greeting,
                            onDismiss = vm::dismissAnniversaryGreeting,
                        )
                    }
                }

                // Màn xác nhận lương là một nhánh độc quyền: không cho call/QR/web-login tạo lớp phủ
                // hoặc semantics tương tác khác cho tới khi xác nhận xong (hoặc đóng lượt tự nguyện).
                if (!vm.payslipConfirmationVisible) {
                    CallHost(vm)
                    QrScanDialog(qrScanner)
                    MobileAppLoginDialog(vm)
                }
                }
            }
        }
    }
}

/**
 * Thư cảm ơn nhân viên ở mốc tròn năm. Nội dung được gõ từng ký tự như máy đánh chữ; nhân viên có thể
 * bấm "Hiện toàn bộ" nếu không muốn chờ. Đóng thư đồng nghĩa đã xem mốc này trên thiết bị hiện tại.
 */
@Composable
private fun AnniversaryLetterDialog(greeting: AnniversaryGreeting, onDismiss: () -> Unit) {
    val fullLetter = remember(greeting.key, greeting.body, greeting.signature) {
        listOf(greeting.body.trim(), greeting.signature.trim())
            .filter { it.isNotEmpty() }
            .joinToString("\n\n")
    }
    var visibleChars by remember(greeting.key) { mutableIntStateOf(0) }
    var revealAll by remember(greeting.key) { mutableStateOf(false) }
    val typingComplete = visibleChars >= fullLetter.length

    LaunchedEffect(greeting.key, fullLetter, revealAll) {
        if (revealAll) {
            visibleChars = fullLetter.length
            return@LaunchedEffect
        }
        visibleChars = 0
        while (visibleChars < fullLetter.length) {
            val next = fullLetter[visibleChars]
            delay(
                when (next) {
                    '.', '!', '?' -> 105L
                    ',', ';', ':' -> 55L
                    '\n' -> 90L
                    else -> 18L
                },
            )
            visibleChars++
        }
    }

    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false),
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .systemBarsPadding()
                .padding(18.dp),
            contentAlignment = Alignment.Center,
        ) {
            Surface(
                modifier = Modifier
                    .fillMaxWidth()
                    .widthIn(max = 560.dp)
                    .heightIn(max = 700.dp),
                shape = RoundedCornerShape(28.dp),
                color = MaterialTheme.colorScheme.surface,
                shadowElevation = 24.dp,
                border = BorderStroke(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.22f)),
            ) {
                Column(modifier = Modifier.padding(22.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.Top,
                        horizontalArrangement = Arrangement.spacedBy(14.dp),
                    ) {
                        Surface(
                            modifier = Modifier.size(72.dp),
                            shape = CircleShape,
                            color = MaterialTheme.colorScheme.primaryContainer,
                        ) {
                            Column(
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.Center,
                            ) {
                                Text(
                                    text = greeting.years.toString(),
                                    style = MaterialTheme.typography.headlineMedium,
                                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                                    fontWeight = FontWeight.Black,
                                )
                                Text(
                                    text = "NĂM",
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                                    fontWeight = FontWeight.Bold,
                                )
                            }
                        }
                        Column(modifier = Modifier.weight(1f)) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Icon(
                                    Icons.Filled.AutoAwesome,
                                    contentDescription = null,
                                    modifier = Modifier.size(17.dp),
                                    tint = MaterialTheme.colorScheme.primary,
                                )
                                Spacer(Modifier.width(6.dp))
                                Text(
                                    text = if (greeting.preview) "BẢN XEM THỬ · THƯ TRI ÂN" else "THƯ TRI ÂN",
                                    style = MaterialTheme.typography.labelLarge,
                                    color = MaterialTheme.colorScheme.primary,
                                    fontWeight = FontWeight.Bold,
                                )
                            }
                            Text(
                                text = greeting.title,
                                style = MaterialTheme.typography.titleLarge,
                                color = MaterialTheme.colorScheme.onSurface,
                                fontWeight = FontWeight.Bold,
                            )
                        }
                        IconButton(onClick = onDismiss) {
                            Icon(Icons.Filled.Close, contentDescription = "Đóng thư tri ân")
                        }
                    }

                    Spacer(Modifier.height(18.dp))
                    Surface(
                        modifier = Modifier
                            .fillMaxWidth()
                            .weight(1f, fill = false),
                        shape = RoundedCornerShape(18.dp),
                        color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.55f),
                    ) {
                        val scroll = rememberScrollState()
                        Text(
                            text = fullLetter.take(visibleChars) + if (typingComplete) "" else "▌",
                            modifier = Modifier
                                .verticalScroll(scroll)
                                .padding(18.dp),
                            style = MaterialTheme.typography.bodyLarge.copy(
                                fontFamily = FontFamily.Monospace,
                                lineHeight = MaterialTheme.typography.bodyLarge.lineHeight * 1.18f,
                            ),
                            color = MaterialTheme.colorScheme.onSurface,
                        )
                    }

                    Spacer(Modifier.height(18.dp))
                    Button(
                        modifier = Modifier.fillMaxWidth(),
                        onClick = if (typingComplete) onDismiss else ({ revealAll = true }),
                    ) {
                        Text(
                            when {
                                !typingComplete -> "Hiện toàn bộ"
                                greeting.preview -> "Đóng bản xem thử"
                                else -> "Cảm ơn công ty"
                            },
                        )
                    }
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

    // Header mọi màn đã trong suốt: icon hệ thống chỉ cần đi theo theme của artwork toàn màn hình.
    val statusBarDark = when (AppPersonalization.themeMode) { "dark" -> true; "light" -> false; else -> isSystemInDarkTheme() }
    val shellContext = LocalContext.current
    LaunchedEffect(vm.selected, statusBarDark) {
        val window = shellContext.findActivity()?.window ?: return@LaunchedEffect
        val controller = WindowCompat.getInsetsController(window, window.decorView)
        // isAppearanceLightStatusBars = true → nền sáng → icon tối. Chỉ dùng icon tối khi nền sáng và KHÔNG ở Trang chủ.
        controller.isAppearanceLightStatusBars = !statusBarDark
        // Footer không còn nền đặc nên màu icon thanh điều hướng cũng phải theo theme của app,
        // không theo theme hệ thống vốn có thể đang ngược sáng/tối.
        controller.isAppearanceLightNavigationBars = !statusBarDark
    }

    // Đây là rẽ nhánh root, không phải một Box phủ lên màn cũ. Vì vậy camera/dialog/semantics và
    // vùng bấm của app bên dưới hoàn toàn không được compose trong thời gian xác nhận.
    if (vm.payslipConfirmationVisible) {
        PayslipConfirmationRoute(user = user, vm = vm)
        return
    }

    val isRefreshing = when (vm.selected) {
        HrDestination.People -> vm.managerState.loading
        HrDestination.Settings -> vm.settingsState.loading
        HrDestination.Scan -> vm.attendanceServer is AttendanceServerState.Checking
        HrDestination.Timesheet -> vm.timesheetState.loading
        HrDestination.MyPayslips -> vm.payslipsState.loading
        HrDestination.Payout -> vm.payoutState.loading
        HrDestination.CashCollections -> vm.cashCollectionState.loading
        HrDestination.Portal -> vm.portalState.loading
        HrDestination.Tasks -> vm.homeState.loading || vm.workTasksState.loading
        HrDestination.Chat -> vm.realChatState.loading
        HrDestination.Directory -> vm.directoryState.loading
        HrDestination.Calls -> vm.callHistoryState.loading
        else -> vm.homeState.loading
    }
    val footerDestinations = vm.bottomDestinations(user)

    Box(modifier = Modifier.fillMaxSize()) {
        // Chat là "mini app": chiếm trọn màn, tự dựng header + thanh tab riêng nên ẩn hẳn header và
        // thanh dưới của HR ở đây.
        val chatFullScreen = vm.selected == HrDestination.Chat
        Scaffold(
            // Khóa vùng nội dung vào safe drawing kể cả trong lúc màn camera phủ ngoài Scaffold đổi
            // trạng thái; tiêu đề và kết quả chấm công luôn nằm dưới status bar/navigation bar.
            contentWindowInsets = WindowInsets.safeDrawing,
            containerColor = Color.Transparent,
            snackbarHost = {
                SnackbarHost(
                    hostState = snackbar,
                    modifier = Modifier.padding(bottom = if (chatFullScreen) 0.dp else BottomBarHeight),
                )
            },
            topBar = {
                if (chatFullScreen) Unit
                else if (vm.searchOpen) SearchTopBar(
                    query = vm.searchQuery,
                    onQuery = vm::typeSearch,
                    onClose = vm::closeSearch,
                )
                // Trang chủ dùng header danh tính riêng, trong suốt để giữ artwork liền mạch.
                else if (vm.selected == HrDestination.Home) HomeHeaderBar(
                    user = user,
                    state = vm.homeState,
                    unread = vm.unreadCount,
                    onBell = vm::openNotifications,
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
                        // Màn Đơn từ có nút "Hỗ trợ" ở góc phải: mở thẳng Chat nội bộ (thay cho tab Chat cũ).
                        if (vm.selected == HrDestination.Requests) {
                            SupportButton(
                                unread = vm.chatUnreadCount,
                                onClick = { vm.select(HrDestination.Chat) },
                            )
                        }
                        NotificationBell(count = vm.unreadCount, onClick = vm::openNotifications)
                        Spacer(Modifier.width(4.dp))
                    },
                    colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.Transparent),
                )
            },
        ) { padding ->
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
            ) {
                // Thanh cập nhật nằm ngoài nội dung từng màn nên luôn được ghim ở cùng một vị trí,
                // kể cả Chat toàn màn hình. Không có nút tắt; bấm vào sẽ mở lại bảng chi tiết.
                val availableUpdate = vm.availableUpdate
                AnimatedVisibility(
                    visible = availableUpdate != null && !vm.updateSheetVisible,
                    enter = expandVertically(expandFrom = Alignment.Top) + fadeIn(),
                    exit = shrinkVertically(shrinkTowards = Alignment.Top) + fadeOut(),
                ) {
                    availableUpdate?.let { info ->
                        UpdateReminderBar(info = info, onOpen = vm::openUpdateSheet)
                    }
                }
                // Báo MẤT KẾT NỐI ngay ở đỉnh mọi màn: mất Internet (máy không có mạng) hoặc không chạm
                // được máy chủ. Tự ẩn khi kết nối phục hồi (heartbeat + callback mạng cập nhật liên tục).
                ConnectionBanner(status = vm.connection)
                // Thông báo điều khiển từ xa (admin đặt ở trang Hệ thống → Cập nhật). Ẩn khi để trống.
                // Trang chủ đã chạy nội dung này trong dải thông báo gõ chữ nên bỏ banner ở đây, tránh lặp.
                val announcement = vm.appConfig.announcement
                if (announcement.isNotBlank() && announcement != dismissedAnnouncement && vm.selected != HrDestination.Home) {
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
                    ) else AnimatedContent(
                        targetState = vm.selected,
                        modifier = Modifier.fillMaxSize(),
                        transitionSpec = {
                            val fromIndex = footerDestinations.indexOf(initialState)
                            val toIndex = footerDestinations.indexOf(targetState)
                            val canSlide =
                                fromIndex >= 0 &&
                                    toIndex >= 0 &&
                                    fromIndex != toIndex &&
                                    initialState != HrDestination.Scan &&
                                    targetState != HrDestination.Scan

                            if (canSlide) {
                                val direction = if (toIndex > fromIndex) 1 else -1
                                (
                                    slideInHorizontally(
                                        animationSpec = tween(
                                            durationMillis = 280,
                                            easing = CubicBezierEasing(0.2f, 0f, 0f, 1f),
                                        ),
                                        initialOffsetX = { width -> direction * width / 10 },
                                    ) + fadeIn(tween(220))
                                ) togetherWith (
                                    slideOutHorizontally(
                                        animationSpec = tween(180, easing = FastOutSlowInEasing),
                                        targetOffsetX = { width -> -direction * width / 14 },
                                    ) + fadeOut(tween(160))
                                )
                            } else {
                                fadeIn(tween(220)) togetherWith fadeOut(tween(160))
                            }
                        },
                        label = "footer-screen-transition",
                    ) { destination ->
                    screenState.SaveableStateProvider(destination) {
                    when (destination) {
                    HrDestination.Home -> HomeScreen(
                        user = user,
                        state = vm.homeState,
                        actions = vm.homeActions(user),
                        notifications = vm.notifications,
                        announcement = vm.appConfig.announcement,
                        serverNotices = vm.appConfig.notices,
                        badgeCount = vm::badgeCount,
                        onOpenNotifications = vm::openNotifications,
                        onSelect = vm::select,
                    )
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
                    HrDestination.Timesheet -> TimesheetScreen(
                        state = vm.timesheetState,
                        payEstimate = vm.payEstimateState,
                        dayLog = vm.dayLogState,
                        username = user.username,
                        onMonthOffset = vm::changeTimesheetMonth,
                        onSelectMonth = vm::setTimesheetMonth,
                        onSelectDay = vm::loadDayLog,
                        onShiftSwap = vm::startShiftSwap,
                        onForgotCheckin = vm::startForgotCheckin,
                        onLoadSalary = vm::loadMyEstimate,
                    )
                    HrDestination.MyPayslips -> MyPayslipsScreen(
                        state = vm.payslipsState,
                        openPeriod = vm.payslipOpenPeriod,
                        username = user.username,
                        onOpen = vm::openPayslip,
                        onClose = vm::closePayslip,
                        onOpenConfirmation = vm::openPayslipConfirmation,
                        onInquiry = vm::sendPayslipInquiry,
                        onDownload = vm::downloadPayslip,
                    )
                    HrDestination.Payout -> PayoutScreen(vm)
                    HrDestination.CashCollections -> CashCollectionScreen(vm)
                    HrDestination.Requests -> RequestsScreen(vm)
                    HrDestination.Tasks -> TaskCenterScreen(vm)
                    HrDestination.TaskHistory -> TaskHistoryScreen(vm)
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
        }

        if (!chatFullScreen) {
            BottomBar(
                items = footerDestinations,
                selected = vm.selected,
                badgeCount = vm::badgeCount,
                onSelect = vm::select,
                onScanQr = qrScanner.startScan,
                modifier = Modifier.align(Alignment.BottomCenter),
            )
        }

        // Camera quét khuôn mặt phủ TOÀN MÀN HÌNH (ngoài Scaffold) → không dính thanh tiêu đề/điều hướng.
        if (vm.selected == HrDestination.Scan && vm.attendanceCapture == AttendanceCapture.Collecting) {
            FullScreenCameraScan(
                onCaptured = vm::onFramesCaptured,
                onCancel = vm::resetCapture,
                motionMode = vm.motionMode,
                smileMode = vm.smileMode,
                smileThreshold = vm.smileThreshold,
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

@Composable
private fun PayslipConfirmationRoute(user: HrUser, vm: HrViewModel) {
    PayslipConfirmationScreen(
        reviewKey = vm.payslipConfirmationReviewKey,
        period = vm.payslipConfirmationPeriod,
        dueAt = vm.payslipConfirmationDueAt,
        required = vm.payslipAccessLocked,
        remainingOverdueCount = vm.payslipConfirmationRemainingCount,
        payslip = vm.payslipConfirmationItem,
        loading = vm.payslipsState.loading,
        loadError = vm.payslipConfirmationError ?: vm.payslipsState.error,
        statusMessage = vm.payslipConfirmationMessage,
        submitting = vm.payslipAcknowledgingId != null,
        awaitingSync = vm.payslipConfirmationAwaitingSync,
        username = user.username,
        onRetry = vm::retryPayslipConfirmation,
        onConfirm = vm::confirmPayslipFromConfirmationScreen,
        onInquiry = vm::sendPayslipInquiry,
        onDownload = vm::downloadPayslip,
        onClose = vm::closePayslipConfirmation,
    )
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
        colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.Transparent),
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
        contentPadding = screenPadding(16.dp, 16.dp),
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
/**
 * Banner MẤT KẾT NỐI ở đỉnh app. KHÔNG đóng được (khác announcement): vấn đề kết nối phải nhìn thấy tới
 * khi hết. Hiện có hiệu ứng trượt xuống khi mất, trượt lên khi phục hồi; kèm vòng xoay nhỏ ngụ ý "đang
 * thử lại". Phân biệt "mất Internet" (máy không có mạng) với "không kết nối được máy chủ".
 */
@Composable
private fun ConnectionBanner(status: ConnectionStatus) {
    AnimatedVisibility(
        visible = status != ConnectionStatus.Online,
        enter = expandVertically() + fadeIn(),
        exit = shrinkVertically() + fadeOut(),
    ) {
        // Giữ lại nội dung cuối khác Online để lúc trượt lên (exit) không nhấp nháy chữ.
        val shown = remember { mutableStateOf(status) }
        if (status != ConnectionStatus.Online) shown.value = status
        val noInternet = shown.value == ConnectionStatus.NoInternet
        val icon = if (noInternet) Icons.Filled.WifiOff else Icons.Filled.CloudOff
        val title = if (noInternet) "Mất kết nối Internet" else "Không kết nối được máy chủ"
        val detail = if (noInternet)
            "Kiểm tra Wi-Fi hoặc dữ liệu di động. Đang chờ có mạng trở lại…"
        else
            "Máy chủ đang không phản hồi. Đang tự thử lại…"
        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 14.dp, end = 14.dp, top = 12.dp, bottom = 2.dp),
            shape = RoundedCornerShape(18.dp),
            color = MaterialTheme.colorScheme.errorContainer,
        ) {
            Row(
                modifier = Modifier.padding(start = 14.dp, end = 14.dp, top = 10.dp, bottom = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Icon(
                    icon,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onErrorContainer,
                    modifier = Modifier.size(24.dp),
                )
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        title,
                        color = MaterialTheme.colorScheme.onErrorContainer,
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                    )
                    Text(
                        detail,
                        color = MaterialTheme.colorScheme.onErrorContainer.copy(alpha = 0.85f),
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
                CircularProgressIndicator(
                    modifier = Modifier.size(18.dp),
                    strokeWidth = 2.dp,
                    color = MaterialTheme.colorScheme.onErrorContainer,
                )
            }
        }
    }
}

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
    IconButton(
        onClick = onClick,
        modifier = Modifier.semantics {
            contentDescription = "Thông báo"
            stateDescription = if (count > 0) "$count thông báo chưa đọc" else "Không có thông báo chưa đọc"
        },
    ) {
        BadgedBox(badge = { if (count > 0) CountBadge(count) }) {
            Icon(
                if (count > 0) Icons.Filled.Notifications else Icons.Filled.NotificationsNone,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurface,
            )
        }
    }
}

/** Chiều cao footer nổi: khung nút 64.dp + phần nút quét QR nhô lên trên. */
internal val BottomBarHeight = 84.dp

/**
 * Khoảng CUỘN THÊM ở đáy mỗi trang.
 *
 * Footer là thanh NỔI: nội dung vẫn trôi ngay dưới nó để artwork chạy liền mạch — tuyệt đối KHÔNG
 * chèn một lớp nền chữ nhật hay đẩy cả trang lên trên footer, vì làm vậy là dựng lại đúng cái khung
 * chữ nhật đã bỏ. Thay vào đó chỉ nới đáy vùng cuộn: cuộn tới cuối thì hàng cuối vẫn lên được
 * phía trên footer để đọc và bấm.
 *
 * Dùng qua [screenPadding] cho mọi màn có trong footer. Đây là chỗ DUY NHẤT giữ con số này.
 */
internal val BottomBarScrollRoom = BottomBarHeight + 26.dp

/**
 * contentPadding chuẩn của một màn: lề như cũ, riêng đáy nới thêm cho footer nổi.
 * Danh sách lồng trong màn (tin nhắn, hàng ngang, nội dung hộp thoại) KHÔNG dùng hàm này.
 */
internal fun screenPadding(horizontal: Dp = 14.dp, top: Dp = 14.dp) = PaddingValues(
    start = horizontal,
    top = top,
    end = horizontal,
    bottom = BottomBarScrollRoom,
)

/**
 * Chỉ khung bo tròn chứa các nút có nền. Lớp bao ngoài trong suốt để artwork tiếp tục hiện ở bốn góc
 * và dưới thanh điều hướng Android; tab đang chọn vẫn giữ gạch xanh riêng bên dưới.
 */
@Composable
private fun BottomBar(
    items: List<HrDestination>,
    selected: HrDestination,
    badgeCount: (HrDestination) -> Int,
    onSelect: (HrDestination) -> Unit,
    onScanQr: () -> Unit,
    modifier: Modifier = Modifier,
) {
    if (items.isEmpty()) return

    val navShape = RoundedCornerShape(28.dp)
    val surface = MaterialTheme.colorScheme.surface
    val surfaceVariant = MaterialTheme.colorScheme.surfaceVariant

    Box(
        modifier = modifier
            .fillMaxWidth()
            // Thanh điều hướng Android đã được bật lại: chỉ chừa đúng inset hệ thống để footer không
            // bị che. Không thêm khoảng đệm đáy riêng vì nó tạo một dải trống sau footer.
            .navigationBarsPadding()
            .padding(horizontal = 14.dp)
            .height(BottomBarHeight),
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(64.dp)
                .align(Alignment.BottomCenter)
                .shadow(16.dp, navShape)
                .clip(navShape)
                .background(
                    Brush.verticalGradient(
                        // Khung phải đục hoàn toàn: nếu đáy bán trong suốt, mép các thẻ nội dung
                        // chạy phía sau sẽ xuyên qua thành một vệt trắng dài dưới hàng nút.
                        colors = listOf(surface, surfaceVariant),
                    ),
                )
                .border(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = 0.72f), navShape),
        ) {
            val rowPadding = 8.dp

            Row(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = rowPadding)
                    .selectableGroup(),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                items.forEach { item ->
                    if (item == HrDestination.Scan) {
                        Spacer(Modifier.weight(1f))
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

        QrScanButton(
            onClick = onScanQr,
            active = selected == HrDestination.Scan,
            modifier = Modifier.align(Alignment.TopCenter),
        )
    }
}

/** Nút "Hỗ trợ" ở góc phải màn Đơn từ → mở Chat nội bộ. Kèm huy hiệu số tin nhắn chưa đọc. */
@Composable
private fun SupportButton(unread: Int, onClick: () -> Unit) {
    TextButton(onClick = onClick, contentPadding = PaddingValues(horizontal = 10.dp)) {
        BadgedBox(badge = { if (unread > 0) CountBadge(unread) }) {
            Icon(
                Icons.Filled.SupportAgent,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(22.dp),
            )
        }
        Spacer(Modifier.width(6.dp))
        Text("Hỗ trợ", color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.Bold)
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
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val activeProgress by animateFloatAsState(
        targetValue = if (active) 1f else 0f,
        animationSpec = tween(180, easing = FastOutSlowInEasing),
        label = "footer-item-active-$label",
    )
    val itemScale by animateFloatAsState(
        targetValue = when {
            pressed -> 0.96f
            active -> 1.035f
            else -> 1f
        },
        animationSpec = spring(
            dampingRatio = Spring.DampingRatioNoBouncy,
            stiffness = if (pressed) 700f else Spring.StiffnessMedium,
        ),
        label = "footer-item-scale-$label",
    )
    val itemColor by animateColorAsState(
        targetValue = if (active) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
        animationSpec = tween(160),
        label = "footer-item-color-$label",
    )
    val badgeProgress by animateFloatAsState(
        targetValue = if (badge > 0) 1f else 0f,
        animationSpec = tween(160),
        label = "footer-badge-$label",
    )
    Box(
        modifier = modifier
            .fillMaxHeight()
            .selectable(
                selected = active,
                interactionSource = interaction,
                indication = null,
                role = Role.Tab,
                onClick = onClick,
            ),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .graphicsLayer {
                    scaleX = itemScale
                    scaleY = itemScale
                }
                .padding(horizontal = 2.dp, vertical = 5.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(2.dp),
        ) {
            BadgedBox(
                badge = {
                    Badge(
                        modifier = Modifier.graphicsLayer {
                            alpha = badgeProgress
                            scaleX = 0.70f + badgeProgress * 0.30f
                            scaleY = 0.70f + badgeProgress * 0.30f
                        },
                        containerColor = MaterialTheme.colorScheme.error,
                        contentColor = MaterialTheme.colorScheme.onError,
                    ) {
                        if (badge > 0) Text(if (badge > 99) "99+" else "$badge")
                    }
                },
            ) {
                Icon(
                    imageVector = icon,
                    contentDescription = label,
                    tint = itemColor,
                    modifier = Modifier.size(22.dp),
                )
            }
            Text(
                text = label,
                style = MaterialTheme.typography.labelSmall,
                color = itemColor,
                fontWeight = if (active) FontWeight.ExtraBold else FontWeight.Medium,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                textAlign = TextAlign.Center,
            )
            Box(
                modifier = Modifier
                    .width(16.dp)
                    .height(2.dp)
                    .graphicsLayer {
                        alpha = activeProgress
                        scaleX = activeProgress.coerceAtLeast(0.01f)
                    }
                    .background(MaterialTheme.colorScheme.primary, CircleShape),
            )
        }
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

/**
 * Nút Quét QR nổi ở giữa thanh dưới. Vạch sáng quét một chiều từ trên xuống, halo tỏa dần ra ngoài
 * và thân nút thở rất nhẹ; các lớp luôn dùng chung một tâm để chuyển động không bị rung/lệch.
 */
@Composable
private fun QrScanButton(
    onClick: () -> Unit,
    active: Boolean,
    modifier: Modifier = Modifier,
) {
    val onPrimary = MaterialTheme.colorScheme.onPrimary
    val transition = rememberInfiniteTransition(label = "qr")
    val scanProgress by transition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(1450, easing = LinearEasing),
            repeatMode = RepeatMode.Restart,
        ),
        label = "qr-scan-line",
    )
    val breathe by transition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(1800, easing = FastOutSlowInEasing),
            repeatMode = RepeatMode.Reverse,
        ),
        label = "qr-breathe",
    )
    val haloProgress by transition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(2000, easing = LinearEasing),
            repeatMode = RepeatMode.Restart,
        ),
        label = "qr-halo-wave",
    )
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val buttonScale by animateFloatAsState(
        targetValue = when {
            pressed -> 0.94f
            active -> 1.04f
            else -> 1f
        },
        animationSpec = spring(dampingRatio = 0.72f, stiffness = 520f),
        label = "qr-button-scale",
    )
    val ringColor by animateColorAsState(
        targetValue = if (active) Color(0xFFFCDE00) else Color.White.copy(alpha = 0.82f),
        animationSpec = tween(180),
        label = "qr-ring-color",
    )
    val labelColor by animateColorAsState(
        targetValue = if (active) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
        animationSpec = tween(160),
        label = "qr-label-color",
    )

    Box(
        modifier = modifier
            .width(80.dp)
            .height(84.dp),
    ) {
        // Halo và nút dùng chung một hộp căn giữa để tâm hai vòng luôn trùng tuyệt đối.
        Box(
            modifier = Modifier
                .size(70.dp)
                .align(Alignment.TopCenter),
            contentAlignment = Alignment.Center,
        ) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .graphicsLayer {
                        val haloScale = 0.90f + haloProgress * 0.18f
                        scaleX = haloScale
                        scaleY = haloScale
                        alpha = (1f - haloProgress) * if (active) 0.28f else 0.18f
                    }
                    .border(2.dp, MaterialTheme.colorScheme.primary, CircleShape),
            )

            Box(
                modifier = Modifier
                    .size(64.dp)
                    .graphicsLayer {
                        val idleBreath = 0.992f + breathe * 0.016f
                        scaleX = buttonScale * idleBreath
                        scaleY = buttonScale * idleBreath
                    }
                    .shadow(14.dp, CircleShape)
                    .clip(CircleShape)
                    .background(
                        Brush.linearGradient(
                            colors = listOf(BrandGradientTop, MaterialTheme.colorScheme.primary, BrandGradientBottom),
                        ),
                    )
                    .border(2.dp, ringColor, CircleShape)
                    .clickable(
                        interactionSource = interaction,
                        indication = null,
                        role = Role.Button,
                        onClick = onClick,
                    ),
                contentAlignment = Alignment.Center,
            ) {
                Canvas(modifier = Modifier.size(30.dp)) {
                    val s = size.minDimension
                    val stroke = s * 0.10f
                    val inset = stroke / 2f
                    val left = inset
                    val top = inset
                    val right = s - inset
                    val bottom = s - inset
                    val arm = s * (0.23f + 0.04f * breathe)
                    val cap = StrokeCap.Round

                    drawLine(onPrimary, Offset(left, top), Offset(left + arm, top), stroke, cap)
                    drawLine(onPrimary, Offset(left, top), Offset(left, top + arm), stroke, cap)
                    drawLine(onPrimary, Offset(right, top), Offset(right - arm, top), stroke, cap)
                    drawLine(onPrimary, Offset(right, top), Offset(right, top + arm), stroke, cap)
                    drawLine(onPrimary, Offset(left, bottom), Offset(left + arm, bottom), stroke, cap)
                    drawLine(onPrimary, Offset(left, bottom), Offset(left, bottom - arm), stroke, cap)
                    drawLine(onPrimary, Offset(right, bottom), Offset(right - arm, bottom), stroke, cap)
                    drawLine(onPrimary, Offset(right, bottom), Offset(right, bottom - arm), stroke, cap)

                    val gridInset = s * 0.30f
                    val cell = (s - gridInset * 2f) / 3f
                    val dot = cell * 0.74f
                    fun module(cx: Int, cy: Int, scale: Float) {
                        val d = dot * scale
                        val off = (cell - d) / 2f
                        drawRect(
                            color = onPrimary,
                            topLeft = Offset(gridInset + cx * cell + off, gridInset + cy * cell + off),
                            size = Size(d, d),
                        )
                    }
                    module(0, 0, 1f); module(2, 0, 1f); module(0, 2, 1f)
                    module(1, 1, 0.6f); module(2, 2, 0.55f)

                    val trackTop = top + arm * 0.15f
                    val trackBottom = bottom - arm * 0.15f
                    val y = trackTop + (trackBottom - trackTop) * scanProgress
                    // Mờ hoàn toàn tại hai đầu để lúc chu kỳ quay lại đỉnh không tạo cảm giác giật.
                    val scanAlpha =
                        (1f - kotlin.math.abs(scanProgress * 2f - 1f)).coerceIn(0f, 1f)
                    drawLine(
                        color = Color(0xFFFCDE00).copy(alpha = scanAlpha * 0.28f),
                        start = Offset(left + arm * 0.10f, y),
                        end = Offset(right - arm * 0.10f, y),
                        strokeWidth = stroke * 2f,
                        cap = cap,
                    )
                    drawLine(
                        color = Color(0xFFFCDE00).copy(alpha = scanAlpha),
                        start = Offset(left + arm * 0.15f, y),
                        end = Offset(right - arm * 0.15f, y),
                        strokeWidth = stroke * 0.70f,
                        cap = cap,
                    )
                }
            }
        }

        Text(
            text = "Quét",
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(bottom = 2.dp),
            style = MaterialTheme.typography.labelSmall,
            color = labelColor,
            fontWeight = if (active) FontWeight.ExtraBold else FontWeight.Bold,
            maxLines = 1,
        )
    }
}
