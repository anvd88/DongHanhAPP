package com.ketoanapk.hr.network

import com.ketoanapk.hr.data.AccountLoginSettings
import com.ketoanapk.hr.data.AuditEntry
import com.ketoanapk.hr.data.AppConfig
import com.ketoanapk.hr.data.ChamCongBurstRequest
import com.ketoanapk.hr.data.ChamCongResult
import com.ketoanapk.hr.data.ChangePasswordBody
import com.ketoanapk.hr.data.CreateRequestBody
import com.ketoanapk.hr.data.CreatedRequest
import com.ketoanapk.hr.data.Department
import com.ketoanapk.hr.data.DeviceSession
import com.ketoanapk.hr.data.EmployeeCard
import com.ketoanapk.hr.data.EmployeeDetail
import com.ketoanapk.hr.data.EmployeeDocument
import com.ketoanapk.hr.data.OnboardingSummary
import com.ketoanapk.hr.data.PerformanceSummary
import com.ketoanapk.hr.data.TrainingCourse
import com.ketoanapk.hr.data.ProgressBody
import com.ketoanapk.hr.data.SelfReviewBody
import com.ketoanapk.hr.data.TrainingProgressBody
import com.ketoanapk.hr.data.QuizBody
import com.ketoanapk.hr.data.QuizResult
import com.ketoanapk.hr.data.BenefitsSummary
import com.ketoanapk.hr.data.FaceEngineStatus
import com.ketoanapk.hr.data.RecoveryResetRequest
import com.ketoanapk.hr.data.RecoveryVerifyRequest
import com.ketoanapk.hr.data.MotionConfig
import com.ketoanapk.hr.data.SmileConfig
import com.ketoanapk.hr.data.OfflineAttendanceRecord
import com.ketoanapk.hr.data.AttendancePolicy
import com.ketoanapk.hr.data.QrAttendanceBody
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.JobPosition
import com.ketoanapk.hr.data.LoginRequest
import com.ketoanapk.hr.data.LoginResponse
import com.ketoanapk.hr.data.MobileAppLoginChallenge
import com.ketoanapk.hr.data.MobileAppLoginCodeBody
import com.ketoanapk.hr.data.MobileAppLoginMessage
import com.ketoanapk.hr.data.QrActionEnvelope
import com.ketoanapk.hr.data.QrDecisionBody
import com.ketoanapk.hr.data.QrResolveBody
import com.ketoanapk.hr.data.ManagerSummary
import com.ketoanapk.hr.data.Penalty
import com.ketoanapk.hr.data.PushTokenBody
import com.ketoanapk.hr.data.RegisterTokenBody
import com.ketoanapk.hr.data.ReleaseInfo
import com.ketoanapk.hr.data.RequestDetail
import com.ketoanapk.hr.data.RequestAttachment
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.RequestType
import com.ketoanapk.hr.data.SalaryListItem
import com.ketoanapk.hr.data.SaveAvatarBody
import com.ketoanapk.hr.data.SelfFaceEnrollRequest
import com.ketoanapk.hr.data.SelfFaceEnrollResult
import com.ketoanapk.hr.data.SelfFaceStatus
import com.ketoanapk.hr.data.NotificationGroupSettings
import com.ketoanapk.hr.data.ServerNotificationFeed
import com.ketoanapk.hr.data.SessionPing
import com.ketoanapk.hr.data.Timesheet
import com.ketoanapk.hr.data.AppPinSetBody
import com.ketoanapk.hr.data.AppPinStatus
import com.ketoanapk.hr.data.AppPinVerifyBody
import com.ketoanapk.hr.data.AppPinResetBody
import okhttp3.ResponseBody
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.Headers
import retrofit2.http.Header
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Path
import retrofit2.http.Query
import retrofit2.http.Streaming
import retrofit2.http.Url

/** Khai báo các endpoint REST của backend KetoanMini mà app native sử dụng. */
interface HrApi {
    @POST("api/auth/login")
    suspend fun login(@Body body: LoginRequest): LoginResponse

    @POST("api/auth/verify-recovery-code")
    suspend fun verifyRecoveryCode(@Body body: RecoveryVerifyRequest): retrofit2.Response<Unit>

