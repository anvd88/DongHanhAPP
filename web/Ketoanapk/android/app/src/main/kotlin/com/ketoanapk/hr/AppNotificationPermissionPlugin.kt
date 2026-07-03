package com.ketoanapk.hr

import android.Manifest
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.app.NotificationManagerCompat
import com.getcapacitor.JSObject
import com.getcapacitor.PermissionState
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin
import com.getcapacitor.annotation.Permission
import com.getcapacitor.annotation.PermissionCallback
import java.util.Locale

@CapacitorPlugin(
    name = "AppNotificationPermission",
    permissions = [Permission(strings = [Manifest.permission.POST_NOTIFICATIONS], alias = "notifications")],
)
class AppNotificationPermissionPlugin : Plugin() {
    @PluginMethod
    fun check(call: PluginCall) {
        resolveNotificationState(call)
    }

    @PluginMethod
    fun request(call: PluginCall) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU || runtimePermissionState() == PermissionState.GRANTED) {
            resolveNotificationState(call)
            return
        }

        requestPermissionForAlias("notifications", call, "notificationPermissionCallback")
    }

    @PermissionCallback
    fun notificationPermissionCallback(call: PluginCall) {
        resolveNotificationState(call)
    }

    @PluginMethod
    fun openSettings(call: PluginCall) {
        val currentActivity = activity
        val packageName = currentActivity.packageName
        val intent = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).apply {
                putExtra(Settings.EXTRA_APP_PACKAGE, packageName)
            }
        } else {
            Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:$packageName"))
        }
        currentActivity.startActivity(intent)
        call.resolve()
    }

    private fun resolveNotificationState(call: PluginCall) {
        val permissionState = runtimePermissionState()
        val systemEnabled = NotificationManagerCompat.from(context).areNotificationsEnabled()
        val granted = permissionState == PermissionState.GRANTED && systemEnabled

        call.resolve(
            JSObject()
                .put("granted", granted)
                .put("systemEnabled", systemEnabled)
                .put("permission", permissionState.toString().lowercase(Locale.US).replace("_", "-"))
                .put("supported", true),
        )
    }

    private fun runtimePermissionState(): PermissionState {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return PermissionState.GRANTED
        return getPermissionState("notifications") ?: PermissionState.PROMPT
    }
}
