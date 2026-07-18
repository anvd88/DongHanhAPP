package com.ketoanapk.hr.data

import android.graphics.Matrix
import android.opengl.GLES11Ext
import android.opengl.GLES20
import android.os.Handler
import android.os.Looper
import android.util.Log
import org.webrtc.EglBase
import org.webrtc.GlShader
import org.webrtc.GlTextureFrameBuffer
import org.webrtc.GlUtil
import org.webrtc.RendererCommon
import org.webrtc.TextureBufferImpl
import org.webrtc.VideoFrame
import org.webrtc.VideoProcessor
import org.webrtc.VideoSink
import org.webrtc.YuvConverter
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Bộ xử lý khung hình video ĐẦU NGUỒN (sau camera, trước bộ mã hoá) áp LÀM MỊN DA + BỘ LỌC MÀU bằng GPU.
 * Vì đứng ở nguồn nên CẢ bên nhận LẪN khung tự xem đều nhận ảnh đã xử lý ⇒ người bên kia thấy đúng, media
 * vẫn P2P (server không gánh) + mã hoá đầu-cuối.
 *
 * QUAN TRỌNG (đã sửa crash): [onFrameCaptured] chạy TRÊN luồng của SurfaceTextureHelper, nơi context EGL
 * của camera ĐÃ SẴN current. Vì vậy KHÔNG tạo EGL context riêng, KHÔNG makeCurrent (tráo context trên
 * luồng này gây crash native) — chỉ render THẲNG vào context đang có. Texture xuất chia sẻ được với bộ
 * mã hoá/renderer (đều dùng context chia sẻ chung từ gốc).
 *
 * TỐI ƯU NHIỆT: không bật hiệu ứng → chuyển tiếp nguyên bản, KHÔNG chạm GPU. Bể 3 khung tái dùng.
 * FAIL-SAFE: mọi lỗi → gửi khung gốc, không làm hỏng cuộc gọi.
 */
class BeautyVideoProcessor(@Suppress("UNUSED_PARAMETER") sharedContext: EglBase.Context) : VideoProcessor {

    private var sink: VideoSink? = null

    private var shader: GlShader? = null
    private var yuvConverter: YuvConverter? = null
    private var handler: Handler? = null
    private var glReady = false
    private var glFailed = false

    private var uTexMatrix = 0
    private var uTexel = 0
    private var uBeauty = 0
    private var uFilter = 0
    private var uSampler = 0

    private var posBuf: java.nio.FloatBuffer? = null
    private var texBuf: java.nio.FloatBuffer? = null

    private val pool = arrayOfNulls<GlTextureFrameBuffer>(POOL)
    private val inUse = Array(POOL) { AtomicBoolean(false) }

    override fun setSink(sink: VideoSink?) { this.sink = sink }
    override fun onCapturerStarted(success: Boolean) {}
    override fun onCapturerStopped() { runCatching { releaseGl() } }

    override fun onFrameCaptured(frame: VideoFrame) {
        val out = sink ?: return
        val buffer = frame.buffer

        // Không bật hiệu ứng / khung không phải texture OES / GL từng lỗi → chuyển tiếp NGUYÊN BẢN.
        if (glFailed || !CallVideoEffects.hasEffect() || buffer !is VideoFrame.TextureBuffer ||
            buffer.type != VideoFrame.TextureBuffer.Type.OES
        ) {
            out.onFrame(frame)
            return
        }

        val processed = runCatching { process(buffer, frame) }.getOrElse {
            Log.w(TAG, "Xử lý hiệu ứng lỗi → gửi khung gốc", it)
            null
        }
        if (processed != null) {
            out.onFrame(processed)
            processed.release()
        } else {
            out.onFrame(frame) // fail-safe
        }
    }

    private fun process(buffer: VideoFrame.TextureBuffer, frame: VideoFrame): VideoFrame? {
        if (!ensureGl()) return null
        val prog = shader ?: return null
        val w = buffer.width
        val h = buffer.height

        val slot = acquireSlot() ?: return null // hiếm: tất cả khung xuất đang bận → bỏ hiệu ứng khung này
        val fb = pool[slot]!!

        fb.setSize(w, h)
        GLES20.glBindFramebuffer(GLES20.GL_FRAMEBUFFER, fb.frameBufferId)
        GLES20.glViewport(0, 0, w, h)

        prog.useProgram()
        GLES20.glActiveTexture(GLES20.GL_TEXTURE0)
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, buffer.textureId)
        GLES20.glUniform1i(uSampler, 0)

        val texMatrix = RendererCommon.convertMatrixFromAndroidGraphicsMatrix(buffer.transformMatrix)
        GLES20.glUniformMatrix4fv(uTexMatrix, 1, false, texMatrix, 0)
        GLES20.glUniform2f(uTexel, 1f / w, 1f / h)
        GLES20.glUniform1f(uBeauty, CallVideoEffects.beauty)
        GLES20.glUniform1i(uFilter, CallVideoEffects.filter)