    @POST("api/auth/reset-with-recovery-code")
    suspend fun resetWithRecoveryCode(@Body body: RecoveryResetRequest): retrofit2.Response<Unit>

    @POST("api/qr/resolve")
    suspend fun resolveQr(@Body body: QrResolveBody): QrActionEnvelope

    @POST("api/qr/decision")
    suspend fun decideQr(@Body body: QrDecisionBody): QrActionEnvelope

    @POST("api/auth/app-login/resolve")
    suspend fun resolveMobileAppLogin(@Body body: MobileAppLoginCodeBody): MobileAppLoginChallenge

    @POST("api/auth/app-login/confirm")
    suspend fun confirmMobileAppLogin(@Body body: MobileAppLoginCodeBody): MobileAppLoginMessage

    @POST("api/auth/app-login/reject")
    suspend fun rejectMobileAppLogin(@Body body: MobileAppLoginCodeBody): retrofit2.Response<Unit>

    @GET("api/auth/me")
    suspend fun me(): HrUser

    @GET("api/app-config")
    suspend fun appConfig(): AppConfig

    @POST("api/auth/heartbeat")
    suspend fun heartbeat(@Body body: SessionPing): retrofit2.Response<Unit>

    @POST("api/auth/logout")
    suspend fun logout(@Body body: SessionPing): retrofit2.Response<Unit>

    @GET("api/hr/me")
    suspend fun myProfile(): EmployeeDetail
    @GET("api/hr/employees/{id}") suspend fun employeeDetail(@Path("id") id:String):EmployeeDetail
    @GET("api/hr/job-positions") suspend fun jobPositions():List<JobPosition>
    @PUT("api/hr/employees/{id}") suspend fun updateEmployee(@Path("id") id:String,@Body body:com.ketoanapk.hr.data.SaveEmployeeBody):retrofit2.Response<Unit>
    @PUT("api/payroll/salaries/{id}") suspend fun updateSalary(@Path("id") id:String,@Body body:com.ketoanapk.hr.data.SaveSalaryBody):retrofit2.Response<Unit>

    // Thư tri ân "tròn X năm gắn bó": server tự tính mốc theo ngày vào làm, trả thư đã điền sẵn.
    @GET("api/hr/anniversary/my-greeting")
    suspend fun anniversaryGreeting(@Query("preview") preview: Boolean = false): com.ketoanapk.hr.data.AnniversaryGreeting

    @GET("api/hr/me/documents")
    suspend fun myDocuments(): List<EmployeeDocument>

    @POST("api/hr/me/documents")
    suspend fun uploadMyDocument(@Query("docType") type: String, @Query("title") title: String,
        @Query("docNumber") number: String?, @Query("expiresAt") expiresAt: String?, @Query("issuedBy") issuedBy: String?,
        @Header("X-File-Name") fileName: String,
        @Body body: okhttp3.RequestBody): retrofit2.Response<Unit>

    @GET("api/talent/onboarding") suspend fun onboarding(): OnboardingSummary
    @POST("api/talent/onboarding/{id}/complete") suspend fun completeOnboarding(@Path("id") id:String): retrofit2.Response<Unit>
    @GET("api/talent/performance") suspend fun performance(): PerformanceSummary
    @PUT("api/talent/performance/goals/{id}") suspend fun updateGoal(@Path("id") id:String,@Body body:ProgressBody): retrofit2.Response<Unit>
    @PUT("api/talent/performance/reviews/{id}/self") suspend fun selfReview(@Path("id") id:String,@Body body:SelfReviewBody): retrofit2.Response<Unit>
    @GET("api/talent/training") suspend fun training(): List<TrainingCourse>
    @PUT("api/talent/training/{id}/progress") suspend fun trainingProgress(@Path("id") id:String,@Body body:TrainingProgressBody): retrofit2.Response<Unit>
    @POST("api/talent/training/{id}/quiz") suspend fun submitQuiz(@Path("id") id:String,@Body body:QuizBody): QuizResult
    @GET("api/talent/benefits") suspend fun benefits():BenefitsSummary

