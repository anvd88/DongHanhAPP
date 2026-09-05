package com.ketoanapk.hr.data

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonObject

/**
 * Các model dữ liệu ánh xạ 1-1 với JSON của backend KetoanMini (đặt tên trường theo camelCase
 * trùng khóa JSON). Mọi trường đều có giá trị mặc định để giải mã an toàn khi thiếu/null.
 */

/**
 * Một dòng trong HỘP THƯ THÔNG BÁO trên máy chủ (bảng web_notifications) — cùng nguồn với cái chuông
 * trên web. App tải về rồi trộn vào chuông của mình, nhờ đó thông báo của cả công ty (giao hàng,
 * thu tiền, chứng từ…) hiện trên điện thoại kể cả khi máy chưa nhận được gói FCM nào.
 *
 * [notifId] là chữ ký sự kiện, TRÙNG với notif_id trong gói FCM — dùng nó làm khoá để một sự kiện
 * không hiện hai lần (một lần do push, một lần do đồng bộ).
 * [appTarget] là tên màn hình của APP (HrDestination); [link] là đường dẫn web nên app bỏ qua.
 */
@Serializable
data class ServerNotification(
    val id: Long = 0,
    val title: String = "",
    val body: String = "",
    val category: String = "",
    val link: String = "",
    val appTarget: String = "",
    val notifId: String = "",
    val createdAt: String = "",
    val read: Boolean = false,
)

/**
 * Nhóm thông báo mà tài khoản này còn nhận. Khoá trùng `Services/NotificationGroups.cs` ở máy chủ.
 * Đây là cấu hình PHÍA MÁY CHỦ và dùng chung với web: tắt ở app thì chuông trên web cũng im.
 */
@Serializable
data class NotificationGroupSettings(
    val groups: Map<String, Boolean> = emptyMap(),
)

@Serializable
data class ServerNotificationFeed(
    val unread: Int = 0,
    val items: List<ServerNotification> = emptyList(),
)

@Serializable data class PayslipInquiryBody(val lineLabel:String="",val message:String)
/** Một đồng nghiệp trong danh bạ tổ chức. */
@Serializable
data class DirectoryContact(
    val username: String = "",
    val displayName: String = "",
    val avatarUrl: String? = null,
    val isOnline: Boolean = false,
    val role: String = "",
    val employeeId: String = "",
    val employeeCode: String = "",
    val departmentId: String = "",
    val departmentName: String = "",
    val position: String = "",
    val phone: String = "",
    val email: String = "",
    val managerUsername: String = "",
    val managerName: String = "",
    val isDirectManager: Boolean = false,
    val sameDepartment: Boolean = false,
)

/**
 * Khóa quyền chuẩn do backend cấp trong UserDto.permissions. Ứng dụng chỉ dùng để dựng UI; API vẫn
 * kiểm tra lại quyền từ CSDL ở từng request. Không suy quyền từ tên role ở phía Android.
 */
object AppPermissions {
    const val UsersManage = "users.manage"
    const val CompanyScopeAll = "scope.company.all"
    const val AuditRead = "audit.read"
    const val PayrollRead = "payroll.read"
    const val PayrollManage = "payroll.manage"
    const val HrRead = "hr.read"
    const val HrManage = "hr.manage"
    const val RequestsApprove = "requests.approve"
    const val PenaltyManage = "penalty.manage"
    const val TasksAssign = "tasks.assign"
    const val PayoutRead = "payout.read"
    const val PayoutCreate = "payout.create"
    const val PayoutApprove = "payout.approve"
    const val PayoutPay = "payout.pay"
    const val CollectionsReadAll = "collections.read.all"
    const val CollectionsSelf = "collections.self"
    const val CollectionsCreate = "collections.create"
    const val CollectionsReceive = "collections.receive"
    const val CollectionsResolve = "collections.resolve"
}

@Serializable
data class HrUser(
    val id: String = "",
    val username: String = "",
    val fullName: String = "",
    val email: String = "",
    val role: String = "",
    val isActive: Boolean = true,
    val approvalStatus: String = "",
    val createdAt: String? = null,
    val avatarUrl: String? = null,
    val verified: Boolean = false,
    val isDiamond: Boolean = false,
    // Đã đăng ký khuôn mặt chưa (máy chủ trả kèm lúc đăng nhập/me) → quyết định hiện banner nhắc.
    val faceRegistered: Boolean = false,
    // Đã gửi vector mã hóa và đang chờ HR đối chiếu trực tiếp/kích hoạt.
    val faceEnrollmentPending: Boolean = false,
    // MỌI vai trò (vai trò chính + vai trò phụ như "Warehouse"/Thủ kho).
    val roles: List<String> = emptyList(),
    // Quyền hiệu lực do backend tính từ DB; thiếu/rỗng thì UI đặc quyền đóng mặc định.
    val permissions: List<String> = emptyList(),
    // Có quyền giao việc & nghiệm thu (Admin hoặc Thủ kho) — server chốt quyền thật.
    val canAssignTasks: Boolean = false,
) {
    val isAdmin: Boolean get() = role.equals("admin", ignoreCase = true)
    fun can(permission: String): Boolean = permissions.any { it == permission }
    fun canAny(vararg requested: String): Boolean = requested.any(::can)
    /** Có quyền giao việc & nghiệm thu. Chỉ để ẩn/hiện UI; server chốt quyền. */
    val isWarehouse: Boolean
        get() = canAssignTasks || can(AppPermissions.TasksAssign)
    val roleLabel: String
        get() = when (role.lowercase()) {
            "admin" -> "Quản trị hệ thống"
            "executive" -> "Ban giám đốc"
            "chiefaccountant" -> "Kế toán trưởng"
            "accounting" -> "Kế toán viên"
            "payroll" -> "Kế toán tiền lương"
            "cashier" -> "Thủ quỹ"
            "hr" -> "Quản lý nhân sự"
            "manager" -> "Trưởng phòng"
            "warehouse" -> "Thủ kho"
            "driver" -> "Lái xe"
            "kiosk" -> "Máy chấm công"
            else -> "Nhân viên"
        }
    val displayName: String get() = fullName.ifBlank { username }.ifBlank { "Nhân viên" }
}

