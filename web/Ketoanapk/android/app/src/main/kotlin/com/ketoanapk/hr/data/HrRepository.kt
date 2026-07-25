package com.ketoanapk.hr.data

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import com.ketoanapk.hr.network.ApiClient
import com.ketoanapk.hr.network.ApiException
import com.ketoanapk.hr.network.DecisionBody
import com.ketoanapk.hr.network.HrApi
import com.ketoanapk.hr.network.friendlyMessage
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import retrofit2.HttpException
import java.io.File
import java.io.IOException
import java.security.MessageDigest
import java.util.Locale
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.RequestBody
import okio.BufferedSink
import okio.source

/** Kết quả nhịp tim: phiên còn hợp lệ, đã bị thu hồi (đá/hết hạn), hay chưa rõ (lỗi mạng). */
sealed interface SessionStatus {
    data object Ok : SessionStatus
    /** Máy chủ trả 401 kèm lý do (đăng nhập máy khác / tài khoản bị khoá / quá hạn nhàn rỗi). */
    data class Invalid(val message: String) : SessionStatus
    data object Unknown : SessionStatus
}

/**
 * Số ngày phiên được phép "nhàn rỗi" — không một lần nào chạm được máy chủ — trước khi phải đăng
 * nhập lại. PHẢI khớp `Security:SessionIdleDays` của backend (Program.cs): máy chủ chặn bằng 401 khi
 * thiết bị online trở lại, còn hằng số này chặn ngay trên máy cho thiết bị suốt ngày không có mạng.
 */
const val SESSION_IDLE_DAYS = 7
private const val SESSION_IDLE_MS = SESSION_IDLE_DAYS * 24L * 60 * 60 * 1000

/**
 * Phiên đã quá hạn nhàn rỗi chưa? [lastOnlineAt] = 0 nghĩa là chưa có mốc → chưa hết hạn.
 *
 * Đồng hồ máy chạy lùi cho hiệu số âm → KHÔNG hết hạn, để không đá nhầm người vừa chỉnh lại giờ máy.
 * Cố tình lùi giờ để né hạn cũng chẳng được gì: chỉ xem được dữ liệu đã lưu khi ngoại tuyến, hễ có
 * mạng là máy chủ chặn bằng 401 theo `last_seen` phía nó.
 */
internal fun sessionIdleExpired(lastOnlineAt: Long, now: Long): Boolean =
    lastOnlineAt > 0L && now - lastOnlineAt > SESSION_IDLE_MS

/**
 * Kết quả hỏi máy chủ về một mã QR vừa quét. Tách [Offline] khỏi [Rejected] vì hai trường hợp này phải
 * xử lý ngược nhau: mất mạng thì app cứ đọc nội dung mã cho người dùng xem, còn máy chủ từ chối
 * (khoá tài khoản, mã sai, quá nhiều lần quét) thì phải báo đúng lý do chứ không được lờ đi mà tự đọc.
 */
sealed interface QrResolveOutcome {
    /** Máy chủ nhận ra mã và trả về giao diện + hành động của nghiệp vụ tương ứng. */
    data class Handled(val envelope: QrActionEnvelope) : QrResolveOutcome
    /** Máy chủ tiếp nhận nhưng không có nghiệp vụ nào cho mã → app tự đọc nội dung. */
    data object Unhandled : QrResolveOutcome
    /** Không hỏi được máy chủ → app tự đọc nội dung, kèm ghi chú là chưa kiểm tra được nghiệp vụ. */
    data object Offline : QrResolveOutcome
    /** Máy chủ trả lỗi (401/403/400/quá nhiều yêu cầu) → chỉ báo lý do, KHÔNG tự đọc. */
    data class Rejected(val message: String) : QrResolveOutcome
}

/** Kết quả khôi phục phiên lúc mở app. */
sealed interface SessionRestore {
    /** Máy chủ xác nhận phiên còn sống. */
    data class Online(val user: HrUser) : SessionRestore
    /** Không hỏi được máy chủ (mất mạng) → vào app bằng hồ sơ đã lưu. */
    data class Offline(val user: HrUser) : SessionRestore
    /** Phải đăng nhập lại; [message] = null nghĩa là chưa từng đăng nhập (không có gì để báo). */
    data class SignedOut(val message: String? = null) : SessionRestore
}

/** Khóa retry ổn định cho đúng một bản ghi; backend dùng nó để upsert thay vì tạo tin/push trùng. */
internal fun voiceClientMessageId(conversationId: String, fileName: String, fileSize: Long): String {
    val bytes = "$conversationId\u0000$fileName\u0000$fileSize".toByteArray(Charsets.UTF_8)
    val digest = MessageDigest.getInstance("SHA-256").digest(bytes)
    return "android-voice:" + digest.joinToString("") { "%02x".format(it) }
}

/** Điểm truy cập dữ liệu duy nhất cho tầng UI: gọi API và chuẩn hoá lỗi thành [ApiException]. */
/**
 * @param background repo của tác vụ NỀN (WorkManager poll) — đánh dấu request để máy chủ không tính
 *   là người dùng đang hoạt động, nếu không phiên sẽ không bao giờ hết hạn nhàn rỗi. Xem [ApiClient].
 */