    @PUT("api/hr/me/avatar")
    suspend fun updateMyAvatar(@Body body: SaveAvatarBody): retrofit2.Response<Unit>

    @GET("api/timesheet/me")
    suspend fun myTimesheet(@Query("month") month: String): Timesheet

    @GET("api/requests")
    suspend fun requests(
        @Query("scope") scope: String,
        @Query("status") status: String? = null,
    ): List<RequestListItem>

    @GET("api/requests/types")
    suspend fun requestTypes(): List<RequestType>

    @GET("api/requests/{id}")
    suspend fun requestDetail(@Path("id") id: String): RequestDetail

    @POST("api/requests")
    suspend fun createRequest(@Body body: CreateRequestBody): CreatedRequest

    @PUT("api/requests/{id}")
    suspend fun updateRequest(@Path("id") id: String, @Body body: CreateRequestBody): retrofit2.Response<Unit>

    @POST("api/requests/{id}/attachments")
    suspend fun uploadRequestAttachment(@Path("id") id: String, @Query("fileName") fileName: String, @Body body: okhttp3.RequestBody): RequestAttachment

    @POST("api/requests/{id}/approve")
    suspend fun approveRequest(@Path("id") id: String, @Body body: DecisionBody): retrofit2.Response<Unit>

    @POST("api/requests/{id}/reject")
    suspend fun rejectRequest(@Path("id") id: String, @Body body: DecisionBody): retrofit2.Response<Unit>

    @POST("api/requests/{id}/cancel")
    suspend fun cancelRequest(@Path("id") id: String): retrofit2.Response<Unit>

    @POST("api/requests/{id}/remind")
    suspend fun remindRequest(@Path("id") id: String): retrofit2.Response<Unit>

    // ---- Giao việc & nghiệm thu ----
    // activeOnly=true: hết ngày thì việc đã xong của hôm qua rụng khỏi màn hình, việc chưa xong ở lại.
    @GET("api/tasks")
    suspend fun workTasks(@Query("activeOnly") activeOnly: Boolean = true): com.ketoanapk.hr.data.WorkTaskListResult
    @GET("api/tasks/history")
    suspend fun workTaskHistory(
        @Query("from") from: String,
        @Query("to") to: String,
        @Query("assignee") assignee: String? = null,
    ): com.ketoanapk.hr.data.WorkTaskHistoryResult
    @GET("api/tasks/meta")
    suspend fun workTaskMeta(): com.ketoanapk.hr.data.WorkTaskMeta
    @GET("api/tasks/{id}")
    suspend fun workTaskDetail(@Path("id") id: String): com.ketoanapk.hr.data.WorkTaskDetailResult
    @POST("api/tasks")
    suspend fun createWorkTask(@Body body: com.ketoanapk.hr.data.CreateTaskBody): com.ketoanapk.hr.data.CreatedTask
    @PUT("api/tasks/{id}")
    suspend fun updateWorkTask(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.CreateTaskBody): retrofit2.Response<Unit>
    @POST("api/tasks/{id}/start")
    suspend fun startWorkTask(@Path("id") id: String): retrofit2.Response<Unit>
    @POST("api/tasks/{id}/progress")
    suspend fun progressWorkTask(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.TaskNoteBody): retrofit2.Response<Unit>
    @POST("api/tasks/{id}/submit")
    suspend fun submitWorkTask(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.TaskNoteBody): retrofit2.Response<Unit>
    @POST("api/tasks/{id}/accept")
    suspend fun acceptWorkTask(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.TaskReviewBody): retrofit2.Response<Unit>
    @POST("api/tasks/{id}/reject")
    suspend fun rejectWorkTask(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.TaskReviewBody): retrofit2.Response<Unit>
    @POST("api/tasks/{id}/cancel")
    suspend fun cancelWorkTask(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.TaskNoteBody): retrofit2.Response<Unit>
    @POST("api/tasks/{id}/comment")
    suspend fun commentWorkTask(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.TaskNoteBody): retrofit2.Response<Unit>
    @DELETE("api/tasks/{id}")
    suspend fun deleteWorkTask(@Path("id") id: String): retrofit2.Response<Unit>