// Cấu hình ứng dụng điều khiển từ xa (khớp AppConfigDto của backend) — admin đổi mà không cần ra APK.
@Serializable
data class AppConfig(
    val announcement: String = "",
    val announcementLevel: String = "info",
    val faceEnrollBannerEnabled: Boolean = true,
    val foregroundPollSeconds: Int = 20,
    // Tham số cắt ảnh chân dung (điều khiển từ xa — đổi trên trang Hệ thống, khỏi build lại APK).
    val portraitHeightFactor: Double = 1.85,
    val portraitVerticalNudge: Double = 0.15,
    val portraitAspect: Double = 0.75,
    val portraitMinWidthFactor: Double = 1.35,
    // Lời nhắc/thông báo chạy chữ trên Trang chủ (admin sửa từ xa) — luân phiên cùng lời chào theo buổi.
    val notices: List<String> = emptyList(),
)

// ----- Cổng thông tin công ty (tin tức, sự kiện, giới thiệu) -----

@Serializable
data class PortalPost(
    val id: Long = 0,
    val kind: String = "news",        // "news" | "event"
    val title: String = "",
    val summary: String = "",
    val body: String = "",
    val coverImage: String? = null,   // data URL base64 (tùy chọn)
    val location: String = "",
    val eventAt: String? = null,      // ISO UTC (chỉ dùng cho sự kiện)
    val pinned: Boolean = false,
    val published: Boolean = true,
    val authorUsername: String = "",
    val authorName: String = "",
    val createdAt: String = "",
    val updatedAt: String = "",
)

@Serializable
data class PortalAbout(
    val title: String = "",
    val content: String = "",
    val coverImage: String? = null,
    val address: String = "",
    val hotline: String = "",
    val email: String = "",
    val website: String = "",
    val updatedAt: String = "",
) {
    val hasContent: Boolean
        get() = title.isNotBlank() || content.isNotBlank() || address.isNotBlank() ||
            hotline.isNotBlank() || email.isNotBlank() || website.isNotBlank() || !coverImage.isNullOrBlank()
}

@Serializable
data class PortalFeed(
    val about: PortalAbout = PortalAbout(),
    val news: List<PortalPost> = emptyList(),
    val events: List<PortalPost> = emptyList(),
)

@Serializable
data class LoginRequest(
    val username: String,
    val password: String,
    val sid: String? = null,
    // Đánh dấu client native để backend KHÔNG chặn bằng cờ "tắt đăng nhập web".
    val client: String = "apk",
)

// Bước 2 màn quên mật khẩu: CHỈ kiểm tra mã khôi phục, chưa đổi mật khẩu.
@Serializable
data class RecoveryVerifyRequest(
    val username: String,
    val code: String,
)

// Khôi phục mật khẩu bằng MÃ do admin cấp (thay cho reset khuôn mặt đã tắt ở backend).
@Serializable
data class RecoveryResetRequest(
    val username: String,
    val code: String,
    val newPassword: String,
)

@Serializable
data class LoginResponse(
    val token: String = "",
    val user: HrUser = HrUser(),
)

/** Protocol QR tổng quát: APK gửi nguyên nội dung, server trả UI + action mà APK có thể thực thi. */
@Serializable
data class QrResolveBody(
    val value: String,
    val protocolVersion: Int = 1,
    val capabilities: List<String> = listOf("server_decision", "open_https_url", "dismiss"),
    val clientVersionCode: Int = 0,
)

@Serializable
data class QrDecisionBody(val decisionToken: String, val actionId: String)

@Serializable
data class MobileAppLoginCodeBody(
    val requestCode: String,
    val clientMode: String = "mobile_app",
)

@Serializable
data class MobileAppLoginChallenge(
    val requestCode: String = "",
    val title: String = "Xác nhận đăng nhập web?",
    val message: String = "",
    val expiresAt: String = "",
    val clientMode: String = "mobile_app",
)

@Serializable
data class MobileAppLoginMessage(val message: String = "")

@Serializable
data class QrPresentation(
    val title: String = "",
    val message: String = "",
    val severity: String = "info",
)

@Serializable
data class QrClientAction(
    val id: String = "",
    val type: String = "",
    val label: String = "",
    val style: String = "secondary",
    val url: String? = null,
    val closeOnSelect: Boolean = false,
)

@Serializable
data class QrActionEnvelope(
    // Phản hồi thiếu version phải fail-closed; chỉ request từ APK mới mặc định gửi protocol 1.
    val protocolVersion: Int = 0,
    val presentation: QrPresentation = QrPresentation(),
    val actions: List<QrClientAction> = emptyList(),
    val decisionToken: String? = null,
    val dismissActionId: String? = null,
    val expiresAt: String? = null,
    /** Máy chủ không có nghiệp vụ nào cho mã này → app tự đọc nội dung tại chỗ. Xem [QrResolveOutcome]. */
    val unhandled: Boolean = false,
)

@Serializable
data class SessionPing(val sid: String? = null)

@Serializable
data class EmployeeCard(
    val id: String = "",
    val employeeCode: String = "",
    val username: String = "",
    val fullName: String = "",
    val position: String = "",
    val positionId: String? = null,
    val positionCode: String? = null,
    val positionIds: List<String> = emptyList(),
    val positions: List<EmployeePosition> = emptyList(),
    val status: String = "Active",
    val phone: String = "",
    val email: String = "",
    val avatar: String? = null,
    val departmentId: String? = null,
    val departmentName: String = "",
    val managerName: String = "",
)

@Serializable
data class EmployeeDetail(
    val id: String = "",
    val employeeCode: String = "",
    val username: String = "",
    val fullName: String = "",
    val position: String = "",
    val positionId: String? = null,
    val positionCode: String? = null,
    val positionIds: List<String> = emptyList(),
    val positions: List<EmployeePosition> = emptyList(),
    val status: String = "Active",
    val phone: String = "",
    val email: String = "",
    val avatar: String? = null,
    val departmentId: String? = null,
    val departmentName: String = "",
    val locationId: String? = null,
    val accessRole: String = "staff",
    val managerName: String = "",
    val dob: String? = null,
    val gender: String = "",
    val address: String = "",
    val managerId: String? = null,
    val hireDate: String? = null,
    val isAccounting: Boolean = false,
)
@Serializable data class SaveEmployeeBody(val employeeCode:String,val username:String,val fullName:String,val dob:String?=null,val gender:String="",val phone:String="",val email:String="",val address:String="",val departmentId:String?=null,val position:String="",val managerId:String?=null,val hireDate:String?=null,val status:String="Active",val avatar:String?=null,val locationId:String?=null,val accessRole:String="staff",val positionId:String?=null,val positionIds:List<String> = emptyList())