class HrRepository(context: Context, background: Boolean = false) {
    companion object {
        @Volatile
        private var foregroundInstance: HrRepository? = null

        /**
         * Một repository dùng chung cho các màn hình foreground. Retrofit/OkHttp và connection pool
         * tương đối nặng; dùng chung tránh tạo thêm cả API chính lẫn API chấm công mỗi lần mở camera QR.
         * Repository chỉ giữ application context và các dependency bên trong đều hỗ trợ đa luồng.
         */
        fun foreground(context: Context): HrRepository {
            foregroundInstance?.let { return it }
            return synchronized(this) {
                foregroundInstance ?: HrRepository(context.applicationContext).also {
                    foregroundInstance = it
                }
            }
        }
    }

    private val tokenStore = TokenStore(context.applicationContext)
    private val api: HrApi = ApiClient.create(tokenStore, background)
    // API chấm công đi RIÊNG qua máy chủ LAN (không qua Internet). Xem ApiClient.createAttendance.
    private val attendanceApi: HrApi = ApiClient.createAttendance(tokenStore)
    private val offlineStore = OfflineAttendanceStore(context.applicationContext)
    private val chatCache = ChatCacheStore(context.applicationContext)
    private val homeCache = HomeCacheStore(context.applicationContext)
    @Volatile private var chatFromCache = false
    fun chatUsingCache(): Boolean = chatFromCache

    /** Ảnh chụp Trang chủ của lần mở trước (chỉ trả về khi đúng tài khoản). Xem [HomeCacheStore]. */
    suspend fun loadHomeSnapshot(username: String): HomeSnapshot? = homeCache.load(username)

    /** Ghi ảnh chụp Trang chủ để lần mở app sau hiện ngay dữ liệu, không phải chờ mạng. */
    suspend fun saveHomeSnapshot(snapshot: HomeSnapshot) = homeCache.save(snapshot)

    suspend fun savedToken(): String? = tokenStore.token()

    suspend fun rememberedUsername(): String = tokenStore.rememberedUsername()

    suspend fun login(username: String, password: String, remember: Boolean): LoginResponse = call {
        val sid = tokenStore.sessionId()
        val result = api.login(LoginRequest(username.trim(), password, sid))
        tokenStore.saveToken(result.token)
        tokenStore.saveRememberedUsername(if (remember) username else "")
        tokenStore.saveCachedUser(result.user) // để lần mở app sau vào được dù đang mất mạng
        tokenStore.touchOnline()
        result
    }

    suspend fun resetPasswordWithCode(username: String, code: String, newPassword: String) = callUnit {
        api.resetWithRecoveryCode(RecoveryResetRequest(username.trim(), code.trim(), newPassword))
    }

    // Không dùng call{} vì nó gộp mọi lỗi thành ApiException, mất thông tin "mất mạng hay bị từ chối" —
    // đúng thứ quyết định app có được tự đọc nội dung mã hay không.
    suspend fun resolveQr(value: String): QrResolveOutcome =
        try {
            val envelope = api.resolveQr(
                QrResolveBody(value.trim(), clientVersionCode = com.ketoanapk.hr.BuildConfig.VERSION_CODE),
            )
            if (envelope.unhandled) QrResolveOutcome.Unhandled else QrResolveOutcome.Handled(envelope)
        } catch (e: HttpException) {
            QrResolveOutcome.Rejected(e.friendlyMessage())
        } catch (_: IOException) {
            QrResolveOutcome.Offline
        }

    suspend fun decideQr(decisionToken: String, actionId: String): QrActionEnvelope = call {
        api.decideQr(QrDecisionBody(decisionToken, actionId))
    }

    suspend fun resolveMobileAppLogin(requestCode: String): MobileAppLoginChallenge = call {
        api.resolveMobileAppLogin(MobileAppLoginCodeBody(requestCode.trim()))
    }

    suspend fun confirmMobileAppLogin(requestCode: String): String = call {
        api.confirmMobileAppLogin(MobileAppLoginCodeBody(requestCode.trim())).message
    }

    suspend fun rejectMobileAppLogin(requestCode: String) = callUnit {
        api.rejectMobileAppLogin(MobileAppLoginCodeBody(requestCode.trim()))
    }

    suspend fun me(): HrUser = call { api.me() }

    suspend fun logout() {
        runCatching {
            val sid = tokenStore.sessionId()
            api.logout(SessionPing(sid))
        }
        clearLocalSession()
    }

    /** Xoá phiên trên máy, KHÔNG gọi máy chủ (dùng khi máy chủ đã từ chối phiên, hoặc đang offline). */
    private suspend fun clearLocalSession() {
        tokenStore.clearToken()
        chatCache.clear()
        homeCache.clear() // ảnh chụp Trang chủ chứa hồ sơ/lương → phải xoá khi phiên kết thúc/bị thu hồi
    }

