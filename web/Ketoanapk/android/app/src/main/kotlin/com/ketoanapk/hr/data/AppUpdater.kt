package com.ketoanapk.hr.data

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import android.widget.Toast
import androidx.core.content.FileProvider
import androidx.core.content.IntentCompat
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

    /** Action nội bộ để nhận kết quả cài từ PackageInstaller (khớp receiver khai báo trong Manifest). */
    const val ACTION_INSTALL_STATUS = "com.ketoanapk.hr.INSTALL_STATUS"

    /**
     * Cài bản cập nhật. Ưu tiên **trình cài đặt hệ thống qua ACTION_VIEW** (tương thích rộng nhất, LUÔN hiện
     * màn xác nhận "Cài đặt bản cập nhật?"). Đây là cách ổn định nhất cho app tải ngoài Play.
     *
     * Trước đây ưu tiên PackageInstaller Session API kèm USER_ACTION_NOT_REQUIRED → trên nhiều máy hệ thống
     * TỪ CHỐI cài im lặng và huỷ phiên (STATUS_FAILURE_ABORTED) nên bấm "Cập nhật ngay" mà không có gì xảy ra.
     * Nay Session API chỉ còn là phương án dự phòng cuối (và KHÔNG xin cài im lặng nữa).
     */
    fun openInstaller(context: Context, apk: File) {
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", apk)
        // 1) ACTION_VIEW — cách mở trình cài đặt tương thích nhất.
        val view = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        if (runCatching { context.startActivity(view) }.isSuccess) return
        // 2) Trình cài đặt cũ ACTION_INSTALL_PACKAGE (một số máy đời cũ).
        if (runCatching { legacyInstall(context, apk) }.isSuccess) return
        // 3) Dự phòng cuối: PackageInstaller Session API.
        runCatching { installWithSession(context, apk) }
    }

    private fun installWithSession(context: Context, apk: File) {
        val installer = context.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(context.packageName)
        }

        val sessionId = installer.createSession(params)
        installer.openSession(sessionId).use { session ->
            apk.inputStream().use { input ->
                session.openWrite("base.apk", 0, apk.length()).use { out ->
                    input.copyTo(out)
                    session.fsync(out)
                }
            }
            val statusIntent = Intent(ACTION_INSTALL_STATUS).setPackage(context.packageName)
            // FLAG_MUTABLE: hệ thống cần chèn EXTRA_INTENT (màn xác nhận) vào broadcast trả về.
            val piFlags = PendingIntent.FLAG_UPDATE_CURRENT or
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0
            val pending = PendingIntent.getBroadcast(context, sessionId, statusIntent, piFlags)
            session.commit(pending.intentSender)
        }
    }

    /** Trình cài đặt hệ thống cũ ACTION_INSTALL_PACKAGE (máy đời cũ) — ném lỗi để [openInstaller] lùi bước tiếp. */
    @Suppress("DEPRECATION")
    private fun legacyInstall(context: Context, apk: File) {
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", apk)
        val install = Intent(Intent.ACTION_INSTALL_PACKAGE).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            putExtra(Intent.EXTRA_NOT_UNKNOWN_SOURCE, true)
        }
        context.startActivity(install)
    }

    private fun packageInfo(context: Context) = runCatching {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            context.packageManager.getPackageInfo(context.packageName, PackageManager.PackageInfoFlags.of(0))
        } else {
            @Suppress("DEPRECATION") context.packageManager.getPackageInfo(context.packageName, 0)
        }
    }.getOrNull()
}

/**
 * Nhận kết quả cài từ PackageInstaller (Session API). Khi hệ thống cần người dùng xác nhận thì mở màn
 * xác nhận cài; khi thất bại thì báo ngắn. Thành công thì không cần làm gì (app sẽ được thay bản mới).
 * Khai báo trong Manifest với action [AppUpdater.ACTION_INSTALL_STATUS].
 */
class InstallResultReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                val confirm = IntentCompat.getParcelableExtra(intent, Intent.EXTRA_INTENT, Intent::class.java)
                confirm?.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                runCatching { if (confirm != null) context.startActivity(confirm) }
            }
            PackageInstaller.STATUS_SUCCESS -> Unit // đã cài xong bản mới
            PackageInstaller.STATUS_FAILURE_ABORTED -> Unit // người dùng hủy — im lặng
            else -> {
                val msg = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE)
                runCatching {
                    Toast.makeText(context, "Cài đặt bản cập nhật thất bại${if (msg.isNullOrBlank()) "" else ": $msg"}.", Toast.LENGTH_LONG).show()
                }
            }
        }
    }
}
