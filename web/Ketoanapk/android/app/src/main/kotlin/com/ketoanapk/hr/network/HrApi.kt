package com.ketoanapk.hr.network

import com.ketoanapk.hr.data.AccountLoginSettings
import com.ketoanapk.hr.data.ChamCongBurstRequest
import com.ketoanapk.hr.data.ChamCongResult
import com.ketoanapk.hr.data.ChangePasswordBody
import com.ketoanapk.hr.data.Department
import com.ketoanapk.hr.data.DeviceSession
import com.ketoanapk.hr.data.EmployeeCard
import com.ketoanapk.hr.data.EmployeeDetail
import com.ketoanapk.hr.data.FaceEngineStatus
import com.ketoanapk.hr.data.FacePasswordResetRequest
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.LoginRequest
import com.ketoanapk.hr.data.LoginResponse
import com.ketoanapk.hr.data.ManagerSummary
import com.ketoanapk.hr.data.Penalty
import com.ketoanapk.hr.data.PushTokenBody
import com.ketoanapk.hr.data.RegisterTokenBody
import com.ketoanapk.hr.data.ReleaseInfo
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.RequestType
import com.ketoanapk.hr.data.SalaryListItem
import com.ketoanapk.hr.data.SessionPing
import com.ketoanapk.hr.data.Timesheet
import okhttp3.ResponseBody
import retrofit2.http.Body
import retrofit2.http.GET
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

    @POST("api/auth/forgot-password-face")
    suspend fun forgotPasswordFace(@Body body: FacePasswordResetRequest): retrofit2.Response<Unit>

    @GET("api/auth/me")
    suspend fun me(): HrUser

    @POST("api/auth/heartbeat")
    suspend fun heartbeat(@Body body: SessionPing): retrofit2.Response<Unit>

    @POST("api/auth/logout")
    suspend fun logout(@Body body: SessionPing): retrofit2.Response<Unit>

    @GET("api/hr/me")
    suspend fun myProfile(): EmployeeDetail

    @GET("api/timesheet/me")
    suspend fun myTimesheet(@Query("month") month: String): Timesheet

    @GET("api/requests")
    suspend fun requests(
        @Query("scope") scope: String,
        @Query("status") status: String? = null,
    ): List<RequestListItem>

    @GET("api/requests/types")
    suspend fun requestTypes(): List<RequestType>

    @POST("api/requests/{id}/approve")
    suspend fun approveRequest(@Path("id") id: String, @Body body: DecisionBody): retrofit2.Response<Unit>

    @POST("api/requests/{id}/reject")
    suspend fun rejectRequest(@Path("id") id: String, @Body body: DecisionBody): retrofit2.Response<Unit>

    @POST("api/requests/{id}/cancel")
    suspend fun cancelRequest(@Path("id") id: String): retrofit2.Response<Unit>

    @GET("api/penalties")
    suspend fun penalties(
        @Query("scope") scope: String,
        @Query("month") month: String? = null,
    ): List<Penalty>

    @GET("api/payroll/salaries")
    suspend fun salaries(): List<SalaryListItem>

    @GET("api/hr/manager/summary")
    suspend fun managerSummary(
        @Query("date") date: String,
        @Query("month") month: String,
    ): ManagerSummary

    @GET("api/hr/employees")
    suspend fun employees(): List<EmployeeCard>

    @GET("api/hr/departments")
    suspend fun departments(): List<Department>

    // --- Cài đặt tài khoản ---
    @POST("api/auth/change-password")
    suspend fun changePassword(@Body body: ChangePasswordBody): retrofit2.Response<Unit>

    @GET("api/auth/devices")
    suspend fun devices(): List<DeviceSession>

    @POST("api/auth/devices/{sid}/revoke")
    suspend fun revokeDevice(@Path("sid") sid: String): retrofit2.Response<Unit>

    @GET("api/auth/account-settings")
    suspend fun accountSettings(): AccountLoginSettings

    @PUT("api/auth/account-settings")
    suspend fun updateAccountSettings(@Body body: AccountLoginSettings): AccountLoginSettings

    // --- Thông báo đẩy (FCM) ---
    @POST("api/notifications/register-token")
    suspend fun registerPushToken(@Body body: RegisterTokenBody): retrofit2.Response<Unit>

    @POST("api/notifications/unregister-token")
    suspend fun unregisterPushToken(@Body body: PushTokenBody): retrofit2.Response<Unit>

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
}

@kotlinx.serialization.Serializable
data class DecisionBody(val comment: String = "")