    /**
     * Khôi phục phiên lúc mở app. Quy tắc: **chỉ 401 mới đăng xuất**. Mất mạng/máy chủ lỗi thì giữ
     * nguyên phiên và vào app bằng hồ sơ đã lưu — trừ khi đã quá [SESSION_IDLE_DAYS] ngày không lần
     * nào chạm được máy chủ.
     */
    suspend fun restoreSession(): SessionRestore {
        val token = tokenStore.token()
        if (token.isNullOrBlank()) return SessionRestore.SignedOut()

        val lastOnline = tokenStore.lastOnlineAt()
        if (lastOnline <= 0L) {
            // Phiên có từ bản app cũ (chưa ghi mốc) → tính mốc nhàn rỗi từ bây giờ.
            tokenStore.touchOnline()
        } else if (sessionIdleExpired(lastOnline, System.currentTimeMillis())) {
            clearLocalSession()
            return SessionRestore.SignedOut(
                "Đã quá $SESSION_IDLE_DAYS ngày không đăng nhập. Vui lòng đăng nhập lại."
            )
        }

        return try {
            val user = api.me()
            tokenStore.saveCachedUser(user)
            tokenStore.touchOnline()
            SessionRestore.Online(user)
        } catch (e: HttpException) {
            // 401 = phiên đã hỏng thật (bị đá / khoá / quá hạn) → đăng xuất. Lỗi máy chủ khác
            // (500/503, tunnel chết) KHÔNG phải lỗi của người dùng nên vẫn giữ phiên.
            if (e.code() == 401) {
                val message = e.friendlyMessage()
                clearLocalSession()
                SessionRestore.SignedOut(message)
            } else restoreFromCache(e.friendlyMessage())
        } catch (e: Exception) {
            restoreFromCache("Không kết nối được máy chủ. Kiểm tra mạng rồi mở lại ứng dụng.")
        }
    }

    /** Chưa có hồ sơ lưu (vừa cập nhật app) → GIỮ token, chỉ hiện màn đăng nhập kèm lý do. */
    private suspend fun restoreFromCache(message: String): SessionRestore =
        tokenStore.cachedUser()?.let { SessionRestore.Offline(it) } ?: SessionRestore.SignedOut(message)

    /** Nhịp tim + kiểm tra phiên: [SessionStatus.Invalid] khi server trả 401 (phiên bị thu hồi/hết hạn). */
    suspend fun heartbeat(): SessionStatus = try {
        api.heartbeat(SessionPing(tokenStore.sessionId()))
        tokenStore.touchOnline() // máy chủ xác nhận phiên còn sống → lùi mốc hết hạn nhàn rỗi
        SessionStatus.Ok
    } catch (e: HttpException) {
        if (e.code() == 401) SessionStatus.Invalid(e.friendlyMessage()) else SessionStatus.Ok
    } catch (e: Exception) {
        SessionStatus.Unknown // lỗi mạng → KHÔNG đá người dùng ra (fail-open)
    }

    suspend fun appConfig(): AppConfig = call { api.appConfig() }
    suspend fun myProfile(): EmployeeDetail = call { api.myProfile() }
    suspend fun anniversaryGreeting(preview: Boolean = false): AnniversaryGreeting =
        call { api.anniversaryGreeting(preview) }
    suspend fun employeeDetail(id:String)=call{api.employeeDetail(id)}
    suspend fun updateEmployee(id:String,body:SaveEmployeeBody)=callUnit{api.updateEmployee(id,body)}
    suspend fun updateSalary(id:String,body:SaveSalaryBody)=callUnit{api.updateSalary(id,body)}
    suspend fun myDocuments(): List<EmployeeDocument> = call { api.myDocuments() }

    suspend fun uploadMyDocument(context: Context, uri: Uri?, type: String, title: String, number: String, expiresAt: String, issuedBy: String) = callUnit {
        val resolver = context.contentResolver
        var name = ""
        var size = 0L
        if (uri != null) resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { c ->
            if (c.moveToFirst()) { c.getColumnIndex(OpenableColumns.DISPLAY_NAME).takeIf { it>=0 }?.let { name=c.getString(it) ?: "" }; c.getColumnIndex(OpenableColumns.SIZE).takeIf { it>=0 }?.let { size=c.getLong(it) } }
        }
        val mime = uri?.let { resolver.getType(it) } ?: "application/octet-stream"
        val body = object : RequestBody() {
            override fun contentType() = mime.toMediaTypeOrNull()
            override fun contentLength() = size
            override fun writeTo(sink: BufferedSink) { if (uri != null) resolver.openInputStream(uri)?.use { sink.writeAll(it.source()) } }
        }
        api.uploadMyDocument(type,title,number.ifBlank { null },expiresAt.ifBlank { null },issuedBy.ifBlank { null },name,body)
    }
    suspend fun onboarding()=call{api.onboarding()}; suspend fun completeOnboarding(id:String)=callUnit{api.completeOnboarding(id)}
    suspend fun performance()=call{api.performance()}; suspend fun updateGoal(id:String,p:Double)=callUnit{api.updateGoal(id,ProgressBody(p))}
    suspend fun selfReview(id:String,text:String)=callUnit{api.selfReview(id,SelfReviewBody(text))}
    suspend fun training()=call{api.training()}; suspend fun trainingProgress(id:String,p:Int,s:Int)=callUnit{api.trainingProgress(id,TrainingProgressBody(p,s))}
    suspend fun submitQuiz(id:String,a:List<String>)=call{api.submitQuiz(id,QuizBody(a))}
    suspend fun benefits()=call{api.benefits()}
    suspend fun updateMyAvatar(avatar: String?) = callUnit { api.updateMyAvatar(SaveAvatarBody(avatar)) }
    suspend fun myTimesheet(month: String): Timesheet = call { api.myTimesheet(month) }
    suspend fun requests(scope: String, status: String? = null): List<RequestListItem> =
        call { api.requests(scope, status) }
    suspend fun requestTypes(): List<RequestType> = call { api.requestTypes() }
    suspend fun requestDetail(id: String): RequestDetail = call { api.requestDetail(id) }
    suspend fun createRequest(body: CreateRequestBody): CreatedRequest = call { api.createRequest(body) }
    // ---- Giao việc & nghiệm thu ----
    suspend fun workTasks(): WorkTaskListResult = call { api.workTasks() }
    suspend fun workTaskMeta(): WorkTaskMeta = call { api.workTaskMeta() }
    suspend fun workTaskDetail(id: String): WorkTaskDetailResult = call { api.workTaskDetail(id) }
    suspend fun createWorkTask(body: CreateTaskBody): CreatedTask = call { api.createWorkTask(body) }
    suspend fun updateWorkTask(id: String, body: CreateTaskBody) = callUnit { api.updateWorkTask(id, body) }
    suspend fun startWorkTask(id: String) = callUnit { api.startWorkTask(id) }
    suspend fun progressWorkTask(id: String, progress: Int, note: String) = callUnit { api.progressWorkTask(id, TaskNoteBody(note, progress)) }
    suspend fun submitWorkTask(id: String, note: String) = callUnit { api.submitWorkTask(id, TaskNoteBody(note)) }
    suspend fun acceptWorkTask(id: String, note: String, rating: Int?) = callUnit { api.acceptWorkTask(id, TaskReviewBody(note, rating)) }
    suspend fun rejectWorkTask(id: String, note: String) = callUnit { api.rejectWorkTask(id, TaskReviewBody(note)) }
    suspend fun cancelWorkTask(id: String, note: String) = callUnit { api.cancelWorkTask(id, TaskNoteBody(note)) }
    suspend fun commentWorkTask(id: String, note: String) = callUnit { api.commentWorkTask(id, TaskNoteBody(note)) }
    suspend fun deleteWorkTask(id: String) = callUnit { api.deleteWorkTask(id) }

