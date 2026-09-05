package com.ketoanapk.hr.ui

import android.Manifest
import android.app.Application
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.SystemClock
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.CardGiftcard
import androidx.compose.material.icons.filled.Checklist
import androidx.compose.material.icons.filled.Dashboard
import androidx.compose.material.icons.filled.Contacts
import androidx.compose.material.icons.filled.Campaign
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.FactCheck
import androidx.compose.material.icons.filled.Gavel
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.HelpCenter
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Inbox
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.Payments
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Poll
import androidx.compose.material.icons.filled.PriceCheck
import androidx.compose.material.icons.filled.ReceiptLong
import androidx.compose.material.icons.filled.School
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.TaskAlt
import androidx.compose.material.icons.filled.TrackChanges
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.vector.ImageVector
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.ketoanapk.hr.data.AppConfig
import com.ketoanapk.hr.data.AppPermissions
import com.ketoanapk.hr.data.AuditEntry
import com.ketoanapk.hr.data.AppEvents
import com.ketoanapk.hr.data.AppNotification
import com.ketoanapk.hr.data.APP_UPDATE_NOTIFICATION_TARGET
import com.ketoanapk.hr.data.AppNotifier
import com.ketoanapk.hr.data.AppUpdater
import com.ketoanapk.hr.data.CapturedFrame
import com.ketoanapk.hr.data.ChamCongResult
import com.ketoanapk.hr.data.AttendancePolicy
import com.ketoanapk.hr.data.OfflineAttendanceItem
import com.ketoanapk.hr.data.OfflineAttendanceRecord
import com.ketoanapk.hr.data.CreateRequestBody
import com.ketoanapk.hr.data.Department
import com.ketoanapk.hr.data.DeviceSession
import com.ketoanapk.hr.data.EmployeeCard
import com.ketoanapk.hr.data.EmployeeDetail
import com.ketoanapk.hr.data.EmployeeDocument
import com.ketoanapk.hr.data.OnboardingSummary
import com.ketoanapk.hr.data.PerformanceSummary
import com.ketoanapk.hr.data.TrainingCourse
import com.ketoanapk.hr.data.BenefitsSummary
import com.ketoanapk.hr.data.FaceEnrollPose
import com.ketoanapk.hr.data.HrRepository
import com.ketoanapk.hr.data.SessionRestore
import com.ketoanapk.hr.data.SessionStatus
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.JobPosition
import com.ketoanapk.hr.data.ManagerSummary
import com.ketoanapk.hr.data.MobileAppLoginChallenge
import com.ketoanapk.hr.data.NotificationCenter
import com.ketoanapk.hr.data.NotificationWorker
import com.ketoanapk.hr.data.missedCheckoutMonthKeys
import com.ketoanapk.hr.data.notificationAccountScope
import com.ketoanapk.hr.data.Penalty
import com.ketoanapk.hr.data.PortalFeed
import com.ketoanapk.hr.data.PortalPost
import com.ketoanapk.hr.data.SseRealtimeClient
import com.ketoanapk.hr.data.ReleaseInfo
import com.ketoanapk.hr.data.RequestDetail
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.RequestType
import com.ketoanapk.hr.data.DayLog
import com.ketoanapk.hr.data.PayEstimate
import com.ketoanapk.hr.data.PayslipItem
import com.ketoanapk.hr.data.PayslipRequirement
import com.ketoanapk.hr.data.CreatePayoutBody
import com.ketoanapk.hr.data.PayoutCategory
import com.ketoanapk.hr.data.PayoutRecipient
import com.ketoanapk.hr.data.PayoutRefundSource
import com.ketoanapk.hr.data.PayoutVoucher
import com.ketoanapk.hr.data.CashCollection
import com.ketoanapk.hr.data.CashCollectionCustomer
import com.ketoanapk.hr.data.CashCollectionDriver
import com.ketoanapk.hr.data.CashCountLineBody
import com.ketoanapk.hr.data.CreateCashCollectionBody
import com.ketoanapk.hr.data.SalaryListItem
import com.ketoanapk.hr.data.Timesheet
import com.ketoanapk.hr.data.TokenStore
import com.ketoanapk.hr.data.CreateTaskBody
import com.ketoanapk.hr.data.TaskCollection
import com.ketoanapk.hr.data.WorkTask
import com.ketoanapk.hr.data.WorkTaskDetailResult
import com.ketoanapk.hr.data.WorkTaskHistoryResult
import com.ketoanapk.hr.data.WorkTaskListResult
import com.ketoanapk.hr.data.WorkTaskMeta
import com.ketoanapk.hr.data.WorkTaskSummary
import com.ketoanapk.hr.network.ApiException
import com.google.firebase.messaging.FirebaseMessaging
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.YearMonth


sealed interface AuthState {
    data object Loading : AuthState
    data object SignedOut : AuthState
    data class SignedIn(val user: HrUser) : AuthState
}

sealed interface MobileAppLoginState {
    data object Idle : MobileAppLoginState
    data object Received : MobileAppLoginState
    data object AwaitingAppLogin : MobileAppLoginState
    data object Resolving : MobileAppLoginState
    data class Confirmation(
        val challenge: MobileAppLoginChallenge,
        val submitting: Boolean = false,
        val error: String? = null,
    ) : MobileAppLoginState
    data class Finished(val message: String, val accepted: Boolean) : MobileAppLoginState
}

/**
 * Các màn hình của app. Biểu tượng phải ĐÔI MỘT KHÁC NHAU: người dùng lướt ngăn kéo bằng hình chứ không
 * đọc từng chữ, nên hai màn dùng chung một icon thì icon đó không mang thông tin gì. Thêm màn mới nhớ
 * chọn icon chưa ai dùng.
 */
enum class HrDestination(
    val title: String,
    val label: String,
    val icon: ImageVector,
    /** Quyền server cần có để ứng dụng hiện/đi tới màn này; null = mọi tài khoản đã đăng nhập. */
    val requiredPermission: String? = null,
) {
    Home("Trang chủ", "Trang chủ", Icons.Filled.Home),
    // Màn "chứa" nhóm cá nhân, mở bằng ảnh đại diện trên header (thay ngăn kéo hamburger đã bỏ).
    Personal("Cá nhân", "Cá nhân", Icons.Filled.Person),
    Portal("Cổng thông tin", "Cổng TT", Icons.Filled.Campaign),
    Profile("Hồ sơ của tôi", "Hồ sơ", Icons.Filled.AccountCircle),
    Scan("Chấm công", "Chấm công", Icons.Filled.Face),
    Timesheet("Bảng công", "Bảng công", Icons.Filled.CalendarMonth),
    Requests("Đơn từ", "Đơn từ", Icons.Filled.Description),
    Onboarding("Bắt đầu công việc", "Onboarding", Icons.Filled.Checklist),
    Performance("Mục tiêu & KPI", "Hiệu suất", Icons.Filled.TrackChanges),
    Training("Đào tạo nội bộ", "Đào tạo", Icons.Filled.School),
    Benefits("Phúc lợi", "Phúc lợi", Icons.Filled.CardGiftcard),
    Feedback("Khảo sát & phản hồi", "Khảo sát", Icons.Filled.Poll),
    Help("Trung tâm trợ giúp", "Trợ giúp", Icons.Filled.HelpCenter),
    // Màn công việc DUY NHẤT (đã gộp màn "Giao việc" cũ vào đây): nhân viên thấy việc được giao +
    // đơn/chấm công cần xử lý; Thủ kho/Admin có thêm tab "Việc tôi giao" để giao & nghiệm thu.
    Tasks("Việc cần làm", "Công việc", Icons.Filled.TaskAlt),
    // Màn RIÊNG (không phải hộp thoại) tra lại việc ĐÃ HOÀN THÀNH theo tuần/tháng. Không nằm trong
    // homeActions: chỉ vào từ nút "Lịch sử" của màn Việc cần làm để không đẻ thêm một ô ở Trang chủ.
    TaskHistory("Lịch sử công việc", "Lịch sử việc", Icons.Filled.FactCheck),
    Directory("Danh bạ", "Danh bạ", Icons.Filled.Contacts),
    // Nhân viên tự xem các phiếu lương đã nhận (mỗi tháng một thẻ).
    MyPayslips("Phiếu lương", "Phiếu lương", Icons.Filled.ReceiptLong),
    // Phiếu chi tiền mặt: nhân viên xem phiếu của mình; kế toán lập phiếu + hiện QR ngay trên app.
    Payout("Phiếu chi", "Phiếu chi", Icons.Filled.Payments),
    // Kế toán tạo lệnh; tài xế thu tiền; thủ quỹ kiểm đếm bàn giao. Không dùng GPS hay địa chỉ.
    CashCollections("Thu tiền khách hàng", "Thu tiền", Icons.Filled.PriceCheck, AppPermissions.CollectionsSelf),
    // Người quản lý xử lý đơn đang chờ ngay trong ứng dụng.
    Approval("Đơn chờ duyệt", "Chờ duyệt", Icons.Filled.Inbox, AppPermissions.RequestsApprove),
    Penalty("Kỷ luật", "Kỷ luật", Icons.Filled.Gavel),
    People("Quản lý nhân sự", "Quản lý", Icons.Filled.People, AppPermissions.HrManage),
    Dashboard("Dashboard điều hành", "Dashboard", Icons.Filled.Dashboard, AppPermissions.HrRead),
    Payroll("Bảng lương", "Lương", Icons.Filled.Payments, AppPermissions.PayrollRead),
    Audit("Nhật ký hệ thống", "Nhật ký", Icons.Filled.History, AppPermissions.AuditRead),
    Settings("Cài đặt", "Cài đặt", Icons.Filled.Settings),
    Notifications("Thông báo", "Thông báo", Icons.Filled.Notifications),
}

private fun HrDestination.isAvailableTo(user: HrUser): Boolean =
    requiredPermission?.let(user::can) ?: true

data class TalentUiState(
    val loading:Boolean=false,
    val onboarding:OnboardingSummary?=null,
    val performance:PerformanceSummary?=null,
    val training:List<TrainingCourse> = emptyList(),
    val benefits:BenefitsSummary?=null,
    val error:String?=null,
)

/**
 * Nháp đơn "Khiếu nại án phạt" khởi tạo từ màn Kỷ luật khi nhân viên bấm đề nghị trên một quyết định phạt
 * tiền còn hiệu lực. Mang sẵn thông tin án phạt để form điền trước (mã, hình thức, số tiền hiện tại).
 */
data class AppealDraft(
    val penaltyNo: String,
    val penaltyTypeLabel: String,
    val amount: Double,
    val installments: Int,
)

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
    val payslipRequirement: PayslipRequirement = PayslipRequirement(),
)

/**
 * Khi app đã nhận được hạn từ máy chủ, vẫn chuyển sang khóa đúng 00:00 nếu mạng rớt đúng thời điểm đó.
 * Lớp chặn API chấm công vẫn là nguồn xác nhận cuối cùng khi có kết nối.
 */
internal fun payslipRequirementAt(
    requirement: PayslipRequirement,
    now: Instant = Instant.now(),
): PayslipRequirement {
    if (requirement.mustAcknowledge) return requirement
    val item = requirement.payslip ?: return requirement
    val dueAt = item.acknowledgementDueAt.takeIf(String::isNotBlank)?.let { raw ->
        runCatching { OffsetDateTime.parse(raw).toInstant() }
            .recoverCatching { Instant.parse(raw) }
            .getOrNull()
    } ?: return requirement
    if (now.isBefore(dueAt)) return requirement
    return requirement.copy(
        pendingCount = maxOf(1, requirement.pendingCount),
        overdueCount = maxOf(1, requirement.overdueCount),
        mustAcknowledge = true,
        payslip = item.copy(overdue = true),
    )
}

/**
 * Chọn đúng phiếu cho màn xác nhận. Requirement mới có id thì tuyệt đối không fallback theo kỳ vì
 * admin có thể đã xóa/phát hành lại cùng tháng; fallback period chỉ dành cho cache/API di sản thiếu id.
 */
internal fun findPayslipForConfirmation(
    items: List<PayslipItem>,
    targetId: String?,
    legacyPeriod: String?,
): PayslipItem? {
    if (!targetId.isNullOrBlank()) return items.firstOrNull { it.id == targetId }
    if (legacyPeriod.isNullOrBlank()) return null
    return items.firstOrNull { it.period == legacyPeriod && it.acknowledgedAt == null }
}

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
    val canDecide: Boolean = false,
    val deciding: Boolean = false,
    val decisionError: String? = null,
)

data class AuditUiState(
    val loading: Boolean = false,
    val loadingMore: Boolean = false,
    val items: List<AuditEntry> = emptyList(),
    val query: String = "",
    val entity: String = "",
    val error: String? = null,
    val hasMore: Boolean = true,
)

data class TimesheetUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val month: String = currentMonthKey(),
    val timesheet: Timesheet? = null,
    /** Bảng công các tháng kề bên (khóa "yyyy-MM") để lịch vuốt hiện luôn tháng liền kề. */
    val neighbors: Map<String, Timesheet> = emptyMap(),
)

/** Trạng thái màn "Giao việc": hộp việc của tôi + việc tôi giao, kèm quyền giao & tổng hợp huy hiệu. */
data class WorkTasksUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val canAssign: Boolean = false,
    val inbox: List<WorkTask> = emptyList(),
    val outbox: List<WorkTask> = emptyList(),
    /** Lệnh thu tiền chưa gộp được vào việc giao hàng nào. */
    val collections: List<TaskCollection> = emptyList(),
    val summary: WorkTaskSummary = WorkTaskSummary(),
    val meta: WorkTaskMeta? = null,
) {
    /** Số việc cần tôi để mắt: việc tôi phải làm + việc chờ tôi nghiệm thu + tiền còn phải thu. */
    val badge: Int get() =
        summary.inboxActionable + summary.outboxReview + summary.outboxAwaitingVoucher + summary.collectionsStandalone
}

/** Khoảng thời gian của màn Lịch sử công việc. */
enum class TaskHistoryRange(val label: String) { Week("Theo tuần"), Month("Theo tháng") }

/**
 * Trạng thái màn "Lịch sử công việc": việc ĐÃ HOÀN THÀNH trong tuần/tháng đang xem.
 * [anchor] là một ngày bất kỳ NẰM TRONG khoảng đang xem — lùi/tiến khoảng chỉ việc dịch ngày này.
 */
data class TaskHistoryUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val range: TaskHistoryRange = TaskHistoryRange.Month,
    val anchor: LocalDate = LocalDate.now(),
    /** null = xem tất cả mọi người mình được phép thấy. */
    val assignee: String? = null,
    val result: WorkTaskHistoryResult? = null,
) {
    val from: LocalDate get() = when (range) {
        TaskHistoryRange.Week -> anchor.with(java.time.DayOfWeek.MONDAY)
        TaskHistoryRange.Month -> anchor.withDayOfMonth(1)
    }
    val to: LocalDate get() = when (range) {
        TaskHistoryRange.Week -> from.plusDays(6)
        TaskHistoryRange.Month -> anchor.withDayOfMonth(anchor.lengthOfMonth())
    }
}

/** Trạng thái "nhật ký ngày" của ô ngày đang chọn trên lịch bảng công. */
data class DayLogUiState(
    val date: String? = null,
    val loading: Boolean = false,
    val error: String? = null,
    val data: DayLog? = null,
)

/** Trạng thái xem chi tiết một công việc (mở khi id != null). */
data class WorkTaskDetailUiState(
    val id: String? = null,
    val loading: Boolean = false,
    val error: String? = null,
    val detail: WorkTaskDetailResult? = null,
)

data class ManagerUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val summary: ManagerSummary? = null,
    val employees: List<EmployeeCard> = emptyList(),
    val departments: List<Department> = emptyList(),
    val jobPositions: List<JobPosition> = emptyList(),
)

/** Lương dự tính của chính nhân viên (tháng hiện tại). */
data class PayEstimateUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val data: PayEstimate? = null,
)

/** Danh sách phiếu lương đã nhận của chính nhân viên (mỗi kỳ một thẻ). */
data class PayslipsUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val items: List<PayslipItem> = emptyList(),
)

/**
 * Phiếu chi tiền mặt. Nhân viên thường chỉ thấy `mine`; kế toán thấy cả sổ và lập/duyệt được.
 * `cashier` do màn hình tính từ role + phòng ban — server vẫn là nơi chốt quyền thật.
 */
data class PayoutUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val items: List<PayoutVoucher> = emptyList(),
    val categories: List<PayoutCategory> = emptyList(),
    val refundSources: List<PayoutRefundSource> = emptyList(),
    val recipients: List<PayoutRecipient> = emptyList(),
    /** Phiếu đang mở mã QR để đưa người nhận quét (chỉ kế toán). */
    val qrVoucher: PayoutVoucher? = null,
    val busy: Boolean = false,
    val message: String? = null,
)

data class CashCollectionUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val message: String? = null,
    val items: List<CashCollection> = emptyList(),
    val drivers: List<CashCollectionDriver> = emptyList(),
    val customers: List<CashCollectionCustomer> = emptyList(),
    val busy: Boolean = false,
)

/** Cổng thông tin công ty (tin tức, sự kiện, giới thiệu). */
data class PortalUiState(
    val loading: Boolean = false,
    val error: String? = null,
    val feed: PortalFeed? = null,
)

/**
 * Các bước của luồng cập nhật ứng dụng. Trước đây chỉ có một cờ `installing` vẽ *bên trong* hộp thoại
 * nhắc — mà hộp thoại lại bị đóng ngay khi bấm "Cập nhật ngay", nên suốt vài phút tải gói ~90 MB người
 * dùng không thấy gì và tưởng app hỏng. Tách thành các bước rõ ràng để giao diện luôn nói được đang làm gì.
 */
sealed interface UpdateStage {
    /** Mới phát hiện bản mới, chưa tải gì. */
    data object Idle : UpdateStage
    /** Đang kiểm tra gói đã tải sẵn từ lần trước (đối chiếu SHA-256) để khỏi tải lại. */
    data object Preparing : UpdateStage
    data class Downloading(val downloaded: Long, val total: Long) : UpdateStage
    /** Đã có gói hợp lệ, đang mở màn xác nhận cài của hệ thống. */
    data object Installing : UpdateStage
    data class Failed(val message: String) : UpdateStage
}

/**
 * Nhóm thông báo hiện trên màn Cài đặt. Danh sách khoá + nhãn phải khớp `NotificationGroups.cs` ở
 * máy chủ; máy chủ mới là nơi chốt (không ghi thông báo, không bắn push cho nhóm đã tắt).
 */
val NOTIFICATION_GROUPS: List<Pair<String, String>> = listOf(
    "delivery" to "Giao hàng",
    "collection" to "Thu tiền",
    "accounting" to "Chứng từ & phiếu chi",
    "work" to "Việc được giao & đơn từ",
    "people" to "Nhân sự & chấm công",
)