/** Chức vụ chuẩn do máy chủ seed; quyền tài khoản được lấy từ defaultRole, không nhận role tự khai. */
@Serializable
data class JobPosition(
    val id: String = "",
    val code: String = "",
    val name: String = "",
    val defaultRole: String = "Employee",
    val defaultRoleLabel: String = "Nhân viên",
    val defaultAccessRole: String = "staff",
    val isSystem: Boolean = true,
    val isActive: Boolean = true,
    val sortOrder: Int = 0,
)

/** Chức vụ gắn vào hồ sơ; isPrimary đánh dấu chức vụ chính, các mục còn lại là kiêm nhiệm. */
@Serializable
data class EmployeePosition(
    val id: String = "",
    val code: String = "",
    val name: String = "",
    val defaultRole: String = "Employee",
    val defaultRoleLabel: String = "Nhân viên",
    val isPrimary: Boolean = false,
)

/** Thư tri ân "tròn X năm gắn bó" đã điền sẵn (server tính mốc theo ngày vào làm). show=false → không hiện. */
@Serializable
data class AnniversaryGreeting(
    val show: Boolean = false,
    /** true chỉ ở bản quản lý chủ động xem thử; đóng bản này không ghi nhận mốc thật là đã xem. */
    val preview: Boolean = false,
    val years: Int = 0,
    val anniversaryDate: String = "",
    /** Khoá để app nhớ đã xem (mỗi mốc chỉ hiện một lần), vd "anniv-2026". */
    val key: String = "",
    val title: String = "",
    val body: String = "",
    val signature: String = "",
)
@Serializable data class SaveSalaryBody(val baseSalary:Double,val allowance:Double,val overtimeRate:Double,val components:List<PayLine> = emptyList(),val note:String="")

@Serializable
data class EmployeeDocument(
    val id: String = "",
    val docType: String = "",
    val title: String = "",
    val issuedBy: String = "",
    val issuedDate: String? = null,
    val docNumber: String = "",
    val expiresAt: String? = null,
    val approvalStatus: String = "",
    val fileName: String = "",
    val mimeType: String = "",
    val note: String = "",
)

@Serializable data class OnboardingSummary(val mentorName:String="", val items:List<OnboardingTask> = emptyList())
@Serializable data class OnboardingTask(val id:String="",val title:String="",val actionKey:String="",val dueAt:String?=null,val policyText:String="",val completed:Boolean=false,val acknowledged:Boolean=false)
@Serializable data class PerformanceSummary(val goals:List<PerformanceGoal> = emptyList(),val reviews:List<PerformanceReview> = emptyList())
@Serializable data class PerformanceGoal(val id:String="",val title:String="",val description:String="",val target:Double=100.0,val progress:Double=0.0,val unit:String="%",val dueAt:String?=null)
@Serializable data class PerformanceReview(val id:String="",val period:String="",val closesAt:String?=null,val selfAssessment:String="",val managerComment:String="",val score:Double?=null,val status:String="")
@Serializable data class TrainingCourse(val id:String="",val title:String="",val description:String="",val materialUrl:String="",val videoUrl:String="",val quiz:List<TrainingQuestion> = emptyList(),val progress:Int=0,val resumeSeconds:Int=0,val score:Double?=null,val completedAt:String?=null,val certificateExpiresAt:String?=null)
@Serializable data class TrainingQuestion(val text:String="",val options:List<String> = emptyList())
@Serializable data class ProgressBody(val progress:Double)
@Serializable data class SelfReviewBody(val text:String)
@Serializable data class TrainingProgressBody(val progress:Int,val resumeSeconds:Int)
@Serializable data class QuizBody(val answers:List<String>)
@Serializable data class QuizResult(val score:Double=0.0,val passed:Boolean=false)
@Serializable data class BenefitsSummary(val leaveTotal:Double=0.0,val leaveUsed:Double=0.0,val leaveRemaining:Double=0.0,val leaveHistory:List<LeaveHistoryItem> = emptyList(),val benefits:List<BenefitItem> = emptyList(),val rewards:List<RewardItem> = emptyList(),val birthday:String?=null,val hireDate:String?=null)
@Serializable data class LeaveHistoryItem(val requestNo:String="",val payload:kotlinx.serialization.json.JsonObject=kotlinx.serialization.json.JsonObject(emptyMap()),val status:String="",val createdAt:String="")
@Serializable data class BenefitItem(val id:String="",val type:String="",val title:String="",val value:String="",val validFrom:String?=null,val validTo:String?=null)
@Serializable data class RewardItem(val id:String="",val title:String="",val points:Int=0,val awardedAt:String="",val note:String="")

@Serializable
data class Department(
    val id: String = "",
    val code: String = "",
    val name: String = "",
    val parentName: String = "",
    val managerName: String = "",
    val isAccounting: Boolean = false,
    val employeeCount: Int = 0,
)

@Serializable
data class TimesheetSummary(
    val workedDays: Double = 0.0,
    val absentDays: Double = 0.0,
    val lateDays: Int = 0,
    val earlyDays: Int = 0,
    val totalLateMinutes: Int = 0,
    val totalEarlyMinutes: Int = 0,
    val totalOvertimeMinutes: Int = 0,
    val totalWorkedHours: Double = 0.0,
)

@Serializable
data class TimesheetDay(
    val date: String = "",
    val shiftName: String = "",
    val shiftStart: String = "",
    val shiftEnd: String = "",
    /** null với server cũ; khi có thì là nguồn chuẩn thay cho suy luận end <= start. */
    val isOvernight: Boolean? = null,
    /** Khoảng chờ sau giờ kết ca trước khi kết luận thiếu giờ ra; null dùng fallback tương thích. */
    val checkoutGraceMinutes: Int? = null,
    /** Trạng thái đơn bù giờ ra hiện hữu do server join theo ngày; null với server cũ. */
    val missingCheckoutRequestStatus: String? = null,
    /** Id đơn gần nhất dùng làm generation khi đơn bị từ chối/hủy để re-arm reminder đúng một lần. */
    val missingCheckoutRequestId: String? = null,
    /** Alias boolean cho contract triển khai tối giản; true nghĩa là không được nhắc/tạo đơn trùng. */
    val hasOpenCheckoutRequest: Boolean? = null,
    val eventType: String = "",
    val holidayName: String = "",
    val holidayType: String = "",
    val checkIn: String? = null,
    val checkOut: String? = null,
    val lateMinutes: Int = 0,
    val earlyMinutes: Int = 0,
    val overtimeMinutes: Int = 0,
    val workedHours: Double = 0.0,
    val status: String = "",
)

