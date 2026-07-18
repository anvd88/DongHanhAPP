package com.ketoanapk.hr.data

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioManager
import android.media.Ringtone
import android.media.RingtoneManager
import android.media.ToneGenerator
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager

/**
 * Âm thanh cuộc gọi TRONG-ỨNG-DỤNG (khi app đang mở/tiền cảnh):
 *  • Chuông ĐẾN (ringtone hệ thống + rung lặp) khi có cuộc gọi đến VÀ app đang mở — vì lúc foreground
 *    ta KHÔNG bắn thông báo toàn màn hình (CallNotifier chỉ reo qua thông báo khi app ở NỀN), nên nếu
 *    không có lớp này thì máy nhận đang mở app sẽ "câm" — không biết có cuộc gọi.
 *  • Nhạc chờ (ringback "tút…tút") cho người GỌI khi máy bên kia đang đổ chuông.
 * Mọi hàm idempotent + dừng an toàn (bọc runCatching), gọi được từ mọi luồng.
 */
object CallSounds {
    private var ringtone: Ringtone? = null
    private var vibrator: Vibrator? = null
    private var ringback: ToneGenerator? = null

    @Synchronized
    fun startRingtone(context: Context) {
        if (ringtone?.isPlaying == true) return
        runCatching {
            val uri = RingtoneManager.getActualDefaultRingtoneUri(context, RingtoneManager.TYPE_RINGTONE)
                ?: RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE)
            val rt = RingtoneManager.getRingtone(context.applicationContext, uri)
            rt.audioAttributes = AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_NOTIFICATION_RINGTONE)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                .build()
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) rt.isLooping = true // <P: chuông kêu 1 lần
            rt.play()
            ringtone = rt
        }
        startVibration(context)
    }

    @Synchronized
    fun stopRingtone() {
        runCatching { ringtone?.stop() }
        ringtone = null
        runCatching { vibrator?.cancel() }
        vibrator = null
    }

    @Synchronized
    fun startRingback() {
        if (ringback != null) return
        runCatching {
            val tg = ToneGenerator(AudioManager.STREAM_VOICE_CALL, 70)
            // TONE_SUP_RINGTONE = nhạc chờ chuẩn "tút…tút", tự lặp cho tới khi stopTone().
            tg.startTone(ToneGenerator.TONE_SUP_RINGTONE)
            ringback = tg
        }
    }

    @Synchronized
    fun stopRingback() {
        runCatching { ringback?.stopTone() }
        runCatching { ringback?.release() }
        ringback = null
    }

    @Synchronized
    fun stopAll() {
        stopRingtone()
        stopRingback()
    }

    private fun startVibration(context: Context) {
        val vib = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            (context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as? VibratorManager)?.defaultVibrator
        } else {
            @Suppress("DEPRECATION")
            context.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
        } ?: return
        val pattern = longArrayOf(0, 700, 800, 700, 800) // rung–nghỉ–rung, lặp từ index 1
        runCatching {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vib.vibrate(VibrationEffect.createWaveform(pattern, 1))
            } else {
                @Suppress("DEPRECATION") vib.vibrate(pattern, 1)
            }
        }
        vibrator = vib
    }
}