    @GET("api/penalties")
    suspend fun penalties(
        @Query("scope") scope: String,
        @Query("month") month: String? = null,
    ): List<Penalty>

    // ---- Phiếu chi tiền mặt ----
    // scope=mine: phiếu của chính tôi (mọi nhân viên). scope=all: cả sổ chi (chỉ kế toán/admin).
    @GET("api/payout-vouchers")
    suspend fun payoutVouchers(@Query("scope") scope: String): List<com.ketoanapk.hr.data.PayoutVoucher>

    @GET("api/payout-vouchers/categories")
    suspend fun payoutCategories(): List<com.ketoanapk.hr.data.PayoutCategory>

    @GET("api/payout-vouchers/sources/refunds")
    suspend fun payoutRefundSources(): List<com.ketoanapk.hr.data.PayoutRefundSource>

    @GET("api/payout-vouchers/recipients")
    suspend fun payoutRecipients(): List<com.ketoanapk.hr.data.PayoutRecipient>

    @POST("api/payout-vouchers")
    suspend fun createPayoutVoucher(@Body body: com.ketoanapk.hr.data.CreatePayoutBody): com.ketoanapk.hr.data.CreatedPayoutVoucher

    @POST("api/payout-vouchers/{id}/qr")
    suspend fun refreshPayoutQr(@Path("id") id: String): com.ketoanapk.hr.data.PayoutQrResponse

    @POST("api/payout-vouchers/{id}/approve")
    suspend fun approvePayoutVoucher(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.TransitionPayoutBody): retrofit2.Response<Unit>

    @POST("api/payout-vouchers/{id}/complete")
    suspend fun completePayoutVoucher(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.TransitionPayoutBody): retrofit2.Response<Unit>

    @POST("api/payout-vouchers/{id}/reject")
    suspend fun rejectPayoutVoucher(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.CancelPayoutBody): retrofit2.Response<Unit>

    @POST("api/payout-vouchers/{id}/cancel")
    suspend fun cancelPayoutVoucher(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.CancelPayoutBody): retrofit2.Response<Unit>

    // ---- Lệnh thu tiền khách hàng ----
    @GET("api/cash-collections")
    suspend fun cashCollections(@Query("scope") scope: String): List<com.ketoanapk.hr.data.CashCollection>

    @GET("api/cash-collections/drivers")
    suspend fun cashCollectionDrivers(): List<com.ketoanapk.hr.data.CashCollectionDriver>

    @GET("api/cash-collections/customers")
    suspend fun accountingCustomers(): List<com.ketoanapk.hr.data.CashCollectionCustomer>

    @POST("api/cash-collections")
    suspend fun createCashCollection(@Body body: com.ketoanapk.hr.data.CreateCashCollectionBody): com.ketoanapk.hr.data.CreatedCashCollection

    @POST("api/cash-collections/{id}/accept")
    suspend fun acceptCashCollection(@Path("id") id: String): retrofit2.Response<Unit>

    @POST("api/cash-collections/{id}/fail")
    suspend fun failCashCollection(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.CashCollectionReasonBody): retrofit2.Response<Unit>

    @POST("api/cash-collections/{id}/collect")
    suspend fun collectCashCollection(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.CashCountBody): com.ketoanapk.hr.data.CashCollectionResult

    @POST("api/cash-collections/{id}/receive")
    suspend fun receiveCashCollection(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.CashCountBody): com.ketoanapk.hr.data.CashCollectionResult

    @POST("api/cash-collections/{id}/cancel")
    suspend fun cancelCashCollection(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.CashCollectionReasonBody): retrofit2.Response<Unit>

    @POST("api/cash-collections/{id}/resolve")
    suspend fun resolveCashCollection(@Path("id") id: String, @Body body: com.ketoanapk.hr.data.ResolveCashCollectionBody): com.ketoanapk.hr.data.CashCollectionResult

    @GET("api/payroll/salaries")
    suspend fun salaries(): List<SalaryListItem>

    @GET("api/payroll/my-estimate")
    suspend fun myEstimate(): com.ketoanapk.hr.data.PayEstimate

