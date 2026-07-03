package com.ketoanapk.hr

import android.content.ActivityNotFoundException
import android.content.Intent
import android.content.pm.PackageManager
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
import java.security.MessageDigest
import java.util.Locale

@CapacitorPlugin(name = "ApkUpdater")
class ApkUpdaterPlugin : Plugin() {
    @PluginMethod
    fun getInstalledVersion(call: PluginCall) {
        try {
            val currentActivity = activity
            val packageInfo = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                currentActivity.packageManager.getPackageInfo(
                    currentActivity.packageName,
                    PackageManager.PackageInfoFlags.of(0),
                )
            } else {
                @Suppress("DEPRECATION")
                currentActivity.packageManager.getPackageInfo(currentActivity.packageName, 0)
            }
            val versionCode = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                packageInfo.longVersionCode
            } else {
                @Suppress("DEPRECATION")
                packageInfo.versionCode.toLong()
            }

            call.resolve(
                JSObject()
                    .put("versionCode", versionCode)
                    .put("versionName", packageInfo.versionName ?: ""),
            )
        } catch (e: Exception) {
            call.reject(e.message ?: "Khong doc duoc phien ban ung dung.", e)
        }
    }

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
        val expectedSha256 = (call.getString("sha256") ?: "").lowercase(Locale.US)
        val expectedSize = call.getInt("size") ?: 0

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

                    val digest = MessageDigest.getInstance("SHA-256")
                    var bytesWritten = 0L
                    connection.inputStream.use { input ->
                        targetFile.outputStream().use { output ->
                            val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
                            while (true) {
                                val read = input.read(buffer)
                                if (read < 0) break
                                if (read == 0) continue
                                digest.update(buffer, 0, read)
                                output.write(buffer, 0, read)
                                bytesWritten += read.toLong()
                            }
                        }
                    }
                    if (expectedSize > 0 && bytesWritten != expectedSize.toLong()) {
                        targetFile.delete()
                        error("File APK tai ve khong du dung luong.")
                    }
                    if (expectedSha256.isNotBlank()) {
                        val actualSha256 = digest.digest().joinToString("") { "%02x".format(it.toInt() and 0xff) }
                        if (actualSha256 != expectedSha256) {
                            targetFile.delete()
                            error("File APK tai ve khong khop ma kiem tra SHA-256.")
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
                    openPackageInstaller(apkUri)
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

    @Suppress("DEPRECATION")
    private fun openPackageInstaller(apkUri: Uri) {
        val currentActivity = activity
        val installIntent = Intent(Intent.ACTION_INSTALL_PACKAGE).apply {
            setDataAndType(apkUri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            putExtra(Intent.EXTRA_NOT_UNKNOWN_SOURCE, true)
            putExtra(Intent.EXTRA_RETURN_RESULT, true)
        }

        try {
            currentActivity.startActivity(installIntent)
        } catch (_: ActivityNotFoundException) {
            val fallbackIntent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(apkUri, "application/vnd.android.package-archive")
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            currentActivity.startActivity(fallbackIntent)
        }
    }
}
