package com.ketoanapk.hr.network

import com.ketoanapk.hr.BuildConfig
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
    }

    fun create(tokenStore: TokenStore): HrApi {
        val auth = Interceptor { chain ->
            val token = runBlocking { tokenStore.token() }
            val request = if (!token.isNullOrBlank()) {
                chain.request().newBuilder()
                    .addHeader("Authorization", "Bearer $token")
                    .build()
            } else {
                chain.request()
            }
            chain.proceed(request)
        }

        val logging = HttpLoggingInterceptor().apply {
            level = if (BuildConfig.DEBUG) HttpLoggingInterceptor.Level.BASIC
            else HttpLoggingInterceptor.Level.NONE
        }

        val client = OkHttpClient.Builder()
            .addInterceptor(auth)
            .addInterceptor(logging)
            .connectTimeout(20, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .build()

        val base = BuildConfig.API_BASE_URL.trimEnd('/') + "/"
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
