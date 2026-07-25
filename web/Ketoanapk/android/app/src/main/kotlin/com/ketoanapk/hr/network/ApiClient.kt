package com.ketoanapk.hr.network

import com.ketoanapk.hr.BuildConfig
import com.ketoanapk.hr.data.ServerClock
import com.ketoanapk.hr.data.TokenStore
import com.jakewharton.retrofit2.converter.kotlinx.serialization.asConverterFactory
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import okhttp3.Interceptor
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.HttpException
import retrofit2.Retrofit
import java.util.concurrent.TimeUnit

/** Lỗi API đã được chuẩn hoá kèm thông điệp thân thiện (thường lấy từ trường "message" của backend). */
class ApiException(message: String) : Exception(message)

object ApiClient {
    val json = Json {
        ignoreUnknownKeys = true
        coerceInputValues = true
        explicitNulls = false
        isLenient = true
        // Luôn ghi cả trường trùng giá trị mặc định khi GỬI request. Vì mọi DTO đều đặt default
        // (để giải mã an toàn), nếu tắt cờ này thì giá trị đúng-bằng-default sẽ bị bỏ khỏi JSON:
        // vd gạt "Bật đăng nhập web" = true (đúng default) → body rỗng → backend nhận false → bật không được.
        encodeDefaults = true
    }

    /**
     * Header đánh dấu request do máy tự bắn ở NỀN (WorkManager poll), không phải người dùng đang
     * dùng app. Máy chủ không tính các request này là "hoạt động" nên phiên vẫn hết hạn sau
     * [com.ketoanapk.hr.data.SESSION_IDLE_DAYS] ngày người dùng không mở app. Xem Program.cs.
     */
    const val BACKGROUND_HEADER = "X-Background-Poll"

    /** API chính (công khai qua Cloudflare Tunnel). Dùng cho MỌI tính năng TRỪ chấm công. */
    fun create(tokenStore: TokenStore, background: Boolean = false): HrApi =
        build(BuildConfig.API_BASE_URL, tokenStore, background)

    /**
     * API chấm công — LUÔN đi thẳng máy chủ trong LAN ([BuildConfig.ATTENDANCE_BASE_URL]), KHÔNG qua
     * Internet/Cloudflare. Bắt buộc thiết bị cùng mạng LAN với máy chủ mới chấm được (chống gian lận
     * "chấm từ xa" + không đẩy ảnh khuôn mặt ra ngoài). Token JWT dùng chung vì cùng một backend.
     */
    fun createAttendance(tokenStore: TokenStore): HrApi = build(BuildConfig.ATTENDANCE_BASE_URL, tokenStore)

    private fun build(baseUrl: String, tokenStore: TokenStore, background: Boolean = false): HrApi {
        val auth = Interceptor { chain ->
            val token = runBlocking { tokenStore.token() }
            val builder = chain.request().newBuilder()
            if (!token.isNullOrBlank()) builder.addHeader("Authorization", "Bearer $token")
            if (background) builder.addHeader(BACKGROUND_HEADER, "1")
            chain.proceed(builder.build())
        }

        val logging = HttpLoggingInterceptor().apply {
            level = if (BuildConfig.DEBUG) HttpLoggingInterceptor.Level.BASIC
            else HttpLoggingInterceptor.Level.NONE
        }

        // Đồng bộ đồng hồ máy chủ từ header "Date" của mọi phản hồi → giờ hiển thị (lời chào theo buổi…)
        // luôn theo máy chủ, không lệ thuộc đồng hồ máy có thể bị chỉnh sai.
        val serverClock = Interceptor { chain ->
            val response = chain.proceed(chain.request())
            response.headers.getDate("Date")?.let { ServerClock.sync(it.time) }
            response
        }

        val client = OkHttpClient.Builder()
            .addInterceptor(auth)
            .addInterceptor(serverClock)
            .addInterceptor(logging)
            .connectTimeout(20, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .build()

        val base = baseUrl.trimEnd('/') + "/"
        return Retrofit.Builder()
            .baseUrl(base)
            .client(client)
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()
            .create(HrApi::class.java)
    }
}

/** Đọc thông điệp lỗi "message" trong thân phản hồi lỗi (nếu có). */
fun HttpException.friendlyMessage(): String {
    val raw = runCatching { response()?.errorBody()?.string() }.getOrNull()
    if (!raw.isNullOrBlank()) {
        val parsed = runCatching {
            ApiClient.json.parseToJsonElement(raw)
        }.getOrNull()
        val message = runCatching {
            (parsed as? kotlinx.serialization.json.JsonObject)?.get("message")
                ?.let { (it as? kotlinx.serialization.json.JsonPrimitive)?.content }
        }.getOrNull()
        if (!message.isNullOrBlank()) return message
    }
    return when (code()) {
        401 -> "Phiên đăng nhập đã hết hạn."
        403 -> "Bạn không có quyền truy cập mục này."
        else -> "Máy chủ trả về lỗi (${code()})."
    }
}