data class SettingsUiState(
    val loading: Boolean = false,
    val webLoginEnabled: Boolean? = null,
    val pushNotificationsEnabled: Boolean? = null,
    /** null = chưa tải xong. Chưa từng đặt thì máy chủ trả về bật hết. */
    val notificationGroups: Map<String, Boolean>? = null,
    val savingNotificationGroup: String? = null,
    val devices: List<DeviceSession> = emptyList(),
    val devicesLoading: Boolean = false,
    val checkingUpdate: Boolean = false,
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
    data object Preparing : AttendanceCapture      // đang xin chuỗi màu flash trước khi mở camera
    data object Collecting : AttendanceCapture    // camera đang căn khung 2 bước + quét 3s soi sáng
    data object Recognizing : AttendanceCapture   // đã gửi loạt ảnh, máy chủ đang nhận diện (xem trước)
    // Đã nhận diện xong nhưng CHƯA ghi công — chờ người dùng bấm Xác nhận. Bình thường chỉ cần gửi
    // result.previewToken là server ghi công ngay; loạt khung vẫn giữ lại để lùi về cách cũ (gửi lại ảnh)
    // khi máy chủ chưa hỗ trợ token.
    data class AwaitingConfirm(
        val result: ChamCongResult,
        val frames: List<CapturedFrame>,
        val motionCheck: Boolean,
    ) : AttendanceCapture
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

/** Trạng thái chụp ảnh chân dung hồ sơ (có hướng dẫn đưa mặt vào khung, tự chụp). */
sealed interface PortraitCapture {
    data object Idle : PortraitCapture
    data object Capturing : PortraitCapture   // camera hướng dẫn chụp phủ toàn màn hình
    data object Saving : PortraitCapture       // đang tải ảnh lên máy chủ
    data class Done(val success: Boolean, val message: String) : PortraitCapture
}

/**
 * Trạng thái kết nối để báo cho người dùng khi mất mạng. [Online] = mọi thứ ổn (ẩn banner);
 * [NoInternet] = máy KHÔNG có mạng (wifi/di động tắt, máy bay…); [ServerUnreachable] = có mạng nhưng
 * KHÔNG chạm được máy chủ (server sập, tunnel chết, DNS lỗi). Phân biệt hai loại để hiển thị đúng lý do.
 */
enum class ConnectionStatus { Online, NoInternet, ServerUnreachable }

class HrViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = HrRepository.foreground(application)
    // Được thay bằng instance immutable theo username ngay khi xác thực; không dùng chung seen/items.
    private var notificationCenter = NotificationCenter(application, "signed-out")
    private val tokenStore = TokenStore(application)
    private val anniversaryStore = com.ketoanapk.hr.data.AnniversaryGreetingStore(application)
    // Business realtime dùng SSE durable.
    private val businessRealtime = SseRealtimeClient(tokenStore)

   // ── Trạng thái kết nối (báo mất mạng cho người dùng) ──────────────────────────────────
    /** Trạng thái kết nối hiện tại — UI hiện banner khi khác [ConnectionStatus.Online]. */
    var connection: ConnectionStatus by mutableStateOf(ConnectionStatus.Online)
        private set

    private val connectivityManager: ConnectivityManager? =
        application.getSystemService(ConnectivityManager::class.java)

    /** Máy có đường ra Internet không (không đảm bảo chạm được máy chủ). */
    private fun hasInternet(): Boolean {
        val cm = connectivityManager ?: return true // không đọc được → coi như có, tránh báo nhầm
        val net = cm.activeNetwork ?: return false
        val caps = cm.getNetworkCapabilities(net) ?: return false
        return caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
    }

    /** Cập nhật trạng thái kết nối trên luồng chính (callback mạng chạy ở luồng nền). */
    private fun postConnection(status: ConnectionStatus) {
        viewModelScope.launch { connection = status }
    }

    private val networkCallback = object : ConnectivityManager.NetworkCallback() {
        override fun onLost(network: Network) = postConnection(ConnectionStatus.NoInternet)
        override fun onUnavailable() = postConnection(ConnectionStatus.NoInternet)
        override fun onAvailable(network: Network) {
            // Có mạng lại → hỏi máy chủ NGAY để gỡ/đổi banner cho đúng, không phải chờ hết nhịp dò 10s.
            // Chỉ hỏi khi đang đăng nhập & đang có banner (khỏi ping thừa khi mọi thứ vẫn ổn).
            if (connection != ConnectionStatus.Online && authState is AuthState.SignedIn) {
                viewModelScope.launch { checkConnectionOnce() }
            }
        }
    }

    /** Hỏi máy chủ một nhịp (heartbeat) và cập nhật [connection]. Trả về trạng thái phiên để nơi gọi xử lý. */
    private suspend fun checkConnectionOnce(): SessionStatus {
        val status = repo.heartbeat()
        when (status) {
            is SessionStatus.Invalid -> {} // phiên bị thu hồi → để vòng heartbeat lo đăng xuất
            is SessionStatus.Ok -> connection = ConnectionStatus.Online
            // Không chạm được máy chủ: phân biệt "máy không có mạng" với "có mạng nhưng server im".
            is SessionStatus.Unknown ->
                connection = if (hasInternet()) ConnectionStatus.ServerUnreachable else ConnectionStatus.NoInternet
        }
        return status
    }

    init {
        // Đăng ký sau khi [connectivityManager] & [networkCallback] đã khởi tạo (khối init này đặt SAU các
        // khai báo đó). Theo dõi mạng của MÁY để báo NGAY khi mất Internet, không phải chờ nhịp tim 45s.
        runCatching { connectivityManager?.registerDefaultNetworkCallback(networkCallback) }
    }
    private var heartbeatJob: Job? = null
    private var timesheetLoadJob: Job? = null
    private val timesheetCache = mutableMapOf<String, Timesheet>()
    private val timesheetPrefetching = mutableSetOf<String>()
    // Vòng làm mới nhẹ khi app đang mở (foreground): tự cập nhật trạng thái đơn từ khi admin duyệt
    // trên web mà không cần người dùng kéo làm mới. Dừng khi app xuống nền để tiết kiệm pin
    // (nền đã có WorkManager + push FCM lo thông báo).
    private var foregroundPollJob: Job? = null
    private var directoryPresenceJob: Job? = null
    private var payslipRequirementJob: Job? = null
    private val payslipRequirementMutex = Mutex()
    private var payslipRequirementServerGeneration = 0L
    private var foregroundUpdateMonitorJob: Job? = null
    private var releaseUpdateDebounceJob: Job? = null
    private var pendingTarget: HrDestination? = null
    private var pendingEntityId: String? = null
    private var pendingNotificationId: String? = null
    private var pendingNotificationAccountScope: String? = null
    private var pushToken: String? = null
    private var pushRegistrationEpoch = 0L
    private var pushRegistrationAccount: String? = null
    private var pushRegistrationJob: Job? = null
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
    /**
     * Những màn đã đi qua, mới nhất ở cuối → Back lùi về đúng chỗ vừa rời thay vì nhảy thẳng Trang chủ.
     * Trang chủ là GỐC: mở Trang chủ sẽ xoá lịch sử, nhờ đó Back ở Trang chủ luôn thoát app đúng như
     * người dùng Android quen, không bị lùi ngược vào các màn cũ.
     */
    private val history = mutableStateListOf<HrDestination>()
    var homeState by mutableStateOf(HomeUiState(loading = true))
        private set
    // Thư tri ân "tròn X năm gắn bó" đang chờ hiện (null = không có/đã đóng). Xem [checkAnniversaryGreeting].
    var anniversaryGreeting: com.ketoanapk.hr.data.AnniversaryGreeting? by mutableStateOf(null)
        private set
    var anniversaryPreviewLoading by mutableStateOf(false)
        private set
    var profileDocuments: List<EmployeeDocument> by mutableStateOf(emptyList())
        private set
    var profileDocumentsLoading by mutableStateOf(false)
        private set
    var talentState by mutableStateOf(TalentUiState())
        private set
    var surveys:List<com.ketoanapk.hr.data.SurveyItem> by mutableStateOf(emptyList())
        private set
    var myFeedback:List<com.ketoanapk.hr.data.GeneralFeedbackItem> by mutableStateOf(emptyList())
        private set
    var diagnostics:Map<String,String> by mutableStateOf(emptyMap())
        private set
    var supportTickets:List<com.ketoanapk.hr.data.SupportTicketItem> by mutableStateOf(emptyList())
        private set
    var timesheetState by mutableStateOf(TimesheetUiState(loading = true))
        private set
    var payEstimateState by mutableStateOf(PayEstimateUiState())
        private set
    var payslipsState by mutableStateOf(PayslipsUiState())
        private set
    var payoutState by mutableStateOf(PayoutUiState())
        private set
    var cashCollectionState by mutableStateOf(CashCollectionUiState())
        private set
    // Kỳ (yyyy-MM) của phiếu lương đang mở chi tiết (null = đang xem danh sách thẻ tháng).
    var payslipOpenPeriod: String? by mutableStateOf(null)
        private set
    // Phiếu đang được đưa sang MÀN XÁC NHẬN RIÊNG. Khi app bị khóa, id từ requirement của máy chủ
    // luôn được ưu tiên; biến này chỉ dùng khi nhân viên chủ động xác nhận sớm từ kho phiếu.
    private var requestedPayslipConfirmationId: String? by mutableStateOf(null)
    var payslipAcknowledgingId: String? by mutableStateOf(null)
        private set
    var payslipConfirmationError: String? by mutableStateOf(null)
        private set
    var payslipConfirmationMessage: String? by mutableStateOf(null)
        private set
    private var acknowledgedPayslipAwaitingSyncId: String? by mutableStateOf(null)
    var portalState by mutableStateOf(PortalUiState())
        private set
    // Bài đang mở ở màn chi tiết cổng thông tin (null = đang xem danh sách).
    var portalDetail: PortalPost? by mutableStateOf(null)
        private set
    var requestDetailState by mutableStateOf(RequestDetailUiState())
        private set
    var auditState by mutableStateOf(AuditUiState())
        private set
    private var auditRequestId = 0
    var creatingRequest by mutableStateOf(false)
        private set
    var taskActionBusyId: String? by mutableStateOf(null)
        private set
    // ── Giao việc & nghiệm thu ──
    var workTasksState by mutableStateOf(WorkTasksUiState())
        private set
    var workTaskDetail by mutableStateOf(WorkTaskDetailUiState())
        private set
    var workTaskBusy by mutableStateOf(false)
        private set
    var taskHistoryState by mutableStateOf(TaskHistoryUiState())
        private set
    // Nhật ký của ô ngày đang chọn trên lịch bảng công.
    var dayLogState by mutableStateOf(DayLogUiState())
        private set
    var directoryState by mutableStateOf(DirectoryUiState())
        private set
    // Nháp khiếu nại mở thẳng từ màn Kỷ luật (bấm đề nghị trên một quyết định phạt). RequestsScreen đọc
    // để mở luôn form penalty_appeal đã điền sẵn án phạt; xoá đi sau khi rời luồng tạo đơn.
    var appealDraft: AppealDraft? by mutableStateOf(null)
        private set
    var requestDraftType: String? by mutableStateOf(null)
        private set
    var requestDraftValues: Map<String, String> by mutableStateOf(emptyMap())
        private set
    var requestDraftNonce: Long by mutableStateOf(0L)
        private set
    var requestDraftRestoreSaved: Boolean by mutableStateOf(true)
        private set
    val currentAccountId: String get() = (authState as? AuthState.SignedIn)?.user?.username.orEmpty()
    private var editingRequestId: String? = null
    var managerState by mutableStateOf(ManagerUiState())
        private set
    var managedEmployee: EmployeeDetail? by mutableStateOf(null)
        private set
    var managedEmployeeLoading by mutableStateOf(false)
        private set
    var dashboardAttendance:List<com.ketoanapk.hr.data.ManagerAttendanceItem> by mutableStateOf(emptyList())
        private set
    var dashboardStatus:String? by mutableStateOf(null)
        private set
    var dashboardDate by mutableStateOf(java.time.LocalDate.now().toString())
        private set
    var dashboardTrend by mutableStateOf<List<Pair<String, Int>>>(emptyList())
        private set
    var settingsState by mutableStateOf(SettingsUiState())
        private set
    // Dung lượng cache dễ đọc (vd "12 MB") cho màn "Bộ nhớ & dữ liệu tạm"; null = chưa đo xong.
    var cacheSizeText: String? by mutableStateOf(null)
        private set
    var cacheClearing by mutableStateOf(false)
        private set
    // Màn con đang mở trong tab Cài đặt. Đặt ở ViewModel để nút Back của điện thoại lùi về đúng cấp
    // (từ màn con → Cài đặt gốc) thay vì nhảy thẳng về Trang chủ.
    var settingsRoute by mutableStateOf(SettingsRoute.Home)
    // Lượt chấm hiện tại có yêu cầu QUAY ĐẦU không (đọc từ cấu hình server). Camera dùng để chạy pha
    // quay đầu; false = giữ khung tĩnh như cũ.
    var motionMode: Boolean by mutableStateOf(false)
        private set
    var smileMode: Boolean by mutableStateOf(false)
        private set
    var smileThreshold: Float by mutableStateOf(0.65f)
        private set
    var attendanceServer: AttendanceServerState by mutableStateOf(AttendanceServerState.Checking)
        private set
    var attendanceCapture: AttendanceCapture by mutableStateOf(AttendanceCapture.Idle)
        private set
    var attendancePending by mutableStateOf(0)   // số bản chấm ngoại tuyến đang chờ đồng bộ
        private set
    var attendanceQueued: List<OfflineAttendanceItem> by mutableStateOf(emptyList())
        private set
    var attendanceHistory: List<OfflineAttendanceRecord> by mutableStateOf(emptyList())
        private set
    var attendancePolicy: AttendancePolicy? by mutableStateOf(null)
        private set
    var attendanceLocation: android.location.Location? by mutableStateOf(null)
        private set
    // Đăng ký khuôn mặt: trạng thái đã đăng ký (null = chưa biết) + luồng quét đăng ký.
    var faceRegistered: Boolean? by mutableStateOf(null)
        private set
    var faceEnrollmentPending by mutableStateOf(false)
        private set
    var faceEnrollmentStatus: String? by mutableStateOf(null)
        private set
    var faceEnrollmentReviewNote: String? by mutableStateOf(null)
        private set
    var faceStatusLoading by mutableStateOf(false)
        private set
    var faceEnroll: FaceEnrollCapture by mutableStateOf(FaceEnrollCapture.Idle)
        private set
    var portraitCapture: PortraitCapture by mutableStateOf(PortraitCapture.Idle)
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
    val showFaceEnrollBanner: Boolean get() =
        faceRegistered == false && !faceEnrollmentPending && appConfig.faceEnrollBannerEnabled
    var rememberedUsername by mutableStateOf("")
        private set
    var actionMessage: String? by mutableStateOf(null)
        private set
    var pendingQrLoginCode: String? by mutableStateOf(null)
        private set
    var pendingMobileAppLoginCode: String? by mutableStateOf(null)
        private set
    var mobileAppLoginState: MobileAppLoginState by mutableStateOf(MobileAppLoginState.Idle)
        private set
    var notifications: List<AppNotification> by mutableStateOf(emptyList())
        private set

    // Cập nhật tự động (không cần vào Cài đặt): bản mới tìm thấy ngầm + thanh nhắc toàn ứng dụng.
    var availableUpdate: ReleaseInfo? by mutableStateOf(null)
        private set
    var updateSheetVisible by mutableStateOf(false)
        private set
    /** Bước hiện tại của luồng cập nhật — bảng cập nhật vẽ theo đúng bước này. */
    var updateStage: UpdateStage by mutableStateOf(UpdateStage.Idle)
        private set
    /** Đang ở mạng tính phí và gói khá lớn → bảng cập nhật hỏi lại trước khi tốn cước. */
    var updateNeedsMeteredConsent by mutableStateOf(false)
        private set
    private var lastSuccessfulUpdateCheckAt = 0L
    private var updateCheckJob: Job? = null
    private var pendingForcedUpdateCheck = false
    private var pendingUpdateOpenDetails = false
    private var pendingManualUpdateCheck = false
    private var updateCheckSession = 0L
    private var loggingOut = false
    private var notificationLoadJob: Job? = null

    val unreadCount: Int get() = notifications.count { !it.read }

    /** Số đơn đang chờ tôi duyệt (hộp thư đã lọc sẵn ở máy chủ theo người duyệt/quản trị). */
    val pendingApprovalCount: Int get() = homeState.inbox.count { it.status.equals("Pending", true) }
    val taskCenterItems: List<TaskCenterItem>
        get() = buildTaskCenterItems(homeState.inbox, homeState.timesheet, managerState.summary?.headcount)

    /**
     * Thanh dưới theo vai trò: nhân viên cần Bảng công + Đơn từ, quản trị cần Chờ duyệt + Quản lý
     * nhân sự. Ô cuối là "Cá nhân" (hồ sơ) — thay cho nút ảnh đại diện trên header đã bỏ.
     *
     * RÀNG BUỘC: đúng 5 mục và nút QR nổi LUÔN ở giữa (chỉ số 2) — nút tròn nổi được căn TopCenter nên
     * đổi thứ tự sẽ khiến nút lệch khỏi khe trống. Vị trí giữa (Scan) chỉ là chỗ trống giữ khe cho nút QR.
     */
    fun bottomDestinations(user: HrUser): List<HrDestination> = when {
        user.can(AppPermissions.HrManage) -> listOf(
            HrDestination.Home,
            HrDestination.Approval,
            HrDestination.Scan,
            HrDestination.People,
            HrDestination.Personal,
        )
        user.can(AppPermissions.RequestsApprove) -> listOf(
            HrDestination.Home,
            HrDestination.Approval,
            HrDestination.Scan,
            HrDestination.Requests,
            HrDestination.Personal,
        )
        else -> listOf(
            HrDestination.Home,
            HrDestination.Timesheet,
            HrDestination.Scan,
            HrDestination.Requests,
            HrDestination.Personal,
        )
    }

    /**
     * Tác vụ có thể ghim/sắp xếp trên Trang chủ. Dashboard điều hành đã chuyển hẳn sang web;
     * Khảo sát & phản hồi đi qua Trung tâm trợ giúp nên hai màn đó không xuất hiện trực tiếp ở đây.
     */
    fun homeActions(user: HrUser): List<HrDestination> = listOf(
        HrDestination.Scan,
        HrDestination.Requests,
        HrDestination.Tasks,
        HrDestination.Timesheet,
        HrDestination.Portal,
        HrDestination.Profile,
        HrDestination.MyPayslips,
        HrDestination.Payout,
        HrDestination.CashCollections,
        HrDestination.Penalty,
        HrDestination.Help,
        HrDestination.Onboarding,
        HrDestination.Performance,
        HrDestination.Training,
        HrDestination.Benefits,
        HrDestination.Approval,
        HrDestination.People,
        HrDestination.Payroll,
        HrDestination.Audit,
        HrDestination.Settings,
    ).filter { it.isAvailableTo(user) }

    /**
     * Màn con nào nằm trong màn "chứa" nào — thay cho `navGroups` của ngăn kéo hamburger đã bỏ.
     *
     * Nguyên tắc: mỗi màn con xuất hiện ở ĐÚNG MỘT chỗ. Nếu để nó vừa là tab vừa nằm trong màn chứa thì
     * người dùng gặp cùng một mục hai lần và ta lại quay về đúng cảnh hỗn độn của ngăn kéo cũ.
     * Nhóm cá nhân không nằm ở đây vì màn Cá nhân chia 3 cụm, xem PersonalHubScreen.
     */
    fun hubFor(destination: HrDestination): List<HrDestination> {
        val user = (authState as? AuthState.SignedIn)?.user ?: return emptyList()
        val destinations = when (destination) {
            // Người duyệt đã có tab "Chờ duyệt" ở thanh dưới nên không lặp lại nó ở đây.
            HrDestination.Requests ->
                if (user.can(AppPermissions.RequestsApprove)) listOf(HrDestination.Penalty)
                else listOf(HrDestination.Approval, HrDestination.Penalty)
            HrDestination.People -> listOf(
                HrDestination.Payroll, HrDestination.Audit,
            ).filter { it.isAvailableTo(user) }
            // Trang chủ dùng một khung Tác vụ có thể sắp xếp, không còn nhóm "Công ty" riêng.
            HrDestination.Home -> emptyList()
            else -> emptyList()
        }
        return destinations.filter { it.isAvailableTo(user) }
    }

    /** Số trên huy hiệu của một điểm đến (0 = không hiện). Dùng chung cho thanh dưới và các màn chứa. */
    fun badgeCount(destination: HrDestination): Int = when (destination) {
        HrDestination.Approval -> pendingApprovalCount
        HrDestination.Tasks -> taskCenterItems.size + workTasksState.badge
        HrDestination.CashCollections -> cashCollectionState.items.count {
            if (canReadAllCollections) it.status in setOf("PendingHandover", "Variance")
            else it.mine && it.status in setOf("Assigned", "Accepted", "PendingHandover", "Variance")
        }
        else -> 0
    }

    init {
        viewModelScope.launch { rememberedUsername = repo.rememberedUsername() }
        // FCM báo có dữ liệu đổi từ máy chủ → làm mới NGAY màn đang xem (đơn từ tức thì, không chờ poll).
        viewModelScope.launch {
            AppEvents.dataChanged.collect { changeScope ->
                if (authState is AuthState.SignedIn && !loggingOut) {
                    pollLiveData(changeScope)
                    if (changeScope == "release") schedulePublishedReleaseCheck()
                    // "all" do SSE resync phát: kiểm tra theo throttle để bắt bản đã lỡ ngoài retention.
                    if (changeScope == "all") autoCheckForUpdate(force = true)
                    if (changeScope == "config" || changeScope == "all") loadAppConfig(force = true)
                    if (changeScope == "access") refreshCurrentAccess()
                    if ((changeScope == "portal" || changeScope == "all") && selected == HrDestination.Portal)
                        loadPortal(silent = true)
                    if ((changeScope == "audit" || changeScope == "all") && selected == HrDestination.Audit)
                        loadAudit(reset = true)
                    val talentScreenOpen = selected == HrDestination.Onboarding ||
                        selected == HrDestination.Performance || selected == HrDestination.Training ||
                        selected == HrDestination.Benefits
                    if ((changeScope == "talent" || changeScope == "all") && talentScreenOpen)
                        loadTalent()
                }
            }
        }
        // Tài khoản đăng nhập ở thiết bị khác (1 máy/tài khoản) → server đẩy "kicked" → đăng xuất NGAY.
        viewModelScope.launch {
            AppEvents.forceLogout.collect { reason ->
                if (authState is AuthState.SignedIn) logout(reason)
            }
        }
        restoreSession()
    }

    fun consumeActionMessage() { actionMessage = null }

    /** Báo một dòng ngắn lên snackbar từ màn hình. */
    fun showActionMessage(text: String) { actionMessage = text }

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

    /** Bước 2 màn quên mật khẩu: kiểm tra mã khôi phục trước khi cho đặt mật khẩu mới. */
    fun verifyRecoveryCode(
        username: String,
        code: String,
        onDone: (Boolean, String?) -> Unit,
    ) {
        if (username.isBlank()) {
            onDone(false, "Vui lòng nhập tên đăng nhập.")
            return
        }
        if (code.isBlank()) {
            onDone(false, "Vui lòng nhập mã khôi phục.")
            return
        }
        viewModelScope.launch {
            runCatching { repo.verifyRecoveryCode(username, code) }
                .onSuccess { onDone(true, null) }
                .onFailure { onDone(false, readable(it)) }
        }
    }

    /** Quên mật khẩu bằng MÃ KHÔI PHỤC do admin cấp (thay cho reset khuôn mặt đã tắt ở backend). */
    fun resetPasswordWithCode(
        username: String,
        code: String,
        newPassword: String,
        onDone: (Boolean, String?) -> Unit,
    ) {
        if (username.isBlank()) {
            onDone(false, "Vui lòng nhập tên đăng nhập.")
            return
        }
        if (code.isBlank()) {
            onDone(false, "Vui lòng nhập mã khôi phục.")
            return
        }
        if (newPassword.length < 6) {
            onDone(false, "Mật khẩu mới cần ít nhất 6 ký tự.")
            return
        }
        viewModelScope.launch {
            resetPasswordLoading = true
            runCatching { repo.resetPasswordWithCode(username, code, newPassword) }
                .onSuccess {
                    rememberedUsername = username.trim()
                    onDone(true, null)
                }
                .onFailure { onDone(false, readable(it)) }
            resetPasswordLoading = false
        }
    }

    private fun onSignedIn(user: HrUser) {
        beginPushRegistrationSession(user.username)
        authState = AuthState.SignedIn(user)
        activateNotificationAccount(user.username)
        resetToHome()
        startHeartbeat()
        startForegroundPoll() // tự làm mới danh sách/chi tiết đơn khi app đang mở
        startPayslipRequirementMonitor()
        startForegroundUpdateMonitor()
        businessRealtime.start(user.username)
        loadNotifications()
        syncPushDelivery()
        registerPush()
        refreshHome(user, silent = false)
        loadTimesheet(currentMonthKey(), silent = false)
        loadWorkTasks(silent = true) // để huy hiệu "Giao việc" trên Trang chủ có số ngay
        if (user.can(AppPermissions.HrRead)) refreshManager(silent = true)
        faceRegistered = user.faceRegistered // cờ đi kèm dữ liệu đăng nhập → không cần gọi API riêng
        faceEnrollmentPending = user.faceEnrollmentPending
        faceEnrollmentStatus = when {
            user.faceRegistered -> "registered"
            user.faceEnrollmentPending -> "pending"
            else -> "not_enrolled"
        }
        loadAppConfig(force = true)
        consumePendingTarget(user)
        autoCheckForUpdate(force = true)
        checkAnniversaryGreeting(user)
    }

    /**
     * Hỏi server xem hôm nay có phải tuần kỷ niệm tròn năm gắn bó của người dùng không. Nếu có và mốc đó
     * CHƯA từng bật popup trên máy này (lưu cục bộ), dựng thư để [HrShell] hiện overlay gõ chữ.
     * Best-effort: lỗi mạng thì thôi, không làm phiền và sẽ thử lại ở lần mở app sau.
     */
    private fun checkAnniversaryGreeting(user: HrUser) {
        viewModelScope.launch {
            val greeting = runCatching { repo.anniversaryGreeting() }.getOrNull() ?: return@launch
            val activeUser = (authState as? AuthState.SignedIn)?.user?.username
            if (!activeUser.equals(user.username, ignoreCase = true)) return@launch
            if (!greeting.show || greeting.key.isBlank()) return@launch
            if (anniversaryStore.wasSeen(user.username, greeting.key)) return@launch
            anniversaryGreeting = greeting
        }
    }

    /** Quản lý chủ động mở bản mẫu 5 năm để kiểm tra giao diện, không cần sửa ngày vào làm thật. */
    fun previewAnniversaryGreeting() {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        if (anniversaryPreviewLoading) return
        viewModelScope.launch {
            anniversaryPreviewLoading = true
            runCatching { repo.anniversaryGreeting(preview = true) }
                .onSuccess { greeting ->
                    val activeUser = (authState as? AuthState.SignedIn)?.user?.username
                    if (activeUser.equals(user.username, ignoreCase = true) && greeting.show) {
                        anniversaryGreeting = greeting
                    }
                }
                .onFailure { actionMessage = readable(it) }
            anniversaryPreviewLoading = false
        }
    }

    /** Đóng thư tri ân và ghi nhớ đã xem để mốc này không bật lại trong suốt tuần kỷ niệm. */
    fun dismissAnniversaryGreeting() {
        val greeting = anniversaryGreeting ?: return
        if (!greeting.preview) {
            (authState as? AuthState.SignedIn)?.let { anniversaryStore.markSeen(it.user.username, greeting.key) }
        }
        anniversaryGreeting = null
    }

    private fun syncPushDelivery() {
        viewModelScope.launch {
            val enabled = repo.pushNotificationsEnabled()
            val permissionGranted = hasNotificationPermission()
            // `enabled` là lựa chọn bền của người dùng; quyền Android chỉ là cổng giao hiện tại. Không
            // ghi đè lựa chọn thành false khi quyền bị thu hồi, nếu không cấp lại quyền từ Settings sẽ
            // không thể giao các reminder đã phát hiện trong khoảng bị chặn.
            settingsState = settingsState.copy(pushNotificationsEnabled = enabled)
            if (enabled) {
                startPushDelivery()
                if (permissionGranted) deliverPendingAttendanceNotifications()
            } else {
                // Tắt thông báo NGHIỆP VỤ nhưng GIỮ token đã đăng ký để vẫn nhận được CUỘC GỌI.
                stopPushDelivery(unregister = false)
            }
        }
    }

    fun loadPushNotificationSetting() {
        syncPushDelivery()
        loadNotificationGroups()
    }

    fun loadNotificationGroups() {
        viewModelScope.launch {
            val groups = repo.notificationGroups()
            // Không đọc được (mất mạng) thì coi như BẬT hết — đó cũng là mặc định của máy chủ; hiện
            // "tắt hết" sẽ khiến người dùng tưởng mình đã tắt và không hiểu vì sao vẫn có thông báo.
            settingsState = settingsState.copy(
                notificationGroups = groups.ifEmpty { NOTIFICATION_GROUPS.associate { it.first to true } },
            )
        }
    }

    fun setNotificationGroup(group: String, enabled: Boolean) {
        val before = settingsState.notificationGroups ?: return
        settingsState = settingsState.copy(
            notificationGroups = before + (group to enabled),
            savingNotificationGroup = group,
        )
        viewModelScope.launch {
            val saved = repo.setNotificationGroup(group, enabled)
            settingsState = if (saved.isEmpty()) {
                actionMessage = "Không lưu được tuỳ chọn thông báo. Vui lòng thử lại."
                settingsState.copy(notificationGroups = before, savingNotificationGroup = null)
            } else {
                settingsState.copy(notificationGroups = saved, savingNotificationGroup = null)
            }
        }
    }

    fun refreshPushPermissionState() {
        syncPushDelivery()
    }

    fun setPushNotificationsEnabled(enabled: Boolean) {
        settingsState = settingsState.copy(pushNotificationsEnabled = enabled)
        viewModelScope.launch {
            repo.setPushNotificationsEnabled(enabled)
            if (enabled) {
                // Vẫn nhận/sync dữ liệu khi quyền hệ thống đang chặn để có thể backfill sau khi cấp lại.
                startPushDelivery()
                if (hasNotificationPermission()) {
                    deliverPendingAttendanceNotifications()
                    actionMessage = "Đã bật thông báo push của ứng dụng."
                } else {
                    actionMessage = "Đã ghi nhớ lựa chọn bật. Hãy cấp quyền thông báo để nhận nhắc nhở."
                }
            } else {
                stopPushDelivery(unregister = false) // giữ token để vẫn nhận cuộc gọi
                actionMessage = "Đã tắt thông báo push của ứng dụng."
            }
        }
    }

    fun onNotificationPermissionDenied() {
        // Người dùng vừa chủ động bật nhưng Android chưa cho phép: giữ ý định bật để khi họ cấp quyền
        // trong Settings, onResume có thể giao ngay reminder còn tồn thay vì bắt bật công tắc lần nữa.
        settingsState = settingsState.copy(pushNotificationsEnabled = true)
        viewModelScope.launch {
            repo.setPushNotificationsEnabled(true)
            startPushDelivery()
            actionMessage = "Chưa có quyền hệ thống. App sẽ giao các nhắc nhở còn tồn sau khi bạn cấp quyền."
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
        val account = (authState as? AuthState.SignedIn)?.user?.username?.trim().orEmpty()
        val epoch = pushRegistrationEpoch
        if (!isPushRegistrationCurrent(epoch, account)) return
        runCatching {
            FirebaseMessaging.getInstance().token.addOnSuccessListener { token ->
                // Firebase Task không hủy được sau khi phát lệnh. Callback phải mang epoch + username
                // tại lúc tạo để kết quả của A không thể dùng JWT của B sau account switch.
                if (token.isNullOrBlank() || !isPushRegistrationCurrent(epoch, account)) return@addOnSuccessListener
                pushToken = token
                pushRegistrationJob?.cancel()
                pushRegistrationJob = viewModelScope.launch {
                    // Kiểm lại ở trong coroutine: logout có thể xảy ra giữa callback và lúc job được chạy.
                    if (!isPushRegistrationCurrent(epoch, account)) return@launch
                    repo.registerPushToken(token)
                }
            }
        }
    }

    /** Mở một ranh giới sở hữu token mới trước khi dựng trạng thái tài khoản. */
    private fun beginPushRegistrationSession(username: String) {
        pushRegistrationEpoch += 1
        pushRegistrationAccount = username.trim()
        pushRegistrationJob?.cancel()
        pushRegistrationJob = null
        pushToken = null
    }

    /** Vô hiệu callback cũ đồng bộ; trả token/job để logout unregister sau khi job A đã dừng hẳn. */
    private fun endPushRegistrationSession(): Pair<String?, Job?> {
        val token = pushToken
        val registrationJob = pushRegistrationJob
        pushRegistrationEpoch += 1
        pushRegistrationAccount = null
        registrationJob?.cancel()
        pushRegistrationJob = null
        pushToken = null
        return token to registrationJob
    }

    private fun isPushRegistrationCurrent(epoch: Long, account: String): Boolean {
        if (account.isBlank() || pushRegistrationEpoch != epoch) return false
        if (!pushRegistrationAccount.equals(account, ignoreCase = true)) return false
        val activeAccount = (authState as? AuthState.SignedIn)?.user?.username
        return activeAccount.equals(account, ignoreCase = true)
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

    /**
     * Đăng xuất. [kickedMessage] != null nghĩa là bị BUỘC đăng xuất (đăng nhập ở máy khác / phiên hết
     * hạn) — hiện lý do ở màn đăng nhập. Bỏ qua nếu đã đăng xuất rồi (chống gọi trùng từ heartbeat + SSE).
     */
    fun logout(kickedMessage: String? = null) {
        if (authState is AuthState.SignedOut || loggingOut) return
        val accountAtLogout = (authState as? AuthState.SignedIn)?.user?.username.orEmpty()
        loggingOut = true
        // Chốt token + vô hiệu callback Firebase trước mọi suspend. Token đã chốt được unregister ở
        // dưới trong khi JWT của tài khoản cũ vẫn còn; callback đến muộn chỉ thấy epoch đã đổi và no-op.
        val (pushTokenAtLogout, pushRegistrationAtLogout) = endPushRegistrationSession()
        // Vô hiệu hóa request phiên bản ngay trước mọi suspend của luồng logout để response cũ không
        // kịp ghi lại settings/notification của tài khoản vừa rời.
        updateCheckSession++
        updateCheckJob?.cancel()
        updateCheckJob = null
        releaseUpdateDebounceJob?.cancel()
        releaseUpdateDebounceJob = null
        val pendingNotificationLoad = notificationLoadJob
        pendingNotificationLoad?.cancel()
        notificationLoadJob = null
        pendingForcedUpdateCheck = false
        pendingUpdateOpenDetails = false
        pendingManualUpdateCheck = false
        pendingTarget = null
        pendingEntityId = null
        pendingNotificationId = null
        pendingNotificationAccountScope = null
        lastSuccessfulUpdateCheckAt = 0L
        val centerAtLogout = notificationCenter
        viewModelScope.launch {
            try {
                // Đợi tác vụ đọc kho chuông đã hủy kết thúc trước khi reset để nó không thể ghi dữ liệu cũ trở lại.
                pendingNotificationLoad?.join()
                // Chờ request register đã cancel kết thúc rồi mới unregister để A không thể ghi đè ngược
                // thứ tự trên server sau lệnh xóa token.
                pushRegistrationAtLogout?.join()
                pushTokenAtLogout?.let { runCatching { repo.unregisterPushToken(it) } }
                repo.logout()
                stopHeartbeat()
                onAppPaused() // dừng vòng poll foreground
                if (accountAtLogout.isNotBlank()) businessRealtime.clearCursor(accountAtLogout)
                NotificationWorker.cancel(getApplication<Application>())
                centerAtLogout.reset() // đồng thời gỡ toàn bộ notification khay thuộc tài khoản này
                notificationCenter = NotificationCenter(getApplication<Application>(), "signed-out")
                notifications = emptyList()
                authState = AuthState.SignedOut
                resetToHome()
                homeState = HomeUiState()
                timesheetLoadJob?.cancel()
                timesheetLoadJob = null
                timesheetCache.clear()
                timesheetPrefetching.clear()
                timesheetState = TimesheetUiState()
                payEstimateState = PayEstimateUiState()
                payslipsState = PayslipsUiState()
                payslipOpenPeriod = null
                requestedPayslipConfirmationId = null
                payslipAcknowledgingId = null
                payslipConfirmationError = null
                payslipConfirmationMessage = null
                acknowledgedPayslipAwaitingSyncId = null
                // Sổ chi có tên + số tiền của người khác: phải xóa sạch khi đăng xuất.
                payoutState = PayoutUiState()
                cashCollectionState = CashCollectionUiState()
                managerState = ManagerUiState()
                settingsState = SettingsUiState()
                attendanceServer = AttendanceServerState.Checking
                attendanceCapture = AttendanceCapture.Idle
                faceRegistered = null
                faceEnrollmentPending = false
                faceEnrollmentStatus = null
                faceEnrollmentReviewNote = null
                faceEnroll = FaceEnrollCapture.Idle
                openFaceEnroll = false
                appConfig = AppConfig()
                lastConfigFetchAt = 0L
                availableUpdate = null
                updateSheetVisible = false
                updateStage = UpdateStage.Idle
                updateNeedsMeteredConsent = false
                loginError = kickedMessage // hiện lý do bị đá (nếu có) trên màn đăng nhập
            } finally {
                loggingOut = false
            }
        }
    }

    // ── Thông báo (chuông) ──────────────────────────────────────────────────────
    private fun loadNotifications() {
        if (loggingOut) return
        val account = (authState as? AuthState.SignedIn)?.user?.username ?: return
        val session = updateCheckSession
        val center = notificationCenter
        notificationLoadJob?.cancel()
        notificationLoadJob = viewModelScope.launch {
            val installed = AppUpdater.installedVersionCode(getApplication())
            val loaded = center.load(installed)
            val activeAccount = (authState as? AuthState.SignedIn)?.user?.username
            if (session == updateCheckSession && !loggingOut && activeAccount.equals(account, ignoreCase = true) &&
                notificationCenter.accountScope == center.accountScope
            ) {
                notifications = loaded
            }
        }
    }

    private fun activateNotificationAccount(username: String) {
        val expected = notificationAccountScope(username)
        if (notificationCenter.accountScope != expected) {
            notificationLoadJob?.cancel()
            notificationLoadJob = null
            notifications = emptyList()
            notificationCenter = NotificationCenter(getApplication<Application>(), username)
        }
    }

   private fun syncNotifications(user: HrUser, state: HomeUiState) {
        val center = notificationCenter
        viewModelScope.launch {
            val nowVietnam = com.ketoanapk.hr.data.ServerClock.nowVietnam()
            val monthKeys = missedCheckoutMonthKeys(nowVietnam.toLocalDate())
            val cachedByMonth = listOfNotNull(state.timesheet).associateBy { it.period.take(7) }
            val attendanceSheets = monthKeys.mapNotNull { month ->
                cachedByMonth[month] ?: runCatching { repo.myTimesheet(month) }.getOrNull()
            }
            val activeAccount = (authState as? AuthState.SignedIn)?.user?.username
            if (!activeAccount.equals(user.username, ignoreCase = true) || center.accountScope != notificationCenter.accountScope)
                return@launch
            val fresh = center.sync(
                state.requests,
                state.inbox,
                state.penalties,
                user.can(AppPermissions.PenaltyManage),
                attendanceSheets = attendanceSheets,
                nowVietnam = nowVietnam,
            ) +
                // Hộp thư trên máy chủ: nguồn của mọi thông báo nghiệp vụ dùng chung với web (giao
                // hàng, thu tiền, chứng từ…). Trộn ở đây nên app không cần biết từng nghiệp vụ một.
                center.ingestFromServer(repo.notificationFeed())
            if (repo.pushNotificationsEnabled() && hasNotificationPermission()) {
                val delivered = fresh.filter { it.kind != com.ketoanapk.hr.data.NotificationKind.Attendance }.filter {
                    AppNotifier.show(getApplication<Application>(), it, user.username)
                }.map { it.id }
                center.markSystemDelivered(delivered)
                notifications = center.deliverPendingSystemAttendance()
            } else {
                notifications = center.current
            }
        }
    }

    /** Cấp quyền sau lúc phát hiện vẫn đưa các nhắc công chưa giao lên khay đúng một lần. */
    private fun deliverPendingAttendanceNotifications() {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        val center = notificationCenter
        viewModelScope.launch {
            if (!repo.pushNotificationsEnabled() || !hasNotificationPermission()) return@launch
            if (notificationCenter.accountScope == center.accountScope) {
                notifications = center.deliverPendingSystemAttendance()
            }
        }
    }

    fun openNotifications() { goTo(HrDestination.Notifications) }

   fun markNotificationRead(id: String) {
        val center = notificationCenter
        // Tra serverId TRƯỚC khi đánh dấu: sau khi markRead chạy, dòng vẫn còn nhưng lấy từ danh sách
        // đang hiển thị là đủ và rẻ hơn một vòng đọc DataStore nữa.
        val serverId = notifications.firstOrNull { it.id == id }?.serverId
        viewModelScope.launch {
            val updated = center.markRead(id)
            if (center.accountScope == notificationCenter.accountScope) notifications = updated
            // Đọc trên app thì chuông trên web cũng phải hết đỏ — cùng một hộp thư.
            serverId?.let { repo.markServerNotificationRead(it) }
        }
    }

    fun markAllNotificationsRead() {
        val center = notificationCenter
        viewModelScope.launch {
            val updated = center.markAllRead()
            if (center.accountScope == notificationCenter.accountScope) notifications = updated
            repo.markAllServerNotificationsRead()
        }
    }

    fun clearNotifications() {
        val center = notificationCenter
        viewModelScope.launch {
            val updated = center.clearAll()
            if (center.accountScope == notificationCenter.accountScope) notifications = updated
        }
    }

    /** Điều hướng theo thông báo hệ thống (deep-link) khi mở app từ khay thông báo. */
    fun navigateTo(
        target: String?,
        entityId: String? = null,
        notificationId: String? = null,
        accountScope: String? = null,
    ) {
        // Thông báo "bản cập nhật mới" → kiểm tra lại ngay và mở bảng cập nhật (không phải điều hướng).
        // Bỏ qua mốc hoãn: người dùng vừa chủ động bấm vào thông báo thì đương nhiên muốn xem bản mới.
        val user = (authState as? AuthState.SignedIn)?.user
        if (target == UPDATE_TARGET) {
            if (user != null && !accountScope.isNullOrBlank() && accountScope != notificationAccountScope(user.username)) {
                actionMessage = "Thông báo này thuộc một tài khoản khác và đã được bỏ qua."
                return
            }
            if (user != null) notificationId?.let(::markNotificationRead)
            autoCheckForUpdate(force = true, openDetails = true)
            return
        }
        // "WorkTasks" là tên màn CŨ (trước khi gộp vào "Việc cần làm") — thông báo do máy chủ bản cũ
        // gửi vẫn còn mang tên này nên phải quy về màn mới, nếu không bấm vào sẽ không đi đâu cả.
        val name = if (target == "WorkTasks") HrDestination.Tasks.name else target
        val dest = name?.let { runCatching { HrDestination.valueOf(it) }.getOrNull() } ?: return
        if (user == null) {
            keepPendingNotification(dest, entityId, notificationId, accountScope)
            return
        }
        if (!accountScope.isNullOrBlank() && accountScope != notificationAccountScope(user.username)) {
            actionMessage = "Thông báo này thuộc một tài khoản khác và đã được bỏ qua."
            return
        }
        if (payslipAccessLocked) {
            keepPendingNotification(dest, entityId, notificationId, accountScope)
            openRequiredPayslip(showMessage = true)
            return
        }
        if (!dest.isAvailableTo(user)) return
        notificationId?.let(::markNotificationRead)
        select(dest)
        openNotificationEntity(dest, entityId)
    }

    private fun keepPendingNotification(
        destination: HrDestination,
        entityId: String?,
        notificationId: String?,
        accountScope: String?,
    ) {
        pendingTarget = destination
        pendingEntityId = entityId
        pendingNotificationId = notificationId
        pendingNotificationAccountScope = accountScope
    }

    private fun consumePendingTarget(user: HrUser) {
        val dest = pendingTarget ?: return
        val entityId = pendingEntityId
        val notificationId = pendingNotificationId
        val requiredScope = pendingNotificationAccountScope
        if (!requiredScope.isNullOrBlank() && requiredScope != notificationAccountScope(user.username)) {
            clearPendingNotification()
            actionMessage = "Thông báo này thuộc một tài khoản khác và đã được bỏ qua."
            return
        }
        if (!dest.isAvailableTo(user)) { clearPendingNotification(); return }
        if (payslipAccessLocked) {
            openRequiredPayslip(showMessage = true)
            return
        }
        clearPendingNotification()
        notificationId?.let(::markNotificationRead)
        goTo(dest)
        openNotificationEntity(dest, entityId)
    }

    private fun clearPendingNotification() {
        pendingTarget = null
        pendingEntityId = null
        pendingNotificationId = null
        pendingNotificationAccountScope = null
    }

    private fun openNotificationEntity(destination: HrDestination, entityId: String?) {
        val id = entityId?.takeIf { it.isNotBlank() } ?: return
        when (destination) {
            HrDestination.Requests -> {
                val missedDate = com.ketoanapk.hr.data.missedCheckoutDateFromEntityId(id)
                if (missedDate != null) startForgotCheckinDraft(missedDate, direction = "out")
                else openRequestDetail(id)
            }
            HrDestination.Approval -> openStaffDetail(id)
            else -> Unit
        }
    }

   /**
     * Đổi màn và ghi màn cũ vào lịch sử. MỌI chỗ đổi `selected` phải đi qua đây, nếu không Back sẽ
     * bỏ sót màn đó. Chặn lịch sử ở 20 để bấm qua lại nhiều lần không tích thành hàng dài vô tận.
     */
    private fun goTo(destination: HrDestination) {
        if (payslipAccessLocked) {
            openRequiredPayslip(showMessage = true)
            return
        }
        if (destination == selected) return
        if (destination == HrDestination.Home) {
            history.clear()
        } else {
            history.add(selected)
            if (history.size > 20) history.removeAt(0)
        }
        selected = destination
        if (destination == HrDestination.MyPayslips && payslipsState.items.isEmpty() && !payslipsState.loading)
            loadMyPayslips()
    }

    /** Về Trang chủ với lịch sử sạch (dùng khi đăng nhập/đăng xuất). */
    private fun resetToHome() {
        history.clear()
        selected = HrDestination.Home
    }

    // ── Tìm kiếm toàn cục ───────────────────────────────────────────────
    // Lối tắt tới bất kỳ màn nào trong ~27 màn mà không cần nhớ nó nằm trong màn chứa nào. Đây là thứ
    // thay cho việc dò danh sách phẳng của ngăn kéo cũ.
    var searchOpen by mutableStateOf(false)
        private set
    var searchQuery by mutableStateOf("")
        private set

    fun openSearch() { searchOpen = true; searchQuery = "" }
    fun closeSearch() { searchOpen = false; searchQuery = "" }
    fun typeSearch(value: String) { searchQuery = value.take(60) }

    /** Các màn khớp từ khoá. Rỗng khi chưa gõ gì. Ẩn màn mà hồ sơ quyền server không cho phép. */
    fun searchResults(user: HrUser): List<HrDestination> {
        val q = searchKey(searchQuery.trim())
        if (q.isBlank()) return emptyList()
        return HrDestination.entries.filter {
            it !in setOf(HrDestination.Dashboard, HrDestination.Feedback) &&
                it.isAvailableTo(user) && searchKey(it.title).contains(q)
        }
    }

    /**
     * Chuẩn hoá chuỗi để so khớp: bỏ dấu tiếng Việt + đ→d + thường hoá. Nhờ vậy gõ "phuc loi" không dấu
     * (kiểu gõ thường gặp trên điện thoại) vẫn ra "Phúc lợi".
     */
    private fun searchKey(s: String): String =
        java.text.Normalizer.normalize(s, java.text.Normalizer.Form.NFD)
            .replace(Regex("\\p{Mn}+"), "")
            .replace('đ', 'd')
            .replace('Đ', 'D')
            .lowercase()

   val payslipAccessLocked: Boolean get() = homeState.payslipRequirement.mustAcknowledge

    /** Id phiếu mà màn xác nhận độc lập đang xử lý; phiếu quá hạn của server luôn thắng lựa chọn tay. */
    val payslipConfirmationId: String?
        get() {
            val required = homeState.payslipRequirement.payslip
            if (payslipAccessLocked) {
                return required?.id?.takeIf(String::isNotBlank)
                    ?: required?.period?.let { period ->
                        payslipsState.items.firstOrNull { it.period == period && it.acknowledgedAt == null }?.id
                    }
            }
            return requestedPayslipConfirmationId
        }

    val payslipConfirmationVisible: Boolean
        get() = payslipAccessLocked || requestedPayslipConfirmationId != null

    /** Chỉ ghép theo period cho dữ liệu requirement cũ không có id; id mới tuyệt đối không fallback. */
    val payslipConfirmationItem: PayslipItem?
        get() {
            val legacyPeriod = homeState.payslipRequirement.payslip?.period.takeIf { payslipAccessLocked }
            val item = findPayslipForConfirmation(payslipsState.items, payslipConfirmationId, legacyPeriod)
            val requirement = homeState.payslipRequirement.payslip
            return item?.takeIf {
                !payslipAccessLocked || requirement == null || when {
                    requirement.revisionToken.isNotBlank() -> it.revisionToken == requirement.revisionToken
                    requirement.updatedAt.isNotBlank() -> it.updatedAt == requirement.updatedAt
                    else -> true
                }
            }
        }

    /** Key của một lượt xem: đổi khi nội dung phiếu đổi để bắt nhập PIN và tích xác nhận lại. */
    val payslipConfirmationReviewKey: String
        get() {
            val required = homeState.payslipRequirement.payslip
            if (payslipAccessLocked && required != null) {
                val version = required.revisionToken.ifBlank {
                    required.updatedAt.ifBlank { required.publishedAt }
                }
                // Dùng kỳ làm identity để cache requirement cũ thiếu id không khiến PIN bật hai lần sau khi tải list.
                return "required:${required.period}:$version"
            }
            val item = payslipConfirmationItem
            val version = item?.let { it.revisionToken.ifBlank { it.updatedAt } }.orEmpty()
            return "voluntary:${requestedPayslipConfirmationId.orEmpty()}:$version"
        }

    val payslipConfirmationRemainingCount: Int
        get() = if (payslipAccessLocked) homeState.payslipRequirement.overdueCount.coerceAtLeast(1) else 1

    val payslipConfirmationAwaitingSync: Boolean
        get() = acknowledgedPayslipAwaitingSyncId != null

    val payslipConfirmationPeriod: String
        get() = if (payslipAccessLocked) {
            homeState.payslipRequirement.payslip?.period.orEmpty()
        } else {
            payslipConfirmationItem?.period.orEmpty()
        }

    val payslipConfirmationDueAt: String
        get() = if (payslipAccessLocked) {
            homeState.payslipRequirement.payslip?.acknowledgementDueAt.orEmpty()
        } else {
            payslipConfirmationItem?.acknowledgementDueAt.orEmpty()
        }

    val canGoBack: Boolean get() = !payslipAccessLocked && (history.isNotEmpty() || selected != HrDestination.Home)

    /** Back cấp màn hình: lùi về màn trước, hết lịch sử thì về Trang chủ. Ở Trang chủ thì không làm gì. */
    fun goBack() {
        if (payslipAccessLocked) {
            openRequiredPayslip(showMessage = true)
            return
        }
        val target = when {
            history.isNotEmpty() -> history.removeAt(history.lastIndex)
            selected != HrDestination.Home -> HrDestination.Home
            else -> return
        }
        selected = target
        (authState as? AuthState.SignedIn)?.user?.let { enterDestination(target, it) }
    }

    fun select(destination: HrDestination) {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        if (!destination.isAvailableTo(user)) return
        if (payslipAccessLocked && destination != HrDestination.MyPayslips) {
            openRequiredPayslip(showMessage = true)
            return
        }
        goTo(destination)
        enterDestination(destination, user)
    }

    /**
     * Việc phải làm mỗi khi BƯỚC VÀO một màn — dù đi tới (select) hay lùi lại (goBack): dọn chi tiết đang
     * mở để vào ở trạng thái sạch, rồi nạp dữ liệu nếu chưa có. Lùi lại cũng là bước vào, nên nếu bỏ qua
     * hàm này thì Back sẽ trả về màn ở trạng thái cũ (ví dụ Cài đặt hiện màn con thay vì màn gốc).
     */
    private fun enterDestination(destination: HrDestination, user: HrUser) {
        // Rời Danh bạ thì dừng vòng làm mới hiện diện; chỉ màn đó hiển thị chip Online.
        if (destination != HrDestination.Directory) {
            directoryPresenceJob?.cancel()
            directoryPresenceJob = null
        }
        closeRequestDetail() // đóng chi tiết đơn đang mở (nếu có) khi chuyển màn để vào trạng thái sạch
        portalDetail = null  // luôn vào cổng thông tin ở danh sách, không giữ chi tiết cũ
        payslipOpenPeriod = null // luôn vào Phiếu lương ở danh sách thẻ tháng, không giữ chi tiết cũ
        when (destination) {
            HrDestination.People -> if (managerState.summary == null) refreshManager(silent = false)
            HrDestination.Dashboard -> refreshDashboard(null,null)
            HrDestination.Feedback -> loadFeedback()
            HrDestination.Help -> runDiagnostics()
            HrDestination.Scan -> { resetCapture(); checkAttendanceServer(); refreshAttendanceContext() }
            HrDestination.Profile -> loadProfileDocuments()
            HrDestination.Onboarding, HrDestination.Performance, HrDestination.Training, HrDestination.Benefits -> loadTalent()
            HrDestination.Timesheet -> if (timesheetState.timesheet == null && !timesheetState.loading) loadTimesheet(timesheetState.month, silent = false)
            HrDestination.MyPayslips -> if (payslipsState.items.isEmpty() && !payslipsState.loading) loadMyPayslips()
            // Sổ chi đổi liên tục (người nhận vừa quét, kế toán khác vừa lập) → luôn tải lại khi mở.
            HrDestination.Payout -> loadPayouts(silent = payoutState.items.isNotEmpty())
            HrDestination.CashCollections -> loadCashCollections(silent = cashCollectionState.items.isNotEmpty())
            HrDestination.Portal -> if (portalState.feed == null && !portalState.loading) loadPortal(silent = false)
            HrDestination.Tasks -> refreshTasks()
            // Lịch sử luôn tải lại: việc vừa nghiệm thu xong phải thấy ngay khi mở màn.
            HrDestination.TaskHistory -> loadTaskHistory(silent = taskHistoryState.result != null)
            HrDestination.Directory -> {
                if (directoryState.contacts.isEmpty()) refreshDirectory()
                startDirectoryPresenceRefresh()
            }
            HrDestination.Settings -> {
                settingsRoute = SettingsRoute.Home // vào tab Cài đặt luôn bắt đầu ở màn gốc
                if (settingsState.webLoginEnabled == null) loadSettings()
                if (settingsState.pushNotificationsEnabled == null) loadPushNotificationSetting()
            }
            HrDestination.Audit -> if (auditState.items.isEmpty() && !auditState.loading) loadAudit(reset = true)
            else -> if (homeState.employee == null && !homeState.loading) refreshHome(user, silent = true)
        }
    }

    fun refreshCurrent() {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        when (selected) {
            HrDestination.Profile -> loadProfileDocuments()
            HrDestination.Onboarding, HrDestination.Performance, HrDestination.Training, HrDestination.Benefits -> loadTalent()
            HrDestination.People -> refreshManager(silent = false)
            HrDestination.Dashboard -> refreshDashboard(dashboardStatus,null)
            HrDestination.Feedback -> loadFeedback()
            HrDestination.Help -> runDiagnostics()
            HrDestination.Scan -> checkAttendanceServer()
            HrDestination.Timesheet -> { loadTimesheet(timesheetState.month, silent = false); if (payEstimateState.data != null) loadMyEstimate() }
            HrDestination.MyPayslips -> loadMyPayslips()
            HrDestination.Payout -> loadPayouts(silent = payoutState.items.isNotEmpty())
            HrDestination.CashCollections -> loadCashCollections(silent = cashCollectionState.items.isNotEmpty())
            HrDestination.Portal -> loadPortal(silent = false)
            HrDestination.Tasks -> refreshTasks()
            HrDestination.TaskHistory -> loadTaskHistory()
            HrDestination.Directory -> refreshDirectory()
            HrDestination.Settings -> {
                loadSettings()
                loadPushNotificationSetting()
            }
            HrDestination.Audit -> loadAudit(reset = true)
            else -> {
                refreshHome(user, silent = false)
                if (user.can(AppPermissions.HrRead)) refreshManager(silent = true)
            }
        }
        // Nếu đang mở chi tiết một đơn thì làm mới luôn (kéo để xem tiến trình duyệt mới nhất).
        requestDetailState.id?.let { refreshOpenDetail(it) }
    }

    fun openManagedEmployee(id:String)=viewModelScope.launch{managedEmployeeLoading=true;runCatching{repo.employeeDetail(id)}.onSuccess{managedEmployee=it}.onFailure{actionMessage=readable(it)};managedEmployeeLoading=false}
    fun refreshDashboard(status:String?,departmentId:String?,date:String?=null){
        dashboardStatus=status
        date?.let { dashboardDate = it }
        viewModelScope.launch {
            val day = dashboardDate
            runCatching { repo.managerAttendance(day,status,departmentId) }.onSuccess { dashboardAttendance=it }.onFailure { actionMessage=readable(it) }
            runCatching { repo.managerSummary(day,day.take(7)) }.onSuccess { managerState=managerState.copy(summary=it) }
            dashboardTrend=(6 downTo 0).map { offset ->
                val d=java.time.LocalDate.parse(day).minusDays(offset.toLong())
                val count=runCatching { repo.managerAttendance(d.toString(),"present",departmentId).size }.getOrDefault(0)
                d.dayOfMonth.toString() to count
            }
        }
    }
    fun loadFeedback()=viewModelScope.launch{runCatching{repo.openSurveys()}.onSuccess{surveys=it}.onFailure{actionMessage=readable(it)};runCatching{repo.myGeneralFeedback()}.onSuccess{myFeedback=it}}
    fun answerSurvey(id:String,a:JsonObject)=viewModelScope.launch{runCatching{repo.answerSurvey(id,a)}.onSuccess{actionMessage="Đã gửi câu trả lời khảo sát.";loadFeedback()}.onFailure{actionMessage=readable(it)}}
    fun sendGeneralFeedback(message:String,anonymous:Boolean)=viewModelScope.launch{runCatching{repo.sendGeneralFeedback(message,anonymous)}.onSuccess{actionMessage="Đã gửi góp ý.";loadFeedback()}.onFailure{actionMessage=readable(it)}}
    fun runDiagnostics() = viewModelScope.launch {
        val context = getApplication<Application>()
        diagnostics = linkedMapOf(
            "API" to if (runCatching { repo.appConfig() }.isSuccess) "OK" else "Lỗi",
            "SSE/Realtime" to if (businessRealtime.isConnected()) "Đã kết nối" else "Đang kết nối lại",
            "FCM" to if (pushToken.isNullOrBlank()) "Chưa có token" else "OK",
            "Camera" to if (ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) "Đã cấp quyền" else "Chưa cấp quyền",
        )
        supportTickets = runCatching { repo.mySupportTickets() }.getOrDefault(emptyList())
    }
    fun createSupportTicket(message:String)=viewModelScope.launch{runCatching{repo.createSupportTicket(message)}.onSuccess{actionMessage="Đã gửi báo lỗi.";runDiagnostics()}.onFailure{actionMessage=readable(it)}}
    fun closeManagedEmployee(){managedEmployee=null}
    fun updateManagedEmployee(detail:EmployeeDetail,departmentId:String?,positionId:String?,positionIds:List<String>,status:String,managerId:String?=detail.managerId)=viewModelScope.launch{
        val selectedPosition = managerState.jobPositions.firstOrNull { it.id == positionId }
        val body=com.ketoanapk.hr.data.SaveEmployeeBody(
            employeeCode=detail.employeeCode, username=detail.username, fullName=detail.fullName,
            dob=detail.dob, gender=detail.gender, phone=detail.phone, email=detail.email,
            address=detail.address, departmentId=departmentId,
            position=selectedPosition?.name ?: detail.position, managerId=managerId,
            hireDate=detail.hireDate, status=status, avatar=detail.avatar,
            locationId=detail.locationId, accessRole=detail.accessRole, positionId=positionId,
            positionIds=(listOfNotNull(positionId)+positionIds).distinct(),
        )
        runCatching{repo.updateEmployee(detail.id,body)}.onSuccess{actionMessage="Đã cập nhật hồ sơ nhân viên.";openManagedEmployee(detail.id);refreshManager(false)}.onFailure{actionMessage=readable(it)}}
    fun updateManagedSalary(id:String,base:Double,allowance:Double,overtime:Double)=viewModelScope.launch{runCatching{repo.updateSalary(id,com.ketoanapk.hr.data.SaveSalaryBody(base,allowance,overtime))}.onSuccess{actionMessage="Đã cập nhật cấu trúc lương.";(authState as? AuthState.SignedIn)?.user?.let{refreshHome(it,true)}}.onFailure{actionMessage=readable(it)}}

    fun cancel(id: String) = decide { repo.cancelRequest(id); "Đã hủy đơn." }

    /** Làm mới màn "Việc cần làm" — gồm cả việc được giao vì hai thứ nay nằm chung một màn. */
    fun refreshTasks() {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        refreshHome(user, silent = false)
        loadWorkTasks(silent = workTasksState.inbox.isNotEmpty() || workTasksState.outbox.isNotEmpty())
        if (user.can(AppPermissions.HrRead)) refreshManager(silent = true)
    }

    fun openTask(task: TaskCenterItem) {
        select(task.target)
        task.entityId?.let { id ->
            when (task.target) {
                HrDestination.Approval -> openStaffDetail(id)
                HrDestination.Requests -> openRequestDetail(id)
                else -> Unit
            }
        }
    }

    fun quickApproveRequest(id: String) {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        if (taskActionBusyId != null) return
        taskActionBusyId = id
        viewModelScope.launch {
            runCatching { repo.approveRequest(id, "Duyệt nhanh từ Việc cần làm") }
                .onSuccess {
                    actionMessage = "Đã duyệt đơn."
                    refreshHome(user, silent = true)
                    if (user.can(AppPermissions.HrRead)) refreshManager(silent = true)
                }
                .onFailure { actionMessage = readable(it) }
            taskActionBusyId = null
        }
    }

    // ── Giao việc & nghiệm thu ───────────────────────────────────────────────────
    /** Nạp danh sách việc (của tôi + tôi giao) + metadata giao việc. */
    fun loadWorkTasks(silent: Boolean = false) {
        if (authState !is AuthState.SignedIn) return
        viewModelScope.launch {
            if (!silent) workTasksState = workTasksState.copy(loading = true, error = null)
            runCatching { repo.workTasks() }
                .onSuccess { res ->
                    workTasksState = workTasksState.copy(
                        loading = false, error = null, canAssign = res.canAssign,
                        inbox = res.inbox, outbox = res.outbox, collections = res.collections,
                        summary = res.summary,
                    )
                }
                .onFailure { if (!silent) workTasksState = workTasksState.copy(loading = false, error = readable(it)) }
            // Metadata (danh sách người nhận) chỉ cần cho người có quyền giao.
            if (workTasksState.canAssign && workTasksState.meta == null)
                runCatching { repo.workTaskMeta() }.onSuccess { workTasksState = workTasksState.copy(meta = it) }
        }
    }

    // ── Lịch sử việc đã hoàn thành ───────────────────────────────────────────────
    /** Mở màn Lịch sử công việc (màn riêng, có nút Back về màn Việc cần làm). */
    fun openTaskHistory() = select(HrDestination.TaskHistory)

    fun setTaskHistoryRange(range: TaskHistoryRange) {
        if (taskHistoryState.range == range) return
        taskHistoryState = taskHistoryState.copy(range = range)
        loadTaskHistory()
    }

    /** Lùi/tiến một tuần hoặc một tháng, tuỳ khoảng đang chọn. */
    fun shiftTaskHistory(offset: Int) {
        val state = taskHistoryState
        val anchor = when (state.range) {
            TaskHistoryRange.Week -> state.anchor.plusWeeks(offset.toLong())
            TaskHistoryRange.Month -> state.anchor.plusMonths(offset.toLong())
        }
        taskHistoryState = state.copy(anchor = anchor)
        loadTaskHistory()
    }

    fun setTaskHistoryAssignee(username: String?) {
        taskHistoryState = taskHistoryState.copy(assignee = username)
        loadTaskHistory()
    }

    fun loadTaskHistory(silent: Boolean = false) {
        if (authState !is AuthState.SignedIn) return
        val state = taskHistoryState
        val from = state.from.toString()
        val to = state.to.toString()
        val assignee = state.assignee
        viewModelScope.launch {
            if (!silent) taskHistoryState = taskHistoryState.copy(loading = true, error = null)
            runCatching { repo.workTaskHistory(from, to, assignee) }
                .onSuccess { res ->
                    // Bỏ qua kết quả đã cũ: người dùng bấm lùi/tiến nhanh hơn tốc độ mạng.
                    if (taskHistoryState.from.toString() == from &&
                        taskHistoryState.to.toString() == to &&
                        taskHistoryState.assignee == assignee
                    ) taskHistoryState = taskHistoryState.copy(loading = false, error = null, result = res)
                }
                .onFailure { taskHistoryState = taskHistoryState.copy(loading = false, error = readable(it)) }
        }
    }

    // ── Nhật ký một ngày (ô ngày trên lịch bảng công) ────────────────────────────
    /** Nạp mọi thứ đã xảy ra trong một ngày: việc đã làm, phạt, đơn tiền, phiếu chi. */
    fun loadDayLog(date: String?) {
        if (date.isNullOrBlank()) { dayLogState = DayLogUiState(); return }
        if (authState !is AuthState.SignedIn) return
        // Cùng ngày và đã có dữ liệu thì không gọi lại (bấm đi bấm lại một ô ngày).
        if (dayLogState.date == date && (dayLogState.data != null || dayLogState.loading)) return
        dayLogState = DayLogUiState(date = date, loading = true)
        viewModelScope.launch {
            runCatching { repo.myDayLog(date) }
                .onSuccess { if (dayLogState.date == date) dayLogState = dayLogState.copy(loading = false, data = it) }
                .onFailure { if (dayLogState.date == date) dayLogState = dayLogState.copy(loading = false, error = readable(it)) }
        }
    }

    fun openWorkTask(id: String) {
        workTaskDetail = WorkTaskDetailUiState(id = id, loading = true)
        refreshWorkTaskDetail(id)
    }

    fun closeWorkTaskDetail() { workTaskDetail = WorkTaskDetailUiState() }

    private fun refreshWorkTaskDetail(id: String) {
        viewModelScope.launch {
            runCatching { repo.workTaskDetail(id) }
                .onSuccess { if (workTaskDetail.id == id) workTaskDetail = workTaskDetail.copy(loading = false, detail = it, error = null) }
                .onFailure { if (workTaskDetail.id == id) workTaskDetail = workTaskDetail.copy(loading = false, error = readable(it)) }
        }
    }

    /** Bọc một thao tác trên việc: chạy → báo kết quả → làm mới chi tiết + danh sách. */
    private fun taskAction(successMsg: String, closeDetail: Boolean = false, block: suspend () -> Unit) {
        if (workTaskBusy) return
        workTaskBusy = true
        viewModelScope.launch {
            runCatching { block() }
                .onSuccess {
                    actionMessage = successMsg
                    val id = workTaskDetail.id
                    if (closeDetail) closeWorkTaskDetail() else id?.let { refreshWorkTaskDetail(it) }
                    loadWorkTasks(silent = true)
                }
                .onFailure { actionMessage = readable(it) }
            workTaskBusy = false
        }
    }

    fun createWorkTask(title: String, description: String, assignee: String, priority: String, dueAt: String?, onDone: () -> Unit) {
        if (workTaskBusy) return
        workTaskBusy = true
        viewModelScope.launch {
            runCatching { repo.createWorkTask(CreateTaskBody(title.trim(), description.trim(), assignee, priority, dueAt?.ifBlank { null })) }
                .onSuccess { actionMessage = "Đã giao việc."; loadWorkTasks(silent = true); onDone() }
                .onFailure { actionMessage = readable(it) }
            workTaskBusy = false
        }
    }

    fun updateWorkTask(id: String, title: String, description: String, assignee: String, priority: String, dueAt: String?, onDone: () -> Unit) {
        if (workTaskBusy) return
        workTaskBusy = true
        viewModelScope.launch {
            runCatching { repo.updateWorkTask(id, CreateTaskBody(title.trim(), description.trim(), assignee, priority, dueAt?.ifBlank { null })) }
                .onSuccess { actionMessage = "Đã cập nhật công việc."; refreshWorkTaskDetail(id); loadWorkTasks(silent = true); onDone() }
                .onFailure { actionMessage = readable(it) }
            workTaskBusy = false
        }
    }

    fun startWorkTask(id: String) = taskAction("Đã bắt đầu làm.") { repo.startWorkTask(id) }
    fun updateWorkTaskProgress(id: String, progress: Int, note: String) = taskAction("Đã cập nhật tiến độ.") { repo.progressWorkTask(id, progress, note) }
    fun submitWorkTask(id: String, note: String) = taskAction("Đã nộp để nghiệm thu.") { repo.submitWorkTask(id, note) }
    fun acceptWorkTask(id: String, note: String, rating: Int?) = taskAction("Đã nghiệm thu đạt.") { repo.acceptWorkTask(id, note, rating) }
    fun rejectWorkTask(id: String, note: String) = taskAction("Đã trả lại công việc.") { repo.rejectWorkTask(id, note) }
    fun cancelWorkTask(id: String, note: String) = taskAction("Đã huỷ công việc.", closeDetail = true) { repo.cancelWorkTask(id, note) }
    fun deleteWorkTask(id: String) = taskAction("Đã xoá công việc.", closeDetail = true) { repo.deleteWorkTask(id) }

   private var directorySearchJob: Job? = null
    fun setDirectoryQuery(value: String) {
        directoryState = directoryState.copy(query = value)
        directorySearchJob?.cancel()
        directorySearchJob = viewModelScope.launch { delay(300); refreshDirectory() }
    }

    fun setOrganizationMode(enabled: Boolean) { directoryState = directoryState.copy(organizationMode = enabled) }

    fun refreshDirectory(silent: Boolean = false) {
        directoryState = directoryState.copy(loading = !silent, error = null)
        viewModelScope.launch {
            runCatching { repo.directoryContacts(directoryState.query) }
                .onSuccess { directoryState = directoryState.copy(loading = false, contacts = it, error = null) }
                .onFailure { directoryState = directoryState.copy(loading = false, error = readable(it)) }
        }
    }


    // ── Tạo đơn từ + xem chi tiết ────────────────────────────────────────────────
    /**
     * Mở luồng khiếu nại từ màn Kỷ luật cho một án phạt tiền cụ thể: ghi nháp rồi chuyển sang tab Đơn từ,
     * nơi form penalty_appeal tự mở với án phạt đã chọn (nhân viên chỉ việc chọn Bỏ phạt / Giảm / Trả góp).
     */
    fun startPenaltyAppeal(penalty: Penalty) {
        appealDraft = AppealDraft(
            penaltyNo = penalty.penaltyNo,
            penaltyTypeLabel = penalty.penaltyTypeLabel.ifBlank { penalty.penaltyType },
            amount = penalty.amount,
            installments = penalty.installments,
        )
        select(HrDestination.Requests)
    }

    /** Xoá nháp khiếu nại khi đã gửi xong hoặc thoát luồng tạo đơn. */
    fun consumeAppealDraft() { appealDraft = null }

    fun startShiftSwap(date: String?) {
        requestDraftNonce++
        requestDraftRestoreSaved = true
        requestDraftType = "shift_swap"
        requestDraftValues = date?.takeIf { it.isNotBlank() }?.let { mapOf("date" to it) } ?: emptyMap()
        select(HrDestination.Requests)
    }

    /** Mở form "Báo quên chấm công" (loại forgot_checkin) điền sẵn ngày đang chọn ở bảng công. */
    fun startForgotCheckin(date: String?) {
        startForgotCheckinDraft(date)
    }

    /** Mở từ nhắc thiếu giờ ra: điền sẵn cả ngày và lựa chọn "Giờ ra". */
    private fun startForgotCheckinDraft(date: String?, direction: String? = null) {
        // Deep-link có thể đến khi màn Đơn từ đang giữ chi tiết/nháp cũ trong back stack. Dọn trạng thái
        // đó để lần bấm nhắc nào cũng mở đúng một đơn MỚI thay vì hiện lại đơn đang xem/sửa.
        closeRequestDetail()
        editingRequestId = null
        appealDraft = null
        requestDraftNonce++ // cùng ngày bấm lại vẫn dựng một form mới, không giữ state Compose đã sửa
        requestDraftRestoreSaved = direction == null
        requestDraftType = "forgot_checkin"
        requestDraftValues = buildMap {
            date?.takeIf { it.isNotBlank() }?.let { put("date", it) }
            direction?.takeIf { it == "in" || it == "out" }?.let { put("direction", it) }
        }
        select(HrDestination.Requests)
    }

    fun startAttendanceExplanation(result: ChamCongResult) {
        requestDraftNonce++
        requestDraftRestoreSaved = false
        requestDraftType = "attendance_fix"
        requestDraftValues = mapOf(
            "date" to java.time.LocalDate.now().toString(),
            "reason" to (result.guidance ?: result.message).orEmpty(),
        )
        resetCapture()
        select(HrDestination.Requests)
    }

    fun consumeRequestDraft() {
        requestDraftType = null
        requestDraftValues = emptyMap()
        requestDraftRestoreSaved = true
        editingRequestId = null
    }

    fun copyRequest(detail: RequestDetail) {
        requestDraftNonce++
        requestDraftRestoreSaved = false
        requestDraftType = detail.request.type
        requestDraftValues = detail.request.payload.mapNotNull { (key, value) ->
            if (key == "requesterSignature") null else value.jsonPrimitive.contentOrNull?.let { key to it }
        }.toMap()
        closeRequestDetail()
    }

    fun editRequest(detail: RequestDetail) {
        requestDraftNonce++
        requestDraftRestoreSaved = false
        editingRequestId = detail.request.id
        requestDraftType = detail.request.type
        requestDraftValues = detail.request.payload.mapNotNull { (key, value) ->
            if (key == "requesterSignature") null else value.jsonPrimitive.contentOrNull?.let { key to it }
        }.toMap()
        closeRequestDetail()
    }

    /** Gửi một đơn mới (payload đã được màn hình dựng từ các trường nhập). */
    fun submitRequest(type: String, title: String, payload: JsonObject, attachments: List<android.net.Uri>, onDone: (Boolean) -> Unit) {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        viewModelScope.launch {
            creatingRequest = true
            runCatching {
                val body = CreateRequestBody(type = type, title = title.trim(), payload = payload)
                val editId = editingRequestId
                val created = if (editId != null) {
                    repo.updateRequest(editId, body)
                    com.ketoanapk.hr.data.CreatedRequest(editId, "")
                } else repo.createRequest(body)
                attachments.forEach { repo.uploadRequestAttachment(getApplication(), created.id, it) }
                created
            }
                .onSuccess {
                    actionMessage = if (it.requestNo.isBlank()) "Đã cập nhật đơn." else "Đã gửi đơn ${it.requestNo}. Bạn có thể theo dõi trạng thái ở đây."
                    val requestDate = payload["date"]?.jsonPrimitive?.contentOrNull
                    val direction = payload["direction"]?.jsonPrimitive?.contentOrNull
                    if (type == "forgot_checkin" && direction == "out" && !requestDate.isNullOrBlank()) {
                        val center = notificationCenter
                        val updated = center.resolveMissedCheckout(requestDate)
                        if (center.accountScope == notificationCenter.accountScope) notifications = updated
                    }
                    refreshHome(user, silent = true)
                    onDone(true)
                }
                .onFailure { actionMessage = readable(it); onDone(false) }
            creatingRequest = false
        }
    }

    /** Xem chi tiết đơn của CHÍNH MÌNH — cho phép hủy khi còn chờ duyệt. */
    fun openRequestDetail(id: String) = loadRequestDetail(id, canCancel = true, canDecide = false)

    /** Xem chi tiết đơn của nhân sự khác ở chế độ CHỈ ĐỌC (phê duyệt thực hiện trên bản web). */
    fun openStaffDetail(id: String) = loadRequestDetail(id, canCancel = false, canDecide = true)

    private fun loadRequestDetail(id: String, canCancel: Boolean, canDecide: Boolean) {
        requestDetailState = RequestDetailUiState(id = id, loading = true, canCancel = canCancel, canDecide = canDecide)
        viewModelScope.launch {
            runCatching { repo.requestDetail(id) }
                .onSuccess { requestDetailState = RequestDetailUiState(id = id, detail = it, canCancel = canCancel, canDecide = canDecide) }
                .onFailure { requestDetailState = requestDetailState.copy(loading = false, error = readable(it)) }
        }
    }

    fun closeRequestDetail() { requestDetailState = RequestDetailUiState() }

    fun remindRequest(id: String) {
        viewModelScope.launch {
            runCatching { repo.remindRequest(id) }
                .onSuccess { actionMessage = "Đã nhắc người đang duyệt đơn." }
                .onFailure { actionMessage = readable(it) }
        }
    }

    /** Hủy đơn ngay trong màn chi tiết rồi đóng lại. */
    fun cancelFromDetail(id: String) {
        closeRequestDetail()
        cancel(id)
    }

    fun decideRequest(id: String, approve: Boolean, comment: String) {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        if (requestDetailState.deciding) return
        requestDetailState = requestDetailState.copy(deciding = true, decisionError = null)
        viewModelScope.launch {
            runCatching {
                if (approve) repo.approveRequest(id, comment.trim()) else repo.rejectRequest(id, comment.trim())
            }.onSuccess {
                actionMessage = if (approve) "Đã duyệt đơn." else "Đã từ chối đơn."
                val detail = runCatching { repo.requestDetail(id) }.getOrNull()
                requestDetailState = requestDetailState.copy(deciding = false, detail = detail ?: requestDetailState.detail)
                refreshHome(user, silent = true)
                if (user.can(AppPermissions.HrRead)) refreshManager(silent = true)
            }.onFailure {
                requestDetailState = requestDetailState.copy(deciding = false, decisionError = readable(it))
            }
        }
    }

    fun setAuditQuery(value: String) {
        auditState = auditState.copy(query = value)
    }

    fun setAuditEntity(value: String) {
        auditState = auditState.copy(entity = value)
        loadAudit(reset = true)
    }

    fun searchAudit() = loadAudit(reset = true)

    fun loadMoreAudit() {
        if (!auditState.loading && !auditState.loadingMore && auditState.hasMore) loadAudit(reset = false)
    }

    fun loadAudit(reset: Boolean) {
        if (reset) auditRequestId++
        val requestId = auditRequestId
        val offset = if (reset) 0 else auditState.items.size
        val query = auditState.query
        val entity = auditState.entity
        auditState = if (reset) auditState.copy(loading = true, error = null) else auditState.copy(loadingMore = true, error = null)
        viewModelScope.launch {
            runCatching { repo.audit(AUDIT_PAGE_SIZE, offset, query, entity) }
                .onSuccess { page ->
                    if (requestId != auditRequestId) return@onSuccess
                    auditState = auditState.copy(
                        loading = false,
                        loadingMore = false,
                        items = if (reset) page else auditState.items + page,
                        hasMore = page.size == AUDIT_PAGE_SIZE,
                        error = null,
                    )
                }
                .onFailure {
                    if (requestId != auditRequestId) return@onFailure
                    auditState = auditState.copy(loading = false, loadingMore = false, error = readable(it))
                }
        }
    }

    private fun decide(block: suspend () -> String) {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        viewModelScope.launch {
            runCatching { block() }
                .onSuccess {
                    actionMessage = it
                    refreshHome(user, silent = true)
                    if (user.can(AppPermissions.HrRead)) refreshManager(silent = true)
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

    /** Tải danh sách phiếu lương ĐÃ NHẬN của chính nhân viên (mỗi kỳ một thẻ). */
    fun loadMyPayslips() {
        if (payslipsState.loading) return
        payslipsState = payslipsState.copy(loading = true, error = null)
        viewModelScope.launch {
            runCatching { repo.myPayslips() }
                .onSuccess { payslipsState = PayslipsUiState(loading = false, items = it) }
                .onFailure { payslipsState = payslipsState.copy(loading = false, error = readable(it)) }
        }
    }

    private fun applyPayslipRequirement(requirement: PayslipRequirement) {
        val effective = payslipRequirementAt(requirement)
        val wasLocked = homeState.payslipRequirement.mustAcknowledge
        // serverNow đổi ở mỗi poll nhưng không phải thay đổi nghiệp vụ; không ghi cache xuống đĩa mỗi phút.
        val requirementChanged = homeState.payslipRequirement.copy(serverNow = "") !=
            effective.copy(serverNow = "")
        val previous = homeState.payslipRequirement.payslip
        val previousVersion = previous?.let { it.revisionToken.ifBlank { it.updatedAt } }.orEmpty()
        val next = effective.payslip
        val nextVersion = next?.let { it.revisionToken.ifBlank { it.updatedAt } }.orEmpty()
        if (previous?.id != next?.id || previousVersion != nextVersion) {
            payslipConfirmationError = null
        }
        homeState = homeState.copy(payslipRequirement = effective)
        if (requirementChanged) {
            (authState as? AuthState.SignedIn)?.user?.username?.let { username ->
                persistHomeSnapshot(username, homeState)
            }
        }
        if (!effective.mustAcknowledge) {
            if (wasLocked) (authState as? AuthState.SignedIn)?.user?.let(::consumePendingTarget)
            return
        }
        // Không đổi route sang kho phiếu cũ. HrShell dựng một màn xác nhận độc lập phủ toàn app.
        openRequiredPayslip(showMessage = false)
    }

    private suspend fun refreshPayslipRequirementNow(): Boolean = payslipRequirementMutex.withLock {
        // Chỉ cho một GET requirement chạy tại một thời điểm. Nếu request cũ bắt đầu trước ACK nhưng
        // về sau request hậu-ACK, nó phải hoàn tất trước để response mới luôn là trạng thái cuối cùng.
        val account = (authState as? AuthState.SignedIn)?.user?.username ?: return@withLock false
        applyPayslipRequirement(homeState.payslipRequirement)
        val response = runCatching { repo.payslipRequirement() }
        val activeAccount = (authState as? AuthState.SignedIn)?.user?.username
        if (!activeAccount.equals(account, ignoreCase = true)) return@withLock false
        response.onSuccess { requirement ->
            payslipRequirementServerGeneration++
            applyPayslipRequirement(requirement)
        }.isSuccess
    }

    private fun refreshPayslipRequirement() {
        if (authState !is AuthState.SignedIn) return
        viewModelScope.launch { refreshPayslipRequirementNow() }
    }

    private fun openRequiredPayslip(showMessage: Boolean) {
        if (!homeState.payslipRequirement.mustAcknowledge) return
        if (showMessage)
            actionMessage = "Phiếu lương đã quá hạn xác nhận. Hãy hoàn tất màn xác nhận bảo mật để mở khóa ứng dụng."
        val required = homeState.payslipRequirement.payslip
        val loaded = findPayslipForConfirmation(payslipsState.items, required?.id, required?.period)
        val versionMatches = when {
            required?.revisionToken?.isNotBlank() == true -> loaded?.revisionToken == required.revisionToken
            required?.updatedAt?.isNotBlank() == true -> loaded?.updatedAt == required.updatedAt
            else -> true
        }
        if ((loaded == null || !versionMatches) && !payslipsState.loading) loadMyPayslips()
    }

    // ---------------- Phiếu chi tiền mặt ----------------

    /** Màn hồ sơ nhân sự dùng cờ này để không hiện nút sửa lương cho vai trò chỉ được đọc lương. */
    val canManagePayroll: Boolean
        get() = (authState as? AuthState.SignedIn)?.user?.can(AppPermissions.PayrollManage) == true

    private fun canUsePayout(permission: String): Boolean {
        val user = (authState as? AuthState.SignedIn)?.user ?: return false
        return user.can(permission) && homeState.employee?.isAccounting == true
    }

    val canCreatePayout: Boolean get() = canUsePayout(AppPermissions.PayoutCreate)
    val canApprovePayout: Boolean get() = canUsePayout(AppPermissions.PayoutApprove)
    val canPayPayout: Boolean get() = canUsePayout(AppPermissions.PayoutPay)
    /** Tương thích tên cũ: có ít nhất một thao tác nghiệp vụ tiền mặt. */
    val isCashier: Boolean get() = canCreatePayout || canApprovePayout || canPayPayout

    fun canCancelPayout(voucher: PayoutVoucher): Boolean = when (voucher.status) {
        "AwaitingScan", "AwaitingApproval", "Confirmed" -> canCreatePayout || canApprovePayout
        "Approved" -> canApprovePayout
        else -> false
    }

    /**
     * Tải sổ phiếu chi. Kế toán lấy thêm danh mục/nguồn/người nhận để lập phiếu ngay trên app; nhân viên
     * thường chỉ cần danh sách phiếu của mình nên không gọi các endpoint bị cấm (tránh 403 vô ích).
     */
    fun loadPayouts(silent: Boolean = false) {
        viewModelScope.launch {
            val cashier = isCashier
            payoutState = payoutState.copy(loading = !silent, error = null)
            runCatching { repo.payoutVouchers(if (cashier) "all" else "mine") }
                .onSuccess { items ->
                    payoutState = payoutState.copy(loading = false, items = items)
                    // Người nhận vừa quét xong thì đóng hộp QR đang mở và báo cho kế toán biết.
                    val open = payoutState.qrVoucher
                    if (open != null) {
                        val fresh = items.firstOrNull { it.id == open.id }
                        when {
                            fresh == null -> Unit
                            fresh.status != "AwaitingScan" -> payoutState = payoutState.copy(
                                qrVoucher = null,
                                message = "${fresh.voucherNo} đã được người nhận ký nhận và chuyển sang chờ duyệt.",
                            )
                            fresh.qrValue != open.qrValue -> payoutState = payoutState.copy(qrVoucher = fresh)
                        }
                    }
                    if (canCreatePayout) loadCashierPickers()
                }
                .onFailure { payoutState = payoutState.copy(loading = false, error = readable(it)) }
        }
    }

    private fun loadCashierPickers() {
        viewModelScope.launch {
            val categories = runCatching { repo.payoutCategories() }.getOrDefault(payoutState.categories)
            val refunds = runCatching { repo.payoutRefundSources() }.getOrDefault(emptyList())
            val recipients = runCatching { repo.payoutRecipients() }.getOrDefault(payoutState.recipients)
            payoutState = payoutState.copy(categories = categories, refundSources = refunds, recipients = recipients)
        }
    }

    /** Lập phiếu rồi mở luôn mã QR để đưa người nhận quét. */
    fun createPayout(body: CreatePayoutBody, onDone: () -> Unit = {}) {
        if (!canCreatePayout) { payoutState = payoutState.copy(error = "Tài khoản không có quyền lập phiếu chi."); return }
        viewModelScope.launch {
            payoutState = payoutState.copy(busy = true, error = null)
            runCatching { repo.createPayoutVoucher(body) }
                .onSuccess { created ->
                    val items = runCatching { repo.payoutVouchers("all") }.getOrDefault(payoutState.items)
                    payoutState = payoutState.copy(
                        busy = false,
                        items = items,
                        qrVoucher = items.firstOrNull { it.id == created.id },
                        message = "Đã lập phiếu ${created.voucherNo}. Đưa mã QR cho người nhận quét.",
                    )
                    loadCashierPickers()
                    onDone()
                }
                .onFailure { payoutState = payoutState.copy(busy = false, error = readable(it)) }
        }
    }

    fun openPayoutQr(voucher: PayoutVoucher) {
        if (!canCreatePayout) { payoutState = payoutState.copy(error = "Tài khoản không có quyền tạo mã xác nhận."); return }
        // Mã hết hạn (người nhận tới muộn) thì xin mã mới ngay, khỏi bắt kế toán bấm hai lần.
        val expiresAt = voucher.qrExpiresAt?.let { runCatching { java.time.Instant.parse(it) }.getOrNull() }
        val expired = voucher.qrValue.isNullOrBlank() || expiresAt == null || !expiresAt.isAfter(java.time.Instant.now())
        if (!expired) { payoutState = payoutState.copy(qrVoucher = voucher); return }
        refreshPayoutQr(voucher)
    }

    fun refreshPayoutQr(voucher: PayoutVoucher) {
        if (!canCreatePayout) { payoutState = payoutState.copy(error = "Tài khoản không có quyền tạo mã xác nhận."); return }
        viewModelScope.launch {
            payoutState = payoutState.copy(busy = true, error = null)
            runCatching { repo.refreshPayoutQr(voucher.id) }
                .onSuccess {
                    payoutState = payoutState.copy(
                        busy = false,
                        qrVoucher = voucher.copy(qrValue = it.qrValue, qrExpiresAt = it.qrExpiresAt),
                    )
                    loadPayouts(silent = true)
                }
                .onFailure { payoutState = payoutState.copy(busy = false, error = readable(it)) }
        }
    }

    fun closePayoutQr() { payoutState = payoutState.copy(qrVoucher = null) }
    fun clearPayoutMessage() { payoutState = payoutState.copy(message = null, error = null) }

    fun approvePayout(voucher: PayoutVoucher) {
        if (!canApprovePayout) { payoutState = payoutState.copy(error = "Tài khoản không có quyền duyệt chi."); return }
        viewModelScope.launch {
            payoutState = payoutState.copy(busy = true, error = null)
            runCatching { repo.approvePayoutVoucher(voucher.id) }
                .onSuccess {
                    payoutState = payoutState.copy(busy = false, message = "Đã duyệt chi ${voucher.voucherNo}.")
                    loadPayouts(silent = true)
                }
                .onFailure { payoutState = payoutState.copy(busy = false, error = readable(it)) }
        }
    }

    fun completePayout(voucher: PayoutVoucher) {
        if (!canPayPayout) { payoutState = payoutState.copy(error = "Tài khoản không có quyền xác nhận thực chi."); return }
        viewModelScope.launch {
            payoutState = payoutState.copy(busy = true, error = null)
            runCatching { repo.completePayoutVoucher(voucher.id) }
                .onSuccess {
                    payoutState = payoutState.copy(busy = false, message = "Đã xác nhận thực chi ${voucher.voucherNo}.")
                    loadPayouts(silent = true)
                }
                .onFailure { payoutState = payoutState.copy(busy = false, error = readable(it)) }
        }
    }

    fun rejectPayout(voucher: PayoutVoucher, reason: String) {
        if (!canApprovePayout) { payoutState = payoutState.copy(error = "Tài khoản không có quyền từ chối phiếu chi."); return }
        viewModelScope.launch {
            payoutState = payoutState.copy(busy = true, error = null)
            runCatching { repo.rejectPayoutVoucher(voucher.id, reason) }
                .onSuccess {
                    payoutState = payoutState.copy(busy = false, qrVoucher = null, message = "Đã từ chối ${voucher.voucherNo}.")
                    loadPayouts(silent = true)
                }
                .onFailure { payoutState = payoutState.copy(busy = false, error = readable(it)) }
        }
    }

    fun cancelPayout(voucher: PayoutVoucher, reason: String = "") {
        if (!canCancelPayout(voucher)) { payoutState = payoutState.copy(error = "Tài khoản không có quyền hủy phiếu này."); return }
        viewModelScope.launch {
            payoutState = payoutState.copy(busy = true, error = null)
            runCatching { repo.cancelPayoutVoucher(voucher.id, reason) }
                .onSuccess {
                    payoutState = payoutState.copy(busy = false, qrVoucher = null, message = "Đã hủy phiếu ${voucher.voucherNo}.")
                    loadPayouts(silent = true)
                }
                .onFailure { payoutState = payoutState.copy(busy = false, error = readable(it)) }
        }
    }

    // ---------------- Lệnh thu tiền khách hàng ----------------

    val canReadAllCollections: Boolean
        get() = (authState as? AuthState.SignedIn)?.user?.can(AppPermissions.CollectionsReadAll) == true

    private fun canUseCollections(permission: String): Boolean {
        val user = (authState as? AuthState.SignedIn)?.user ?: return false
        return user.can(permission) && homeState.employee?.isAccounting == true
    }

    val canCreateCashCollection: Boolean get() = canUseCollections(AppPermissions.CollectionsCreate)
    val canReceiveCashCollection: Boolean get() = canUseCollections(AppPermissions.CollectionsReceive)

    fun loadCashCollections(silent: Boolean = false) {
        viewModelScope.launch {
            cashCollectionState = cashCollectionState.copy(loading = !silent, error = null)
            runCatching { repo.cashCollections(if (canReadAllCollections) "all" else "mine") }
                .onSuccess { items ->
                    cashCollectionState = cashCollectionState.copy(loading = false, items = items)
                    if (canCreateCashCollection) loadCashCollectionPickers()
                }
                .onFailure { cashCollectionState = cashCollectionState.copy(loading = false, error = readable(it)) }
        }
    }

    private fun loadCashCollectionPickers() {
        viewModelScope.launch {
            val drivers = runCatching { repo.cashCollectionDrivers() }.getOrDefault(cashCollectionState.drivers)
            val customers = runCatching { repo.accountingCustomers() }.getOrDefault(cashCollectionState.customers)
            cashCollectionState = cashCollectionState.copy(drivers = drivers, customers = customers)
        }
    }

    fun clearCashCollectionMessage() {
        cashCollectionState = cashCollectionState.copy(message = null, error = null)
    }

    fun createCashCollection(body: CreateCashCollectionBody, onDone: () -> Unit = {}) {
        if (!canCreateCashCollection) {
            cashCollectionState = cashCollectionState.copy(error = "Tài khoản không có quyền tạo lệnh thu tiền.")
            return
        }
        viewModelScope.launch {
            cashCollectionState = cashCollectionState.copy(busy = true, error = null)
            runCatching { repo.createCashCollection(body) }
                .onSuccess { created ->
                    cashCollectionState = cashCollectionState.copy(
                        busy = false,
                        message = "Đã tạo và giao lệnh ${created.orderNo} cho tài xế.",
                    )
                    loadCashCollections(silent = true)
                    onDone()
                }
                .onFailure { cashCollectionState = cashCollectionState.copy(busy = false, error = readable(it)) }
        }
    }

    fun acceptCashCollection(order: CashCollection) {
        viewModelScope.launch {
            cashCollectionState = cashCollectionState.copy(busy = true, error = null)
            runCatching { repo.acceptCashCollection(order.id) }
                .onSuccess {
                    cashCollectionState = cashCollectionState.copy(busy = false, message = "Bạn đã nhận lệnh ${order.orderNo}.")
                    loadCashCollections(silent = true)
                }
                .onFailure { cashCollectionState = cashCollectionState.copy(busy = false, error = readable(it)) }
        }
    }

    fun failCashCollection(order: CashCollection, reason: String) {
        if (reason.isBlank()) {
            cashCollectionState = cashCollectionState.copy(error = "Vui lòng nhập lý do không thu được tiền.")
            return
        }
        viewModelScope.launch {
            cashCollectionState = cashCollectionState.copy(busy = true, error = null)
            runCatching { repo.failCashCollection(order.id, reason.trim()) }
                .onSuccess {
                    cashCollectionState = cashCollectionState.copy(busy = false, message = "Đã báo không thu được tiền cho ${order.orderNo}.")
                    loadCashCollections(silent = true)
                }
                .onFailure { cashCollectionState = cashCollectionState.copy(busy = false, error = readable(it)) }
        }
    }

    fun collectCashCollection(order: CashCollection, quantities: Map<Long, Int>, reason: String, onDone: () -> Unit = {}) {
        val lines = cashCollectionLines(quantities)
        if (lines.isEmpty()) {
            cashCollectionState = cashCollectionState.copy(error = "Vui lòng nhập số lượng ít nhất một mệnh giá.")
            return
        }
        val total = lines.sumOf { it.denomination * it.quantity }.toDouble()
        if (total != order.expectedAmount && reason.isBlank()) {
            cashCollectionState = cashCollectionState.copy(error = "Số thực thu lệch dự kiến; vui lòng nhập lý do chênh lệch.")
            return
        }
        viewModelScope.launch {
            cashCollectionState = cashCollectionState.copy(busy = true, error = null)
            runCatching { repo.collectCashCollection(order.id, lines, reason.trim()) }
                .onSuccess { result ->
                    cashCollectionState = cashCollectionState.copy(
                        busy = false,
                        message = "Đã xác nhận thu ${formatMoney(result.collectedAmount)}. Hãy bàn giao thủ quỹ đúng hạn.",
                    )
                    loadCashCollections(silent = true)
                    onDone()
                }
                .onFailure { cashCollectionState = cashCollectionState.copy(busy = false, error = readable(it)) }
        }
    }

    fun receiveCashCollection(order: CashCollection, quantities: Map<Long, Int>, onDone: () -> Unit = {}) {
        if (!canReceiveCashCollection) {
            cashCollectionState = cashCollectionState.copy(error = "Tài khoản không có quyền nhận bàn giao tiền.")
            return
        }
        val lines = cashCollectionLines(quantities)
        if (lines.isEmpty()) {
            cashCollectionState = cashCollectionState.copy(error = "Vui lòng nhập số lượng ít nhất một mệnh giá.")
            return
        }
        viewModelScope.launch {
            cashCollectionState = cashCollectionState.copy(busy = true, error = null)
            runCatching { repo.receiveCashCollection(order.id, lines) }
                .onSuccess { result ->
                    cashCollectionState = cashCollectionState.copy(
                        busy = false,
                        message = "Đã nhận đủ ${formatMoney(result.amount)} — đã nộp đủ tiền.",
                    )
                    loadCashCollections(silent = true)
                    onDone()
                }
                .onFailure { failure ->
                    val latest = runCatching { repo.cashCollections(if (canReadAllCollections) "all" else "mine") }
                        .getOrDefault(cashCollectionState.items)
                    cashCollectionState = cashCollectionState.copy(busy = false, items = latest, error = readable(failure))
                }
        }
    }

    fun cancelCashCollection(order: CashCollection, reason: String) {
        if (!order.canCancel || reason.isBlank()) {
            cashCollectionState = cashCollectionState.copy(error = "Vui lòng nhập lý do hủy lệnh.")
            return
        }
        viewModelScope.launch {
            cashCollectionState = cashCollectionState.copy(busy = true, error = null)
            runCatching { repo.cancelCashCollection(order.id, reason.trim()) }
                .onSuccess {
                    cashCollectionState = cashCollectionState.copy(busy = false, message = "Đã hủy ${order.orderNo}.")
                    loadCashCollections(silent = true)
                }
                .onFailure { cashCollectionState = cashCollectionState.copy(busy = false, error = readable(it)) }
        }
    }

    fun resolveCashCollection(order: CashCollection, action: String, reason: String, onDone: () -> Unit = {}) {
        if (!order.canResolve || reason.isBlank()) {
            cashCollectionState = cashCollectionState.copy(error = "Vui lòng nhập lý do xử lý sai lệch.")
            return
        }
        viewModelScope.launch {
            cashCollectionState = cashCollectionState.copy(busy = true, error = null)
            runCatching { repo.resolveCashCollection(order.id, action, reason.trim()) }
                .onSuccess { result ->
                    cashCollectionState = cashCollectionState.copy(
                        busy = false,
                        message = if (action == "approve_actual")
                            "Đã duyệt ${formatMoney(result.amount)} — đã nộp đủ tiền."
                        else "Đã trả ${order.orderNo} cho tài xế kiểm đếm và khai lại.",
                    )
                    loadCashCollections(silent = true)
                    onDone()
                }
                .onFailure { cashCollectionState = cashCollectionState.copy(busy = false, error = readable(it)) }
        }
    }

    private fun cashCollectionLines(quantities: Map<Long, Int>): List<CashCountLineBody> =
        quantities.filterValues { it > 0 }.map { CashCountLineBody(it.key, it.value) }

    /** Mở chi tiết phiếu lương của một kỳ (bấm vào thẻ tháng). */
    fun openPayslip(period: String) { payslipOpenPeriod = period }

    /** Đóng chi tiết, quay lại danh sách thẻ tháng. */
    fun closePayslip() { payslipOpenPeriod = null }

    /** Mọi thao tác xác nhận đều đi qua màn riêng, kể cả xác nhận sớm từ kho phiếu. */
    fun openPayslipConfirmation(id: String) {
        val item = payslipsState.items.firstOrNull { it.id == id && it.acknowledgedAt == null } ?: return
        requestedPayslipConfirmationId = item.id
        payslipConfirmationError = null
        payslipConfirmationMessage = null
    }

    fun closePayslipConfirmation() {
        if (payslipAccessLocked || payslipAcknowledgingId != null) return
        requestedPayslipConfirmationId = null
        payslipConfirmationError = null
        payslipConfirmationMessage = null
    }

    /** Chỉ ghi nhận đúng snapshot đang vẽ; requirement đổi giữa lúc bấm sẽ buộc tải/xem lại. */
    fun confirmPayslipFromConfirmationScreen(displayed: PayslipItem) {
        val current = payslipConfirmationItem
        val sameRevision = if (current?.revisionToken?.isNotBlank() == true || displayed.revisionToken.isNotBlank())
            current?.revisionToken == displayed.revisionToken
        else current?.updatedAt == displayed.updatedAt
        if (current == null || current.id != displayed.id || !sameRevision) {
            payslipConfirmationError = "Phiếu lương vừa thay đổi. Vui lòng tải lại và kiểm tra số liệu mới trước khi xác nhận."
            payslipConfirmationMessage =
                "Phiếu vừa được cập nhật. Ứng dụng đã tải lại bản mới; hãy xác thực và kiểm tra lại trước khi xác nhận."
            reloadPayslipConfirmation(clearError = false)
            return
        }
        if (acknowledgedPayslipAwaitingSyncId == displayed.id) return
        acknowledgePayslip(displayed)
    }

    private fun acknowledgePayslip(item: PayslipItem) {
        if (payslipAcknowledgingId != null) return
        payslipAcknowledgingId = item.id
        payslipConfirmationError = null
        payslipConfirmationMessage = null
        viewModelScope.launch {
            runCatching { repo.acknowledgePayslip(item.id, item.revisionToken) }
                .onSuccess {
                    requestedPayslipConfirmationId = null
                    acknowledgedPayslipAwaitingSyncId = item.id
                    // Chờ requirement mới trước khi bỏ màn khóa. Nếu còn nhiều phiếu quá hạn, server
                    // trả phiếu tiếp theo và màn xác nhận tự chuyển sang id đó.
                    val synced = refreshPayslipRequirementNow()
                    loadMyPayslips()
                    if (!synced) {
                        payslipConfirmationError =
                            "Máy chủ đã ghi nhận phiếu này nhưng chưa đồng bộ được trạng thái mở khóa. Kiểm tra mạng và bấm Thử lại; không cần xác nhận lần nữa."
                    } else {
                        acknowledgedPayslipAwaitingSyncId = null
                        val message = if (homeState.payslipRequirement.mustAcknowledge)
                            "Đã ghi nhận phiếu này. Bạn còn phiếu lương quá hạn khác cần xác nhận."
                        else "Đã xác nhận phiếu lương. Ứng dụng đã được mở khóa."
                        payslipConfirmationMessage = message
                        actionMessage = message
                    }
                }
                .onFailure {
                    payslipConfirmationError = readable(it)
                    if (it is retrofit2.HttpException && it.code() == 409) {
                        payslipConfirmationMessage =
                            "Phiếu vừa được cập nhật. Ứng dụng đã tải lại bản mới; hãy xác thực và kiểm tra lại trước khi xác nhận."
                    }
                    // 409 do phiếu vừa sửa cũng đi qua đây; tải lại sẽ đổi review key và bắt xem/PIN lại.
                    refreshPayslipRequirement()
                    loadMyPayslips()
                }
            payslipAcknowledgingId = null
        }
    }

    fun retryPayslipConfirmation() {
        reloadPayslipConfirmation(clearError = true)
    }

    private fun reloadPayslipConfirmation(clearError: Boolean) {
        if (payslipAcknowledgingId != null) return
        if (clearError) payslipConfirmationError = null
        viewModelScope.launch {
            val synced = refreshPayslipRequirementNow()
            if (synced) acknowledgedPayslipAwaitingSyncId = null
            loadMyPayslips()
            if (!synced && (clearError || payslipConfirmationError == null))
                payslipConfirmationError = "Chưa kết nối được máy chủ. Vui lòng kiểm tra mạng rồi thử lại."
        }
    }

    fun sendPayslipInquiry(id: String, line: String, message: String) = viewModelScope.launch {
        runCatching { repo.payslipInquiry(id, line, message) }
            .onSuccess {
                val text = "Đã gửi thắc mắc tới bộ phận lương."
                if (payslipConfirmationVisible && payslipConfirmationItem?.id == id)
                    payslipConfirmationMessage = text
                actionMessage = text
            }
            .onFailure {
                val text = readable(it)
                if (payslipConfirmationVisible && payslipConfirmationItem?.id == id)
                    payslipConfirmationError = text
                actionMessage = text
            }
    }
    fun downloadPayslip(item:PayslipItem)=viewModelScope.launch{runCatching{repo.downloadPayslipPdf(getApplication(),item)}.onSuccess{file->
        val context=getApplication<Application>();val uri=androidx.core.content.FileProvider.getUriForFile(context,"${context.packageName}.fileprovider",file);val intent=android.content.Intent(android.content.Intent.ACTION_VIEW).apply{setDataAndType(uri,"application/pdf");addFlags(android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION or android.content.Intent.FLAG_ACTIVITY_NEW_TASK)};runCatching{context.startActivity(intent)}.onFailure{actionMessage="Đã tải PDF nhưng thiết bị không có ứng dụng mở PDF."}
    }.onFailure{actionMessage=readable(it)}}

    /** Mở màn chi tiết một bài (tin tức/sự kiện) trong cổng thông tin. */
    fun openPortalPost(post: PortalPost) { portalDetail = post }

    /** Đóng màn chi tiết, quay lại danh sách cổng thông tin. */
    fun closePortalDetail() { portalDetail = null }

    /** Tải cổng thông tin công ty (tin tức, sự kiện, giới thiệu) cho app hiển thị. */
    fun loadPortal(silent: Boolean) {
        viewModelScope.launch {
            portalState = portalState.copy(loading = true, error = if (silent) portalState.error else null)
            runCatching { repo.portalFeed() }
                .onSuccess { portalState = PortalUiState(loading = false, feed = it) }
                .onFailure { portalState = portalState.copy(loading = false, error = readable(it)) }
        }
    }

    private fun loadTimesheet(month: String, silent: Boolean) {
        val monthKey = runCatching { YearMonth.parse(month.take(7)).toString() }
            .getOrElse { currentMonthKey() }
        timesheetLoadJob?.cancel()

        val current = timesheetState.timesheet?.takeIf { it.period.take(7) == monthKey }
        val cached = timesheetCache[monthKey] ?: current
        timesheetState = TimesheetUiState(
            loading = cached == null,
            error = if (silent && cached != null) timesheetState.error else null,
            month = monthKey,
            timesheet = cached,
            neighbors = timesheetNeighborsOf(monthKey),
        )

        timesheetLoadJob = viewModelScope.launch {
            val result = runCatching { repo.myTimesheet(monthKey) }
            if (!isActive) return@launch
            result.onSuccess { sheet ->
                val resultMonth = runCatching { YearMonth.parse(sheet.period.take(7)).toString() }
                    .getOrElse { monthKey }
                timesheetCache[monthKey] = sheet
                timesheetCache[resultMonth] = sheet
                timesheetState = TimesheetUiState(
                    loading = false,
                    month = resultMonth,
                    timesheet = sheet,
                    neighbors = timesheetNeighborsOf(resultMonth),
                )
                prefetchTimesheetNeighbors(resultMonth)
            }.onFailure { error ->
                if (timesheetState.month == monthKey) {
                    timesheetState = timesheetState.copy(
                        loading = false,
                        error = if (cached == null) readable(error) else null,
                    )
                }
            }
        }
    }

    /** Nạp sẵn hai tháng kề bên để lần vuốt kế tiếp có dữ liệu ngay, không phải chờ mạng. */
    private fun prefetchTimesheetNeighbors(month: String) {
        val owner = (authState as? AuthState.SignedIn)?.user?.username ?: return
        val center = runCatching { YearMonth.parse(month.take(7)) }.getOrNull() ?: return
        listOf(center.minusMonths(1), center.plusMonths(1)).forEach { neighbor ->
            val key = neighbor.toString()
            if (timesheetCache.containsKey(key) || !timesheetPrefetching.add(key)) return@forEach
            viewModelScope.launch {
                val sheet = runCatching { repo.myTimesheet(key) }.getOrNull()
                val activeOwner = (authState as? AuthState.SignedIn)?.user?.username
                if (sheet != null && activeOwner == owner) {
                    timesheetCache[key] = sheet
                    // Đẩy ra state để trang lịch kề bên (đang hé ra khi vuốt) có dữ liệu thật.
                    timesheetState = timesheetState.copy(neighbors = timesheetNeighborsOf(timesheetState.month))
                }
                timesheetPrefetching.remove(key)
            }
        }
    }

    /** Lấy bảng công tháng trước/tháng sau đã có trong bộ nhớ đệm (nếu chưa nạp xong thì bỏ trống). */
    private fun timesheetNeighborsOf(month: String): Map<String, Timesheet> {
        val center = runCatching { YearMonth.parse(month.take(7)) }.getOrNull() ?: return emptyMap()
        return listOf(center.minusMonths(1), center.plusMonths(1))
            .mapNotNull { key -> timesheetCache[key.toString()]?.let { key.toString() to it } }
            .toMap()
    }

    private fun shiftMonthKey(month: String, offset: Int): String {
        val ym = runCatching { YearMonth.parse(month.take(7)) }.getOrElse { YearMonth.now() }
        return ym.plusMonths(offset.toLong()).toString()
    }

    /**
     * Khôi phục phiên khi mở app. Mất mạng KHÔNG làm mất phiên: [SessionRestore.Offline] vào thẳng
     * app bằng hồ sơ đã lưu, các vòng nhịp tim/poll/realtime bên dưới tự bắt lại khi có mạng.
     */
    private fun restoreSession() {
        viewModelScope.launch {
            // Giữ scope trước khi restore có thể xoá cached user do phiên 401/hết hạn.
            val previousAccount = tokenStore.cachedUser()?.username
            when (val restored = repo.restoreSession()) {
                is SessionRestore.Online -> enterSignedIn(restored.user)
                is SessionRestore.Offline -> enterSignedIn(restored.user)
                is SessionRestore.SignedOut -> {
                    previousAccount?.takeIf { it.isNotBlank() }?.let { oldAccount ->
                        NotificationCenter(getApplication<Application>(), oldAccount).reset()
                    }
                    authState = AuthState.SignedOut
                    homeState = HomeUiState()
                    loginError = if (pendingQrLoginCode != null || pendingMobileAppLoginCode != null) {
                        "Hãy đăng nhập ứng dụng để xác nhận đăng nhập trên web."
                    } else {
                        restored.message // lý do phải đăng nhập lại (nếu có)
                    }
                }
            }
        }
    }

    /** Dựng toàn bộ trạng thái sau khi đã xác định được người dùng (dù trực tuyến hay ngoại tuyến). */
    private fun enterSignedIn(user: HrUser) {
        beginPushRegistrationSession(user.username)
        authState = AuthState.SignedIn(user)
        activateNotificationAccount(user.username)
        startHeartbeat()
        startForegroundPoll() // tự làm mới danh sách/chi tiết đơn khi app đang mở
        startPayslipRequirementMonitor()
        startForegroundUpdateMonitor()
        businessRealtime.start(user.username)
        loadNotifications()
        syncPushDelivery()
        registerPush()
        // Mở lại app sau khi đã thoát HẲN (tiến trình bị thu hồi): hiện NGAY dữ liệu lần trước từ ảnh chụp
        // trên đĩa để không thấy màn trống + vòng quay tải, rồi làm mới IM LẶNG ở nền. Không có ảnh chụp
        // (lần đầu) thì tải bình thường. refreshHome ghi lại ảnh chụp sau mỗi lần tải thành công. Xem
        // [com.ketoanapk.hr.data.HomeCacheStore].
        viewModelScope.launch {
            val requirementGenerationBeforeCacheLoad = payslipRequirementServerGeneration
            // Chỉ khôi phục khi Trang chủ còn trống (mở app mới), tránh đè lên dữ liệu đã có trong bộ nhớ.
            val snapshot = if (homeState.employee == null)
                runCatching { repo.loadHomeSnapshot(user.username) }.getOrNull() else null
            val activeAccount = (authState as? AuthState.SignedIn)?.user?.username
            if (!activeAccount.equals(user.username, ignoreCase = true)) return@launch
            val restored = snapshot != null
            if (snapshot != null) {
                val cachedPayslipRequirement = payslipRequirementAt(snapshot.payslipRequirement)
                // Realtime/monitor có thể hoàn tất GET trong lúc đọc cache. Khi đó luôn giữ kết quả server mới,
                // không cho ảnh chụp cũ tạm mở khóa hoặc khóa lại ứng dụng.
                val effectivePayslipRequirement = if (
                    requirementGenerationBeforeCacheLoad == payslipRequirementServerGeneration
                ) cachedPayslipRequirement else homeState.payslipRequirement
                applyServerRequestFields(snapshot.requestTypes)
                homeState = HomeUiState(
                    loading = false,
                    employee = snapshot.employee,
                    timesheet = snapshot.timesheet,
                    requests = snapshot.requests,
                    inbox = snapshot.inbox,
                    penalties = snapshot.penalties,
                    salaries = snapshot.salaries,
                    requestTypes = snapshot.requestTypes,
                    payslipRequirement = effectivePayslipRequirement,
                )
                if (effectivePayslipRequirement.mustAcknowledge) openRequiredPayslip(showMessage = false)
                snapshot.timesheet?.let { sheet ->
                    val key = runCatching { YearMonth.parse(sheet.period.take(7)).toString() }
                        .getOrElse { currentMonthKey() }
                    timesheetCache[key] = sheet // để tab Bảng công cũng hiện ngay, không nhấp nháy
                }
            }
            refreshHome(user, silent = restored)
            loadTimesheet(currentMonthKey(), silent = restored)
        }
        loadWorkTasks(silent = true) // để huy hiệu "Giao việc" trên Trang chủ có số ngay
        if (user.can(AppPermissions.HrRead)) refreshManager(silent = true)
        faceRegistered = user.faceRegistered // cờ đi kèm dữ liệu đăng nhập → không cần gọi API riêng
        faceEnrollmentPending = user.faceEnrollmentPending
        faceEnrollmentStatus = when {
            user.faceRegistered -> "registered"
            user.faceEnrollmentPending -> "pending"
            else -> "not_enrolled"
        }
        loadAppConfig(force = true)
        consumePendingTarget(user)
        // MainActivity.onResume thường chạy khi restoreSession vẫn còn Loading nên lần kiểm tra ở đó bị
        // bỏ qua. Khôi phục phiên thành công phải tự kiểm tra để cold start không bỏ lỡ bản phát hành.
        autoCheckForUpdate(force = true)
    }

    private fun refreshHome(user: HrUser, silent: Boolean) {
        viewModelScope.launch {
            if (!silent) homeState = homeState.copy(loading = true, error = null)
            runCatching {
                val month = currentMonthKey()
                // Tải danh mục loại đơn kèm định nghĩa field, rồi nạp vào registry để dựng form động.
                val types = runCatching { repo.requestTypes() }.getOrDefault(emptyList())
                applyServerRequestFields(types)
                // Gọi SONG SONG các API độc lập. Trước đây gọi tuần tự → 6 vòng mạng nối đuôi nhau, mở app
                // (và mỗi lần làm mới) chậm hẳn; async chạy đồng thời nên chỉ tốn bằng lời gọi lâu nhất.
                coroutineScope {
                    val employee = async { runCatching { repo.myProfile() }.getOrNull() }
                    val timesheet = async { runCatching { repo.myTimesheet(month) }.getOrNull() }
                    val requests = async { runCatching { repo.requests("mine") }.getOrDefault(emptyList()) }
                    // Hộp thư duyệt cho MỌI người: máy chủ đã lọc theo người duyệt (quản lý trực tiếp) hoặc quản trị.
                    val inbox = async { runCatching { repo.requests("inbox") }.getOrDefault(emptyList()) }
                    val canManagePenalties = user.can(AppPermissions.PenaltyManage)
                    val penalties = async { runCatching { repo.penalties(if (canManagePenalties) "all" else "mine", if (canManagePenalties) month else null) }.getOrDefault(emptyList()) }
                    val salaries = async { if (user.can(AppPermissions.PayrollRead)) runCatching { repo.salaries() }.getOrDefault(emptyList()) else emptyList() }
                    HomeUiState(
                        loading = false,
                        employee = employee.await(),
                        timesheet = timesheet.await(),
                        requests = requests.await(),
                        inbox = inbox.await(),
                        penalties = penalties.await(),
                        salaries = salaries.await(),
                        requestTypes = types,
                        // Requirement được tải bằng coordinator riêng để response Home cũ không thể
                        // ghi đè trạng thái mới sau khi nhân viên vừa ACK.
                        payslipRequirement = homeState.payslipRequirement,
                    )
                }
            }.onSuccess {
                val activeAccount = (authState as? AuthState.SignedIn)?.user?.username
                if (!activeAccount.equals(user.username, ignoreCase = true)) return@onSuccess
                val refreshed = it.copy(payslipRequirement = homeState.payslipRequirement)
                homeState = refreshed
                syncNotifications(user, refreshed)
                // Ghi ảnh chụp để lần mở app sau hiện ngay, không phụ thuộc mạng. Best-effort. Xem [enterSignedIn].
                persistHomeSnapshot(user.username, refreshed)
                if (refreshed.payslipRequirement.mustAcknowledge) openRequiredPayslip(showMessage = false)
                refreshPayslipRequirement()
            }
                .onFailure {
                    // Đã có dữ liệu sẵn (từ ảnh chụp/ lần tải trước) thì đừng phủ lỗi lên trên — giữ dữ liệu
                    // cũ cho người dùng xem (giống cách tab Bảng công xử lý mất mạng). Chỉ báo lỗi khi trống trơn.
                    homeState = homeState.copy(
                        loading = false,
                        error = if (homeState.employee == null) readable(it) else null,
                    )
                }
        }
    }

    private fun persistHomeSnapshot(username: String, state: HomeUiState) {
        if (username.isBlank() || state.employee == null) return
        viewModelScope.launch {
            runCatching {
                repo.saveHomeSnapshot(
                    com.ketoanapk.hr.data.HomeSnapshot(
                        username = username,
                        employee = state.employee,
                        timesheet = state.timesheet,
                        requests = state.requests,
                        inbox = state.inbox,
                        penalties = state.penalties,
                        salaries = state.salaries,
                        requestTypes = state.requestTypes,
                        payslipRequirement = state.payslipRequirement,
                    ),
                )
            }
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
                    jobPositions = runCatching { repo.jobPositions() }.getOrDefault(emptyList()),
                )
            }.onSuccess { managerState = it }
                .onFailure { managerState = managerState.copy(loading = false, error = readable(it)) }
        }
    }

    // Nhịp tim THÍCH ỨNG theo SSE để app chạy nhẹ mà không mất an toàn phiên:
    //  • SSE foreground khỏe → session được server revalidate định kỳ; HTTP ping thưa là lưới an toàn.
    //  • SSE dừng ở nền hoặc mất kết nối → ping 45s/10s gánh presence/banner/thu hồi.
    // 401 = phiên bị thu hồi (đăng nhập máy khác / bị khoá / quá hạn nhàn rỗi) → đăng xuất kèm ĐÚNG lý do
    // máy chủ trả về. Mất mạng = Unknown → giữ phiên (fail-open).
    private fun startHeartbeat() {
        heartbeatJob?.cancel()
        heartbeatJob = viewModelScope.launch {
            var lastBackstopAt = 0L
            while (isActive) {
                if (businessRealtime.isConnected()) {
                    // Thu hồi TRONG LÚC đang kết nối đã có sự kiện "kicked" bắt tức thì; ping thưa ở đây chỉ
                    // phòng trường hợp hiếm lỡ mất "kicked" (rớt mạng chớp nhoáng rồi nối lại).
                    connection = ConnectionStatus.Online
                    val now = SystemClock.elapsedRealtime()
                    if (now - lastBackstopAt >= HEARTBEAT_BACKSTOP_MS) {
                        lastBackstopAt = now
                        val status = repo.heartbeat()
                        if (status is SessionStatus.Invalid) { logout(status.message); break }
                    }
                    // Dò lại nhanh để đổi nhánh kịp khi SSE rớt.
                    delay(15_000)
                } else {
                    lastBackstopAt = 0L // nối lại được thì cho ping lưới-an-toàn chạy ngay một nhịp
                    val status = checkConnectionOnce()
                    if (status is SessionStatus.Invalid) { logout(status.message); break }
                    // Mất kết nối → dò lại nhanh (10s) để GỠ banner ngay khi phục hồi; bình thường 45s.
                    delay(if (connection == ConnectionStatus.Online) 45_000 else 10_000)
                }
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
        val signedIn = authState as? AuthState.SignedIn ?: return
        startHeartbeat()        // bật lại nhịp tim (đã tắt lúc xuống nền để đỡ pin) — bắt ngay nếu bị thu hồi phiên
        pollLiveData()          // cập nhật ngay khi vừa mở lại app
        refreshCurrentAccess()  // role/quyền có thể vừa được quản trị thay đổi khi app ở nền
        loadAppConfig()         // lấy remote config mới (tiết chế 60s)
        startForegroundPoll()
        startPayslipRequirementMonitor()
        startForegroundUpdateMonitor()
        businessRealtime.start(signedIn.user.username)
        if (selected == HrDestination.Directory) startDirectoryPresenceRefresh()
        val forceUpdateCheck = pendingForcedUpdateCheck
        pendingForcedUpdateCheck = false
        autoCheckForUpdate(force = forceUpdateCheck)
    }

    /** Nạp lại UserDto do server tính; không suy quyền từ role/cached UI và đóng màn vừa bị thu quyền. */
    private fun refreshCurrentAccess() {
        val current = (authState as? AuthState.SignedIn)?.user ?: return
        viewModelScope.launch {
            runCatching { repo.me() }.onSuccess { fresh ->
                val stillSameAccount = (authState as? AuthState.SignedIn)?.user?.username
                    ?.equals(current.username, ignoreCase = true) == true
                if (!stillSameAccount) return@onSuccess
                authState = AuthState.SignedIn(fresh)
                if (!selected.isAvailableTo(fresh)) resetToHome()
                if (fresh.can(AppPermissions.HrRead)) refreshManager(silent = true)
            }
        }
    }

    /**
     * Activity gọi khi app xuống nền: dừng poll + SSE + nhịp tim để đỡ pin/mạng.
     * WorkManager + push FCM lo thông báo; còn thu hồi phiên (đăng nhập máy khác) sẽ được bắt ngay ở
     * [onAppResumed] khi mở lại — không cần ping HTTP đều đặn suốt lúc app nằm nền.
     */
    fun onAppPaused() {
        foregroundPollJob?.cancel()
        foregroundPollJob = null
        payslipRequirementJob?.cancel()
        payslipRequirementJob = null
        foregroundUpdateMonitorJob?.cancel()
        foregroundUpdateMonitorJob = null
        if (releaseUpdateDebounceJob?.isActive == true) pendingForcedUpdateCheck = true
        releaseUpdateDebounceJob?.cancel()
        releaseUpdateDebounceJob = null
        stopHeartbeat()
        directoryPresenceJob?.cancel()
        directoryPresenceJob = null
        businessRealtime.stop()
    }

    /**
     * Chip Online TẮT vì im lặng quá cửa sổ hiện diện — mà im lặng thì không có lệnh ghi nào, nên
     * máy chủ không thể phát sự kiện cho chiều này (xem UpdateGuards trong DatabaseChangePublisher).
     * Chỉ màn Danh bạ hiện chip đó, nên chỉ nó tự làm mới, và chỉ trong lúc đang mở.
     */
    private fun startDirectoryPresenceRefresh() {
        if (directoryPresenceJob?.isActive == true) return
        directoryPresenceJob = viewModelScope.launch {
            while (isActive) {
                delay(45_000)
                if (authState !is AuthState.SignedIn || selected != HrDestination.Directory) return@launch
                refreshDirectory(silent = true)
            }
        }
    }

    private fun startForegroundPoll() {
        if (foregroundPollJob?.isActive == true) return
        foregroundPollJob = viewModelScope.launch {
            while (isActive) {
                delay(pollIntervalMs()) // nhịp lấy từ remote config (admin chỉnh được), chặn 5–3600s
                // CHỈ poll khi SSE đang rớt; SSE khỏe thì server đã phát invalidation tức thì.
                if (authState is AuthState.SignedIn && !businessRealtime.isConnected()) pollLiveData()
            }
        }
    }

    /**
     * Hạn xác nhận có thể tự chuyển sang quá hạn lúc 00:00 dù không có sự kiện realtime nào phát sinh. Vì vậy app
     * kiểm tra riêng mỗi phút khi ở foreground; xuống nền thì dừng và kiểm tra ngay khi người dùng quay lại.
     */
    private fun startPayslipRequirementMonitor() {
        if (payslipRequirementJob?.isActive == true || authState !is AuthState.SignedIn) return
        payslipRequirementJob = viewModelScope.launch {
            while (isActive) {
                refreshPayslipRequirementNow()
                delay(60_000)
            }
        }
    }

    /**
     * Lưới dự phòng cho trường hợp FCM bị tắt/mất token và SSE không nhận được trigger. App để mở
     * liên tục vẫn hỏi bản mới mỗi 10 phút; [autoCheckForUpdate] tự tiết chế và lỗi mạng hoàn toàn im lặng.
     */
    private fun startForegroundUpdateMonitor() {
        if (foregroundUpdateMonitorJob?.isActive == true || authState !is AuthState.SignedIn) return
        foregroundUpdateMonitorJob = viewModelScope.launch {
            while (isActive) {
                delay(FOREGROUND_UPDATE_CHECK_MS)
                // Vòng này đã tự giãn đúng 10 phút nên ép kiểm tra để không trượt sang nhịp 20 phút
                // chỉ vì timestamp lệch vài mili-giây so với lần đăng nhập.
                autoCheckForUpdate(force = true)
            }
        }
    }

    /** INSERT release + ghi xong APK tạo hai sự kiện gần nhau; debounce để chỉ hỏi khi file đã sẵn sàng. */
    private fun schedulePublishedReleaseCheck() {
        if (loggingOut) return
        releaseUpdateDebounceJob?.cancel()
        releaseUpdateDebounceJob = viewModelScope.launch {
            delay(350)
            loadNotifications() // FCM có thể vừa ghi kho chuông bằng một NotificationCenter khác.
            autoCheckForUpdate(force = true)
        }
    }

    /** Nhịp tự làm mới foreground (mili-giây) theo remote config, có chặn biên an toàn. */
    private fun pollIntervalMs(): Long {
        val configured = appConfig.foregroundPollSeconds.coerceIn(5, 3600) * 1000L
        return if (com.ketoanapk.hr.data.AppPersonalization.dataSaver) maxOf(configured, 60_000L) else configured
    }

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
    private fun pollLiveData(changeScope: String = "all") {
        val user = (authState as? AuthState.SignedIn)?.user ?: return
        if (changeScope == "hr" || changeScope == "all") {
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
        if (changeScope == "tasks" || changeScope == "all") {
            // Giao việc: cập nhật danh sách + huy hiệu và chi tiết đang mở.
            loadWorkTasks(silent = true)
            workTaskDetail.id?.let { refreshWorkTaskDetail(it) }
        }
        // Bảng công + lương dự tính bám thẳng vào log chấm công / hồ sơ nhân sự (scope attendance|hr) →
        // làm mới IM LẶNG khi đang xem tab Bảng công để số liệu đổi tại chỗ (admin sửa công, duyệt đơn…).
        // Chỉ nạp lương khi nhân viên đã mở khoá (data != null) để không lộ/không gọi thừa lúc còn che.
        if (changeScope == "attendance" || changeScope == "hr" || changeScope == "cash" || changeScope == "all") {
            refreshPayslipRequirement()
            if (selected == HrDestination.CashCollections) loadCashCollections(silent = true)
            if (selected == HrDestination.Timesheet) {
                loadTimesheet(timesheetState.month, silent = true)
                if (payEstimateState.data != null) loadMyEstimate()
            }
        }
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

    // ── Bộ nhớ & dữ liệu tạm ─────────────────────────────────────────────────────
    /** Đo dung lượng cache để hiện trên màn "Bộ nhớ & dữ liệu tạm". */
    fun loadCacheSize() {
        viewModelScope.launch {
            val ctx = getApplication<Application>()
            val bytes = runCatching { com.ketoanapk.hr.data.CacheManager.sizeBytes(ctx) }.getOrDefault(0L)
            cacheSizeText = com.ketoanapk.hr.data.CacheManager.format(ctx, bytes)
        }
    }

    /**
     * Dọn cache của ứng dụng (gói cập nhật tải sẵn, ảnh/PDF tạm, ảnh chụp Trang chủ).
     * GIỮ nguyên phiên đăng nhập, đơn nháp và hàng đợi chấm công ngoại tuyến. Xem [com.ketoanapk.hr.data.CacheManager].
     */
    fun clearCache() {
        if (cacheClearing) return
        viewModelScope.launch {
            cacheClearing = true
            val ctx = getApplication<Application>()
            val freed = runCatching { com.ketoanapk.hr.data.CacheManager.clear(ctx) }.getOrDefault(0L)
            val bytes = runCatching { com.ketoanapk.hr.data.CacheManager.sizeBytes(ctx) }.getOrDefault(0L)
            cacheSizeText = com.ketoanapk.hr.data.CacheManager.format(ctx, bytes)
            cacheClearing = false
            actionMessage = if (freed > 0)
                "Đã dọn ${com.ketoanapk.hr.data.CacheManager.format(ctx, freed)} cache."
            else "Không có cache nào cần dọn."
        }
    }

    fun resolveQr(value: String, onResult: (com.ketoanapk.hr.data.QrResolveOutcome) -> Unit) {
        viewModelScope.launch {
            // Lỗi ngoài dự kiến (vd phản hồi sai định dạng) không được làm treo nút quét: coi như bị từ
            // chối để màn hình vẫn nhả trạng thái bận và báo được lý do.
            onResult(
                runCatching { repo.resolveQr(value) }
                    .getOrElse { com.ketoanapk.hr.data.QrResolveOutcome.Rejected(readable(it)) },
            )
        }
    }

    /** Nhận mã đăng nhập web từ deep-link Android; giữ lại qua màn Loading/Đăng nhập của app. */
    fun receiveQrLoginDeepLink(value: String) {
        val normalized = value.trim()
        if (normalized.isEmpty() || normalized.length > 4_096) return
        pendingQrLoginCode = normalized
        if (authState !is AuthState.SignedIn)
            loginError = "Hãy đăng nhập ứng dụng để xác nhận đăng nhập trên web."
    }

    fun consumePendingQrLoginCode() {
        pendingQrLoginCode = null
    }

    fun receiveMobileAppLoginDeepLink(value: String) {
        val normalized = value.trim()
        if (normalized.isEmpty() || normalized.length > 4_096) return
        pendingMobileAppLoginCode = normalized
        mobileAppLoginState = MobileAppLoginState.Received
        if (authState !is AuthState.SignedIn)
            loginError = "Hãy đăng nhập ứng dụng để xác nhận đăng nhập trên web."
    }

    fun processPendingMobileAppLogin() {
        val requestCode = pendingMobileAppLoginCode ?: return
        when (authState) {
            AuthState.Loading -> return
            AuthState.SignedOut -> {
                mobileAppLoginState = MobileAppLoginState.AwaitingAppLogin
                return
            }
            is AuthState.SignedIn -> Unit
        }
        if (mobileAppLoginState !is MobileAppLoginState.Idle &&
            mobileAppLoginState !is MobileAppLoginState.Received &&
            mobileAppLoginState !is MobileAppLoginState.AwaitingAppLogin) return
        pendingMobileAppLoginCode = null
        mobileAppLoginState = MobileAppLoginState.Resolving
        viewModelScope.launch {
            runCatching { repo.resolveMobileAppLogin(requestCode) }
                .onSuccess { mobileAppLoginState = MobileAppLoginState.Confirmation(it) }
                .onFailure { mobileAppLoginState = MobileAppLoginState.Finished(readable(it), accepted = false) }
        }
    }

    fun decideMobileAppLogin(accept: Boolean) {
        val current = mobileAppLoginState as? MobileAppLoginState.Confirmation ?: return
        if (current.submitting) return
        mobileAppLoginState = current.copy(submitting = true, error = null)
        viewModelScope.launch {
            runCatching {
                if (accept) repo.confirmMobileAppLogin(current.challenge.requestCode)
                else {
                    repo.rejectMobileAppLogin(current.challenge.requestCode)
                    "Đã từ chối yêu cầu đăng nhập web."
                }
            }.onSuccess { message ->
                mobileAppLoginState = MobileAppLoginState.Finished(message, accepted = accept)
            }.onFailure {
                mobileAppLoginState = current.copy(submitting = false, error = readable(it))
            }
        }
    }

    fun dismissMobileAppLogin() {
        val current = mobileAppLoginState
        mobileAppLoginState = MobileAppLoginState.Idle
        if (current is MobileAppLoginState.Confirmation && !current.submitting) {
            viewModelScope.launch { runCatching { repo.rejectMobileAppLogin(current.challenge.requestCode) } }
        }
    }

    fun decideQr(decisionToken: String, actionId: String, onResult: (com.ketoanapk.hr.data.QrActionEnvelope?) -> Unit = {}) {
        viewModelScope.launch {
            runCatching { repo.decideQr(decisionToken, actionId) }
                .onSuccess(onResult)
                .onFailure {
                    actionMessage = readable(it)
                    onResult(null)
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
    fun revokeAllDevices(){viewModelScope.launch{runCatching{repo.revokeAllDevices()}.onSuccess{logout()}.onFailure{actionMessage=readable(it)}}}

    fun changePassword(current: String, next: String, onDone: (Boolean) -> Unit) {
        viewModelScope.launch {
            runCatching { repo.changePassword(current, next) }
                .onSuccess { actionMessage = "Đã đổi mật khẩu."; onDone(true) }
                .onFailure { actionMessage = readable(it); onDone(false) }
        }
    }

    fun checkForUpdate() {
        if (loggingOut) return
        settingsState = settingsState.copy(checkingUpdate = true, updateMessage = null)
        pendingManualUpdateCheck = true
        autoCheckForUpdate(force = true)
    }

    /**
     * Kiểm tra cập nhật NGẦM để nhắc người dùng bằng thanh cố định trên mọi màn hình (không phải vào Cài đặt).
     * Gọi lúc đăng nhập ([force]) và mỗi khi app quay lại foreground; tự chặn gọi dồn trong 10 phút.
     * Lỗi mạng thì im lặng — người dùng vẫn có nút "Kiểm tra cập nhật" thủ công trong Cài đặt.
     */
    fun autoCheckForUpdate(force: Boolean = false, openDetails: Boolean = false) {
        if (loggingOut) return
        if (openDetails) pendingUpdateOpenDetails = true
        val signedIn = authState as? AuthState.SignedIn
        if (signedIn == null) {
            if (force || openDetails) pendingForcedUpdateCheck = true
            return
        }
        val now = System.currentTimeMillis()
        if (!force && now - lastSuccessfulUpdateCheckAt < 10 * 60 * 1000L) {
            if (openDetails && availableUpdate != null) {
                pendingUpdateOpenDetails = false
                openUpdateSheet()
            }
            return
        }
        if (updateCheckJob?.isActive == true) {
            if (force || openDetails) pendingForcedUpdateCheck = true
            return
        }
        if (force) pendingForcedUpdateCheck = false
        val account = signedIn.user.username
        val session = updateCheckSession
        updateCheckJob = viewModelScope.launch {
            val ctx = getApplication<Application>()
            val current = AppUpdater.installedVersionCode(ctx)
            val result = runCatching { repo.latestRelease(current) }
            if (session != updateCheckSession) return@launch
            result
                .onSuccess { info ->
                    val activeAccount = (authState as? AuthState.SignedIn)?.user?.username
                    if (!activeAccount.equals(account, ignoreCase = true)) return@onSuccess
                    lastSuccessfulUpdateCheckAt = System.currentTimeMillis()
                    notifications = notificationCenter.markObsoleteAppUpdatesRead(
                        installedVersionCode = current,
                        noUpdateAvailable = !info.hasUpdate,
                    )
                    if (!info.hasUpdate) {
                        availableUpdate = null
                        settingsState = settingsState.copy(
                            updateInfo = null,
                            updateChecked = true,
                            updateMessage = "Bạn đang dùng phiên bản mới nhất.",
                        )
                        // Nếu có follow-up (release/deep-link tới trong lúc request cũ chạy), giữ nguyên
                        // intent mở chi tiết và spinner thủ công cho response kế tiếp mới được consume.
                        if (!pendingForcedUpdateCheck) {
                            pendingUpdateOpenDetails = false
                            if (pendingManualUpdateCheck) {
                                pendingManualUpdateCheck = false
                                settingsState = settingsState.copy(checkingUpdate = false)
                            }
                        }
                        if (updateStage is UpdateStage.Idle) updateSheetVisible = false
                        return@onSuccess
                    }
                    availableUpdate = info
                    // Đồng bộ luôn màn Cài đặt để nút "Cập nhật ngay" ở đó cũng sẵn sàng.
                    settingsState = settingsState.copy(
                        updateInfo = info,
                        updateChecked = true,
                        updateMessage = "Có bản cập nhật ${info.version}.",
                    )
                    val shouldOpenDetails = pendingUpdateOpenDetails
                    pendingUpdateOpenDetails = false
                    if (pendingManualUpdateCheck && !pendingForcedUpdateCheck) {
                        pendingManualUpdateCheck = false
                        settingsState = settingsState.copy(checkingUpdate = false)
                    }
                    // Đang tải/đang cài thì đừng dựng lại bảng đè lên tiến trình đang chạy.
                    if (updateStage !is UpdateStage.Idle) return@onSuccess
                    // Bản thường chỉ hiện thanh nhỏ để không chặn công việc. Bản bắt buộc vẫn mở bảng;
                    // openDetails=true là lúc người dùng chủ động bấm thông báo phát hành ngoài hệ thống.
                    if (info.isMandatory || shouldOpenDetails) {
                        updateNeedsMeteredConsent = needsMeteredConsent(ctx, info)
                        updateSheetVisible = true
                    }
                }
            result.exceptionOrNull()?.let { error ->
                if (pendingManualUpdateCheck && !pendingForcedUpdateCheck) {
                    pendingManualUpdateCheck = false
                    settingsState = settingsState.copy(
                        checkingUpdate = false,
                        updateChecked = true,
                        updateMessage = readable(error),
                    )
                }
            }
            updateCheckJob = null
            // Sự kiện release/all tới trong lúc request đang chạy không bị bỏ: hoàn tất response hiện
            // tại trước, rồi hỏi lại một lần để bắt trạng thái APK mới nhất. Request lỗi không cập nhật
            // mốc throttle nên reconnect/resume kế tiếp sẽ thử ngay.
            if (pendingForcedUpdateCheck && authState is AuthState.SignedIn && !loggingOut) {
                pendingForcedUpdateCheck = false
                autoCheckForUpdate(force = true)
            }
        }
    }

    /** Mở bảng cập nhật theo yêu cầu (nút trong Cài đặt, hoặc bấm vào thông báo phát hành bản mới). */
    fun openUpdateSheet() {
        val info = availableUpdate ?: settingsState.updateInfo ?: return
        availableUpdate = info
        updateNeedsMeteredConsent = needsMeteredConsent(getApplication(), info)
        updateSheetVisible = true
    }

    /**
     * Người dùng bấm "Để sau" — chỉ ẩn bảng chi tiết; thanh cập nhật nhỏ vẫn hiện trên mọi màn hình.
     * Không lưu trạng thái "đã bỏ qua" nên đóng/mở app vẫn tiếp tục nhắc cho tới khi đã cài bản mới.
     */
    fun dismissUpdateSheet() {
        val info = availableUpdate
        if (info?.isMandatory == true) return          // bản bắt buộc: không cho bỏ qua
        if (updateStage is UpdateStage.Downloading) return  // đang tải: đóng bảng sẽ mất dấu tiến độ
        updateSheetVisible = false
        updateStage = UpdateStage.Idle
    }

    /** Gói lớn + đang dùng dữ liệu di động → phải xác nhận trước khi tốn cước. */
    private fun needsMeteredConsent(context: Context, release: ReleaseInfo): Boolean =
        release.apkSize > AppUpdater.LARGE_UPDATE_BYTES && AppUpdater.isMeteredConnection(context)

    /** Người dùng chấp nhận tải bằng dữ liệu di động — bỏ chặn rồi tải luôn. */
    fun acceptMeteredUpdate(context: Context) {
        updateNeedsMeteredConsent = false
        startUpdateDownload(context)
    }

    /**
     * Bấm "Cập nhật ngay": **giữ nguyên bảng cập nhật** và chạy tải ngay tại chỗ, có tiến độ.
     * (Lỗi cũ: đóng bảng trước rồi mới tải ngầm ~90 MB → màn hình im lìm vài phút, người dùng tưởng hỏng.)
     */
    fun startUpdateDownload(context: Context) {
        val release = availableUpdate ?: settingsState.updateInfo ?: return
        if (updateStage is UpdateStage.Downloading || updateStage is UpdateStage.Preparing) return
        if (!AppUpdater.canInstallPackages(context)) {
            AppUpdater.openUnknownSourcesSettings(context)
            updateStage = UpdateStage.Failed("Hãy bật quyền \"Cài ứng dụng không rõ nguồn\" cho ứng dụng này rồi bấm Thử lại.")
            return
        }
        if (needsMeteredConsent(context, release)) {
            updateNeedsMeteredConsent = true
            return
        }
        updateSheetVisible = true
        viewModelScope.launch {
            updateStage = UpdateStage.Preparing
            runCatching {
                // Gói của đúng bản này đã tải xong từ lần trước (vd. lỡ thoát màn cài) → cài lại luôn,
                // khỏi bắt người dùng tải lại ~90 MB.
                val cached = withContext(Dispatchers.IO) {
                    AppUpdater.verifiedCachedApk(context, release.apkSize, release.apkSha256)
                }
                val file = cached ?: AppUpdater.apkCacheFile(context, release.apkFileName).also { target ->
                    repo.downloadRelease(release, target) { downloaded, total ->
                        updateStage = UpdateStage.Downloading(downloaded, total)
                    }
                }
                updateStage = UpdateStage.Installing
                AppUpdater.openInstaller(context, file)
            }
                .onFailure { updateStage = UpdateStage.Failed(readable(it)) }
        }
    }

    /** Sau khi thoát màn cài của hệ thống mà chưa cài xong — cho bấm mở lại thay vì tải lại từ đầu. */
    fun resetUpdateStage() { updateStage = UpdateStage.Idle }

    /** Nút "Cập nhật ngay" ở màn Cài đặt — dùng chung một bảng cập nhật với luồng nhắc tự động. */
    fun installUpdate(context: Context) {
        if (availableUpdate == null) availableUpdate = settingsState.updateInfo
        openUpdateSheet()
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
        viewModelScope.launch {
            attendanceQueued = runCatching { repo.offlineItems() }.getOrDefault(emptyList())
            attendancePending = attendanceQueued.size
        }
    }

    fun refreshAttendanceContext() {
        refreshPendingCount()
        attendanceLocation = readLastLocation()
        viewModelScope.launch {
            attendancePolicy = runCatching { repo.attendancePolicy() }.getOrNull()
            attendanceHistory = runCatching { repo.myOfflineAttendance() }.getOrDefault(attendanceHistory)
        }
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
        if (attendanceServer !is AttendanceServerState.Online) return
        captureOffline = false
        motionMode = false
        smileMode = false
        smileThreshold = 0.65f
        // Đọc đồng thời cấu hình quay đầu + yêu cầu cười TRƯỚC khi mở camera. Backend cũ/lỗi mạng
        // sẽ tự lùi về tắt để không khóa chức năng chấm công.
        attendanceCapture = AttendanceCapture.Preparing
        viewModelScope.launch {
            val motionRequest = async { runCatching { repo.motionConfig().enabled }.getOrDefault(false) }
            val smileRequest = async { runCatching { repo.smileConfig() }.getOrNull() }
            motionMode = motionRequest.await()
            smileRequest.await()?.let {
                smileMode = it.enabled
                smileThreshold = it.threshold.coerceIn(0.35, 0.95).toFloat()
            }
            // Chỉ mở camera nếu người dùng chưa hủy trong lúc chờ.
            if (attendanceCapture is AttendanceCapture.Preparing) {
                attendanceCapture = AttendanceCapture.Collecting
            }
        }
    }

    /** Bắt đầu chấm công NGOẠI TUYẾN (khi mất mạng/máy chủ): quét xong lưu tạm, chờ đồng bộ + duyệt. */
    fun startOfflineCapture() {
        captureOffline = true
        motionMode = false
        smileMode = false
        attendanceCapture = AttendanceCapture.Collecting
    }

    fun submitQrAttendance(token: String) {
        if (token.isBlank()) return
        attendanceCapture = AttendanceCapture.Submitting
        viewModelScope.launch {
            runCatching { repo.qrAttendance(token) }
                .onSuccess {
                    attendanceCapture = AttendanceCapture.Done(it)
                    (authState as? AuthState.SignedIn)?.user?.let { user -> refreshHome(user, silent = true) }
                    loadTimesheet(timesheetState.month, silent = true)
                }
                .onFailure { attendanceCapture = AttendanceCapture.Done(ChamCongResult(status = "error", message = readable(it))) }
        }
    }

    fun resetCapture() { attendanceCapture = AttendanceCapture.Idle }

    /** Camera quét xong → tuỳ chế độ: nhận diện trực tuyến (xem trước) hoặc lưu tạm ngoại tuyến. */
    fun onFramesCaptured(frames: List<CapturedFrame>) {
        if (captureOffline) saveOfflineAttendance(frames.map { it.image }) else previewAttendance(frames)
    }

    /**
     * Camera đã quét xong loạt khung → gửi lên `/api/chamcong/cham` ở chế độ XEM TRƯỚC (previewOnly):
     * máy chủ chỉ nhận diện (chưa ghi công) rồi trả về ai + Vào/Ra dự kiến để hiện form xác nhận.
     * Chỉ khi khớp đúng người đang đăng nhập mới sang bước xác nhận; các trường hợp khác (sai tư thế,
     * mờ, giả mạo, không khớp…) hiện luôn kết quả để người dùng quét lại.
     */
    fun previewAttendance(frames: List<CapturedFrame>) {
        if (frames.isEmpty()) { attendanceCapture = AttendanceCapture.Idle; return }
        val motion = motionMode
        val images = frames.map { it.image }
        attendanceCapture = AttendanceCapture.Recognizing
        viewModelScope.launch {
            runCatching { repo.chamCong(images, previewOnly = true, motionCheck = motion) }
                .onSuccess { result ->
                    attendanceCapture =
                        if (result.status.equals("ok", true) && result.matched)
                            AttendanceCapture.AwaitingConfirm(result, frames, motion)
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

    /**
     * Người dùng bấm "Xác nhận" → ghi công thật.
     *
     * Đường chính: gửi TOKEN mà bước xem trước đã cấp. Server ghi công theo kết quả đã nhận diện vài giây
     * trước, không chạy lại AdaFace/Silent-Face lần hai — nhanh hơn hẳn và giảm một nửa tải suy luận.
     * Đường lùi: máy chủ cũ chưa cấp token thì vẫn gửi lại loạt ảnh như trước để app không kén phiên bản.
     */
    fun confirmAttendance() {
        val pending = attendanceCapture as? AttendanceCapture.AwaitingConfirm ?: return
        val token = pending.result.previewToken
        attendanceCapture = AttendanceCapture.Submitting
        viewModelScope.launch {
            runCatching {
                if (!token.isNullOrBlank()) {
                    repo.chamCongXacNhan(token)
                } else {
                    repo.chamCong(
                        pending.frames.map { it.image },
                        previewOnly = false,
                        motionCheck = pending.motionCheck,
                    )
                }
            }
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
            runCatching { repo.saveOfflineAttendance(images, occurredAt, loc?.latitude, loc?.longitude) }
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
        when {
            captureOffline -> attendanceCapture = AttendanceCapture.Collecting
            // Trực tuyến: xin CHUỖI MÀU MỚI cho lượt quét lại (challenge cũ có thể đã dùng/hết hạn).
            attendanceServer is AttendanceServerState.Online -> startCapture()
            else -> attendanceCapture = AttendanceCapture.Idle
        }
    }

    // ── Tự đăng ký khuôn mặt (mỗi tài khoản một lần, quét nhiều góc) ─────────────
    /** Nạp trạng thái đã đăng ký khuôn mặt của chính tài khoản (để làm mờ nút đăng ký). */
    fun loadFaceStatus(force: Boolean = false) {
        if (!force && faceEnrollmentStatus != null) return
        viewModelScope.launch {
            faceStatusLoading = true
            runCatching { repo.myFaceStatus() }
                .onSuccess {
                    faceRegistered = it.registered
                    faceEnrollmentPending = it.pending
                    faceEnrollmentStatus = it.requestStatus ?: if (it.registered) "registered" else "not_enrolled"
                    faceEnrollmentReviewNote = it.reviewNote
                }
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
        if (faceRegistered == true || faceEnrollmentPending) return
        faceEnroll = FaceEnrollCapture.Capturing
    }

    /** Hủy quét đăng ký (bấm Đóng / nút Back). */
    fun cancelFaceEnroll() { faceEnroll = FaceEnrollCapture.Idle }

    /** Bỏ kết quả đăng ký (thành công/thất bại) để quay về trạng thái nghỉ. */
    fun resetFaceEnroll() { faceEnroll = FaceEnrollCapture.Idle }

    // ----- Ảnh chân dung hồ sơ (tự chụp có hướng dẫn) -----
    /** Mở lớp camera hướng dẫn chụp chân dung (phủ toàn màn hình). */
    fun startPortraitCapture() { portraitCapture = PortraitCapture.Capturing }

    fun loadProfileDocuments() {
        viewModelScope.launch {
            profileDocumentsLoading = true
            runCatching { repo.myDocuments() }
                .onSuccess { profileDocuments = it }
                .onFailure { actionMessage = readable(it) }
            profileDocumentsLoading = false
        }
    }

    fun loadTalent() {
        viewModelScope.launch {
            talentState = talentState.copy(loading=true,error=null)
            val onboarding = runCatching { repo.onboarding() }
            val performance = runCatching { repo.performance() }
            val training = runCatching { repo.training() }
            val benefits = runCatching { repo.benefits() }
            talentState = TalentUiState(false,onboarding.getOrNull(),performance.getOrNull(),training.getOrDefault(emptyList()),benefits.getOrNull(),
                listOfNotNull(onboarding.exceptionOrNull(),performance.exceptionOrNull(),training.exceptionOrNull(),benefits.exceptionOrNull()).firstOrNull()?.let(::readable))
        }
    }
    fun completeOnboarding(id:String)=viewModelScope.launch{runCatching{repo.completeOnboarding(id)}.onSuccess{loadTalent()}.onFailure{actionMessage=readable(it)}}
    fun updateGoal(id:String,p:Double)=viewModelScope.launch{runCatching{repo.updateGoal(id,p)}.onSuccess{loadTalent()}.onFailure{actionMessage=readable(it)}}
    fun submitSelfReview(id:String,text:String)=viewModelScope.launch{runCatching{repo.selfReview(id,text)}.onSuccess{loadTalent()}.onFailure{actionMessage=readable(it)}}
    fun updateTraining(id:String,p:Int,s:Int)=viewModelScope.launch{runCatching{repo.trainingProgress(id,p,s)}.onSuccess{loadTalent()}.onFailure{actionMessage=readable(it)}}
    fun submitTrainingQuiz(id:String,answers:List<String>)=viewModelScope.launch{runCatching{repo.submitQuiz(id,answers)}.onSuccess{actionMessage="Điểm bài kiểm tra: ${it.score}%";loadTalent()}.onFailure{actionMessage=readable(it)}}

    fun uploadProfileDocument(uri: android.net.Uri?, type: String, title: String, number: String, expiresAt: String, issuedBy: String) {
        viewModelScope.launch {
            profileDocumentsLoading = true
            runCatching { repo.uploadMyDocument(getApplication(), uri, type, title, number, expiresAt, issuedBy) }
                .onSuccess { actionMessage = "Đã gửi hồ sơ, đang chờ HR duyệt."; loadProfileDocuments() }
                .onFailure { actionMessage = readable(it) }
            profileDocumentsLoading = false
        }
    }

    /** Đóng camera chụp chân dung (bấm Đóng / Back). */
    fun cancelPortraitCapture() { portraitCapture = PortraitCapture.Idle }

    /** Bỏ kết quả (thành công/thất bại) để quay về nghỉ. */
    fun resetPortraitCapture() { portraitCapture = PortraitCapture.Idle }

    /** Đã chụp xong 1 ảnh chân dung (data URL JPEG) → lưu lên máy chủ rồi làm mới hồ sơ. */
    fun submitPortrait(dataUrl: String) {
        if (dataUrl.isBlank()) { portraitCapture = PortraitCapture.Idle; return }
        portraitCapture = PortraitCapture.Saving
        viewModelScope.launch {
            runCatching { repo.updateMyAvatar(dataUrl) }
                .onSuccess {
                    portraitCapture = PortraitCapture.Done(true, "Đã cập nhật ảnh chân dung.")
                    (authState as? AuthState.SignedIn)?.user?.let { refreshHome(it, silent = true) }
                }
                .onFailure { portraitCapture = PortraitCapture.Done(false, readable(it)) }
        }
    }

    /** Camera đã quét đủ các góc → gửi lên máy chủ lưu mẫu. */
    fun submitFaceEnroll(poses: List<FaceEnrollPose>) {
        if (poses.isEmpty()) { faceEnroll = FaceEnrollCapture.Idle; return }
        faceEnroll = FaceEnrollCapture.Submitting
        viewModelScope.launch {
            runCatching { repo.enrollFace(poses) }
                .onSuccess {
                    faceRegistered = false
                    faceEnrollmentPending = it.status.equals("pending", ignoreCase = true)
                    faceEnrollmentStatus = it.status.ifBlank { "pending" }
                    faceEnrollmentReviewNote = null
                    faceEnroll = FaceEnrollCapture.Done(true, it.message.ifBlank { "Đã gửi yêu cầu đăng ký khuôn mặt chờ HR duyệt." })
                }
                .onFailure {
                    faceEnroll = FaceEnrollCapture.Done(false, readable(it))
                }
        }
    }

    /** Đọc vị trí GPS gần nhất (nỗ lực tốt nhất) nếu đã có quyền — dùng cho kiểm tra geofence khi duyệt. */
    @android.annotation.SuppressLint("MissingPermission")
    private fun readLastLocation(): android.location.Location? {
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
            best
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
        runCatching { connectivityManager?.unregisterNetworkCallback(networkCallback) }
        onAppPaused()
        super.onCleared()
    }

    companion object {
        /** Sentinel target của thông báo "bản cập nhật mới" (khớp với backend PushService). */
        const val UPDATE_TARGET = APP_UPDATE_NOTIFICATION_TARGET
        /** Bấm "Để sau" thì im 24 giờ (bản bắt buộc không áp dụng). */
        private const val AUDIT_PAGE_SIZE = 50
        /** Khi SSE đang mở (foreground): chỉ ping HTTP thưa làm lưới an toàn phát hiện phiên bị thu hồi. */
        private const val HEARTBEAT_BACKSTOP_MS = 5 * 60_000L
        private const val FOREGROUND_UPDATE_CHECK_MS = 10 * 60_000L
    }
}
