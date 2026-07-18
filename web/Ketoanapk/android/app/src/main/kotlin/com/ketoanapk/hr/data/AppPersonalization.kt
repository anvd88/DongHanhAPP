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
    var reverseHomeCards by mutableStateOf(false); private set
    var dataSaver by mutableStateOf(false); private set

    fun init(context:Context){if(prefs!=null)return;prefs=context.getSharedPreferences("personalization",Context.MODE_PRIVATE);themeMode=prefs!!.getString("theme","system")!!;fontScale=prefs!!.getFloat("font",1f);language=prefs!!.getString("language","vi")!!;reverseHomeCards=prefs!!.getBoolean("reverse_home",false);dataSaver=prefs!!.getBoolean("data_saver",false)}
    fun setTheme(v:String){themeMode=v;prefs?.edit()?.putString("theme",v)?.apply()}
    fun setFont(v:Float){fontScale=v.coerceIn(.85f,1.3f);prefs?.edit()?.putFloat("font",fontScale)?.apply()}
    fun updateLanguage(v:String){language=v;prefs?.edit()?.putString("language",v)?.apply();androidx.appcompat.app.AppCompatDelegate.setApplicationLocales(androidx.core.os.LocaleListCompat.forLanguageTags(v))}
    fun setReverseHome(v:Boolean){reverseHomeCards=v;prefs?.edit()?.putBoolean("reverse_home",v)?.apply()}
    fun updateDataSaver(v:Boolean){dataSaver=v;prefs?.edit()?.putBoolean("data_saver",v)?.apply()}
}