@Serializable
data class Timesheet(
    val period: String = "",
    val summary: TimesheetSummary = TimesheetSummary(),
    val days: List<TimesheetDay> = emptyList(),
)

@Serializable
data class RequestType(
    val type: String = "",
    val label: String = "",
    val category: String = "",
    // Định nghĩa field do server trả (NGUỒN CHUẨN). App dựng form động từ đây; rỗng thì dùng bản dự phòng.
    val fields: List<ReqFieldDto> = emptyList(),
)

/** Một trường nhập của đơn do server mô tả. type: text|date|time|number|money|textarea|select|checkboxes. */
@Serializable
data class ReqFieldDto(
    val key: String = "",
    val label: String = "",
    val type: String = "text",
    val hint: String = "",
    val required: Boolean = true,
    val options: List<ReqOptionDto> = emptyList(),
)

@Serializable
data class ReqOptionDto(val value: String = "", val label: String = "")

@Serializable
data class RequestListItem(
    val id: String = "",
    val requestNo: String = "",
    val type: String = "",
    val typeLabel: String = "",
    val title: String = "",
    val requesterUsername: String = "",
    val employeeName: String = "",
    val employeeCode: String = "",
    val status: String = "",
    val currentStep: Int = 0,
    val totalSteps: Int = 0,
    val createdAt: String = "",
)

// Thân yêu cầu tạo đơn mới: type = mã loại đơn, payload = các trường chi tiết (jsonb linh hoạt).
// title để trống → backend tự đặt theo nhãn loại đơn.
@Serializable
data class CreateRequestBody(
    val type: String,
    val title: String = "",
    val payload: JsonObject = JsonObject(emptyMap()),
)

@Serializable
data class CreatedRequest(
    val id: String = "",
    val requestNo: String = "",
)

// Chi tiết một đơn: phần đầu (thông tin + payload) + tiến trình phê duyệt nhiều bước.
@Serializable
data class RequestDetail(
    val request: RequestHead = RequestHead(),
    val approvals: List<RequestApproval> = emptyList(),
    val attachments: List<RequestAttachment> = emptyList(),
)

@Serializable
data class RequestAttachment(
    val id: Long = 0,
    val fileName: String = "",
    val mimeType: String = "",
    val fileSize: Long = 0,
)

@Serializable
data class RequestHead(
    val id: String = "",
    val requestNo: String = "",
    val type: String = "",
    val typeLabel: String = "",
    val title: String = "",
    val requesterUsername: String = "",
    val employeeName: String = "",
    val employeeCode: String = "",
    val departmentName: String = "",
    val payload: JsonObject = JsonObject(emptyMap()),
    val status: String = "",
    val currentStep: Int = 0,
    val createdAt: String = "",
    val dueAt: String? = null,
)

@Serializable
data class RequestApproval(
    val stepNo: Int = 0,
    val approverRole: String = "",
    val approverUsername: String = "",
    val approverName: String = "",
    val status: String = "",
    val decidedAt: String? = null,
    val decidedBy: String = "",
    val comment: String = "",
    val hasSignature: Boolean = false,
)

@Serializable
data class AuditEntry(
    val occurredAt: String = "",
    val username: String = "",
    val action: String = "",
    val entity: String = "",
    val entityName: String = "",
    val details: String = "",
)

@Serializable
data class Penalty(
    val id: String = "",
    val penaltyNo: String = "",
    val employeeName: String = "",
    val employeeCode: String = "",
    val penaltyType: String = "",
    val penaltyTypeLabel: String = "",
    val penaltyDate: String? = null,
    val amount: Double = 0.0,
    val installments: Int = 1,
    val reason: String = "",
    val note: String = "",
    val status: String = "",
    val createdBy: String = "",
    val createdAt: String = "",
)

@Serializable
data class SalaryListItem(
    val employeeId: String = "",
    val employeeName: String = "",
    val employeeCode: String = "",
    val departmentName: String = "",
    val hasSalary: Boolean = false,
    val baseSalary: Double = 0.0,
    val allowance: Double = 0.0,
    val overtimeRate: Double = 0.0,
    val extraCount: Int = 0,
)

/** Một dòng lương (khoản cộng hoặc khoản trừ) trong phiếu/ước tính lương. */
@Serializable
data class PayLine(
    val label: String = "",
    val amount: Double = 0.0,
)

/** Một ngày tăng ca đã được duyệt và chốt vào phiếu lương. */
@Serializable
data class PayslipOvertimeDay(
    val date: String = "",
    val checkIn: String = "",
    val checkOut: String = "",
    val minutes: Int = 0,
)

/** Phiếu chưa xác nhận cấp bách nhất và trạng thái khóa ứng dụng do máy chủ tính theo giờ Việt Nam. */
@Serializable
data class PayslipRequirementItem(
    val id: String = "",
    val period: String = "",
    val publishedAt: String = "",
    val updatedAt: String = "",
    val revisionToken: String = "",
    val acknowledgementDueAt: String = "",
    val overdue: Boolean = false,
)

@Serializable
data class PayslipRequirement(
    val pendingCount: Int = 0,
    val overdueCount: Int = 0,
    val mustAcknowledge: Boolean = false,
    val serverNow: String = "",
    val payslip: PayslipRequirementItem? = null,
)

/** Một phiếu lương ĐÃ PHÁT HÀNH của chính nhân viên (mỗi kỳ yyyy-MM một phiếu). */
@Serializable
data class PayslipItem(
    val id: String = "",
    val period: String = "",
    val baseSalary: Double = 0.0,
    val allowance: Double = 0.0,
    val overtimePay: Double = 0.0,
    val overtimeHours: Double = 0.0,
    val workedDays: Int = 0,
    val absentDays: Int = 0,
    val lateDays: Int = 0,
    val totalWorkedHours: Double = 0.0,
    val overtimeRate: Double = 0.0,
    val overtimeDays: List<PayslipOvertimeDay> = emptyList(),
    val earnings: List<PayLine> = emptyList(),
    val deductions: List<PayLine> = emptyList(),
    val totalEarnings: Double = 0.0,
    val totalDeductions: Double = 0.0,
    val netPay: Double = 0.0,
    val note: String = "",
    val createdAt: String = "",
    val publishedAt: String = "",
    val updatedAt: String = "",
    val revisionToken: String = "",
    val acknowledgementDueAt: String = "",
    val acknowledgementOverdue: Boolean = false,
    val acknowledgedAt: String? = null,
)

