package com.ketoanapk.hr.data

import android.content.Context
import android.media.MediaRecorder
import android.os.Build
import java.io.File
import kotlin.math.log10

/** Bộ ghi âm ngắn cho chat; chỉ được khởi động sau thao tác người dùng và sau khi quyền micro đã cấp. */
class ChatAudioRecorder(private val context: Context) {
    private var recorder: MediaRecorder? = null
    private var output: File? = null
    private var startedAt = 0L

    val isRecording: Boolean get() = recorder != null

    /** Số mili giây đã ghi; 0 khi không ghi. Phải ĐỌC TRƯỚC [stop] vì stop xoá mốc bắt đầu. */
    fun elapsedMs(): Long = if (recorder == null) 0L else System.currentTimeMillis() - startedAt

    /**
     * Biên độ đỉnh kể từ lần gọi trước, quy về 0..1 để vẽ sóng âm.
     *
     * MediaRecorder trả biên độ TUYẾN TÍNH (0..32767) nhưng tai người nghe theo thang loga: giọng nói
     * bình thường chỉ quanh 1/10 thang tuyến tính, vẽ thẳng ra thì cột sóng nằm bẹp dưới đáy suốt. Quy về
     * decibel rồi trải dải -50 dB..0 dB thành 0..1 mới ra sóng nhấp nhô đúng nhịp nói.
     */
    fun amplitude(): Float {
        // getMaxAmplitude() ném IllegalStateException nếu recorder đã dừng — bắt lại, không để rơi ra
        // vòng lặp vẽ sóng.
        val peak = runCatching { recorder?.maxAmplitude ?: 0 }.getOrDefault(0)
        if (peak <= 0) return 0f
        val db = 20.0 * log10(peak / 32_767.0)
        return ((db + 50.0) / 50.0).coerceIn(0.0, 1.0).toFloat()
    }

    fun start(): Boolean {
        if (recorder != null) return false
        val dir = File(context.cacheDir, "chat").apply { mkdirs() }
        // Opus có từ Android 10; đây là codec thoại của Telegram/WhatsApp/Signal, ở 16 kbps nghe rõ hơn
        // AAC cùng mức vì nó được thiết kế riêng cho giọng nói. Máy cũ hơn rơi về AAC 24 kbps.
        val opus = Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q
        val file = File(dir, "ghi-am-${System.currentTimeMillis()}.${if (opus) "ogg" else "m4a"}")
        return runCatching {
            val next = newRecorder()
            next.setAudioSource(MediaRecorder.AudioSource.MIC)
            if (opus) {
                next.setOutputFormat(MediaRecorder.OutputFormat.OGG)
                next.setAudioEncoder(MediaRecorder.AudioEncoder.OPUS)
                next.setAudioEncodingBitRate(16_000)
            } else {
                next.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
                next.setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
                next.setAudioEncodingBitRate(24_000)
            }
            // Giọng người nằm trong 300–3.400 Hz nên 16 kHz thừa sức tái tạo; 44,1 kHz (mức cũ) là tần số
            // cho NHẠC, tức trả dung lượng cho dải tần không ai nghe thấy. Mono: thoại không cần stereo.
            next.setAudioSamplingRate(16_000)
            next.setAudioChannels(1)
            next.setOutputFile(file.absolutePath)
            next.prepare()
            next.start()
            recorder = next
            output = file
            startedAt = System.currentTimeMillis()
            true
        }.getOrElse { file.delete(); false }
    }

    fun stop(): File? {
        val active = recorder ?: return null
        val file = output
        recorder = null
        output = null
        startedAt = 0L
        val ok = runCatching { active.stop() }.isSuccess
        runCatching { active.release() }
        if (!ok) file?.delete()
        return file?.takeIf { ok && it.exists() && it.length() > 0 }
    }

    fun cancel() { stop()?.delete() }

    @Suppress("DEPRECATION")
    private fun newRecorder(): MediaRecorder =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) MediaRecorder(context) else MediaRecorder()
}