    suspend fun penalties(scope: String, month: String? = null): List<Penalty> =
        call { api.penalties(scope, month) }
    suspend fun salaries(): List<SalaryListItem> = call { api.salaries() }
    suspend fun myEstimate(): PayEstimate = call { api.myEstimate() }
    // ---- Phiếu chi tiền mặt ----
    suspend fun payoutVouchers(scope: String): List<PayoutVoucher> = call { api.payoutVouchers(scope) }
    suspend fun payoutCategories(): List<PayoutCategory> = call { api.payoutCategories() }
    suspend fun payoutRefundSources(): List<PayoutRefundSource> = call { api.payoutRefundSources() }
    suspend fun payoutRecipients(): List<PayoutRecipient> = call { api.payoutRecipients() }
    suspend fun createPayoutVoucher(body: CreatePayoutBody): CreatedPayoutVoucher = call { api.createPayoutVoucher(body) }
    suspend fun refreshPayoutQr(id: String): PayoutQrResponse = call { api.refreshPayoutQr(id) }
    suspend fun approvePayoutVoucher(id: String) = callUnit { api.approvePayoutVoucher(id) }
    suspend fun cancelPayoutVoucher(id: String, reason: String) = callUnit { api.cancelPayoutVoucher(id, CancelPayoutBody(reason)) }

    suspend fun myPayslips(): List<PayslipItem> = call { api.myPayslips() }
    suspend fun acknowledgePayslip(id:String)=callUnit{api.acknowledgePayslip(id)}
    suspend fun payslipInquiry(id:String,line:String,message:String)=callUnit{api.payslipInquiry(id,PayslipInquiryBody(line,message))}
    suspend fun downloadPayslipPdf(context:Context,item:PayslipItem):File=withContext(Dispatchers.IO){val response=api.payslipPdf(item.id);if(!response.isSuccessful)throw HttpException(response);val dir=File(context.cacheDir,"payslips").apply{mkdirs()};val file=File(dir,"Payslip_${item.period}.pdf");response.body()?.byteStream()?.use{input->file.outputStream().use{input.copyTo(it)}}?:throw IOException("Phản hồi PDF rỗng");file}
    suspend fun managerSummary(date: String, month: String): ManagerSummary =
        call { api.managerSummary(date, month) }
    suspend fun managerAttendance(date:String,status:String?,departmentId:String?)=call{api.managerAttendance(date,status,departmentId)}
    suspend fun employees(): List<EmployeeCard> = call { api.employees() }
    suspend fun departments(): List<Department> = call { api.departments() }
    suspend fun openSurveys()=call{api.openSurveys()};suspend fun answerSurvey(id:String,a:kotlinx.serialization.json.JsonObject)=callUnit{api.answerSurvey(id,SurveyResponseBody(a))};suspend fun sendGeneralFeedback(m:String,a:Boolean)=callUnit{api.sendGeneralFeedback(GeneralFeedbackBody(m,a))};suspend fun myGeneralFeedback()=call{api.myGeneralFeedback()}
    suspend fun createSupportTicket(message:String)=callUnit{api.createSupportTicket(SupportTicketBody(message,com.ketoanapk.hr.BuildConfig.VERSION_NAME,"${android.os.Build.MANUFACTURER} ${android.os.Build.MODEL}"))};suspend fun mySupportTickets()=call{api.mySupportTickets()}
    suspend fun audit(take: Int, skip: Int, search: String?, entity: String?): List<AuditEntry> =
        call { api.audit(take, skip, search?.trim()?.ifBlank { null }, entity?.ifBlank { null }) }
    suspend fun portalFeed(): PortalFeed = call { api.portalFeed() }