/** Lương dự tính của chính nhân viên cho tháng hiện tại (gồm khấu trừ phạt nếu có). */
@Serializable
data class PayEstimate(
    val employeeName: String = "",
    val employeeCode: String = "",
    val period: String = "",
    val baseSalary: Double = 0.0,
    val overtimeHours: Double = 0.0,
    val overtimePay: Double = 0.0,
    val workedDays: Int = 0,
    val absentDays: Int = 0,
    val lateDays: Int = 0,
    val earnings: List<PayLine> = emptyList(),
    val deductions: List<PayLine> = emptyList(),
    val totalEarnings: Double = 0.0,
    val totalDeductions: Double = 0.0,
    val netPay: Double = 0.0,
    val hasSalary: Boolean = false,
)

@Serializable
data class ManagerHeadcount(
    val total: Int = 0,
    val active: Int = 0,
    val present: Int = 0,
    val leave: Int = 0,
    val business: Int = 0,
    val absent: Int = 0,
    val late: Int = 0,
    val overtime: Int = 0,
    val unassigned: Int = 0,
    val pendingApprovals: Int = 0,
    val expiringContracts: Int = 0,
    val alerts: Int = 0,
)
@Serializable data class ManagerAttendanceItem(val employeeId:String="",val employeeCode:String="",val employeeName:String="",val position:String="",val departmentId:String?=null,val departmentName:String="",val status:String="",val statusLabel:String="",val shiftName:String="",val checkIn:String="",val checkOut:String="",val lateMinutes:Int=0,val overtimeMinutes:Int=0,val requestNo:String="",val requestTitle:String="")
@Serializable data class SurveyItem(val id:String="",val title:String="",val description:String="",val questions:List<SurveyQuestion> = emptyList(),val closesAt:String?=null,val answered:Boolean=false)
@Serializable data class SurveyQuestion(val key:String="",val text:String="",val type:String="text",val options:List<String> = emptyList())
@Serializable data class SurveyResponseBody(val answers:kotlinx.serialization.json.JsonObject)
@Serializable data class GeneralFeedbackBody(val message:String,val anonymous:Boolean=false)
@Serializable data class GeneralFeedbackItem(val id:String="",val message:String="",val status:String="",val response:String="",val createdAt:String="")
@Serializable data class SupportTicketBody(val message:String,val appVersion:String,val deviceModel:String)
@Serializable data class SupportTicketItem(val id:String="",val code:String="",val message:String="",val status:String="",val response:String="",val createdAt:String="")

@Serializable
data class ManagerDepartmentStatus(
    val departmentId: String? = null,
    val departmentName: String = "",
    val total: Int = 0,
    val present: Int = 0,
    val leave: Int = 0,
    val business: Int = 0,
    val absent: Int = 0,
)

@Serializable
data class ManagerSummary(
    val date: String = "",
    val month: String = "",
    val headcount: ManagerHeadcount = ManagerHeadcount(),
    val departments: List<ManagerDepartmentStatus> = emptyList(),
)

// ----- Cài đặt: thiết bị, mật khẩu, thông báo, đăng nhập web, cập nhật -----

@Serializable
data class DeviceSession(
    val sid: String = "",
    val machineName: String = "",
    val clientKind: String = "",
    val userAgent: String = "",
    val startedAt: String? = null,
    val lastSeen: String? = null,
    val isActive: Boolean = false,
    val revoked: Boolean = false,
    val current: Boolean = false,
)

@Serializable
data class ChangePasswordBody(
    val currentPassword: String,
    val newPassword: String,
)

@Serializable
data class AccountLoginSettings(
    val webLoginEnabled: Boolean = true,
)

@Serializable
data class ReleaseInfo(
    val hasUpdate: Boolean = false,
    val id: Long = 0,
    val version: String = "",
    val versionCode: Int = 0,
    val releaseNotes: String = "",
    val isMandatory: Boolean = false,
    val apkFileName: String = "",
    val apkSize: Long = 0,
    val apkSha256: String = "",
    val downloadUrl: String = "",
    val currentVersionCode: Int = 0,
)

// ----- Thông báo đẩy (FCM) -----

@Serializable
data class RegisterTokenBody(val token: String, val platform: String = "android")

@Serializable
data class PushTokenBody(val token: String)

// ----- Chấm công khuôn mặt (native) -----

@Serializable
data class FaceEngineStatus(
    val engine: String = "",
    val matchThreshold: Double = 0.0,
)

// Loạt ảnh gửi lên để chấm công (server tự chọn khung tốt nhất). selfOnly=true: chỉ chấm cho chính mình.
// previewOnly=true: chỉ nhận diện (chưa ghi log) để hiện form xác nhận.
// confirmToken: token do bước xem trước cấp — bấm Xác nhận chỉ gửi token này, KHÔNG gửi lại ảnh, nên
// server ghi công luôn thay vì chạy lại toàn bộ khâu nhận diện lần thứ hai.
// occurredAt (ISO UTC) khác null = ĐỒNG BỘ NGOẠI TUYẾN: server không ghi thẳng mà tạo bản chờ duyệt,
// kèm gpsLat/gpsLng (nếu có) để kiểm tra geofence.
@Serializable
data class ChamCongBurstRequest(
    val images: List<String> = emptyList(),
    val confirmToken: String? = null,
    val selfOnly: Boolean = true,
    val previewOnly: Boolean = false,
    val occurredAt: String? = null,
    val gpsLat: Double? = null,
    val gpsLng: Double? = null,
    // Liveness QUAY ĐẦU: true = loạt ảnh chụp khi người dùng quay đầu (server kiểm tra biên độ góc quay).
    val motionCheck: Boolean = false,
)

// Cấu hình liveness quay đầu (đọc từ server để biết có yêu cầu quay đầu lúc quét không).
@Serializable
data class MotionConfig(val enabled: Boolean = false, val enforce: Boolean = false)

