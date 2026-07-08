package com.ketoanapk.hr.data

import android.content.Context
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

/** Điểm truy cập dữ liệu duy nhất cho tầng UI: gọi API và chuẩn hoá lỗi thành [ApiException]. */
class HrRepository(context: Context) {
    private val tokenStore = TokenStore(context.applicationContext)
    private val api: HrApi = ApiClient.create(tokenStore)
    // API chấm công đi RIÊNG qua máy chủ LAN (không qua Internet). Xem ApiClient.createAttendance.
    private val attendanceApi: HrApi = ApiClient.createAttendance(tokenStore)
    private val offlineStore = OfflineAttendanceStore(context.applicationContext)

    suspend fun savedToken(): String? = tokenStore.token()

    suspend fun rememberedUsername(): String = tokenStore.rememberedUsername()

    suspend fun login(username: String, password: String, remember: Boolean): LoginResponse = call {
        val sid = tokenStore.sessionId()
        val result = api.login(LoginRequest(username.trim(), password, sid))
        tokenStore.saveToken(result.token)
        tokenStore.saveRememberedUsername(if (remember) username else "")
        result
    }

    suspend fun resetPasswordWithFace(username: String, newPassword: String, images: List<String>) = callUnit {
        api.forgotPasswordFace(FacePasswordResetRequest(username.trim(), newPassword, images))
    }

    suspend fun me(): HrUser = call { api.me() }

    suspend fun logout() {
        runCatching {
            val sid = tokenStore.sessionId()
            api.logout(SessionPing(sid))
        }
        tokenStore.clearToken()
    }

    suspend fun heartbeat() {
        runCatching {
            val sid = tokenStore.sessionId()
            api.heartbeat(SessionPing(sid))
        }
    }

    suspend fun appConfig(): AppConfig = call { api.appConfig() }
    suspend fun myProfile(): EmployeeDetail = call { api.myProfile() }
    suspend fun myTimesheet(month: String): Timesheet = call { api.myTimesheet(month) }
    suspend fun requests(scope: String, status: String? = null): List<RequestListItem> =
        call { api.requests(scope, status) }
    suspend fun requestTypes(): List<RequestType> = call { api.requestTypes() }
    suspend fun requestDetail(id: String): RequestDetail = call { api.requestDetail(id) }
    suspend fun createRequest(body: CreateRequestBody): CreatedRequest = call { api.createRequest(body) }
    suspend fun penalties(scope: String, month: String? = null): List<Penalty> =
        call { api.penalties(scope, month) }
    suspend fun salaries(): List<SalaryListItem> = call { api.salaries() }
    suspend fun myEstimate(): PayEstimate = call { api.myEstimate() }
    suspend fun managerSummary(date: String, month: String): ManagerSummary =
        call { api.managerSummary(date, month) }
    suspend fun employees(): List<EmployeeCard> = call { api.employees() }
    suspend fun departments(): List<Department> = call { api.departments() }

    suspend fun approveRequest(id: String, comment: String) = callUnit {
        api.approveRequest(id, DecisionBody(comment))
    }

    suspend fun rejectRequest(id: String, comment: String) = callUnit {
        api.rejectRequest(id, DecisionBody(comment))
    }

    suspend fun cancelRequest(id: String) = callUnit { api.cancelRequest(id) }

    // --- Cài đặt tài khoản ---
    suspend fun changePassword(current: String, next: String) = callUnit {
        api.changePassword(ChangePasswordBody(current, next))
    }
    suspend fun devices(): List<DeviceSession> = call { api.devices() }
    suspend fun revokeDevice(sid: String) = callUnit { api.revokeDevice(sid) }
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
     */
    suspend fun downloadRelease(release: ReleaseInfo, target: File) = withContext(Dispatchers.IO) {
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

        val digest = MessageDigest.getInstance("SHA-256")
        var written = 0L
        try {
            body.byteStream().use { input ->
                target.outputStream().use { output ->
                    val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
                    while (true) {
                        val read = input.read(buffer)
                        if (read < 0) break
                        if (read == 0) continue
                        digest.update(buffer, 0, read)
                        output.write(buffer, 0, read)
                        written += read
                    }
                }
            }
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

    suspend fun chamCong(images: List<String>, previewOnly: Boolean = false): ChamCongResult =
        call { attendanceApi.chamCong(ChamCongBurstRequest(images, selfOnly = true, previewOnly = previewOnly)) }

    // --- Tự đăng ký khuôn mặt (đi qua máy chủ LAN như chấm công) ---
    suspend fun myFaceStatus(): SelfFaceStatus = call { attendanceApi.myFaceStatus() }

    suspend fun enrollFace(poses: List<FaceEnrollPose>): SelfFaceEnrollResult =
        call { attendanceApi.enrollFace(SelfFaceEnrollRequest(poses)) }

    // ── Chấm công ngoại tuyến (mất điện/mất mạng) ───────────────────────────────
    /** Lưu tạm lượt chấm vào hàng đợi trên máy để đồng bộ sau. */
    suspend fun saveOfflineAttendance(images: List<String>, occurredAt: String, gpsLat: Double?, gpsLng: Double?) =
        offlineStore.enqueue(images, occurredAt, gpsLat, gpsLng)

    suspend fun offlineCount(): Int = offlineStore.count()

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
