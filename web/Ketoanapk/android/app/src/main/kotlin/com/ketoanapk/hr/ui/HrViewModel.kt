package com.ketoanapk.hr.ui

import android.Manifest
import android.app.Application
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AccountBalanceWallet
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Gavel
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Inbox
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.Payments
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.Settings
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.ketoanapk.hr.data.AppConfig
import com.ketoanapk.hr.data.AppEvents
import com.ketoanapk.hr.data.AppNotification
import com.ketoanapk.hr.data.AppNotifier
import com.ketoanapk.hr.data.AppUpdater
import com.ketoanapk.hr.data.ChamCongResult
import com.ketoanapk.hr.data.CreateRequestBody
import com.ketoanapk.hr.data.Department
import com.ketoanapk.hr.data.DeviceSession
import com.ketoanapk.hr.data.EmployeeCard
import com.ketoanapk.hr.data.EmployeeDetail
import com.ketoanapk.hr.data.FaceEnrollPose
import com.ketoanapk.hr.data.HrRepository
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.ManagerSummary
import com.ketoanapk.hr.data.NotificationCenter
import com.ketoanapk.hr.data.NotificationWorker
import com.ketoanapk.hr.data.Penalty
import com.ketoanapk.hr.data.RealtimeClient
import com.ketoanapk.hr.data.ReleaseInfo
import com.ketoanapk.hr.data.RequestDetail
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.RequestType
import com.ketoanapk.hr.data.PayEstimate
import com.ketoanapk.hr.data.SalaryListItem
import com.ketoanapk.hr.data.Timesheet
import com.ketoanapk.hr.data.TokenStore
import com.ketoanapk.hr.network.ApiException
import com.google.firebase.messaging.FirebaseMessaging
import kotlinx.coroutines.Job
import kotlinx.serialization.json.JsonObject
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.time.YearMonth

sealed interface AuthState {
    data object Loading : AuthState
    data object SignedOut : AuthState
    data class SignedIn(val user: HrUser) : AuthState
}

enum class HrDestination(
    val title: String,
    val label: String,
    val icon: ImageVector,
    val adminOnly: Boolean = false,
) {
    Home("Trang chủ", "Trang chủ", Icons.Filled.Home),
    Profile("Hồ sơ của tôi", "Hồ sơ", Icons.Filled.AccountCircle),
    Scan("Chấm công", "Chấm công", Icons.Filled.Face),
    Timesheet("Bảng công", "Bảng công", Icons.Filled.CalendarMonth),
    Requests("Đơn từ", "Đơn từ", Icons.Filled.Description),
    // Nhân viên tự xem lương dự tính tháng hiện tại (gồm phạt nếu có).
    MySalary("Lương của tôi", "Lương", Icons.Filled.AccountBalanceWallet),
    // Chỉ để XEM trạng thái đơn của nhân sự (không duyệt trong app — duyệt trên bản web).
    Approval("Đơn chờ duyệt", "Chờ duyệt", Icons.Filled.Inbox),
    Penalty("Kỷ luật", "Kỷ luật", Icons.Filled.Gavel),
    People("Quản lý nhân sự", "Quản lý", Icons.Filled.People, adminOnly = true),
    Payroll("Bảng lương", "Lương", Icons.Filled.Payments, adminOnly = true),
    Audit("Nhật ký hệ thống", "Nhật ký", Icons.Filled.History, adminOnly = true),
    Settings("Cài đặt", "Cài đặt", Icons.Filled.Settings),
    Notifications("Thông báo", "Thông báo", Icons.Filled.Notifications),
}

data class NavGroup(val title: String, val destinations: List<HrDestination>)

data class HomeUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val employee: EmployeeDetail? = null,
    val timesheet: Timesheet? = null,
    val requests: List<RequestListItem> = emptyList(),
    val inbox: List<RequestListItem> = emptyList(),
    val penalties: List<Penalty> = emptyList(),
    val salaries: List<SalaryListItem> = emptyList(),
    val requestTypes: List<RequestType> = emptyList(),
)

/**
 * Trạng thái xem chi tiết một đơn (mở khi id != null). canCancel = đơn của chính mình còn chờ duyệt
 * → cho phép hủy. Với đơn của nhân sự khác thì mở ở chế độ CHỈ ĐỌC (canCancel=false); việc phê duyệt
 * được thực hiện trên bản web.
 */
data class RequestDetailUiState(
    val id: String? = null,
    val loading: Boolean = false,
    val error: String? = null,
    val detail: RequestDetail? = null,
    val canCancel: Boolean = false,
)

data class TimesheetUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val month: String = currentMonthKey(),
    val timesheet: Timesheet? = null,
)

data class ManagerUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val summary: ManagerSummary? = null,
    val employees: List<EmployeeCard> = emptyList(),
    val departments: List<Department> = emptyList(),
)

/** Lương dự tính của chính nhân viên (tháng hiện tại). */
data class PayEstimateUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val data: PayEstimate? = null,
)

data class SettingsUiState(
    val loading: Boolean = false,
    val webLoginEnabled: Boolean? = null,
    val pushNotificationsEnabled: Boolean? = null,
    val devices: List<DeviceSession> = emptyList(),
    val devicesLoading: Boolean = false,
    val checkingUpdate: Boolean = false,
    val installing: Boolean = false,
    val updateInfo: ReleaseInfo? = null,
    val updateChecked: Boolean = false,
    val updateMessage: String? = null,
)

/** Trạng thái kết nối máy chủ chấm công (LAN). */
sealed interface AttendanceServerState {
    data object Checking : AttendanceServerState
    data class Online(val engine: String, val threshold: Double) : AttendanceServerState
    data class Offline(val message: String) : AttendanceServerState
}

/** Trạng thái luồng chấm công bằng khuôn mặt trên máy (kiểu sinh trắc học có bước xác nhận). */
sealed interface AttendanceCapture {
    data object Idle : AttendanceCapture
    data object Collecting : AttendanceCapture    // camera đang căn khung 2 bước + quét 3s soi sáng
    data object Recognizing : AttendanceCapture   // đã gửi loạt ảnh, máy chủ đang nhận diện (xem trước)
    // Đã nhận diện xong nhưng CHƯA ghi công — chờ người dùng bấm Xác nhận. Giữ lại loạt ảnh để gửi lại.
    data class AwaitingConfirm(val result: ChamCongResult, val frames: List<String>) : AttendanceCapture
    data object Submitting : AttendanceCapture     // đang ghi công thật lên máy chủ (sau khi xác nhận)
    data class Done(val result: ChamCongResult) : AttendanceCapture
}