// Cấu hình yêu cầu cười trước khi thu ảnh; tải từ server ở đầu mỗi lượt chấm công.
@Serializable
data class SmileConfig(val enabled: Boolean = false, val threshold: Double = 0.65)

// Một khung đã chụp kèm nhãn PHA quét soi sáng (slot 0/1/2 = ba pha sáng của màn quét 3 giây; -1 = khung
// tự nhiên ở đuôi). Nhãn này chỉ dùng NỘI BỘ trong app để giới hạn số khung giữ lại mỗi pha — server
// không còn nhận nó nữa (active-flash liveness đã gỡ hẳn ở cả hai phía).
data class CapturedFrame(val image: String, val slot: Int)

// ----- Tự đăng ký khuôn mặt (mỗi tài khoản một lần, nhiều góc) -----
// Một góc quét = nhãn tư thế ("front" | "side1" | "side2") + loạt ảnh của góc đó.
@Serializable
data class FaceEnrollPose(val pose: String, val images: List<String>)

@Serializable
data class SelfFaceEnrollRequest(val poses: List<FaceEnrollPose>)

// Cập nhật ảnh chân dung hồ sơ của chính tài khoản (data URL JPEG; rỗng = xoá ảnh).
@Serializable
data class SaveAvatarBody(val avatar: String?)

// Trạng thái đã đăng ký khuôn mặt của chính tài khoản (để làm mờ nút "Đăng ký khuôn mặt").
@Serializable
data class SelfFaceStatus(
    val registered: Boolean = false,
    val sampleCount: Int = 0,
    val createdAt: String? = null,
    val pending: Boolean = false,
    val requestId: String? = null,
    val requestStatus: String? = null,
    val requestedAt: String? = null,
    val reviewNote: String? = null,
)

@Serializable
data class SelfFaceEnrollResult(
    val message: String = "",
    val sampleCount: Int = 0,
    val status: String = "pending",
    val requestId: String? = null,
)

// Một bản chấm công ngoại tuyến đang chờ đồng bộ (lưu trong hàng đợi trên máy).
@Serializable
data class OfflineAttendanceItem(
    val id: Long,
    val frames: List<String>,
    val occurredAt: String,     // ISO UTC — giờ chấm thật lúc mất mạng
    val gpsLat: Double? = null,
    val gpsLng: Double? = null,
    val tryCount: Int = 0,
)

@Serializable
data class OfflineAttendanceRecord(
    val id: Long = 0,
    val loai: String = "",
    val occurredAt: String = "",
    val syncedAt: String = "",
    val gpsLat: Double? = null,
    val gpsLng: Double? = null,
    val distanceM: Double? = null,
    val inGeofence: Boolean? = null,
    val flags: String = "",
    val status: String = "pending",
    val reviewNote: String = "",
)

@Serializable
data class AttendancePolicy(
    val geofenceLat: Double? = null,
    val geofenceLng: Double? = null,
    val geofenceRadiusM: Double = 300.0,
    val maxBackdateMinutes: Int = 720,
)

@Serializable
data class QrAttendanceBody(val token: String)

// Kết quả chấm công. status: ok | posture | eyesclosed | nosmile | lowquality | noface | spoof | unknown | proxy | expired | error
// previewToken chỉ có ở phản hồi bước xem trước — gửi lại nó khi bấm Xác nhận (thay cho cả loạt ảnh).
@Serializable
data class ChamCongResult(
    val status: String = "",
    val matched: Boolean = false,
    val username: String? = null,
    val fullName: String? = null,
    val similarity: Double = 0.0,
    val loai: String? = null,
    val occurredAt: String? = null,
    val quality: Double = 0.0,
    val message: String = "",
    val guidance: String? = null,
    val previewToken: String? = null,
)

// ---------------- Phiếu chi tiền mặt ----------------
// Kế toán lập phiếu → người nhận quét QR ký nhận → kế toán duyệt chi. Không ký nhận thì không duyệt chi
// được (server chặn), nên mã QR chính là chữ ký điện tử của người nhận.

/** Loại chi (danh mục do quản trị tự thêm/sửa trên web). */
@Serializable
data class PayoutCategory(
    val id: String = "",
    val code: String = "",
    val name: String = "",
    val description: String = "",
    val isActive: Boolean = true,
    val isSystem: Boolean = false,
    val sortOrder: Int = 100,
)

/** Một phiếu chi. `qrValue` chỉ có với tài khoản kế toán — server ẩn với người khác. */
@Serializable
data class PayoutVoucher(
    val id: String = "",
    val voucherNo: String = "",
    val categoryName: String = "",
    val categoryCode: String = "",
    val employeeId: String = "",
    val employeeName: String = "",
    val employeeCode: String = "",
    val amount: Double = 0.0,
    val sourceKind: String = "",
    val sourceNo: String = "",
    val reason: String = "",
    val note: String = "",
    val status: String = "",
    val createdBy: String = "",
    val requiresRecipientConfirmation: Boolean = true,
    val confirmedBy: String = "",
    val confirmedAt: String? = null,
    val approvedBy: String = "",
    val approvedAt: String? = null,
    val paidAt: String? = null,
    val completedBy: String = "",
    val completedAt: String? = null,
    val rejectedBy: String = "",
    val rejectedAt: String? = null,
    val rejectReason: String = "",
    val cancelledBy: String = "",
    val cancelledAt: String? = null,
    val cancelReason: String = "",
    val createdAt: String = "",
    val qrValue: String? = null,
    val qrExpiresAt: String? = null,
)

/** Khoản hoàn tiền phạt đang chờ chi — kế toán chọn là ra đúng số tiền. */
@Serializable
data class PayoutRefundSource(
    val id: String = "",
    val refundNo: String = "",
    val employeeId: String = "",
    val employeeName: String = "",
    val employeeCode: String = "",
    val penaltyNo: String = "",
    val appealRequestNo: String = "",
    val amount: Double = 0.0,
    val reason: String = "",
    val createdAt: String = "",
)

/** Người có thể nhận tiền (mọi phòng ban, không chỉ phòng kế toán). */
@Serializable
data class PayoutRecipient(
    val id: String = "",
    val employeeCode: String = "",
    val fullName: String = "",
    val departmentName: String = "",
)

