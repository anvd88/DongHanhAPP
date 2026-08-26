package com.ketoanapk.hr.data

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

object AppPersonalization {
    private var prefs: android.content.SharedPreferences? = null
    var themeMode by mutableStateOf("system"); private set
    var fontScale by mutableStateOf(1f); private set
    var language by mutableStateOf("vi"); private set
    /** Thứ tự tác vụ trên Trang chủ. Lưu bằng tên enum để không phụ thuộc giao diện/ngôn ngữ. */
    var homeActionOrder by mutableStateOf<List<String>>(emptyList()); private set
    var dataSaver by mutableStateOf(false); private set

    fun init(context: Context) {
        if (prefs != null) return
        prefs = context.getSharedPreferences("personalization", Context.MODE_PRIVATE)
        themeMode = prefs!!.getString("theme", "system")!!
        fontScale = prefs!!.getFloat("font", 1f)
        language = prefs!!.getString("language", "vi")!!
        homeActionOrder = prefs!!.getString("home_action_order", "")
            .orEmpty()
            .split(',')
            .map(String::trim)
            .filter(String::isNotEmpty)
            .distinct()
        dataSaver = prefs!!.getBoolean("data_saver", false)
    }
    fun setTheme(v:String){themeMode=v;prefs?.edit()?.putString("theme",v)?.apply()}
    fun setFont(v:Float){fontScale=v.coerceIn(.85f,1.3f);prefs?.edit()?.putFloat("font",fontScale)?.apply()}
    fun updateLanguage(v:String){language=v;prefs?.edit()?.putString("language",v)?.apply();androidx.appcompat.app.AppCompatDelegate.setApplicationLocales(androidx.core.os.LocaleListCompat.forLanguageTags(v))}
    fun updateHomeActionOrder(value: List<String>) {
        homeActionOrder = value.map(String::trim).filter(String::isNotEmpty).distinct()
        prefs?.edit()?.putString("home_action_order", homeActionOrder.joinToString(","))?.apply()
    }
    fun updateDataSaver(v:Boolean){dataSaver=v;prefs?.edit()?.putBoolean("data_saver",v)?.apply()}
}