/** Trạng thái luồng TỰ ĐĂNG KÝ khuôn mặt (quét nhiều góc → gửi máy chủ lưu mẫu). */
sealed interface FaceEnrollCapture {
    data object Idle : FaceEnrollCapture
    data object Capturing : FaceEnrollCapture   // camera đang quét lần lượt các góc (toàn màn hình)
    data object Submitting : FaceEnrollCapture   // đang gửi mẫu lên máy chủ để lưu
    data class Done(val success: Boolean, val message: String) : FaceEnrollCapture
}

class HrViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = HrRepository(application)
    private val notificationCenter = NotificationCenter(application)
    // Client SignalR (realtime tức thì khi app đang mở, như bản web). Bật/tắt theo foreground.
    private val realtime = RealtimeClient(TokenStore(application))
    private var heartbeatJob: Job? = null
    // Vòng làm mới nhẹ khi app đang mở (foreground): tự cập nhật trạng thái đơn từ khi admin duyệt
    // trên web mà không cần người dùng kéo làm mới. Dừng khi app xuống nền để tiết kiệm pin
    // (nền đã có WorkManager + push FCM lo thông báo).
    private var foregroundPollJob: Job? = null
    private var pendingTarget: HrDestination? = null
    private var pushToken: String? = null
    private var captureOffline = false   // lượt chấm hiện tại là ngoại tuyến (mất mạng) hay trực tuyến

    var authState: AuthState by mutableStateOf(AuthState.Loading)
        private set
    var loginLoading by mutableStateOf(false)
        private set
    var loginError: String? by mutableStateOf(null)
        private set
    var resetPasswordLoading by mutableStateOf(false)
        private set
    var selected by mutableStateOf(HrDestination.Home)
        private set
    var homeState by mutableStateOf(HomeUiState(loading = true))
        private set
    var timesheetState by mutableStateOf(TimesheetUiState(loading = true))
        private set
    var payEstimateState by mutableStateOf(PayEstimateUiState())
        private set
    var requestDetailState by mutableStateOf(RequestDetailUiState())
        private set
    var creatingRequest by mutableStateOf(false)
        private set
    var managerState by mutableStateOf(ManagerUiState())
        private set
    var settingsState by mutableStateOf(SettingsUiState())
        private set
    // Màn con đang mở trong tab Cài đặt. Đặt ở ViewModel để nút Back của điện thoại lùi về đúng cấp
    // (từ màn con → Cài đặt gốc) thay vì nhảy thẳng về Trang chủ.
    var settingsRoute by mutableStateOf(SettingsRoute.Home)
    var attendanceServer: AttendanceServerState by mutableStateOf(AttendanceServerState.Checking)
        private set
    var attendanceCapture: AttendanceCapture by mutableStateOf(AttendanceCapture.Idle)
        private set
    var attendancePending by mutableStateOf(0)   // số bản chấm ngoại tuyến đang chờ đồng bộ
        private set
    // Đăng ký khuôn mặt: trạng thái đã đăng ký (null = chưa biết) + luồng quét đăng ký.
    var faceRegistered: Boolean? by mutableStateOf(null)
        private set
    var faceStatusLoading by mutableStateOf(false)
        private set
    var faceEnroll: FaceEnrollCapture by mutableStateOf(FaceEnrollCapture.Idle)
        private set
    // Tín hiệu "mở thẳng màn Đăng ký khuôn mặt" (bấm từ banner nhắc) — màn Cài đặt đọc rồi tự nhảy vào.
    var openFaceEnroll by mutableStateOf(false)
        private set
    // Cấu hình điều khiển từ xa (admin đổi mà không cần ra APK): thông báo trong app, bật/tắt banner
    // khuôn mặt, nhịp tự làm mới. Nạp lúc đăng nhập + mỗi lần quay lại foreground (có tiết chế).
    var appConfig by mutableStateOf(AppConfig())
        private set
    private var lastConfigFetchAt = 0L

    /** Chỉ hiện banner nhắc khuôn mặt khi CHẮC CHẮN chưa đăng ký VÀ admin không tắt từ xa. */
    val showFaceEnrollBanner: Boolean get() = faceRegistered == false && appConfig.faceEnrollBannerEnabled
    var rememberedUsername by mutableStateOf("")
        private set
    var actionMessage: String? by mutableStateOf(null)
        private set
    var notifications: List<AppNotification> by mutableStateOf(emptyList())
        private set

    // Cập nhật tự động (không cần vào Cài đặt): bản mới tìm thấy ngầm + cờ hiện hộp thoại nhắc.
    var availableUpdate: ReleaseInfo? by mutableStateOf(null)
        private set
    var updatePromptVisible by mutableStateOf(false)
        private set
    private var lastUpdateCheckAt = 0L        // mốc lần kiểm tra gần nhất (chống gọi dồn)
    private var dismissedUpdateVersionCode = 0 // mã bản người dùng đã bấm "Để sau" (không nhắc lại)

    val unreadCount: Int get() = notifications.count { !it.read }

    /** Số đơn đang chờ tôi duyệt (hộp thư đã lọc sẵn ở máy chủ theo người duyệt/quản trị). */
    val pendingApprovalCount: Int get() = homeState.inbox.count { it.status.equals("Pending", true) }

    val bottomDestinations = listOf(
        HrDestination.Home,
        HrDestination.Timesheet,
        HrDestination.Scan,
        HrDestination.Requests,
    )

    private val navGroups = listOf(
        NavGroup("Cá nhân", listOf(HrDestination.Home, HrDestination.Profile, HrDestination.Scan, HrDestination.Timesheet, HrDestination.MySalary, HrDestination.Requests)),
        NavGroup("Công việc", listOf(HrDestination.Approval, HrDestination.Penalty)),
        NavGroup("Quản trị", listOf(HrDestination.People, HrDestination.Payroll, HrDestination.Audit)),
        NavGroup("Hệ thống", listOf(HrDestination.Settings)),
    )

    init {
        viewModelScope.launch { rememberedUsername = repo.rememberedUsername() }
        // FCM báo có dữ liệu đổi từ máy chủ → làm mới NGAY màn đang xem (đơn từ tức thì, không chờ poll).
        viewModelScope.launch {
            AppEvents.dataChanged.collect {
                if (authState is AuthState.SignedIn) pollLiveData()
            }
        }
        restoreSession()
    }

    fun visibleNavGroups(user: HrUser): List<NavGroup> =
        navGroups.map { g -> g.copy(destinations = g.destinations.filter { !it.adminOnly || user.isAdmin }) }
            .filter { it.destinations.isNotEmpty() }

    fun consumeActionMessage() { actionMessage = null }

    fun login(username: String, password: String, remember: Boolean) {
        if (username.isBlank() || password.isBlank()) {
            loginError = "Vui lòng nhập tên đăng nhập và mật khẩu."
            return
        }
        viewModelScope.launch {
            loginLoading = true
            loginError = null
            runCatching { repo.login(username, password, remember) }
                .onSuccess { result ->
                    rememberedUsername = if (remember) username.trim() else ""
                    onSignedIn(result.user)
                }
                .onFailure { loginError = readable(it) }
            loginLoading = false
        }
    }

    /** Quên mật khẩu bằng khuôn mặt: backend so 1:1 với mẫu đã đăng ký của đúng username. */
    fun resetPasswordWithFace(
        username: String,
        newPassword: String,
        images: List<String>,
        onDone: (Boolean, String?) -> Unit,
    ) {
        if (username.isBlank()) {
            onDone(false, "Vui lòng nhập tên đăng nhập.")
            return
        }
        if (newPassword.length < 6) {
            onDone(false, "Mật khẩu mới cần ít nhất 6 ký tự.")
            return
        }
        if (images.isEmpty()) {
            onDone(false, "Chưa chụp được khuôn mặt. Vui lòng thử lại.")
            return
        }
        viewModelScope.launch {
            resetPasswordLoading = true
            runCatching { repo.resetPasswordWithFace(username, newPassword, images) }
                .onSuccess {
                    rememberedUsername = username.trim()
                    onDone(true, null)
                }
                .onFailure { onDone(false, readable(it)) }
            resetPasswordLoading = false
        }
    }

    private fun onSignedIn(user: HrUser) {
        authState = AuthState.SignedIn(user)
        selected = HrDestination.Home
        startHeartbeat()
        startForegroundPoll() // tự làm mới danh sách/chi tiết đơn khi app đang mở
        realtime.start()      // realtime tức thì như web (app đang mở lúc đăng nhập)
        loadNotifications()
        syncPushDelivery()
        refreshHome(user, silent = false)
        loadTimesheet(currentMonthKey(), silent = false)
        if (user.isAdmin) refreshManager(silent = true)
        faceRegistered = user.faceRegistered // cờ đi kèm dữ liệu đăng nhập → không cần gọi API riêng
        loadAppConfig(force = true)
        consumePendingTarget(user)
        autoCheckForUpdate(force = true)
    }

    private fun syncPushDelivery() {
        viewModelScope.launch {
            val enabled = repo.pushNotificationsEnabled()
            val permissionGranted = hasNotificationPermission()
            val effectiveEnabled = enabled && permissionGranted
            if (enabled && !permissionGranted) {
                repo.setPushNotificationsEnabled(false)
            }
            settingsState = settingsState.copy(pushNotificationsEnabled = effectiveEnabled)
            if (effectiveEnabled) {
                startPushDelivery()
            } else {
                stopPushDelivery(unregister = enabled)
            }
        }
    }

    fun loadPushNotificationSetting() {
        syncPushDelivery()
    }

    fun refreshPushPermissionState() {
        syncPushDelivery()
    }

    fun setPushNotificationsEnabled(enabled: Boolean) {
        settingsState = settingsState.copy(pushNotificationsEnabled = enabled)
        viewModelScope.launch {
            repo.setPushNotificationsEnabled(enabled)
            if (enabled) {
                if (hasNotificationPermission()) {
                    startPushDelivery()
                    actionMessage = "Đã bật thông báo push của ứng dụng."
                } else {
                    repo.setPushNotificationsEnabled(false)
                    settingsState = settingsState.copy(pushNotificationsEnabled = false)
                    stopPushDelivery(unregister = true)
                    actionMessage = "Chưa cấp quyền thông báo nên push vẫn đang tắt."
                }
            } else {
                stopPushDelivery(unregister = true)
                actionMessage = "Đã tắt thông báo push của ứng dụng."
            }
        }
    }

    fun onNotificationPermissionDenied() {
        settingsState = settingsState.copy(pushNotificationsEnabled = false)
        viewModelScope.launch {
            repo.setPushNotificationsEnabled(false)
            stopPushDelivery(unregister = true)
            actionMessage = "Chưa cấp quyền thông báo nên push vẫn đang tắt."
        }
    }

    private fun hasNotificationPermission(): Boolean {
        val context = getApplication<Application>()
        val runtimeGranted = Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
        return runtimeGranted && NotificationManagerCompat.from(context).areNotificationsEnabled()
    }

    private fun startPushDelivery() {
        NotificationWorker.schedule(getApplication<Application>())
        registerPush()
    }

    private fun stopPushDelivery(unregister: Boolean) {
        NotificationWorker.cancel(getApplication<Application>())
        if (unregister) unregisterPush()
    }

    /** Lấy token FCM của thiết bị rồi đăng ký với máy chủ để nhận push tức thì. */
    private fun registerPush() {
        runCatching {
            FirebaseMessaging.getInstance().token.addOnSuccessListener { token ->
                if (!token.isNullOrBlank()) {
                    pushToken = token
                    viewModelScope.launch { repo.registerPushToken(token) }
                }
            }
        }
    }

    private fun unregisterPush() {
        val current = pushToken
        pushToken = null
        if (!current.isNullOrBlank()) {
            viewModelScope.launch { repo.unregisterPushToken(current) }
        }
        runCatching {
            FirebaseMessaging.getInstance().token.addOnSuccessListener { token ->
                if (!token.isNullOrBlank() && token != current) {
                    viewModelScope.launch { repo.unregisterPushToken(token) }
                }
            }
        }
    }

    fun logout() {
        viewModelScope.launch {
            pushToken?.let { runCatching { repo.unregisterPushToken(it) } }
            pushToken = null
            repo.logout()
            stopHeartbeat()
            onAppPaused() // dừng vòng poll foreground
            NotificationWorker.cancel(getApplication<Application>())
            notificationCenter.reset()
            notifications = emptyList()
            authState = AuthState.SignedOut
            selected = HrDestination.Home
            homeState = HomeUiState()
            timesheetState = TimesheetUiState()
            payEstimateState = PayEstimateUiState()
            managerState = ManagerUiState()
            settingsState = SettingsUiState()
            attendanceServer = AttendanceServerState.Checking
            attendanceCapture = AttendanceCapture.Idle
            faceRegistered = null
            faceEnroll = FaceEnrollCapture.Idle
            openFaceEnroll = false
            appConfig = AppConfig()
            lastConfigFetchAt = 0L
            availableUpdate = null
            updatePromptVisible = false
            dismissedUpdateVersionCode = 0
            lastUpdateCheckAt = 0L
        }
    }

    // ── Thông báo (chuông) ──────────────────────────────────────────────────────
    private fun loadNotifications() {
        viewModelScope.launch { notifications = notificationCenter.load() }
    }

    private fun syncNotifications(user: HrUser, state: HomeUiState) {
        viewModelScope.launch {
            val fresh = notificationCenter.sync(state.requests, state.inbox, state.penalties, user.isAdmin)
            notifications = notificationCenter.current
            if (repo.pushNotificationsEnabled() && hasNotificationPermission()) {
                fresh.forEach { AppNotifier.show(getApplication<Application>(), it) }
            }
        }
    }

    fun openNotifications() { selected = HrDestination.Notifications }

    fun markNotificationRead(id: String) {
        viewModelScope.launch { notifications = notificationCenter.markRead(id) }
    }

    fun markAllNotificationsRead() {
        viewModelScope.launch { notifications = notificationCenter.markAllRead() }
    }

    fun clearNotifications() {
        viewModelScope.launch { notifications = notificationCenter.clearAll() }
    }

    /** Điều hướng theo thông báo hệ thống (deep-link) khi mở app từ khay thông báo. */
    fun navigateTo(target: String?) {
        // Thông báo "bản cập nhật mới" → kiểm tra lại ngay và mở hộp thoại cập nhật (không phải điều hướng).
        if (target == UPDATE_TARGET) { autoCheckForUpdate(force = true); return }
        val dest = target?.let { runCatching { HrDestination.valueOf(it) }.getOrNull() } ?: return
        val user = (authState as? AuthState.SignedIn)?.user
        if (user == null) { pendingTarget = dest; return }
        if (dest.adminOnly && !user.isAdmin) return
        select(dest)
    }

    private fun consumePendingTarget(user: HrUser) {
        val dest = pendingTarget ?: return
        pendingTarget = null
        if (dest.adminOnly && !user.isAdmin) return
        selected = dest
    }

    fun select(destination: HrDestination) {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        if (destination.adminOnly && !user.isAdmin) return
        selected = destination
        closeRequestDetail() // đóng chi tiết đơn đang mở (nếu có) khi chuyển màn để vào trạng thái sạch
        when (destination) {
            HrDestination.People -> if (managerState.summary == null) refreshManager(silent = false)
            HrDestination.Scan -> { resetCapture(); checkAttendanceServer(); refreshPendingCount() }
            HrDestination.Timesheet -> if (timesheetState.timesheet == null && !timesheetState.loading) loadTimesheet(timesheetState.month, silent = false)
            HrDestination.MySalary -> if (payEstimateState.data == null && !payEstimateState.loading) loadMyEstimate()
            HrDestination.Settings -> {
                settingsRoute = SettingsRoute.Home // vào tab Cài đặt luôn bắt đầu ở màn gốc
                if (settingsState.webLoginEnabled == null) loadSettings()
                if (settingsState.pushNotificationsEnabled == null) loadPushNotificationSetting()
            }
            HrDestination.Audit -> Unit
            else -> if (homeState.employee == null && !homeState.loading) refreshHome(user, silent = true)
        }
    }

    fun refreshCurrent() {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        when (selected) {
            HrDestination.People -> refreshManager(silent = false)
            HrDestination.Scan -> checkAttendanceServer()
            HrDestination.Timesheet -> loadTimesheet(timesheetState.month, silent = false)
            HrDestination.MySalary -> loadMyEstimate()
            HrDestination.Settings -> {
                loadSettings()
                loadPushNotificationSetting()
            }
            else -> {
                refreshHome(user, silent = false)
                if (user.isAdmin) refreshManager(silent = true)
            }
        }
        // Nếu đang mở chi tiết một đơn thì làm mới luôn (kéo để xem tiến trình duyệt mới nhất).
        requestDetailState.id?.let { refreshOpenDetail(it) }
    }

    fun cancel(id: String) = decide { repo.cancelRequest(id); "Đã hủy đơn." }

    // ── Tạo đơn từ + xem chi tiết ────────────────────────────────────────────────
    /** Gửi một đơn mới (payload đã được màn hình dựng từ các trường nhập). */
    fun submitRequest(type: String, title: String, payload: JsonObject, onDone: (Boolean) -> Unit) {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        viewModelScope.launch {
            creatingRequest = true
            runCatching { repo.createRequest(CreateRequestBody(type = type, title = title.trim(), payload = payload)) }
                .onSuccess {
                    actionMessage = "Đã gửi đơn ${it.requestNo}. Bạn có thể theo dõi trạng thái ở đây."
                    refreshHome(user, silent = true)
                    onDone(true)
                }
                .onFailure { actionMessage = readable(it); onDone(false) }
            creatingRequest = false
        }
    }

    /** Xem chi tiết đơn của CHÍNH MÌNH — cho phép hủy khi còn chờ duyệt. */
    fun openRequestDetail(id: String) = loadRequestDetail(id, canCancel = true)

    /** Xem chi tiết đơn của nhân sự khác ở chế độ CHỈ ĐỌC (phê duyệt thực hiện trên bản web). */
    fun openStaffDetail(id: String) = loadRequestDetail(id, canCancel = false)

    private fun loadRequestDetail(id: String, canCancel: Boolean) {
        requestDetailState = RequestDetailUiState(id = id, loading = true, canCancel = canCancel)
        viewModelScope.launch {
            runCatching { repo.requestDetail(id) }
                .onSuccess { requestDetailState = RequestDetailUiState(id = id, detail = it, canCancel = canCancel) }
                .onFailure { requestDetailState = requestDetailState.copy(loading = false, error = readable(it)) }
        }
    }

    fun closeRequestDetail() { requestDetailState = RequestDetailUiState() }

    /** Hủy đơn ngay trong màn chi tiết rồi đóng lại. */
    fun cancelFromDetail(id: String) {
        closeRequestDetail()
        cancel(id)
    }

    private fun decide(block: suspend () -> String) {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        viewModelScope.launch {
            runCatching { block() }
                .onSuccess {
                    actionMessage = it
                    refreshHome(user, silent = true)
                    if (user.isAdmin) refreshManager(silent = true)
                }
                .onFailure { actionMessage = readable(it) }
        }
    }

    fun changeTimesheetMonth(offset: Int) {
        loadTimesheet(shiftMonthKey(timesheetState.month, offset), silent = false)
    }

    fun resetTimesheetMonth() {
        loadTimesheet(currentMonthKey(), silent = false)
    }

    /** Nhảy tới đúng tháng/năm người dùng chọn từ bộ chọn tháng (định dạng "yyyy-MM"). */
    fun setTimesheetMonth(month: String) {
        loadTimesheet(month, silent = false)
    }

    /** Tải lương dự tính (tháng hiện tại) của chính nhân viên đang đăng nhập. */
    fun loadMyEstimate() {
        viewModelScope.launch {
            payEstimateState = payEstimateState.copy(loading = true, error = null)
            runCatching { repo.myEstimate() }
                .onSuccess { payEstimateState = PayEstimateUiState(loading = false, data = it) }
                .onFailure { payEstimateState = payEstimateState.copy(loading = false, error = readable(it)) }
        }
    }

    private fun loadTimesheet(month: String, silent: Boolean) {
        viewModelScope.launch {
            if (!silent) timesheetState = timesheetState.copy(loading = true, error = null, month = month, timesheet = null)
            else timesheetState = timesheetState.copy(loading = true, error = null, month = month)
            runCatching { repo.myTimesheet(month) }
                .onSuccess { timesheetState = TimesheetUiState(loading = false, month = it.period.ifBlank { month }, timesheet = it) }
                .onFailure { timesheetState = timesheetState.copy(loading = false, error = readable(it)) }
        }
    }

    private fun shiftMonthKey(month: String, offset: Int): String {
        val ym = runCatching { YearMonth.parse(month.take(7)) }.getOrElse { YearMonth.now() }
        return ym.plusMonths(offset.toLong()).toString()
    }

    private fun restoreSession() {
        viewModelScope.launch {
            val token = repo.savedToken()
            if (token.isNullOrBlank()) {
                authState = AuthState.SignedOut
                homeState = HomeUiState()
                return@launch
            }
            runCatching { repo.me() }
                .onSuccess { user ->
                    authState = AuthState.SignedIn(user)
                    startHeartbeat()
                    startForegroundPoll() // tự làm mới danh sách/chi tiết đơn khi app đang mở
                    realtime.start()      // realtime tức thì như web (app đang mở lúc khôi phục phiên)
                    loadNotifications()
                    syncPushDelivery()
                    refreshHome(user, silent = false)
                    loadTimesheet(currentMonthKey(), silent = false)
                    if (user.isAdmin) refreshManager(silent = true)
                    faceRegistered = user.faceRegistered // cờ đi kèm dữ liệu đăng nhập → không cần gọi API riêng
                    loadAppConfig(force = true)
                    consumePendingTarget(user)
                }
                .onFailure {
                    repo.logout()
                    authState = AuthState.SignedOut
                    homeState = HomeUiState()
                }
        }
    }

    private fun refreshHome(user: HrUser, silent: Boolean) {
        viewModelScope.launch {
            if (!silent) homeState = homeState.copy(loading = true, error = null)
            runCatching {
                val month = currentMonthKey()
                HomeUiState(
                    loading = false,
                    employee = runCatching { repo.myProfile() }.getOrNull(),
                    timesheet = runCatching { repo.myTimesheet(month) }.getOrNull(),
                    requests = runCatching { repo.requests("mine") }.getOrDefault(emptyList()),
                    // Hộp thư duyệt cho MỌI người: máy chủ đã lọc theo người duyệt (quản lý trực tiếp) hoặc quản trị.
                    inbox = runCatching { repo.requests("inbox") }.getOrDefault(emptyList()),
                    penalties = runCatching { repo.penalties(if (user.isAdmin) "all" else "mine", if (user.isAdmin) month else null) }.getOrDefault(emptyList()),
                    salaries = if (user.isAdmin) runCatching { repo.salaries() }.getOrDefault(emptyList()) else emptyList(),
                    requestTypes = runCatching { repo.requestTypes() }.getOrDefault(emptyList()),
                )
            }.onSuccess {
                homeState = it
                syncNotifications(user, it)
            }
                .onFailure { homeState = homeState.copy(loading = false, error = readable(it)) }
        }
    }

    private fun refreshManager(silent: Boolean) {
        viewModelScope.launch {
            if (!silent) managerState = managerState.copy(loading = true, error = null)
            runCatching {
                ManagerUiState(
                    loading = false,
                    summary = repo.managerSummary(todayKey(), currentMonthKey()),
                    employees = runCatching { repo.employees() }.getOrDefault(emptyList()),
                    departments = runCatching { repo.departments() }.getOrDefault(emptyList()),
                )
            }.onSuccess { managerState = it }
                .onFailure { managerState = managerState.copy(loading = false, error = readable(it)) }
        }
    }

    private fun startHeartbeat() {
        heartbeatJob?.cancel()
        heartbeatJob = viewModelScope.launch {
            while (isActive) {
                repo.heartbeat()
                delay(45_000)
            }
        }
    }

    private fun stopHeartbeat() {
        heartbeatJob?.cancel()
        heartbeatJob = null
    }

    // ── Tự làm mới khi app đang mở (thay cho "phải kéo/F5" mỗi lần admin duyệt) ──────────
    /** Activity gọi khi app quay lại foreground: làm mới ngay + bật vòng poll nhẹ. */
    fun onAppResumed() {
        if (authState !is AuthState.SignedIn) return
        pollLiveData()          // cập nhật ngay khi vừa mở lại app
        loadAppConfig()         // lấy remote config mới (tiết chế 60s)
        startForegroundPoll()
        realtime.start()        // realtime tức thì như web khi app đang mở
    }

    /** Activity gọi khi app xuống nền: dừng poll + SignalR (nền đã có WorkManager + push FCM). */
    fun onAppPaused() {
        foregroundPollJob?.cancel()
        foregroundPollJob = null
        realtime.stop()
    }

    private fun startForegroundPoll() {
        if (foregroundPollJob?.isActive == true) return
        foregroundPollJob = viewModelScope.launch {
            while (isActive) {
                delay(pollIntervalMs()) // nhịp lấy từ remote config (admin chỉnh được), chặn 5–3600s
                if (authState is AuthState.SignedIn) pollLiveData()
            }
        }
    }

    /** Nhịp tự làm mới foreground (mili-giây) theo remote config, có chặn biên an toàn. */
    private fun pollIntervalMs(): Long = appConfig.foregroundPollSeconds.coerceIn(5, 3600) * 1000L

    /** Nạp remote config (tiết chế 60s trừ khi ép). Lỗi mạng → giữ cấu hình cũ, im lặng. */
    private fun loadAppConfig(force: Boolean = false) {
        val now = System.currentTimeMillis()
        if (!force && now - lastConfigFetchAt < 60_000L) return
        lastConfigFetchAt = now
        viewModelScope.launch {
            runCatching { repo.appConfig() }.onSuccess { appConfig = it }
        }
    }

    /**
     * Làm mới im lặng các dữ liệu hay đổi từ nơi khác (admin duyệt trên web): danh sách "Đơn của tôi",
     * hộp thư "Chờ duyệt", và chi tiết đơn đang mở. Chỉ ghi đè khi tải được (giữ nguyên dữ liệu cũ nếu
     * một nhịp mạng lỗi) để không nhấp nháy/mất danh sách.
     */
    private fun pollLiveData() {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        viewModelScope.launch {
            val mine = runCatching { repo.requests("mine") }.getOrNull()
            val inbox = runCatching { repo.requests("inbox") }.getOrNull()
            if (mine != null || inbox != null) {
                val next = homeState.copy(
                    requests = mine ?: homeState.requests,
                    inbox = inbox ?: homeState.inbox,
                )
                homeState = next
                syncNotifications(user, next) // cập nhật chuông (đơn được duyệt/từ chối) — đã tự chống trùng
            }
        }
        // Đang xem chi tiết một đơn → làm mới để thấy kết quả duyệt/tiến trình ngay.
        requestDetailState.id?.let { refreshOpenDetail(it) }
    }

    /** Làm mới im lặng chi tiết đơn đang mở (không hiện lại vòng quay tải, không đụng nếu đã đóng/đổi đơn). */
    private fun refreshOpenDetail(id: String) {
        viewModelScope.launch {
            runCatching { repo.requestDetail(id) }
                .onSuccess { detail ->
                    if (requestDetailState.id == id) {
                        requestDetailState = requestDetailState.copy(detail = detail, loading = false, error = null)
                    }
                }
        }
    }

    // ── Cài đặt ─────────────────────────────────────────────────────────────────
    fun loadSettings() {
        viewModelScope.launch {
            settingsState = settingsState.copy(loading = true)
            val web = runCatching { repo.accountSettings().webLoginEnabled }.getOrNull()
            settingsState = settingsState.copy(loading = false, webLoginEnabled = web)
        }
    }

    fun setWebLoginEnabled(enabled: Boolean) {
        settingsState = settingsState.copy(webLoginEnabled = enabled) // cập nhật lạc quan
        viewModelScope.launch {
            runCatching { repo.setWebLoginEnabled(enabled) }
                .onSuccess { actionMessage = if (enabled) "Đã bật đăng nhập trên web." else "Đã tắt đăng nhập trên web." }
                .onFailure {
                    settingsState = settingsState.copy(webLoginEnabled = !enabled)
                    actionMessage = readable(it)
                }
        }
    }

    fun loadDevices() {
        viewModelScope.launch {
            settingsState = settingsState.copy(devicesLoading = true)
            runCatching { repo.devices() }
                .onSuccess { settingsState = settingsState.copy(devicesLoading = false, devices = it) }
                .onFailure { settingsState = settingsState.copy(devicesLoading = false); actionMessage = readable(it) }
        }
    }

    fun revokeDevice(sid: String) {
        viewModelScope.launch {
            runCatching { repo.revokeDevice(sid) }
                .onSuccess { actionMessage = "Đã thu hồi thiết bị."; loadDevices() }
                .onFailure { actionMessage = readable(it) }
        }
    }

    fun changePassword(current: String, next: String, onDone: (Boolean) -> Unit) {
        viewModelScope.launch {
            runCatching { repo.changePassword(current, next) }
                .onSuccess { actionMessage = "Đã đổi mật khẩu."; onDone(true) }
                .onFailure { actionMessage = readable(it); onDone(false) }
        }
    }

    fun checkForUpdate(context: Context) {
        viewModelScope.launch {
            settingsState = settingsState.copy(checkingUpdate = true, updateMessage = null)
            val current = AppUpdater.installedVersionCode(context)
            runCatching { repo.latestRelease(current) }
                .onSuccess {
                    settingsState = settingsState.copy(
                        checkingUpdate = false,
                        updateChecked = true,
                        updateInfo = if (it.hasUpdate) it else null,
                        updateMessage = if (it.hasUpdate) "Có bản cập nhật ${it.version}." else "Bạn đang dùng phiên bản mới nhất.",
                    )
                }
                .onFailure { settingsState = settingsState.copy(checkingUpdate = false, updateChecked = true, updateMessage = readable(it)) }
        }
    }

    /**
     * Kiểm tra cập nhật NGẦM để nhắc người dùng ngay trên Trang chủ (không phải vào Cài đặt).
     * Gọi lúc đăng nhập và mỗi khi app quay lại foreground; tự chặn gọi dồn trong 10 phút.
     * Lỗi mạng thì im lặng — người dùng vẫn có nút "Kiểm tra cập nhật" thủ công trong Cài đặt.
     */
    fun autoCheckForUpdate(force: Boolean = false) {
        if (authState !is AuthState.SignedIn) return
        val now = System.currentTimeMillis()
        if (!force && now - lastUpdateCheckAt < 10 * 60 * 1000L) return
        lastUpdateCheckAt = now
        viewModelScope.launch {
            val ctx = getApplication<Application>()
            val current = AppUpdater.installedVersionCode(ctx)
            runCatching { repo.latestRelease(current) }
                .onSuccess { info ->
                    if (info.hasUpdate) {
                        availableUpdate = info
                        // Đồng bộ luôn màn Cài đặt để nút "Cập nhật ngay" ở đó cũng sẵn sàng.
                        settingsState = settingsState.copy(
                            updateInfo = info,
                            updateChecked = true,
                            updateMessage = "Có bản cập nhật ${info.version}.",
                        )
                        // Bản bắt buộc luôn nhắc; bản thường không nhắc lại nếu vừa bấm "Để sau".
                        if (info.isMandatory || info.versionCode != dismissedUpdateVersionCode) {
                            updatePromptVisible = true
                        }
                    }
                }
        }
    }

    /** Người dùng bấm "Để sau" trong hộp thoại nhắc — ẩn đi và không nhắc lại bản này trong phiên. */
    fun dismissUpdatePrompt() {
        dismissedUpdateVersionCode = availableUpdate?.versionCode ?: dismissedUpdateVersionCode
        updatePromptVisible = false
    }

    /** Người dùng bấm "Cập nhật ngay" trong hộp thoại nhắc. */
    fun confirmUpdatePrompt(context: Context) {
        updatePromptVisible = false
        installUpdate(context)
    }

    fun installUpdate(context: Context) {
        val release = settingsState.updateInfo ?: availableUpdate ?: return
        if (!AppUpdater.canInstallPackages(context)) {
            AppUpdater.openUnknownSourcesSettings(context)
            actionMessage = "Hãy cho phép cài ứng dụng không rõ nguồn rồi bấm cập nhật lại."
            return
        }
        viewModelScope.launch {
            settingsState = settingsState.copy(installing = true)
            runCatching {
                val file = AppUpdater.apkCacheFile(context, release.apkFileName)
                repo.downloadRelease(release, file)
                AppUpdater.openInstaller(context, file)
            }
                .onSuccess { settingsState = settingsState.copy(installing = false) }
                .onFailure {
                    settingsState = settingsState.copy(installing = false)
                    actionMessage = readable(it)
                }
        }
    }

    // ── Chấm công: kiểm tra kết nối máy chủ LAN ─────────────────────────────────
    fun checkAttendanceServer() {
        viewModelScope.launch {
            attendanceServer = AttendanceServerState.Checking
            runCatching { repo.faceEngineStatus() }
                .onSuccess {
                    attendanceServer = AttendanceServerState.Online(it.engine.ifBlank { "Máy chủ chấm công" }, it.matchThreshold)
                    syncOffline() // máy chủ đã sống lại → đẩy các bản chấm ngoại tuyến còn tồn
                }
                .onFailure { attendanceServer = AttendanceServerState.Offline(readable(it)) }
            refreshPendingCount()
        }
    }

    /** Đếm lại số bản chấm ngoại tuyến đang chờ đồng bộ (cho badge/nhãn). */
    fun refreshPendingCount() {
        viewModelScope.launch { attendancePending = runCatching { repo.offlineCount() }.getOrDefault(0) }
    }

    /** Đồng bộ hàng đợi ngoại tuyến khi có mạng; báo người dùng nếu có bản được gửi. */
    private fun syncOffline() {
        viewModelScope.launch {
            val n = runCatching { repo.syncOfflineAttendance() }.getOrDefault(0)
            attendancePending = runCatching { repo.offlineCount() }.getOrDefault(0)
            if (n > 0) actionMessage = "Đã đồng bộ $n lượt chấm công ngoại tuyến (chờ quản lý duyệt)."
        }
    }

    // ── Chấm công: căn khung → nhận diện (xem trước) → xác nhận → ghi công ───────
    fun startCapture() {
        if (attendanceServer is AttendanceServerState.Online) {
            captureOffline = false
            attendanceCapture = AttendanceCapture.Collecting
        }
    }

    /** Bắt đầu chấm công NGOẠI TUYẾN (khi mất mạng/máy chủ): quét xong lưu tạm, chờ đồng bộ + duyệt. */
    fun startOfflineCapture() {
        captureOffline = true
        attendanceCapture = AttendanceCapture.Collecting
    }

    fun resetCapture() { attendanceCapture = AttendanceCapture.Idle }

    /** Camera quét xong → tuỳ chế độ: nhận diện trực tuyến (xem trước) hoặc lưu tạm ngoại tuyến. */
    fun onFramesCaptured(images: List<String>) {
        if (captureOffline) saveOfflineAttendance(images) else previewAttendance(images)
    }

    /**
     * Camera đã quét xong loạt khung → gửi lên `/api/chamcong/cham` ở chế độ XEM TRƯỚC (previewOnly):
     * máy chủ chỉ nhận diện (chưa ghi công) rồi trả về ai + Vào/Ra dự kiến để hiện form xác nhận.
     * Chỉ khi khớp đúng người đang đăng nhập mới sang bước xác nhận; các trường hợp khác (sai tư thế,
     * mờ, giả mạo, không khớp…) hiện luôn kết quả để người dùng quét lại.
     */
    fun previewAttendance(images: List<String>) {
        if (images.isEmpty()) { attendanceCapture = AttendanceCapture.Idle; return }
        attendanceCapture = AttendanceCapture.Recognizing
        viewModelScope.launch {
            runCatching { repo.chamCong(images, previewOnly = true) }
                .onSuccess { result ->
                    attendanceCapture =
                        if (result.status.equals("ok", true) && result.matched)
                            AttendanceCapture.AwaitingConfirm(result, images)
                        else
                            AttendanceCapture.Done(result)
                }
                .onFailure {
                    attendanceCapture = AttendanceCapture.Done(
                        ChamCongResult(status = "error", message = readable(it)),
                    )
                }
        }
    }

    /** Người dùng bấm "Xác nhận" → gửi lại đúng loạt ảnh (previewOnly=false) để ghi công thật. */
    fun confirmAttendance() {
        val pending = attendanceCapture as? AttendanceCapture.AwaitingConfirm ?: return
        attendanceCapture = AttendanceCapture.Submitting
        viewModelScope.launch {
            runCatching { repo.chamCong(pending.frames, previewOnly = false) }
                .onSuccess { result ->
                    attendanceCapture = AttendanceCapture.Done(result)
                    if (result.status.equals("ok", true)) {
                        (authState as? AuthState.SignedIn)?.user?.let { refreshHome(it, silent = true) }
                        loadTimesheet(timesheetState.month, silent = true)
                    }
                }
                .onFailure {
                    attendanceCapture = AttendanceCapture.Done(
                        ChamCongResult(status = "error", message = readable(it)),
                    )
                }
        }
    }

    /** Lưu tạm lượt chấm ngoại tuyến (kèm GPS nếu có quyền) rồi báo "đã lưu, chờ duyệt". */
    private fun saveOfflineAttendance(images: List<String>) {
        if (images.isEmpty()) { attendanceCapture = AttendanceCapture.Idle; return }
        attendanceCapture = AttendanceCapture.Submitting
        viewModelScope.launch {
            val occurredAt = java.time.Instant.now().toString()
            val loc = readLastLocation()
            runCatching { repo.saveOfflineAttendance(images, occurredAt, loc?.first, loc?.second) }
            attendancePending = runCatching { repo.offlineCount() }.getOrDefault(attendancePending + 1)
            attendanceCapture = AttendanceCapture.Done(
                ChamCongResult(
                    status = "offline",
                    matched = false,
                    occurredAt = occurredAt,
                    message = "Đã lưu chấm công ngoại tuyến — sẽ tự đồng bộ và chờ quản lý duyệt khi có mạng.",
                ),
            )
        }
    }

    /** Bấm "Quét lại" từ form xác nhận hoặc màn kết quả → bỏ ảnh cũ, quét lại từ đầu (giữ chế độ). */
    fun rescanAttendance() {
        attendanceCapture = when {
            captureOffline -> AttendanceCapture.Collecting
            attendanceServer is AttendanceServerState.Online -> AttendanceCapture.Collecting
            else -> AttendanceCapture.Idle
        }
    }

    // ── Tự đăng ký khuôn mặt (mỗi tài khoản một lần, quét nhiều góc) ─────────────
    /** Nạp trạng thái đã đăng ký khuôn mặt của chính tài khoản (để làm mờ nút đăng ký). */
    fun loadFaceStatus(force: Boolean = false) {
        if (!force && faceRegistered != null) return
        viewModelScope.launch {
            faceStatusLoading = true
            runCatching { repo.myFaceStatus() }
                .onSuccess { faceRegistered = it.registered }
                .onFailure { /* im lặng: giữ null, người dùng vẫn có thể thử đăng ký */ }
            faceStatusLoading = false
        }
    }

    /** Banner nhắc "Đăng ký ngay" → sang màn Cài đặt và ra tín hiệu tự mở màn Đăng ký khuôn mặt. */
    fun requestFaceEnroll() {
        openFaceEnroll = true
        select(HrDestination.Settings)
    }

    /** Màn Cài đặt gọi sau khi đã nhảy vào Đăng ký khuôn mặt để không lặp lại. */
    fun clearOpenFaceEnroll() { openFaceEnroll = false }

    /** Bắt đầu quét đăng ký khuôn mặt (chặn nếu đã đăng ký rồi). */
    fun startFaceEnroll() {
        if (faceRegistered == true) return
        faceEnroll = FaceEnrollCapture.Capturing
    }

    /** Hủy quét đăng ký (bấm Đóng / nút Back). */
    fun cancelFaceEnroll() { faceEnroll = FaceEnrollCapture.Idle }

    /** Bỏ kết quả đăng ký (thành công/thất bại) để quay về trạng thái nghỉ. */
    fun resetFaceEnroll() { faceEnroll = FaceEnrollCapture.Idle }

    /** Camera đã quét đủ các góc → gửi lên máy chủ lưu mẫu. */
    fun submitFaceEnroll(poses: List<FaceEnrollPose>) {
        if (poses.isEmpty()) { faceEnroll = FaceEnrollCapture.Idle; return }
        faceEnroll = FaceEnrollCapture.Submitting
        viewModelScope.launch {
            runCatching { repo.enrollFace(poses) }
                .onSuccess {
                    faceRegistered = true
                    faceEnroll = FaceEnrollCapture.Done(true, it.message.ifBlank { "Đăng ký khuôn mặt thành công." })
                }
                .onFailure {
                    faceEnroll = FaceEnrollCapture.Done(false, readable(it))
                }
        }
    }

    /** Đọc vị trí GPS gần nhất (nỗ lực tốt nhất) nếu đã có quyền — dùng cho kiểm tra geofence khi duyệt. */
    @android.annotation.SuppressLint("MissingPermission")
    private fun readLastLocation(): Pair<Double, Double>? {
        val ctx = getApplication<Application>()
        val granted =
            ContextCompat.checkSelfPermission(ctx, android.Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED ||
                ContextCompat.checkSelfPermission(ctx, android.Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED
        if (!granted) return null
        val lm = ctx.getSystemService(Context.LOCATION_SERVICE) as? android.location.LocationManager ?: return null
        return try {
            var best: android.location.Location? = null
            for (p in lm.getProviders(true)) {
                val l = lm.getLastKnownLocation(p) ?: continue
                if (best == null || l.time > best.time) best = l
            }
            best?.let { it.latitude to it.longitude }
        } catch (_: SecurityException) {
            null
        }
    }

    private fun readable(error: Throwable): String = when (error) {
        is ApiException -> error.message ?: "Đã xảy ra lỗi."
        else -> error.message ?: "Không kết nối được máy chủ."
    }

    override fun onCleared() {
        stopHeartbeat()
        onAppPaused()
        super.onCleared()
    }

    companion object {
        /** Sentinel target của thông báo "bản cập nhật mới" (khớp với backend PushService). */
        const val UPDATE_TARGET = "AppUpdate"
    }
}