/**
 * Body lập phiếu. Các id để null sẽ KHÔNG được gửi lên (Json cấu hình explicitNulls=false), nên chọn
 * khoản hoàn thì chỉ gửi sourceKind+sourceId và server tự lấy người nhận + số tiền từ đơn.
 */
@Serializable
data class CreatePayoutBody(
    val sourceKind: String,
    val sourceId: String? = null,
    val categoryId: String? = null,
    val employeeId: String? = null,
    val amount: Double = 0.0,
    val reason: String = "",
    val note: String = "",
    val requiresRecipientConfirmation: Boolean = true,
)

@Serializable data class CreatedPayoutVoucher(val id: String = "", val voucherNo: String = "")
@Serializable data class PayoutQrResponse(val qrValue: String = "", val qrExpiresAt: String = "")
@Serializable data class CancelPayoutBody(val reason: String = "")
@Serializable data class TransitionPayoutBody(val note: String = "")

// ---------------- Lệnh thu tiền khách hàng ----------------
// Không có GPS và địa chỉ. Tài xế xác nhận bảng mệnh giá; kế toán đếm lại trước khi ghi công nợ.

@Serializable
data class CashCollection(
    val id: String = "",
    val orderNo: String = "",
    val customerId: String = "",
    val customerName: String = "",
    val customerPhone: String = "",
    val driverEmployeeId: String = "",
    val driverUsername: String = "",
    val driverName: String = "",
    val expectedAmount: Double = 0.0,
    val scheduledDate: String = "",
    val handoverDueAt: String = "",
    val note: String = "",
    val status: String = "",
    val createdBy: String = "",
    val createdAt: String = "",
    val acceptedAt: String? = null,
    val collectedAt: String? = null,
    val collectedAmount: Double? = null,
    val failureReason: String = "",
    val receivedBy: String = "",
    val receivedAt: String? = null,
    val receivedAmount: Double? = null,
    val paymentId: String? = null,
    val cancelReason: String = "",
    val driverCash: Map<String, Int> = emptyMap(),
    val accountantCash: Map<String, Int> = emptyMap(),
    val overdue: Boolean = false,
    val expectedVariance: Boolean = false,
    val cashVariance: Boolean = false,
    val mine: Boolean = false,
    val canAccept: Boolean = false,
    val canCollect: Boolean = false,
    val canFail: Boolean = false,
    val canReceive: Boolean = false,
    val canCancel: Boolean = false,
    val canResolve: Boolean = false,
)

@Serializable data class CashCollectionDriver(
    val id: String = "", val username: String = "", val name: String = "",
    val employeeCode: String = "", val position: String = "",
)

@Serializable data class CashCollectionCustomer(
    val id: String = "", val name: String = "", val phone: String = "",
)

@Serializable data class CashCountLineBody(val denomination: Long, val quantity: Int)
@Serializable data class CashCountBody(val lines: List<CashCountLineBody>, val reason: String = "")
@Serializable data class CashCollectionReasonBody(val reason: String)
@Serializable data class ResolveCashCollectionBody(val action: String, val reason: String)
@Serializable data class CreatedCashCollection(val id: String = "", val orderNo: String = "")
@Serializable data class CashCollectionResult(val paymentId: String? = null, val amount: Double = 0.0, val collectedAmount: Double = 0.0)
@Serializable data class CreateCashCollectionBody(
    val customerId: String,
    val driverEmployeeId: String,
    val expectedAmount: Double,
    val scheduledDate: String,
    val handoverDueAt: String,
    val note: String = "",
)

// ───────────────────────── Giao việc & nghiệm thu ─────────────────────────
/** Một công việc được giao (khớp WorkTaskDto của backend). */
@Serializable
data class WorkTask(
    val id: String = "",
    val taskNo: String = "",
    val title: String = "",
    val description: String = "",
    val assignerUsername: String = "",
    val assignerName: String = "",
    val assigneeUsername: String = "",
    val assigneeName: String = "",
    val priority: String = "normal",
    val dueAt: String? = null,
    val status: String = "assigned",
    val progress: Int = 0,
    val submitNote: String = "",
    val submittedAt: String? = null,
    val reviewNote: String = "",
    val rating: Int? = null,
    val reviewedAt: String? = null,
    val reviewedBy: String = "",
    val createdAt: String = "",
    val updatedAt: String = "",
    val overdue: Boolean = false,
    /** Có giá trị khi việc sinh từ một phiếu xuất kho được gán cho lái xe. */
    val delivery: WorkTaskDelivery? = null,
)

/**
 * Phần giao hàng của một việc: phiếu xuất kho nào, giao cho khách nào.
 * [collection] có giá trị khi CHÍNH khách đó cũng đang có lệnh thu tiền giao cho lái xe này —
 * máy chủ ghép sẵn để lái xe nhìn một thẻ là biết phải giao hàng VÀ thu tiền.
 */
@Serializable
data class WorkTaskDelivery(
    val documentId: String = "",
    val voucherNo: String = "",
    val customerName: String = "",
    val customerId: String? = null,
    val collection: TaskCollection? = null,
)

/** Lệnh thu tiền hiển thị kèm trong mục "Việc được giao". */
@Serializable
data class TaskCollection(
    val id: String = "",
    val orderNo: String = "",
    val customerId: String = "",
    val customerName: String = "",
    val expectedAmount: Double = 0.0,
    val status: String = "",
    val handoverDueAt: String = "",
)

@Serializable
data class WorkTaskEvent(
    val id: Long = 0,
    val actorUsername: String = "",
    val actorName: String = "",
    val kind: String = "",
    val note: String = "",
    val createdAt: String = "",
)

@Serializable
data class WorkTaskSummary(
    val inbox: Int = 0,
    val inboxActionable: Int = 0,
    val outbox: Int = 0,
    val outboxReview: Int = 0,
    /** Việc giao hàng đã giao xong, đang chờ tờ phiếu về kho (không phải "chờ nghiệm thu"). */
    val outboxAwaitingVoucher: Int = 0,
    val collections: Int = 0,
    val collectionsStandalone: Int = 0,
)

@Serializable
data class WorkTaskListResult(
    val canAssign: Boolean = false,
    val isAdmin: Boolean = false,
    val inbox: List<WorkTask> = emptyList(),
    val outbox: List<WorkTask> = emptyList(),
    /** Lệnh thu tiền KHÔNG gộp được vào việc giao hàng nào; vẫn phải hiện để lái xe không bỏ sót. */
    val collections: List<TaskCollection> = emptyList(),
    val summary: WorkTaskSummary = WorkTaskSummary(),
)