    suspend fun approveRequest(id: String, comment: String) = callUnit {
        api.approveRequest(id, DecisionBody(comment))
    }

    suspend fun rejectRequest(id: String, comment: String) = callUnit {
        api.rejectRequest(id, DecisionBody(comment))
    }

    suspend fun cancelRequest(id: String) = callUnit { api.cancelRequest(id) }
    suspend fun remindRequest(id: String) = callUnit { api.remindRequest(id) }
    suspend fun updateRequest(id: String, body: CreateRequestBody) = callUnit { api.updateRequest(id, body) }

    suspend fun uploadRequestAttachment(context: Context, requestId: String, uri: Uri): RequestAttachment = call {
        val resolver = context.contentResolver
        var name = "dinh-kem"
        var size = -1L
        resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { c ->
            if (c.moveToFirst()) {
                c.getColumnIndex(OpenableColumns.DISPLAY_NAME).takeIf { it >= 0 }?.let { name = c.getString(it) ?: name }
                c.getColumnIndex(OpenableColumns.SIZE).takeIf { it >= 0 }?.let { size = c.getLong(it) }
            }
        }
        val mime = resolver.getType(uri) ?: "application/octet-stream"
        val body = object : RequestBody() {
            override fun contentType() = mime.toMediaTypeOrNull()
            override fun contentLength() = size
            override fun writeTo(sink: BufferedSink) {
                resolver.openInputStream(uri)?.use { sink.writeAll(it.source()) } ?: throw java.io.IOException("Không mở được tệp đính kèm.")
            }
        }
        api.uploadRequestAttachment(requestId, name, body)
    }

    // --- Cài đặt tài khoản ---
    suspend fun changePassword(current: String, next: String) = callUnit {
        api.changePassword(ChangePasswordBody(current, next))
    }
    suspend fun verifyPassword(password: String) = callUnit {
        api.verifyPassword(VerifyPasswordBody(password))
    }
    suspend fun devices(): List<DeviceSession> = call { api.devices() }
    suspend fun revokeDevice(sid: String) = callUnit { api.revokeDevice(sid) }
    suspend fun revokeAllDevices()=callUnit{api.revokeAllDevices()}
    suspend fun accountSettings(): AccountLoginSettings = call { api.accountSettings() }
    suspend fun setWebLoginEnabled(enabled: Boolean): AccountLoginSettings =
        call { api.updateAccountSettings(AccountLoginSettings(enabled)) }

    // --- Cài đặt thông báo push trên thiết bị ---
    suspend fun pushNotificationsEnabled(): Boolean = tokenStore.pushNotificationsEnabled()
    suspend fun setPushNotificationsEnabled(enabled: Boolean) {
        tokenStore.setPushNotificationsEnabled(enabled)
    }

    // --- Thông báo đẩy (FCM): tốt-nhất-có-thể, không chặn luồng chính nếu lỗi ---
    suspend fun registerPushToken(token: String) {
        runCatching { callUnit { api.registerPushToken(RegisterTokenBody(token)) } }
    }

    suspend fun unregisterPushToken(token: String) {
        runCatching { callUnit { api.unregisterPushToken(PushTokenBody(token)) } }
    }

    // --- Cập nhật ứng dụng ---
    suspend fun latestRelease(currentVersionCode: Int): ReleaseInfo =
        call { api.latestRelease("hr-apk", currentVersionCode) }

    /**
     * Tải APK về [target] qua đúng OkHttp/TLS + token của app (giống mọi request khác đang chạy tốt),
     * đồng thời kiểm tra dung lượng và SHA-256. Ném [ApiException] với thông điệp rõ ràng khi lỗi.
     *
     * [onProgress] báo (đã tải, tổng) để màn hình vẽ thanh tiến độ. Gói cập nhật cỡ ~90 MB nên nếu
     * không báo tiến độ thì người dùng chỉ thấy app "đứng hình" vài phút và tưởng hỏng. Chỉ gọi lại
     * mỗi 200 ms (hoặc khi xong) để không làm Compose recompose liên tục theo từng buffer 8 KB.
     */
    suspend fun downloadRelease(
        release: ReleaseInfo,
        target: File,
        onProgress: (downloaded: Long, total: Long) -> Unit = { _, _ -> },
    ) = withContext(Dispatchers.IO) {
        val response = try {
            api.downloadApk(release.downloadUrl)
        } catch (e: HttpException) {
            throw ApiException(e.friendlyMessage())
        } catch (e: IOException) {
            throw ApiException("Không kết nối được máy chủ để tải bản cập nhật. Kiểm tra mạng LAN.")
        }
        if (!response.isSuccessful) {
            throw ApiException("Máy chủ trả lỗi ${response.code()} khi tải bản cập nhật.")
        }
        val body = response.body() ?: throw ApiException("Máy chủ không trả về dữ liệu bản cập nhật.")

        // Tổng dung lượng: ưu tiên số máy chủ đã ghi trong bản phát hành, không có thì lấy Content-Length.
        val total = if (release.apkSize > 0) release.apkSize else body.contentLength().coerceAtLeast(0L)
        val digest = MessageDigest.getInstance("SHA-256")
        var written = 0L
        try {
            body.byteStream().use { input ->
                target.outputStream().use { output ->
                    val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
                    var lastReport = 0L
                    onProgress(0L, total)
                    while (true) {
                        val read = input.read(buffer)
                        if (read < 0) break
                        if (read == 0) continue
                        digest.update(buffer, 0, read)
                        output.write(buffer, 0, read)
                        written += read
                        val now = System.currentTimeMillis()
                        if (now - lastReport >= 200L) {
                            lastReport = now
                            onProgress(written, total)
                        }
                    }
                }
            }
            onProgress(written, total)
        } catch (e: IOException) {
            target.delete()
            throw ApiException("Tải bản cập nhật bị gián đoạn: ${e.message ?: "lỗi mạng"}.")
        }

        if (release.apkSize > 0 && written != release.apkSize) {
            target.delete()
            throw ApiException("File cập nhật tải về không đủ dung lượng.")
        }
        val expected = release.apkSha256.lowercase(Locale.US)
        if (expected.isNotBlank()) {
            val actual = digest.digest().joinToString("") { "%02x".format(it.toInt() and 0xff) }
            if (actual != expected) {
                target.delete()
                throw ApiException("File cập nhật không khớp mã kiểm tra (SHA-256).")
            }
        }
    }