    // Nhật ký một ngày: việc đã làm + phạt/kỷ luật + đơn tiền + phiếu chi của chính mình.
    @GET("api/payroll/my-day")
    suspend fun myDayLog(@Query("date") date: String): com.ketoanapk.hr.data.DayLog

    @GET("api/payroll/my-payslips")
    suspend fun myPayslips(): List<com.ketoanapk.hr.data.PayslipItem>
    @GET("api/payroll/my-payslips/requirement")
    suspend fun payslipRequirement(): com.ketoanapk.hr.data.PayslipRequirement
    @POST("api/payroll/my-payslips/{id}/ack")
    suspend fun acknowledgePayslip(
        @Path("id") id: String,
        @Query("expectedRevision") expectedRevision: String?,
    ): retrofit2.Response<Unit>
    @POST("api/payroll/my-payslips/{id}/inquiries") suspend fun payslipInquiry(@Path("id") id:String,@Body body:com.ketoanapk.hr.data.PayslipInquiryBody):retrofit2.Response<Unit>
    @Streaming @GET("api/payroll/my-payslips/{id}/pdf") suspend fun payslipPdf(@Path("id") id:String):retrofit2.Response<ResponseBody>

    @GET("api/hr/manager/summary")
    suspend fun managerSummary(
        @Query("date") date: String,
        @Query("month") month: String,
    ): ManagerSummary
    @GET("api/hr/manager/attendance") suspend fun managerAttendance(@Query("date") date:String,@Query("status") status:String?,@Query("departmentId") departmentId:String?):List<com.ketoanapk.hr.data.ManagerAttendanceItem>

    @GET("api/hr/employees")
    suspend fun employees(): List<EmployeeCard>

    @GET("api/hr/departments")
    suspend fun departments(): List<Department>
    @GET("api/feedback/surveys/open") suspend fun openSurveys():List<com.ketoanapk.hr.data.SurveyItem>
    @POST("api/feedback/surveys/{id}/responses") suspend fun answerSurvey(@Path("id")id:String,@Body body:com.ketoanapk.hr.data.SurveyResponseBody):retrofit2.Response<Unit>
    @POST("api/feedback/general") suspend fun sendGeneralFeedback(@Body body:com.ketoanapk.hr.data.GeneralFeedbackBody):retrofit2.Response<Unit>
    @GET("api/feedback/general/mine") suspend fun myGeneralFeedback():List<com.ketoanapk.hr.data.GeneralFeedbackItem>
    @POST("api/feedback/support") suspend fun createSupportTicket(@Body body:com.ketoanapk.hr.data.SupportTicketBody):retrofit2.Response<Unit>
    @GET("api/feedback/support/mine") suspend fun mySupportTickets():List<com.ketoanapk.hr.data.SupportTicketItem>

    @GET("api/audit")
    suspend fun audit(
        @Query("take") take: Int,
        @Query("skip") skip: Int = 0,
        @Query("search") search: String? = null,
        @Query("entity") entity: String? = null,
    ): List<AuditEntry>

    // --- Cổng thông tin công ty (tin tức, sự kiện, giới thiệu) ---
    @GET("api/portal/feed")
    suspend fun portalFeed(): com.ketoanapk.hr.data.PortalFeed

    // --- Cài đặt tài khoản ---
    @POST("api/auth/change-password")
    suspend fun changePassword(@Body body: ChangePasswordBody): retrofit2.Response<Unit>

    // --- Mã bảo mật 6 số của ứng dụng (lưu Ở MÁY CHỦ, thiết bị không giữ bản sao) ---
    @GET("api/auth/app-pin")
    suspend fun appPinStatus(): AppPinStatus

    /** Tạo mã lần đầu (currentPin = null) hoặc đổi mã (phải kèm mã cũ). */
    @POST("api/auth/app-pin")
    suspend fun setAppPin(@Body body: AppPinSetBody): retrofit2.Response<Unit>

    @POST("api/auth/app-pin/verify")
    suspend fun verifyAppPin(@Body body: AppPinVerifyBody): retrofit2.Response<Unit>

