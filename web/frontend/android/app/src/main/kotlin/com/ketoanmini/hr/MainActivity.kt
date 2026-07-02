package com.ketoanmini.hr

import android.os.Bundle
import com.getcapacitor.BridgeActivity

class MainActivity : BridgeActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        registerPlugin(ApkUpdaterPlugin::class.java)
        super.onCreate(savedInstanceState)
    }
}
