package com.ketoanapk.hr

import java.net.URI
import java.net.URLDecoder

/** Phân tích deep-link từ web mà không phụ thuộc Android SDK để có thể kiểm thử bằng JVM thuần. */
internal object AppDeepLink {
    private const val Scheme = "ketoanhr"
    private const val QrLoginHost = "qr-login"
    private const val MobileAppLoginHost = "app-login"
    private const val MobileAppLoginPrefix = "ketoanmini-app-login:"
    private const val MaxQrValueLength = 4_096

    fun qrLoginCode(value: String?): String? {
        return parameter(value, QrLoginHost, "code")
    }

    fun mobileAppLoginRequest(
        value: String?,
        requestExtra: String? = null,
        clientModeExtra: String? = null,
    ): String? {
        val request = parameter(value, MobileAppLoginHost, "request")
        val mode = parameter(value, MobileAppLoginHost, "client_mode")
        normalizeMobileAppRequest(request, mode)?.let { return it }
        return normalizeMobileAppRequest(requestExtra, clientModeExtra)
    }

    private fun normalizeMobileAppRequest(request: String?, mode: String?): String? {
        val normalized = request?.trim().orEmpty()
        return normalized.takeIf {
            mode == "mobile_app" &&
                it.startsWith(MobileAppLoginPrefix) &&
                it.length <= MaxQrValueLength
        }
    }

    private fun parameter(value: String?, host: String, name: String): String? {
        if (value.isNullOrBlank() || value.length > MaxQrValueLength * 2) return null
        val uri = runCatching { URI(value) }.getOrNull() ?: return null
        if (!uri.scheme.equals(Scheme, ignoreCase = true) ||
            !uri.host.equals(host, ignoreCase = true)) return null

        val encoded = uri.rawQuery
            ?.split('&')
            ?.firstOrNull { it.substringBefore('=') == name }
            ?.substringAfter('=', missingDelimiterValue = "")
            ?: return null
        val decoded = runCatching {
            // Charset overload is available below API 33; the Charset overload would crash on API 29–32.
            URLDecoder.decode(encoded, "UTF-8").trim()
        }.getOrNull() ?: return null
        return decoded.takeIf { it.isNotEmpty() && it.length <= MaxQrValueLength }
    }
}