    // --- Chấm công: kiểm tra kết nối máy chủ LAN qua trạng thái engine khuôn mặt ---
    // Dùng attendanceApi → gọi thẳng máy chủ LAN, nên đây cũng là phép thử "có trong mạng LAN không".
    suspend fun faceEngineStatus(): FaceEngineStatus = call { attendanceApi.faceEngineStatus() }

    suspend fun chamCong(
        images: List<String>,
        previewOnly: Boolean = false,
        motionCheck: Boolean = false,
    ): ChamCongResult =
        call {
            attendanceApi.chamCong(
                ChamCongBurstRequest(
                    images = images,
                    selfOnly = true,
                    previewOnly = previewOnly,
                    motionCheck = motionCheck,
                ),
            )
        }

    /**
     * Ghi công theo token đã cấp ở bước xem trước — KHÔNG gửi lại ảnh. Server bỏ qua toàn bộ khâu nhận
     * diện (đã chạy vài giây trước ở bước xem trước) nên lượt xác nhận gần như tức thì và không tốn thêm
     * suy luận. Token dùng một lần, sống 2 phút, ràng buộc theo tài khoản đang đăng nhập.
     */
    suspend fun chamCongXacNhan(previewToken: String): ChamCongResult =
        call { attendanceApi.chamCong(ChamCongBurstRequest(confirmToken = previewToken)) }

    // Đọc cấu hình liveness quay đầu (có yêu cầu quay đầu lúc quét không).
    suspend fun motionConfig(): MotionConfig = call { attendanceApi.motionConfig() }

    // --- Tự đăng ký khuôn mặt (đi qua máy chủ LAN như chấm công) ---
    suspend fun myFaceStatus(): SelfFaceStatus = call { attendanceApi.myFaceStatus() }

    suspend fun enrollFace(poses: List<FaceEnrollPose>): SelfFaceEnrollResult =
        call { attendanceApi.enrollFace(SelfFaceEnrollRequest(poses)) }

    // ── Chấm công ngoại tuyến (mất điện/mất mạng) ───────────────────────────────
    /** Lưu tạm lượt chấm vào hàng đợi trên máy để đồng bộ sau. */
    suspend fun saveOfflineAttendance(images: List<String>, occurredAt: String, gpsLat: Double?, gpsLng: Double?) =
        offlineStore.enqueue(images, occurredAt, gpsLat, gpsLng)

    suspend fun offlineCount(): Int = offlineStore.count()
    suspend fun offlineItems(): List<OfflineAttendanceItem> = offlineStore.all()
    suspend fun myOfflineAttendance(): List<OfflineAttendanceRecord> = call { attendanceApi.myOfflineAttendance() }
    suspend fun attendancePolicy(): AttendancePolicy = call { attendanceApi.attendancePolicy() }
    suspend fun qrAttendance(token: String): ChamCongResult = call { attendanceApi.qrAttendance(QrAttendanceBody(token)) }

    /**
     * Đồng bộ hàng đợi ngoại tuyến: gửi từng bản kèm occurredAt + GPS. Server xử lý xong (2xx) → xóa
     * khỏi hàng đợi (server tạo bản chờ duyệt). Mất mạng giữa chừng → dừng, giữ phần còn lại. Trả về
     * số bản đã đồng bộ thành công.
     */
    suspend fun syncOfflineAttendance(): Int {
        var synced = 0
        for (item in offlineStore.all()) {
            val ok = runCatching {
                attendanceApi.chamCong(
                    ChamCongBurstRequest(
                        images = item.frames,
                        selfOnly = true,
                        previewOnly = false,
                        occurredAt = item.occurredAt,
                        gpsLat = item.gpsLat,
                        gpsLng = item.gpsLng,
                    ),
                )
            }
            if (ok.isSuccess) {
                offlineStore.remove(item.id)
                synced++
            } else {
                // Lỗi mạng → dừng, thử lại lần sau. (Lỗi khác cũng dừng để không mất dữ liệu.)
                break
            }
        }
        return synced
    }

