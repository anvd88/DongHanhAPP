package com.ketoanmini.hr

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import com.getcapacitor.JSObject
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

@CapacitorPlugin(name = "ApkUpdater")
class ApkUpdaterPlugin : Plugin() {
    @PluginMethod
    fun install(call: PluginCall) {
        val downloadUrl = call.getString("url")
        if (downloadUrl.isNullOrBlank()) {
            call.reject("Thiếu đường dẫn tải APK.")
            return
        }

        val currentActivity = activity
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && !currentActivity.packageManager.canRequestPackageInstalls()) {
            openUnknownSourcesSettings()
            call.reject(
                "Android cần bạn cho phép Ketoan cài ứng dụng không rõ nguồn, sau đó bấm cập nhật lại.",
                "ALLOW_UNKNOWN_SOURCES_REQUIRED",
            )
            return
        }

        val token = call.getString("token") ?: ""
        val requestedName = call.getString("fileName") ?: "ketoan-hr-update.apk"
        val fileName = requestedName.substringAfterLast('/').substringAfterLast('\\').ifBlank { "ketoan-hr-update.apk" }

        Thread {
            try {
                val targetFile = File(currentActivity.cacheDir, fileName)
                if (targetFile.exists()) targetFile.delete()

                val connection = (URL(downloadUrl).openConnection() as HttpURLConnection).apply {
                    requestMethod = "GET"
                    connectTimeout = 15000
                    readTimeout = 120000
                    if (token.isNotBlank()) setRequestProperty("Authorization", "Bearer $token")
                }

                try {
                    val status = connection.responseCode
                    if (status !in 200..299) error("Backend trả lỗi $status khi tải APK.")

                    connection.inputStream.use { input ->
                        targetFile.outputStream().use { output ->
                            input.copyTo(output)
                        }
                    }
                } finally {
                    connection.disconnect()
                }

                currentActivity.runOnUiThread {
                    val apkUri = FileProvider.getUriForFile(
                        currentActivity,
                        "${currentActivity.packageName}.fileprovider",
                        targetFile,
                    )
                    val intent = Intent(Intent.ACTION_VIEW).apply {
                        setDataAndType(apkUri, "application/vnd.android.package-archive")
                        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                    }
                    currentActivity.startActivity(intent)
                    call.resolve(JSObject().put("started", true))
                }
            } catch (e: Exception) {
                currentActivity.runOnUiThread {
                    call.reject(e.message ?: "Không tải/cài được APK.", e)
                }
            }
        }.start()
    }

    @PluginMethod
    fun openInstallSettings(call: PluginCall) {
        openUnknownSourcesSettings()
        call.resolve()
    }

    private fun openUnknownSourcesSettings() {
        val currentActivity = activity
        val intent = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${currentActivity.packageName}"))
        } else {
            Intent(Settings.ACTION_SECURITY_SETTINGS)
        }
        currentActivity.startActivity(intent)
    }
}
