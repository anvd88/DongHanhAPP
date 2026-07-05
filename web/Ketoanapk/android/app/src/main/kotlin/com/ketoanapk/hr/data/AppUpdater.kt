package com.ketoanapk.hr.data

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import com.ketoanapk.hr.BuildConfig
import java.io.File

/**
 * Tiện ích cập nhật APK THUẦN NATIVE: đọc phiên bản đã cài, chuẩn bị file đích, và mở trình cài đặt
 * hệ thống qua FileProvider. Việc TẢI APK do [HrRepository.downloadRelease] đảm nhận (dùng chung
 * OkHttp/TLS + token với mọi request khác) để tránh lỗi mạng của HttpURLConnection tự dựng.
 */
object AppUpdater {

    fun installedVersionCode(context: Context): Int {
        val info = packageInfo(context) ?: return BuildConfig.VERSION_CODE
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            info.longVersionCode.toInt()
        } else {
            @Suppress("DEPRECATION") info.versionCode
        }
    }

    fun installedVersionName(context: Context): String =
        packageInfo(context)?.versionName ?: BuildConfig.VERSION_NAME

    /** Android O+ yêu cầu quyền "cài ứng dụng không rõ nguồn" cho từng app. */
    fun canInstallPackages(context: Context): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.O || context.packageManager.canRequestPackageInstalls()

    fun openUnknownSourcesSettings(context: Context) {
        val intent = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}"))
        } else {
            Intent(Settings.ACTION_SECURITY_SETTINGS)
        }.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        context.startActivity(intent)
    }

    /**
     * File đích trong cacheDir (đã khai báo <cache-path> trong file_paths.xml cho FileProvider).
     *
     * Dọn SẠCH mọi file .apk cũ còn sót trong cacheDir trước khi tải bản mới. Nếu không, mỗi lần
     * cập nhật tải về một file mang tên khác (tên do admin đặt, thường kèm phiên bản) nên các APK cũ
     * (mỗi cái vài chục MB) tích lại mãi → dung lượng ứng dụng phình dần sau nhiều lần cập nhật.
     */
    fun apkCacheFile(context: Context, fileName: String): File {
        purgeCachedApks(context)
        val safe = fileName.substringAfterLast('/').substringAfterLast('\\')
            .takeIf { it.endsWith(".apk", ignoreCase = true) } ?: "ketoan-hr-update.apk"
        return File(context.cacheDir, safe).also { if (it.exists()) it.delete() }
    }

    /** Xóa mọi APK cũ trong cacheDir (bản cập nhật lần trước đã cài xong, không còn cần giữ). */
    fun purgeCachedApks(context: Context) {
        runCatching {
            context.cacheDir.listFiles { f -> f.isFile && f.name.endsWith(".apk", ignoreCase = true) }
                ?.forEach { it.delete() }
        }
    }

    @Suppress("DEPRECATION")
    fun openInstaller(context: Context, apk: File) {
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", apk)
        val install = Intent(Intent.ACTION_INSTALL_PACKAGE).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            putExtra(Intent.EXTRA_NOT_UNKNOWN_SOURCE, true)
        }
        try {
            context.startActivity(install)
        } catch (_: ActivityNotFoundException) {
            context.startActivity(
                Intent(Intent.ACTION_VIEW).apply {
                    setDataAndType(uri, "application/vnd.android.package-archive")
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                },
            )
        }
    }

    private fun packageInfo(context: Context) = runCatching {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            context.packageManager.getPackageInfo(context.packageName, PackageManager.PackageInfoFlags.of(0))
        } else {
            @Suppress("DEPRECATION") context.packageManager.getPackageInfo(context.packageName, 0)
        }
    }.getOrNull()
}