    /** Danh bạ gọi nội bộ (đồng nghiệp + trạng thái online) để chọn người gọi. */
    suspend fun callContacts(search: String?): List<CallContact> = call {
        api.chatContacts(search?.trim()?.takeIf { it.isNotBlank() })
    }

    suspend fun chatConversations(): List<ChatConversation> = try {
        val items = call { api.chatConversations() }
        chatFromCache = false
        chatCache.saveConversations(items)
        items
    } catch (error: Exception) {
        chatCache.load().conversations.ifEmpty { throw error }.also { chatFromCache = true }
    }
    suspend fun openDirectConversation(username: String): ChatConversationId = call { api.openDirectConversation(username) }
    suspend fun chatMessages(id: String, beforeId: Long? = null, search: String? = null): List<ChatMessage> = try {
        val normalizedSearch = search?.trim()?.ifBlank { null }
        val items = call { api.chatMessages(id, beforeId, 50, normalizedSearch) }
        chatFromCache = false
        if (beforeId == null && normalizedSearch == null) chatCache.saveMessages(id, items)
        items
    } catch (error: Exception) {
        val cached = chatCache.load().messages[id].orEmpty()
            .filter { search.isNullOrBlank() || it.body.contains(search, true) || it.fileName.orEmpty().contains(search, true) }
        if (cached.isEmpty()) throw error else cached.also { chatFromCache = true }
    }
    suspend fun sendChatMessage(id: String, text: String, forwarded: Boolean = false): ChatMessage = call {
        api.sendChatMessage(id, SendChatMessageBody(text.trim(), forwarded))
    }
    suspend fun editChatMessage(id: String, messageId: Long, text: String) = callUnit {
        api.editChatMessage(id, messageId, EditChatMessageBody(text.trim()))
    }
    suspend fun deleteChatMessage(id: String, messageId: Long) = callUnit { api.deleteChatMessage(id, messageId) }
    suspend fun reactChatMessage(id: String, messageId: Long, emoji: String) = callUnit {
        api.reactChatMessage(id, messageId, ReactChatMessageBody(emoji))
    }
    suspend fun pinChatConversation(id: String, pinned: Boolean) = callUnit {
        api.pinChatConversation(id, PinChatConversationBody(pinned))
    }

