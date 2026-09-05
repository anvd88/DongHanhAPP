package com.ketoanapk.hr.data

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/**
 * Nhận thao tác "Từ chối" từ thông báo cuộc gọi đến (không cần mở app). Cúp máy ngay: [CallManager]
 * gửi tín hiệu từ chối (xếp hàng gửi bù nếu chưa nối lại hub) và tắt thông báo đổ chuông.
 */
class CallActionReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            ACTION_DECLINE -> {
                CallManager.init(context.applicationContext)
                CallManager.hangup("declined")
                CallNotifier.dismiss(context)
            }
        }
    }

    companion object {
        const val ACTION_DECLINE = "com.ketoanapk.hr.CALL_DECLINE"
    }
}
