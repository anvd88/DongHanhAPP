package com.ketoanapk.hr

import android.os.Bundle
import com.getcapacitor.BridgeActivity

class MainActivity : BridgeActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        registerPlugin(ApkUpdaterPlugin::class.java)
        registerPlugin(AppNotificationPermissionPlugin::class.java)
        super.onCreate(savedInstanceState)
    }
}