    suspend fun sendChatFile(context: Context, conversationId: String, uri: Uri, kind: String = "file"): ChatMessage = withContext(Dispatchers.IO) {
        call {
            val resolver = context.contentResolver
            var name = "tep-dinh-kem"
            var size = -1L
            resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { cursor ->
                if (cursor.moveToFirst()) {
                    cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME).takeIf { it >= 0 }?.let { name = cursor.getString(it) ?: name }
                    cursor.getColumnIndex(OpenableColumns.SIZE).takeIf { it >= 0 }?.let { size = cursor.getLong(it) }
                }
            }
            val mime = resolver.getType(uri) ?: "application/octet-stream"
            val normalizedKind = if (kind.equals("voice", ignoreCase = true)) "voice" else "file"
            val clientMessageId = if (normalizedKind == "voice") voiceClientMessageId(conversationId, name, size) else null
            val metadata = api.createChatFileMessage(
                conversationId,
                SendChatFileBody(name.take(260), size.coerceAtLeast(0), mime, normalizedKind, clientMessageId),
            )
            if (!metadata.hasBlob) {
                val body = object : RequestBody() {
                    override fun contentType() = mime.toMediaTypeOrNull()
                    override fun contentLength(): Long = size
                    override fun writeTo(sink: BufferedSink) {
                        resolver.openInputStream(uri)?.use { input -> sink.writeAll(input.source()) }
                            ?: throw java.io.IOException("Không mở được tệp đã chọn.")
                    }
                }
                val response = api.uploadChatFile(conversationId, metadata.id, body)
                if (!response.isSuccessful) throw HttpException(response)
            }
            val ready = metadata.copy(hasBlob = true)
            // Người gửi cũng phát lại từ cùng cache key theo messageId; cache lỗi không được biến một lần
            // upload server đã thành công thành trạng thái "gửi thất bại".
            runCatching { cacheChatFileFromUri(context, uri, ready) }
            ready
        }
    }

    suspend fun downloadChatFile(context: Context, conversationId: String, message: ChatMessage): File = withContext(Dispatchers.IO) {
        val target = chatFileCacheTarget(context, message)
        if (isValidChatFileCache(target, message)) return@withContext target
        if (target.exists()) target.delete()

        val response = try { api.downloadChatFile(conversationId, message.id) }
        catch (e: Exception) { throw ApiException("Không tải được tệp: ${e.message ?: "lỗi mạng"}.") }
        if (!response.isSuccessful) throw ApiException(HttpException(response).friendlyMessage())
        val body = response.body() ?: throw ApiException("Máy chủ không trả nội dung tệp.")
        val partial = File(target.parentFile, "${target.name}.${System.nanoTime()}.part")
        try {
            body.byteStream().use { input -> partial.outputStream().use { output -> input.copyTo(output) } }
            val expected = body.contentLength()
            if (partial.length() <= 0L || (expected > 0L && partial.length() != expected))
                throw java.io.IOException("Nội dung tải về không đầy đủ.")
            replaceAtomically(partial, target)
            target
        } catch (e: Exception) {
            partial.delete()
            throw ApiException("Tải tệp bị gián đoạn: ${e.message ?: "lỗi đọc dữ liệu"}.")
        }
    }

    private fun chatFileCacheTarget(context: Context, message: ChatMessage): File {
        val safeName = message.fileName.orEmpty().ifBlank { "tep-${message.id}" }
            .replace(Regex("[^A-Za-z0-9._-]"), "_").take(180)
        val dir = File(context.cacheDir, "chat").apply { mkdirs() }
        return File(dir, "${message.id}-$safeName")
    }

    private fun isValidChatFileCache(file: File, message: ChatMessage): Boolean {
        if (!file.isFile || file.length() <= 0L) return false
        val expected = message.fileSize ?: 0L
        return expected <= 0L || file.length() == expected
    }

    private fun cacheChatFileFromUri(context: Context, uri: Uri, message: ChatMessage) {
        val target = chatFileCacheTarget(context, message)
        if (isValidChatFileCache(target, message)) return
        val partial = File(target.parentFile, "${target.name}.${System.nanoTime()}.part")
        try {
            context.contentResolver.openInputStream(uri)?.use { input ->
                partial.outputStream().use { output -> input.copyTo(output) }
            } ?: throw java.io.IOException("Không đọc được tệp vừa gửi.")
            if (!isValidChatFileCache(partial, message)) throw java.io.IOException("Bản cache tệp vừa gửi không đầy đủ.")
            replaceAtomically(partial, target)
        } finally {
            partial.delete()
        }
    }

    private fun replaceAtomically(source: File, target: File) {
        try {
            java.nio.file.Files.move(
                source.toPath(), target.toPath(),
                java.nio.file.StandardCopyOption.ATOMIC_MOVE,
                java.nio.file.StandardCopyOption.REPLACE_EXISTING,
            )
        } catch (_: java.nio.file.AtomicMoveNotSupportedException) {
            java.nio.file.Files.move(source.toPath(), target.toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING)
        }
    }

    /** Đổ chuông cuộc gọi qua FCM (best-effort — không chặn cuộc gọi nếu lỗi mạng). */
    suspend fun ringCall(toUser: String, callId: String, media: String) {
        runCatching { api.ringCall(com.ketoanapk.hr.network.CallRingBody(toUser, callId, media)) }
    }

    /** Hủy chuông đã đẩy (người gọi cúp trước khi người kia bắt máy). */
    suspend fun cancelCallRing(toUser: String, callId: String) {
        runCatching { api.cancelCallRing(com.ketoanapk.hr.network.CallCancelBody(toUser, callId)) }
    }

    /** Báo cuộc gọi NHỠ (người kia không bắt máy/không online) — lưu DB + gửi thông báo TTL dài. */
    suspend fun missedCall(toUser: String, callId: String, media: String) {
        runCatching { api.missedCall(com.ketoanapk.hr.network.CallMissedBody(toUser, callId, media)) }
    }

    /** Lấy cuộc gọi nhỡ chưa xem (từ DB) — dùng khi mở app để hiện dù trước đó offline. */
    suspend fun fetchMissedCalls(): List<MissedCall> =
        runCatching { api.missedCalls() }.getOrDefault(emptyList())

    /**
     * Lấy TURN credential ĐỘNG (có hạn giờ) để gọi được khi 2 máy khác mạng. Backend cấp qua HMAC —
     * KHÔNG có mật khẩu tĩnh trong app. Trả null nếu server chưa cấu hình TURN (app tự lùi về STUN).
     */
    suspend fun fetchTurnCreds(): CallManager.TurnCreds? = runCatching {
        val r = api.turnCredentials()
        if (r.urls.isEmpty() || r.username.isBlank()) null
        else CallManager.TurnCreds(r.urls, r.username, r.credential)
    }.getOrNull()

    /** Đánh dấu đã xem hết cuộc gọi nhỡ. */
    suspend fun markMissedCallsSeen() {
        runCatching { api.markMissedCallsSeen() }
    }

    suspend fun recordCall(session: CallManager.Session, reason: String, endedAt: Long) {
        runCatching {
            api.recordCall(
                RecordCallBody(
                    peerUsername = session.peerUsername,
                    peerName = session.peerName,
                    callId = session.callId,
                    media = session.media.name.lowercase(),
                    direction = if (session.incoming) "incoming" else "outgoing",
                    outcome = reason,
                    startedAtEpochMs = session.startedAt,
                    endedAtEpochMs = endedAt,
                ),
            )
        }
    }

    suspend fun callHistory(): List<CallHistoryItem> = call { api.callHistory() }

    private suspend fun <T> call(block: suspend () -> T): T =
        try {
            block()
        } catch (e: HttpException) {
            throw ApiException(e.friendlyMessage())
        } catch (e: IOException) {
            throw ApiException("Không kết nối được máy chủ. Kiểm tra mạng LAN và địa chỉ máy chủ.")
        }

    private suspend fun callUnit(block: suspend () -> retrofit2.Response<Unit>) {
        val response = try {
            block()
        } catch (e: HttpException) {
            throw ApiException(e.friendlyMessage())
        } catch (e: IOException) {
            throw ApiException("Không kết nối được máy chủ.")
        }
        if (!response.isSuccessful) {
            throw ApiException(HttpException(response).friendlyMessage())
        }
    }
}