/**
 * Một người có thể nhận việc. Ba trường cuối do MÁY CHỦ tính cho ngày hôm nay:
 *   • [selectable] = false ⇒ chưa chấm công hoặc đang nghỉ phép ⇒ không giao việc được.
 *   • [attendanceNote] là chú thích hiện ngay dưới tên trong bảng chọn.
 * Người không chọn được vẫn nằm trong danh sách (bị làm mờ) — danh sách tự ngắn đi thì người giao
 * lại tưởng nhân viên đã bị xoá tài khoản. Máy chủ chốt lại lần nữa lúc lưu.
 */
@Serializable
data class WorkTaskAssignee(
    val username: String = "",
    val fullName: String = "",
    val position: String = "",
    val department: String = "",
    /** present | absent | leave | sick | trip */
    val attendanceStatus: String = "",
    val attendanceNote: String = "",
    val selectable: Boolean = true,
)

@Serializable
data class WorkTaskMeta(
    val canAssign: Boolean = false,
    val priorities: List<String> = emptyList(),
    val assignees: List<WorkTaskAssignee> = emptyList(),
)

@Serializable
data class WorkTaskFlags(
    val mine: Boolean = false,
    val assignedByMe: Boolean = false,
    val canSubmit: Boolean = false,
    val canStart: Boolean = false,
    val canReview: Boolean = false,
    /** Trả lại chuyến/việc. Việc giao hàng bỏ nghiệm thu nhưng vẫn trả lại được. */
    val canReject: Boolean = false,
    val canEdit: Boolean = false,
    val canCancel: Boolean = false,
)

@Serializable
data class WorkTaskDetailResult(
    val task: WorkTask = WorkTask(),
    val events: List<WorkTaskEvent> = emptyList(),
    val flags: WorkTaskFlags = WorkTaskFlags(),
)

@Serializable
data class CreateTaskBody(
    val title: String,
    val description: String = "",
    val assigneeUsername: String,
    val priority: String = "normal",
    val dueAt: String? = null,
)

@Serializable data class TaskNoteBody(val note: String = "", val progress: Int? = null)
@Serializable data class TaskReviewBody(val note: String = "", val rating: Int? = null)
@Serializable data class CreatedTask(val id: String = "", val taskNo: String = "")

// ───────────────────────── Lịch sử việc đã hoàn thành ─────────────────────────
/** Một người từng hoàn thành việc trong khoảng đang xem — dựng hàng chip lọc theo nhân viên. */
@Serializable
data class WorkTaskHistoryPerson(
    val username: String = "",
    val fullName: String = "",
    val count: Int = 0,
)

/**
 * Kết quả tra lịch sử việc đã hoàn thành trong một khoảng ngày (app lọc theo tuần/tháng).
 * [people] luôn là DANH SÁCH ĐẦY ĐỦ của khoảng đó — không bị co lại khi đang lọc một người —
 * nên chọn nhầm người vẫn quay về người khác được.
 */
@Serializable
data class WorkTaskHistoryResult(
    val from: String = "",
    val to: String = "",
    val isAdmin: Boolean = false,
    val items: List<WorkTask> = emptyList(),
    val people: List<WorkTaskHistoryPerson> = emptyList(),
    val total: Int = 0,
)

// ───────────────────────── Nhật ký một ngày (bảng công / bảng lương) ─────────────────────────
/** Một lần chạm vào công việc trong ngày: được giao, bắt đầu, nộp, nghiệm thu… */
@Serializable
data class DayLogTask(
    val id: String = "",
    val taskNo: String = "",
    val title: String = "",
    val status: String = "",
    val statusLabel: String = "",
    val progress: Int = 0,
    val kind: String = "",
    val kindLabel: String = "",
    val note: String = "",
    val actorName: String = "",
    val assignerName: String = "",
    val assigneeName: String = "",
    val at: String = "",
)

/** Một mốc trong đời một chứng từ (duyệt cấp 1, ký nhận, thực chi…) kèm giờ phút và người làm. */
@Serializable
data class DayLogStep(
    val label: String = "",
    val status: String = "",
    val statusLabel: String = "",
    val at: String? = null,
    val by: String = "",
    val note: String = "",
)

/** Quyết định phạt/kỷ luật ghi cho ngày đang xem. */
@Serializable
data class DayLogPenalty(
    val id: String = "",
    val code: String = "",
    val type: String = "",
    val typeLabel: String = "",
    val penaltyDate: String = "",
    val amount: Double = 0.0,
    val installments: Int = 1,
    val reason: String = "",
    val note: String = "",
    val status: String = "",
    val statusLabel: String = "",
    val createdBy: String = "",
    val at: String = "",
)

/** Đơn tiền bạc (tạm ứng, thanh toán, hoàn ứng, mua sắm) phát sinh trong ngày. */
@Serializable
data class DayLogRequest(
    val id: String = "",
    val code: String = "",
    val type: String = "",
    val typeLabel: String = "",
    val title: String = "",
    val amount: Double = 0.0,
    val status: String = "",
    val statusLabel: String = "",
    val at: String = "",
    val updatedAt: String = "",
    val steps: List<DayLogStep> = emptyList(),
)

/** Phiếu chi tiền mặt kế toán lập cho tôi, kèm đủ mốc lập → ký nhận → duyệt → thực chi. */
@Serializable
data class DayLogPayout(
    val id: String = "",
    val code: String = "",
    val category: String = "",
    val amount: Double = 0.0,
    val status: String = "",
    val statusLabel: String = "",
    val reason: String = "",
    val note: String = "",
    val at: String = "",
    val steps: List<DayLogStep> = emptyList(),
)

/** Toàn bộ những gì xảy ra với tôi trong MỘT ngày: việc, phạt, đơn tiền, phiếu chi. */
@Serializable
data class DayLog(
    val date: String = "",
    val tasks: List<DayLogTask> = emptyList(),
    val penalties: List<DayLogPenalty> = emptyList(),
    val requests: List<DayLogRequest> = emptyList(),
    val payouts: List<DayLogPayout> = emptyList(),
) {
    val isEmpty: Boolean
        get() = tasks.isEmpty() && penalties.isEmpty() && requests.isEmpty() && payouts.isEmpty()
}
