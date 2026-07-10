package com.ketoanapk.hr.data

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonObject

/**
 * Các model dữ liệu ánh xạ 1-1 với JSON của backend KetoanMini (đặt tên trường theo camelCase
 * trùng khóa JSON). Mọi trường đều có giá trị mặc định để giải mã an toàn khi thiếu/null.
 */

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
) {
    val isAdmin: Boolean get() = role.equals("admin", ignoreCase = true)
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

@Serializable
data class FacePasswordResetRequest(
    val username: String,
    val newPassword: String,
    val images: List<String>,
    val client: String = "apk",
)

@Serializable
data class LoginResponse(
    val token: String = "",
    val user: HrUser = HrUser(),
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
    val status: String = "Active",
    val phone: String = "",
    val email: String = "",
    val avatar: String? = null,
    val departmentId: String? = null,
    val departmentName: String = "",
    val managerName: String = "",
    val dob: String? = null,
    val gender: String = "",
    val address: String = "",
    val managerId: String? = null,
    val hireDate: String? = null,
    val isAccounting: Boolean = false,
)

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
    val earnings: List<PayLine> = emptyList(),
    val deductions: List<PayLine> = emptyList(),
    val totalEarnings: Double = 0.0,
    val totalDeductions: Double = 0.0,
    val netPay: Double = 0.0,
    val note: String = "",
    val createdAt: String = "",
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
// previewOnly=true: chỉ nhận diện (chưa ghi log) để hiện form xác nhận; bấm Xác nhận mới gửi lại previewOnly=false.
// occurredAt (ISO UTC) khác null = ĐỒNG BỘ NGOẠI TUYẾN: server không ghi thẳng mà tạo bản chờ duyệt,
// kèm gpsLat/gpsLng (nếu có) để kiểm tra geofence.
@Serializable
data class ChamCongBurstRequest(
    val images: List<String>,
    val selfOnly: Boolean = true,
    val previewOnly: Boolean = false,
    val occurredAt: String? = null,
    val gpsLat: Double? = null,
    val gpsLng: Double? = null,
    // Active-flash liveness (đã ngừng dùng — giữ để tương thích): challengeId + slotIndices.
    val challengeId: String? = null,
    val slotIndices: List<Int>? = null,
    // Liveness QUAY ĐẦU: true = loạt ảnh chụp khi người dùng quay đầu (server kiểm tra biên độ góc quay).
    val motionCheck: Boolean = false,
)

// Cấu hình liveness quay đầu (đọc từ server để biết có yêu cầu quay đầu lúc quét không).
@Serializable
data class MotionConfig(val enabled: Boolean = false, val enforce: Boolean = false)

// ----- Active-flash liveness (chống giả mạo chủ động bằng ánh sáng màn hình) -----
// Server phát chuỗi màu ngẫu nhiên; app hiển thị full màn hình từng màu theo slotMs (chờ settleMs cho
// màn hình + camera ổn định) rồi gắn nhãn slot cho từng khung ảnh gửi lên để server đối chiếu phản xạ.
@Serializable
data class FlashChallenge(
    val challengeId: String = "",
    val slots: List<FlashSlot> = emptyList(),
    val slotMs: Int = 420,
    val settleMs: Int = 150,
)

@Serializable
data class FlashSlot(val index: Int = 0, val color: String = "#FFFFFF")

// Một khung đã chụp kèm nhãn slot màu đang chiếu (slot = -1 khi không chạy flash liveness).
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
)

@Serializable
data class SelfFaceEnrollResult(
    val message: String = "",
    val sampleCount: Int = 0,
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

// Kết quả chấm công. status: ok | posture | lowquality | noface | spoof | unknown | proxy | error
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
)