    /** Quên mã: máy chủ xác minh mật khẩu tài khoản rồi xoá mã cũ trong cùng một lượt. */
    @POST("api/auth/app-pin/reset")
    suspend fun resetAppPin(@Body body: AppPinResetBody): retrofit2.Response<Unit>

    @GET("api/auth/devices")
    suspend fun devices(): List<DeviceSession>

    @POST("api/auth/devices/{sid}/revoke")
    suspend fun revokeDevice(@Path("sid") sid: String): retrofit2.Response<Unit>
    @POST("api/auth/devices/revoke-all") suspend fun revokeAllDevices():retrofit2.Response<Unit>

    @GET("api/auth/account-settings")
    suspend fun accountSettings(): AccountLoginSettings

    @PUT("api/auth/account-settings")
    suspend fun updateAccountSettings(@Body body: AccountLoginSettings): AccountLoginSettings

    // --- Thông báo đẩy (FCM) ---
    @POST("api/notifications/register-token")
    suspend fun registerPushToken(@Body body: RegisterTokenBody): retrofit2.Response<Unit>

    @POST("api/notifications/unregister-token")
    suspend fun unregisterPushToken(@Body body: PushTokenBody): retrofit2.Response<Unit>

    // --- Hộp thư thông báo trên máy chủ (dùng chung với chuông trên web) ---
    @GET("api/notifications")
    suspend fun notificationFeed(@Query("limit") limit: Int = 50): ServerNotificationFeed

    @POST("api/notifications/{id}/read")
    suspend fun markNotificationRead(@retrofit2.http.Path("id") id: Long): retrofit2.Response<Unit>

    @POST("api/notifications/read-all")
    suspend fun markAllNotificationsRead(): retrofit2.Response<Unit>

    // --- Nhóm thông báo được nhận (dùng chung web + app) ---
    @GET("api/preferences/notifications")
    suspend fun notificationGroups(): NotificationGroupSettings

    @PUT("api/preferences/notifications")
    suspend fun updateNotificationGroups(@Body body: NotificationGroupSettings): NotificationGroupSettings

    // --- Cập nhật ứng dụng ---
    @GET("api/releases/latest")
    suspend fun latestRelease(
        @Query("appTarget") appTarget: String,
        @Query("currentVersionCode") currentVersionCode: Int,
    ): ReleaseInfo

    // Tải APK theo dòng (streaming) qua đúng OkHttp/TLS + token của app.
    @Streaming
    @GET
    suspend fun downloadApk(@Url url: String): retrofit2.Response<ResponseBody>

    // --- Chấm công khuôn mặt (trạng thái engine = kiểm tra kết nối máy chủ LAN) ---
    @GET("api/chamcong/trangthai")
    suspend fun faceEngineStatus(): FaceEngineStatus

    @POST("api/chamcong/cham")
    suspend fun chamCong(@Body body: ChamCongBurstRequest): ChamCongResult

    // Liveness quay đầu: đọc cấu hình để biết có yêu cầu quay đầu lúc quét không.
    @GET("api/chamcong/motion-config")
    suspend fun motionConfig(): MotionConfig

    @GET("api/chamcong/smile-config")
    suspend fun smileConfig(): SmileConfig

    @GET("api/chamcong/offline/mine")
    suspend fun myOfflineAttendance(): List<OfflineAttendanceRecord>

    @GET("api/chamcong/offline-policy")
    suspend fun attendancePolicy(): AttendancePolicy

    @POST("api/chamcong/qr")
    suspend fun qrAttendance(@Body body: QrAttendanceBody): ChamCongResult

    @GET("api/directory")
    suspend fun directoryContacts(@Query("search") search: String? = null): List<com.ketoanapk.hr.data.DirectoryContact>

    // --- Tự đăng ký khuôn mặt (mỗi tài khoản một lần, nhiều góc) ---
    @GET("api/chamcong/dangky/cua-toi")
    suspend fun myFaceStatus(): SelfFaceStatus

    @POST("api/chamcong/dangky/tu")
    suspend fun enrollFace(@Body body: SelfFaceEnrollRequest): SelfFaceEnrollResult
}

@kotlinx.serialization.Serializable
data class DecisionBody(val comment: String = "")