        prog.setVertexAttribArray("aPos", 2, posBuf)
        prog.setVertexAttribArray("aTex", 2, texBuf)
        GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4)

        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, 0)
        GLES20.glBindFramebuffer(GLES20.GL_FRAMEBUFFER, 0)
        GLES20.glFinish() // đảm bảo render xong trước khi bộ mã hoá/renderer (context khác) đọc texture

        val outBuffer = TextureBufferImpl(
            w, h, VideoFrame.TextureBuffer.Type.RGB, fb.textureId, Matrix(),
            handler, yuvConverter,
        ) { inUse[slot].set(false) }
        return VideoFrame(outBuffer, frame.rotation, frame.timestampNs)
    }

    private fun acquireSlot(): Int? {
        for (i in 0 until POOL) if (inUse[i].compareAndSet(false, true)) return i
        return null
    }

    /**
     * Khởi tạo GL 1 lần — chạy khi context camera ĐANG current (trong onFrameCaptured). KHÔNG tạo/đổi
     * context để tránh crash native.
     */
    private fun ensureGl(): Boolean {
        if (glReady) return true
        if (glFailed) return false
        try {
            handler = Handler(Looper.myLooper() ?: Looper.getMainLooper())
            posBuf = GlUtil.createFloatBuffer(floatArrayOf(-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f))
            texBuf = GlUtil.createFloatBuffer(floatArrayOf(0f, 0f, 1f, 0f, 0f, 1f, 1f, 1f))
            yuvConverter = YuvConverter()
            shader = GlShader(VERTEX, FRAGMENT).also {
                it.useProgram()
                uSampler = it.getUniformLocation("uTex")
                uTexMatrix = it.getUniformLocation("uTexMatrix")
                uTexel = it.getUniformLocation("uTexel")
                uBeauty = it.getUniformLocation("uBeauty")
                uFilter = it.getUniformLocation("uFilter")
            }
            for (i in 0 until POOL) pool[i] = GlTextureFrameBuffer(GLES20.GL_RGBA)
            glReady = true
            return true
        } catch (t: Throwable) {
            Log.w(TAG, "Khởi tạo GL hiệu ứng thất bại → tắt hiệu ứng, gọi bình thường", t)
            glFailed = true
            runCatching { releaseGl() }
            return false
        }
    }

    private fun releaseGl() {
        runCatching { shader?.release() }
        pool.forEach { runCatching { it?.release() } }
        runCatching { yuvConverter?.release() }
        shader = null; yuvConverter = null; glReady = false
    }

    companion object {
        private const val TAG = "BeautyVideoProcessor"
        private const val POOL = 3

        private const val VERTEX = """
            attribute vec4 aPos;
            attribute vec4 aTex;
            uniform mat4 uTexMatrix;
            varying vec2 vTex;
            void main() {
                gl_Position = aPos;
                vTex = (uTexMatrix * aTex).xy;
            }
        """

        private const val FRAGMENT = """
            #extension GL_OES_EGL_image_external : require
            precision mediump float;
            uniform samplerExternalOES uTex;
            uniform vec2 uTexel;
            uniform float uBeauty;
            uniform int uFilter;
            varying vec2 vTex;
            void main() {
                vec3 c = texture2D(uTex, vTex).rgb;
                if (uBeauty > 0.001) {
                    vec3 sum = c; float wsum = 1.0;
                    float radius = 2.0 + uBeauty * 2.0;
                    for (int i = 0; i < 8; i++) {
                        float a = float(i) * 0.7853981634;
                        vec2 off = vec2(cos(a), sin(a)) * uTexel * radius;
                        vec3 n = texture2D(uTex, vTex + off).rgb;
                        float d = dot(n - c, n - c);
                        float w = exp(-d * 28.0);
                        sum += n * w; wsum += w;
                    }
                    vec3 sm = sum / wsum;
                    sm = sm * 1.04 + 0.015;
                    c = mix(c, sm, clamp(uBeauty, 0.0, 1.0));
                }
                if (uFilter == 1) { c.r *= 1.10; c.g *= 1.02; c.b *= 0.90; c += 0.02; }
                else if (uFilter == 2) { c.b *= 1.12; c.g *= 1.02; c.r *= 0.92; }
                else if (uFilter == 3) { c.r *= 1.10; c.b *= 1.04; c.g *= 0.97; c += vec3(0.02, 0.0, 0.01); }
                else if (uFilter == 4) { float g = dot(c, vec3(0.299, 0.587, 0.114)); c = mix(vec3(g), c, 0.2) * vec3(1.06, 1.0, 0.92); }
                else if (uFilter == 5) { c = c * 0.9 + 0.06; c.b *= 1.06; c.r *= 1.02; }
                gl_FragColor = vec4(clamp(c, 0.0, 1.0), 1.0);
            }
        """
    }
}
